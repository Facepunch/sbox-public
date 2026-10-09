using Sandbox.SceneRenderer;
using Sandbox.SceneRenderer.Bridge;

namespace Sandbox.SceneLab;

/// <summary>
/// GameObject scenes (<see cref="SceneLabScene.Game"/>): ticked every frame, their main camera orbited like the grid's
/// view, and drawn by the managed renderer's GameObject bridge - the path <c>r_managed_scene</c> takes in a game - or by
/// native. A capture compares the two as <c>r_managed_scene_compare</c> does, then changes the scene and compares again
/// when it has a <see cref="SceneLabScene.ChangeGame"/>.
/// </summary>
internal sealed partial class SceneLabWindow
{
	/// <summary>
	/// Where GameObject scenes' cameras look, and orbit around.
	/// </summary>
	static readonly Vector3 GameFocus = new( 0, 0, 30 );

	Scene game;
	(float Yaw, float Pitch, float Distance) gameCamera;
	int gameChangedFrame;
	CameraComponent[] targetCameras = [];

	/// <summary>
	/// <c>-isolated</c>: capture a GameObject scene through the chosen renderer alone, for effects whose history belongs to
	/// the camera - AO, SSR - which a compare in one process mixes, the two renderers reading what the other wrote. Each
	/// capture frame renders the camera once, into a readback target instead of the swap chain; the native run saves
	/// <c>name.native.png</c>, and the managed run compares against it. The built-in <c>-parity -isolated</c> runner
	/// performs both passes from fresh scene loads.
	/// </summary>
	bool isolated => Args.Has( "-isolated" ) || parity?.Isolated == true;

	// The last game compare's result, for the parity run
	FrameComparison.Parity? lastParity;

	// The frame an isolated capture rendered this frame, if it did
	Color32[] isolatedPixels;
	Vector2Int isolatedSize;

	bool LoadGame( SceneLabScene scene )
	{
		UnloadGame();
		if ( !scene.IsGame ) return false;

		game = WithGameUI( scene.Game );
		gameChangedFrame = 0;

		// Cameras that render into textures - monitors, mirrors - drawn each frame before the main one
		targetCameras = game.GetAllComponents<CameraComponent>().Where( x => x != game.Camera && x.RenderTarget is not null ).ToArray();

		// Orbit from where the scene put its camera
		if ( game.Camera is { } camera )
		{
			var offset = GameFocus - camera.WorldPosition;
			var angles = Rotation.LookAt( offset ).Angles();
			gameCamera = (angles.yaw, angles.pitch, offset.Length);
		}

		ResetCamera();
		timer.Clear();
		Title = $"Scene Lab - {scene.Name}";
		Log.Info( $"Scene Lab: {scene.Name}, a GameObject scene" );
		return true;
	}

	/// <summary>
	/// Run scene code with the window's UI system as the current one: a panel app has no game UI system, so screen panels'
	/// roots live in the window's, which lays them out and records their command lists every frame, as the gallery's
	/// camera page does. They're rendered manually, so they only draw at the camera's UI stage.
	/// </summary>
	T WithGameUI<T>( Func<T> action )
	{
		var context = Sandbox.Engine.GlobalContext.Current;
		var previous = context.UISystem;
		context.UISystem = Root.UISystem;
		try
		{
			return action();
		}
		finally
		{
			context.UISystem = previous;
		}
	}

	// What a NativeAlongside scene's camera renders into natively
	Texture nativeAlongside;

	void UnloadGame()
	{
		nativeAlongside?.Dispose();
		nativeAlongside = null;
		game?.Destroy();
		game = null;
		targetCameras = [];
	}

