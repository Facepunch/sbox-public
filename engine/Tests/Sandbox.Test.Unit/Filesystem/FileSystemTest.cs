using System;
using System.IO;

namespace FilesystemTests;

[TestClass]
public class FileSystemTest
{
	/// <summary>
	/// Writing then reading a text file should round-trip the content,
	/// and existence checks should reflect the write.
	/// </summary>
	[TestMethod]
	public void WriteReadText()
	{
		var fs = new MemoryFileSystem();

		Assert.IsFalse( fs.FileExists( "hello.txt" ) );

		fs.WriteAllText( "hello.txt", "hello world" );

		Assert.IsTrue( fs.FileExists( "hello.txt" ) );
		Assert.AreEqual( "hello world", fs.ReadAllText( "hello.txt" ) );
	}

	/// <summary>
	/// Binary content should round-trip byte for byte.
	/// </summary>
	[TestMethod]
	public void WriteReadBytes()
	{
		var fs = new MemoryFileSystem();
		var data = new byte[] { 0, 1, 2, 255, 128, 7 };

		fs.WriteAllBytes( "blob.bin", data );

		CollectionAssert.AreEqual( data, fs.ReadAllBytes( "blob.bin" ).ToArray() );
		Assert.AreEqual( data.Length, fs.FileSize( "blob.bin" ) );
	}

	/// <summary>
	/// Deleting a file should make it stop existing.
	/// </summary>
	[TestMethod]
	public void DeleteFile()
	{
		var fs = new MemoryFileSystem();
		fs.WriteAllText( "doomed.txt", "x" );

		fs.DeleteFile( "doomed.txt" );

		Assert.IsFalse( fs.FileExists( "doomed.txt" ) );
	}

	/// <summary>
	/// Directories should be creatable, discoverable and deletable.
	/// </summary>
	[TestMethod]
	public void Directories()
	{
		var fs = new MemoryFileSystem();

		Assert.IsFalse( fs.DirectoryExists( "sub" ) );

		fs.CreateDirectory( "sub" );
		Assert.IsTrue( fs.DirectoryExists( "sub" ) );

		fs.WriteAllText( "sub/file.txt", "content" );
		Assert.IsTrue( fs.FileExists( "sub/file.txt" ) );

		fs.DeleteDirectory( "sub", recursive: true );
		Assert.IsFalse( fs.DirectoryExists( "sub" ) );
	}

	/// <summary>
	/// FindFile should filter by pattern and respect the recursive flag.
	/// </summary>
	[TestMethod]
	public void FindFile()
	{
		var fs = new MemoryFileSystem();
		fs.WriteAllText( "a.txt", "" );
		fs.WriteAllText( "b.txt", "" );
		fs.WriteAllText( "c.json", "" );
		fs.CreateDirectory( "sub" );
		fs.WriteAllText( "sub/d.txt", "" );

		var txt = fs.FindFile( "/", "*.txt" ).ToList();
		Assert.AreEqual( 2, txt.Count );

		var all = fs.FindFile( "/", "*.txt", recursive: true ).ToList();
		Assert.AreEqual( 3, all.Count );
	}

	/// <summary>
	/// A sub-system should scope all paths beneath its root.
	/// </summary>
	[TestMethod]
	public void SubSystemScopesPaths()
	{
		var fs = new MemoryFileSystem();
		fs.CreateDirectory( "scoped" );
		fs.WriteAllText( "scoped/inner.txt", "inner" );

		var sub = fs.CreateSubSystem( "scoped" );

		Assert.IsTrue( sub.FileExists( "inner.txt" ) );
		Assert.AreEqual( "inner", sub.ReadAllText( "inner.txt" ) );

		sub.WriteAllText( "written.txt", "from sub" );
		Assert.AreEqual( "from sub", fs.ReadAllText( "scoped/written.txt" ) );
	}

