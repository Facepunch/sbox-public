using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Collections;
using System.Diagnostics;
using System.Text.Json;

namespace BytePackTests;

// 51 test methods across the partial files in this folder all round-trip through the same
// TypeLibrary, and building one scans the whole engine assembly - so this class is
// [DoNotParallelize] (methods run one at a time, never touching typeLibrary concurrently)
// and it's built once for the whole class instead of once per test method.
[DoNotParallelize]
public partial class RoundTripTest : BaseRoundTrip
{
	static TypeLibrary typeLibrary;

	[ClassInitialize]
	public static void ClassInitialize( TestContext context )
	{
		typeLibrary = new TypeLibrary();
		typeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		typeLibrary.AddAssembly( typeof( Bootstrap ).Assembly, false );
		typeLibrary.AddAssembly( typeof( RoundTripTest ).Assembly, true );
	}

	public override byte[] Serialize( object obj ) => typeLibrary.ToBytes( obj );
	public override object Deserialize( byte[] data ) => typeLibrary.FromBytes<object>( data );
}


public class BaseRoundTrip
{
	// IncludeFields matters: types like Vertex expose public fields rather than
	// properties, and the default options would serialize them as "{}" - making
	// the comparison below pass without comparing any data at all.
	static readonly JsonSerializerOptions CompareOptions = new() { IncludeFields = true };

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

		var ja = JsonSerializer.Serialize( a, CompareOptions );
		var jb = JsonSerializer.Serialize( b, CompareOptions );

		Assert.AreEqual( ja, jb );
	}

	TypeLibrary typeLibrary;

	// Lazy: subclasses that override both Serialize and Deserialize (to test against
	// their own TypeLibrary instances) never touch this, so building it eagerly in the
	// constructor was wasted work on every one of their test methods.
	TypeLibrary TypeLibrary => typeLibrary ??= CreateTypeLibrary();

	static TypeLibrary CreateTypeLibrary()
	{
		var typeLibrary = new TypeLibrary();
		typeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		typeLibrary.AddAssembly( typeof( Bootstrap ).Assembly, false );
		typeLibrary.AddAssembly( typeof( BaseRoundTrip ).Assembly, true );
		return typeLibrary;
	}

	public virtual byte[] Serialize( object obj )
	{
		return TypeLibrary.ToBytes( obj );
	}

	public virtual object Deserialize( byte[] data )
	{
		return TypeLibrary.FromBytes<object>( data );
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
