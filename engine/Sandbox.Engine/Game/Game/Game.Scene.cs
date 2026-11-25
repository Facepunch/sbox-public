using Sandbox.Engine;

namespace Sandbox;

public static partial class Game
{
	/// <summary>
	/// Indicates whether the game is currently running and actively playing a scene.
	/// </summary>
	public static bool IsPlaying { get; internal set; } = true;

	/// <summary>
	/// Indicates whether the game is currently paused.
	/// </summary>
	public static bool IsPaused { get; set; }

	/// <summary>
	/// The current scene that is being played.
	/// </summary>
	public static Scene ActiveScene
	{
		get => GlobalContext.Current.ActiveScene;
		internal set => GlobalContext.Current.ActiveScene = value;
	}

	/// <summary>
	/// Change the active scene and optionally bring all connected clients to
	/// the new scene (broadcast the scene change.) If we're in a networking
	/// session, then only the host can change the scene.
	/// </summary>
	/// <param name="scene"></param>
	/// <returns>Whether the scene was changed successfully.</returns>
	public static bool ChangeScene( SceneFile scene )
	{
		var loadOptions = new SceneLoadOptions();
		loadOptions.SetScene( scene );

		return ChangeScene( loadOptions );
	}

	/// <summary>
	/// Change the active scene and optionally bring all connected clients to
	/// the new scene (broadcast the scene change.) If we're in a networking
	/// session, then only the host can change the scene.
	/// </summary>
	/// <param name="scenePath">The relative path to the scene to load.</param>
	/// <returns>Whether the scene was changed successfully.</returns>
	public static bool ChangeScene( string scenePath )
	{
		var loadOptions = new SceneLoadOptions();
		loadOptions.SetScene( scenePath );

		return ChangeScene( loadOptions );
	}

	/// <summary>
	/// Change the active scene and optionally bring all connected clients to
	/// the new scene (broadcast the scene change.) If we're in a networking
	/// session, then only the host can change the scene.
	/// </summary>
	/// <param name="options">The <see cref="SceneLoadOptions"/> to use which also specifies which scene to load.</param>
	/// <returns>Whether the scene was changed successfully.</returns>
	public static bool ChangeScene( SceneLoadOptions options )
	{
		if ( !Networking.IsHost )
			return false;

		// We don't want to send any networked messages to do with deletion or creation
		// of GameObjects here. Because the client will destroy their scene locally
		// anyway. This saves us sending a message for potentially 100s of objects.
		using ( SceneNetworkSystem.SuppressSpawnMessages() )
		{
			using ( SceneNetworkSystem.SuppressDestroyMessages() )
			{
				if ( !ActiveScene.Load( options ) )
					return false;
			}
		}

		// Conna: We want to send a new snapshot to every client.
		SceneNetworkSystem.Instance?.LoadSceneBroadcast( options );
		return true;
	}

	internal static void Render( SwapChainHandle_t swapChain )
	{
		// An exported game's own loading screen, in place of whatever's loading
		if ( StandaloneLoadingScene.Render( swapChain ) )
			return;

		// IToolsDll.OnRender handles the case where game is not playing (render from editor scene)
		if ( !IsPlaying )
			return;

		// Could be loading still
		if ( ActiveScene is null )
			return;

		if ( DrawsLoading( ActiveScene ) )
		{
			ActiveScene.RenderEnvmaps();

			// Make sure overlays are rendered even when we are loading
			if ( ActiveScene.Camera is not null )
			{
				ActiveScene.Camera.SceneCamera.EnableEngineOverlays = true;
				ActiveScene.Camera.AddToRenderList( swapChain, default );
			}

			return;
		}

		ActiveScene.Camera?.SceneCamera.EnableEngineOverlays = true;
		SceneCamera.RecordingCamera = ActiveScene.Camera?.SceneCamera;

		ActiveScene.Render( swapChain, default );
	}

	/// <summary>
	/// The game's half of a frame the game window draws, on the main thread at the sync point (<see cref="Scene.ExtractRender"/>):
	/// the active scene's cameras prepared into <paramref name="drawn"/>. Returns the scene they draw, or null when the frame
	/// draws the game as <see cref="Render"/> does (nothing playing, a load showing: its envmaps and overlays, on this thread).
	/// </summary>
	internal static Scene ExtractRender( List<CameraComponent> drawn )
	{
		drawn.Clear();
		if ( !IsPlaying || ActiveScene is null ) return null;
		if ( DrawsLoading( ActiveScene ) ) return null;

		ActiveScene.Camera?.SceneCamera.EnableEngineOverlays = true;
		SceneCamera.RecordingCamera = ActiveScene.Camera?.SceneCamera;
		ActiveScene.ExtractRender( drawn );
		return ActiveScene;
	}

	/// <summary>
	/// Whether a frame draws <paramref name="scene"/> as a load does (its main camera and envmaps, on the main thread): while it
	/// loads, a loading screen shows or it connects. Not once it has loaded and the screen stays only while its first frames
	/// draw behind it (<see cref="BorealisGpuRenderBackend.Revealing"/>): those draw as the frames after it will.
	/// </summary>
	internal static bool DrawsLoading( Scene scene ) => scene.IsLoading || Networking.IsConnecting
		|| (LoadingScreen.IsVisible && BorealisGpuRenderBackend.Revealing != scene);

	internal static void Shutdown()
	{
		IsClosing = true;
		IsPlaying = false;

		ActiveScene?.Destroy();
		ActiveScene = null;

		IsClosing = false;
	}
}
