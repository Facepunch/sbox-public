using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace JsonTests;

/// <summary>
/// Test blob for binary serialization testing
/// </summary>
public class TestBlob : BlobData
{
	public int IntValue { get; set; }
	public string StringValue { get; set; }
	public List<Vector3> Positions { get; set; } = new();

	public override int Version => 1;

	public override void Serialize( ref Writer writer )
	{
		writer.Stream.Write( IntValue );
		writer.Stream.Write( StringValue ?? "" );
		writer.Stream.Write( Positions.Count );
		foreach ( var pos in Positions )
		{
			writer.Stream.Write( pos );
		}
	}

	public override void Deserialize( ref Reader reader )
	{
		IntValue = reader.Stream.Read<int>();
		StringValue = reader.Stream.Read<string>();
		int count = reader.Stream.Read<int>();
		Positions = new List<Vector3>( count );
		for ( int i = 0; i < count; i++ )
		{
			Positions.Add( reader.Stream.Read<Vector3>() );
		}
	}
}

[TestClass]
[DoNotParallelize]
public class BlobSerialization
{
	[TestMethod]
	public void BlobSerializeToNode()
	{
		var blob = new TestBlob
		{
			IntValue = 42,
			StringValue = "Hello Blob",
			Positions = new List<Vector3>
			{
				new Vector3( 1, 2, 3 ),
				new Vector3( 4, 5, 6 )
			}
		};

		var testFilePath = Path.GetTempFileName();

		using var blobs = BlobDataSerializer.Capture();
		var node = Json.ToNode( blob );

		Assert.IsNotNull( node );
		Assert.IsInstanceOfType( node, typeof( JsonObject ) );

		var obj = node as JsonObject;
		Assert.IsTrue( obj.ContainsKey( "$blob" ) );
		Assert.IsTrue( Guid.TryParse( obj["$blob"].ToString(), out _ ) );

		blobs.SaveTo( testFilePath );
		Assert.IsTrue( File.Exists( testFilePath + "_d" ) );

		File.Delete( testFilePath );
		File.Delete( testFilePath + "_d" );
	}

	[TestMethod]
	public void BlobRoundTrip()
	{
		// Arrange
		var originalBlob = new TestBlob
		{
			IntValue = 123,
			StringValue = "Test Data",
			Positions = new List<Vector3>
			{
				new Vector3( 10, 20, 30 ),
				new Vector3( 40, 50, 60 )
			}
		};

		var testFilePath = Path.GetTempFileName();

		JsonNode node;
		using ( var blobs = BlobDataSerializer.Capture() )
		{
			node = Json.ToNode( originalBlob );
			blobs.SaveTo( testFilePath );
		}

		TestBlob deserializedBlob;
		using ( var blobs = BlobDataSerializer.LoadFrom( testFilePath ) )
		{
			deserializedBlob = Json.FromNode( node, typeof( TestBlob ) ) as TestBlob;
		}

		Assert.IsNotNull( deserializedBlob );
		Assert.AreEqual( originalBlob.IntValue, deserializedBlob.IntValue );
		Assert.AreEqual( originalBlob.StringValue, deserializedBlob.StringValue );
		Assert.AreEqual( originalBlob.Positions.Count, deserializedBlob.Positions.Count );

		for ( int i = 0; i < originalBlob.Positions.Count; i++ )
		{
			Assert.AreEqual( originalBlob.Positions[i], deserializedBlob.Positions[i] );
		}

		File.Delete( testFilePath );
		File.Delete( testFilePath + "_d" );
	}

	[TestMethod]
	public void MultipleBlobs()
	{
		var blob1 = new TestBlob { IntValue = 100, StringValue = "Blob 1" };
		var blob2 = new TestBlob { IntValue = 200, StringValue = "Blob 2" };

		var testFilePath = Path.GetTempFileName();

		JsonNode node1, node2;
		using ( var blobs = BlobDataSerializer.Capture() )
		{
			node1 = Json.ToNode( blob1 );
			node2 = Json.ToNode( blob2 );
			blobs.SaveTo( testFilePath );
		}

		TestBlob result1, result2;
		using ( var blobs = BlobDataSerializer.LoadFrom( testFilePath ) )
		{
			result1 = Json.FromNode( node1, typeof( TestBlob ) ) as TestBlob;
			result2 = Json.FromNode( node2, typeof( TestBlob ) ) as TestBlob;
		}

		Assert.IsNotNull( result1 );
		Assert.IsNotNull( result2 );
		Assert.AreEqual( 100, result1.IntValue );
		Assert.AreEqual( 200, result2.IntValue );

		File.Delete( testFilePath );
		File.Delete( testFilePath + "_d" );
	}

