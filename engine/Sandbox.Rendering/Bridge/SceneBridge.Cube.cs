using Borealis.Backend;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Cube captures on Borealis.Gpu (<c>-borealisgpu</c>): environment map probes, realtime and baked.
/// </summary>
internal sealed partial class SceneBridge
{
	/// <summary>
	/// The six faces cube captures render with, each a render system of its own, shared by every capture: a probe's camera is
	/// new for each capture, and six render systems made for each would cost more than the capture.
	/// </summary>
	readonly CameraState[] captureFaces = new CameraState[6];

	/// <summary>
	/// The sun cascades the faces draw into in turn, rather than a set each: a face reads its cascades only in its own frame, and
	/// six sets were six times the driver allocations (2-3 ms each) in the first frames each face renders after a load.
	/// </summary>
	Borealis.Shadows.SharedShadowTargets captureShadows;

	/// <summary>The face target, copy and filter of captures of each size.</summary>
	readonly Dictionary<int, CubeCapture> cubeCaptures = new();

	/// <summary>
	/// A cube capture (an envmap probe) on the Borealis.Gpu device, as native's six face views
	/// (<c>CCameraRenderer::RenderToCubeTexture</c>): mip 0 cleared to red, then six 90-degree faces of the camera's view, each
	/// a Borealis frame of its own (<see cref="RenderView.Capture"/>, exposure 1, no stages) copied into its slice as it
	/// finishes, so later faces see the earlier ones; then <c>CubemapRendering</c>'s filter at the quality it was made with.
	/// The cube is the engine texture's capture (<see cref="MirrorContent.CaptureCube"/>), which probes draw with. Its faces submit
	/// lists of their own, so a capture asked for inside a render block or one of our frames renders the same way, before what
	/// the block records. A camera with stage hooks of its own (the DDGI bake's) gets its after-transparent stage once each
	/// face is in its slice (<see cref="RunCaptureFaceStages"/>); no work after the faces runs other than the filter.
	/// </summary>
	public void RenderToCube( SceneCamera camera, Texture cube, in ViewSetup setup, CubemapRendering.GGXFilterType? filter = null )
	{
		if ( cube is null || Gpu is null || MirrorContent.Gpu is not { } device ) return;

		var hooked = camera.OnRenderStageHook is not null;
		if ( !camera.World.IsValid() ) return;
		if ( cube.Depth != 6 ) throw new Exception( "Expected a texture with 6 depth slices for RenderToCubeTexture" );

		var size = cube.Width;
		if ( size <= 0 || MirrorContent.CaptureCube( cube ) is not { } target ) return;

		// Captures run first in the engine's frame: its device frame starts before the faces record, not inside one
		Gpu.FollowFrame();

		// A capture spread over frames is finished first: its faces wait in the targets this one would draw into
		ContinueSpreadCapture( double.MaxValue );

		camera.OnPreRender( new Vector2( size, size ) );
		var view = setup;

		if ( !cubeCaptures.TryGetValue( size, out var capture ) ) cubeCaptures[size] = capture = new CubeCapture( device, size );
		var quality = capture.CanFilter ? (int?)filter : null;

		// The envmap scheduler's: faces while its time lasts, the rest in later frames, the cube keeping its last capture meanwhile.
		// With the faces left for the frame's drawing (FacesInDrawing) none renders here
		if ( SpreadCaptureMs >= 0 && !hooked )
		{
			// The camera the spread capture renders from, kept for every spread capture: it's made and let go of only on the
			// main thread, and its faces may render on the render thread
			var copy = spreadCamera ??= new SceneCamera( "Spread cube capture" ) { FieldOfView = 90 };
			copy.ZNear = camera.ZNear;
			copy.ZFar = camera.ZFar;
			copy.Position = camera.Position;
			copy.Rotation = camera.Rotation;
			copy.World = camera.World;
			copy.ExcludeTags.SetFrom( camera.ExcludeTags );
			spread = new SpreadCapture( copy, target, capture, view, quality, size );
			if ( !FacesInDrawing ) ContinueSpreadCapture( SpreadCaptureMs );
			return;
		}

		capture.Render( target, ( i, faceTarget ) => RenderCaptureFace( device, camera, view, size, i, faceTarget ), quality,
			hooked ? ( i, faceTarget ) => RunCaptureFaceStages( camera, i, faceTarget ) : null );
	}

	// The engine's drawing on a capture face, for a camera's stages (RunCaptureFaceStages)
	BorealisGpuFrame captureFrame;
	BorealisGpuView captureView;

