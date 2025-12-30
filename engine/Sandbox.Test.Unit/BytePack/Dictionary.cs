using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace bytePack;

public partial class RoundTrip
{
	[TestMethod]
	public void Dictionary_Values()
	{
		DoRoundTrip( new Dictionary<int, int> { { 1, 1 }, { 4, 4 }, { 6, 6 } } );
		DoRoundTrip( new Dictionary<float, int> { { 1, 1 }, { 2, 2 }, { 4, 4 }, { 5, 5 }, { 6, 6 }, { 7, 7 }, { 8, 8 } } );
		DoRoundTrip( new Dictionary<double, int> { { 1, 1 }, { 2, 2 }, { 4, 4 }, { 5, 5 }, { 6, 6 }, { 7, 7 }, { 8, 8 } } );
		DoRoundTrip( new Dictionary<byte, int> { { 1, 1 }, { 2, 2 }, { 4, 4 }, { 5, 5 }, { 6, 6 }, { 7, 7 }, { 8, 8 } } );
	}

	[TestMethod]
	public void Dictionary_Strings()
	{
		DoRoundTrip( new Dictionary<string, int> { { "one", 1 }, { "two", 2 } } );
		DoRoundTrip( new Dictionary<string, string> { { "one", "one" }, { "two", "one" } } );
		DoRoundTrip( new Dictionary<string, Vector3> { { "one", global::Vector3.One }, { "two", global::Vector3.One * 2.0f } } );
	}

	[TestMethod]
	public void Dictionary_Values_Empty()
	{
		DoRoundTrip( new Dictionary<int, int>() );
		DoRoundTrip( new Dictionary<float, int>() );
		DoRoundTrip( new Dictionary<double, int>() );
		DoRoundTrip( new Dictionary<byte, int>() );
		DoRoundTrip( new Dictionary<string, int>() );
	}


	[TestMethod]
	public void Dictionary_Values_Huge()
	{
		var dct = new Dictionary<int, global::Vector3>();

		for ( int i = 0; i < 10_000; i++ )
			dct.Add( i, global::Vector3.One );

		DoRoundTrip( dct );
	}

	[TestMethod]
	public void Dictionary_Objects()
	{
		DoRoundTrip( new Dictionary<int, object> { { 0, 1 } } );
		DoRoundTrip( new Dictionary<object, object> { { 0, 1 }, { 2000.0, "the" }, { 4, 6.0f } } );
		DoRoundTrip( new Dictionary<object, object> { { 0, 1 }, { 2000.0, "the" }, { 4, 6.0f }, { 99.95f, 89 }, { 55, 34 }, { "poops", null } } );

		var objectArray = new object[] { 1, 2000.0, "the", 4, 6.0f, global::Vector3.One, null, null, 34, "poops" };
		var valueArray = new int[] { 1, 2, 3, 4 };

		DoRoundTrip( new Dictionary<object, object> { { 0, 1 }, { 2, objectArray }, { "the", valueArray } } );
	}

	[TestMethod]
	public void Dictionary_Objects_Empty()
	{
		DoRoundTrip( new Dictionary<object, object>() );
	}

	[TestMethod]
	public void Dictionary_Objects_Huge()
	{
		var dct = new Dictionary<object, object>();

		for ( int i = 0; i < 10_000; i++ )
			dct.Add( i, global::Vector3.Random );

		DoRoundTrip( dct );
	}

}
