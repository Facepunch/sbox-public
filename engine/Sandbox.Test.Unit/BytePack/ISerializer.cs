using Sandbox.Engine;
using Sandbox.Internal;
using System;

namespace bytePack;

class MySerializedClass : BytePack.ISerializer
{
	public float DataFloat { get; set; }
	public int DataInt { get; set; }
	public Vector3 DataVector { get; set; }
	public string StringValue { get; set; }

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		var c = new MySerializedClass();
		c.DataFloat = bs.Read<float>();
		c.DataInt = bs.Read<int>();
		c.DataVector = bs.Read<Vector3>();
		c.StringValue = bs.Read<string>();
		return c;
	}

	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		if ( value is not MySerializedClass c )
			throw new NotImplementedException();

		bs.Write( c.DataFloat );
		bs.Write( c.DataInt );
		bs.Write( c.DataVector );
		bs.Write( c.StringValue );
	}

	public override string ToString()
	{
		return $"[{DataFloat}/{DataInt}/{DataVector}/{StringValue}]";
	}
}

[TestClass]
public class ISerializer : BaseRoundTrip
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

	public override byte[] Serialize( object obj ) => tlA.ToBytes( obj );
	public override object Deserialize( byte[] data ) => tlB.FromBytes<object>( data );

	[TestMethod]
	public void MySerializedClass()
	{
		var s = new MySerializedClass();

		DoRoundTrip( s );

		s.DataFloat = 67.0f;
		s.DataVector = Vector3.Random;
		s.DataInt = 77;
		s.StringValue = "Test1";

		DoRoundTrip( s );

		s.DataFloat = 67.0f;
		s.DataVector = Vector3.Random;
		s.DataInt = 77;
		s.StringValue = "Test2";

		DoRoundTrip( s );
	}
}
