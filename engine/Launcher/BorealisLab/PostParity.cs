using Borealis;
using Borealis.Gpu.Source2;
using Sandbox.Rendering;
using ViewConstants = Borealis.Gpu.ViewConstants;

namespace Sandbox.BorealisLab;

/// <summary>
/// <c>-post-parity folder [-case a,b] [-tolerance N] [-validation]</c>: renders the Sun Shadows preset through Borealis on
/// Source 2's device, then runs the engine's post processing over that HDR frame twice - on Source 2's device and on a
/// Borealis.Gpu device through the engine's own <see cref="BorealisGpuRenderBackend"/> - and compares the pixels. Each case
/// records the command list its post process component records (<c>BasePostProcess.Blit</c>, a frame grab, and
/// <c>Bloom</c>'s compute chain), into a copy of the frame. Each case writes <c>native.png</c>, <c>borealis.png</c> and
/// <c>diff.png</c> (differences x16, after encoding to 8-bit sRGB) to a folder of its own, and the folder gets
/// <c>results.md</c>. Exits 1 when a case differs by more than <c>-tolerance</c> (default 2), threw, or validation
/// reported errors. With <c>-components</c> the Borealis.Gpu side runs the engine's own post process components (built
/// with Borealis.Gpu as the device recorded for, so their attributes are managed) against the same Source 2 reference.
/// <para>
/// Stage 3 of moving the engine's rendering onto Borealis.Gpu (docs/managed/borealis-gpu-path.md). Auto exposure isn't here:
/// it's native's (<c>RenderTools.RenderAutoExposure</c>) and has no Borealis.Gpu form yet, so tonemapping runs with its
/// default exposure on both devices.
/// </para>
/// </summary>
internal static class PostParity
{
	const int Width = 640, Height = 360;

	/// <summary>
	/// A case: what a component would record, with its values on the command list's attributes.
	/// With <paramref name="InView"/>, Source 2's reference runs the command list inside a native view of its own (a
	/// <see cref="SceneCustomObject"/> drawn by a <see cref="SceneCamera"/> from the scene's camera, over a grey clear) rather
	/// than in a block outside a view, which leaves what these shaders read unbound.
	/// </summary>
	sealed record Case( string Name, string Covers, Action<CommandList> Record, string NotCompared = null, Action<GameObject> Components = null, bool NeedsDepth = false, Func<ViewConstants, ViewConstants> View = null, bool InView = false );

	/// <summary>Run the engine's post process components (<c>-components</c>) rather than the copies of their command lists.</summary>
	static bool components;
	static Scene scene;

	/// <summary>
	/// Render the Borealis.Gpu side's frame on Borealis.Gpu (<c>-scene-gpu</c>): Sun Shadows drawn by Borealis through the
	/// backend's own <see cref="Borealis.Backend.BackendDevice"/>, then the post stack over it, against Sun Shadows on Source 2
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

		// What a view binds every shader (Borealis's ViewEnvironment, native's view setup)
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

	/// <summary>
	/// As <c>MotionBlur.Render</c> at its defaults: a blit of a frame grab that rebuilds each pixel's world position from the
	/// view's depth chain and reprojects it into the previous frame, sampling along the way there.
	/// </summary>
	static void MotionBlur( CommandList cl )
	{
		var a = cl.Attributes;
		a.Set( "standard.motionblur.scale", 0.05f );
		a.Set( "standard.motionblur.samples", 12 );
		a.SetCombo( "D_MOTION_BLUR", 1 );
		Blit( cl, Material.Load( "materials/postprocess/standard_pass1.vmat" ), backbuffer: true );
	}

	/// <summary>
	/// The view constants of a camera that has just moved 20 units along view x: the scenes' cameras are still, so the
	/// previous frame's projection is moved instead, and how far each pixel moves depends on its depth.
	/// </summary>
	static ViewConstants Moved( ViewConstants constants )
	{
		ref var previous = ref System.Runtime.CompilerServices.Unsafe.As<Matrix, System.Numerics.Matrix4x4>( ref constants.CurrFrameViewToPrevFrameProj );
		previous = System.Numerics.Matrix4x4.CreateTranslation( 20, 0, 0 ) * previous;
		return constants;
	}

	// The reference's depth chain, built from Source 2's depth in its block
	static Texture source2Chain;

