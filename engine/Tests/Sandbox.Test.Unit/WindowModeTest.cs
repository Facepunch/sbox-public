using Sandbox.Engine;

namespace EngineTests;

[TestClass]
public class WindowModeTest
{
	[TestMethod]
	[DataRow( false, false, 1280, 720, 1280, 720 )]
	[DataRow( false, false, 3840, 2160, 1920, 1080 )]
	[DataRow( false, true, 1280, 720, 1920, 1080 )]
	[DataRow( true, false, 1280, 720, 1280, 720 )]
	[DataRow( true, true, 1280, 720, 1280, 720 )]
	[DataRow( true, false, 3840, 2160, 3840, 2160 )]
	[DataRow( true, true, 0, 0, 1920, 1080 )]
	[DataRow( false, false, -1, 720, 1920, 1080 )]
	public void WindowSizeHonorsFullscreenResolutionAndWindowedBounds( bool fullscreen, bool borderless, int width, int height, int expectedWidth, int expectedHeight )
	{
		var mode = new GameWindow.WindowMode( fullscreen, borderless, width, height );
		Assert.AreEqual( new Vector2( expectedWidth, expectedHeight ), mode.GetSize( new Vector2( 1920, 1080 ) ) );
	}

}
