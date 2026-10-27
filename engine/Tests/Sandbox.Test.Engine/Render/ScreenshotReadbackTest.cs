using Sandbox;

namespace RenderTests;

[TestClass]
public class ScreenshotReadbackTest
{
	/// <summary>
	/// Linux swapchains are usually BGRA8888, so the screenshot readback has to be converted
	/// rather than copied straight into the RGBA bitmap.
	/// </summary>
	[TestMethod]
	public void BgraReadbackIsConvertedToRgba()
	{
		byte[] bgra = [10, 20, 30, 255, 40, 50, 60, 128];

		using var bitmap = new Bitmap( 2, 1 );
		Assert.IsTrue( ScreenshotService.CopyReadbackToBitmap( bgra, ImageFormat.BGRA8888, 2, 1, bitmap ) );

		CollectionAssert.AreEqual( new byte[] { 30, 20, 10, 255, 60, 50, 40, 128 }, bitmap.GetBuffer().ToArray() );
	}
}