	/// <summary>A frame grab with a box-filtered mip chain (<c>downsample_spd</c>), read back at mip 3.</summary>
	static void BoxMips( CommandList cl )
	{
		cl.Attributes.GrabFrameTexture( "ColorBuffer", Graphics.DownsampleMethod.Box );
		var a = cl.Attributes;
		a.Set( "scale", 0.125f );
		cl.Blit( Shader( "shaders/postprocess/pp_pixelate.shader" ) );
	}

	//
	// Shaders whose .shader has a geometry stage, drawn with Graphics.Draw over a grey clear from the scene's camera: Source 2
	// runs their geometry stages in a native view of their own, Borealis.Gpu their pass modules' vertex stages, which expand
	// the primitives themselves
	//

	/// <summary>How far along the scene's focus the cases draw.</summary>
	const float StageScale = 0.5f;

	static readonly Color Grey = new( 0.18f, 0.18f, 0.2f );

	/// <summary>The command list runs inside Source 2's native view, on its targets.</summary>
	static bool inNativeView;

	/// <summary>The depth and stencil the cases draw with on Borealis.Gpu, as the native view has its own.</summary>
	static Texture caseStencil;

	static (Vector3 Center, Vector3 Right, Vector3 Up) Stage()
	{
		var c = gpuScene.Constants;
		var forward = (Vector3)c.CameraDirWs;
		var up = (Vector3)c.CameraUpDirWs;
		return ((Vector3)c.CameraPositionWs + forward * (focalDistance * StageScale), Vector3.Cross( forward, up ).Normal, up);
	}

	/// <summary>
	/// The case's target cleared to a flat grey, with a depth and stencil of its own cleared too (the scene's depth isn't
	/// bound, so nothing hides the draws): on Borealis.Gpu the case's colour and <see cref="caseStencil"/>, in Source 2's
	/// native view its own targets.
	/// </summary>
	static void ClearToGrey()
	{
		if ( !inNativeView )
		{
			if ( caseStencil is null || caseStencil.Width != caseColor.Width || caseStencil.Height != caseColor.Height )
			{
				caseStencil?.Dispose();
				caseStencil = Texture.CreateRenderTarget().WithSize( caseColor.Width, caseColor.Height ).WithDepthFormat().Create( "Post parity case depth" );
			}

			Graphics.RenderTarget = RenderTarget.From( caseColor, caseStencil );
		}

		Graphics.Clear( Grey, true, true, true );
	}

	/// <summary>Clear the target to a flat grey, then draw <paramref name="vertices"/> with <paramref name="material"/>.</summary>
	static void DrawOnGrey( CommandList cl, Material material, Graphics.PrimitiveType type, Vertex[] vertices, string mode = null )
	{
		cl.AddAction( ClearToGrey );
		DrawVertices( cl, material, type, vertices, mode );
	}

	/// <summary>
	/// Draw CPU vertices as <c>Graphics.Draw</c> does, with the transform at the frame's first slot (the identity). Borealis.Gpu
	/// takes the engine's <see cref="Vertex"/>; Source 2 is given the same vertices with an instance transform id (TEXCOORD13,
	/// 0), which these shaders index the view's transform buffer with. A material whose shader has only another
	/// <paramref name="mode"/> (<c>ToolsUtil</c>, which only tools views' layers draw) draws natively in that mode.
	/// </summary>
	static void DrawVertices( CommandList cl, Material material, Graphics.PrimitiveType type, Vertex[] vertices, string mode = null ) => cl.AddAction( () =>
	{
		if ( Graphics.Backend is BorealisGpuRenderBackend )
		{
			Graphics.Draw( vertices.AsSpan(), vertices.Length, material, null, type );
			return;
		}

		var reference = vertices.Select( v => new ReferenceVertex { Position = v.Position, Color = v.Color, Normal = v.Normal, TexCoord0 = v.TexCoord0, TexCoord1 = v.TexCoord1, Tangent = v.Tangent } ).ToArray();
		if ( mode is null ) Graphics.Draw<ReferenceVertex>( reference.AsSpan(), reference.Length, material, null, type );
		else DrawInMode( material, mode, type, reference );
	} );

