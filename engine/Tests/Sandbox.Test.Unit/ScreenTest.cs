namespace EngineTests;

[TestClass]
public class ScreenTest
{
	[TestMethod]
	public void AspectIsFiniteBeforeScreenSizeIsKnown()
	{
		var oldSize = Screen.Size;

		try
		{
			// Before first play the swapchain reports (0, 0) — https://github.com/Facepunch/sbox/issues/5319
			Screen.Size = new Vector2( 0, 0 );
			Assert.AreEqual( 1f, Screen.Aspect );
			Assert.IsTrue( float.IsFinite( Screen.CreateVerticalFieldOfView( 90f ) ) );

			Screen.Size = new Vector2( 1920, 1080 );
			Assert.AreEqual( 1920f / 1080f, Screen.Aspect );
		}
		finally
		{
			Screen.Size = oldSize;
		}
	}

	[TestMethod]
	public void CreateVerticalFieldOfViewWithExplicitAspect()
	{
		// 90° through the inverse 16:9 aspect gives the classic 58.72° vertical fov
		Assert.AreEqual( 58.7155f, Screen.CreateVerticalFieldOfView( 90f, 1080f / 1920f ), 0.001f );
	}
}