	/// <summary>
	/// WriteJson/ReadJson should round-trip an object, and ReadJson should
	/// return the default for a missing file.
	/// </summary>
	[TestMethod]
	public void JsonRoundTrip()
	{
		var fs = new MemoryFileSystem();

		fs.WriteJson( "data.json", new TestPayload { Name = "bob", Count = 3 } );

		var loaded = fs.ReadJson<TestPayload>( "data.json" );
		Assert.AreEqual( "bob", loaded.Name );
		Assert.AreEqual( 3, loaded.Count );

		var missing = fs.ReadJson<TestPayload>( "missing.json", new TestPayload { Name = "fallback" } );
		Assert.AreEqual( "fallback", missing.Name );
	}

	/// <summary>
	/// Stream based writes should be readable back through OpenRead.
	/// </summary>
	[TestMethod]
	public void StreamRoundTrip()
	{
		var fs = new MemoryFileSystem();

		using ( var w = fs.OpenWrite( "stream.bin" ) )
		{
			w.Write( new byte[] { 1, 2, 3 }, 0, 3 );
		}

		using var r = fs.OpenRead( "stream.bin" );
		var buffer = new byte[3];
		r.ReadExactly( buffer );

		CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, buffer );
	}

	/// <summary>
	/// The content CRC should be stable for identical content and change
	/// when the content changes.
	/// </summary>
	[TestMethod]
	public void CrcTracksContent()
	{
		var fs = new MemoryFileSystem();
		fs.WriteAllText( "crc.txt", "version one" );

		var first = fs.GetCrc( "crc.txt" );
		Assert.AreEqual( first, fs.GetCrc( "crc.txt" ) );

		fs.WriteAllText( "crc.txt", "version two" );
		Assert.AreNotEqual( first, fs.GetCrc( "crc.txt" ) );
	}

	/// <summary>
	/// FindFileWithFullPath lists the same files as FindFile and gives each the path GetFullPath
	/// resolves it to - including which mount wins for a file that several have, and files that
	/// only a nested aggregate has.
	/// </summary>
	[TestMethod]
	public void FindFileWithFullPathMatchesGetFullPath()
	{
		var root = Path.Combine( Path.GetTempPath(), $"fs-fullpath-{Guid.NewGuid()}" );
		var low = Path.Combine( root, "low" );
		var high = Path.Combine( root, "high" );
		var nested = Path.Combine( root, "nested" );
		foreach ( var dir in new[] { low, high, nested } )
			Directory.CreateDirectory( Path.Combine( dir, "sub" ) );

		File.WriteAllText( Path.Combine( low, "shared.txt" ), "" );
		File.WriteAllText( Path.Combine( high, "shared.txt" ), "" );
		File.WriteAllText( Path.Combine( low, "sub", "low.txt" ), "" );
		File.WriteAllText( Path.Combine( nested, "sub", "nested.json" ), "" );

		try
		{
			var inner = new AggregateFileSystem();
			inner.Mount( new LocalFileSystem( nested ) );

			var fs = new AggregateFileSystem();
			fs.Mount( new LocalFileSystem( low ) );
			fs.Mount( inner );
			fs.Mount( new LocalFileSystem( high ) );

			var expected = fs.FindFile( "/", "*", true ).Select( x => (x, fs.GetFullPath( x )) ).ToList();
			Assert.AreEqual( 3, expected.Count );
			CollectionAssert.AreEqual( expected, fs.FindFileWithFullPath( "/" ) );

			var json = fs.FindFileWithFullPath( "/", x => x.EndsWith( ".json" ) );
			Assert.AreEqual( 1, json.Count );
			Assert.AreEqual( ("sub/nested.json", fs.GetFullPath( "sub/nested.json" )), json[0] );
		}
		finally
		{
			Directory.Delete( root, true );
		}
	}

	class TestPayload
	{
		public string Name { get; set; }
		public int Count { get; set; }
	}
}