	/// <summary>Source 2's <c>Graphics.Draw</c> of <paramref name="vertices"/> with the material's <paramref name="mode"/>.</summary>
	static unsafe void DrawInMode( Material material, string mode, Graphics.PrimitiveType type, ReferenceVertex[] vertices )
	{
		var layout = VertexLayout.Get( typeof( ReferenceVertex ) );
		if ( !NativeEngine.RenderTools.SetRenderState( Graphics.Context, Graphics.Attributes.Get(), material.native.GetMode( mode ), layout, Graphics.Stats ) ) return;
		fixed ( ReferenceVertex* data = vertices )
			NativeEngine.RenderTools.Draw( Graphics.Context, (NativeEngine.RenderPrimitiveType)type, layout, (IntPtr)data, vertices.Length, IntPtr.Zero, 0, Graphics.Stats );
	}

	/// <summary>The engine's <see cref="Vertex"/> with an instance transform id, for the Source 2 reference.</summary>
	[System.Runtime.InteropServices.StructLayout( System.Runtime.InteropServices.LayoutKind.Sequential )]
	struct ReferenceVertex
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Color] public Color32 Color;
		[VertexLayout.Normal] public Vector3 Normal;
		[VertexLayout.TexCoord( 0 )] public Vector4 TexCoord0;
		[VertexLayout.TexCoord( 1 )] public Vector4 TexCoord1;
		[VertexLayout.Tangent] public Vector4 Tangent;
		[VertexLayout.TexCoord( 13 )] public int InstanceTransform;
	}

	static Color32 Hue( float t ) => new Color( 0.5f + 0.5f * MathF.Cos( t * MathF.Tau ), 0.5f + 0.5f * MathF.Cos( (t - 1 / 3f) * MathF.Tau ), 0.5f + 0.5f * MathF.Cos( (t - 2 / 3f) * MathF.Tau ) ).ToColor32();

	/// <summary>A star of lines round the centre, each end its own colour, some reaching towards and away from the camera.</summary>
	static Vertex[] Star( int count, float radius )
	{
		var (center, right, up) = Stage();
		var forward = Vector3.Cross( up, right );
		var vertices = new Vertex[count * 2];
		for ( int i = 0; i < count; i++ )
		{
			var a = i * MathF.Tau / count;
			var inner = center + (right * MathF.Cos( a ) + up * MathF.Sin( a )) * radius * 0.2f;
			var outer = center + (right * MathF.Cos( a ) + up * MathF.Sin( a )) * radius + forward * (radius * 0.5f * MathF.Sin( a * 3 ));
			vertices[i * 2] = new Vertex( inner, Hue( i / (float)count ) ) { Normal = up };
			vertices[i * 2 + 1] = new Vertex( outer, Hue( i / (float)count + 0.5f ) ) { Normal = right };
		}

		return vertices;
	}

	/// <summary>A zigzag below the centre, as a strip.</summary>
	static Vertex[] Zigzag( int count, float width )
	{
		var (center, right, up) = Stage();
		var vertices = new Vertex[count];
		for ( int i = 0; i < count; i++ ) vertices[i] = new Vertex( center + right * (width * (i / (float)(count - 1) - 0.5f)) - up * (width * 0.35f + ((i & 1) != 0 ? width * 0.08f : 0)), Hue( i / (float)count ) );
		return vertices;
	}

	/// <summary>A grid of points round the centre, each made by <paramref name="point"/>.</summary>
	static Vertex[] Points( int columns, int rows, float spacing, Func<int, Vector3, Vertex> point )
	{
		var (center, right, up) = Stage();
		var vertices = new Vertex[columns * rows];
		for ( int y = 0; y < rows; y++ )
		{
			for ( int x = 0; x < columns; x++ )
			{
				var i = y * columns + x;
				vertices[i] = point( i, center + right * ((x - (columns - 1) * 0.5f) * spacing) + up * ((y - (rows - 1) * 0.5f) * spacing) );
			}
		}

		return vertices;
	}

	/// <summary>A sphere round the centre as a triangle list, with normals, tangents and uvs.</summary>
	static Vertex[] Sphere( float radius, int slices, int stacks )
	{
		var (center, _, _) = Stage();
		Vertex At( int slice, int stack )
		{
			var u = slice / (float)slices;
			var v = stack / (float)stacks;
			var theta = u * MathF.Tau;
			var phi = v * MathF.PI;
			var normal = new Vector3( MathF.Sin( phi ) * MathF.Cos( theta ), MathF.Sin( phi ) * MathF.Sin( theta ), MathF.Cos( phi ) );
			var tangent = new Vector3( -MathF.Sin( theta ), MathF.Cos( theta ), 0 );
			return new Vertex( center + normal * radius, normal, tangent, new Vector4( u, v, 0, 0 ) );
		}

		var vertices = new List<Vertex>();
		for ( int stack = 0; stack < stacks; stack++ )
		{
			for ( int slice = 0; slice < slices; slice++ )
			{
				vertices.AddRange( [At( slice, stack ), At( slice + 1, stack ), At( slice + 1, stack + 1 )] );
				vertices.AddRange( [At( slice, stack ), At( slice + 1, stack + 1 ), At( slice, stack + 1 )] );
			}
		}

		return [.. vertices];
	}

	static Material GizmoLine => Material.Load( "materials/gizmo/line.vmat" );

	/// <summary>Gizmo lines (gizmo_line): a list 3 pixels thick, depth tested, and a dashed strip a pixel thick over everything.</summary>
	static void GizmoLines( CommandList cl )
	{
		cl.Attributes.Set( "LineThickness", 3.0f );
		DrawOnGrey( cl, GizmoLine, Graphics.PrimitiveType.Lines, Star( 24, 60 * StageScale ) );
		cl.Attributes.Set( "LineThickness", 1.0f );
		cl.Attributes.Set( "PatternType", 1.0f );
		cl.Attributes.SetCombo( "D_NO_ZTEST", 1 );
		DrawVertices( cl, GizmoLine, Graphics.PrimitiveType.LineStrip, Zigzag( 16, 160 * StageScale ) );
		cl.Attributes.Set( "PatternType", 0.0f );
		cl.Attributes.SetCombo( "D_NO_ZTEST", 0 );
	}

	/// <summary>
	/// Gizmo sprites (gizmo_sprite): screen-sized and world-sized points, some rotated, textured, each a little further than
	/// the last, so which hides which doesn't come down to rounding.
	/// </summary>
	static void GizmoSprites( CommandList cl )
	{
		cl.Attributes.Set( "TextureColor", Texture.White );
		var (_, right, up) = Stage();
		var away = Vector3.Cross( up, right ) * StageScale;
		DrawOnGrey( cl, Material.FromShader( "shaders/gizmo/gizmo_sprite.shader" ), Graphics.PrimitiveType.Points, Points( 6, 4, 24 * StageScale, ( i, p ) =>
			new Vertex( p + away * i, new Vector4( (i & 1) != 0 ? 12 + i : (12 + i) * StageScale, (i & 1) != 0 ? 12 + i : (12 + i) * StageScale, (i & 1) != 0 ? 0 : 1, i * 0.3f ), Hue( i / 24f ) ) ) );
	}

	/// <summary>
	/// Tool sprites (tools_sprite at its defaults: Borealis.Gpu draws a material with its shader's, not its own): points of
	/// the uniform size, then the same with a border.
	/// </summary>
	static void ToolSprites( CommandList cl )
	{
		var material = Material.FromShader( "shaders/tools_sprite.shader" );
		cl.Attributes.Set( "UniformPointSize", 9.0f );
		DrawOnGrey( cl, material, Graphics.PrimitiveType.Points, Points( 6, 2, 24 * StageScale, ( i, p ) => new Vertex( p + new Vector3( 0, 0, 20 * StageScale ), Hue( i / 12f ) ) ) );
		cl.Attributes.SetCombo( "D_BORDER", 1 );
		cl.Attributes.Set( "BorderWidthPx", 2.0f );
		DrawVertices( cl, material, Graphics.PrimitiveType.Points, Points( 6, 2, 24 * StageScale, ( i, p ) => new Vertex( p - new Vector3( 0, 0, 20 * StageScale ), Hue( i / 12f + 0.5f ) ) ) );
		cl.Attributes.SetCombo( "D_BORDER", 0 );
	}

	/// <summary>Thick tool wireframe lines (tools_wireframe with D_THICK_LINES), shaded, depth biased.</summary>
	static void ThickWireframe( CommandList cl )
	{
		cl.Attributes.SetCombo( "D_THICK_LINES", 1 );
		cl.Attributes.SetCombo( "D_DEPTH_BIAS", 1 );
		cl.Attributes.Set( "g_LineThickness", 4.0f );
		DrawOnGrey( cl, Material.FromShader( "shaders/tools_wireframe.shader" ), Graphics.PrimitiveType.Lines, Star( 16, 60 * StageScale ) );
		cl.Attributes.SetCombo( "D_THICK_LINES", 0 );
		cl.Attributes.SetCombo( "D_DEPTH_BIAS", 0 );
	}

	/// <summary>
	/// Normals (tools_visualize_tangent_frame) of a sphere's triangles, and of a smaller one beside it, drawn as native's tools
	/// views draw them (its <c>ToolsUtil</c> mode).
	/// </summary>
	static void TangentFrames( CommandList cl )
	{
		var material = Material.Load( "materials/tools/show_normals.vmat" );
		var sphere = Sphere( 30 * StageScale, 12, 8 );
		cl.Attributes.Set( "g_flVectorLength", 10.0f );
		DrawOnGrey( cl, material, Graphics.PrimitiveType.Triangles, sphere, "ToolsUtil" );
		var moved = Sphere( 18 * StageScale, 10, 6 ).Select( v => v with { Position = v.Position + Stage().Right * (70 * StageScale) } ).ToArray();
		DrawVertices( cl, material, Graphics.PrimitiveType.Triangles, moved, "ToolsUtil" );
	}

	/// <summary>A highlight (ObjectHighlight): a sphere's inside pass, then its outline, as the Highlight component draws them.</summary>
	static void Highlight( CommandList cl )
	{
		var material = Material.FromShader( "shaders/postprocess/objecthighlight/objecthighlight.shader" );
		var sphere = Sphere( 30 * StageScale, 12, 8 );
		cl.AddAction( ClearToGrey );
		cl.Attributes.GrabFrameTexture( "ColorTexture" );
		cl.Attributes.SetCombo( "D_OUTLINE_PASS", 0 );
		cl.Attributes.Set( "Color", new Color( 0.2f, 0.6f, 1.0f, 0.3f ) );
		cl.Attributes.Set( "ObscuredColor", new Color( 1.0f, 0.4f, 0.1f, 0.3f ) );
		DrawVertices( cl, material, Graphics.PrimitiveType.Triangles, sphere );
		cl.Attributes.SetCombo( "D_OUTLINE_PASS", 1 );
		cl.Attributes.Set( "Color", new Color( 0.2f, 0.8f, 1.0f, 1.0f ) );
		cl.Attributes.Set( "ObscuredColor", new Color( 1.0f, 0.6f, 0.1f, 1.0f ) );
		cl.Attributes.Set( "LineWidth", 0.5f );
		DrawVertices( cl, material, Graphics.PrimitiveType.Triangles, sphere );
		cl.Attributes.SetCombo( "D_OUTLINE_PASS", 0 );
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
		new( "motionblur", "motion blur: depth through the view's depth chain, reprojected into a previous frame 20 units to the side", MotionBlur, NeedsDepth: true, View: Moved,
			Components: go => go.Components.Create<global::Sandbox.MotionBlur>() ),
		new( "gizmo-lines", "gizmo_line: a thick line list depth tested, a dashed strip over everything", GizmoLines, NeedsDepth: true, InView: true ),
		new( "gizmo-sprites", "gizmo_sprite: points as screen- and world-sized sprites, rotated, textured", GizmoSprites, NeedsDepth: true, InView: true ),
		new( "tool-sprites", "tools_sprite: uniform-sized points, with a border", ToolSprites, NeedsDepth: true, InView: true ),
		new( "thick-wireframe", "tools_wireframe with D_THICK_LINES: lines four pixels thick, depth biased", ThickWireframe, NeedsDepth: true, InView: true ),
		new( "tangent-frames", "tools_visualize_tangent_frame: two spheres' normals", TangentFrames, "tangents and bitangents: show_tangent_space.vmat's F_SHOW_TANGENTS is a material feature, and Borealis.Gpu draws a material as its shader", NeedsDepth: true, InView: true ),
		new( "highlight", "ObjectHighlight: a sphere's inside pass and outline", Highlight, NeedsDepth: true, InView: true ),
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

	/// <summary>
	/// A scene finds its systems through the type library, which this lab doesn't make otherwise (as PanelAppSystem makes
	/// one). Its constructor and AddIntrinsicTypes are internal to the engine's app assemblies, hence reflection.
	/// </summary>
	internal static void EnsureTypeLibrary()
	{
		if ( Game.TypeLibrary is not null ) return;

		const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
		var library = (Sandbox.Internal.TypeLibrary)Activator.CreateInstance( typeof( Sandbox.Internal.TypeLibrary ), nonPublic: true );
		typeof( Sandbox.Internal.TypeLibrary ).GetMethod( "AddIntrinsicTypes", any ).Invoke( library, null );
		var add = typeof( Sandbox.Internal.TypeLibrary ).GetMethod( "AddAssembly", any, [typeof( System.Reflection.Assembly ), typeof( bool )] );
		add.Invoke( library, [typeof( Vector3 ).Assembly, false] );
		add.Invoke( library, [typeof( Scene ).Assembly, false] );
		Game.TypeLibrary = library;
	}

	public static int Run( Source2Backend backend, string folder )
	{
		components = Args.Has( "-components" );
		sceneOnGpu = Args.Has( "-scene-gpu" );
		var meanTolerance = Args.Float( "-mean-tolerance", 1.0f );
		if ( components ) EnsureTypeLibrary();

		scene = components ? new Scene() : null;
		var only = Args.String( "-case" );
		var cases = Cases.Where( c => only is null || only.Split( ',' ).Contains( c.Name ) ).ToArray();

		// What reads the view's depth runs over the scene drawn on each device, which has its depth
		foreach ( var test in cases.Where( c => c.NeedsDepth && !sceneOnGpu ) ) Console.WriteLine( $"{test.Name}: not run, it reads the scene's depth (-scene-gpu)" );
		cases = cases.Where( c => !c.NeedsDepth || sceneOnGpu ).ToArray();
		var tolerance = Args.Int( "-tolerance", 2 );
		folder = System.IO.Path.GetFullPath( folder );
		System.IO.Directory.CreateDirectory( folder );

		using var gpu = BorealisGpuRenderBackend.Create( Args.Has( "-validation" ), m => Log.Info( $"Borealis.Gpu: {m.Severity} {m.Text}" ) );
		backend.Phase( $"Borealis.Gpu device ({gpu.Device.Adapter.Name})" );

		// The frame the post stack starts from, made after the Borealis.Gpu device so its texture keeps its pixels for it
		using var frame = SunShadows( backend, folder );
		backend.Phase( "Sun Shadows on Source 2" );
		gpuScene = sceneOnGpu ? new GpuScene( gpu ) : null;
		if ( sceneOnGpu ) backend.Phase( "Sun Shadows on Borealis.Gpu" );

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
		var summary = $"Post parity{(components ? " (components)" : "")}{(sceneOnGpu ? " (scene on Borealis.Gpu)" : "")}: {cases.Length - failed} of {cases.Length} cases within {(sceneOnGpu ? $"{meanTolerance:F2} mean difference" : $"{tolerance}")} on {gpu.Device.Adapter.Name}, Sun Shadows {Width}x{Height} ({folder}); Borealis.Gpu validation errors {errors}";
		Log.Info( summary );
		Console.WriteLine( summary );

		var report = new List<string>
		{
			$"# Post parity on {gpu.Device.Adapter.Name}",
			"",
			summary,
			"",
			"Each case runs a post process's command list over Borealis's Sun Shadows frame on Source 2's device, then on a Borealis.Gpu device through `BorealisGpuRenderBackend`.",
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
		source2Chain?.Dispose();
		dofTiles = null;
		caseStencil?.Dispose();
		caseStencil = null;
		dofArguments = null;
		source2Chain = null;

		if ( scene is not null )
		{
			scene.Destroy();
			scene = null;

			// Made once for every scene, and kept for the process; this one ends here
			ParticleLightingSystem.Unlit.Dispose();
		}
		return failed == 0 && errors == 0 ? 0 : 1;
	}

	// Sun Shadows on Source 2, kept for its depth, which the reference binds with the frame when the scene is on Borealis.Gpu
	static RenderSystem source2Renderer;
	static RenderView labView;
	static ViewTarget source2Target;

	/// <summary>
	/// Sun Shadows rendered by Borealis on Source 2 as two RGBA16F textures of its pixels, for auto exposure: highlights pushed
	/// past 1 as the post stack gets it, and a tenth as bright (<c>frame-dark.png</c>). Make them after the Borealis.Gpu device,
	/// so they keep their pixels for it.
	/// </summary>
	internal static (Texture Bright, Texture Dark) ExposureFrames( Source2Backend backend, string folder )
	{
		var bright = SunShadows( backend, folder );
		var halves = new ushort[Width * Height * 4];
		source2Target.Resolved.Engine().GetPixels( (0, 0, Width, Height), 0, 0, halves.AsSpan(), ImageFormat.RGBA16161616F );
		var dark = FrameTexture( halves, System.IO.Path.Combine( folder, "frame-dark.png" ), 0.1f );
		source2Target.Dispose();
		source2Renderer.Dispose();
		source2Target = null;
		source2Renderer = null;
		return (bright, dark);
	}

	/// <summary>Sun Shadows rendered by Borealis on Source 2, as an RGBA16F texture of its pixels.</summary>
	static Texture SunShadows( Source2Backend backend, string folder )
	{
		var lab = LabWorld.Build( LabPresets.Find( "Sun Shadows" ), backend.Content );
		var view = new RenderView { Name = "Post parity", ClearColor = new Color( 0.08f, 0.09f, 0.11f ), Viewport = new Rect( 0, 0, Width, Height ) };
		lab.Aim( view, LabWorld.DefaultPitch, LabWorld.DefaultYaw, lab.DefaultDistance );
		labView = view;
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
	/// Sun Shadows rendered by Borealis on the Borealis.Gpu device the post stack runs on, from content Borealis.Source2 reads
	/// (no 2D sky there yet), handed to the engine's drawing on that device as a camera frame would be
	/// (<see cref="BorealisGpuFrame"/>): its targets and view constants, no readback.
	/// </summary>
	sealed class GpuScene : IDisposable
	{
		readonly RenderSystem renderer;
		readonly ViewTarget target;
		readonly RenderHost host;

		public BorealisGpuFrame Frame { get; }

		/// <summary>The view constants the scene drew with.</summary>
		public ViewConstants Constants { get; }

		public GpuScene( BorealisGpuRenderBackend gpu )
		{
			var device = gpu.Services;
			host = new Borealis.Backend.BackendRenderHost( device );
			var previous = RenderHost.Current;
			RenderHost.Current = host;
			try
			{

				// Made once the host is the Borealis.Gpu device's, which it records its frames with
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

				Frame = new BorealisGpuFrame( gpu );
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

	static (int Max, int Over, double Mean, string Error) RunCase( Source2Backend backend, BorealisGpuRenderBackend gpu, Texture frame, Case test, string folder, int tolerance )
	{
		backend.BeginFrame();
		string error = null;

		// Source 2
		// The reference is always the copies of the command lists on Source 2: the components' own attributes are drawn
		// with as they are, and Source 2 doesn't layer a block's over them outside a view, so in this block they'd miss the
		// frame grab and view constants there
		var native = test.InView ? InNativeView( backend, test ) : Apply( RenderBackend.Default, frame, test, reference: true );
		g_pRenderDevice.Flush();
		var nativePixels = Read( native, null );
		native.Dispose();
		backend.Phase( $"{test.Name}: Source 2" );

		// Borealis.Gpu
		var borealisPixels = new ushort[Width * Height * 4];
		try
		{
			var borealis = Apply( gpu, frame, test );
			borealisPixels = Read( borealis, gpu );
			borealis.Dispose();
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

		backend.Phase( $"{test.Name}: Borealis.Gpu" );

		var a = Encode( nativePixels );
		var b = Encode( borealisPixels );
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
		Source2Backend.SavePng( b, Width, Height, System.IO.Path.Combine( folder, "borealis.png" ) );
		Source2Backend.SavePng( diff, Width, Height, System.IO.Path.Combine( folder, "diff.png" ) );
		return (max, over, total / (3.0 * a.Length), error);
	}

	/// <summary>A copy of the frame, and the case's command list run over it in a block on <paramref name="device"/>.</summary>
	static Texture Apply( RenderBackend device, Texture frame, Case test, bool reference = false )
	{
		var target = Texture.CreateRenderTarget().WithSize( Width, Height ).WithFormat( ImageFormat.RGBA16161616F ).WithUAVBinding().Create();
		var cl = new CommandList( test.Name );

		// What a camera's view gives its post processing: the scene's own constants when it's drawn on Borealis.Gpu (on both
		// devices, so they draw with the same), else the viewport and target size with no exposure scaling
		var onFrame = gpuScene is not null && device is BorealisGpuRenderBackend;
		if ( gpuScene is not null ) cl.Attributes.SetData( "PerViewConstantBuffer_t", test.View is null ? gpuScene.Constants : test.View( gpuScene.Constants ) );
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

		// The reference's view depth chain, which the Borealis.Gpu frame builds for itself
		if ( reference && caseDepth is not null )
		{
			source2Chain ??= Texture.CreateRenderTarget().WithSize( Width, Height ).WithFormat( ImageFormat.RG3232F ).WithMips( System.Numerics.BitOperations.Log2( Height ) + 1 ).WithUAVBinding().Create( "Post parity depth chain" );
			cl.Attributes.Set( "DepthChainDownsample", source2Chain );
		}
		var lists = components && !reference && test.Components is not null ? ComponentLists( test, device ) : null;
		if ( lists is null ) test.Record( cl );

		// On the GPU scene's frame: a block from the frame, which binds the view's constants itself, over a copy of its colour
		// made on the GPU, with its depth. Source 2 then binds its own Sun Shadows depth with the copy, as a view would (its
		// blocks without a view take no depth target, so it's bound on the context)
		using ( onFrame ? gpuScene.Frame.Begin() : Graphics.Scope.CreateOn( device ) )
		{
			Graphics.RenderTarget = RenderTarget.From( target, onFrame ? gpuScene.Frame.Depth : null );
			if ( !onFrame && caseDepth is not null )
			{
				NativeEngine.RenderTools.BindColorAndDepthTarget( Graphics.Context, target.native, caseDepth.native, false );
				Graphics.DownsampleSpd( Graphics.Context, source2Chain, Graphics.DownsampleMethod.MinMax, fuseDepth: true, depth: caseDepth );
			}
			Graphics.CopyTexture( onFrame ? gpuScene.Frame.Color : frame, target );
			cl.Execute();
			foreach ( var list in lists ?? [] ) list.Execute();
		}

		if ( onFrame ) gpuScene.Frame.End();
		return target;
	}

	/// <summary>
	/// The case's command list run by Source 2 inside a native view: a scene world holding one custom object that runs it,
	/// drawn by a native camera where the scene's camera is, over a grey clear, without MSAA or post processing.
	/// </summary>
	static Texture InNativeView( Source2Backend backend, Case test )
	{
		// The layers of native's pipeline, which the app system installs and this lab otherwise doesn't need
		RenderPipeline.Current ??= (INativeRenderPipeline)Activator.CreateInstance( Type.GetType( "Sandbox.Rendering.Pipeline.NativeRenderPipeline, Sandbox.Rendering", true ), nonPublic: true );

		var world = new SceneWorld();
		var camera = new SceneCamera( "Post parity native view" )
		{
			World = world,
			Position = labView.Position,
			Rotation = labView.Rotation,
			FieldOfView = labView.FieldOfView,
			ZNear = labView.ZNear,
			ZFar = labView.ZFar,
			BackgroundColor = Grey,
			ClearFlags = ClearFlags.All,
			EnablePostProcessing = false,
			AllowCmaa2 = false,
		};
		camera.Attributes.Set( "msaa", (int)NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_NONE );

		var cl = new CommandList( test.Name );
		cl.Attributes.Set( "BlueNoise", Texture.Load( "textures/dev/blue_noise_256.vtex" ) );
		test.Record( cl );
		_ = new SceneCustomObject( world )
		{
			RenderOverride = _ =>
			{
				inNativeView = true;
				try { cl.Execute(); }
				finally { inNativeView = false; }
			}
		};

		var target = Texture.CreateRenderTarget().WithSize( Width, Height ).WithFormat( ImageFormat.RGBA16161616F ).Create( "Post parity native view" );
		// Queued: native renders it with the next frame's views
		SceneRenderer.Native.RenderToTexture( camera, target, null, default );
		for ( var i = 0; i < 2; i++ )
		{
			backend.BeginFrame();
			backend.EndFrame();
		}
		backend.Flush();

		camera.Dispose();
		world.Delete();
		return target;
	}

	static ushort[] Read( Texture texture, BorealisGpuRenderBackend gpu )
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
