using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;

namespace bytePack;

public partial class RoundTrip
{


	[TestMethod]
	public void Array_Values()
	{
		DoRoundTrip( new int[] { 1, 2, 4, 5, 6, 7, 8 } );
		DoRoundTrip( new float[] { 1, 2, 4, 5, 6, 7, 8 } );
		DoRoundTrip( new double[] { 1, 2, 4, 5, 6, 7, 8 } );
		DoRoundTrip( new byte[] { 1, 2, 4, 5, 6, 7, 8 } );
	}

	[TestMethod]
	public void Array_Strings()
	{
		DoRoundTrip( new string[] { "one", "two", "three" } );
	}

	[TestMethod]
	public void Array_Values_Empty()
	{
		DoRoundTrip( new int[0] );
		DoRoundTrip( new float[0] );
		DoRoundTrip( new double[0] );
		DoRoundTrip( new byte[0] );
		DoRoundTrip( new string[0] );
		DoRoundTrip( Array.Empty<int>() );
		DoRoundTrip( Array.Empty<float>() );
		DoRoundTrip( Array.Empty<double>() );
		DoRoundTrip( Array.Empty<byte>() );
		DoRoundTrip( Array.Empty<string>() );
	}


	[TestMethod]
	public void Array_Values_Huge()
	{
		var array = Array.CreateInstance( typeof( global::Vector3 ), 10_000 );

		DoRoundTrip( array );
	}


	[TestMethod]
	public void Array_Objects()
	{
		DoRoundTrip( new object[] { 1 } );
		DoRoundTrip( new object[] { 1, 2000.0, "the", 4, 6.0f } );
		DoRoundTrip( new object[] { 1, 2000.0, "the", 4, 6.0f, global::Vector3.One, null, null, 34, "poops" } );

		var objectArray = new object[] { 1, 2000.0, "the", 4, 6.0f, global::Vector3.One, null, null, 34, "poops" };
		var valueArray = new int[] { 1, 2, 3, 4 };

		DoRoundTrip( new object[] { 1, 2, objectArray, "the", valueArray } );
	}

	[TestMethod]
	public void Array_Objects_Empty()
	{
		DoRoundTrip( new object[0] );
		DoRoundTrip( Array.Empty<object>() );
	}

	[TestMethod]
	public void Array_Objects_Huge()
	{
		var list = new List<object>();

		for ( int i = 0; i < 10_000; i++ )
			list.Add( global::Vector3.Random );

		DoRoundTrip( list.ToArray() );
	}

}
