using Basalt;
using Basalt.Gpu.Source2;
using Sandbox.Rendering;
using ViewConstants = Basalt.Gpu.ViewConstants;

namespace Sandbox.BasaltLab;

/// <summary>
/// <c>-post-parity folder [-case a,b] [-tolerance N] [-validation]</c>: renders the Sun Shadows preset through Basalt on
/// Source 2's device, then runs the engine's post processing over that HDR frame twice - on Source 2's device and on a
/// Basalt.Gpu device through the engine's own <see cref="BasaltGpuRenderBackend"/> - and compares the pixels. Each case
/// records the command list its post process component records (<c>BasePostProcess.Blit</c>, a frame grab, and
/// <c>Bloom</c>'s compute chain), into a copy of the frame. Each case writes <c>native.png</c>, <c>basalt.png</c> and
/// <c>diff.png</c> (differences x16, after encoding to 8-bit sRGB) to a folder of its own, and the folder gets
/// <c>results.md</c>. Exits 1 when a case differs by more than <c>-tolerance</c> (default 2), threw, or validation
/// reported errors.
/// <para>
/// Stage 3 of moving the engine's rendering onto Basalt.Gpu (docs/managed/basalt-gpu-path.md). Auto exposure isn't here:
/// it's native's (<c>RenderTools.RenderAutoExposure</c>) and has no Basalt.Gpu form yet, so tonemapping runs with its
/// default exposure on both devices.
/// </para>
/// </summary>
internal static class PostParity
{
	const int Width = 640, Height = 360;

	/// <summary>
	/// A case: what a component would record, with its values on the command list's attributes.
	/// </summary>
	sealed record Case( string Name, string Covers, Action<CommandList> Record, string NotCompared = null );

	static Material Shader( string path ) => Material.FromShader( path );

	/// <summary>
	/// As <c>BasePostProcess.Blit</c>: grab the frame when it wants the backbuffer, then blit. The component's values are
	/// on the command list's attributes here, where a block without a view finds them with the grab on both devices.
	/// </summary>
	static void Blit( CommandList cl, Material material, bool backbuffer, bool mips = false )
	{
		if ( backbuffer ) cl.Attributes.GrabFrameTexture( "ColorBuffer", mips );
		cl.Blit( material );
	}

	static void Vignette( CommandList cl )
	{
		var a = cl.Attributes;
		a.Set( "color", new Vector4( 0, 0, 0, 1 ) );
		a.Set( "intensity", 1.0f );
		a.Set( "smoothness", 1.0f );
		a.Set( "roundness", 1.0f );
		a.Set( "center", new Vector2( 0.5f, 0.5f ) );
		Blit( cl, Shader( "shaders/postprocess/pp_vignette.shader" ), backbuffer: false );
	}

	static void ColorAdjustments( CommandList cl )
	{
		var a = cl.Attributes;
		a.Set( "blend", 1.0f );
		a.Set( "saturate", 0.6f );
		a.Set( "hue_rotate", 30.0f );
		a.Set( "brightness", 1.1f );
		a.Set( "contrast", 1.2f );
		Blit( cl, Shader( "shaders/postprocess/pp_color.shader" ), backbuffer: true );
	}

	static void FilmGrain( CommandList cl )
	{
		var a = cl.Attributes;
		a.Set( "intensity", 0.1f );
		a.Set( "response", 0.5f );

		// What a view binds every shader (Basalt's ViewEnvironment, native's view setup)
		a.Set( "BlueNoise", Texture.Load( "textures/dev/blue_noise_256.vtex" ) );
		Blit( cl, Shader( "shaders/postprocess/pp_filmgrain.shader" ), backbuffer: true );
	}

	static void ChromaticAberration( CommandList cl )
	{
		var a = cl.Attributes;
		a.Set( "scale", 0.33f );
		a.Set( "amount", new Vector3( 6f, 2f, 4f ) / 1000.0f );
		Blit( cl, Shader( "shaders/postprocess/pp_chromaticaberration.shader" ), backbuffer: true );
	}

	static void Sharpen( CommandList cl )
	{
		var a = cl.Attributes;
		a.Set( "strength", 2.0f );
		a.Set( "size", 1.0f );
		Blit( cl, Shader( "shaders/postprocess/pp_sharpen.shader" ), backbuffer: true );
	}

	static void Pixelate( CommandList cl )
	{
		var a = cl.Attributes;
		a.Set( "scale", 0.25f );
		Blit( cl, Shader( "shaders/postprocess/pp_pixelate.shader" ), backbuffer: true, mips: true );
	}

	static void Blur( CommandList cl )
	{
		var a = cl.Attributes;
		a.Set( "size", 1.0f );
		Blit( cl, Shader( "shaders/postprocess/pp_blur.shader" ), backbuffer: true, mips: true );
	}

