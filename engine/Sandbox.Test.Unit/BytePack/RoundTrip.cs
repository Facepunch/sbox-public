using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Collections;
using System.Diagnostics;
using System.Text.Json;

namespace bytePack;

public partial class RoundTrip : BaseRoundTrip
{

}


public class BaseRoundTrip
{
	void CompareObjects( object a, object b )
	{
		if ( a == null || b == null )
		{
			Assert.AreEqual( a, b );
			return;
		}

		var ta = a.GetType();
		var tb = b.GetType();

		Assert.AreEqual( ta, tb );

		var ja = JsonSerializer.Serialize( a );
		var jb = JsonSerializer.Serialize( b );

		Assert.AreEqual( ja, jb );
	}

	TypeLibrary typeLibrary;

	public BaseRoundTrip()
	{
		typeLibrary = new TypeLibrary();
		typeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		typeLibrary.AddAssembly( typeof( Bootstrap ).Assembly, false );
		typeLibrary.AddAssembly( GetType().Assembly, true );
	}

	public virtual byte[] Serialize( object obj )
	{
		return typeLibrary.ToBytes( obj );
	}

	public virtual object Deserialize( byte[] data )
	{
		return typeLibrary.FromBytes<object>( data );
	}

	public int DoRoundTrip( object obj )
	{
		var serialize_time = Stopwatch.StartNew();
		var memory = Serialize( obj );
		serialize_time.Stop();

		var deserialize_time = Stopwatch.StartNew();
		var returnValue = Deserialize( memory );
		deserialize_time.Stop();

		if ( obj is Array array )
		{
			var array2 = returnValue as Array;

			CompareObjects( array, array2 );
			Console.WriteLine( $"{array.Length} elements, {memory.Length}b [{serialize_time.Elapsed.TotalMilliseconds:0.00}ms/{deserialize_time.Elapsed.TotalMilliseconds:0.00}ms]" );
			return memory.Length;
		}

		if ( obj is IList list )
		{
			var list2 = returnValue as IList;

			CompareObjects( list, list2 );
			Console.WriteLine( $"{list.Count} elements, {memory.Length}b [{serialize_time.Elapsed.TotalMilliseconds:0.00}ms/{deserialize_time.Elapsed.TotalMilliseconds:0.00}ms]" );
			return memory.Length;
		}

		Console.WriteLine( $"{memory.Length}b [{serialize_time.Elapsed.TotalMilliseconds:0.00}ms/{deserialize_time.Elapsed.TotalMilliseconds:0.00}ms]" );
		CompareObjects( obj, returnValue );
		return memory.Length;
	}

	public int DoRoundTrip<T>( IEquatable<T> obj )
	{
		var serialize_time = Stopwatch.StartNew();
		var memory = Serialize( obj );
		serialize_time.Stop();

		var deserialize_time = Stopwatch.StartNew();
		var returnValue = Deserialize( memory );
		deserialize_time.Stop();

		Console.WriteLine( $"{memory.Length}b [{serialize_time.Elapsed.TotalMilliseconds:0.00}ms/{deserialize_time.Elapsed.TotalMilliseconds:0.00}ms]" );
		Assert.AreEqual( obj, returnValue );

		return memory.Length;
	}
}
