using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace bytePack;

#region Structs

// We need to include at least one reference type field in these structs to force them
// to not be unmanaged serializable types.

struct MyStruct
{
	public float DataFloat { get; set; }
	public int DataInt { get; set; }
	public Vector3 DataVector { get; set; }
	public string StringValue { get; set; }
	public List<object> ObjectList { get; set; }

	public override string ToString()
	{
		return $"[{DataFloat}/{DataInt}/{DataVector}/{StringValue}]";
	}
}

struct StaticPropertyStruct
{
	public float DataFloat { get; set; }
	public string StringValue { get; set; }

	public static string StaticProperty { get; set; }

	public bool Equals( ReadOnlyPropertyStruct other )
	{
		return DataFloat.Equals( other.DataFloat ) && StringValue == other.StringValue;
	}

	public override bool Equals( object obj )
	{
		return obj is ReadOnlyPropertyStruct other && Equals( other );
	}

	public override int GetHashCode()
	{
		return HashCode.Combine( DataFloat, StringValue );
	}

	public override string ToString()
	{
		return $"[{DataFloat}/{StringValue}]";
	}
}

readonly record struct RecordStruct( float DataFloat, int DataInt, Vector3 DataVector, string StringValue );

struct ReadOnlyPropertyStruct : IEquatable<ReadOnlyPropertyStruct>
{
	public float DataFloat { get; }
	public string StringValue { get; }

	public ReadOnlyPropertyStruct( float dataFloat, string stringValue )
	{
		DataFloat = dataFloat;
		StringValue = stringValue;
	}

	public bool Equals( ReadOnlyPropertyStruct other )
	{
		return DataFloat.Equals( other.DataFloat ) && StringValue == other.StringValue;
	}

	public override bool Equals( object obj )
	{
		return obj is ReadOnlyPropertyStruct other && Equals( other );
	}

	public override int GetHashCode()
	{
		return HashCode.Combine( DataFloat, StringValue );
	}

	public override string ToString()
	{
		return $"[{DataFloat}/{StringValue}]";
	}
}

struct FieldOnlyStruct : IEquatable<FieldOnlyStruct>
{
	public float DataFloat;
	public string StringValue;

	public bool Equals( FieldOnlyStruct other )
	{
		return DataFloat.Equals( other.DataFloat ) && StringValue == other.StringValue;
	}

	public override bool Equals( object obj )
	{
		return obj is FieldOnlyStruct other && Equals( other );
	}

	public override int GetHashCode()
	{
		return HashCode.Combine( DataFloat, StringValue );
	}

	public override string ToString()
	{
		return $"[{DataFloat}/{StringValue}]";
	}
}

struct PrivateFieldStruct : IEquatable<PrivateFieldStruct>
{
	private float _dataFloat;
	private string _stringValue;

	public PrivateFieldStruct( float dataFloat, string stringValue )
	{
		_dataFloat = dataFloat;
		_stringValue = stringValue;
	}

	public bool Equals( PrivateFieldStruct other )
	{
		return _dataFloat.Equals( other._dataFloat ) && _stringValue == other._stringValue;
	}

	public override bool Equals( object obj )
	{
		return obj is PrivateFieldStruct other && Equals( other );
	}

	public override int GetHashCode()
	{
		return HashCode.Combine( _dataFloat, _stringValue );
	}

	public override string ToString()
	{
		return $"[{_dataFloat}/{_stringValue}]";
	}
}

struct RecursiveStruct : IEquatable<RecursiveStruct>
{
	public float DataFloat { get; set; }
	public string StringValue { get; set; }

	public RecursiveStruct Inverse => new()
	{
		DataFloat = -DataFloat,
		StringValue = new string( StringValue.Reverse().ToArray() )
	};

	public bool Equals( RecursiveStruct other )
	{
		return DataFloat.Equals( other.DataFloat ) && StringValue == other.StringValue;
	}

	public override bool Equals( object obj )
	{
		return obj is RecursiveStruct other && Equals( other );
	}