	static void Tonemapping( CommandList cl )
	{
		var a = cl.Attributes;
		a.SetCombo( "D_TONEMAPPING", global::Sandbox.Tonemapping.TonemappingMode.HableFilmic );
		a.SetCombo( "D_EXPOSUREMETHOD", global::Sandbox.Tonemapping.ExposureColorSpaceEnum.RGB );
		Blit( cl, Shader( "shaders/tonemapping/tonemapping.shader" ), backbuffer: true );
	}

	static readonly string[] BloomMips = ["BloomMip0", "BloomMip1", "BloomMip2", "BloomMip3", "BloomMip4", "BloomMip5"];
	static ComputeShader bloomCs;

	/// <summary>As <c>Bloom.Render</c>: a compute chain over named targets, then a composite.</summary>
	static void Bloom( CommandList command )
	{
		bloomCs ??= new ComputeShader( "postprocess/postprocess_bloom_cs" );
		var colorHandle = command.Attributes.GrabFrameTexture( "ColorBuffer" );
		for ( int i = 0; i < BloomMips.Length; i++ )
			command.GetRenderTarget( BloomMips[i], 2 << i, ImageFormat.RGBA16161616F, ImageFormat.None );

		static RenderTargetHandle Mip( int i ) => new() { Name = BloomMips[i] };

		command.Attributes.Set( "Threshold", 0.5f );
		command.Attributes.SetCombo( "D_STAGE", 0 );
		command.Attributes.Set( "InputTexture", colorHandle.ColorTexture );
		command.Attributes.Set( "BloomOut", Mip( 0 ).ColorTexture );
		command.DispatchCompute( bloomCs, Mip( 0 ).Size );
		command.ResourceBarrierTransition( Mip( 0 ), ResourceState.NonPixelShaderResource );

		command.Attributes.SetCombo( "D_STAGE", 1 );
		for ( int i = 1; i < BloomMips.Length; i++ )
		{
			command.Attributes.Set( "InputTexture", Mip( i - 1 ).ColorTexture );
			command.Attributes.Set( "BloomOut", Mip( i ).ColorTexture );
			command.DispatchCompute( bloomCs, Mip( i ).Size );
			command.ResourceBarrierTransition( Mip( i ), ResourceState.NonPixelShaderResource );
		}

		command.Attributes.SetCombo( "D_STAGE", 2 );
		for ( int i = BloomMips.Length - 2; i >= 0; i-- )
		{
			command.Attributes.Set( "InputTexture", Mip( i + 1 ).ColorTexture );
			command.Attributes.Set( "BloomOut", Mip( i ).ColorTexture );
			command.DispatchCompute( bloomCs, Mip( i ).Size );
			command.ResourceBarrierTransition( Mip( i ), i == 0 ? ResourceState.PixelShaderResource : ResourceState.NonPixelShaderResource );
		}

		command.Attributes.Set( "BloomTexture", Mip( 0 ).ColorTexture );
		command.Attributes.Set( "CompositeMode", 0 );
		command.Attributes.Set( "BloomScale", 1.0f / (2.0f * BloomMips.Length) );
		command.Attributes.Set( "Tint", Color.White );
		command.Blit( Shader( "shaders/postprocess/postprocess_bloom.shader" ) );
	}

	/// <summary>A frame grab with a box-filtered mip chain (<c>downsample_spd</c>), read back at mip 3.</summary>
	static void BoxMips( CommandList cl )
	{
		cl.Attributes.GrabFrameTexture( "ColorBuffer", Graphics.DownsampleMethod.Box );
		var a = cl.Attributes;
		a.Set( "scale", 0.125f );
		cl.Blit( Shader( "shaders/postprocess/pp_pixelate.shader" ) );
	}

	static readonly Case[] Cases =
	[
		new( "copy", "the frame copied into the target, nothing else", _ => { } ),
		new( "vignette", "a blit over the frame (BlitMode.Simple)", Vignette ),
		new( "color", "a blit of a frame grab (BlitMode.WithBackbuffer)", ColorAdjustments ),
		new( "filmgrain", "frame grab, noise", FilmGrain ),
		new( "chromatic", "frame grab, offset reads", ChromaticAberration ),
		new( "sharpen", "frame grab, neighbour reads", Sharpen ),
		new( "pixelate", "frame grab with gaussian mips", Pixelate ),
		new( "blur", "frame grab with gaussian mips, read between mips", Blur ),
		new( "box-mips", "frame grab with downsample_spd box mips", BoxMips ),
		new( "tonemapping", "Hable filmic at the default exposure", Tonemapping ),
		new( "bloom", "named targets, a compute chain down and up, a composite", Bloom,
			NotCompared: "Source 2's run, in a block with no view, samples its missing-texture checker through the chain, so it isn't a reference" ),
		new( "stack", "tonemapping, sharpen, chromatic aberration, colour adjustments, film grain, vignette in turn", cl =>
		{
			Tonemapping( cl );
			Sharpen( cl );
			ChromaticAberration( cl );
			ColorAdjustments( cl );
			FilmGrain( cl );
			Vignette( cl );
		} ),
	];

