using MenuProject.Overlay.Overlays;
using System;

[TestClass]
public class WorkshopDownloadEtaTests
{
	const double OneBytePerSecond = 8.0 / (1024 * 1024);

	[TestMethod]
	[DataRow( 1L, OneBytePerSecond, 1 )]
	[DataRow( 5L, OneBytePerSecond * 2, 2 )]
	[DataRow( 7L, OneBytePerSecond * 2, 4 )]
	[DataRow( 1L, OneBytePerSecond * 8, 1 )]
	[DataRow( 61L, OneBytePerSecond, 61 )]
	public void EstimatesKeepWholeSecondRoundingAndMinimum( long bytesLeft, double mbps, int seconds )
	{
		Assert.AreEqual( TimeSpan.FromSeconds( seconds ), WorkshopDownloadEta.Calculate( bytesLeft, mbps ) );
	}

	[TestMethod]
	[DataRow( 0L )]
	[DataRow( -1L )]
	public void CompletedOrInvalidDownloadHasNoEstimate( long bytesLeft )
	{
		Assert.IsNull( WorkshopDownloadEta.Calculate( bytesLeft, OneBytePerSecond ) );
	}

	[TestMethod]
	[DataRow( 0.0 )]
	[DataRow( -1.0 )]
	[DataRow( double.NaN )]
	[DataRow( double.PositiveInfinity )]
	[DataRow( double.NegativeInfinity )]
	[DataRow( double.Epsilon )]
	[DataRow( 1e-300 )]
	public void UnusableSpeedHasNoEstimate( double mbps )
	{
		Assert.IsNull( WorkshopDownloadEta.Calculate( 1, mbps ) );
	}

	[TestMethod]
	public void LargestWholeSecondEstimateIsRepresentable()
	{
		Assert.AreEqual( TimeSpan.FromSeconds( 922337203685 ), WorkshopDownloadEta.Calculate( 922337203685, OneBytePerSecond ) );
	}

	[TestMethod]
	public void EstimatesBeyondTimeSpanRangeHaveNoEstimate()
	{
		Assert.IsNull( WorkshopDownloadEta.Calculate( long.MaxValue, OneBytePerSecond ) );
		Assert.IsNull( WorkshopDownloadEta.Calculate( 922337203686, OneBytePerSecond ) );
		// 922337203685.5 seconds rounds up past the last representable whole second.
		Assert.IsNull( WorkshopDownloadEta.Calculate( 1844674407371, OneBytePerSecond * 2 ) );
	}
}
