using System;
using System.Text.Json.Serialization;

namespace ResourceTests;

/// <summary>
/// A GameResource that keeps an array in a binary blob alongside the json, like terrain
/// storage does with its maps. Used to test that blob data survives the resource operations.
/// </summary>
[AssetType( Name = "Blob Resource", Extension = "blobres" )]
public class BlobResource : GameResource
{
	[JsonInclude] private DataBlob Blob { get; set; } = new();

	[JsonIgnore]
	public int[] Data
	{
		get => Blob.Data;
		set => Blob.Data = value;
	}

	public int Scalar { get; set; }

	private class DataBlob : BlobData
	{
		public int[] Data { get; set; } = Array.Empty<int>();

		public override void Serialize( ref Writer writer )
		{
			writer.Stream.Write( Data.Length );
			foreach ( var value in Data )
				writer.Stream.Write( value );
		}

		public override void Deserialize( ref Reader reader )
		{
			var count = reader.Stream.Read<int>();
			Data = new int[count];
			for ( int i = 0; i < count; i++ )
				Data[i] = reader.Stream.Read<int>();
		}
	}
}

/// <summary>
/// References an embedded resource the way a component does.
/// </summary>
public class BlobResourceHost
{
	public BlobResource Resource { get; set; }
}

[TestClass]
public class BlobResourceTest
{
	static BlobResource CreateResource()
	{
		var resource = new BlobResource { Scalar = 42, Data = new[] { 5, 10, 15, 20, 25 } };
		resource.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };
		return resource;
	}

	/// <summary>
	/// Copy/paste of an object that embeds a blob resource keeps its blob data: copy stores the
	/// blob inline on the json, paste loads it back. Same path as the editor clipboard.
	/// </summary>
	[TestMethod]
	public void CopyWithBinaryData()
	{
		var source = CreateResource();
		var host = new BlobResourceHost { Resource = source };

		// Copy
		string clipboard;
		using ( var blobs = BlobDataSerializer.Capture() )
		{
			var json = Json.ToNode( host );
			blobs.SaveTo( json );
			clipboard = Json.Serialize( json );
		}

		// Paste
		var node = Json.ParseToJsonObject( clipboard );

		BlobResourceHost result;
		using ( var blobs = BlobDataSerializer.LoadFrom( node ) )
		{
			result = Json.FromNode( node, typeof( BlobResourceHost ) ) as BlobResourceHost;
		}

		Assert.IsNotNull( result?.Resource );
		Assert.AreEqual( source.Scalar, result.Resource.Scalar );
		Assert.IsTrue( result.Resource.Data.SequenceEqual( source.Data ) );
	}

	/// <summary>
	/// CopyFrom copies another resource's state including its blob data, and the copy owns its
	/// own data so editing the source afterwards doesn't touch it. Used when embedding a file.
	/// </summary>
	[TestMethod]
	public void CopyFromWithBinaryData()
	{
		var source = CreateResource();

		var copy = new BlobResource();
		copy.CopyFrom( source );

		Assert.AreEqual( source.Scalar, copy.Scalar );
		Assert.IsTrue( copy.Data.SequenceEqual( source.Data ) );
		Assert.AreNotSame( source.Data, copy.Data );

		// Editing the source must not change the copy.
		source.Data[0] = 999;
		Assert.AreEqual( 5, copy.Data[0] );
	}
}
