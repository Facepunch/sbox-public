namespace EngineTests;

/// <summary>
/// Without Source 2's device (the game's empty one), what the engine asks of a device is answered by the drawing one.
/// </summary>
[TestClass]
public class BorealisGpuDeviceQueries
{
	[TestMethod]
	public void VideoMemoryIsTheDrawingDevicesOwn()
	{
		TestGraphics.Require();

		Assert.IsTrue( Graphics.VideoMemoryBudget > 0, "a budget" );
		Assert.IsTrue( Graphics.VideoMemoryUsed > 0, "some used" );
	}

	[TestMethod]
	public void FlushWaitsForTheDrawingDevice()
	{
		TestGraphics.Require();
		Graphics.FlushGPU();
	}
}
