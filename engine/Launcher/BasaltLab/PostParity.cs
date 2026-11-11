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
/// reported errors. With <c>-components</c> the Basalt.Gpu side runs the engine's own post process components (built
/// with Basalt.Gpu as the device recorded for, so their attributes are managed) against the same Source 2 reference.
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
	sealed record Case( string Name, string Covers, Action<CommandList> Record, string NotCompared = null, Action<GameObject> Components = null, bool NeedsDepth = false );

	/// <summary>Run the engine's post process components (<c>-components</c>) rather than the copies of their command lists.</summary>
	static bool components;
	static Scene scene;

	/// <summary>
	/// Render the Basalt.Gpu side's frame on Basalt.Gpu (<c>-scene-gpu</c>): Sun Shadows drawn by Basalt through the
	/// backend's own <see cref="Basalt.Backend.BackendDevice"/>, then the post stack over it, against Sun Shadows on Source 2
	/// with the post stack there. The frames differ as the scene on two devices does, so cases pass on the mean.
	/// </summary>
	static bool sceneOnGpu;

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

	// What the case being run draws into: the copy of the frame and the depth bound with it (the scene's)
	static Texture caseColor, caseDepth;

	// Depth of field's focus: the lab camera's distance to the scene, and a range that leaves the front and back blurred
	static float focalDistance = 200, focusRange = 100;

	static ComputeShader dofCs;
	static GpuBuffer<uint> dofTiles;
	static GpuBuffer<GpuBuffer.IndirectDispatchArguments> dofArguments;

	/// <summary>
	/// As <c>DepthOfField.Render</c> at high quality, front and back: circle of confusion into half-size targets, tile
	/// classification into an indirect dispatch's arguments, two indirect blur passes per layer, and composites that test
	/// the view's depth. A block without a view has no <c>RenderValue</c> targets on Source 2, so the colour and depth are
	/// set as the targets themselves.
	/// </summary>
	static void DepthOfField( CommandList command )
	{
		const int TileCapacity = 32 * 1024, downsample = 2, tileSize = 16;
		dofCs ??= new ComputeShader( "postprocess/postprocess_standard_dof_cs" );
		dofTiles ??= new GpuBuffer<uint>( TileCapacity * 2, GpuBuffer.UsageFlags.Structured, "DoF_Tiles" );
		dofArguments ??= new GpuBuffer<GpuBuffer.IndirectDispatchArguments>( 2, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments, "DoF_DispatchArgs" );

		command.SetBufferData( dofArguments, new GpuBuffer.IndirectDispatchArguments[]
		{
			new() { ThreadGroupCountX = 0, ThreadGroupCountY = 1, ThreadGroupCountZ = 1 },
			new() { ThreadGroupCountX = 0, ThreadGroupCountY = 1, ThreadGroupCountZ = 1 },
		} );

		command.Attributes.Set( "Color", caseColor );
		command.Attributes.Set( "Depth", caseDepth );
		command.Attributes.SetCombo( "D_MSAA", 0 );
		var back = command.GetRenderTarget( "DofBack", downsample, ImageFormat.RGBA16161616F, ImageFormat.None );
		var front = command.GetRenderTarget( "DofFront", downsample, ImageFormat.RGBA16161616F, ImageFormat.None );
		var vertical = command.GetRenderTarget( "DofVertical", downsample, ImageFormat.RGBA16161616F, ImageFormat.None );
		var diagonal = command.GetRenderTarget( "DofDiagonal", downsample, ImageFormat.RGBA16161616F, ImageFormat.None );
		command.Clear( back, Color.Transparent );
		command.Clear( front, Color.Transparent );

		var tileMax = command.GetRenderTarget( "DofTileMax", (downsample * tileSize) - 1, ImageFormat.RG1616F, ImageFormat.None );
		var tileDilated = command.GetRenderTarget( "DofTileDilated", (downsample * tileSize) - 1, ImageFormat.RG1616F, ImageFormat.None );

		command.Attributes.Set( "InvDimensions", vertical.Size, true );
		command.Attributes.Set( "Dimensions", vertical.Size );
		command.Attributes.Set( "BlurSize", 30.0f );
		command.Attributes.Set( "StepScale", 1.0f );
		command.Attributes.Set( "FocusPlane", focalDistance );
		command.Attributes.Set( "FocusRange", focusRange );
		command.Attributes.Set( "EnableBack", true );
		command.Attributes.Set( "EnableFront", true );
		command.Attributes.Set( "TileCapacity", TileCapacity );

		command.ResourceBarrierTransition( back, ResourceState.UnorderedAccess );
		command.ResourceBarrierTransition( front, ResourceState.UnorderedAccess );
		command.ResourceBarrierTransition( tileMax, ResourceState.UnorderedAccess );
		command.Attributes.Set( "OutColor0", back.ColorTexture );
		command.Attributes.Set( "OutColor1", front.ColorTexture );
		command.Attributes.Set( "OutTile", tileMax.ColorTexture );
		command.Attributes.SetCombo( "D_PASS", 0 );
		command.DispatchCompute( dofCs, vertical.Size );

		command.ResourceBarrierTransition( tileMax, ResourceState.NonPixelShaderResource );
		command.ResourceBarrierTransition( back, ResourceState.NonPixelShaderResource );
		command.ResourceBarrierTransition( front, ResourceState.NonPixelShaderResource );

		command.ResourceBarrierTransition( dofTiles, ResourceState.UnorderedAccess );
		command.ResourceBarrierTransition( dofArguments, ResourceState.CopyDestination, ResourceState.UnorderedAccess );
		command.ResourceBarrierTransition( tileDilated, ResourceState.UnorderedAccess );
		command.Attributes.Set( "TileMaxSRV", tileMax.ColorTexture );
		command.Attributes.Set( "OutTile", tileDilated.ColorTexture );
		command.Attributes.Set( "TileListRW", dofTiles );
		command.Attributes.Set( "DispatchArgsRW", dofArguments );
		command.Attributes.SetCombo( "D_PASS", 1 );
		command.DispatchCompute( dofCs, tileMax.Size );

		command.ResourceBarrierTransition( dofTiles, ResourceState.UnorderedAccess, ResourceState.NonPixelShaderResource );
		command.ResourceBarrierTransition( dofArguments, ResourceState.UnorderedAccess, ResourceState.IndirectArgument );
		command.ResourceBarrierTransition( tileDilated, ResourceState.NonPixelShaderResource );

		command.Attributes.SetCombo( "D_DEBUG_COC", 0 );
		command.Attributes.Set( "TileList", dofTiles );
		command.Attributes.Set( "TileDilatedSRV", tileDilated.ColorTexture );

		var composite = Shader( "shaders/postprocess/postprocess_standard_dof.shader" );
		for ( var type = 0; type < 2; type++ )
		{
			var target = type == 0 ? back : front;
			command.Clear( vertical, Color.Transparent );
			command.Clear( diagonal, Color.Transparent );
			command.ResourceBarrierTransition( vertical, ResourceState.UnorderedAccess );
			command.ResourceBarrierTransition( diagonal, ResourceState.UnorderedAccess );

			command.Attributes.Set( "IsFront", type == 1 );
			command.Attributes.Set( "ListOffset", type == 1 ? TileCapacity : 0 );
			command.Attributes.Set( "FinalSRV", target.ColorTexture );
			command.Attributes.Set( "OutColor0", vertical.ColorTexture );
			command.Attributes.Set( "OutColor1", diagonal.ColorTexture );
			command.Attributes.SetCombo( "D_PASS", 2 );
			command.DispatchComputeIndirect( dofCs, dofArguments, (uint)type );

			command.ResourceBarrierTransition( vertical, ResourceState.NonPixelShaderResource );
			command.ResourceBarrierTransition( diagonal, ResourceState.NonPixelShaderResource );
			command.ResourceBarrierTransition( target, ResourceState.UnorderedAccess );

			command.Attributes.Set( "VerticalSRV", vertical.ColorTexture );
			command.Attributes.Set( "DiagonalSRV", diagonal.ColorTexture );
			command.Attributes.Set( "OutColor0", target.ColorTexture );
			command.Attributes.SetCombo( "D_PASS", 3 );
			command.DispatchComputeIndirect( dofCs, dofArguments, (uint)type );

			command.ResourceBarrierTransition( target, ResourceState.PixelShaderResource );
			command.Attributes.Set( "Final", target.ColorTexture );
			command.Attributes.SetCombo( "D_DOF_TYPE", type );
			command.Blit( composite );
		}
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
		new( "vignette", "a blit over the frame (BlitMode.Simple)", Vignette, Components: go => go.Components.Create<global::Sandbox.Vignette>() ),
		new( "color", "a blit of a frame grab (BlitMode.WithBackbuffer)", ColorAdjustments, Components: ColorComponent ),
		new( "filmgrain", "frame grab, noise", FilmGrain, Components: go => go.Components.Create<global::Sandbox.FilmGrain>() ),
		new( "chromatic", "frame grab, offset reads", ChromaticAberration, Components: go => go.Components.Create<global::Sandbox.ChromaticAberration>() ),
		new( "sharpen", "frame grab, neighbour reads", Sharpen, Components: go => go.Components.Create<global::Sandbox.Sharpen>().Scale = 2 ),
		new( "pixelate", "frame grab with gaussian mips", Pixelate, Components: go => go.Components.Create<global::Sandbox.Pixelate>().Scale = 0.25f ),
		new( "blur", "frame grab with gaussian mips, read between mips", Blur, Components: go => go.Components.Create<global::Sandbox.Blur>().Size = 1 ),
		new( "box-mips", "frame grab with downsample_spd box mips", BoxMips ),
		new( "tonemapping", "Hable filmic at the default exposure", Tonemapping, Components: go => go.Components.Create<global::Sandbox.Tonemapping>().Mode = global::Sandbox.Tonemapping.TonemappingMode.HableFilmic ),
		new( "bloom", "named targets, a compute chain down and up, a composite", Bloom,
			Components: go =>
			{
				var bloom = go.Components.Create<global::Sandbox.Bloom>();
				bloom.Threshold = 0.5f;
			} ),
		new( "dof", "depth of field: the view's depth, indirect dispatches, composites tested against depth", DepthOfField, NeedsDepth: true,
			Components: go =>
			{
				var dof = go.Components.Create<global::Sandbox.DepthOfField>();
				dof.BlurSize = 30;
				dof.FocalDistance = focalDistance;
				dof.FocusRange = focusRange;
				dof.FrontBlur = true;
				dof.BackBlur = true;
			} ),
		new( "stack", "bloom, vignette, tonemapping, sharpen, film grain, chromatic aberration, colour adjustments, in their stages' order", cl =>
		{
			Bloom( cl );
			Vignette( cl );
			Tonemapping( cl );
			Sharpen( cl );
			FilmGrain( cl );
			ChromaticAberration( cl );
			ColorAdjustments( cl );
		}, Components: go =>
		{
			go.Components.Create<global::Sandbox.Bloom>().Threshold = 0.5f;
			go.Components.Create<global::Sandbox.Vignette>();
			go.Components.Create<global::Sandbox.Tonemapping>().Mode = global::Sandbox.Tonemapping.TonemappingMode.HableFilmic;
			go.Components.Create<global::Sandbox.Sharpen>().Scale = 2;
			go.Components.Create<global::Sandbox.FilmGrain>();
			go.Components.Create<global::Sandbox.ChromaticAberration>();
			ColorComponent( go );
		} ),
	];

	static void ColorComponent( GameObject go )
	{
		var color = go.Components.Create<global::Sandbox.ColorAdjustments>();
		color.Saturation = 0.6f;
		color.HueRotate = 30;
		color.Brightness = 1.1f;
		color.Contrast = 1.2f;
	}

	/// <summary>
	/// The command lists a case's components record for a camera, in the order a camera runs them: by stage, then by
	/// order within it. Made with <paramref name="device"/> as the device being recorded for, so their attributes are
	/// the kind its blocks draw with.
	/// </summary>
	static List<CommandList> ComponentLists( Case test, RenderBackend device )
	{
		var previous = Graphics.SetRecordingBackend( device == RenderBackend.Default ? null : device );
		try
		{
			using var _ = scene.Push();
			var go = new GameObject( true, $"Post parity {test.Name}" );
			var camera = go.Components.Create<CameraComponent>();
			test.Components( go );

			foreach ( var effect in go.Components.GetAll<BasePostProcess>() )
				effect.Build( new PostProcessContext { Camera = camera, Components = [new WeightedEffect { Effect = effect, Weight = 1 }] } );

			var lists = new List<CommandList>();
			foreach ( var (_, layers) in camera.PostProcess.Layers.OrderBy( x => x.Key ) )
			{
				layers.Sort();
				lists.AddRange( layers.Select( x => x.CommandList ) );
			}

			go.Destroy();
			return lists;
		}
		finally
		{
			Graphics.SetRecordingBackend( previous );
		}
	}

	public static int Run( Source2Backend backend, string folder )
	{
		components = Args.Has( "-components" );
		sceneOnGpu = Args.Has( "-scene-gpu" );
		var meanTolerance = Args.Float( "-mean-tolerance", 1.0f );
		if ( components && Game.TypeLibrary is null )
		{
			// A scene finds its systems through the type library, which this lab doesn't make otherwise (as PanelAppSystem makes one)
			// (its constructor and AddIntrinsicTypes are internal to the engine's app assemblies, hence reflection)
			const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
			var library = (Sandbox.Internal.TypeLibrary)Activator.CreateInstance( typeof( Sandbox.Internal.TypeLibrary ), nonPublic: true );
			typeof( Sandbox.Internal.TypeLibrary ).GetMethod( "AddIntrinsicTypes", any ).Invoke( library, null );
			var add = typeof( Sandbox.Internal.TypeLibrary ).GetMethod( "AddAssembly", any, [typeof( System.Reflection.Assembly ), typeof( bool )] );
			add.Invoke( library, [typeof( Vector3 ).Assembly, false] );
			add.Invoke( library, [typeof( Scene ).Assembly, false] );
			Game.TypeLibrary = library;
		}

		scene = components ? new Scene() : null;
		var only = Args.String( "-case" );
		var cases = Cases.Where( c => only is null || only.Split( ',' ).Contains( c.Name ) ).ToArray();

		// What reads the view's depth runs over the scene drawn on each device, which has its depth
		foreach ( var test in cases.Where( c => c.NeedsDepth && !sceneOnGpu ) ) Console.WriteLine( $"{test.Name}: not run, it reads the scene's depth (-scene-gpu)" );
		cases = cases.Where( c => !c.NeedsDepth || sceneOnGpu ).ToArray();
		var tolerance = Args.Int( "-tolerance", 2 );
		folder = System.IO.Path.GetFullPath( folder );
		System.IO.Directory.CreateDirectory( folder );

		using var gpu = BasaltGpuRenderBackend.Create( Args.Has( "-validation" ), m => Log.Info( $"Basalt.Gpu: {m.Severity} {m.Text}" ) );
		backend.Phase( $"Basalt.Gpu device ({gpu.Device.Adapter.Name})" );

		// The frame the post stack starts from, made after the Basalt.Gpu device so its texture keeps its pixels for it
		using var frame = SunShadows( backend, folder );
		backend.Phase( "Sun Shadows on Source 2" );
		gpuScene = sceneOnGpu ? new GpuScene( gpu ) : null;
		if ( sceneOnGpu ) backend.Phase( "Sun Shadows on Basalt.Gpu" );

		var rows = new List<string>();
		var failed = 0;
		foreach ( var test in cases )
		{
			var (max, over, mean, error) = RunCase( backend, gpu, frame, test, System.IO.Path.Combine( folder, test.Name ), tolerance );
			var ok = (sceneOnGpu ? mean <= meanTolerance : over == 0 || test.NotCompared is not null) && error is null;
			if ( !ok ) failed++;

			var line = $"{test.Name}: max channel difference {max}, {over} pixels over {tolerance}, mean difference {mean:F3}" + (error is null ? "" : $"; {error}") + (test.NotCompared is null ? "" : $"; not compared: {test.NotCompared}");
			Log.Info( $"Post parity {line}" );
			Console.WriteLine( line );
			rows.Add( $"| {test.Name} | {test.Covers} | {max} | {over} | {(!ok ? "FAIL" : test.NotCompared is not null ? "not compared" : "pass")} |" );
		}

		gpu.WaitIdle();
		var errors = gpu.Device.ValidationErrors;
		var summary = $"Post parity{(components ? " (components)" : "")}{(sceneOnGpu ? " (scene on Basalt.Gpu)" : "")}: {cases.Length - failed} of {cases.Length} cases within {(sceneOnGpu ? $"{meanTolerance:F2} mean difference" : $"{tolerance}")} on {gpu.Device.Adapter.Name}, Sun Shadows {Width}x{Height} ({folder}); Basalt.Gpu validation errors {errors}";
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

		// The components' scene and what scenes make for the process (particle lighting's texture)
		gpuScene?.Dispose();
		gpuScene = null;
		source2Target?.Dispose();
		source2Renderer?.Dispose();
		source2Target = null;
		source2Renderer = null;
		dofTiles?.Dispose();
		dofArguments?.Dispose();
		dofTiles = null;
		dofArguments = null;

		if ( scene is not null )
		{
			scene.Destroy();
			scene = null;

			// Made once for every scene, and kept for the process; this one ends here
			ParticleLightingSystem.Unlit.Dispose();
		}
		return failed == 0 && errors == 0 ? 0 : 1;
	}

	// Sun Shadows on Source 2, kept for its depth, which the reference binds with the frame when the scene is on Basalt.Gpu
	static RenderSystem source2Renderer;
	static ViewTarget source2Target;

	/// <summary>Sun Shadows rendered by Basalt on Source 2, as an RGBA16F texture of its pixels.</summary>
	static Texture SunShadows( Source2Backend backend, string folder )
	{
		var lab = LabWorld.Build( LabPresets.Find( "Sun Shadows" ), backend.Content );
		var view = new RenderView { Name = "Post parity", ClearColor = new Color( 0.08f, 0.09f, 0.11f ), Viewport = new Rect( 0, 0, Width, Height ) };
		lab.Aim( view, LabWorld.DefaultPitch, LabWorld.DefaultYaw, lab.DefaultDistance );
		focalDistance = lab.DefaultDistance;
		focusRange = lab.DefaultDistance * 0.5f;
		var renderer = source2Renderer = new RenderSystem();
		var target = source2Target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Post parity" );

		for ( var i = 0; i < 10; i++ )
		{
			backend.BeginFrame();
			renderer.Render( lab.World, view, target );
			backend.EndFrame();
		}

		backend.Flush();
		var halves = new ushort[Width * Height * 4];
		target.Resolved.Engine().GetPixels( (0, 0, Width, Height), 0, 0, halves.AsSpan(), ImageFormat.RGBA16161616F );
		return FrameTexture( halves, System.IO.Path.Combine( folder, "frame.png" ), sceneOnGpu ? 1 : 2.5f );
	}

	/// <summary>
	/// Sun Shadows rendered by Basalt on the Basalt.Gpu device the post stack runs on, from content Basalt.Source2 reads
	/// (no 2D sky there yet), handed to the engine's drawing on that device as a camera frame would be
	/// (<see cref="BasaltGpuFrame"/>): its targets and view constants, no readback.
	/// </summary>
	sealed class GpuScene : IDisposable
	{
		readonly RenderSystem renderer;
		readonly ViewTarget target;
		readonly RenderHost host;

		public BasaltGpuFrame Frame { get; }

		/// <summary>The view constants the scene drew with.</summary>
		public ViewConstants Constants { get; }

		public GpuScene( BasaltGpuRenderBackend gpu )
		{
			var device = gpu.Services;
			host = new Basalt.Backend.BackendRenderHost( device );
			var previous = RenderHost.Current;
			RenderHost.Current = host;
			try
			{

				// Made once the host is the Basalt.Gpu device's, which it records its frames with
				renderer = new RenderSystem { ParallelRecording = false };
				var lab = LabWorld.Build( LabPresets.Find( "Sun Shadows" ), new BackendLabContent( device ) );
				var view = new RenderView { Name = "Post parity", ClearColor = new Color( 0.08f, 0.09f, 0.11f ), Viewport = new Rect( 0, 0, Width, Height ) };
				lab.Aim( view, LabWorld.DefaultPitch, LabWorld.DefaultYaw, lab.DefaultDistance );
				target = ViewTarget.Create( new Vector2Int( Width, Height ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Post parity" );

				for ( var i = 0; i < 10; i++ )
				{
					device.BeginFrame();
					RenderHost.Current.NextFrame();
					renderer.Render( lab.World, view, target );
				}

				Frame = new BasaltGpuFrame( gpu );
				Constants = renderer.Frame.Constants;
				Frame.Adopt( target, Constants );
			}
			finally
			{
				// The lab's own frames go on through Source 2's host
				RenderHost.Current = previous;
			}
		}

		public void Dispose()
		{
			var previous = RenderHost.Current;
			RenderHost.Current = host;
			Frame.Dispose();
			target.Dispose();
			renderer.Dispose();
			RenderHost.Current = previous;
		}
	}

	static GpuScene gpuScene;

	/// <summary>A frame's pixels, with its highlights pushed past 1, as an RGBA16F texture.</summary>
	static Texture FrameTexture( ushort[] halves, string png, float scale )
	{
		// Some highlights over 1, so bloom's threshold and the tonemapper have something to do
		for ( int i = 0; i < halves.Length; i++ )
		{
			if ( i % 4 == 3 ) continue;
			halves[i] = BitConverter.HalfToUInt16Bits( (Half)((float)BitConverter.UInt16BitsToHalf( halves[i] ) * scale) );
		}

		Source2Backend.SavePng( Encode( halves ), Width, Height, png );
		var bytes = new byte[halves.Length * 2];
		Buffer.BlockCopy( halves, 0, bytes, 0, bytes.Length );
		return Texture.Create( Width, Height, ImageFormat.RGBA16161616F ).WithData( bytes ).Finish();
	}

	static (int Max, int Over, double Mean, string Error) RunCase( Source2Backend backend, BasaltGpuRenderBackend gpu, Texture frame, Case test, string folder, int tolerance )
	{
		backend.BeginFrame();
		string error = null;

		// Source 2
		// The reference is always the copies of the command lists on Source 2: the components' own attributes are drawn
		// with as they are, and Source 2 doesn't layer a block's over them outside a view, so in this block they'd miss the
		// frame grab and view constants there
		var native = Apply( RenderBackend.Default, frame, test, reference: true );
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
		long total = 0;
		for ( int i = 0; i < a.Length; i++ )
		{
			var d = Math.Max( Math.Max( Math.Abs( a[i].r - b[i].r ), Math.Abs( a[i].g - b[i].g ) ), Math.Abs( a[i].b - b[i].b ) );
			max = Math.Max( max, d );
			if ( d > tolerance ) over++;
			total += Math.Abs( a[i].r - b[i].r ) + Math.Abs( a[i].g - b[i].g ) + Math.Abs( a[i].b - b[i].b );
			var v = (byte)Math.Min( 255, d * 16 );
			diff[i] = new Color32( v, v, v, 255 );
		}

		System.IO.Directory.CreateDirectory( folder );
		Source2Backend.SavePng( a, Width, Height, System.IO.Path.Combine( folder, "native.png" ) );
		Source2Backend.SavePng( b, Width, Height, System.IO.Path.Combine( folder, "basalt.png" ) );
		Source2Backend.SavePng( diff, Width, Height, System.IO.Path.Combine( folder, "diff.png" ) );
		return (max, over, total / (3.0 * a.Length), error);
	}

	/// <summary>A copy of the frame, and the case's command list run over it in a block on <paramref name="device"/>.</summary>
	static Texture Apply( RenderBackend device, Texture frame, Case test, bool reference = false )
	{
		var target = Texture.CreateRenderTarget().WithSize( Width, Height ).WithFormat( ImageFormat.RGBA16161616F ).WithUAVBinding().Create();
		var cl = new CommandList( test.Name );

		// What a camera's view gives its post processing: the scene's own constants when it's drawn on Basalt.Gpu (on both
		// devices, so they draw with the same), else the viewport and target size with no exposure scaling
		var onFrame = gpuScene is not null && device is BasaltGpuRenderBackend;
		if ( gpuScene is not null ) cl.Attributes.SetData( "PerViewConstantBuffer_t", gpuScene.Constants );
		else cl.Attributes.SetData( "PerViewConstantBuffer_t", new ViewConstants
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
		cl.Attributes.Set( "BlueNoise", Texture.Load( "textures/dev/blue_noise_256.vtex" ) );

		// Bloom adds what a view's bloom-only effects drew at quarter size: none here
		cl.Attributes.Set( "QuarterResEffectsBloomInputTexture", Texture.Black );

		// The engine's components when asked and the case has them, else the copies of their command lists
		caseColor = target;
		caseDepth = onFrame ? gpuScene.Frame.Depth : gpuScene is not null ? source2Target.Depth.Engine() : null;
		var lists = components && !reference && test.Components is not null ? ComponentLists( test, device ) : null;
		if ( lists is null ) test.Record( cl );

		// On the GPU scene's frame: a block from the frame, which binds the view's constants itself, over a copy of its colour
		// made on the GPU, with its depth. Source 2 then binds its own Sun Shadows depth with the copy, as a view would (its
		// blocks without a view take no depth target, so it's bound on the context)
		using ( onFrame ? gpuScene.Frame.Begin() : Graphics.Scope.CreateOn( device ) )
		{
			Graphics.RenderTarget = RenderTarget.From( target, onFrame ? gpuScene.Frame.Depth : null );
			if ( !onFrame && caseDepth is not null ) NativeEngine.RenderTools.BindColorAndDepthTarget( Graphics.Context, target.native, caseDepth.native, false );
			Graphics.CopyTexture( onFrame ? gpuScene.Frame.Color : frame, target );
			cl.Execute();
			foreach ( var list in lists ?? [] ) list.Execute();
		}

		if ( onFrame ) gpuScene.Frame.End();
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
