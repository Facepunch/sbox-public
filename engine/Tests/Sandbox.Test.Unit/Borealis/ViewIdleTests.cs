using Sandbox.Rendering.Bridge;

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
		Assert.IsFalse( SceneBridge.IdleLongEnough( 10_000, 10_000 - 2 * SceneBridge.ViewIdleFrames, now ) );
	}

	[TestMethod]
	public void FewFramesOverManySecondsArentIdle()
	{
		var longAgo = System.Diagnostics.Stopwatch.GetTimestamp() - (long)(2 * SceneBridge.ViewIdleSeconds * System.Diagnostics.Stopwatch.Frequency);
		Assert.IsFalse( SceneBridge.IdleLongEnough( 10_000, 10_000 - SceneBridge.ViewIdleFrames, longAgo ) );
	}

	[TestMethod]
	public void ManyFramesOverManySecondsAreIdle()
	{
		var longAgo = System.Diagnostics.Stopwatch.GetTimestamp() - (long)(2 * SceneBridge.ViewIdleSeconds * System.Diagnostics.Stopwatch.Frequency);
		Assert.IsTrue( SceneBridge.IdleLongEnough( 10_000, 10_000 - 2 * SceneBridge.ViewIdleFrames, longAgo ) );
	}
}
