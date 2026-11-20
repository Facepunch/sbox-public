using Sandbox.Engine;

namespace SceneTests.Components;

/// <summary>
/// When Borealis counts the game's scene as loading (<see cref="BorealisGpuRenderBackend.GameLoading"/>): loading budgets,
/// loading the scene ahead and native's envmap order apply only then.
/// </summary>
[TestClass]
[DoNotParallelize]
public class BorealisGameLoadingTests
{
	/// <summary>
	/// A game instance whose load nothing finished (a game started without a scene load, then given a map through a
	/// <c>MapInstance</c>) doesn't make its loaded scene count as loading once the loading screen is down: the engine draws
	/// it as usual then. Before, its frames kept a loading screen's budgets for ever (100 ms of model making a frame) and its
	/// world never counted as shown. The loading screen still counts.
	/// </summary>
	[TestMethod]
	public void GameInstanceStillLoadingDoesNotHoldTheShownScene()
	{
		var previousInstance = IGameInstance.Current;
		var previousScreen = LoadingScreen.IsVisible;
		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			IGameInstance.Current = new GameInstance( "test.game", GameLoadingFlags.Host );
			LoadingScreen.IsVisible = false;
			Assert.IsTrue( IGameInstance.Current.IsLoading, "nothing finished the game's load" );
			Assert.IsFalse( scene.IsLoading );

			Assert.IsFalse( BorealisGpuRenderBackend.GameLoading( scene ), "a scene on screen isn't loading" );

			LoadingScreen.IsVisible = true;
			Assert.IsTrue( BorealisGpuRenderBackend.GameLoading( scene ), "behind the loading screen it is" );
		}
		finally
		{
			LoadingScreen.IsVisible = previousScreen;
			IGameInstance.Current = previousInstance;
			scene.Destroy();
		}
	}
}
