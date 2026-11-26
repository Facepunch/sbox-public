using Borealis;
using Borealis.Backend;
using GpuViewConstants = Borealis.Gpu.ViewConstants;

namespace Sandbox;

/// <summary>
/// A finished Borealis frame on a Borealis.Gpu device, handed to the engine's drawing on that device with no copy: its colour
/// and depth targets as engine <see cref="Texture"/>s whose backings are the frame's own, and what a camera's view gives
/// the drawing after the scene - the frame's view constants, the blue noise texture and bloom's quarter-size input.
/// What a camera frame on Borealis.Gpu is after the scene: post processing and the camera's later stages draw in a block
/// from <see cref="Begin()"/>, which also builds the view's depth chain from the frame's depth, then the frame is presented.
/// </summary>
/// <remarks>
/// The textures keep their engine-side identity from frame to frame; <see cref="Adopt"/> points them at the targets the
/// frame drew into. Layouts are handed over at both ends: the block starts where Borealis left each target, and Borealis's
/// next frame starts where the block left it.
/// </remarks>
internal sealed class BorealisGpuFrame : IDisposable
{
	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );
	static readonly StringToken DepthChainName = new( "DepthChainDownsample" );
	static readonly StringToken FogVolumeName = new( "FogVolume" );
	static readonly StringToken CubemapFogTextureName = new( "CubemapFogTexture" );
	static readonly StringToken BloomInputName = new( "QuarterResEffectsBloomInputTexture" );

	// What a stage or custom object draws inside the frame, apart from lending the frame to it and taking it back
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings InFrameDrawTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu in-frame draw" );

	/// <summary>
	/// The view's lookup tables, as Borealis binds them for its own draws (<c>ViewEnvironment</c>, native's
	/// renderingpipeline_standard.cpp): what draws on the frame read too. Without the BRDF lookup a forward shader drawn
	/// through the engine (Mow the Lawn's terrain) has no environment reflection.
	/// </summary>
	static readonly (StringToken Name, string Path)[] ViewTextureFiles =
	[
		("BlueNoise", "textures/dev/blue_noise_256.vtex"),
		("BRDFLookup", "textures/dev/ggx_integrate_brdf_lut_schlick.vtex"),
		("LtcAmplitudeLookup", "textures/dev/ltc_amp.vtex"),
		("LtcMatrixLookup", "textures/dev/ltc_mat.vtex"),
	];

	// Every block on the frame binds them: looked up once, again only if one was let go
	static readonly Texture[] viewTextures = new Texture[ViewTextureFiles.Length];

	/// <summary>Let go of the view's lookup tables, at shutdown (before the engine's leak check).</summary>
	internal static void ReleaseViewTextures() => Array.Clear( viewTextures );

	internal static void SetViewTextures( RenderAttributes attributes )
	{
		for ( int i = 0; i < ViewTextureFiles.Length; i++ )
		{
			if ( viewTextures[i] is not { IsValid: true } ) viewTextures[i] = Texture.Load( ViewTextureFiles[i].Path );
			attributes.Set( ViewTextureFiles[i].Name, viewTextures[i] );
		}

		// The sheet cache, which sprites read their sheets' frames from (the view environment's SheetTexture lookup)
		if ( RenderBackend.Default is BorealisGpuRenderBackend { SheetCacheTexture: { } sheets } ) attributes.Set( SheetTextureName, sheets );
	}

	static readonly StringToken SheetTextureName = new( "SheetTexture" );
	static readonly StringToken DepthChainPrevFrameName = new( "DepthChainDownsamplePrevFrame" );
	static readonly StringToken NormalsName = new( "NormalsGBuffer" );

	/// <summary>
	/// The view's settings the frame's own draws read from its attributes (specialization constants and view-wide values:
	/// the shadow filter, the tools visualisation, light modulators, DDGI, screen-space effects and normals, a reflection's, the
	/// cubemap fog's tint),
	/// which custom objects drawn in it read too, as native's layer gives its custom objects the view's attributes.
	/// </summary>
	internal static readonly StringToken[] ViewSettings =
	[
		"ShadowFilterQuality", "ToolsVisMode", "LightModulatorsDisabled", "LightModulators_Enabled", "DDGI_Enabled",
		"DisableScreenSpaceEffects", "DisableScreenSpaceShadows", "NormalsTextureIndex", "IsReflection", "ReflectionShadows", "NeedsDepthNormals",
		"CubemapFogColor",
	];

	/// <summary>Set <see cref="ViewSettings"/> the frame has (<paramref name="frame"/>, its attributes) on <paramref name="into"/>.</summary>
	internal static void CopyViewSettings( Borealis.ShaderParameters frame, RenderAttributes into )
	{
		if ( frame is not BackendParameters parameters ) return;
		foreach ( var name in ViewSettings )
		{
			switch ( parameters.Get( name ) )
			{
				case int value: into.Set( name, value ); break;
				case bool value: into.Set( name, value ); break;
				case float value: into.Set( name, value ); break;
				case Vector4 value: into.Set( name, value ); break;
			}
		}
	}

	readonly BorealisGpuRenderBackend backend;
	ViewTarget target;
	byte[] constants = [];

	/// <summary>
	/// The frame's colour target (its resolved one when multisampled); once <see cref="Upscaled"/>, the upscaled colour the
	/// stages after the upscale draw on.
	/// </summary>
	public Texture Color => upscaled ? upscaledColor : SceneColor;

	/// <summary>The frame's depth target; once <see cref="Upscaled"/>, the upscaled depth.</summary>
	public Texture Depth => upscaled ? upscaledDepth : SceneDepth;

	/// <summary>The colour the scene rendered into (resolved when multisampled), at the render size when upscaled.</summary>
	public Texture SceneColor { get; private set; }

	/// <summary>The depth the scene rendered into, at the render size when upscaled.</summary>
	public Texture SceneDepth { get; private set; }

	/// <summary>
	/// The quarter-resolution bloom objects the scene drew (<c>QuarterResEffectsBloomInputTexture</c>, which the bloom effect
	/// adds), or null when it drew none.
	/// </summary>
	public Texture BloomObjects { get; private set; }

	// The Borealis texture behind BloomObjects this frame
	BackendTexture bloomObjects;

	/// <summary>
	/// The overlays' depth the scene drew for a temporal upscaler's reactive mask (<see cref="RenderSystem.ReactiveDepth"/>), or
	/// null when it drew none.
	/// </summary>
	public Texture ReactiveDepth { get; private set; }

	// The Borealis texture behind ReactiveDepth this frame
	BackendTexture reactiveDepth;

	/// <summary>
	/// The view's depth chain (<c>DepthChainDownsample</c>, which <c>Depth::Get</c> reads): the min and max of the frame's
	/// depth, mipped, built by <see cref="Begin()"/> (and from the upscaled depth by <see cref="Upscaled"/>). Null for a frame
	/// with no depth.
	/// </summary>
	public Texture DepthChain => upscaled ? upscaledChain : sceneChain;

	/// <summary>
	/// The view constants the frame draws with, as <c>PerViewConstantBuffer_t</c>'s bytes: the upscaled ones once
	/// <see cref="Upscaled"/>.
	/// </summary>
	public byte[] Constants => upscaled ? upscaledConstants : constants;

	public BorealisGpuFrame( BorealisGpuRenderBackend backend ) => this.backend = backend;

	/// <summary>Whether the frame was drawn multisampled: <see cref="Color"/> is its resolved colour, <see cref="Depth"/> keeps its samples.</summary>
	public bool Multisampled => !upscaled && target is { Samples: > 1 };

	// After an upscale (Upscaled): the display-size colour and depth the upscaler wrote (its own), their chain and constants
	bool upscaled;
	Texture upscaledColor, upscaledDepth, upscaledChain, sceneChain;
	byte[] upscaledConstants = [];
	RenderTarget upscaledTarget;

	// The view's depth range the scene drew in (its constants' ViewportMinZ and ViewportMaxZ), which the stages draw in
	(float Min, float Max) depthRange = (0, 1);

	/// <summary>
	/// Take a frame Borealis rendered into <paramref name="target"/> on the backend's device, with the view constants it drew
	/// with, the bloom objects it drew (<see cref="RenderSystem.BloomObjects"/>) and the overlays' reactive depth
	/// (<see cref="RenderSystem.ReactiveDepth"/>), if any.
	/// </summary>
	public void Adopt( ViewTarget target, in GpuViewConstants viewConstants, GpuTexture bloom = null, GpuTexture reactive = null )
	{
		this.target = target;
		var color = target.Resolved ?? target.Color;
		upscaled = false;
		SceneColor = FrameTexture( SceneColor, color, target.Size, ImageFormat.RGBA16161616F, depth: false );
		SceneDepth = target.Depth is null ? null : FrameTexture( SceneDepth, target.Depth, target.Size, ImageFormat.D32FS8, depth: true );

		if ( bloomObjects is not null && !ReferenceEquals( bloomObjects, bloom ) && bloomObjects.IsValid ) backend.ForgetBorrowed( bloomObjects.Handle );
		bloomObjects = bloom as BackendTexture;
		BloomObjects = bloomObjects is null ? null : FrameTexture( null, bloomObjects, new Vector2Int( bloomObjects.Width, bloomObjects.Height ), ImageFormat.RGBA1010102, depth: false );

		if ( reactiveDepth is not null && !ReferenceEquals( reactiveDepth, reactive ) && reactiveDepth.IsValid ) backend.ForgetBorrowed( reactiveDepth.Handle );
		reactiveDepth = reactive as BackendTexture;
		ReactiveDepth = reactiveDepth is null ? null : FrameTexture( null, reactiveDepth, new Vector2Int( reactiveDepth.Width, reactiveDepth.Height ), ImageFormat.D32, depth: true );

		if ( constants.Length != System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>() ) constants = new byte[System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>()];
		System.Runtime.InteropServices.MemoryMarshal.Write( constants, in viewConstants );
		depthRange = viewConstants.ViewportMaxZ > viewConstants.ViewportMinZ ? (viewConstants.ViewportMinZ, viewConstants.ViewportMaxZ) : (0, 1);

		// Made here, outside any block: materials can't be made while drawing
		if ( Multisampled ) depthResolve ??= Material.FromShader( "shaders/depthresolve.shader" );
		depthResolved = false;
	}

	static Material depthResolve;
	Texture resolvedDepth, resolveScratch;
	RenderTarget resolveTarget;
	RenderAttributes resolveAttributes;

	// The multisampled depth was resolved for this frame already (the depth doesn't change after the scene)
	bool depthResolved;

	/// <summary>
	/// The frame's depth with one sample, in the block being recorded: <see cref="Depth"/>, or for a multisampled frame its
	/// first sample written into a depth of its own (<c>depthresolve</c>, as native resolves depth), once a frame, for
	/// drawing depth tested over a single-sampled target. Leaves no target bound that the caller should keep.
	/// </summary>
	public Texture ResolvedDepth()
	{
		if ( Depth is null || !Multisampled ) return Depth;
		if ( depthResolve is null ) return null;
		if ( depthResolved && resolvedDepth is not null ) return resolvedDepth;

		if ( resolvedDepth is null || resolvedDepth.Width != Depth.Width || resolvedDepth.Height != Depth.Height )
		{
			resolvedDepth?.Dispose();
			resolveScratch?.Dispose();
			resolvedDepth = Texture.CreateRenderTarget().WithSize( Depth.Width, Depth.Height ).WithDepthFormat().Create( "Borealis.Gpu frame resolved depth" );
			// The resolve writes depth alone; the engine's drawing always has a colour target
			resolveScratch = Texture.CreateRenderTarget().WithSize( Depth.Width, Depth.Height ).WithFormat( ImageFormat.RGBA8888 ).Create( "Borealis.Gpu depth resolve scratch" );
			resolveTarget = RenderTarget.From( resolveScratch, resolvedDepth );
		}

		resolveAttributes ??= RenderAttributes.CreateManaged();
		resolveAttributes.Set( "SourceDepth", Depth );
		resolveAttributes.SetCombo( "D_MSAA", 1 );
		resolveAttributes.SetCombo( "D_UPSCALE", 0 );
		resolveAttributes.Set( "DownsampleFactor", 1 );

		backend.BindRenderTarget( resolveTarget );
		Graphics.Viewport = new Rect( 0, 0, Depth.Width, Depth.Height );
		Graphics.Clear( global::Color.Transparent, false, true, true );
		Graphics.Blit( depthResolve, resolveAttributes );
		depthResolved = true;
		return resolvedDepth;
	}

	byte[] outputConstants = [];

	/// <summary>
	/// Set the frame's view constants on the block being recorded for drawing over its output: the viewport and target sizes
	/// become <paramref name="viewport"/> in a target of <paramref name="targetSize"/>. The stages after post processing and the
	/// UI draw there, as native's draw with the view's constants.
	/// </summary>
	public void SetOutputConstants( Rect viewport, Vector2 targetSize )
	{
		if ( constants.Length == 0 || targetSize.x < 1 || targetSize.y < 1 ) return;

		var c = System.Runtime.InteropServices.MemoryMarshal.Read<GpuViewConstants>( Constants );
		c.ViewportOffset = new( viewport.Left, viewport.Top );
		c.ViewportSize = new( viewport.Width, viewport.Height );
		c.InvViewportSize = new( 1.0f / viewport.Width, 1.0f / viewport.Height );
		c.RenderTargetSize = new( targetSize.x, targetSize.y );
		c.ViewportToGBufferRatio = new( viewport.Width / targetSize.x, viewport.Height / targetSize.y );
		c.InvGBufferSize = new( 1.0f / targetSize.x, 1.0f / targetSize.y, 1.0f, 1.0f );

		if ( outputConstants.Length != constants.Length ) outputConstants = new byte[constants.Length];
		System.Runtime.InteropServices.MemoryMarshal.Write( outputConstants, in c );
		Graphics.Attributes.SetDataShared( ViewConstantsName, outputConstants );
	}

	// The frame's colour and depth as engine textures, one per Borealis texture: frames alternate between targets, and an engine
	// texture kept per target keeps its backing instead of being given a new one each frame
	readonly Dictionary<(bool Depth, BackendTexture Texture), Texture> frameTextures = new();

	Texture FrameTexture( Texture current, GpuTexture gpu, Vector2Int size, ImageFormat format, bool depth )
	{
		if ( gpu is not BackendTexture texture ) return Engine( current, gpu, size, format, depth );

		if ( frameTextures.Count > 8 )
		{
			foreach ( var (key, engine) in frameTextures )
			{
				if ( key.Texture.IsValid ) continue;
				frameTextures.Remove( key );
				engine.Dispose();
			}
		}

		ref var kept = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault( frameTextures, (depth, texture), out _ );
		kept = Engine( kept, gpu, size, format, depth );
		return kept;
	}

	/// <summary>An engine texture of the target's size whose backing on the device is <paramref name="gpu"/>.</summary>
	Texture Engine( Texture existing, GpuTexture gpu, Vector2Int size, ImageFormat format, bool depth )
	{
		var (handle, layout) = BackendDevice.Unwrap( gpu );
		return Engine( existing, handle, layout, size, format, depth );
	}

	Texture Engine( Texture existing, Borealis.Gpu.TextureHandle handle, Borealis.Gpu.ImageLayout layout, Vector2Int size, ImageFormat format, bool depth )
	{
		if ( existing is null || existing.Width != size.x || existing.Height != size.y || (!depth && existing.ImageFormat != format) )
		{
			existing?.Dispose();
			var builder = Texture.CreateRenderTarget().WithSize( size.x, size.y );
			builder = depth ? builder.WithDepthFormat() : builder.WithFormat( format );
			existing = builder.Create( depth ? "Borealis.Gpu frame depth" : "Borealis.Gpu frame colour" );
		}

		backend.Borrow( existing, handle, layout );
		return existing;
	}

	// A stage's targets inside the frame (RunStage): engine textures kept from frame to frame, one per Borealis texture and
	// slot, so borrowing one for a stage leaves the others' backings as they are instead of remaking a backing each stage
	enum StageSlot { Color, Depth, Chain, Normals, Fog, FogCube }

	readonly Dictionary<(StageSlot Slot, BackendTexture Texture), Texture> stageTextures = new();

	// Per slot, the Borealis texture borrowed last
	readonly BackendTexture[] stageLast = new BackendTexture[Enum.GetValues<StageSlot>().Length];

	// A target per pair of stage textures, made once
	readonly Dictionary<(Texture Color, Texture Depth), RenderTarget> stageTargets = new();

	/// <summary>
	/// The engine texture standing for <paramref name="texture"/> in <paramref name="slot"/>, borrowed in <paramref name="layout"/>.
	/// When a slot moves to another texture, the engine's context stops tracking the one before (it isn't this block's), as
	/// giving its engine texture a new backing did; textures since disposed lose their engine textures.
	/// </summary>
	Texture StageTexture( StageSlot slot, BackendTexture texture, Borealis.Gpu.ImageLayout layout, ImageFormat format, bool depth )
	{
		if ( stageLast[(int)slot] is { } last && !ReferenceEquals( last, texture ) ) backend.ForgetBorrowed( last.Handle );
		stageLast[(int)slot] = texture;

		if ( stageTextures.Count > 8 ) DropDisposedStageTextures();
		ref var engine = ref System.Runtime.InteropServices.CollectionsMarshal.GetValueRefOrAddDefault( stageTextures, (slot, texture), out _ );
		var before = engine;
		engine = Engine( engine, texture.Handle, layout, new Vector2Int( texture.Width, texture.Height ), format, depth );
		if ( before is not null && !ReferenceEquals( before, engine ) ) stageTargets.Clear();
		return engine;
	}

	void DropDisposedStageTextures()
	{
		var dropped = false;
		foreach ( var (key, engine) in stageTextures )
		{
			if ( key.Texture.IsValid ) continue;
			stageTextures.Remove( key );
			engine.Dispose();
			dropped = true;
		}

		if ( dropped ) stageTargets.Clear();
	}

	RenderTarget StageTarget( Texture color, Texture depth )
	{
		if ( stageTargets.TryGetValue( (color, depth), out var known ) ) return known;
		if ( stageTargets.Count > 16 ) stageTargets.Clear();
		return stageTargets[(color, depth)] = RenderTarget.From( color, depth );
	}

	/// <summary>
	/// Draw a camera's stage inside Borealis's frame while it records it (<c>AfterDepthPrepass</c>, <c>AfterOpaque</c>,
	/// <c>AfterSkybox</c>): <paramref name="draw"/> runs in a block that records into the frame's own list at this point of
	/// it, on the stage's targets (the frame's colour, multisampled as the frame is, none for the depth prepass's stage, and
	/// its depth), with the frame's view constants and depth chain, through <paramref name="view"/>. Layouts are handed over
	/// at both ends and the frame binds what it draws with again after. <paramref name="first"/> (the frame's first stage)
	/// clears the frame's pipeline textures before the stage, so ambient occlusion's result is this frame's or none. The
	/// opaque pass's normals and roughness (<see cref="StageTarget.Normals"/>), when the target has them, are the stage's
	/// <c>NormalsGBuffer</c>, read by screen-space reflections' trace.
	/// </summary>
	internal void RunStage( in StageTarget target, in GpuViewConstants viewConstants, BorealisGpuView view, bool first, Action draw )
	{
		if ( stageConstants.Length != System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>() ) stageConstants = new byte[System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>()];
		System.Runtime.InteropServices.MemoryMarshal.Write( stageConstants, in viewConstants );
		RunInFrame( target, stageConstants, view, first, draw );
	}

	byte[] stageConstants = [];

	/// <summary>
	/// As <see cref="RunStage"/>, with the view constants as <c>PerViewConstantBuffer_t</c>'s bytes: also what custom objects
	/// draw through (<see cref="BorealisGpuCustomDraw"/>). <paramref name="depthAlone"/> draws into the target's depth with no
	/// colour, at its slice (a shadow map's, or the depth prepass's), where a stage with no colour binds nothing. Either way the
	/// draw reads <paramref name="cameraAttributes"/> under the view constants.
	/// </summary>
	internal void RunInFrame( in StageTarget target, byte[] viewConstants, BorealisGpuView view, bool first, Action draw, bool depthAlone = false, RenderAttributes cameraAttributes = null )
	{
		if ( depthAlone )
		{
			RunInDepth( target, viewConstants, view, draw, cameraAttributes );
			return;
		}

		var context = (BackendContext)target.Context;
		var list = context.Lend();

		// In a segment recorded beside others, each starts where the stage uses it; the frame moves it there at the list's start
		(Texture Engine, BackendTexture Gpu) Borrow( StageSlot slot, GpuTexture gpu, ImageFormat format, bool depth, Borealis.Gpu.ImageLayout start )
		{
			if ( gpu is not BackendTexture texture ) return default;
			var engine = StageTexture( slot, texture, context.LendLayout( texture, start ), format, depth );
			return (engine, texture);
		}

		// On the async compute queue the depth stays readable, as the frame's handoff left it: moving an attachment there may
		// need a decompression only graphics can do
		var color = Borrow( StageSlot.Color, target.Color, ImageFormat.RGBA16161616F, depth: false, Borealis.Gpu.ImageLayout.ColorTarget );
		var depth = Borrow( StageSlot.Depth, target.Depth, ImageFormat.D32FS8, depth: true, context.AsyncCompute ? Borealis.Gpu.ImageLayout.ShaderRead : Borealis.Gpu.ImageLayout.DepthTarget );
		var chain = Borrow( StageSlot.Chain, ((BackendParameters)target.Attributes).Get( DepthChainName ) as GpuTexture, ImageFormat.RG3232F, depth: false, Borealis.Gpu.ImageLayout.ShaderRead );
		var normals = Borrow( StageSlot.Normals, target.Normals, ImageFormat.RGBA1010102, depth: false, Borealis.Gpu.ImageLayout.ShaderRead );

		// The frame's volumetric fog (or opaque black without it), which the view constants tell forward shaders to read
		var fog = Borrow( StageSlot.Fog, ((BackendParameters)target.Attributes).Get( FogVolumeName ) as GpuTexture, ImageFormat.RGBA16161616F, depth: false, Borealis.Gpu.ImageLayout.ShaderRead );

		// The cubemap fog's cube (a sky's, or black without it), which the view constants tell forward shaders to read,
		// as native's view attributes give its custom objects: without it Goblingeddon's grass was fogged towards black
		var fogCube = Borrow( StageSlot.FogCube, ((BackendParameters)target.Attributes).Get( CubemapFogTextureName ) as GpuTexture, ImageFormat.RGBA16161616F, depth: false, Borealis.Gpu.ImageLayout.ShaderRead );

		view.Target = color.Engine is null ? null : StageTarget( color.Engine, depth.Engine );
		view.DepthSlice = 0;

		// The block's multisampling (the MSAA combos) is its targets': a stage before the resolve reads and draws into the
		// frame's multisampled colour and depth (depth of field's D_MSAA), and the trace its multisampled normals (D_MSAA_NORMALS)
		view.Msaa = (color.Gpu ?? normals.Gpu ?? depth.Gpu) is { Samples: > 1 } multisampled ? multisampled.Samples switch { 2 => MultisampleAmount.Multisample2x, 8 => MultisampleAmount.Multisample8x, _ => MultisampleAmount.Multisample4x } : MultisampleAmount.MultisampleNone;
		view.Viewport = target.Viewport;
		backend.LendNextBlock( list );
		backend.DepthRange = target.MaxZ > target.MinZ ? (target.MinZ, target.MaxZ) : (0, 1);
		try
		{
			using var scope = Graphics.Scope.CreateOnView( backend, view );
			view.Bind();
			if ( first ) backend.ClearPipelineTextures();

			var attributes = Graphics.Attributes;
			cameraAttributes?.MergeTo( attributes );
			attributes.SetDataShared( ViewConstantsName, viewConstants );

			// Under what the block sets: Mow the Lawn's terrain filtered its shadows with none of the frame's filter (hard and dark)
			CopyViewSettings( target.Attributes, attributes );
			if ( chain.Engine is not null )
			{
				attributes.Set( DepthChainName, chain.Engine );
				attributes.Set( DepthChainPrevFrameName, chain.Engine );
			}

			if ( normals.Engine is not null ) attributes.Set( NormalsName, normals.Engine );
			if ( fog.Engine is not null ) attributes.Set( FogVolumeName, fog.Engine );
			if ( fogCube.Engine is not null ) attributes.Set( CubemapFogTextureName, fogCube.Engine );

			SetViewTextures( attributes );
			attributes.Set( BloomInputName, Texture.Black );
			using ( InFrameDrawTiming.Scope() )
				draw();
		}
		finally
		{
			// A block that threw before taking the list leaves it lent: later frames would take themselves as inside it
			backend.LendNextBlock( null );

			// Back to the frame, where the block left them, and no longer the engine's to track; the engine textures keep their
			// backings for the next block on these textures
			foreach ( var (engine, gpu) in (ReadOnlySpan<(Texture, BackendTexture)>)[color, depth, chain, normals, fog, fogCube] )
			{
				if ( engine is null ) continue;
				context.SetLayout( gpu, backend.LayoutOf( engine ) );
				backend.ForgetBorrowed( gpu.Handle );
			}

			backend.DepthRange = (0, 1);
			view.Msaa = MultisampleAmount.MultisampleNone;
			context.Reclaim();
		}
	}

	// Depth targets drawn into alone (RunInDepth), one per Borealis texture (shadow maps, the prepass's depth): engine textures
	// kept from frame to frame with their backings, each borrowed for a run, and a target of each. Those whose Borealis texture
	// was destroyed (a world's shadow maps, after a map change) go as the next one is made
	readonly Dictionary<BackendTexture, (Texture Texture, RenderTarget Target)> depthAlone = new();

	/// <summary>
	/// <see cref="RunInFrame"/> into <paramref name="target"/>'s depth alone, at its slice: what a custom object in a shadow map
	/// or the depth prepass draws into. The frame's view constants for that view, no depth chain (depth passes don't read it).
	/// </summary>
	void RunInDepth( in StageTarget target, byte[] viewConstants, BorealisGpuView view, Action draw, RenderAttributes cameraAttributes )
	{
		if ( target.Depth is not BackendTexture gpu ) return;

		var context = (BackendContext)target.Context;
		var list = context.Lend();
		if ( !depthAlone.TryGetValue( gpu, out var kept ) )
		{
			DropDisposedDepthTextures();
			var texture = Texture.CreateRenderTarget().WithSize( gpu.Width, gpu.Height ).WithDepthFormat().Create( "Borealis.Gpu depth drawn alone" );
			depthAlone[gpu] = kept = (texture, RenderTarget.FromDepth( texture ));
		}

		backend.Borrow( kept.Texture, gpu.Handle, context.LendLayout( gpu, Borealis.Gpu.ImageLayout.DepthTarget ) );
		view.Target = kept.Target;
		view.DepthSlice = target.DepthSlice;
		view.Viewport = target.Viewport;
		backend.LendNextBlock( list );
		backend.DepthRange = target.MaxZ > target.MinZ ? (target.MinZ, target.MaxZ) : (0, 1);
		var keptWritable = false;
		try
		{
			using var scope = Graphics.Scope.CreateOnView( backend, view );
			try
			{
				view.Bind();
				var attributes = Graphics.Attributes;
				cameraAttributes?.MergeTo( attributes );
				attributes.SetDataShared( ViewConstantsName, viewConstants );
				draw();
			}
			finally
			{
				// FinishReadable makes the engine's textures readable for bindless use. This attachment belongs to the
				// frame, which transitions it at the pass that reads it. Hand back writable depth without the round trip.
				// If the custom draw left it in another layout, let the block finish it as usual.
				if ( backend.Context.LayoutOf( gpu.Handle ) == Borealis.Gpu.ImageLayout.DepthTarget )
				{
					backend.Context.Finish();
					backend.ForgetBorrowed( gpu.Handle );
					keptWritable = true;
				}
			}
		}
		finally
		{
			backend.LendNextBlock( null );
			if ( keptWritable )
			{
				context.SetLayout( gpu, Borealis.Gpu.ImageLayout.DepthTarget );
				// The engine's rendering scope ended. A subsequent attachment load/write still depends on its writes.
				context.BarrierToDepthWrite( gpu );
			}
			else
			{
				context.SetLayout( gpu, backend.LayoutOf( kept.Texture ) );
				backend.ForgetBorrowed( gpu.Handle );
			}
			backend.DepthRange = (0, 1);
			view.Target = null;
			view.DepthSlice = 0;
			context.Reclaim();
		}
	}

	void DropDisposedDepthTextures()
	{
		foreach ( var (gpu, kept) in depthAlone )
		{
			if ( gpu.IsValid ) continue;
			depthAlone.Remove( gpu );
			kept.Texture.Dispose();
		}
	}

	/// <summary>
	/// A render block on the frame: its targets bound, its depth chain built, and what the camera's view gives its drawing
	/// set on the block. Dispose it to submit; then call <see cref="End"/>.
	/// </summary>
	public Graphics.Scope Begin() => Begin( null );

	// The frame's targets as the block binds them; the textures are the frame's, so nothing to dispose
	RenderTarget frameTarget;

	/// <summary>
	/// A render block on the frame as <see cref="Begin()"/>'s, which a camera's stages draw in: <paramref name="view"/> is
	/// what they ask about the frame, and drawing into a render target of their own goes back to the frame's targets after.
	/// </summary>
	public Graphics.Scope Begin( BorealisGpuView view )
	{
		frameTarget = StageTarget( Color, Multisampled ? null : Depth );
		if ( view is not null ) view.Target = frameTarget;
		var scope = Graphics.Scope.CreateOnView( backend, view );
		try
		{
			// The stages draw in the view's depth range, as native's hook layers take the view's viewport: depth of field's
			// back layer places its quad at the focus plane and depth tests it against the scene's depth in that range
			backend.DepthRange = depthRange;

			// A multisampled frame hands over its resolved colour, and its depth resolved to one sample to go with it: what the
			// stages draw is depth tested against the scene, and what reads the bound depth (depth of field's
			// RenderValue.DepthTarget, depth grabs) reads it. The view chain is built from every sample
			var timed = backend.BeginTimed( "Post depth resolve" );
			var oneSample = Multisampled ? ResolvedDepth() : null;
			backend.EndTimed( timed );
			if ( oneSample is { } resolved )
			{
				frameTarget = StageTarget( Color, resolved );
				if ( view is not null ) view.Target = frameTarget;
			}

			if ( view is null ) Graphics.RenderTarget = frameTarget;
			else view.Bind();

			var attributes = Graphics.Attributes;
			attributes.SetDataShared( ViewConstantsName, constants );

			// What reads the view's depth through Depth::Get; the previous frame's aliases it, as Borealis's does. Built when
			// something in the block first reads it: a camera's stages after the scene mostly don't
			if ( Depth is not null )
			{
				backend.DeferDepthChain( Depth, sceneChain = Chain( sceneChain, Depth.Width, Depth.Height ) );
				attributes.Set( DepthChainName, sceneChain );
				attributes.Set( DepthChainPrevFrameName, sceneChain );
			}

			SetViewTextures( attributes );
			attributes.Set( BloomInputName, BloomObjects ?? Texture.Black );
			return scope;
		}
		catch
		{
			scope.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Count <paramref name="exposure"/>'s luminance bucket for this frame over its colour, in the block from
	/// <see cref="Begin()"/> before tonemapping, as a camera's stages do on Source 2. The exposure it moves to
	/// (<see cref="AutoExposure.FrameUpdate"/>, before the scene) is the next frames' <c>ToneMapScalar</c>. <paramref name="bucket"/>:
	/// the one <see cref="AutoExposure.Reserve"/> picked as the frame began, as native picks it when its pipeline adds the layer.
	/// </summary>
	public void CountExposure( AutoExposure exposure, int bucket = -1 ) => backend.CountExposure( exposure, SceneColor, new Rect( 0, 0, SceneColor.Width, SceneColor.Height ), Math.Max( target?.Samples ?? 1, 1 ), bucket );

	/// <summary>
	/// In the block from <see cref="Begin(BorealisGpuView)"/>, once an upscaler has written <paramref name="color"/> and
	/// <paramref name="depth"/> at the display size from the scene's targets: they are the frame's from now on, for the stages
	/// after the upscale, with <paramref name="viewConstants"/> (the display-size viewport and the unjittered projection),
	/// bound at <paramref name="view"/>'s viewport, with the depth chain built again from them. The scene's own targets still
	/// go back to Borealis at <see cref="End"/>, and auto exposure counts the scene's colour, as native's measures the frame
	/// as it rendered.
	/// </summary>
	public void Upscaled( Texture color, Texture depth, in GpuViewConstants viewConstants, BorealisGpuView view )
	{
		upscaled = true;
		upscaledColor = color;
		upscaledDepth = depth;
		if ( upscaledConstants.Length != System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>() ) upscaledConstants = new byte[System.Runtime.CompilerServices.Unsafe.SizeOf<GpuViewConstants>()];
		System.Runtime.InteropServices.MemoryMarshal.Write( upscaledConstants, in viewConstants );

		if ( upscaledTarget is null || upscaledTarget.ColorTarget != color || upscaledTarget.DepthTarget != depth )
		{
			upscaledTarget?.Dispose();
			upscaledTarget = RenderTarget.From( color, depth );
		}

		frameTarget = upscaledTarget;
		if ( view is null ) Graphics.RenderTarget = frameTarget;
		else
		{
			view.Target = frameTarget;
			view.Bind();
		}

		var attributes = Graphics.Attributes;
		attributes.SetDataShared( ViewConstantsName, upscaledConstants );
		backend.BuildDepthChain( depth, upscaledChain = Chain( upscaledChain, depth.Width, depth.Height ) );
		attributes.Set( DepthChainName, upscaledChain );
		attributes.Set( DepthChainPrevFrameName, upscaledChain );
	}

	/// <summary>An RG32F depth chain of this size with every mip down to the smaller side's last texel.</summary>
	static Texture Chain( Texture existing, int width, int height )
	{
		if ( existing is not null && existing.Width == width && existing.Height == height ) return existing;
		existing?.Dispose();
		var mips = System.Numerics.BitOperations.Log2( (uint)Math.Min( width, height ) ) + 1;
		return Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RG3232F ).WithMips( mips ).WithUAVBinding().Create( "Borealis.Gpu frame depth chain" );
	}

	/// <summary>Give the frame's targets back to Borealis, in the layouts the drawing left them in.</summary>
	public void End()
	{
		if ( target is null ) return;
		BackendDevice.SetLayout( target.Resolved ?? target.Color, backend.LayoutOf( SceneColor ) );
		if ( target.Depth is not null ) BackendDevice.SetLayout( target.Depth, backend.LayoutOf( SceneDepth ) );
		if ( bloomObjects is not null ) BackendDevice.SetLayout( bloomObjects, backend.LayoutOf( BloomObjects ) );
		if ( reactiveDepth is not null ) BackendDevice.SetLayout( reactiveDepth, backend.LayoutOf( ReactiveDepth ) );
	}

	/// <summary>
	/// The engine's drawing is done with the frame (its output is drawn): its targets go back to Borealis where that left them,
	/// and the engine no longer tracks them, so no later block moves them behind Borealis's back. The next frame borrows them
	/// again (<see cref="Adopt"/>).
	/// </summary>
	public void Release()
	{
		End();

		// Its engine textures keep their backings for the next frame on these targets
		if ( (target?.Resolved ?? target?.Color) is BackendTexture color ) backend.ForgetBorrowed( color.Handle );
		else if ( SceneColor is not null ) backend.Release( SceneColor );
		if ( target?.Depth is BackendTexture depth ) backend.ForgetBorrowed( depth.Handle );
		else if ( SceneDepth is not null ) backend.Release( SceneDepth );
		if ( bloomObjects is not null ) backend.ForgetBorrowed( bloomObjects.Handle );
		bloomObjects = null;
		BloomObjects = null;
		if ( reactiveDepth is not null ) backend.ForgetBorrowed( reactiveDepth.Handle );
		reactiveDepth = null;
		ReactiveDepth = null;
		target = null;
	}

	public void Dispose()
	{
		foreach ( var engine in frameTextures.Values ) engine.Dispose();
		if ( SceneColor is not null && !frameTextures.ContainsValue( SceneColor ) ) SceneColor.Dispose();
		if ( SceneDepth is not null && !frameTextures.ContainsValue( SceneDepth ) ) SceneDepth.Dispose();
		frameTextures.Clear();
		sceneChain?.Dispose();
		upscaledChain?.Dispose();
		upscaledTarget?.Dispose();
		upscaledTarget = null;
		upscaled = false;
		foreach ( var engine in stageTextures.Values ) engine.Dispose();
		stageTextures.Clear();
		stageTargets.Clear();
		Array.Clear( stageLast );
		resolvedDepth?.Dispose();
		resolveScratch?.Dispose();
		foreach ( var (texture, _) in depthAlone.Values ) texture.Dispose();
		depthAlone.Clear();
		SceneColor = SceneDepth = sceneChain = upscaledChain = upscaledColor = upscaledDepth = resolvedDepth = resolveScratch = null;
	}
}
