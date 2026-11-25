using Borealis.Source2;
using Borealis.Source2.Resources;
using System.IO;
using System.Text;

namespace BorealisGpuTests;

/// <summary>
/// Reading Valve paks (<c>.vpk</c>), which compiled maps ship in: a small one written here, so these need no content.
/// </summary>
[TestClass]
public class VpkFileTests
{
	/// <summary>
	/// A version 2 VPK with files in folders and at the root, data after the tree and some preloaded in it, reads back
	/// byte for byte, and <see cref="ContentFiles"/> finds compiled files in it after its folders.
	/// </summary>
	[TestMethod]
	public void FilesReadBack()
	{
		var files = new (string Path, byte[] Data, int Preload)[]
		{
			("maps/test/world.vwrld_c", Enumerable.Range( 0, 300 ).Select( i => (byte)i ).ToArray(), 0),
			("maps/test/worldnodes/node000.vwnod_c", Encoding.ASCII.GetBytes( "node data" ), 4),
			("readme.txt", Encoding.ASCII.GetBytes( "root" ), 0),
		};

		var path = Path.Combine( Path.GetTempPath(), $"borealis-vpk-test-{Guid.NewGuid():N}.vpk" );
		try
		{
			File.WriteAllBytes( path, Write( files ) );
			using var vpk = VpkFile.Open( path );
			Assert.AreEqual( files.Length, vpk.Count );
			foreach ( var (name, data, _) in files ) CollectionAssert.AreEqual( data, vpk.Read( name ), name );
			Assert.IsNull( vpk.Read( "maps/test/missing.vmdl_c" ) );
			Assert.IsTrue( vpk.Contains( "MAPS\\test\\world.vwrld_c" ) );

			var content = new ContentFiles();
			content.AddPackage( vpk );
			CollectionAssert.AreEqual( files[1].Data, content.TryRead( "maps/test/worldnodes/node000.vwnod" ) );
			Assert.IsNull( content.TryRead( "maps/test/none.vwnod" ) );
		}
		finally
		{
			File.Delete( path );
		}
	}

	/// <summary>
	/// Reads from many threads at once (a map's models, materials and textures read on workers) each get their own file's
	/// bytes, without a lock between them; once the VPK is disposed, reads find nothing rather than opening it again.
	/// </summary>
	[TestMethod]
	public void ManyThreadsReadAtOnceAndNothingAfterDispose()
	{
		var files = Enumerable.Range( 0, 64 ).Select( i => ($"models/m{i}.vmdl_c", Enumerable.Range( 0, 1000 + i * 37 ).Select( b => (byte)(b * 7 + i) ).ToArray(), i % 3) ).ToArray();
		var path = Path.Combine( Path.GetTempPath(), $"borealis-vpk-test-{Guid.NewGuid():N}.vpk" );
		try
		{
			File.WriteAllBytes( path, Write( files ) );
			var vpk = VpkFile.Open( path );
			var wrong = 0;
			System.Threading.Tasks.Parallel.For( 0, files.Length * 40, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
			{
				var (name, data, _) = files[i % files.Length];
				var read = i % 2 == 0 ? vpk.Read( name ) : vpk.ReadPrefix( name, data.Length / 2 );
				if ( read is null || !read.AsSpan().SequenceEqual( data.AsSpan( 0, read.Length ) ) || read.Length != (i % 2 == 0 ? data.Length : data.Length / 2) ) System.Threading.Interlocked.Increment( ref wrong );
			} );
			Assert.AreEqual( 0, wrong );

			vpk.Dispose();
			Assert.IsNull( vpk.Read( files[5].Item1 ) );
		}
		finally
		{
			File.Delete( path );
		}
	}

	/// <summary>A version 2 VPK with every file's data after the tree (archive <c>0x7FFF</c>), less any preloaded bytes.</summary>
	static byte[] Write( (string Path, byte[] Data, int Preload)[] files )
	{
		var tree = new MemoryStream();
		var data = new MemoryStream();
		void Text( string s )
		{
			tree.Write( Encoding.UTF8.GetBytes( s ) );
			tree.WriteByte( 0 );
		}

		foreach ( var byExtension in files.GroupBy( f => Path.GetExtension( f.Path ).TrimStart( '.' ) ) )
		{
			Text( byExtension.Key );
			foreach ( var byFolder in byExtension.GroupBy( f => Path.GetDirectoryName( f.Path )?.Replace( '\\', '/' ) is { Length: > 0 } d ? d : " " ) )
			{
				Text( byFolder.Key );
				foreach ( var (name, bytes, preload) in byFolder )
				{
					Text( Path.GetFileNameWithoutExtension( name ) );
					using var entry = new BinaryWriter( tree, Encoding.UTF8, leaveOpen: true );
					entry.Write( 0u );
					entry.Write( (ushort)preload );
					entry.Write( (ushort)0x7FFF );
					entry.Write( (uint)data.Length );
					entry.Write( (uint)(bytes.Length - preload) );
					entry.Write( (ushort)0xFFFF );
					tree.Write( bytes, 0, preload );
					data.Write( bytes, preload, bytes.Length - preload );
				}

				Text( "" );
			}

			Text( "" );
		}

		Text( "" );

		var output = new MemoryStream();
		using var header = new BinaryWriter( output, Encoding.UTF8, leaveOpen: true );
		header.Write( 0x55AA1234u );
		header.Write( 2u );
		header.Write( (uint)tree.Length );
		header.Write( (uint)data.Length );
		header.Write( 0u );
		header.Write( 0u );
		header.Write( 0u );
		tree.WriteTo( output );
		data.WriteTo( output );
		return output.ToArray();
	}
}
