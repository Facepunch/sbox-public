using Sandbox.Rendering;
using Sandbox.Rendering.Bridge;

namespace Sandbox.SceneLab;

/// <summary>
/// GameObject scenes (<see cref="SceneLabScene.Game"/>): ticked every frame, their main camera orbited, and drawn by native's
/// scene system. A capture saves the camera's frame (<c>name.native.png</c>), then changes the scene and captures again when it
/// has a <see cref="SceneLabScene.ChangeGame"/>.
/// </summary>
internal sealed partial class SceneLabWindow
{
	/// <summary>
	/// Where GameObject scenes' cameras look, and orbit around.
	/// </summary>
	// What the orbit turns about: -camera-lookat moves it, so a map preset can face anywhere
	static readonly Vector3 GameFocus = Args.String( "-camera-lookat" ) is { } at ? Vector3.Parse( at ) : new( 0, 0, 30 );

	Scene game;

	/// <summary>
	/// The GameObject scene loaded, if one is.
	/// </summary>
	public Scene GameScene => game;

	// A preset's own capture frame, unless -capture-frame chose one
	int GameCaptureFrame => !Args.Has( "-capture-frame" ) && Scene.CaptureFrame is { } preset ? preset : captureFrame;

	(float Yaw, float Pitch, float Distance) gameCamera;
	int gameChangedFrame;
	CameraComponent[] targetCameras = [];

	/// <summary>
	/// <c>-isolated</c>: capture a GameObject scene's camera alone, for effects whose history belongs to the camera - AO, SSR -
	/// which a second render of the camera in the frame would advance. Each capture frame renders the camera once, into a
	/// readback target instead of the swap chain. The built-in <c>-parity</c> runner captures such presets from fresh loads.
	/// </summary>
	bool isolated => Args.Has( "-isolated" ) || parity?.Isolated == true;

	// The frame an isolated capture rendered this frame, if it did
	Color32[] isolatedPixels;
	Vector2Int isolatedSize;

	// The faces of the cube the preset captured that frame (SceneLabScene.CaptureFaces), if it has one
	Color32[][] isolatedFaces;
	int isolatedFaceSize;

	// The volume textures the preset made (SceneLabScene.CaptureVolumes), read back as floats, if it has any
	Vector4[][] isolatedVolumes;

	/// <summary>
	/// Float textures' texels, mip 0: a volume's as half floats, a single-channel cube's face by face.
	/// </summary>
	static Vector4[][] ReadVolumes( Texture[] volumes )
	{
		var texels = new Vector4[volumes.Length][];
		for ( int i = 0; i < volumes.Length; i++ )
		{
			var volume = volumes[i];
			var count = volume.Width * volume.Height * volume.Depth;

			// A single-channel cube, face by face
			if ( volume.ImageFormat == ImageFormat.R32F )
			{
				var face = volume.Width * volume.Height;
				var values = new float[face];
				texels[i] = new Vector4[count];
				for ( int f = 0; f < volume.Depth; f++ )
				{
					volume.GetPixels<float>( (0, 0, volume.Width, volume.Height), f, 0, values.AsSpan(), ImageFormat.R32F );
					for ( int t = 0; t < face; t++ ) texels[i][f * face + t] = new Vector4( values[t], 0, 0, 0 );
				}

				continue;
			}

			var halves = new ushort[count * 4];
			volume.GetPixels3D<ushort>( (0, 0, 0, volume.Width, volume.Height, volume.Depth), 0, halves.AsSpan(), ImageFormat.RGBA16161616F );
			texels[i] = new Vector4[count];
			for ( int t = 0; t < count; t++ )
			{
				texels[i][t] = new Vector4( (float)BitConverter.UInt16BitsToHalf( halves[t * 4] ), (float)BitConverter.UInt16BitsToHalf( halves[t * 4 + 1] ),
					(float)BitConverter.UInt16BitsToHalf( halves[t * 4 + 2] ), (float)BitConverter.UInt16BitsToHalf( halves[t * 4 + 3] ) );
			}
		}

		return texels;
	}

