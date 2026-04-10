using System;

namespace SystemTest;

[TestClass]
public class RangedFloatTest
{
	[TestMethod]

	//
	// Legacy format
	//

	[DataRow( "1 1 0", 1f, 1f, RangedFloat.RangeType.Fixed )]
	[DataRow( "1.125 1.125 0", 1.125f, 1.125f, RangedFloat.RangeType.Fixed )]
	[DataRow( "-7.5 -7.5 0", -7.5f, -7.5f, RangedFloat.RangeType.Fixed )]

	// For Fixed, we ignore the second float
	[DataRow( "1 2 0", 1f, 1f, RangedFloat.RangeType.Fixed )]

	[DataRow( "1 2 1", 1f, 2f, RangedFloat.RangeType.Between )]
	[DataRow( "1.125 2 1", 1.125f, 2f, RangedFloat.RangeType.Between )]
	[DataRow( "-8.25 -2.5 1", -8.25f, -2.5f, RangedFloat.RangeType.Between )]

	//
	// New format
	//

	[DataRow( "1", 1f, 1f, RangedFloat.RangeType.Fixed )]
	[DataRow( "-1", -1f, -1f, RangedFloat.RangeType.Fixed )]
	[DataRow( "1.125", 1.125f, 1.125f, RangedFloat.RangeType.Fixed )]
	[DataRow( "-1.125", -1.125f, -1.125f, RangedFloat.RangeType.Fixed )]

	[DataRow( "1 1", 1f, 1f, RangedFloat.RangeType.Between )]
	[DataRow( "1 2", 1f, 2f, RangedFloat.RangeType.Between )]
	[DataRow( "1 -2", 1f, -2f, RangedFloat.RangeType.Between )]
	[DataRow( "1.125 2.125", 1.125f, 2.125f, RangedFloat.RangeType.Between )]
	public void TestParse( string str, float min, float max, RangedFloat.RangeType type )
	{
		var parsed = RangedFloat.Parse( str );

		Assert.AreEqual( type, parsed.Range );
		Assert.AreEqual( min, parsed.Min );
		Assert.AreEqual( max, parsed.Max );
	}

	[TestMethod]
	[DataRow( 1f, null, "1" )]
	[DataRow( 1f, 1f, "1 1" )]
	[DataRow( 0.19851673f, null, "0.198516726" )]
	public void TestToString( float min, float? max, string str )
	{
		var range = max is null ? new RangedFloat( min ) : new RangedFloat( min, max.Value );
		Assert.AreEqual( str, range.ToString() );
	}

	[TestMethod]
	public void StressTestToString()
	{
		const int seed = 0x4cc7e2c8;

		var random = new Random( seed );

		for ( var i = 0; i < 10_000; ++i )
		{
			var src = random.Float() < 0.5f
				? new RangedFloat( random.Float( -1000f, 1000f ), random.Float( -1000f, 1000f ) )
				: new RangedFloat( random.Float( -1000f, 1000f ) );
			var dst = RangedFloat.Parse( src.ToString() );

			Assert.AreEqual( src.ToString(), dst.ToString() );
		}
	}

	/// <summary>
	/// G9 format can produce scientific notation (e.g. 7.247925E-05).
	/// Verify Parse handles these and the ToString/Parse round-trip contract holds.
	/// </summary>
	[TestMethod]
	[DataRow( 7.247925E-05f, null )]
	[DataRow( -7.247925E-05f, null )]
	[DataRow( 1.23456789E+10f, null )]
	[DataRow( -9.99999944E-11f, null )]
	[DataRow( 7.247925E-05f, 1.5f )]
	[DataRow( -1E-06f, 1E+06f )]
	public void ScientificNotationRoundTrip( float min, float? max )
	{
		var src = max is null ? new RangedFloat( min ) : new RangedFloat( min, max.Value );
		var str = src.ToString();
		var dst = RangedFloat.Parse( str );

		Assert.AreEqual( src.Min, dst.Min, $"Min mismatch: \"{str}\"" );
		Assert.AreEqual( src.Max, dst.Max, $"Max mismatch: \"{str}\"" );
		Assert.AreEqual( src.Range, dst.Range, $"Range type mismatch: \"{str}\"" );
		Assert.AreEqual( str, dst.ToString(), $"Double round-trip mismatch" );
	}
}

