using Sandbox.Rendering.Bridge;

namespace BorealisTests;

/// <summary>
/// A loading game scene's unseen renders draw at the game window's size and samples, so they run before any camera has drawn
/// into the window (the menu draws no scene).
/// </summary>
[TestClass]
public class WarmTargetTests
{
	[TestMethod]
	public void TheGameWindowsSizeBeforeAnyCameraDrew()
	{
		Assert.IsTrue( SceneBridge.WarmTarget( new Vector2( 1600, 900 ), 4, Vector2.Zero, 1, out var size, out var samples ) );
		Assert.AreEqual( new Vector2( 1600, 900 ), size );
		Assert.AreEqual( 4, samples );
	}

	[TestMethod]
	public void TheGameWindowOverTheLastCamerasWindow()
	{
		Assert.IsTrue( SceneBridge.WarmTarget( new Vector2( 1600, 900 ), 2, new Vector2( 640, 360 ), 1, out var size, out var samples ) );
		Assert.AreEqual( new Vector2( 1600, 900 ), size );
		Assert.AreEqual( 2, samples );
	}

	[TestMethod]
	public void TheLastCamerasWindowWithoutAGameWindow()
	{
		Assert.IsTrue( SceneBridge.WarmTarget( null, 1, new Vector2( 640, 360 ), 4, out var size, out var samples ) );
		Assert.AreEqual( new Vector2( 640, 360 ), size );
		Assert.AreEqual( 4, samples );

		// A minimised game window counts as none
		Assert.IsTrue( SceneBridge.WarmTarget( Vector2.Zero, 1, new Vector2( 640, 360 ), 4, out size, out _ ) );
		Assert.AreEqual( new Vector2( 640, 360 ), size );
	}

	[TestMethod]
	public void NothingWithNeither()
	{
		Assert.IsFalse( SceneBridge.WarmTarget( null, 1, Vector2.Zero, 1, out _, out _ ) );
	}
}
