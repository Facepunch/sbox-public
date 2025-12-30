using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace bytePack;

public partial class RoundTrip
{
	[TestMethod]
	public void List_Values()
	{
		DoRoundTrip( new List<int> { 1, 2, 4, 5, 6, 7, 8 } );
		DoRoundTrip( new List<float> { 1, 2, 4, 5, 6, 7, 8 } );
		DoRoundTrip( new List<double> { 1, 2, 4, 5, 6, 7, 8 } );
		DoRoundTrip( new List<byte> { 1, 2, 4, 5, 6, 7, 8 } );
	}

	[TestMethod]
	public void List_Strings()
	{
		DoRoundTrip( new List<string> { "one", "two", "three" } );
	}

	[TestMethod]
	public void List_Values_Empty()
	{
		DoRoundTrip( new List<int>() );
		DoRoundTrip( new List<float>() );
		DoRoundTrip( new List<double>() );
		DoRoundTrip( new List<byte>() );
		DoRoundTrip( new List<string>() );
	}


	[TestMethod]
	public void List_Values_Huge()
	{
		var list = new List<global::Vector3>();

		for ( int i = 0; i < 10_000; i++ )
			list.Add( global::Vector3.One );

		DoRoundTrip( list );
	}


	[TestMethod]
	public void List_Objects()
	{
		DoRoundTrip( new List<object> { 1 } );
		DoRoundTrip( new List<object> { 1, 2000.0, "the", 4, 6.0f } );
		DoRoundTrip( new List<object> { 1, 2000.0, "the", 4, 6.0f, global::Vector3.One, null, null, 34, "poops" } );

		var objectArray = new object[] { 1, 2000.0, "the", 4, 6.0f, global::Vector3.One, null, null, 34, "poops" };
		var valueArray = new int[] { 1, 2, 3, 4 };

		DoRoundTrip( new List<object> { 1, 2, objectArray, "the", valueArray } );
	}

	[TestMethod]
	public void List_Objects_Empty()
	{
		DoRoundTrip( new List<object>() );
	}

	[TestMethod]
	public void List_Objects_Huge()
	{
		var list = new List<object>();

		for ( int i = 0; i < 10_000; i++ )
			list.Add( global::Vector3.Random );

		DoRoundTrip( list );
	}

}