	[TestMethod]
	public void BlobUpgrade()
	{
		var testFilePath = Path.GetTempFileName();
		var blobPath = testFilePath + "_d";

		var blobGuid = Guid.NewGuid();

		var blobStream = ByteStream.Create( 256 );
		blobStream.Write( 1 ); // data version
		blobStream.Write( 42 ); // IntValue
		blobStream.Write( "Old Format" ); // StringValue
		var blobData = blobStream.ToArray();
		blobStream.Dispose();

		var fileStream = ByteStream.Create( 512 );

		fileStream.Write( 1 ); // File version
		fileStream.Write( 1 ); // Entry count

		// Calculate offset: Header (8 bytes) + TOC entry (32 bytes)
		long dataOffset = 8 + 32;

		// Write TOC entry
		fileStream.Write( blobGuid.ToByteArray() );
		fileStream.Write( 1 ); // Data version = 1 (old version)
		fileStream.Write( dataOffset );
		fileStream.Write( blobData.Length );

		// Write the blob data
		fileStream.Write( blobData );

		File.WriteAllBytes( blobPath, fileStream.ToArray() );
		fileStream.Dispose();

		// should trigger upgrade
		UpgradeableTestBlob upgradedBlob;
		using ( var blobs = BlobDataSerializer.LoadFrom( testFilePath ) )
		{
			var node = new JsonObject { ["$blob"] = blobGuid.ToString() };
			upgradedBlob = Json.FromNode( node, typeof( UpgradeableTestBlob ) ) as UpgradeableTestBlob;
		}

		Assert.IsNotNull( upgradedBlob );
		Assert.AreEqual( 42, upgradedBlob.IntValue );
		Assert.AreEqual( "Old Format", upgradedBlob.StringValue );
		Assert.IsNotNull( upgradedBlob.Positions );
		Assert.AreEqual( 0, upgradedBlob.Positions.Count );
		Assert.IsTrue( upgradedBlob.WasUpgraded );

		File.Delete( testFilePath );
		File.Delete( blobPath );
	}

	[TestMethod]
	public void ObjectWithBlobProperty()
	{
		var container = new ContainerWithBlob
		{
			Name = "Test Container",
			RegularValue = 999,
			BlobData = new TestBlob
			{
				IntValue = 456,
				StringValue = "Data in blob",
				Positions = new List<Vector3>
				{
					new Vector3( 1, 1, 1 ),
					new Vector3( 2, 2, 2 ),
					new Vector3( 3, 3, 3 )
				}
			}
		};

		var testFilePath = Path.GetTempFileName();

		JsonNode jsonNode;
		using ( var blobs = BlobDataSerializer.Capture() )
		{
			jsonNode = Json.ToNode( container );
			var json = jsonNode.ToJsonString();

			Console.WriteLine( "Generated JSON:" );
			Console.WriteLine( json );

			Assert.IsNotNull( json );
			Assert.IsTrue( json.Contains( "$blob" ), "JSON should contain $blob reference" );
			Assert.IsFalse( json.Contains( "\"IntValue\"" ), "Blob contents should NOT be in JSON" );

			blobs.SaveTo( testFilePath );
			Assert.IsTrue( File.Exists( testFilePath + "_d" ) );
		}

		ContainerWithBlob deserialized;
		using ( var blobs = BlobDataSerializer.LoadFrom( testFilePath ) )
		{
			deserialized = Json.FromNode( jsonNode, typeof( ContainerWithBlob ) ) as ContainerWithBlob;
		}

		Assert.IsNotNull( deserialized );
		Assert.AreEqual( "Test Container", deserialized.Name );
		Assert.AreEqual( 999, deserialized.RegularValue );
		Assert.IsNotNull( deserialized.BlobData );
		Assert.AreEqual( 456, deserialized.BlobData.IntValue );
		Assert.AreEqual( "Data in blob", deserialized.BlobData.StringValue );
		Assert.AreEqual( 3, deserialized.BlobData.Positions.Count );

		File.Delete( testFilePath );
		File.Delete( testFilePath + "_d" );
	}
}

/// <summary>
/// Test blob that supports upgrading from version 1 to version 2
/// </summary>
public class UpgradeableTestBlob : BlobData
{
	public int IntValue { get; set; }
	public string StringValue { get; set; }
	public List<Vector3> Positions { get; set; } = new();
	public bool WasUpgraded { get; set; }

	public override int Version => 2;

	public override void Serialize( ref Writer writer )
	{
		writer.Stream.Write( IntValue );
		writer.Stream.Write( StringValue ?? "" );
		writer.Stream.Write( Positions.Count );
		foreach ( var pos in Positions )
		{
			writer.Stream.Write( pos );
		}
	}

	public override void Deserialize( ref Reader reader )
	{
		IntValue = reader.Stream.Read<int>();
		StringValue = reader.Stream.Read<string>();
		int count = reader.Stream.Read<int>();
		Positions = new List<Vector3>( count );
		for ( int i = 0; i < count; i++ )
		{
			Positions.Add( reader.Stream.Read<Vector3>() );
		}
	}

	public override void Upgrade( ref Reader reader, int fromVersion )
	{
		if ( fromVersion == 1 )
		{
			IntValue = reader.Stream.Read<int>();
			StringValue = reader.Stream.Read<string>();
			Positions = new List<Vector3>();
			WasUpgraded = true;
		}
		else
		{
			Deserialize( ref reader );
		}
	}
}

public class ContainerWithBlob
{
	public string Name { get; set; }
	public int RegularValue { get; set; }
	public TestBlob BlobData { get; set; }
}
