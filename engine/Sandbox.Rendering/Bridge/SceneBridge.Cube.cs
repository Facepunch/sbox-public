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
	/// the block records. A camera with stage hooks of its own (the DDGI bake's) isn't on Borealis.Gpu yet, nor work after
	/// the faces other than the filter. With Source 2's device kept (the editor) a realtime probe also captures natively
	/// while a native tool's views are drawn (<see cref="RenderPipeline.ToolViewsDrawn"/>), for them; a bake asks for native's
	/// by name (<see cref="SceneRenderer.Native"/>) and reads it back, and a camera with stage hooks (the DDGI bake) captures natively only.
	/// </summary>
	public void RenderToCube( SceneCamera camera, Texture cube, in ViewSetup setup, CommandList afterFaces = null )
	{
		if ( cube is null || Gpu is null || MirrorContent.Gpu is not { } device ) return;

		// With Source 2's device kept (the editor), native's capture for a native tool's views (Hammer's) while one is open,
		// and the DDGI bake's only: it reads each face from its hooks
		if ( Graphics.HasSource2Device && (camera.OnRenderStageHook is not null || RenderPipeline.ToolViewsDrawn) )
		{
			SceneRenderer.Native.RenderToCube( camera, cube, setup, afterFaces );
			if ( camera.OnRenderStageHook is not null ) return;
		}
		if ( NotYetInBorealis( camera, setup ) is not null ) return;

		// The DDGI bake reads each face from its stage hooks, which captures here don't run
		if ( camera.OnRenderStageHook is not null ) return;
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

		camera.OnPreRender( new Vector2( size, size ) );
		var view = setup;

		if ( !cubeCaptures.TryGetValue( size, out var capture ) ) cubeCaptures[size] = capture = new CubeCapture( device, size );
		if ( filter is not null && !capture.CanFilter ) filter = null;

		capture.Render( target, ( i, faceTarget ) =>
		{
			var face = view;
			face.Transform = new Transform( camera.Position, camera.Rotation * SceneCamera.CubeRotations[i] );
			face.FieldOfView = 90;
			face.EnableUI = false;

			// Recorded on this thread, as the lab's captures are: a face is a small frame, and six back to back in one engine
			// frame gain nothing from workers
			var state = captureFaces[i] ??= new CameraState( camera ) { System = { ParallelRecording = false } };
			state.Camera.SetTarget( camera );
			var (mirror, prepared) = Prepare( camera, face, new Vector2( size, size ), state );
			prepared.View.Capture = true;
			prepared.View.ToneMapScalar = 1;
			using ( new Drawing( this, prepared ) )
				prepared.System.Render( mirror.World, prepared.View, faceTarget, null );
		}, filter );
	}

	/// <summary>Free the capture faces and their copies and filters.</summary>
	void ShutdownCaptures()
	{
		for ( int i = 0; i < captureFaces.Length; i++ )
		{
			captureFaces[i]?.Dispose();
			captureFaces[i] = null;
		}

		foreach ( var (_, capture) in cubeCaptures ) capture.Dispose();
		cubeCaptures.Clear();
	}
}
