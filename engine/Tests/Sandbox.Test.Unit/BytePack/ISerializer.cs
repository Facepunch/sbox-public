using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using System;
using System.Runtime.CompilerServices;

namespace BytePackTests;

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

abstract class PolymorphicSerializerBase : BytePack.ISerializer
{
	public int Value { get; set; }

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		return new PolymorphicSerializerB { Value = bs.Read<int>() };
	}

	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		bs.Write( (value as PolymorphicSerializerBase)?.Value ?? throw new InvalidOperationException() );
	}
}

sealed class PolymorphicSerializerA : PolymorphicSerializerBase
{
}

sealed class PolymorphicSerializerB : PolymorphicSerializerBase
{
}

abstract class PolymorphicSerializerIntermediate : PolymorphicSerializerBase
{
}

sealed class PolymorphicSerializerLeaf : PolymorphicSerializerIntermediate
{
}

sealed class ReimplementedReader : PolymorphicSerializerBase, BytePack.ISerializer
{
	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		return new PolymorphicSerializerB { Value = bs.Read<int>() };
	}
}

sealed class ReimplementedWriter : PolymorphicSerializerBase, BytePack.ISerializer
{
	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		bs.Write( ((ReimplementedWriter)value).Value + 1 );
	}
}

abstract class TargetTypedSerializer : BytePack.ISerializer
{
	public int Value { get; set; }

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		var value = (TargetTypedSerializer)Activator.CreateInstance( targetType );
		value.Value = bs.Read<int>();
		return value;
	}

	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		bs.Write( ((TargetTypedSerializer)value).Value );
	}
}

sealed class TargetTypedSerializerDerived : TargetTypedSerializer
{
}

// Building tlA/tlB scans every type in Bootstrap's assembly, which is expensive - so this
// class is [DoNotParallelize] (methods run one at a time, never touching tlA/tlB concurrently)
// and the pair is built once for the whole class instead of once per test method.
[TestClass, DoNotParallelize]
public class CustomSerializerTest : BaseRoundTrip
{
	static TypeLibrary tlA;
	static TypeLibrary tlB;

