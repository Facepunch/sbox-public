using Sandbox.Rendering;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// A camera's frame on Borealis.Gpu (<c>-borealisgpu</c>): into a game window, an engine texture, or as a child of another.
/// </summary>
internal sealed partial class SceneBridge
{
	static readonly StringToken UIGammaOutput = new( "UIGammaOutput" );
	static readonly StringToken CopySource = new( "CustomTexture1" );
	static readonly StringToken SrgbTransfer = new( "D_SRGB_TRANSFER" );
	static readonly StringToken FlipXName = new( "FlipX" );
	static readonly StringToken FlipYName = new( "FlipY" );
	static readonly StringToken UberEffectsName = new( "UberEffects" );
	static readonly StringToken UberSource = new( "ColorBuffer" );

	// Where a camera's frame in a Borealis.Gpu window goes, in the dev tools' timings
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings PrepareTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu camera prepare" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings SceneTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu camera scene" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings PostTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu camera post" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings OutputTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu camera output" );

	// The editor can have two frames recording. Their targets and mutable stage scratch must not overlap.
	sealed class OutputScratch : IDisposable
	{
		public BorealisGpuView View;
		public RenderView UIOnlyView;
		public RenderAttributes CopyAttributes;
		public RenderAttributes UberAttributes;
		public Texture BitmapTarget;
		public Texture LentTexture;
		public (SceneCamera Camera, BorealisGpuFrame Frame, bool Ui, RenderView Overlay) WindowOutput;
		public readonly HashSet<(int, Stage)> StageFailures = new();

		public void ClearRouting()
		{
			if ( View is null ) return;
			View.RenderSceneObject = null;
			View.LightingWorld = null;
		}

		public void Dispose()
		{
			View?.Dispose();
			BitmapTarget?.Dispose();
			LentTexture?.Dispose();
		}
	}

	OutputScratch mainOutput, renderOutput;
	Material copyMaterial, uberMaterial;
	OutputScratch Output => ThreadSafe.IsRenderThread ? renderOutput ??= new() : mainOutput ??= new();
	BorealisGpuView gpuView => Output.View;

	// A target per output texture (a window's swap chain images, a scene panel's texture), made once, going with the texture
	readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture, RenderTarget> outputTargets = new();

	RenderTarget OutputTarget( Texture texture )
	{
		if ( outputTargets.TryGetValue( texture, out var known ) ) return known;
		var made = RenderTarget.From( texture );
		outputTargets.AddOrUpdate( texture, made );
		return made;
	}

	/// <summary>
	/// Render a camera into the frame <paramref name="window"/> is drawing, as a swap chain render does on Source 2's device:
	/// <list type="number">
	/// <item>its scene, by Borealis on the Borealis.Gpu device, into a float target of its own (with no stages inside it yet);</item>
	/// <item>that frame handed to the engine's drawing (<see cref="BorealisGpuFrame"/>), where the stages after the scene and
	/// post processing run, auto exposure counts its bucket, and tonemapping;</item>
	/// <item>the frame copied into the camera's part of the window, encoded as an sRGB swap chain stores it;</item>
	/// <item>the stages that draw into the output: after post processing, the UI, after the UI.</item>
	/// </list>
	/// A camera without a scene (UI only) clears its part of the window instead.
	/// </summary>
	void RenderInto( BorealisGpuWindow window, SceneCamera camera, Vector2? size, in ViewSetup setup )
	{
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		var renderSize = size ?? window.Size;
		if ( renderSize.x < 1 || renderSize.y < 1 ) return;
		warmSize = window.Size;
		warmSamples = window.Samples;

		camera.OnPreRender( renderSize );
		window.CameraPosition = camera.Position;
		var viewport = PixelViewport( camera, renderSize );
		var backend = (BorealisGpuRenderBackend)RenderBackend.Default;

		// The main view outside the tools upscales as the player's settings ask, as native's does
		BorealisGpuFrame frame = null;
		RenderView overlay = null;
		if ( !camera.UIOnly && camera.World.IsValid() )
		{
			frame = RenderScene( backend, camera, renderSize, window.Samples, setup, out var state, upscale: !Application.IsEditor && !camera.ToolsView, inWindow: true, destination: window );
			overlay = state.View;
		}
		else
		{
			var view = Output.UIOnlyView ??= new RenderView();
			FillView( view, camera, setup, renderSize );
			PrepareStageView( backend, camera, setup, view );
			gpuView.RenderSceneObject = null;
			gpuView.LightingWorld = null;
		}

		// A frame on screen has drawn its world: a game's scene isn't loaded ahead any more (BeginFrame)
		if ( frame is not null && !GameLoading( Sandbox.Engine.GlobalContext.Game?.ActiveScene ) ) RenderFor( camera.World ).Shown = true;

		// Made outside any block: materials can't be made while drawing
		copyMaterial ??= Material.FromShader( "shaders/copytexture.shader" );
		uberMaterial ??= Material.FromShader( "shaders/postprocess/pp_uber.shader" );

		var ui = setup.EnableUI ?? camera.RenderUI;
		gpuView.Target = OutputTarget( window.Target );
		gpuView.Viewport = viewport;
		using var _ = OutputTiming.Scope();
		var output = Output;
		var outer = output.WindowOutput;
		output.WindowOutput = (camera, frame, ui, overlay);
		try
		{
			window.Draw( gpuView, drawWindowOutput ??= DrawWindowOutput );
		}
		finally
		{
			output.WindowOutput = outer;
			frame?.Release();
		}
	}

	void PrepareStageView( BorealisGpuRenderBackend backend, SceneCamera camera, in ViewSetup setup, RenderView view )
	{
		Output.View ??= new BorealisGpuView( backend );
		gpuView.Viewport = view.Viewport;
		gpuView.SrgbWrite = false;
		gpuView.PostProcessEnabled = setup.EnablePostprocessing ?? camera.EnablePostProcessing;
		gpuView.ToolsVisMode = view.ToolsVisMode;
		float aspect = view.Viewport.Height > 0 ? view.Viewport.Width / view.Viewport.Height : 1;
		gpuView.SetCamera( view.Position, view.Rotation, view.FieldOfView, view.ZNear, view.ZFar, aspect, view.Orthographic, view.OrthoSize );
	}

	// What the window's output block draws, which the one delegate handed to it reads, so a frame allocates no closure
	Action drawWindowOutput;

	void DrawWindowOutput()
	{
		var (camera, frame, ui, overlay) = Output.WindowOutput;
		DrawOutput( camera, frame, ui, encode: true, false, false, overlay );
	}

	/// <summary>
	/// The scene of a camera as <see cref="RenderInto"/> renders it for the window, without drawing it anywhere: what its first
	/// frame makes (resident lists, child views, pipelines, targets, texture sizes asked for) is made now.
	/// </summary>
	void RenderUnseen( BorealisGpuRenderBackend backend, SceneCamera camera, Vector2 size, int samples, in ViewSetup setup )
	{
		if ( size.x < 1 || size.y < 1 || camera.UIOnly || !camera.World.IsValid() ) return;

		RenderScene( backend, camera, size, samples, setup, out _, upscale: !Application.IsEditor && !camera.ToolsView,
			destination: Sandbox.Engine.GameWindow.Current?.Borealis )?.Release();

		// Never drawn: its tonemap, which the output would have drawn, draws in its stage again next time
		camera.EndOutputPostProcess();
	}

	/// <summary>
	/// Render a camera into an engine texture on Borealis.Gpu (a scene panel's), as native's texture render: its scene and stages
	/// as into a window, without multisampling, then the frame copied into the texture (mirrored as the setup asks) and the
	/// stages that draw into the output. An 8-bit texture is written through its sRGB view, as Source 2 writes one: encoded,
	/// with the frame's alpha kept (a scene panel's transparent background).
	/// </summary>
	bool RenderIntoTexture( BorealisGpuRenderBackend backend, SceneCamera camera, Texture target, Vector2 size, in ViewSetup setup, out ViewData viewData, object destination = null )
	{
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		viewData = default;
		if ( size.x < 1 || size.y < 1 || !camera.World.IsValid() ) return false;

		var draw = setup;
		// The final copy flips color; the scene and its stages draw in unflipped camera coordinates.
		draw.FlipX = draw.FlipY = false;
		var frame = RenderScene( backend, camera, size, 1, draw, out var state, destination: destination ?? target, fullTarget: true, leads: true );
		try
		{
			copyMaterial ??= Material.FromShader( "shaders/copytexture.shader" );
			uberMaterial ??= Material.FromShader( "shaders/postprocess/pp_uber.shader" );
			gpuView.Target = OutputTarget( target );
			gpuView.SrgbWrite = !Source2Formats.From( target.ImageFormat ).IsFloat();
			using var _ = OutputTiming.Scope();
			using ( Graphics.Scope.CreateOnView( backend, gpuView ) )
			{
				gpuView.Bind();
				DrawOutput( camera, frame, setup.EnableUI ?? camera.RenderUI, encode: false, setup.FlipX == true, setup.FlipY == true, state.View );
			}

			viewData = ViewDataFor( state, target, setup.FlipX, setup.FlipY );
			return true;
		}
		finally
		{
			gpuView.SrgbWrite = false;
			frame.Release();
		}
	}

	/// <summary>
	/// A camera's output in the block being recorded, its target bound: the frame copied in (or the background, without one),
	/// then the stages that draw over the finished frame. <paramref name="encode"/> is for an 8-bit target, bound through a
	/// linear view: the copy and the UI write gamma-encoded output into it, so the UI blends in gamma space like a browser.
	/// </summary>
	void DrawOutput( SceneCamera camera, BorealisGpuFrame frame, bool ui, bool encode, bool flipX, bool flipY, RenderView overlay )
	{
		// A depth grab over the output (a game's own post processing at AfterPostProcess) takes the frame's depth, which the
		// output's target doesn't have
		var backend = Gpu;
		var previousDepth = backend?.OutputDepth;
		if ( backend is not null && frame is not null )
		{
			// One sample, as the grab's target has (made for the stages after the scene already, or now, binding its own target)
			backend.OutputDepth = frame.ResolvedDepth();
			if ( frame.Multisampled ) gpuView.Bind();
		}

		try
		{
			DrawOutputStages( camera, frame, ui, encode, flipX, flipY, overlay );
		}
		finally
		{
			if ( backend is not null ) backend.OutputDepth = previousDepth;
			camera.EndOutputPostProcess();
		}
	}

	void DrawOutputStages( SceneCamera camera, BorealisGpuFrame frame, bool ui, bool encode, bool flipX, bool flipY, RenderView overlay )
	{
		// The view's lookup tables, as the scene's blocks have them: film grain reads the blue noise
		BorealisGpuFrame.SetViewTextures( Graphics.Attributes );
		var earlyUI = ui && camera.Attributes.GetBool( "enableEarlyUI" );

		if ( frame is not null )
		{
			// What draws over the output reads the view's constants, sized for the output (pp_uber's g_vRenderTargetSize), the
			// copy's uber post draw too
			var output = gpuView.Target?.ColorTarget;
			var viewport = gpuView.Viewport;
			var targetSize = output is null ? viewport.Size : new Vector2( output.Width, output.Height );
			frame.SetOutputConstants( viewport, targetSize );

			// The first run after post processing joins the copy when it would read the frame's pixels where the copy puts
			// them: unmirrored, the frame the output's size and filling it, with nothing drawn between them (early UI)
			var color = frame.Color;
			var withRun = !flipX && !flipY && !earlyUI && viewport.Left == 0 && viewport.Top == 0
				&& viewport.Width == targetSize.x && viewport.Height == targetSize.y
				&& color.Width == (int)targetSize.x && color.Height == (int)targetSize.y;
			CopyFrame( camera, frame, encode, flipX, flipY, withRun );
		}
		// A window shows opaque whatever its background's alpha: some drivers present a window's alpha
		else if ( (camera.ClearFlags & ClearFlags.Color) != 0 ) Graphics.Clear( encode ? camera.BackgroundColor.WithAlpha( 1 ) : camera.BackgroundColor, true, false, false );

		// Early UI draws here too, over the finished frame, not before post processing
		Graphics.Attributes.Set( UIGammaOutput, encode );
		if ( earlyUI ) RunStage( camera, Stage.EarlyUI );

		// Post processing over the output reads and writes it as on Source 2's sRGB targets: an 8-bit target bound linear for
		// the UI is written through its sRGB view meanwhile, so the effects' linear results are encoded
		Graphics.Attributes.Set( UIGammaOutput, false );
		var srgbWrite = gpuView.SrgbWrite;
		if ( encode )
		{
			gpuView.SrgbWrite = true;
			gpuView.Bind();
		}

		RunStage( camera, Stage.AfterPostProcess );
		if ( frame is not null && overlay is not null ) DrawOverlays( frame, overlay );
		if ( encode )
		{
			gpuView.SrgbWrite = srgbWrite;
			gpuView.Bind();
		}

		Graphics.Attributes.Set( UIGammaOutput, encode );
		if ( ui ) RunStage( camera, Stage.UI );

		// After the UI as native's hook layer draws over its sRGB target: through the sRGB view again, the shaders' linear
		// results encoded (a game's own compositing at AfterUI, RUN IT AGAIN's glare), not gamma-encoded by the UI's
		Graphics.Attributes.Set( UIGammaOutput, false );
		if ( encode )
		{
			gpuView.SrgbWrite = true;
			gpuView.Bind();
		}

		RunStage( camera, Stage.AfterUI );
		if ( encode )
		{
			gpuView.SrgbWrite = srgbWrite;
			gpuView.Bind();
		}
	}

	/// <summary>
	/// The camera's scene rendered by Borealis on the Borealis.Gpu device, then its stages after the scene and its post processing
	/// over that frame. Null if it couldn't render. With <paramref name="samples"/> over one the scene is multisampled, as the
	/// game's anti-aliasing setting asks, and the stages get its resolved colour. With <paramref name="upscale"/> the player's
	/// upscaler runs (<see cref="BorealisGpuUpscaler"/>): the scene and the stages up to the viewmodel's at the render size, the
	/// rest at <paramref name="size"/>. A scene shown in a window (<paramref name="inWindow"/>) gives the dev tools its frame plan
	/// and the debug views their report.
	/// </summary>
	BorealisGpuFrame RenderScene( BorealisGpuRenderBackend backend, SceneCamera camera, Vector2 size, int samples, in ViewSetup setup, out CameraState state, bool upscale = false, bool inWindow = false, object destination = null, bool fullTarget = false, bool leads = false )
	{
		// The view's viewport at the display size: the stages inside the scene's frame move it to theirs
		var display = new Vector2Int( (int)size.x, (int)size.y );
		var displayViewport = PixelViewport( camera, size, fullTarget );
		state = destination is null ? StateFor( camera, setup.ViewHash ) : CaptureStateFor( camera, setup.ViewHash, destination, null, directCapture: false );
		var upscaler = upscale ? state.GpuUpscaler ??= new BorealisGpuUpscaler( backend ) : null;
		var upscaling = upscaler?.Begin( display, allowed: true, debugReactiveMask: ToolsVisModeOf( camera ) == (int)SceneCameraDebugMode.ReactiveMask, temporalAllowed: !camera.DisableAntiAliasing ) ?? false;
		upscaler?.Track( setup.Transform?.Position ?? camera.Position, setup.Transform?.Rotation ?? camera.Rotation, camera.World );
		if ( camera.DisableAntiAliasing ) samples = 1;
		if ( upscaling )
		{
			size = upscaler.RenderSize;
			if ( upscaler.SingleSampled ) samples = 1;
			BorealisGpuUpscaler.Prepare();
		}

		WorldRender render;
		using ( PrepareTiming.Scope() )
			render = Prepare( camera, setup, size, state, fullTarget ).Render;

		PrepareStageView( backend, camera, setup, state.View );
		// Painter still draws in display pixels, mapped to the upscaler's render viewport.
		gpuView.DisplayViewport = upscaling ? displayViewport : null;

		// A texture camera drawn before the frame's window camera draws the sun's cascades for that one, which takes them
		state.View.CascadesFor = leads ? LeadView( camera, state.View, render ) : null;

		// A temporal upscaler's sub-pixel offset this frame, which the view's projection takes, and its reactive mask's
		// overlays drawn depth only; neither otherwise
		state.View.Jitter = upscaling ? upscaler.ClipJitter : Vector2.Zero;
		state.View.ReactiveDepth = upscaling && upscaler.IsTemporal;

		// Objects' own motion resolved from the ids only for a frame whose effects are made of motion (Motion::Get): a temporal
		// upscaler's motion vectors, motion blur. Screen-space reflections (on for every camera) read it too, for the last frame's
		// colour at a hit, and get it when one of those asks; without, camera motion, as before
		state.View.ObjectMotion = upscaling && upscaler.IsTemporal || camera.ReadsMotionVectors;

		// Graphics.Render in its stages (a highlight's outline) draws the camera's mirrored objects again
		gpuView.RenderSceneObject = state.Drawer.RenderOnGpu;

		// Graphics.SetupLighting in its stages lights from the probe volumes its view shows
		gpuView.LightingWorld = render.World;
		gpuView.LightingSubWorlds = state.View.SubWorlds;

		// Histogram and queries belong to the destination's view, not the camera's settings object.
		var exposure = camera.Tonemap.Enabled ? state.Exposure ??= backend.CreateExposure() : null;
		exposure?.SetSettings( BorealisGpuRenderBackend.Settings( camera.Tonemap.Settings ) );
		exposure?.FrameUpdate( RealTime.NowDouble );
		state.View.ToneMapScalar = exposure?.Scale ?? 1.0f;

		// The frame's bucket is picked now, as native picks it when its pipeline adds the frame's AutoExposure layer
		var exposureBucket = exposure?.Reserve() ?? -1;

		// Its planar reflection first, with its exposure: it depends on nothing in the frame
		RenderPlanarReflectionOnGpu( camera, state, setup );

		var targetSize = new Vector2Int( (int)size.x, (int)size.y );
		samples = Math.Max( samples, 1 );
		if ( state.GpuTarget is null || state.GpuTarget.Size != targetSize || state.GpuTarget.Samples != samples )
		{
			state.GpuTarget?.Dispose();
			state.GpuTarget = ViewTarget.Create( targetSize, samples, TextureFormat.RGBA16161616F, state.System.DepthFormat, "Borealis.Gpu camera" );
		}

		// The stages before the scene is finished draw inside its frame. Ambient occlusion runs early on the async compute queue
		// when the frame overlaps compute (GpuCameraStages.RenderEarlyPostProcess), which its stage then skips; else in its stage
		var frame = state.GpuFrame ??= new BorealisGpuFrame( backend );
		var stages = state.GpuStages ??= new GpuCameraStages { Run = RunStage };
		stages.Camera = camera;
		stages.System = state.System;
		stages.Frame = frame;
		stages.View = gpuView;
		stages.PrepareScreenSpaceReflections();

		// Dynamic objects its overlay layers reach wait for the output (DrawOverlays)
		BorealisGpuCustomDraw.ForgetOverlays( state.View );

		try
		{
			using ( SceneTiming.Scope() )
			using ( new Drawing( this, state ) )
				state.System.Render( render.World, state.View, state.GpuTarget, stages, inWindow );
		}
		finally
		{
			stages.Camera = null;
		}

		frame.Adopt( state.GpuTarget, state.System.Frame.Constants, state.System.BloomObjects, state.System.ReactiveDepth, state.System.DepthChain, state.System.ObjectMotion );

		gpuView.LayerType = SceneLayerType.Translucent;
		if ( upscaling ) gpuView.Viewport = state.View.Viewport;

		using var _ = PostTiming.Scope();
		using ( frame.Begin( gpuView ) )
		{
			RunStage( camera, Stage.AfterTransparent );
			RunStage( camera, Stage.AfterViewmodel );

			// Upscaled before post processing, as native's: the stages after draw at the display size, unjittered
			if ( upscaling ) Upscale( upscaler, frame, state.View, displayViewport );
			gpuView.DisplayViewport = null;

			RunStage( camera, Stage.BeforePostProcess );
			if ( exposure is not null && exposureBucket >= 0 )
			{
				var timed = backend.BeginTimed( "Exposure count" );
				frame.CountExposure( exposure, exposureBucket );
				backend.EndTimed( timed );
			}

			// The tonemap drawn by the output copy when nothing after it in the frame reads the frame (CopyFrame): the frame
			// keeps its HDR colour. Not under the reactive mask's debug view, drawn over the tonemapped frame below
			camera.DeferTonemap( allowed: uberMaterial is not null && !(upscaling && gpuView.ToolsVisMode == (int)SceneCameraDebugMode.ReactiveMask) );
			RunStage( camera, Stage.Tonemapping );

			// Native's debug view of the reactive mask, over the frame
			if ( upscaling && gpuView.ToolsVisMode == (int)SceneCameraDebugMode.ReactiveMask ) upscaler.DrawReactiveMask( displayViewport );
		}

		frame.End();
		return frame;
	}

	// The frame's window camera's view as it will draw, for a texture camera drawn before it (LeadView)
	readonly Borealis.RenderView leadView = new();

	/// <summary>
	/// The view the frame's window camera (<see cref="SceneRenderer.Lead"/>) will draw, as <see cref="RenderInto"/> prepares it,
	/// for <paramref name="view"/>, a texture camera drawn before it in the same world, to draw the sun's cascades for. Null
	/// when there's no such camera, or its view can't be told now: a temporal upscaler jitters it. A view that turns out other
	/// than this draws its own cascades (ShadowMapper.SameView).
	/// </summary>
	Borealis.RenderView LeadView( SceneCamera camera, Borealis.RenderView view, WorldRender render )
	{
		var (lead, size) = SceneRenderer.Lead;
		if ( lead is null || lead == camera || lead.World != camera.World || view.Reflection || lead.UIOnly || lead.Ortho ) return null;
		if ( BorealisGpuWindow.Drawing is not { } window ) return null;

		var renderSize = size ?? window.Size;
		if ( renderSize.x < 1 || renderSize.y < 1 ) return null;

		// At the upscaler's render size when it upscales the camera, as it did last frame
		var setup = lead.MainView;
		if ( !Application.IsEditor && !lead.ToolsView && views.TryGetValue( new ViewKey( lead._cameraId, setup.ViewHash ), out var states ) && states.Main?.GpuUpscaler is { } upscaler && upscaler.Active != BorealisGpuUpscaler.Mode.Off )
		{
			if ( upscaler.IsTemporal || upscaler.DisplaySize != new Vector2Int( (int)renderSize.x, (int)renderSize.y ) ) return null;
			renderSize = upscaler.RenderSize;
		}

		FillView( leadView, lead, setup, renderSize, render );
		leadView.SubWorlds = view.SubWorlds;
		leadView.HostView = lead.Attributes;
		leadView.Update();
		return leadView;
	}

	/// <summary>
	/// The frame upscaled into the display size's <paramref name="displayViewport"/>, in its block: the constants after it are
	/// the frame's at the display size with the projection unjittered.
	/// </summary>
	void Upscale( BorealisGpuUpscaler upscaler, BorealisGpuFrame frame, RenderView view, Rect displayViewport )
	{
		var constants = System.Runtime.InteropServices.MemoryMarshal.Read<Borealis.Gpu.ViewConstants>( frame.Constants );
		var display = upscaler.DisplaySize;
		view.FillUpscaledConstants( ref constants, displayViewport, new Vector2( display.x, display.y ) );

		// Native's frustum: a horizontal field of view; the upscalers take the vertical one
		var aspect = view.Viewport.Height > 0 ? view.Viewport.Width / view.Viewport.Height : 1.0f;
		var verticalFov = 2.0f * MathF.Atan( MathF.Tan( view.FieldOfView.DegreeToRadian() * 0.5f ) / aspect );
		upscaler.Upscale( frame, gpuView, displayViewport, constants, view.ZNear, verticalFov );
	}

	readonly record struct CaptureOutput( CameraState State, GpuTexture Color, GpuTexture Depth );

	/// <summary>
	/// Render a child of the frame being drawn (a command list's <c>DrawView</c>, a water body's <c>DrawRefraction</c>) for
	/// <see cref="RenderCapture"/>: prepared and recorded now, and submitted before the parent's sampling lists. It draws
	/// into targets of its own the engine texture's size and format, which
	/// Borealis's draws then read in place of <paramref name="target"/> (and <paramref name="depthTarget"/>), through
	/// <see cref="MirrorContent.SetCapture"/>. It takes the parent's exposure and doesn't count into it: native's camera
	/// histogram counts its child views too, which Borealis leaves out on purpose. The engine draws nothing in it: the graphics
	/// context is the parent's (custom objects, dynamic objects and command lists aren't drawn, nor the camera's stages).
	/// </summary>
	bool RenderOnGpu( SceneCamera camera, in ViewSetup setup, Texture target, Texture depthTarget, float toneMapScalar, out CaptureOutput output, bool child = false, object destination = null )
	{
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		output = default;
		if ( !camera.World.IsValid() || target.Width < 1 || target.Height < 1 ) return false;

		var format = Source2Formats.From( target.ImageFormat );
		if ( format == TextureFormat.Unknown ) return false;
		var state = CaptureStateFor( camera, setup.ViewHash, destination ?? target, depthTarget, directCapture: true );
		var (render, _) = Prepare( camera, setup, target.Size, state, fullTarget: true );
		state.View.ToneMapScalar = toneMapScalar;
		state.System.MayFinishLater = child;

		var size = new Vector2Int( target.Width, target.Height );
		var samples = depthTarget is null ? 1 : Source2Formats.Samples( depthTarget.MultisampleType );
		var useIntermediate = !format.IsFloat();
		var renderFormat = useIntermediate ? TextureFormat.RGBA16161616F : format;
		var depthFormat = depthTarget is null ? state.System.DepthFormat : TextureFormat.D32FS8;
		if ( state.GpuTarget is not { } drawn || drawn.Size != size || drawn.Samples != samples
			|| drawn.Color.Format != renderFormat || drawn.Depth.Format != depthFormat )
		{
			state.GpuTarget?.Dispose();
			drawn = state.GpuTarget = ViewTarget.Create( size, samples, renderFormat, depthFormat, "Borealis.Gpu capture" );
		}

		GpuTexture color;
		using ( new Drawing( this, state ) )
		{
			if ( useIntermediate )
			{
				// Blend in HDR, then resolve into the destination's non-float format. Depth stays with the intermediate.
				if ( state.GpuOutput is not { } resolved || resolved.Size != size || resolved.Format != format )
				{
					state.GpuOutput?.Dispose();
					state.GpuOutput = resolved = RenderHost.Current.Device.CreateTexture( TextureDesc.Target( size.x, size.y, format ), "Borealis.Gpu capture color" );
				}
				color = resolved;
				state.System.Render( render.World, state.View, color, samples, null, intermediate: drawn );
			}
			else
			{
				state.System.Render( render.World, state.View, drawn, null );
				color = drawn.Resolved;
			}
		}

		state.System.AfterSubmit( makeReadable, color );
		var depth = depthTarget is not null ? drawn.Depth : null;
		if ( depth is not null ) state.System.AfterSubmit( makeReadable, depth );
		output = new CaptureOutput( state, color, depth );
		return true;
	}

	static readonly Action<Borealis.GpuTexture> makeReadable = static texture => MirrorContent.Gpu.MakeReadable( texture );

	/// <summary>
	/// A top-level view's planar reflection, as native's pipeline renders one for its views (<see cref="PlanarReflections"/>): the
	/// plane covering the most of its screen, mirrored, as a reflection view of its own, rendered before the view into targets
	/// of its own that the view's attributes name for the planes' materials, with the view's exposure. Reflections and
	/// orthographic views have none.
	/// </summary>
	void RenderPlanarReflectionOnGpu( SceneCamera camera, CameraState state, in ViewSetup setup )
	{
		var view = state.View;
		view.ViewAttributes = null;
		if ( setup.Reflection || view.Orthographic || camera.World is null ) return;

		var eye = new Transform( view.Position, view.Rotation );
		var viewport = view.Viewport;
		var aspect = viewport.Height > 0 ? viewport.Width / viewport.Height : 1;
		var frustum = state.PlanarFrustum ??= new PlanarFrustum();
		frustum.Of( view );
		if ( !PlanarReflections.Plan( camera, eye, view.FieldOfView, aspect, view.ZNear, view.ZFar, viewport.Size, new Visibility( frustum ), out var planned ) )
			return;

		var reflection = CommandList.ReflectionView( eye, view.FieldOfView, aspect, view.ZNear, view.ZFar, planned.Plane, planned.Setup );
		var target = planned.Target;
		if ( !RenderOnGpu( camera, reflection, target.ColorTarget, target.DepthTarget, view.ToneMapScalar, out var output ) ) return;

		var data = ViewDataFor( output.State, target.ColorTarget );
		PlanarReflections.RecordRendered( planned );
		var parameters = state.GpuViewParameters ??= MirrorContent.Gpu.CreateParameters();
		parameters.Set( "PlanarReflectionTexture", output.Color );
		parameters.Set( "PlanarReflectionDepthTexture", output.Depth );
		parameters.Set( "PlanarReflectionProjectionToView", data.ProjectionToView );
		parameters.Set( "PlanarReflectionDepthUvScaleBias", data.DepthUvScaleBias );
		parameters.Set( "PlanarReflectionViewport", data.Viewport );
		parameters.Set( "PlanarReflectionDepthRange", data.DepthRange );
		parameters.Set( "PlanarReflectionPlane", new Vector4( planned.Plane.Normal, planned.Plane.Distance ) );
		parameters.Set( "HasPlanarReflection", true );
		view.ViewAttributes = parameters;
	}

	/// <summary>
	/// The dynamic objects the scene's overlay layers reached (gizmos, debug overlays), after post processing as native draws
	/// its overlay layers: those depth tested against the frame's depth, single-sampled for the output.
	/// </summary>
	void DrawOverlays( BorealisGpuFrame frame, RenderView view )
	{
		if ( !BorealisGpuCustomDraw.HasOverlays( view, out var withDepth ) ) return;

		var depth = !withDepth ? null : frame.Multisampled ? frame.ResolvedDepth() : frame.Depth;
		if ( depth is not null && frame.Multisampled ) gpuView.Bind();
		var timed = Gpu?.BeginTimed( "Output overlays" ) ?? default;
		try
		{
			BorealisGpuCustomDraw.DrawOverlays( view, gpuView, depth );
		}
		finally
		{
			Gpu?.EndTimed( timed );
		}
	}

	/// <summary>
	/// The frame's colour into the bound target's viewport (<c>copytexture.shader</c>), encoded to sRGB when
	/// <paramref name="encode"/>, mirrored as asked. When the camera's tonemap was left to it (<see cref="SceneCamera.DeferTonemap"/>),
	/// or <paramref name="withRun"/> and the first run after post processing can join it, the copy is the uber post shader's
	/// draw of those effects from the frame (<c>pp_uber</c>'s output mode), and their own passes are skipped: the tonemap's
	/// copy and draw of the HDR frame, the run's of the output.
	/// </summary>
	void CopyFrame( SceneCamera camera, BorealisGpuFrame frame, bool encode, bool flipX, bool flipY, bool withRun )
	{
		var output = Output;
		var uberAttributes = output.UberAttributes ??= RenderAttributes.CreateManaged();
		uberAttributes.Clear();
		var effects = uberMaterial is null ? UberPostEffect.None : camera.OutputPostProcess( withRun, uberAttributes );
		if ( effects != UberPostEffect.None )
		{
			effects |= UberPostEffect.Output;
			if ( encode ) effects |= UberPostEffect.Encode;
			uberAttributes.Set( UberEffectsName, (int)effects );
			uberAttributes.Set( UberSource, frame.Color );
			uberAttributes.Set( FlipXName, flipX );
			uberAttributes.Set( FlipYName, flipY );
			BorealisGpuFrame.SetViewTextures( uberAttributes );
			var uberTimed = Gpu?.BeginTimed( "Output uber post" ) ?? default;
			Graphics.Blit( uberMaterial, uberAttributes );
			Gpu?.EndTimed( uberTimed );
			return;
		}

		var copyAttributes = output.CopyAttributes ??= RenderAttributes.CreateManaged();
		copyAttributes.Set( CopySource, frame.Color );
		copyAttributes.Set( FlipXName, flipX );
		copyAttributes.Set( FlipYName, flipY );
		copyAttributes.SetCombo( SrgbTransfer, encode ? 1 : 0 );
		var timed = Gpu?.BeginTimed( "Output copy" ) ?? default;
		Graphics.Blit( copyMaterial, copyAttributes );
		Gpu?.EndTimed( timed );
	}

	/// <summary>A camera's stage, in the block being recorded. One that throws is logged once, and the frame goes on.</summary>
	void RunStage( SceneCamera camera, Stage stage )
	{
		var timed = Gpu?.BeginTimed( StageScopeName( stage ) ) ?? default;
		try
		{
			((IManagedCamera)camera).OnRenderStage( stage );
		}
		catch ( Exception e )
		{
			if ( Output.StageFailures.Add( (camera._cameraId, stage) ) ) Log.Warning( e, $"Borealis.Gpu: {camera.Name}'s {stage} stage: {e.Message}" );
		}
		finally
		{
			Gpu?.EndTimed( timed );
		}
	}

	/// <summary>A stage's pass timer scope (<see cref="BorealisGpuRenderBackend.BeginTimed"/>): named once, not per frame.</summary>
	static string StageScopeName( Stage stage ) => stage switch
	{
		Stage.AfterDepthPrepass => "Stage AfterDepthPrepass",
		Stage.AfterOpaque => "Stage AfterOpaque",
		Stage.AfterSkybox => "Stage AfterSkybox",
		Stage.AfterTransparent => "Stage AfterTransparent",
		Stage.AfterViewmodel => "Stage AfterViewmodel",
		Stage.EarlyUI => "Stage EarlyUI",
		Stage.BeforePostProcess => "Stage BeforePostProcess",
		Stage.Tonemapping => "Stage Tonemapping",
		Stage.AfterPostProcess => "Stage AfterPostProcess",
		Stage.UI => "Stage UI",
		Stage.AfterUI => "Stage AfterUI",
		_ => "Stage",
	};
}