	public static int Run( Source2Backend backend, string folder )
	{
		var only = Args.String( "-case" );
		var cases = Cases.Where( c => only is null || only.Split( ',' ).Contains( c.Name ) ).ToArray();
		var tolerance = Args.Int( "-tolerance", 2 );
		folder = System.IO.Path.GetFullPath( folder );
		System.IO.Directory.CreateDirectory( folder );

		using var gpu = BasaltGpuRenderBackend.Create( Args.Has( "-validation" ), m => Log.Info( $"Basalt.Gpu: {m.Severity} {m.Text}" ) );
		backend.Phase( $"Basalt.Gpu device ({gpu.Device.Adapter.Name})" );

		// The frame the post stack starts from, made after the Basalt.Gpu device so its texture keeps its pixels for it
		using var frame = SunShadows( backend, folder );
		backend.Phase( "Sun Shadows on Source 2" );

		var rows = new List<string>();
		var failed = 0;
		foreach ( var test in cases )
		{
			var (max, over, error) = RunCase( backend, gpu, frame, test, System.IO.Path.Combine( folder, test.Name ), tolerance );
			var ok = (over == 0 || test.NotCompared is not null) && error is null;
			if ( !ok ) failed++;

			var line = $"{test.Name}: max channel difference {max}, {over} pixels over {tolerance}" + (error is null ? "" : $"; {error}") + (test.NotCompared is null ? "" : $"; not compared: {test.NotCompared}");
			Log.Info( $"Post parity {line}" );
			Console.WriteLine( line );
			rows.Add( $"| {test.Name} | {test.Covers} | {max} | {over} | {(!ok ? "FAIL" : test.NotCompared is not null ? "not compared" : "pass")} |" );
		}

		gpu.WaitIdle();
		var errors = gpu.Device.ValidationErrors;
		var summary = $"Post parity: {cases.Length - failed} of {cases.Length} cases within {tolerance} on {gpu.Device.Adapter.Name}, Sun Shadows {Width}x{Height} ({folder}); Basalt.Gpu validation errors {errors}";
		Log.Info( summary );
		Console.WriteLine( summary );

		var report = new List<string>
		{
			$"# Post parity on {gpu.Device.Adapter.Name}",
			"",
			summary,
			"",
			"Each case runs a post process's command list over Basalt's Sun Shadows frame on Source 2's device, then on a Basalt.Gpu device through `BasaltGpuRenderBackend`.",
			"",
			"| Case | Covers | Max channel difference (8-bit sRGB) | Pixels over the tolerance | |",
			"|---|---|---|---|---|",
		};
		report.AddRange( rows );
		System.IO.File.WriteAllLines( System.IO.Path.Combine( folder, "results.md" ), report );
		return failed == 0 && errors == 0 ? 0 : 1;
	}

	/// <summary>Sun Shadows rendered by Basalt on Source 2, as an RGBA16F texture of its pixels.</summary>
	static Texture SunShadows( Source2Backend backend, string folder )
	{
		var lab = LabWorld.Build( LabPresets.Find( "Sun Shadows" ), backend.Content );
		var view = new RenderView { Name = "Post parity", ClearColor = new Color( 0.08f, 0.09f, 0.11f ), Viewport = new Rect( 0, 0, Width, Height ) };
		lab.Aim( view, LabWorld.DefaultPitch, LabWorld.DefaultYaw, lab.DefaultDistance );
		using var renderer = new RenderSystem();
		using var target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Post parity" );

		for ( var i = 0; i < 10; i++ )
		{
			backend.BeginFrame();
			renderer.Render( lab.World, view, target );
			backend.EndFrame();
		}

		backend.Flush();
		var halves = new ushort[Width * Height * 4];
		target.Resolved.Engine().GetPixels( (0, 0, Width, Height), 0, 0, halves.AsSpan(), ImageFormat.RGBA16161616F );

		// Some highlights over 1, so bloom's threshold and the tonemapper have something to do
		for ( int i = 0; i < halves.Length; i++ )
		{
			if ( i % 4 == 3 ) continue;
			halves[i] = BitConverter.HalfToUInt16Bits( (Half)((float)BitConverter.UInt16BitsToHalf( halves[i] ) * 2.5f) );
		}

		Source2Backend.SavePng( Encode( halves ), Width, Height, System.IO.Path.Combine( folder, "frame.png" ) );
		var bytes = new byte[halves.Length * 2];
		Buffer.BlockCopy( halves, 0, bytes, 0, bytes.Length );
		return Texture.Create( Width, Height, ImageFormat.RGBA16161616F ).WithData( bytes ).Finish();
	}

