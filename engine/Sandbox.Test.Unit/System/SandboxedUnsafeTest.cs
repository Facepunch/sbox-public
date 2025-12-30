using System;
using System.Numerics;

namespace SystemTest;

#pragma warning disable CS0649

[TestClass]
public class SandboxedUnsafeTest
{
	[TestMethod]
	[DataRow( typeof( bool ) )]
	[DataRow( typeof( int ) )]
	[DataRow( typeof( uint ) )]
	[DataRow( typeof( float ) )]
	[DataRow( typeof( double ) )]
	[DataRow( typeof( Vector3 ) )]
	[DataRow( typeof( Vector2 ) )]
	[DataRow( typeof( Vector4 ) )]
	[DataRow( typeof( Quaternion ) )]
	[DataRow( typeof( Angles ) )]
	[DataRow( typeof( Transform ) )]
	[DataRow( typeof( MySafeStruct ) )]
	public void SafeTypes( System.Type t )
	{
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod( t ) );
	}

	[TestMethod]
	[DataRow( typeof( IntPtr ) )]
	[DataRow( typeof( UIntPtr ) )]
	[DataRow( typeof( void* ) )]
	[DataRow( typeof( System.RuntimeFieldHandle ) )]
	[DataRow( typeof( MyUnsafeStruct ) )]
	[DataRow( typeof( MyUnsafeStructProperties ) )]
	[DataRow( typeof( MyUnsafeStructArrays ) )]
	public void UnsafeTypes( System.Type t )
	{
		Assert.IsFalse( SandboxedUnsafe.IsAcceptablePod( t ) );
	}

	[TestMethod]
	public void GenericTest()
	{
		//Assert.IsFalse( SandboxedUnsafe.IsAcceptablePod<Type>() );
		Assert.IsFalse( SandboxedUnsafe.IsAcceptablePod<IntPtr>() );
		Assert.IsFalse( SandboxedUnsafe.IsAcceptablePod<UIntPtr>() );
		//Assert.IsFalse( SandboxedUnsafe.IsAcceptablePod<void*>() );
		Assert.IsFalse( SandboxedUnsafe.IsAcceptablePod<System.RuntimeFieldHandle>() );
		Assert.IsFalse( SandboxedUnsafe.IsAcceptablePod<MyUnsafeStruct>() );
		Assert.IsFalse( SandboxedUnsafe.IsAcceptablePod<MyUnsafeStructProperties>() );
		//Assert.IsFalse( SandboxedUnsafe.IsAcceptablePod<MyUnsafeStructArrays>() );

		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<bool>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<int>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<uint>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<float>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<double>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<Vector3>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<Vector2>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<Vector4>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<Quaternion>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<Angles>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<Transform>() );
		Assert.IsTrue( SandboxedUnsafe.IsAcceptablePod<MySafeStruct>() );
	}

	struct MySafeStruct
	{
		public bool One;
		public float Two;
		public Vector3 OtherStuff;
	}

	unsafe struct MyUnsafeStruct
	{
		public void* One;
		public delegate* unmanaged< int, int > functionPointer;
	}

	unsafe struct MyUnsafeStructProperties
	{
		public void* One { get; set; }
	}

	unsafe struct MyUnsafeStructArrays
	{
		public IntPtr[] One { get; set; }
	}

}