	public override int GetHashCode()
	{
		return HashCode.Combine( DataFloat, StringValue );
	}

	public override string ToString()
	{
		return $"[{DataFloat}/{StringValue}]";
	}
}

#endregion

class ClassWithJsonIgnore
{
	public string ThisShouldSerialize { get; set; } = "Hello";
	[JsonIgnore] public string ThisShouldNotSerialize { get; set; } = "Default Value";
}

class ClassWithNoSetterProperty
{
	public bool PropertyWithNoSetterTypeA { get; } = true;
	public bool PropertyWithNoSetterTypeB => true;
}

class RecursiveClass
{
	public RecursiveClass Other { get; set; }
}

[TestClass]
public class Runtime : BaseRoundTrip
{
	TypeLibrary tlA;
	TypeLibrary tlB;

	[TestInitialize]
	public void TestInitialize()
	{
		// test using different type libraries, to avoid caches
		tlA = new TypeLibrary();
		tlA.AddAssembly( typeof( Bootstrap ).Assembly, true );
		tlA.AddAssembly( GetType().Assembly, true );

		tlB = new TypeLibrary();
		tlB.AddAssembly( typeof( Bootstrap ).Assembly, true );
		tlB.AddAssembly( GetType().Assembly, true );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		StaticPropertyStruct.StaticProperty = null;
	}

	public override byte[] Serialize( object obj ) => tlA.ToBytes( obj );
	public override object Deserialize( byte[] data ) => tlB.FromBytes<object>( data );

	object GetRandomObjectValue()
	{
		var i = Random.Shared.Int( 5 );

		if ( i == 0 ) return 10.0f;
		if ( i == 1 ) return Vector3.Random;
		if ( i == 2 ) return new MyStruct { StringValue = "Hello" };
		if ( i == 3 ) return null;
		if ( i == 4 ) return "String Value";

		return 100;
	}

	List<object> FillObjectList( int count )
	{
		var list = new List<object>();

		for ( int i = 0; i < count; i++ )
		{
			list.Add( GetRandomObjectValue() );
		}

		return list;
	}

	[TestMethod]
	public void ClassWithJsonIgnore()
	{
		var s = new ClassWithJsonIgnore
		{
			ThisShouldNotSerialize = "Foo",
			ThisShouldSerialize = "Foo"
		};

		DoRoundTrip( s );

		var serialized = Serialize( s );
		var deserialized = Deserialize( serialized ) as ClassWithJsonIgnore;

		Assert.AreEqual( "Foo", deserialized.ThisShouldSerialize );
		Assert.AreEqual( "Default Value", deserialized.ThisShouldNotSerialize );
	}

	[TestMethod]
	public void ClassWithCycle()
	{
		var a = new RecursiveClass();
		var b = new RecursiveClass();

		a.Other = b;
		b.Other = a;

		Assert.ThrowsException<SerializationException>( () => DoRoundTrip( a ) );
	}

	[TestMethod]
	public void ClassWithNoSetterProperty()
	{
		var s = new ClassWithNoSetterProperty();

		DoRoundTrip( s );
	}

	[TestMethod]
	public void MyStruct()
	{
		var s = new MyStruct();

		DoRoundTrip( s );

		s.DataFloat = 67.0f;
		s.DataVector = Vector3.Random;
		s.DataInt = 77;
		s.ObjectList = FillObjectList( 48 );

		DoRoundTrip( s );

		s.DataFloat = 67.0f;
		s.DataVector = Vector3.Random;
		s.DataInt = 77;
		s.ObjectList = new List<object> { new MyStruct() { DataFloat = 1.0f }, new MyStruct() { DataFloat = 2.0f }, new MyStruct() { DataFloat = 3.0f } };

		DoRoundTrip( s );
	}

	[TestMethod]
	public void MyStruct_Array()
	{
		var s = new MyStruct[32];
		s[12].DataFloat = 8;

		DoRoundTrip( s );

		s = new MyStruct[10000];
		s[77].DataFloat = 8;
		DoRoundTrip( s );
	}