	/// <summary>
	/// A capture camera's stages on face <paramref name="i"/>, once it's in the cube's slice: its after-transparent stage in a
	/// block on the face's frame (its colour and depth, its view constants), as native runs a cube face's stages in its view.
	/// The DDGI bake's hook copies each face's depth (<see cref="Graphics.DepthTexture"/>) and integrates the probe after the
	/// sixth, which reads the whole cube.
	/// </summary>
	void RunCaptureFaceStages( SceneCamera camera, int i, ViewTarget faceTarget )
	{
		if ( RenderBackend.Default is not BorealisGpuRenderBackend backend || captureFaces[i] is not { } state ) return;

		var frame = captureFrame ??= new BorealisGpuFrame( backend );
		var view = captureView ??= new BorealisGpuView( backend );
		frame.Adopt( faceTarget, state.System.Frame.Constants );
		var face = state.View;
		view.Viewport = face.Viewport;
		view.SrgbWrite = false;
		view.PostProcessEnabled = false;
		view.LayerType = SceneLayerType.Translucent;
		view.RenderSceneObject = null;
		view.SetCamera( face.Position, face.Rotation, face.FieldOfView, face.ZNear, face.ZFar, 1, face.Orthographic, face.OrthoSize );
		view.CubeFace = i;
		try
		{
			using ( frame.Begin( view ) )
				RunStage( camera, Stage.AfterTransparent );
		}
		finally
		{
			view.CubeFace = -1;
			frame.Release();
		}
	}

	/// <summary>
	/// Set by the envmap scheduler around a capture it starts: spread it over frames, rendering faces for this many
	/// milliseconds now (at least one) and the rest when it continues (<see cref="ContinueSpreadCapture"/>). Negative: whole now.
	/// </summary>
	internal double SpreadCaptureMs { get; set; } = -1;

	/// <summary>A capture spread over frames: a copy of its camera, its cube and the next face to render.</summary>
	sealed record SpreadCapture( SceneCamera Camera, GpuTexture Target, CubeCapture Capture, ViewSetup View, int? Filter, int Size )
	{
		public int Next { get; set; }
	}

	SpreadCapture spread;

	// The spread captures' camera (RenderToCube), and the delegate their faces render through, which reads the capture
	SceneCamera spreadCamera;
	Action<int, ViewTarget> renderSpreadFace;

	/// <summary>Whether a capture is spread over frames and not finished.</summary>
	internal bool CaptureSpread => spread is not null;

	/// <summary>
	/// Whether a spread capture's faces wait for the game window's drawing (<see cref="DrawCaptures"/>, on the render thread
	/// when the frame is handed to it) rather than rendering where the capture starts or continues. Set by the envmap scheduler.
	/// </summary>
	internal bool FacesInDrawing { get; set; }

	/// <summary>
	/// Milliseconds of the spread capture's faces the next drawing renders (<see cref="DrawCaptures"/>; at least one face when
	/// above zero). Set by the envmap scheduler, on the main thread, for the frame it hands over.
	/// </summary>
	internal double DrawingBudgetMs { get; set; }

	// Milliseconds the drawings spent on faces since the scheduler last took them (TakeDrawnMs)
	double drawnMs;

	/// <summary>Milliseconds the drawings spent on faces since this was last called, on the main thread after the render thread's join.</summary>
	internal double TakeDrawnMs()
	{
		var ms = drawnMs;
		drawnMs = 0;
		return ms;
	}

