namespace BorealisTests;

/// <summary>
/// A view that stops rendering keeps its renderer and targets for some frames and some seconds, so one that goes off screen
/// for a moment at a few hundred frames a second (a water body's refraction) isn't made again from nothing.
/// </summary>
[TestClass]
public class ViewIdleTests
{
	[TestMethod]
	public void ManyFramesInAMomentArentIdle()
	{
		var now = System.Diagnostics.Stopwatch.GetTimestamp();
		Assert.IsFalse( CaptureIdlePolicy.Expired( 10_000, 10_000 - 2 * CaptureIdlePolicy.Frames, now ) );
	}

	[TestMethod]
	public void FewFramesOverManySecondsArentIdle()
	{
		var longAgo = System.Diagnostics.Stopwatch.GetTimestamp() - (long)(2 * CaptureIdlePolicy.Seconds * System.Diagnostics.Stopwatch.Frequency);
		Assert.IsFalse( CaptureIdlePolicy.Expired( 10_000, 10_000 - CaptureIdlePolicy.Frames, longAgo ) );
	}

	[TestMethod]
	public void ManyFramesOverManySecondsAreIdle()
	{
		var longAgo = System.Diagnostics.Stopwatch.GetTimestamp() - (long)(2 * CaptureIdlePolicy.Seconds * System.Diagnostics.Stopwatch.Frequency);
		Assert.IsTrue( CaptureIdlePolicy.Expired( 10_000, 10_000 - 2 * CaptureIdlePolicy.Frames, longAgo ) );
	}
}
