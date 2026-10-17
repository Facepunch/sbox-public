using System;

namespace BytePackTests;

[TestClass]
public class ValueAllocationsTest
{
	[TestMethod]
	public void PodValuesSerializeWithoutBoxing()
	{
		var packer = new BytePack();
		var stream = ByteStream.Create( 64 * 1024 );

		try
		{
			packer.SerializeTo( ref stream, Vector3.One );
			packer.SerializeTo( ref stream, Rotation.Identity );
			stream.Position = 0;

			var before = GC.GetAllocatedBytesForCurrentThread();

			for ( var i = 0; i < 1000; i++ )
			{
				packer.SerializeTo( ref stream, new Vector3( i, 0, 0 ) );
				packer.SerializeTo( ref stream, Rotation.FromYaw( i ) );
			}

			var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

			Assert.AreEqual( 0, allocated );
		}
		finally
		{
			stream.Dispose();
		}
	}

	[TestMethod]
	public void PodValuesDeserializeWithoutBoxing()
	{
		var packer = new BytePack();
		var position = packer.Serialize( new Vector3( 1, 2, 3 ) );
		var rotation = packer.Serialize( Rotation.FromYaw( 45 ) );

		Assert.AreEqual( new Vector3( 1, 2, 3 ), packer.Deserialize<Vector3>( position ) );
		Assert.AreEqual( Rotation.FromYaw( 45 ), packer.Deserialize<Rotation>( rotation ) );

		var before = GC.GetAllocatedBytesForCurrentThread();

		for ( var i = 0; i < 1000; i++ )
		{
			packer.Deserialize<Vector3>( position );
			packer.Deserialize<Rotation>( rotation );
		}

		Assert.AreEqual( 0, GC.GetAllocatedBytesForCurrentThread() - before );
	}

	[TestMethod]
	public void TypedDeserializeFallsBackForOtherTypes()
	{
		var packer = new BytePack();

		Assert.AreEqual( "hello", packer.Deserialize<string>( packer.Serialize( "hello" ) ) );
		Assert.AreEqual( (object)7, packer.Deserialize<object>( packer.Serialize( 7 ) ) );
		Assert.ThrowsException<InvalidCastException>( () => packer.Deserialize<float>( packer.Serialize( 7 ) ) );
	}

	[TestMethod]
	public void PodValuesKeepTheBoxedWireFormat()
	{
		var packer = new BytePack();
		var transform = new Transform( new Vector3( 1, 2, 3 ), Rotation.FromPitch( 30 ), 2 );

		CollectionAssert.AreEqual( packer.Serialize<object>( transform ), packer.Serialize( transform ) );
		CollectionAssert.AreEqual( packer.Serialize<object>( 7 ), packer.Serialize( 7 ) );
		Assert.AreEqual( transform, packer.Deserialize( packer.Serialize( transform ) ) );
	}
}
