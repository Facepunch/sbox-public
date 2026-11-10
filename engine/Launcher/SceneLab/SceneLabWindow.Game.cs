using Basalt;
using Sandbox.Rendering;
using Sandbox.Rendering.Bridge;

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
		if ( !scene.IsGame ) return false;

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

	// What a NativeAlongside scene's camera renders into natively
	Texture nativeAlongside;

	void UnloadGame()
	{
		nativeAlongside?.Dispose();
		nativeAlongside = null;
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

			// Each renderer by name, whatever r_managed_scene says: these are the A/B
			Scene.Capture?.Invoke( game, Renderer == RendererKind.Managed ? (gameBridge ??= new SceneBridge()) : SceneRenderer.Native );

			foreach ( var target in targetCameras )
			{
				target.InitializeRendering();
				// Each renderer by name, whatever r_managed_scene says: these are the A/B
				ISceneRenderer renderer = Renderer == RendererKind.Managed ? (gameBridge ??= new SceneBridge()) : SceneRenderer.Native;
				renderer.RenderToTexture( target.SceneCamera, target.RenderTarget, target.RenderTarget.Size, Scene.TargetSetup?.Invoke( target ) ?? default );
			}

			camera.InitializeRendering();

			if ( isolatedCapture )
			{
				isolatedSize = new Vector2Int( (int)size.x, (int)size.y );
				isolatedPixels = Renderer == RendererKind.Managed
					? (gameBridge ??= new SceneBridge()).RenderManaged( camera, isolatedSize, updateExposure: true )
					: SceneBridge.RenderNative( camera, isolatedSize, prepare: false );
				isolatedFaces = Scene.CaptureFaces?.Invoke( game ) is { } cube ? ReadFaces( cube ) : null;
				isolatedVolumes = Scene.CaptureVolumes?.Invoke( game ) is { } volumes ? ReadVolumes( volumes ) : null;
			}
			else if ( Renderer == RendererKind.Managed )
			{
				camera.SceneCamera.AddToRenderList( gameBridge ??= new SceneBridge(), swapChain, size );

				// The same camera natively too, where native's history has to keep pace (SceneLabScene.NativeAlongside) - but not
				// while benchmarking, where it would put native's frame into the managed one's numbers
				if ( Scene.NativeAlongside && benchmark is null )
				{
					if ( nativeAlongside is null || nativeAlongside.Size != size )
					{
						nativeAlongside?.Dispose();
						nativeAlongside = Texture.CreateRenderTarget().WithSize( (int)size.x, (int)size.y ).WithFormat( ImageFormat.RGBA16161616F ).Create( "Scene Lab native alongside" );
					}

					SceneRenderer.Native.RenderToTexture( camera.SceneCamera, nativeAlongside, size, default );
				}
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

		if ( Renderer == RendererKind.Managed && Scene.ReportMirror?.Invoke( game ) is { } label && gameBridge?.CachesFor( camera.SceneCamera, camera.SceneCamera.MainView ) is { } caches )
			Log.Info( $"Scene Lab: {Scene.Name}, {label}: mirror holds {caches}" );

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

		if ( frame == GameCaptureFrame )
		{
			var result = isolated ? CaptureIsolated( captureFolder, captureName ) : CompareGame( captureFolder, captureName );
			Log.Info( $"Scene Lab: {Scene.Name}: {result}" );
			CapturePlan( CapturePath );
			if ( !isolated && Renderer == RendererKind.Managed && DebugOverlay.ManagedViews.Showing )
				CaptureDebugViews( ReadSwapChain( swapChain, size ), (int)size.x, (int)size.y, CapturePath );
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
			result = Scene.ExtraWorld is { } extraWorld ? CompareWithExtraWorld( extraWorld( game ), folder, name ) : gameBridge.Compare( game.Camera, folder, name );
		}

		lastParity = gameBridge.LastParity;
		if ( Scene.ValidateGame?.Invoke( game ) is { } error )
		{
			lastParity = null;
			Environment.ExitCode = 2;
			return $"FAILED: {error}";
		}
		if ( gameBridge.LastParity is { Matches: false } ) Environment.ExitCode = 2;
		return result;
	}

	// The camera an ExtraWorld scene compares through
	SceneCamera extraWorldsCamera;

	/// <summary>
	/// Compare a camera set up from the scene's, with <paramref name="extraWorld"/> among its extra worlds, through both
	/// renderers into 8-bit frames as the bridge's compare does: native first, then managed reusing native's exposure update.
	/// </summary>
	string CompareWithExtraWorld( SceneWorld extraWorld, string folder, string name )
	{
		var size = new Vector2Int( 1280, 720 );
		var camera = game.Camera;
		extraWorldsCamera ??= new SceneCamera( "Scene Lab extra worlds" );

		game.PreCameraRender();
		camera.UpdateSceneCamera( extraWorldsCamera );
		extraWorldsCamera.Worlds.Add( extraWorld );

		using var bitmap = new Bitmap( size.x, size.y );
		extraWorldsCamera.OnPreRender( bitmap.Size );
		SceneRenderer.Native.RenderToBitmap( extraWorldsCamera, bitmap, extraWorldsCamera.MainView );
		var native = bitmap.GetPixels32();
		var managed = gameBridge.RenderManaged( extraWorldsCamera, size, updateExposure: false );

		var parity = FrameComparison.Compare( managed, native );
		var managedPath = System.IO.Path.Combine( folder, $"{name}.png" );
		FrameComparison.SavePng( managed, size.x, size.y, managedPath );
		FrameComparison.SavePng( native, size.x, size.y, System.IO.Path.ChangeExtension( managedPath, ".native.png" ) );
		FrameComparison.SavePng( FrameComparison.Difference( managed, native ), size.x, size.y, System.IO.Path.ChangeExtension( managedPath, ".diff.png" ) );
		gameBridge.LastParity = parity;

		var stats = gameBridge.StatsFor( extraWorldsCamera, extraWorldsCamera.MainView );
		return $"{parity} | extra worlds: {extraWorld.SceneObjects.Count} objects, debug world {game.DebugSceneWorld?.SceneObjects.Count ?? 0} | {stats?.ObjectsVisible} objects drawn in {stats?.Draws} draws, {stats?.CustomDraws} custom draws | frames in {folder}";
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
			for ( int i = 0; i < (isolatedFaces?.Length ?? 0); i++ )
				FrameComparison.SavePng( isolatedFaces[i], isolatedFaceSize, isolatedFaceSize, System.IO.Path.Combine( folder, $"{name}.face{i}.native.png" ) );
			for ( int i = 0; i < (isolatedVolumes?.Length ?? 0); i++ )
				System.IO.File.WriteAllBytes( System.IO.Path.Combine( folder, $"{name}.volume{i}.native.f32" ), System.Runtime.InteropServices.MemoryMarshal.AsBytes( isolatedVolumes[i].AsSpan() ).ToArray() );
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
		var readbacks = $"{CompareFaces( folder, name )}{CompareVolumes( folder, name )}";

		// A readback that fails fails the preset, whatever the frame did
		if ( result.Matches && lastParity is { Matches: false } ) return $"parity FAILED: a readback differs | {result} | isolated{readbacks}";
		return $"{result} | isolated{readbacks}";
	}

	/// <summary>
	/// Compare each volume texture's texels with native's: the mean difference over the mean of native's values, which fails
	/// the capture over 0.1%.
	/// </summary>
	string CompareVolumes( string folder, string name )
	{
		if ( isolatedVolumes is not { } volumes ) return "";

		var text = new System.Text.StringBuilder( " | volumes:" );
		for ( int i = 0; i < volumes.Length; i++ )
		{
			var nativePath = System.IO.Path.Combine( folder, $"{name}.volume{i}.native.f32" );
			if ( !System.IO.File.Exists( nativePath ) ) return $"{text} no native volumes to compare";

			var native = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Vector4>( System.IO.File.ReadAllBytes( nativePath ) );
			var mine = volumes[i];
			System.IO.File.WriteAllBytes( System.IO.Path.Combine( folder, $"{name}.volume{i}.f32" ), System.Runtime.InteropServices.MemoryMarshal.AsBytes( mine.AsSpan() ).ToArray() );
			if ( native.Length != mine.Length ) return $"{text} {i}: sizes differ";

			double difference = 0, magnitude = 0, largest = 0;
			for ( int t = 0; t < mine.Length; t++ )
			{
				var d = mine[t] - native[t];
				var absolute = Math.Abs( d.x ) + Math.Abs( d.y ) + Math.Abs( d.z ) + Math.Abs( d.w );
				difference += absolute;
				largest = Math.Max( largest, absolute );
				magnitude += Math.Abs( native[t].x ) + Math.Abs( native[t].y ) + Math.Abs( native[t].z ) + Math.Abs( native[t].w );
			}

			var relative = magnitude > 0 ? difference / magnitude : difference;
			text.Append( $" {i}: {relative * 100:0.000}% of native's mean, largest texel {largest:0.###}" );
			if ( relative > 0.001 )
			{
				text.Append( " FAILED" );
				lastParity = lastParity is { } frame ? frame with { Different = frame.Compared } : null;
				Environment.ExitCode = 2;
			}
		}

		return text.ToString();
	}

	/// <summary>
	/// Save the captured cube's faces, and compare each with native's: a face that fails fails the capture.
	/// </summary>
	string CompareFaces( string folder, string name )
	{
		if ( isolatedFaces is not { } faces ) return "";

		var text = new System.Text.StringBuilder( " | cube faces:" );
		for ( int i = 0; i < faces.Length; i++ )
		{
			var size = isolatedFaceSize;
			FrameComparison.SavePng( faces[i], size, size, System.IO.Path.Combine( folder, $"{name}.face{i}.png" ) );
			var nativePath = System.IO.Path.Combine( folder, $"{name}.face{i}.native.png" );
			if ( !System.IO.File.Exists( nativePath ) ) return $"{text} no native faces to compare";

			using var reference = Bitmap.CreateFromBytes( System.IO.File.ReadAllBytes( nativePath ) );
			var face = FrameComparison.Compare( faces[i], reference.GetPixels32() );
			text.Append( $" {i}: {face.MeanDifference:0.00}/255, {face.Different} off" );
			if ( !face.Matches )
			{
				text.Append( " FAILED" );
				if ( lastParity is { Matches: true } ) lastParity = face;
				Environment.ExitCode = 2;
			}
		}

		return text.ToString();
	}

	static string FileName( string name ) => new string( name.Where( char.IsLetterOrDigit ).ToArray() ).ToLowerInvariant();

	/// <summary>
	/// The managed renderer's numbers for the game camera's last frame.
	/// </summary>
	Basalt.RenderStats? GameStats()
	{
		if ( game?.Camera is not { } camera || gameBridge?.StatsFor( camera.SceneCamera, camera.SceneCamera.MainView ) is not { } stats ) return null;

		// Each render target camera is a render of its own, and the first to render a frame syncs its changes: the frame's
		// numbers are all of them
		foreach ( var target in targetCameras )
		{
			if ( gameBridge.StatsFor( target.SceneCamera, default ) is not { } more ) continue;

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
