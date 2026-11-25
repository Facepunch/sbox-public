using System;
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

	/// <summary>
	/// A screenshot is converted, encoded and written on a worker, not in the frame it was taken in: on the render thread
	/// that cost each photo a 300-380 ms frame (the sandbox game's camera, bench-low, 2026-11-24).
	/// </summary>
	[TestMethod]
	public void ScreenshotIsSavedOnAWorker()
	{
		var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"screenshot-test-{Guid.NewGuid():N}.png" );
		try
		{
			byte[] rgba = [10, 20, 30, 255, 40, 50, 60, 255];
			var saving = ScreenshotService.SaveInBackground( rgba, ImageFormat.RGBA8888, 2, 1, path );
			Assert.IsTrue( saving.Wait( TimeSpan.FromSeconds( 30 ) ), "saved" );

			Assert.AreNotEqual( Environment.CurrentManagedThreadId, ScreenshotService.SavedOnThread, "not on the thread that took it" );
			Assert.IsTrue( System.IO.File.Exists( path ), "the PNG was written" );
		}
		finally
		{
			System.IO.File.Delete( path );
		}
	}
}