	/// <summary>
	/// Tick the scene, point its camera where the orbit is, and draw it into the swap chain.
	/// </summary>
	void RenderGame( SwapChainHandle_t swapChain, Vector2 size )
	{
		var camera = game.Camera;
		if ( camera is null ) return;

		using ( game.Push() )
		{
			var rotation = Rotation.From( Pitch, Yaw, 0 );
			camera.WorldRotation = rotation;
			camera.WorldPosition = GameFocus - rotation.Forward * Distance;

			WithGameUI( () =>
			{
				game.GameTick( 1.0 / 60.0 );
				Scene.Frame?.Invoke( game );
				return true;
			} );
		}

		timer.BeginRender();

		// An isolated capture frame renders the camera once, into its readback target
		var isolatedCapture = isolated && CapturePath is not null && (frame + 1 == captureFrame || (gameChangedFrame != 0 && frame + 1 == gameChangedFrame)) && (Scene.Ready?.Invoke( game ) ?? true);
		isolatedPixels = null;

		using ( game.Push() )
		{
			game.PreCameraRender();

			foreach ( var target in targetCameras )
			{
				target.InitializeRendering();
				if ( Renderer == RendererKind.Managed )
				{
					gameBridge ??= new SceneBridge();
					gameBridge.RenderToTexture( target, target.RenderTarget );
				}
				else
				{
					target.SceneCamera.RenderToTexture( target.RenderTarget, target.RenderTarget.Size, default );
				}
			}

			camera.InitializeRendering();

			if ( isolatedCapture )
			{
				isolatedSize = new Vector2Int( (int)size.x, (int)size.y );
				isolatedPixels = Renderer == RendererKind.Managed
					? (gameBridge ??= new SceneBridge()).RenderManaged( camera, isolatedSize, updateExposure: true )
					: SceneBridge.RenderNative( camera, isolatedSize, prepare: false );
			}
			else if ( Renderer == RendererKind.Managed )
			{
				gameBridge ??= new SceneBridge();
				gameBridge.Render( camera, swapChain, size );

				// The same camera natively too, where native's history has to keep pace (SceneLabScene.NativeAlongside)
				if ( Scene.NativeAlongside )
				{
					if ( nativeAlongside is null || nativeAlongside.Size != size )
					{
						nativeAlongside?.Dispose();
						nativeAlongside = Texture.CreateRenderTarget().WithSize( (int)size.x, (int)size.y ).WithFormat( ImageFormat.RGBA16161616F ).Create( "Scene Lab native alongside" );
					}

					camera.SceneCamera.RenderToTexture( nativeAlongside, size, default );
				}
			}
			else
			{
				camera.SceneCamera.AddToRenderList( swapChain, size );
			}
		}

		timer.EndRender( swapChain );

		// Frames count from when the scene is ready, so a capture waits for what it's loading
		if ( Scene.Ready is { } ready && !ready( game ) ) return;
		frame++;

		if ( benchmark is not null )
		{
			if ( benchmark.Frame( this, timer ) )
			{
				Finished = true;
				benchmark = null;
			}

			return;
		}

		UpdateStats();

		if ( compareRequested is not null )
		{
			var folder = compareRequested;
			compareRequested = null;
			ShowMessage( CompareGame( folder, FileName( Scene.Name ) ) );
		}

		// A capture compares at the capture frame, then when the scene changes itself, again a few frames after changing
		if ( CapturePath is null ) return;

		var captureFolder = System.IO.Path.GetDirectoryName( System.IO.Path.GetFullPath( CapturePath ) );
		var captureName = System.IO.Path.GetFileNameWithoutExtension( CapturePath );

		if ( frame == captureFrame )
		{
			var result = isolated ? CaptureIsolated( captureFolder, captureName ) : CompareGame( captureFolder, captureName );
			Log.Info( $"Scene Lab: {Scene.Name}: {result}" );
			CapturePlan( CapturePath );
			parity?.Report( result, lastParity );

			if ( Scene.ChangeGame is { } change )
			{
				WithGameUI( () => { change( game ); return true; } );
				gameChangedFrame = frame + 10;
				return;
			}

			CaptureDone();
		}
		else if ( frame == gameChangedFrame )
		{
			var result = isolated ? CaptureIsolated( captureFolder, $"{captureName}_changed" ) : CompareGame( captureFolder, $"{captureName}_changed" );
			Log.Info( $"Scene Lab: {Scene.Name}, changed: {result}" );
			parity?.Report( result, lastParity );
			CaptureDone();
		}
	}

	/// <summary>
	/// Compare the scene's main camera through both renderers, saving the frames in <paramref name="folder"/>.
	/// </summary>
	string CompareGame( string folder, string name )
	{
		System.IO.Directory.CreateDirectory( folder );
		gameBridge ??= new SceneBridge();

		string result;
		using ( game.Push() )
		{
			result = gameBridge.Compare( game.Camera, folder, name );
		}

		lastParity = gameBridge.LastParity;
		if ( gameBridge.LastParity is { Matches: false } ) Environment.ExitCode = 2;
		return result;
	}

	/// <summary>
	/// Save this frame's isolated capture: native's as <c>name.native.png</c>, managed's as <c>name.png</c>, compared with
	/// native's when the native run saved it.
	/// </summary>
	string CaptureIsolated( string folder, string name )
	{
		lastParity = null;
		if ( isolatedPixels is not { } pixels ) return "no isolated frame rendered";

		System.IO.Directory.CreateDirectory( folder );
		var (width, height) = (isolatedSize.x, isolatedSize.y);
		var managedPath = System.IO.Path.Combine( folder, $"{name}.png" );
		var nativePath = System.IO.Path.Combine( folder, $"{name}.native.png" );

		if ( Renderer != RendererKind.Managed )
		{
			FrameComparison.SavePng( pixels, width, height, nativePath );
			return $"isolated native frame to {nativePath}";
		}

		FrameComparison.SavePng( pixels, width, height, managedPath );
		if ( !System.IO.File.Exists( nativePath ) ) return $"isolated managed frame to {managedPath}, no native frame to compare";

		using var reference = Bitmap.CreateFromBytes( System.IO.File.ReadAllBytes( nativePath ) );
		var native = reference.GetPixels32();
		var result = FrameComparison.Compare( pixels, native );
		if ( native.Length == pixels.Length ) FrameComparison.SavePng( FrameComparison.Difference( pixels, native ), width, height, System.IO.Path.Combine( folder, $"{name}.diff.png" ) );
		lastParity = result;
		if ( !result.Matches ) Environment.ExitCode = 2;
		return $"{result} | isolated";
	}

	static string FileName( string name ) => new string( name.Where( char.IsLetterOrDigit ).ToArray() ).ToLowerInvariant();

	/// <summary>
	/// The managed renderer's numbers for the game camera's last frame.
	/// </summary>
	SceneRenderer.RenderStats? GameStats()
	{
		if ( game?.Camera is not { } camera || gameBridge?.StatsFor( camera ) is not { } stats ) return null;

		// Each render target camera is a render of its own, and the first to render a frame syncs its changes: the frame's
		// numbers are all of them
		foreach ( var target in targetCameras )
		{
			if ( gameBridge.StatsFor( target ) is not { } more ) continue;

			stats.SyncMs += more.SyncMs;
			stats.CollectMs += more.CollectMs;
			stats.PrepareMs += more.PrepareMs;
			stats.SetupMs += more.SetupMs;
			stats.RecordMs += more.RecordMs;
			stats.RecordWaitMs += more.RecordWaitMs;
			stats.WorkerRecordMs += more.WorkerRecordMs;
			stats.StagesMs += more.StagesMs;
			stats.SubmitMs += more.SubmitMs;
			stats.Draws += more.Draws;
			stats.DepthDraws += more.DepthDraws;
			stats.ShadowDraws += more.ShadowDraws;
		}

		return stats;
	}
}
