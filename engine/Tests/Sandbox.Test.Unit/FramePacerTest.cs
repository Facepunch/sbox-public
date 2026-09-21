using Sandbox.Engine;

namespace EngineTests;

[TestClass]
public class FramePacerTest
{
	[TestMethod]
	public void WorkAndOversleepDoNotAccumulateDrift()
	{
		var pacer = new FramePacer();
		Assert.AreEqual( 10.0, pacer.GetNextDeadline( 0, 100 ) );
		Assert.AreEqual( 20.0, pacer.GetNextDeadline( 12, 100 ) );
		Assert.AreEqual( 30.0, pacer.GetNextDeadline( 23, 100 ) );
		Assert.AreEqual( 40.0, pacer.GetNextDeadline( 34, 100 ) );
	}

	[TestMethod]
	public void HitchResumesAtTheCurrentTime()
	{
		var pacer = new FramePacer();
		pacer.GetNextDeadline( 0, 100 );

		// A long frame should not produce a burst of catch-up frames.
		Assert.AreEqual( 150.0, pacer.GetNextDeadline( 150, 100 ) );
		Assert.AreEqual( 160.0, pacer.GetNextDeadline( 152, 100 ) );
	}

	[TestMethod]
	public void UncappingDropsTheOldSchedule()
	{
		var pacer = new FramePacer();
		pacer.GetNextDeadline( 0, 100 );
		Assert.AreEqual( 0.0, pacer.GetNextDeadline( 5, 0 ) );
		Assert.AreEqual( 25.0, pacer.GetNextDeadline( 5, 50 ) );
	}

	[TestMethod]
	public void StricterCapsApplyToAnUncappedLoopAndKeepTheirReason()
	{
		var limit = new FrameRateLimit( 0, "fps_max" )
			.Tighten( 120, "fps_max_menu" )
			.Tighten( 60, "fps_max_inactive" )
			.Tighten( 144, "vsync" );

		Assert.AreEqual( 60.0, limit.FramesPerSecond );
		Assert.AreEqual( "fps_max_inactive", limit.Source );
		Assert.AreEqual( limit, limit.Tighten( 0, "disabled" ) );
		Assert.AreEqual( limit, limit.Tighten( 60, "equal" ) );
	}
}
