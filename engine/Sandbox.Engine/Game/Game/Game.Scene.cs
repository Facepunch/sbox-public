using Sandbox.Engine;

namespace Sandbox;

public static partial class Game
{
	/// <summary>
	/// Indicates whether the game is currently running and actively playing a scene.
	/// </summary>
	public static bool IsPlaying { get; internal set; }

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
		// IToolsDll.OnRender handles the case where game is not playing (render from editor scene)
		if ( !IsPlaying )
			return;

		// Could be loading still
		if ( ActiveScene is null )
			return;

		if ( ActiveScene.IsLoading || LoadingScreen.IsVisible || Networking.IsConnecting )
		{
			ActiveScene.RenderEnvmaps();
			return;
		}

		ActiveScene.Camera.SceneCamera.EnableEngineOverlays = true;
		SceneCamera.RecordingCamera = ActiveScene.Camera.SceneCamera;

		ActiveScene.Render( swapChain, default );
	}

	internal static void Shutdown()
	{
		IsClosing = true;
		IsPlaying = false;

		ActiveScene?.Destroy();
		ActiveScene = null;

		IsClosing = false;
	}
}
