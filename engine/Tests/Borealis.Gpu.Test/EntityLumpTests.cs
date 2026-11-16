using Borealis.Source2.Resources;
using Borealis.Source2.World;
using System.IO;
using System.Text;

namespace BorealisGpuTests;

/// <summary>
/// KV3 binary blobs and the entity key values compiled maps keep in them (<see cref="EntityLump"/>), from blocks written
/// here, so these need no content.
/// </summary>
[TestClass]
public class EntityLumpTests
{
	/// <summary>
	/// A KV3 block of an entity lump's shape, its one entity's key values a blob after the main section, reads back
	/// uncompressed and as LZ4 (the blob as a stream of chunks); the entity's keys stored by hash and by name both read.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void BlobsAndEntityKeyValuesRead( bool lz4 )
	{
		var entity = EntityBlob();
		var root = Kv3.Read( Lump( entity, lz4 ) ) as Dictionary<string, object>;
		Assert.IsNotNull( root );
		var entries = root.Get( "m_entityKeyValues" ) as object[];
		Assert.AreEqual( 1, entries?.Length );
		var blob = ((Dictionary<string, object>)entries[0]).Get( "m_keyValuesData" ) as byte[];
		CollectionAssert.AreEqual( entity, blob, "the blob reads back byte for byte" );

		var e = MapEntity.Read( blob );
		Assert.AreEqual( "light_spot", e.ClassName );
		Assert.AreEqual( new System.Numerics.Vector3( 1, 2, 3 ), e.Origin );
		Assert.AreEqual( 37, e.Float( "bakelightindex", -1 ) );
		Assert.AreEqual( 0.5f, e.Float( "BakeLightIndexScale" ), "keys are found whatever their case" );
		Assert.AreEqual( new System.Numerics.Vector4( 241, 236, 213, 255 ), e.Color( "color" ) );
		Assert.AreEqual( 512, e.Float( "range" ), "a key stored with its name" );
		Assert.AreEqual( 808657659, e.Int( "handshake" ), "a whole number past a float's precision reads exactly" );
		Assert.AreEqual( 37, e.Int( "bakelightindex" ), "and one stored as a string" );
		Assert.AreEqual( -1, e.Int( "missing", -1 ) );
		Assert.IsNull( e["missing"] );
	}

	/// <summary>
	/// One entity's key values as compiled: version 1, two keys by hash and four more, then one key with its name.
	/// </summary>
	static byte[] EntityBlob()
	{
		using var w = new BinaryWriter( new MemoryStream() );
		w.Write( 1u );
		w.Write( 6u );
		w.Write( 1u );

		w.Write( MapEntity.Hash( "classname" ) );
		w.Write( 30u );
		w.Write( Encoding.UTF8.GetBytes( "light_spot\0" ) );

		w.Write( MapEntity.Hash( "origin" ) );
		w.Write( 3u );
		w.Write( 1f );
		w.Write( 2f );
		w.Write( 3f );

		w.Write( MapEntity.Hash( "bakelightindex" ) );
		w.Write( 30u );
		w.Write( Encoding.UTF8.GetBytes( "37\0" ) );

		w.Write( MapEntity.Hash( "bakelightindexscale" ) );
		w.Write( 1u );
		w.Write( 0.5f );

		w.Write( MapEntity.Hash( "handshake" ) );
		w.Write( 5u );
		w.Write( 808657659 );

		w.Write( MapEntity.Hash( "color" ) );
		w.Write( 9u );
		w.Write( new byte[] { 241, 236, 213, 255 } );

		w.Write( MapEntity.Hash( "range" ) );
		w.Write( Encoding.UTF8.GetBytes( "range\0" ) );
		w.Write( 1u );
		w.Write( 512f );

		return ((MemoryStream)w.BaseStream).ToArray();
	}

	/// <summary>
	/// <c>{ m_entityKeyValues = [ { m_keyValuesData = blob } ] }</c> as KV3 version 2: integers (the string count, member
	/// counts and names), strings, types, then the blob's length and the trailer, and the blob after the main section.
	/// </summary>
	static byte[] Lump( byte[] blob, bool lz4 )
	{
		using var main = new BinaryWriter( new MemoryStream() );
		int[] ints = [2, 1, 0, 1, 1, 1];
		foreach ( var i in ints ) main.Write( i );
		main.Write( Encoding.UTF8.GetBytes( "m_entityKeyValues\0m_keyValuesData\0" ) );
		main.Write( new byte[] { 9, 8, 9, 7 } );
		var stringsAndTypes = (int)main.BaseStream.Length - ints.Length * 4;
		main.Write( blob.Length );
		main.Write( 0xFFEEDD00u );

		// LZ4: the blob in two chunks of one stream, each chunk's length a ushort after the trailer
		var chunks = lz4 ? new[] { blob[..10], blob[10..] } : [];
		foreach ( var chunk in chunks ) main.Write( (ushort)Literals( chunk ).Length );
		var data = ((MemoryStream)main.BaseStream).ToArray();

		var packedMain = lz4 ? Literals( data ) : data;
		var after = lz4 ? chunks.SelectMany( Literals ).ToArray() : blob;

		using var w = new BinaryWriter( new MemoryStream() );
		w.Write( 0x4B563302u );
		w.Write( new byte[16] );
		w.Write( lz4 ? 1u : 0u );
		w.Write( (ushort)0 );
		w.Write( (ushort)16384 ); // frame size
		w.Write( 0 ); // bytes
		w.Write( ints.Length );
		w.Write( 0 ); // eight-byte values
		w.Write( stringsAndTypes );
		w.Write( (ushort)0 );
		w.Write( (ushort)0 );
		w.Write( data.Length );
		w.Write( packedMain.Length );
		w.Write( 1 ); // blobs
		w.Write( blob.Length );
		w.Write( packedMain );
		w.Write( after );
		return ((MemoryStream)w.BaseStream).ToArray();
	}

	/// <summary>An LZ4 block of literals only.</summary>
	static byte[] Literals( byte[] data )
	{
		var output = new List<byte>();
		var length = data.Length;
		output.Add( (byte)(Math.Min( length, 15 ) << 4) );
		if ( length >= 15 )
		{
			var rest = length - 15;
			for ( ; rest >= 255; rest -= 255 ) output.Add( 255 );
			output.Add( (byte)rest );
		}

		output.AddRange( data );
		return [.. output];
	}
}
