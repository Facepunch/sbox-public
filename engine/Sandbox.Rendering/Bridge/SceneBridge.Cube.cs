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

	/// <summary>The face target, copy and filter of captures of each size.</summary>
	readonly Dictionary<int, CubeCapture> cubeCaptures = new();

	bool warnedAfterFaces;

	/// <summary>
	/// A cube capture (an envmap probe) on the Borealis.Gpu device, as native's six face views
	/// (<c>CCameraRenderer::RenderToCubeTexture</c>): mip 0 cleared to red, then six 90-degree faces of the camera's view, each
	/// a Borealis frame of its own (<see cref="RenderView.Capture"/>, exposure 1, no stages) copied into its slice as it
	/// finishes, so later faces see the earlier ones; then <c>CubemapRendering</c>'s filter at the quality it was made with.
	/// The cube is the engine texture's capture (<see cref="MirrorContent.CaptureCube"/>), which probes draw with. Its faces submit
	/// lists of their own, so a capture asked for inside a render block or one of our frames renders the same way, before what
	/// the block records. A camera with stage hooks of its own (the DDGI bake's) gets its after-transparent stage once each
	/// face is in its slice (<see cref="RunCaptureFaceStages"/>); no work after the faces runs other than the filter. With
	/// Source 2's device kept (the editor) a realtime probe also captures natively while a native tool's views are drawn
	/// (<see cref="RenderPipeline.ToolViewsDrawn"/>), for them; a cube only Borealis.Gpu has (a bake's) doesn't.
	/// </summary>
	public void RenderToCube( SceneCamera camera, Texture cube, in ViewSetup setup, CommandList afterFaces = null )
	{
		if ( cube is null || Gpu is null || MirrorContent.Gpu is not { } device ) return;

		// With Source 2's device kept (the editor), native's capture too for a native tool's views (Hammer's) while one is open
		var hooked = camera.OnRenderStageHook is not null;
		if ( Graphics.HasSource2Device && RenderPipeline.ToolViewsDrawn && !hooked && !cube.OnBorealisGpuOnly )
			SceneRenderer.Native.RenderToCube( camera, cube, setup, afterFaces );
		if ( NotYetInBorealis( camera, setup ) is not null ) return;
		if ( cube.Depth != 6 ) throw new Exception( "Expected a texture with 6 depth slices for RenderToCubeTexture" );

		var size = cube.Width;
		if ( size <= 0 || MirrorContent.CaptureCube( cube ) is not { } target ) return;

		// Captures run first in the engine's frame: its device frame starts before the faces record, not inside one
		Gpu.FollowFrame();

		int? filter = null;
		if ( afterFaces is not null )
		{
			if ( CubemapRendering.FilterOf( afterFaces ) is { } type ) filter = (int)type;
			else if ( !warnedAfterFaces )
			{
				warnedAfterFaces = true;
				Log.Warning( "Borealis.Gpu: a cube capture's work after its faces isn't run; only CubemapRendering's filter is" );
			}
		}

		// A capture spread over frames is finished first: its faces wait in the targets this one would draw into
		ContinueSpreadCapture( double.MaxValue );

		camera.OnPreRender( new Vector2( size, size ) );
		var view = setup;

		if ( !cubeCaptures.TryGetValue( size, out var capture ) ) cubeCaptures[size] = capture = new CubeCapture( device, size );
		if ( filter is not null && !capture.CanFilter ) filter = null;

		// The envmap scheduler's: faces while its time lasts, the rest in later frames, the cube keeping its last capture meanwhile
		if ( SpreadCaptureMs >= 0 && !hooked )
		{
#pragma warning disable CA2000 // Kept by the spread capture, which disposes it when it finishes
			var copy = new SceneCamera( "Spread cube capture" )
			{
				FieldOfView = 90,
				ZNear = camera.ZNear,
				ZFar = camera.ZFar,
				Position = camera.Position,
				Rotation = camera.Rotation,
				World = camera.World,
				ExcludeFromTextureStreaming = camera.ExcludeFromTextureStreaming,
			};
			copy.ExcludeTags.SetFrom( camera.ExcludeTags );
#pragma warning restore CA2000
			spread = new SpreadCapture( copy, target, capture, view, filter, size );
			ContinueSpreadCapture( SpreadCaptureMs );
			return;
		}

		capture.Render( target, ( i, faceTarget ) => RenderCaptureFace( device, camera, view, size, i, faceTarget ), filter,
			hooked ? ( i, faceTarget ) => RunCaptureFaceStages( camera, size, i, faceTarget ) : null );
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
	void RunCaptureFaceStages( SceneCamera camera, int size, int i, ViewTarget faceTarget )
	{
		if ( RenderBackend.Default is not BorealisGpuRenderBackend backend || captureFaces[i] is not { } state ) return;

		var frame = captureFrame ??= new BorealisGpuFrame( backend );
		var view = captureView ??= new BorealisGpuView( backend );
		frame.Adopt( faceTarget, state.System.Frame.Constants );
		view.Viewport = new Rect( 0, 0, size, size );
		view.SrgbWrite = false;
		view.PostProcessEnabled = false;
		view.LayerType = SceneLayerType.Translucent;
		view.RenderSceneObject = null;
		view.SetCamera( camera.Position, camera.Rotation * SceneCamera.CubeRotations[i], 90, camera.ZNear, camera.ZFar, 1, false, default );
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

	/// <summary>Whether a capture is spread over frames and not finished.</summary>
	internal bool CaptureSpread => spread is not null;

	/// <summary>
	/// Render the spread capture's next faces for up to <paramref name="ms"/> milliseconds (at least one), and once it has all
	/// six, copy them into its cube and filter it. Its milliseconds.
	/// </summary>
	internal double ContinueSpreadCapture( double ms )
	{
		if ( spread is not { } s || MirrorContent.Gpu is not { } device ) return 0;
		if ( !s.Target.IsValid || s.Camera.World is null )
		{
			s.Camera.Dispose();
			spread = null;
			return 0;
		}

		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		Gpu?.FollowFrame();
		s.Camera.OnPreRender( new Vector2( s.Size, s.Size ) );
		while ( s.Next < 6 )
		{
			s.Capture.RenderFace( s.Next, ( i, faceTarget ) => RenderCaptureFace( device, s.Camera, s.View, s.Size, i, faceTarget ) );
			s.Next++;
			if ( System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds >= ms ) break;
		}

		if ( s.Next == 6 )
		{
			s.Capture.FinishFaces( s.Target, s.Filter );
			s.Camera.Dispose();
			spread = null;
		}

		return System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds;
	}

	/// <summary>One face of a capture: the camera's view turned to it, 90 degrees, no UI, into <paramref name="faceTarget"/>.</summary>
	void RenderCaptureFace( BackendDevice device, SceneCamera camera, ViewSetup view, int size, int i, ViewTarget faceTarget )
	{
		var face = view;
		face.Transform = new Transform( camera.Position, camera.Rotation * SceneCamera.CubeRotations[i] );
		face.FieldOfView = 90;
		face.EnableUI = false;

		// Recorded on this thread, as the lab's captures are: a face is a small frame, and six back to back in one engine
		// frame gain nothing from workers
		var state = captureFaces[i] ??= new CameraState( camera ) { System = { ParallelRecording = false } };
		lastCaptureFace = Application.FrameCount;
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

	/// <summary>The capture faces' render systems and targets, once no capture has drawn a face for <see cref="ViewIdleFrames"/> frames.</summary>
	void FreeIdleCaptures( ulong frame )
	{
		if ( spread is not null || lastCaptureFace == 0 || frame - lastCaptureFace <= ViewIdleFrames ) return;
		lastCaptureFace = 0;
		for ( int i = 0; i < captureFaces.Length; i++ )
		{
			captureFaces[i]?.Dispose();
			captureFaces[i] = null;
		}

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
		spread?.Camera.Dispose();
		spread = null;
		for ( int i = 0; i < captureFaces.Length; i++ )
		{
			captureFaces[i]?.Dispose();
			captureFaces[i] = null;
		}

		foreach ( var (_, capture) in cubeCaptures ) capture.Dispose();
		cubeCaptures.Clear();
		FreeCaptureStages();
	}
}
