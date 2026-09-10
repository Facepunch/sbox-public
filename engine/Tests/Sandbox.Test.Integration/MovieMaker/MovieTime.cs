using System;
using System.Linq;
using Sandbox.MovieMaker;

namespace MovieMakerTests;

#nullable enable

[TestClass]
public sealed class MovieTimeTest
{
	[TestMethod]
	public void TimeRangeListUnionWithEmpty()
	{
		var first = new MovieTimeRange[] { new( 0d, 1d ) };
		var second = Array.Empty<MovieTimeRange>();

		var union = first.Union( second ).ToArray();

		Assert.AreEqual( 1, union.Length );
		Assert.AreEqual( 0d, union[0].Start );
		Assert.AreEqual( 1d, union[0].End );
	}

	[TestMethod]
	[DataRow( 0.0, 0 )]
	[DataRow( -0.1, -1 )]
	[DataRow( -0.9, -1 )]
	[DataRow( -1, -1 )]
	[DataRow( -1.1, -2 )]
	[DataRow( -1.9, -2 )]
	public void GetFrameIndexNegativeFromRate( double time, int expectedIndex )
	{
		Assert.AreEqual( expectedIndex, MovieTime.FromSeconds( time ).GetFrameIndex( 1 ) );
	}

	[TestMethod]
	[DataRow( 0.0, 0 )]
	[DataRow( -0.1, -1 )]
	[DataRow( -0.9, -1 )]
	[DataRow( -1, -1 )]
	[DataRow( -1.1, -2 )]
	[DataRow( -1.9, -2 )]
	public void GetFrameIndexNegativeFromFrameDuration( double time, int expectedIndex )
	{
		Assert.AreEqual( expectedIndex, MovieTime.FromSeconds( time ).GetFrameIndex( MovieTime.FromSeconds( 1d ) ) );
	}
}
