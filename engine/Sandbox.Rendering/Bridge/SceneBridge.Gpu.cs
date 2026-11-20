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

	// Where a camera's frame in a Borealis.Gpu window goes, in the dev tools' timings
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings PrepareTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu camera prepare" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings SceneTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu camera scene" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings PostTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu camera post" );
	static readonly Sandbox.Diagnostics.PerformanceStats.Timings OutputTiming = Sandbox.Diagnostics.PerformanceStats.Timings.Get( "Borealis.Gpu camera output" );

	// What a camera's stages on Borealis.Gpu ask of their frame; stages run one camera at a time, so one does
	BorealisGpuView gpuView;

	// A target per output texture (a window's swap chain images, a scene panel's texture), made once, going with the texture
	readonly System.Runtime.CompilerServices.ConditionalWeakTable<Texture, RenderTarget> outputTargets = new();

	RenderTarget OutputTarget( Texture texture )
	{
		if ( outputTargets.TryGetValue( texture, out var known ) ) return known;
		var made = RenderTarget.From( texture );
		outputTargets.AddOrUpdate( texture, made );
		return made;
	}
	Material copyMaterial;
	RenderAttributes copyAttributes;

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
		var rect = camera.Rect;
		var viewport = new Rect( rect.Left * renderSize.x, rect.Top * renderSize.y, rect.Width * renderSize.x, rect.Height * renderSize.y );

		var backend = (BorealisGpuRenderBackend)RenderBackend.Default;
		gpuView ??= new BorealisGpuView( backend );
		gpuView.Viewport = viewport;
		gpuView.SrgbWrite = false;
		gpuView.PostProcessEnabled = setup.EnablePostprocessing ?? camera.EnablePostProcessing;
		gpuView.ToolsVisMode = ToolsVisModeOf( camera );
		gpuView.SetCamera( camera, viewport.Size );
		gpuView.RenderSceneObject = null;
		gpuView.LightingWorld = null;

		// The main view outside the tools upscales as the player's settings ask, as native's does
		var frame = camera.UIOnly || NotYetInBorealis( camera, setup ) is not null ? null : RenderScene( backend, camera, renderSize, window.Samples, setup, upscale: !Application.IsEditor && !camera.ToolsView, inWindow: true );

		// A frame on screen has drawn its world: a game's scene isn't loaded ahead any more (BeginFrame)
		if ( frame is not null && !GameLoading( Sandbox.Engine.GlobalContext.Game?.ActiveScene ) ) RenderFor( camera.World ).Shown = true;

		// Made outside any block: materials can't be made while drawing
		copyMaterial ??= Material.FromShader( "shaders/copytexture.shader" );

		var ui = setup.EnableUI ?? camera.RenderUI;
		gpuView.Target = OutputTarget( window.Target );
		gpuView.Viewport = viewport;
		using var _ = OutputTiming.Scope();
		var outer = windowOutput;
		windowOutput = (camera, frame, ui);
		try
		{
			window.Draw( gpuView, drawWindowOutput ??= DrawWindowOutput );
		}
		finally
		{
			windowOutput = outer;
		}

		frame?.Release();
	}

	// What the window's output block draws, which the one delegate handed to it reads, so a frame allocates no closure
	(SceneCamera Camera, BorealisGpuFrame Frame, bool Ui) windowOutput;
	Action drawWindowOutput;

	void DrawWindowOutput() => DrawOutput( windowOutput.Camera, windowOutput.Frame, windowOutput.Ui, encode: true, false, false );

	/// <summary>
	/// The scene of a camera as <see cref="RenderInto"/> renders it for the window, without drawing it anywhere: what its first
	/// frame makes (resident lists, child views, pipelines, targets, texture sizes asked for) is made now.
	/// </summary>
	void RenderUnseen( BorealisGpuRenderBackend backend, SceneCamera camera, Vector2 size, int samples, in ViewSetup setup )
	{
		if ( size.x < 1 || size.y < 1 || camera.UIOnly || NotYetInBorealis( camera, setup ) is not null ) return;

		camera.OnPreRender( size );
		var rect = camera.Rect;
		var viewport = new Rect( rect.Left * size.x, rect.Top * size.y, rect.Width * size.x, rect.Height * size.y );
		gpuView ??= new BorealisGpuView( backend );
		gpuView.Viewport = viewport;
		gpuView.SrgbWrite = false;
		gpuView.PostProcessEnabled = setup.EnablePostprocessing ?? camera.EnablePostProcessing;
		gpuView.ToolsVisMode = ToolsVisModeOf( camera );
		gpuView.SetCamera( camera, viewport.Size );
		gpuView.RenderSceneObject = null;
		gpuView.LightingWorld = null;
		RenderScene( backend, camera, size, samples, setup, upscale: !Application.IsEditor && !camera.ToolsView )?.Release();
	}

	/// <summary>
	/// Render a camera into an engine texture on Borealis.Gpu (a scene panel's), as native's texture render: its scene and stages
	/// as into a window, without multisampling, then the frame copied into the texture (mirrored as the setup asks) and the
	/// stages that draw into the output. An 8-bit texture is written through its sRGB view, as Source 2 writes one: encoded,
	/// with the frame's alpha kept (a scene panel's transparent background).
	/// </summary>
	void RenderIntoTexture( BorealisGpuRenderBackend backend, SceneCamera camera, Texture target, Vector2 size, in ViewSetup setup )
	{
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		if ( size.x < 1 || size.y < 1 || NotYetInBorealis( camera, setup ) is not null ) return;

		camera.ConfigureView( setup );
		var viewport = new Rect( 0, 0, size.x, size.y );
		gpuView ??= new BorealisGpuView( backend );
		gpuView.Viewport = viewport;
		gpuView.SrgbWrite = false;
		gpuView.PostProcessEnabled = setup.EnablePostprocessing ?? camera.EnablePostProcessing;
		gpuView.ToolsVisMode = ToolsVisModeOf( camera );
		gpuView.SetCamera( camera, size );

		var frame = RenderScene( backend, camera, size, 1, setup );
		copyMaterial ??= Material.FromShader( "shaders/copytexture.shader" );

		var ui = setup.EnableUI ?? camera.RenderUI;
		var flipX = setup.FlipX == true;
		var flipY = setup.FlipY == true;
		gpuView.Target = OutputTarget( target );
		gpuView.Viewport = viewport;
		gpuView.SrgbWrite = !Source2Formats.From( target.ImageFormat ).IsFloat();
		using var _ = OutputTiming.Scope();
		using ( Graphics.Scope.CreateOnView( backend, gpuView ) )
		{
			gpuView.Bind();
			DrawOutput( camera, frame, ui, encode: false, flipX, flipY );
		}

		gpuView.SrgbWrite = false;
		frame.Release();
	}

	/// <summary>
	/// A camera's output in the block being recorded, its target bound: the frame copied in (or the background, without one),
	/// then the stages that draw over the finished frame. <paramref name="encode"/> is for an 8-bit target, bound through a
	/// linear view: the copy and the UI write gamma-encoded output into it, so the UI blends in gamma space like a browser.
	/// </summary>
	void DrawOutput( SceneCamera camera, BorealisGpuFrame frame, bool ui, bool encode, bool flipX, bool flipY )
	{
		// A depth grab over the output (a game's own post processing at AfterPostProcess) takes the frame's depth, which the
		// output's target doesn't have
		var backend = Gpu;
		if ( backend is not null && frame is not null )
		{
			// One sample, as the grab's target has (made for the stages after the scene already, or now, binding its own target)
			backend.OutputDepth = frame.ResolvedDepth();
			if ( frame.Multisampled ) gpuView.Bind();
		}

		try
		{
			DrawOutputStages( camera, frame, ui, encode, flipX, flipY );
		}
		finally
		{
			if ( backend is not null ) backend.OutputDepth = null;
		}
	}

	void DrawOutputStages( SceneCamera camera, BorealisGpuFrame frame, bool ui, bool encode, bool flipX, bool flipY )
	{
		if ( frame is not null )
		{
			CopyFrame( frame, encode, flipX, flipY );

			// What draws over the output reads the view's constants, sized for the output (pp_color's g_vRenderTargetSize)
			var output = gpuView.Target?.ColorTarget;
			frame.SetOutputConstants( gpuView.Viewport, output is null ? gpuView.Viewport.Size : new Vector2( output.Width, output.Height ) );
		}
		else if ( (camera.ClearFlags & ClearFlags.Color) != 0 ) Graphics.Clear( camera.BackgroundColor, true, false, false );

		// The view's lookup tables, as the scene's blocks have them: film grain reads the blue noise
		BorealisGpuFrame.SetViewTextures( Graphics.Attributes );

		// Early UI draws here too, over the finished frame, not before post processing
		Graphics.Attributes.Set( UIGammaOutput, encode );
		if ( ui && camera.Attributes.GetBool( "enableEarlyUI" ) ) RunStage( camera, Stage.EarlyUI );

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
		if ( frame is not null ) DrawOverlays( frame );
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
	BorealisGpuFrame RenderScene( BorealisGpuRenderBackend backend, SceneCamera camera, Vector2 size, int samples, in ViewSetup setup, bool upscale = false, bool inWindow = false )
	{
		// The view's viewport at the display size: the stages inside the scene's frame move it to theirs
		var display = new Vector2Int( (int)size.x, (int)size.y );
		var displayViewport = gpuView.Viewport;
		var upscaler = upscale ? StateFor( camera, setup.ViewHash ).GpuUpscaler ??= new BorealisGpuUpscaler( backend ) : null;
		var upscaling = upscaler?.Begin( display, allowed: true, debugReactiveMask: gpuView.ToolsVisMode == (int)SceneCameraDebugMode.ReactiveMask ) ?? false;
		upscaler?.Track( camera.Position, camera.Rotation, camera.World );
		if ( upscaling )
		{
			size = upscaler.RenderSize;
			if ( upscaler.SingleSampled ) samples = 1;
			BorealisGpuUpscaler.Prepare();
		}

		WorldRender render;
		CameraState state;
		using ( PrepareTiming.Scope() )
			(render, state) = Prepare( camera, setup, size );

		// A temporal upscaler's sub-pixel offset this frame, which the view's projection takes, and its reactive mask's
		// overlays drawn depth only; neither otherwise
		state.View.Jitter = upscaling ? upscaler.ClipJitter : Vector2.Zero;
		state.View.ReactiveDepth = upscaling && upscaler.IsTemporal;

		// Graphics.Render in its stages (a highlight's outline) draws the camera's mirrored objects again
		gpuView.RenderSceneObject = state.Drawer.RenderOnGpu;

		// Graphics.SetupLighting in its stages lights from the probe volumes its view shows
		gpuView.LightingWorld = render.World;
		gpuView.LightingSubWorlds = state.View.SubWorlds;

		// Auto exposure is the port on this device: its frame update here, where native's runs, its scale this frame's
		var exposure = camera.Tonemap?.GpuExposure( backend );
		exposure?.FrameUpdate( RealTime.NowDouble );
		state.View.ToneMapScalar = exposure?.Scale ?? 1.0f;

		// The frame's bucket is picked now, as native picks it when its pipeline adds the frame's AutoExposure layer
		var exposureBucket = exposure?.Reserve() ?? -1;

		// Its planar reflection first, with its exposure: it depends on nothing in the frame
		RenderPlanarReflectionOnGpu( camera, render, state, setup );

		var targetSize = new Vector2Int( (int)size.x, (int)size.y );
		samples = Math.Max( samples, 1 );
		if ( state.GpuTarget is null || state.GpuTarget.Size != targetSize || state.GpuTarget.Samples != samples )
		{
			state.GpuTarget?.Dispose();
			state.GpuTarget = ViewTarget.Create( targetSize, samples, TextureFormat.RGBA16161616F, state.System.DepthFormat, "Borealis.Gpu camera" );
		}

		// The stages before the scene is finished draw inside its frame; ambient occlusion runs in its stage, not early
		var frame = state.GpuFrame ??= new BorealisGpuFrame( backend );
		var stages = state.GpuStages ??= new GpuCameraStages { Run = RunStage };
		stages.Camera = camera;
		stages.System = state.System;
		stages.Frame = frame;
		stages.View = gpuView;
		camera.ScheduleEarlyPostProcess( Stage.AfterDepthPrepass, false );

		// Dynamic objects its overlay layers reach wait for the output (DrawOverlays)
		BorealisGpuCustomDraw.ForgetOverlays( state.View );
		overlayView = state.View;

		using ( SceneTiming.Scope() )
		using ( new Drawing( this, state ) )
			state.System.Render( render.World, state.View, state.GpuTarget, stages, inWindow );

		frame.Adopt( state.GpuTarget, state.System.Frame.Constants, state.System.BloomObjects, state.System.ReactiveDepth );

		gpuView.LayerType = SceneLayerType.Translucent;
		if ( upscaling ) gpuView.Viewport = state.View.Viewport;

		using var _ = PostTiming.Scope();
		using ( frame.Begin( gpuView ) )
		{
			RunStage( camera, Stage.AfterTransparent );
			RunStage( camera, Stage.AfterViewmodel );

			// Upscaled before post processing, as native's: the stages after draw at the display size, unjittered
			if ( upscaling ) Upscale( upscaler, frame, state.View, displayViewport );

			RunStage( camera, Stage.BeforePostProcess );
			if ( exposure is not null && exposureBucket >= 0 ) frame.CountExposure( exposure, exposureBucket );
			RunStage( camera, Stage.Tonemapping );

			// Native's debug view of the reactive mask, over the frame
			if ( upscaling && gpuView.ToolsVisMode == (int)SceneCameraDebugMode.ReactiveMask ) upscaler.DrawReactiveMask( displayViewport );
		}

		frame.End();
		return frame;
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

	/// <summary>
	/// Render a child of the frame being drawn (a command list's <c>DrawView</c>, a water body's <c>DrawRefraction</c>) for
	/// <see cref="RenderChild"/>: prepared, recorded and submitted now, so it reaches the GPU before the parent's lists. It draws
	/// into targets of its own the engine texture's size and format, which
	/// Borealis's draws then read in place of <paramref name="target"/> (and <paramref name="depthTarget"/>), through
	/// <see cref="MirrorContent.SetCapture"/>. It takes the parent's exposure and doesn't count into it: native's camera
	/// histogram counts its child views too, which Borealis leaves out on purpose. The engine draws nothing in it: the graphics
	/// context is the parent's (custom objects, dynamic objects and command lists aren't drawn, nor the camera's stages).
	/// </summary>
	bool RenderChildOnGpu( SceneCamera camera, in ViewSetup setup, Texture target, Texture depthTarget, out CameraState state )
		=> RenderOnGpu( camera, setup, target, depthTarget, rendering.View.ToneMapScalar, out state );

	/// <summary>
	/// Render a view on the Borealis.Gpu device into targets of its own for <paramref name="target"/> and
	/// <paramref name="depthTarget"/> (<see cref="RenderChildOnGpu"/>), with <paramref name="toneMapScalar"/> as its exposure.
	/// </summary>
	bool RenderOnGpu( SceneCamera camera, in ViewSetup setup, Texture target, Texture depthTarget, float toneMapScalar, out CameraState state )
	{
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		state = null;
		if ( target.Width < 1 || target.Height < 1 ) return false;

		(var render, state) = Prepare( camera, setup, target.Size );
		state.View.ToneMapScalar = toneMapScalar;

		var size = new Vector2Int( target.Width, target.Height );
		var format = Source2Formats.From( target.ImageFormat );
		if ( format == TextureFormat.Unknown ) format = TextureFormat.RGBA8888;
		using var drawing = new Drawing( this, state );

		// An 8-bit texture gets the frame blended in HDR first, as a texture render on Source 2's device
		if ( !format.IsFloat() )
		{
			if ( state.GpuOutput is not { } output || output.Size != size || output.Format != format )
			{
				state.GpuOutput?.Dispose();
				state.GpuOutput = output = RenderHost.Current.Device.CreateTexture( TextureDesc.Target( size.x, size.y, format ), "Borealis.Gpu child view" );
			}

			state.System.Render( render.World, state.View, output, 1, null );
			MirrorContent.Gpu.MakeReadable( output );
			MirrorContent.SetCapture( target, output );
			return true;
		}

		// A float one renders straight into its colour, multisampled as the caller's depth is
		var samples = depthTarget is null ? 1 : Source2Formats.Samples( depthTarget.MultisampleType );
		if ( state.GpuTarget is not { } drawn || drawn.Size != size || drawn.Samples != samples || drawn.Color.Format != format )
		{
			// The other size's target, kept for views that alternate sizes
			(state.GpuTarget, state.GpuOtherTarget) = (state.GpuOtherTarget, state.GpuTarget);
			if ( state.GpuTarget is not { } other || other.Size != size || other.Samples != samples || other.Color.Format != format )
			{
				state.GpuTarget?.Dispose();
				state.GpuTarget = ViewTarget.Create( size, samples, format, state.System.DepthFormat, "Borealis.Gpu child view" );
			}

			drawn = state.GpuTarget;
		}

		state.System.Render( render.World, state.View, drawn, null );

		// What reads them samples them
		var gpu = MirrorContent.Gpu;
		gpu.MakeReadable( drawn.Resolved );
		MirrorContent.SetCapture( target, drawn.Resolved );
		if ( depthTarget is not null )
		{
			gpu.MakeReadable( drawn.Depth );
			MirrorContent.SetCapture( depthTarget, drawn.Depth );
		}
		return true;
	}

	/// <summary>
	/// A top-level view's planar reflection, as native's pipeline renders one for its views (<see cref="PlanarReflections"/>): the
	/// plane covering the most of its screen, mirrored, as a reflection view of its own, rendered before the view into targets
	/// of its own that the view's attributes name for the planes' materials, with the view's exposure. Reflections and
	/// orthographic views have none.
	/// </summary>
	void RenderPlanarReflectionOnGpu( SceneCamera camera, WorldRender render, CameraState state, in ViewSetup setup )
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
		camera.ConfigureView( reflection );
		var rendered = RenderOnGpu( camera, reflection, target.ColorTarget, target.DepthTarget, view.ToneMapScalar, out var reflected );

		// The reflection's prepare set the world's 3D skybox up for its view
		PrepareSkybox( render, camera, setup );
		if ( !rendered ) return;

		var attributes = state.GpuViewAttributes ??= RenderAttributes.CreateManaged();
		attributes.Clear();
		PlanarReflections.Publish( attributes, planned, ViewDataFor( reflected, target.ColorTarget ) );
		view.ViewAttributes = MirrorContent.ViewParameters( attributes, ref state.GpuViewParameters );
	}

	// The view of the scene the output is drawing, whose overlays wait for it
	RenderView overlayView;

	/// <summary>
	/// The dynamic objects the scene's overlay layers reached (gizmos, debug overlays), after post processing as native draws
	/// its overlay layers: those depth tested against the frame's depth, single-sampled for the output.
	/// </summary>
	void DrawOverlays( BorealisGpuFrame frame )
	{
		var view = overlayView;
		overlayView = null;
		if ( view is null || !BorealisGpuCustomDraw.HasOverlays( view, out var withDepth ) ) return;

		var depth = !withDepth ? null : frame.Multisampled ? frame.ResolvedDepth() : frame.Depth;
		if ( depth is not null && frame.Multisampled ) gpuView.Bind();
		BorealisGpuCustomDraw.DrawOverlays( view, gpuView, depth );
	}

	/// <summary>
	/// The frame's colour into the bound target's viewport (<c>copytexture.shader</c>), encoded to sRGB when
	/// <paramref name="encode"/>, mirrored as asked.
	/// </summary>
	void CopyFrame( BorealisGpuFrame frame, bool encode, bool flipX, bool flipY )
	{
		copyAttributes ??= RenderAttributes.CreateManaged();
		copyAttributes.Set( CopySource, frame.Color );
		copyAttributes.Set( FlipXName, flipX );
		copyAttributes.Set( FlipYName, flipY );
		copyAttributes.SetCombo( SrgbTransfer, encode ? 1 : 0 );
		Graphics.Blit( copyMaterial, copyAttributes );
	}

	readonly HashSet<(int, Stage)> stageFailures = new();

	/// <summary>A camera's stage, in the block being recorded. One that throws is logged once, and the frame goes on.</summary>
	void RunStage( SceneCamera camera, Stage stage )
	{
		try
		{
			((IManagedCamera)camera).OnRenderStage( stage );
		}
		catch ( Exception e )
		{
			if ( stageFailures.Add( (camera._cameraId, stage) ) ) Log.Warning( e, $"Borealis.Gpu: {camera.Name}'s {stage} stage: {e.Message}" );
		}
	}
}
