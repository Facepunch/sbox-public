using System;
using System.Globalization;

namespace SystemTests;

/// <summary>
/// Covers countdown boundaries and culture-independent compact formatting.
/// </summary>
[TestClass]
public class TimeSpanExtensionsTest
{
	/// <summary>
	/// Counts down through day, hour, minute and second boundaries without leading zero units.
	/// </summary>
	[TestMethod]
	[DataRow( 0, "", "" )]
	[DataRow( -1, "", "" )]
	[DataRow( 1, "1s", "<1m" )]
	[DataRow( 59, "59s", "<1m" )]
	[DataRow( 60, "1m 00s", "1m" )]
	[DataRow( 245, "4m 05s", "4m" )]
	[DataRow( 3599, "59m 59s", "59m" )]
	[DataRow( 3600, "1h 00m 00s", "1h 00m" )]
	[DataRow( 86399, "23h 59m 59s", "23h 59m" )]
	[DataRow( 86400, "1d 00h 00m 00s", "1d 00h" )]
	[DataRow( 183845, "2d 03h 04m 05s", "2d 03h" )]
	public void CountdownBoundaries( int seconds, string full, string compact )
	{
		var span = TimeSpan.FromSeconds( seconds );

		Assert.AreEqual( full, span.ToCountdownString() );
		Assert.AreEqual( compact, span.ToCountdownString( includeSeconds: false ) );
	}

	/// <summary>
	/// Handles the full duration range and subsecond values without rounding or overflowing.
	/// </summary>
	[TestMethod]
	public void CountdownExtremes()
	{
		Assert.AreEqual( "", TimeSpan.MinValue.ToCountdownString() );
		Assert.AreEqual( "10675199d 02h 48m 05s", TimeSpan.MaxValue.ToCountdownString() );
		Assert.AreEqual( "0s", TimeSpan.FromTicks( 1 ).ToCountdownString() );
		Assert.AreEqual( "<1m", TimeSpan.FromTicks( 1 ).ToCountdownString( includeSeconds: false ) );
	}

	/// <summary>
	/// Keeps the compact UI representation stable across user cultures.
	/// </summary>
	[TestMethod]
	public void CountdownCulture()
	{
		var previous = CultureInfo.CurrentCulture;

		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo( "ar-SA" );
			Assert.AreEqual( "2d 03h 04m 05s", new TimeSpan( 2, 3, 4, 5 ).ToCountdownString() );
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}
}