	[TestMethod]
	public void MyStruct_Array_object()
	{
		var s = new object[32];
		s[12] = new MyStruct() { DataFloat = 8, ObjectList = FillObjectList( 7 ) };

		DoRoundTrip( s );

		s = new object[10000];
		s[12] = new MyStruct() { DataFloat = 8 };
		DoRoundTrip( s );
	}

	[TestMethod]
	public void MyStruct_List()
	{
		var s = new List<MyStruct>();

		DoRoundTrip( s );

		s.Add( new MyStruct { DataVector = Vector3.Random, ObjectList = FillObjectList( 7 ) } );
		DoRoundTrip( s );

		for ( int i = 0; i < 100; i++ )
			s.Add( new MyStruct { DataVector = Vector3.Random, ObjectList = FillObjectList( 3 ) } );

		DoRoundTrip( s );
	}

	[TestMethod]
	public void MyStruct_List_Object()
	{
		var s = new List<object>();

		DoRoundTrip( s );

		s.Add( new MyStruct { DataVector = Vector3.Random } );
		DoRoundTrip( s );

		for ( int i = 0; i < 100; i++ )
			s.Add( new MyStruct { DataVector = Vector3.Random } );

		DoRoundTrip( s );
	}

	[TestMethod]
	public void RecordStruct_Default()
	{
		var s = new RecordStruct();

		DoRoundTrip( s );
	}

	[TestMethod]
	public void RecordStruct_Populated()
	{
		var s = new RecordStruct
		{
			DataFloat = 67f,
			DataInt = -77,
			DataVector = Vector3.Random,
			StringValue = "Hello, World!"
		};

		DoRoundTrip( s );
	}

	[TestMethod]
	public void StaticPropertyStruct_Default()
	{
		var s = new StaticPropertyStruct();

		// Big string so we'd notice it being serialized

		StaticPropertyStruct.StaticProperty = new string( 'A', 16384 );

		var length = DoRoundTrip( s );

		Assert.IsFalse( length >= 16384, "Static property was serialized!" );
	}

	[TestMethod]
	public void StaticPropertyStruct_Populated()
	{
		var s = new StaticPropertyStruct
		{
			DataFloat = 67f,
			StringValue = "Hello, World!"
		};

		// Big string so we'd notice it being serialized

		StaticPropertyStruct.StaticProperty = new string( 'A', 16384 );

		var length = DoRoundTrip( s );

		Assert.IsFalse( length >= 16384, "Static property was serialized!" );
	}

	[TestMethod]
	public void ReadOnlyPropertyStruct_Default()
	{
		var s = new ReadOnlyPropertyStruct();

		DoRoundTrip( s );
	}

	[TestMethod]
	public void ReadOnlyPropertyStruct_Populated()
	{
		var s = new ReadOnlyPropertyStruct( 67f, "Hello, World!" );

		DoRoundTrip( s );
	}

	[TestMethod]
	public void FieldOnlyStruct_Default()
	{
		var s = new FieldOnlyStruct();

		DoRoundTrip( s );
	}

	[TestMethod]
	public void FieldOnlyStruct_Populated()
	{
		var s = new FieldOnlyStruct { DataFloat = 67f, StringValue = "Hello, World!" };

		DoRoundTrip( s );
	}

	[TestMethod]
	public void PrivateFieldStruct_Default()
	{
		var s = new PrivateFieldStruct();

		DoRoundTrip( s );
	}

	[TestMethod]
	public void PrivateFieldStruct_Populated()
	{
		var s = new PrivateFieldStruct( 67f, "Hello, World!" );

		DoRoundTrip( s );
	}

	[TestMethod]
	public void RecursiveStruct_Default()
	{
		var s = new RecursiveStruct();

		DoRoundTrip( s );
	}

	[TestMethod]
	public void RecursiveStruct_Populated()
	{
		var s = new RecursiveStruct { DataFloat = 67f, StringValue = "Hello, World!" };

		DoRoundTrip( s );
	}
}