	static (int Max, int Over, string Error) RunCase( Source2Backend backend, BasaltGpuRenderBackend gpu, Texture frame, Case test, string folder, int tolerance )
	{
		backend.BeginFrame();
		string error = null;

		// Source 2
		var native = Apply( RenderBackend.Default, frame, test );
		g_pRenderDevice.Flush();
		var nativePixels = Read( native, null );
		native.Dispose();
		backend.Phase( $"{test.Name}: Source 2" );

		// Basalt.Gpu
		var basaltPixels = new ushort[Width * Height * 4];
		try
		{
			var basalt = Apply( gpu, frame, test );
			basaltPixels = Read( basalt, gpu );
			basalt.Dispose();
		}
		catch ( Exception e )
		{
			error = $"{e.GetType().Name}: {e.Message}";
			Log.Warning( e, $"Post parity {test.Name}" );
		}
		finally
		{
			gpu.WaitIdle();
			gpu.NextFrame();
		}

		backend.Phase( $"{test.Name}: Basalt.Gpu" );

		var a = Encode( nativePixels );
		var b = Encode( basaltPixels );
		var diff = new Color32[a.Length];
		int max = 0, over = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			var d = Math.Max( Math.Max( Math.Abs( a[i].r - b[i].r ), Math.Abs( a[i].g - b[i].g ) ), Math.Abs( a[i].b - b[i].b ) );
			max = Math.Max( max, d );
			if ( d > tolerance ) over++;
			var v = (byte)Math.Min( 255, d * 16 );
			diff[i] = new Color32( v, v, v, 255 );
		}

		System.IO.Directory.CreateDirectory( folder );
		Source2Backend.SavePng( a, Width, Height, System.IO.Path.Combine( folder, "native.png" ) );
		Source2Backend.SavePng( b, Width, Height, System.IO.Path.Combine( folder, "basalt.png" ) );
		Source2Backend.SavePng( diff, Width, Height, System.IO.Path.Combine( folder, "diff.png" ) );
		return (max, over, error);
	}

	/// <summary>A copy of the frame, and the case's command list run over it in a block on <paramref name="device"/>.</summary>
	static Texture Apply( RenderBackend device, Texture frame, Case test )
	{
		var target = Texture.CreateRenderTarget().WithSize( Width, Height ).WithFormat( ImageFormat.RGBA16161616F ).WithUAVBinding().Create();
		var cl = new CommandList( test.Name );

		// What a camera's view gives its post processing: the viewport and target size, no exposure scaling
		cl.Attributes.SetData( "PerViewConstantBuffer_t", new ViewConstants
		{
			ToneMapScalarLinear = 1,
			ToneMapScalarGamma = 1,
			LightMapScalar = 1,
			EnvMapScalar = 1,
			ViewportMaxZ = 1,
			ViewportSize = new Vector2( Width, Height ),
			InvViewportSize = new Vector2( 1.0f / Width, 1.0f / Height ),
			RenderTargetSize = new Vector2( Width, Height ),
		} );
		test.Record( cl );

		using ( Graphics.Scope.CreateOn( device ) )
		{
			Graphics.RenderTarget = RenderTarget.From( target );
			Graphics.CopyTexture( frame, target );
			cl.Execute();
		}

		return target;
	}

	static ushort[] Read( Texture texture, BasaltGpuRenderBackend gpu )
	{
		var halves = new ushort[Width * Height * 4];
		if ( gpu is null )
		{
			texture.GetPixels( (0, 0, Width, Height), 0, 0, halves.AsSpan(), ImageFormat.RGBA16161616F );
			return halves;
		}

		var bytes = gpu.ReadBytes( texture );
		Buffer.BlockCopy( bytes, 0, halves, 0, Math.Min( bytes.Length, halves.Length * 2 ) );
		return halves;
	}

	/// <summary>8-bit sRGB, as a window's final blit encodes a float frame.</summary>
	static Color32[] Encode( ushort[] halves )
	{
		static byte Channel( ushort half )
		{
			var linear = Math.Clamp( (float)BitConverter.UInt16BitsToHalf( half ), 0, 1 );
			var gamma = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow( linear, 1.0f / 2.4f ) - 0.055f;
			return (byte)MathF.Round( gamma * 255 );
		}

		var pixels = new Color32[halves.Length / 4];
		for ( int i = 0; i < pixels.Length; i++ ) pixels[i] = new Color32( Channel( halves[i * 4] ), Channel( halves[i * 4 + 1] ), Channel( halves[i * 4 + 2] ), 255 );
		return pixels;
	}
}