	/// <inheritdoc/>
	public void DrawCaptures()
	{
		var budget = DrawingBudgetMs;
		DrawingBudgetMs = 0;
		if ( spread is null || budget <= 0 ) return;

		// A face that throws drops its capture, not the window's frame drawn after it
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		try
		{
			ContinueSpreadCapture( budget );
		}
		catch ( Exception e ) when ( e is not Borealis.Gpu.GpuException { IsDeviceLost: true } )
		{
			if ( spread is not null ) EndSpreadCapture();
			if ( !warnedDrawnCapture )
			{
				warnedDrawnCapture = true;
				Log.Warning( e, $"Borealis.Gpu: an envmap capture's face threw in the frame's drawing, so the capture was dropped: {e.Message}" );
			}
		}

		drawnMs += System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	bool warnedDrawnCapture;

	/// <summary>
	/// Render the spread capture's next faces for up to <paramref name="ms"/> milliseconds (at least one), and once it has all
	/// six, copy them into its cube and filter it. Its milliseconds.
	/// </summary>
	internal double ContinueSpreadCapture( double ms )
	{
		if ( spread is not { } s || MirrorContent.Gpu is null ) return 0;
		if ( !s.Target.IsValid || s.Camera.World is null )
		{
			EndSpreadCapture();
			return 0;
		}

		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		Gpu?.FollowFrame();
		s.Camera.OnPreRender( new Vector2( s.Size, s.Size ) );
		renderSpreadFace ??= RenderSpreadFace;
		while ( s.Next < 6 )
		{
			s.Capture.RenderFace( s.Next, renderSpreadFace );
			s.Next++;
			if ( System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds >= ms ) break;
		}

		if ( s.Next == 6 )
		{
			s.Capture.FinishFaces( s.Target, s.Filter );
			EndSpreadCapture();
		}

		return System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	void RenderSpreadFace( int i, ViewTarget faceTarget )
	{
		var s = spread;
		RenderCaptureFace( MirrorContent.Gpu, s.Camera, s.View, s.Size, i, faceTarget, parallel: FacesInDrawing );
	}

	// The spread capture is done: its camera keeps no world alive meanwhile
	void EndSpreadCapture()
	{
		spread.Camera.World = null;
		spread = null;
	}

	/// <summary>One face of a capture: the camera's view turned to it, 90 degrees, no UI, into <paramref name="faceTarget"/>.</summary>
	void RenderCaptureFace( BackendDevice device, SceneCamera camera, ViewSetup view, int size, int i, ViewTarget faceTarget, bool parallel = false )
	{
		var face = view;
		face.Transform = new Transform( camera.Position, camera.Rotation * SceneCamera.CubeRotations[i] );
		face.FieldOfView = 90;
		face.EnableUI = false;

		// Recorded on this thread, as the lab's captures are: a face is a small frame, and six back to back in one engine
		// frame gain nothing from workers. A face of a capture spread over the window's drawings (FacesInDrawing) is one or two
		// a frame, on the thread drawing the window, and records on workers as the window's frame does
		// The faces' render systems draw one world: what they keep from a frame (lists, plans, tables) is that world's, and kept
		// across a map change (KeepCaptures) it would point at the last map's freed buffers
		if ( captureWorld != camera.World ) FreeCaptureFaces( camera.World );
		var made = captureFaces[i] is null;
		using var zone = (made ? Borealis.Zones.CaptureFaceMade : Borealis.Zones.CaptureFace).Start();
		var state = captureFaces[i] ??= new CameraState( camera );
		state.System.ShadowMaps.Shared = captureShadows ??= new Borealis.Shadows.SharedShadowTargets();
		state.System.ParallelRecording = parallel && RenderSystem.ParallelRecordingDefault;
		lastCaptureFace = Application.FrameCount;
		lastCaptureFaceTicks = System.Diagnostics.Stopwatch.GetTimestamp();
		state.Camera.SetTarget( camera );
		// What preparing the world loads (a map's textures and lightmaps at the first capture after a load) written with the face's
		// submission rather than each submitted and waited for
		WorldRender render;
		CameraState prepared;
		using ( device.Gpu.DeferUploads() )
			(render, prepared) = Prepare( camera, face, new Vector2( size, size ), state );
		prepared.View.Capture = true;
		prepared.View.ToneMapScalar = 1;
		using ( new Drawing( this, prepared ) )
			prepared.System.Render( render.World, prepared.View, faceTarget, null );
	}

	/// <summary>Free the capture faces and their copies and filters.</summary>
	// The engine frame a capture last rendered a face in, for freeing the faces' systems once captures stop
	ulong lastCaptureFace;
	long lastCaptureFaceTicks;

	// The world the capture faces' render systems last drew
	SceneWorld captureWorld;

	/// <summary>The world the capture faces' render systems were made for, or null with none made. For tests.</summary>
	internal SceneWorld CaptureFacesWorld => captureFaces[0] is null ? null : captureWorld;

	void FreeCaptureFaces( SceneWorld world )
	{
		captureWorld = world;
		for ( int i = 0; i < captureFaces.Length; i++ )
		{
			captureFaces[i]?.Dispose();
			captureFaces[i] = null;
		}

		captureShadows?.Dispose();
		captureShadows = null;
	}

	/// <summary>
	/// Captures are still to come (the scene has envmap probes): the capture faces stay made rather than being freed as idle,
	/// so the next capture doesn't make their render systems and targets again (the driver's allocations, a few milliseconds a
	/// face).
	/// </summary>
	internal void KeepCaptures()
	{
		if ( lastCaptureFace == 0 ) return;
		lastCaptureFace = Application.FrameCount;
		lastCaptureFaceTicks = System.Diagnostics.Stopwatch.GetTimestamp();
	}

	/// <summary>Release the capture faces' render systems and targets once they go idle.</summary>
	void FreeIdleCaptures( ulong frame )
	{
		if ( spread is not null || lastCaptureFace == 0 || !CaptureIdlePolicy.Expired( frame, lastCaptureFace, lastCaptureFaceTicks ) ) return;
		lastCaptureFace = 0;
		FreeCaptureFaces( null );
		foreach ( var (_, capture) in cubeCaptures ) capture.Dispose();
		cubeCaptures.Clear();
		FreeCaptureStages();
	}

	void FreeCaptureStages()
	{
		captureFrame?.Dispose();
		captureFrame = null;
		captureView?.Dispose();
		captureView = null;
	}

	void ShutdownCaptures()
	{
		spread = null;
		spreadCamera?.Dispose();
		spreadCamera = null;
		FreeCaptureFaces( null );
		foreach ( var (_, capture) in cubeCaptures ) capture.Dispose();
		cubeCaptures.Clear();
		FreeCaptureStages();
	}
}
