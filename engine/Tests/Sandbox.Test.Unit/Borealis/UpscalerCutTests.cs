namespace BorealisTests;

/// <summary>
/// A temporal upscaler's camera cuts on Borealis.Gpu (<see cref="BorealisGpuUpscaler.CutDetector"/>): a teleport, a sharp
/// turn, another world or a gap in the frames resets its history; ordinary movement doesn't.
/// </summary>
[TestClass]
public class UpscalerCutTests
{
	[TestMethod]
	public void CutsAreTeleportsTurnsWorldChangesAndGaps()
	{
		var cuts = new BorealisGpuUpscaler.CutDetector();
		var world = new object();
		var start = new Vector3( 100, 200, 64 );
		var facing = Rotation.FromYaw( 90 );

		Assert.IsTrue( cuts.Cut( start, facing, world, 10 ), "the first frame has no history" );
		Assert.IsFalse( cuts.Cut( start + Vector3.Forward * 20, Rotation.FromYaw( 95 ), world, 11 ), "walking and turning" );
		Assert.IsFalse( cuts.Cut( start + Vector3.Forward * 20, Rotation.FromYaw( 95 ), world, 11 ), "rendered again in the same frame" );
		Assert.IsFalse( cuts.Cut( start + Vector3.Forward * 220, Rotation.FromYaw( 130 ), world, 12 ), "fast, but under the limits" );

		Assert.IsTrue( cuts.Cut( start + Vector3.Forward * 2000, Rotation.FromYaw( 130 ), world, 13 ), "a teleport" );
		Assert.IsFalse( cuts.Cut( start + Vector3.Forward * 2010, Rotation.FromYaw( 130 ), world, 14 ), "settled after it" );
		Assert.IsTrue( cuts.Cut( start + Vector3.Forward * 2010, Rotation.FromYaw( 230 ), world, 15 ), "a turn of 100 degrees in a frame" );
		var other = new object();
		Assert.IsTrue( cuts.Cut( start + Vector3.Forward * 2010, Rotation.FromYaw( 230 ), other, 16 ), "another world" );
		Assert.IsFalse( cuts.Cut( start + Vector3.Forward * 2010, Rotation.FromYaw( 230 ), other, 17 ) );
		Assert.IsTrue( cuts.Cut( start + Vector3.Forward * 2010, Rotation.FromYaw( 230 ), other, 20 ), "frames it didn't render in between" );
		Assert.IsFalse( cuts.Cut( start + Vector3.Forward * 2010, Rotation.FromYaw( 230 ), other, 21 ) );
	}
}