	[ClassInitialize]
	public static void ClassInitialize( TestContext context )
	{
		// test using different type libraries, to avoid caches
		tlA = new TypeLibrary();
		tlA.AddAssembly( typeof( Bootstrap ).Assembly, true );
		tlA.AddAssembly( typeof( CustomSerializerTest ).Assembly, true );

		tlB = new TypeLibrary();
		tlB.AddAssembly( typeof( Bootstrap ).Assembly, true );
		tlB.AddAssembly( typeof( CustomSerializerTest ).Assembly, true );
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

	[TestMethod]
	[DataRow( typeof( PolymorphicSerializerA ), 42 )]
	[DataRow( typeof( PolymorphicSerializerLeaf ), 42 )]
	[DataRow( typeof( ReimplementedWriter ), 43 )]
	public void InheritedReaderCanResolveSiblingType( Type type, int expectedValue )
	{
		var sender = (PolymorphicSerializerBase)Activator.CreateInstance( type );
		sender.Value = 42;
		var bytes = tlA.ToBytes<object>( sender );
		var result = tlB.FromBytes<PolymorphicSerializerBase>( bytes );
		Assert.IsInstanceOfType<PolymorphicSerializerB>( result );
		Assert.AreEqual( expectedValue, result.Value );
	}

	[TestMethod]
	public void ReimplementedReaderRejectsSiblingType()
	{
		var bytes = tlA.ToBytes<object>( new ReimplementedReader { Value = 42 } );
		Assert.IsNull( tlB.FromBytes<object>( bytes ) );
	}

	[TestMethod]
	public void InheritedReaderReceivesConcreteTargetType()
	{
		var bytes = tlA.ToBytes<object>( new TargetTypedSerializerDerived { Value = 42 } );
		var result = tlB.FromBytes<TargetTypedSerializerDerived>( bytes );
		Assert.IsNotNull( result );
		Assert.AreEqual( 42, result.Value );
	}

	[TestMethod]
	public void TypedCallerStillRejectsIncompatibleResolvedReference()
	{
		var bytes = tlA.ToBytes<object>( new PolymorphicSerializerA { Value = 42 } );
		Assert.ThrowsException<InvalidCastException>( () => tlB.FromBytes<PolymorphicSerializerA>( bytes ) );
	}

	[TestMethod]
	public void TypedArrayStillRejectsIncompatibleResolvedReference()
	{
		var bytes = tlA.ToBytes( new[] { new PolymorphicSerializerA { Value = 42 } } );
		Assert.ThrowsException<InvalidCastException>( () => tlB.FromBytes<PolymorphicSerializerA[]>( bytes ) );
	}

	[TestMethod]
	public void CustomStructSerializerRoundTrips()
	{
		var sender = new TargetedInternalMessage
		{
			SenderId = Guid.NewGuid(),
			TargetId = Guid.NewGuid(),
			Data = [1, 2, 3],
			Flags = 7
		};
		var result = tlB.FromBytes<TargetedInternalMessage>( tlA.ToBytes( sender ) );
		Assert.AreEqual( sender.SenderId, result.SenderId );
		Assert.AreEqual( sender.TargetId, result.TargetId );
		Assert.AreEqual( sender.Flags, result.Flags );
		CollectionAssert.AreEqual( sender.Data, result.Data );
	}

	[TestMethod]
	public void CanCreatePackersForEngineSerializers()
	{
		foreach ( var type in typeof( Connection ).Assembly.GetTypes()
			.Where( t => !t.ContainsGenericParameters && t.IsAssignableTo( typeof( BytePack.ISerializer ) ) ) )
		{
			var serializer = tlA.CreateDetachedSerializer( type );
			serializer.Dispose();
		}
	}

	[TestMethod]
	[DataRow( typeof( LocalConnection ), typeof( TcpChannel ) )]
	[DataRow( typeof( TcpChannel ), typeof( LocalConnection ) )]
	[DataRow( typeof( SteamNetwork.SocketConnection ), typeof( LocalConnection ) )]
	[DataRow( typeof( SteamNetwork.IdConnection ), typeof( LocalConnection ) )]
	[DataRow( typeof( LocalConnection ), typeof( LocalConnection ) )]
	public void ConnectionResolvesToReceiversConcreteType( Type senderType, Type receiverType )
	{
		var originalLocal = Connection.Local;
		var id = Guid.NewGuid();
		var sender = CreateConnectionReference( senderType, id );
		var receiver = CreateConnectionReference( receiverType, id );

		try
		{
			Connection.Local = sender;
			var bytes = tlA.ToBytes<object>( sender );
			using var expected = ByteStream.Create( 32 );
			expected.Write( BytePack.Identifier.Runtime );
			expected.Write( tlA.GetType( senderType ).Identity );
			expected.Write( id );
			CollectionAssert.AreEqual( expected.ToArray(), bytes );
			var arrayBytes = tlA.ToBytes( new Connection[] { sender, null } );

			Connection.Local = receiver;
			Assert.AreSame( receiver, tlB.FromBytes<object>( bytes ) );
			Assert.AreSame( receiver, tlB.FromBytes<Connection>( bytes ) );
			var array = tlB.FromBytes<Connection[]>( arrayBytes );
			Assert.AreSame( receiver, array[0] );
			Assert.IsNull( array[1] );
		}
		finally
		{
			Connection.Local = originalLocal;
		}
	}

	[TestMethod]
	public void ConnectionSerializationSurvivesPackerCacheReset()
	{
		var originalLocal = Connection.Local;
		var id = Guid.NewGuid();
		var sender = new LocalConnection( id );
		var receiver = CreateConnectionReference( typeof( TcpChannel ), id );

		try
		{
			Connection.Local = sender;
			var bytes = tlA.ToBytes<Connection>( sender );
			Connection.Local = receiver;
			Assert.AreSame( receiver, tlB.FromBytes<Connection>( bytes ) );

			// Adding an assembly rebuilds the packer cache. Previously sent bytes must
			// still resolve correctly, and rebuilding must not change the wire format.
			tlA.AddAssembly( typeof( CustomSerializerTest ).Assembly, true );
			tlB.AddAssembly( typeof( CustomSerializerTest ).Assembly, true );
			Assert.AreSame( receiver, tlB.FromBytes<Connection>( bytes ) );

			Connection.Local = sender;
			CollectionAssert.AreEqual( bytes, tlA.ToBytes<Connection>( sender ) );
			Connection.Local = receiver;
			var receiverBytes = tlB.ToBytes<Connection>( receiver );
			Connection.Local = sender;
			Assert.AreSame( sender, tlA.FromBytes<Connection>( receiverBytes ) );
		}
		finally
		{
			Connection.Local = originalLocal;
		}
	}

	static Connection CreateConnectionReference( Type type, Guid id )
	{
		// Only the real connection type and ID are needed to exercise BytePack and
		// Connection.Find. Skip constructors so this unit test starts no transports.
		var connection = (Connection)RuntimeHelpers.GetUninitializedObject( type );
		GC.SuppressFinalize( connection );
		connection.UpdateFrom( new ChannelInfo { Id = id } );
		return connection;
	}
}