	/// <summary>
	/// A cube's six faces, mip 0, read back as 8-bit pixels.
	/// </summary>
	Color32[][] ReadFaces( Texture cube )
	{
		isolatedFaceSize = cube.Width;
		var faces = new Color32[6][];
		for ( int i = 0; i < faces.Length; i++ )
		{
			faces[i] = new Color32[cube.Width * cube.Height];
			cube.GetPixels<Color32>( (0, 0, cube.Width, cube.Height), i, 0, faces[i].AsSpan(), ImageFormat.RGBA8888 );
		}

		return faces;
	}

	bool LoadGame( SceneLabScene scene )
	{
		UnloadGame();

		game = WithGameUI( scene.Game );
		gameChangedFrame = 0;

		// Cameras that render into textures - monitors, mirrors - drawn each frame before the main one
		targetCameras = game.GetAllComponents<CameraComponent>().Where( x => x != game.Camera && x.RenderTarget is not null ).ToArray();

		// Orbit from where the scene put its camera
		if ( game.Camera is { } camera )
		{
			// -debugmode AmbientOcclusion and the like: capture a debug visualisation instead of the lit frame
			if ( Args.String( "-debugmode" ) is { Length: > 0 } debugModeName )
			{
				if ( Enum.TryParse<SceneCameraDebugMode>( debugModeName, true, out var debugMode ) && Enum.IsDefined( debugMode ) )
					camera.DebugMode = debugMode;
				else
					Log.Warning( $"Scene Lab: unknown -debugmode {debugModeName}, capturing the lit frame" );
			}

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

	void UnloadGame()
	{
		extraWorldsCamera?.Dispose();
		extraWorldsCamera = null;
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
		var isolatedCapture = isolated && CapturePath is not null && (frame + 1 == GameCaptureFrame || (gameChangedFrame != 0 && frame + 1 == gameChangedFrame)) && (Scene.Ready?.Invoke( game ) ?? true);
		isolatedPixels = null;

		using ( game.Push() )
		{
			game.PreCameraRender();

			// Native's renderer by name: a panel app's frame doesn't pick one
			Scene.Capture?.Invoke( game, SceneRenderer.Native );

			foreach ( var target in targetCameras )
			{
				target.InitializeRendering();
				SceneRenderer.Native.RenderToTexture( target.SceneCamera, target.RenderTarget, target.RenderTarget.Size, Scene.TargetSetup?.Invoke( target ) ?? default );
			}

			camera.InitializeRendering();

			if ( isolatedCapture )
			{
				isolatedSize = new Vector2Int( (int)size.x, (int)size.y );
				isolatedPixels = RenderBitmap( camera.SceneCamera, isolatedSize, default );
				isolatedFaces = Scene.CaptureFaces?.Invoke( game ) is { } cube ? ReadFaces( cube ) : null;
				isolatedVolumes = Scene.CaptureVolumes?.Invoke( game ) is { } volumes ? ReadVolumes( volumes ) : null;
			}
			else
			{
				camera.SceneCamera.AddToRenderList( SceneRenderer.Native, swapChain, size );
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

		if ( captureRequested is not null )
		{
			var folder = captureRequested;
			captureRequested = null;
			ShowMessage( CaptureGame( folder, FileName( Scene.Name ) ).Result );
		}

		// A capture at the capture frame, then when the scene changes itself, again a few frames after changing
		if ( CapturePath is null ) return;

		var captureFolder = System.IO.Path.GetDirectoryName( System.IO.Path.GetFullPath( CapturePath ) );
		var captureName = System.IO.Path.GetFileNameWithoutExtension( CapturePath );

		if ( frame == GameCaptureFrame )
		{
			var (result, failed) = isolated ? CaptureIsolated( captureFolder, captureName ) : CaptureGame( captureFolder, captureName );
			Log.Info( $"Scene Lab: {Scene.Name}: {result}" );
			parity?.Report( result, failed );

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
			var (result, failed) = isolated ? CaptureIsolated( captureFolder, $"{captureName}_changed" ) : CaptureGame( captureFolder, $"{captureName}_changed" );
			Log.Info( $"Scene Lab: {Scene.Name}, changed: {result}" );
			parity?.Report( result, failed );
			CaptureDone();
		}
	}

	/// <summary>
	/// The scene's main camera rendered natively into an 8-bit bitmap, effects and UI included, saved in <paramref name="folder"/>
	/// as <c>name.native.png</c>; or a camera set up from it with the scene's extra world added
	/// (<see cref="SceneLabScene.ExtraWorld"/>), which the camera component clears every frame. Fails on what the preset's
	/// <see cref="SceneLabScene.ValidateGame"/> finds.
	/// </summary>
	(string Result, bool Failed) CaptureGame( string folder, string name )
	{
		System.IO.Directory.CreateDirectory( folder );
		var size = new Vector2Int( 1280, 720 );
		var path = System.IO.Path.Combine( folder, $"{name}.native.png" );

		Color32[] pixels;
		using ( game.Push() )
		{
			game.PreCameraRender();
			var camera = game.Camera;
			if ( Scene.ExtraWorld is { } extraWorld )
			{
				extraWorldsCamera ??= new SceneCamera( "Scene Lab extra worlds" );
				camera.UpdateSceneCamera( extraWorldsCamera );
				extraWorldsCamera.Worlds.Add( extraWorld( game ) );
				pixels = RenderBitmap( extraWorldsCamera, size, extraWorldsCamera.MainView );
			}
			else
			{
				camera.InitializeRendering();
				pixels = RenderBitmap( camera.SceneCamera, size, default );
			}
		}

		FrameComparison.SavePng( pixels, size.x, size.y, path );
		if ( Scene.ValidateGame?.Invoke( game ) is { } error )
		{
			Environment.ExitCode = 2;
			return ($"FAILED: {error}", true);
		}

		return ($"native frame to {path}", false);
	}

	// The camera an ExtraWorld scene captures through
	SceneCamera extraWorldsCamera;

	/// <summary>
	/// A camera rendered natively into an 8-bit bitmap, as a game's screenshot is.
	/// </summary>
	static Color32[] RenderBitmap( SceneCamera camera, Vector2Int size, in ViewSetup setup )
	{
		using var bitmap = new Bitmap( size.x, size.y );
		camera.OnPreRender( bitmap.Size );
		SceneRenderer.Native.RenderToBitmap( camera, bitmap, setup );
		return bitmap.GetPixels32();
	}

	/// <summary>
	/// Save this frame's isolated capture as <c>name.native.png</c>, with the preset's cube faces and volumes beside it.
	/// </summary>
	(string Result, bool Failed) CaptureIsolated( string folder, string name )
	{
		if ( isolatedPixels is not { } pixels ) return ("no isolated frame rendered", true);

		System.IO.Directory.CreateDirectory( folder );
		var nativePath = System.IO.Path.Combine( folder, $"{name}.native.png" );
		FrameComparison.SavePng( pixels, isolatedSize.x, isolatedSize.y, nativePath );
		for ( int i = 0; i < (isolatedFaces?.Length ?? 0); i++ )
			FrameComparison.SavePng( isolatedFaces[i], isolatedFaceSize, isolatedFaceSize, System.IO.Path.Combine( folder, $"{name}.face{i}.native.png" ) );
		for ( int i = 0; i < (isolatedVolumes?.Length ?? 0); i++ )
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( folder, $"{name}.volume{i}.native.f32" ), System.Runtime.InteropServices.MemoryMarshal.AsBytes( isolatedVolumes[i].AsSpan() ).ToArray() );
		return ($"isolated native frame to {nativePath}", false);
	}

	static string FileName( string name ) => new string( name.Where( char.IsLetterOrDigit ).ToArray() ).ToLowerInvariant();
}
