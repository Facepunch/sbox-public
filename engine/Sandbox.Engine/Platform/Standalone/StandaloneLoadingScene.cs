using NativeEngine;

namespace Sandbox;

/// <summary>
/// An exported game's own loading screen: the scene its <c>LoadingScene</c> project setting names, shown in place of
/// the game while <see cref="LoadingScreen"/> is up. It's an ordinary scene with a camera and UI, which reads what's
/// loading from <see cref="LoadingScreen"/>. Without one, the startup splash covers loads instead.
/// </summary>
internal static class StandaloneLoadingScene
{
	static Scene scene;
	static string path;

	/// <summary>
	/// The scene to show, once the game's code and content are loaded. Null for none.
	/// </summary>
	internal static string Path
	{
		get => path;
		set
		{
			path = string.IsNullOrWhiteSpace( value ) ? null : value;
			Close();
		}
	}

	/// <summary>
	/// The loading scene is up and covering the game.
	/// </summary>
	internal static bool IsShowing => scene.IsValid() && scene.Camera.IsValid();

	static bool IsWanted => Application.IsStandalone && !Application.IsHeadless && path is not null && (Standalone.IsLoadingGame || LoadingScreen.IsVisible);

	/// <summary>
	/// Open the loading scene while something's loading and close it after, ticking it while it's open. Every frame on
	/// the main thread, in the game's scope.
	/// </summary>
	internal static void Tick()
	{
		if ( !IsWanted )
		{
			Close();
			return;
		}

		if ( scene is null && !Open() )
			return;

		using ( scene.PushScope() )
		{
			scene.GameTick( RealTime.Delta );
		}
	}

	/// <summary>
	/// Draw the loading scene in place of the game. False if it isn't showing.
	/// </summary>
	internal static bool Render( SwapChainHandle_t swapChain )
	{
		if ( !IsShowing ) return false;

		scene.Render( swapChain, default );
		return true;
	}

	static bool Open()
	{
		var options = new SceneLoadOptions { ShowLoadingScreen = false };
		if ( options.SetScene( path ) )
		{
			// Not pushed: loading the active scene is how a game changes scene, and this one sits beside it
			scene = new Scene { WantsSystemScene = false, IsUnlisted = true };
			if ( scene.Load( options ) ) return true;
		}

		// Don't try again every frame, the splash covers loads instead
		Log.Warning( $"Couldn't open the loading scene '{path}'" );
		Path = null;
		return false;
	}

	static void Close()
	{
		if ( scene is null ) return;

		if ( scene.IsValid() ) scene.Destroy();
		scene = null;
	}
}
