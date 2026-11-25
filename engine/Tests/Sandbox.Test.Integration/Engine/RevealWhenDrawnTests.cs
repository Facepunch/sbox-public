using Sandbox.Tasks;
using System.Threading;

namespace EngineTests;

/// <summary>
/// <see cref="BorealisGpuRenderBackend.RevealWhenDrawn(Scene, Func{bool})"/>: the loading screen a game scene's load leaves up
/// on Borealis.Gpu until the game's first frames after it have drawn behind it.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RevealWhenDrawnTests
{
	bool screenBefore;
	Scene scene;

	[TestInitialize]
	public void TestInitialize()
	{
		SyncContext.Init();
		SyncContext.Reset();
		screenBefore = LoadingScreen.IsVisible;
		scene = new Scene();
		LoadingScreen.IsVisible = true;
	}

	[TestCleanup]
	public void TestCleanup()
	{
		LoadingScreen.IsVisible = screenBefore;
		scene.Destroy();
	}

	static void NextFrame()
	{
		Application.FrameCount++;
		for ( int i = 0; i < 3; i++ ) SyncContext.MainThread.ProcessQueue();
	}

	/// <summary>
	/// The screen stays while the frames after the load still make models, shaders or pipelines, then goes at the start of
	/// the second frame with nothing made: one drawn with all of it, so the first frame shown makes nothing. Before, it went
	/// with the load, before the player had spawned: the first frames shown drew the player's view, viewmodel and HUD for
	/// the first time and made what they asked for on the render thread (150-230 ms a frame warm, 2-4.8 s with an empty
	/// pipeline cache on Flatgrass and Construct).
	/// </summary>
	[TestMethod]
	public void StaysUntilTheFramesAfterTheLoadDrawWithNothingToMake()
	{
		var making = true;
		var reveal = BorealisGpuRenderBackend.RevealWhenDrawn( scene, () => making );
		for ( int i = 0; i < 6; i++ ) NextFrame();
		Assert.IsTrue( LoadingScreen.IsVisible, "up while its frames make what they draw" );

		making = false;
		NextFrame();
		Assert.IsTrue( LoadingScreen.IsVisible, "up for a frame drawn with everything made" );

		NextFrame();
		Assert.IsFalse( LoadingScreen.IsVisible, "gone at the next frame's start" );
		Assert.IsTrue( reveal.IsCompletedSuccessfully );
	}

	/// <summary>With nothing to make it stays for <see cref="BorealisGpuRenderBackend.RevealFrames"/> frames, at least two.</summary>
	[TestMethod]
	public void StaysForTheGamesFirstFramesWithNothingToMake()
	{
		var reveal = BorealisGpuRenderBackend.RevealWhenDrawn( scene, () => false );
		var frames = 0;
		while ( LoadingScreen.IsVisible && frames < 20 ) { NextFrame(); frames++; }
		Assert.IsFalse( LoadingScreen.IsVisible );
		Assert.IsTrue( reveal.IsCompletedSuccessfully );
		Assert.AreEqual( System.Math.Max( BorealisGpuRenderBackend.RevealFrames, 2 ), frames, "the frames it waited" );
	}

	/// <summary>
	/// Behind the screen the loaded scene's frames draw as the frames after it will (<c>Game.ExtractRender</c>, on the render
	/// thread), not as a load does: the render thread's first frame of the game, 60-110 ms on Construct, was the first shown.
	/// Any other scene behind a loading screen still draws as a load.
	/// </summary>
	[TestMethod]
	public void TheLoadedScenesFramesDrawAsShownWhileItWaits()
	{
		Assert.IsTrue( Game.DrawsLoading( scene ), "a load before the wait" );
		var reveal = BorealisGpuRenderBackend.RevealWhenDrawn( scene, () => true );
		NextFrame();
		Assert.IsTrue( LoadingScreen.IsVisible );
		Assert.IsFalse( Game.DrawsLoading( scene ), "drawn as shown behind the screen" );

		var other = new Scene();
		try
		{
			Assert.IsTrue( Game.DrawsLoading( other ), "another scene behind the screen is a load" );
		}
		finally
		{
			other.Destroy();
		}

		LoadingScreen.IsVisible = false;
		LoadingScreen.IsVisible = true;
		NextFrame();
		Assert.IsTrue( reveal.IsCompletedSuccessfully );
		Assert.IsNull( BorealisGpuRenderBackend.Revealing, "nothing waits once something else has the screen" );
		Assert.IsTrue( Game.DrawsLoading( scene ), "a load again" );
	}

	/// <summary>Something else showing or hiding the screen meanwhile (a network join) has it: the wait leaves it alone.</summary>
	[TestMethod]
	public void LeavesTheScreenToWhatElseShowsOrHidesIt()
	{
		var reveal = BorealisGpuRenderBackend.RevealWhenDrawn( scene, () => false );
		LoadingScreen.IsVisible = false;
		LoadingScreen.IsVisible = true;
		for ( int i = 0; i < 6; i++ ) NextFrame();
		Assert.IsTrue( reveal.IsCompletedSuccessfully );
		Assert.IsTrue( LoadingScreen.IsVisible, "shown again by something else, so not hidden by the wait" );
	}
}
