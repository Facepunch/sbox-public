namespace Sandbox;

public static class MenuScene
{
	public static Scene Scene;

	public static void Startup( string sceneName )
	{
		Log.Info( $"Loading startup scene: {sceneName}" );

		Scene = new Scene();

		using ( Scene.Push() )
		{
			Scene.LoadFromFile( sceneName );
			LoadingScreen.IsVisible = false;
		}
	}

	/// <summary>
	/// Tick the scene. This only happens when the menu is visible
	/// </summary>
	public static void Tick()
	{
		if ( Scene is null ) return;
		if ( !Game.IsMainMenuVisible ) return;

		using ( Scene.Push() )
		{
			Scene.GameTick( RealTime.Delta );
		}
	}

	internal static void Render( SwapChainHandle_t swapChain )
	{
		if ( Scene is null ) return;
		if ( !Game.IsMainMenuVisible ) return;
		if ( Scene.IsLoading )
		{
			Scene.RenderEnvmaps();
			return;
		}

		Scene.Camera?.SceneCamera.EnableEngineOverlays = true;
		SceneCamera.RecordingCamera = Scene.Camera?.SceneCamera;

		using ( Scene.Push() )
		{
			Scene.Render( swapChain, default );
		}
	}
}
