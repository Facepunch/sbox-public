using Sandbox.Engine;
using Sandbox.Internal;
using System.Collections.Generic;

namespace BytePackTests;

struct MyCustomStruct
{
	public float DataFloat { get; set; }
	public int DataInt { get; set; }
	public Vector3 DataVector { get; set; }
}

// Building tlA/tlB scans every type in Bootstrap's assembly, which is expensive - so this
// class is [DoNotParallelize] (methods run one at a time, never touching tlA/tlB concurrently)
// and the pair is built once for the whole class instead of once per test method.
[TestClass, DoNotParallelize]
public class RuntimeValueTest : BaseRoundTrip
{
	static TypeLibrary tlA;
	static TypeLibrary tlB;

	[ClassInitialize]
	public static void ClassInitialize( TestContext context )
	{
		// test using different type libraries, to avoid caches
		tlA = new TypeLibrary();
		tlA.AddAssembly( typeof( Bootstrap ).Assembly, true );
		tlA.AddAssembly( typeof( RuntimeValueTest ).Assembly, true );

		tlB = new TypeLibrary();
		tlB.AddAssembly( typeof( Bootstrap ).Assembly, true );
		tlB.AddAssembly( typeof( RuntimeValueTest ).Assembly, true );
	}

	public override byte[] Serialize( object obj ) => tlA.ToBytes( obj );
	public override object Deserialize( byte[] data ) => tlB.FromBytes<object>( data );


	[TestMethod]
	public void MyCustomStruct()
	{
		var s = new MyCustomStruct();
		s.DataFloat = 67.0f;
		s.DataVector = Vector3.Random;
		s.DataInt = 77;

		DoRoundTrip( s );
	}

	[TestMethod]
	public void MyCustomStruct_Array()
	{
		var s = new MyCustomStruct[32];
		DoRoundTrip( s );

		s = new MyCustomStruct[10000];
		DoRoundTrip( s );
	}

	[TestMethod]
	public void MyCustomStruct_List()
	{
		var s = new List<MyCustomStruct>();

		for ( int i = 0; i < 100; i++ )
			s.Add( new MyCustomStruct { DataVector = Vector3.Random } );

		DoRoundTrip( s );
	}

	[TestMethod]
	public void Vertex()
	{
		var s = new Vertex();
		DoRoundTrip( s );
	}

	[TestMethod]
	public void Vertex_Array()
	{
		var s = new Vertex[32];
		DoRoundTrip( s );

		s = new Vertex[10000];
		DoRoundTrip( s );
	}


	[TestMethod]
	public void Vertex_List()
	{
		var s = new List<Vertex>();

		for ( int i = 0; i < 100; i++ )
			s.Add( new Sandbox.Vertex( Vector3.Random ) );

		DoRoundTrip( s );
	}

}
