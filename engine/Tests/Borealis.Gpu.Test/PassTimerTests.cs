using Borealis.Backend;
using Borealis.Gpu;

namespace BorealisGpuTests;

/// <summary>
/// The pass timer's scopes belong to the frame they began in: a scene can start recording before the engine's frame
/// moves the device on (a scene panel drawn before the window acquires), so a scope can end after the next frame began.
/// </summary>
[TestClass]
public class PassTimerTests : GpuTest
{
	[TestMethod]
	public void AScopeEndsInTheFrameItBeganIn()
	{
		if ( !Device.Capabilities.Timestamps ) Assert.Inconclusive( "No timestamps on this device" );

		using var timer = new PassTimer( Device );
		timer.BeginFrame( 1 );
		var list = Device.BeginCommandList( QueueType.Graphics, "Timed" );
		timer.ResetIn( list );
		var scope = timer.Begin( list, "Layer", PassTimer.ScopeKind.Pass );

		timer.BeginFrame( 2 );
		timer.End( list, scope );
		Device.Submit( list );
		Device.WaitIdle();

		var timings = new List<PassTimer.Timing>();
		Assert.IsTrue( timer.TryRead( 1, timings ) );
		Assert.AreEqual( 1, timings.Count );
		Assert.AreEqual( "Layer", timings[0].Name );
		Assert.IsTrue( timings[0].Ms >= 0 );

		Assert.IsTrue( timer.TryRead( 2, timings ) );
		Assert.AreEqual( 0, timings.Count );
	}
}
