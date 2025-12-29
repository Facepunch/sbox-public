using System;
using System.Linq;
using Sandbox.Diagnostics;

namespace TestFileSystem;

[TestClass]
public partial class FileSystem
{
	[TestInitialize]
	public void ClassInitialize()
	{
		Logging.Enabled = true;

		if ( System.IO.Directory.Exists( ".source2/TestFolder" ) )
			System.IO.Directory.Delete( ".source2/TestFolder", true );

		System.IO.Directory.CreateDirectory( ".source2/TestFolder" );

		Sandbox.EngineFileSystem.Shutdown();
		Sandbox.EngineFileSystem.Initialize( ".source2/TestFolder", true );

		Sandbox.EngineFileSystem.Root.WriteAllText( "root_text_file.txt", "Hello" );

		Sandbox.EngineFileSystem.Root.CreateDirectory( "Addons/Red" );
		Sandbox.EngineFileSystem.Root.WriteAllText( "Addons/Red/red.txt", "Red" );
		Sandbox.EngineFileSystem.Root.WriteAllText( "Addons/Red/common.txt", "Red" );

		Sandbox.EngineFileSystem.Root.CreateDirectory( "Addons/Green" );
		Sandbox.EngineFileSystem.Root.WriteAllText( "Addons/Green/green.txt", "Green" );
		Sandbox.EngineFileSystem.Root.WriteAllText( "Addons/Green/common.txt", "Green" );

		Sandbox.EngineFileSystem.Root.CreateDirectory( "Addons/Blue" );
		Sandbox.EngineFileSystem.Root.WriteAllText( "Addons/Blue/blue.txt", "Blue" );
		Sandbox.EngineFileSystem.Root.WriteAllText( "Addons/Blue/common.txt", "Blue" );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Sandbox.EngineFileSystem.Shutdown();

		if ( System.IO.Directory.Exists( ".source2/TestFolder" ) )
			System.IO.Directory.Delete( ".source2/TestFolder", true );

		Sandbox.EngineFileSystem.Initialize( Environment.CurrentDirectory );
	}

	[TestMethod]
	public void FindDirectory()
	{
		var paths = Sandbox.EngineFileSystem.Root.FindDirectory( "/", "*", false ).ToArray();

		foreach ( var path in paths )
		{
			Console.WriteLine( path );
		}

		Assert.IsTrue( paths.Contains( "Addons" ) );
		Assert.IsFalse( paths.Contains( "Addons/Blue" ) );
		Assert.IsFalse( paths.Contains( "Addons/Red" ) );
		Assert.IsFalse( paths.Contains( "Addons/Green" ) );
	}

	[TestMethod]
	public void FindDirectoryFolder()
	{
		var paths = Sandbox.EngineFileSystem.Root.FindDirectory( "/Addons", "*", false ).ToArray();

		foreach ( var path in paths )
		{
			Console.WriteLine( path );
		}

		Assert.IsTrue( paths.Contains( "Blue" ) );
		Assert.IsTrue( paths.Contains( "Red" ) );
		Assert.IsTrue( paths.Contains( "Green" ) );
	}

	[TestMethod]
	public void FindDirectoryRecursive()
	{
		var paths = Sandbox.EngineFileSystem.Root.FindDirectory( "/", "*", true ).ToArray();

		foreach ( var path in paths )
		{
			Console.WriteLine( path );
		}

		Assert.IsTrue( paths.Contains( "Addons/Green" ) );
		Assert.IsTrue( paths.Contains( "Addons" ) );
		Assert.IsTrue( paths.Contains( "Addons/Blue" ) );
		Assert.IsTrue( paths.Contains( "Addons/Red" ) );
	}

	[TestMethod]
	public void FindDirectoryRecursiveSearch()
	{
		var paths = Sandbox.EngineFileSystem.Root.FindDirectory( "/", "Red", true ).ToArray();

		foreach ( var path in paths )
		{
			Console.WriteLine( path );
		}

		Assert.IsFalse( paths.Contains( "Addons/Green" ) );
		Assert.IsFalse( paths.Contains( "Addons" ) );
		Assert.IsFalse( paths.Contains( "Addons/Blue" ) );
		Assert.IsTrue( paths.Contains( "Addons/Red" ) );
	}

	[TestMethod]
	public void FindFile()
	{
		var paths = Sandbox.EngineFileSystem.Root.FindFile( "/", "*", false ).ToArray();

		foreach ( var path in paths )
		{
			Console.WriteLine( path );
		}

		Assert.IsTrue( paths.Length == 1 );
		Assert.IsTrue( paths.Contains( "root_text_file.txt" ) );
	}

	[TestMethod]
	public void FindFileRecursive()
	{
		var paths = Sandbox.EngineFileSystem.Root.FindFile( "/", "*", true ).ToArray();

		foreach ( var path in paths )
		{
			Console.WriteLine( path );
		}

		Assert.IsTrue( paths.Contains( "root_text_file.txt" ) );
		Assert.IsTrue( paths.Contains( "Addons/Blue/blue.txt" ) );
		Assert.IsTrue( paths.Contains( "Addons/Blue/common.txt" ) );
		Assert.IsTrue( paths.Contains( "Addons/Green/common.txt" ) );
		Assert.IsTrue( paths.Contains( "Addons/Green/green.txt" ) );
		Assert.IsTrue( paths.Contains( "Addons/Red/common.txt" ) );
		Assert.IsTrue( paths.Contains( "Addons/Red/red.txt" ) );
		Assert.IsFalse( paths.Contains( "Addons/Red" ) );
	}

	[TestMethod]
	public void FindFileRecursiveSearch()
	{
		var paths = Sandbox.EngineFileSystem.Root.FindFile( "/", "red.tx*", true ).ToArray();

		foreach ( var path in paths )
		{
			Console.WriteLine( path );
		}

		Assert.IsTrue( paths.Contains( "Addons/Red/red.txt" ) );
		Assert.IsFalse( paths.Contains( "root_text_file.txt" ) );
		Assert.IsFalse( paths.Contains( "Addons/Blue/blue.txt" ) );
		Assert.IsFalse( paths.Contains( "Addons/Blue/common.txt" ) );
		Assert.IsFalse( paths.Contains( "Addons/Green/common.txt" ) );
		Assert.IsFalse( paths.Contains( "Addons/Green/green.txt" ) );
		Assert.IsFalse( paths.Contains( "Addons/Red/common.txt" ) );
		Assert.IsFalse( paths.Contains( "Addons/Red" ) );
	}

	[TestMethod]
	public void FindFileRecursiveSearchFolder()
	{
		var paths = Sandbox.EngineFileSystem.Root.FindFile( "/Addons/", "red.tx*", true ).ToArray();

		foreach ( var path in paths )
		{
			Console.WriteLine( path );
		}

		Assert.IsTrue( paths.Length == 1 );
		Assert.IsTrue( paths.Contains( "Red/red.txt" ) );
	}

	[TestMethod]
	public void Exists()
	{
		Assert.IsTrue( Sandbox.EngineFileSystem.Root.FileExists( "/root_text_file.txt" ) );
		Assert.IsTrue( Sandbox.EngineFileSystem.Root.DirectoryExists( "/Addons" ) );

		Assert.IsFalse( Sandbox.EngineFileSystem.Root.FileExists( "/missing_text_file.txt" ) );
		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "/Windows" ) );

		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "/mnt/c/Windows" ) );
		Assert.ThrowsException<NotSupportedException>( () => Sandbox.EngineFileSystem.Root.DirectoryExists( "c:/Windows" ) );
		Assert.ThrowsException<ArgumentException>( () => Sandbox.EngineFileSystem.Root.DirectoryExists( "/.." ) );
	}

	[TestMethod]
	public void MountedFolders()
	{
		var fs = new Sandbox.AggregateFileSystem();

		var blue = fs.CreateAndMount( Sandbox.EngineFileSystem.Root, "Addons/Blue" );
		var red = fs.CreateAndMount( Sandbox.EngineFileSystem.Root, "Addons/Red" );
		var green = fs.CreateAndMount( Sandbox.EngineFileSystem.Root, "Addons/Green" );

		Assert.IsTrue( fs.FileExists( "red.txt" ) );
		Assert.IsTrue( fs.FileExists( "green.txt" ) );
		Assert.IsTrue( fs.FileExists( "blue.txt" ) );

		Assert.AreEqual( fs.ReadAllText( "red.txt" ), "Red" );
		Assert.AreEqual( fs.ReadAllText( "green.txt" ), "Green" );
		Assert.AreEqual( fs.ReadAllText( "blue.txt" ), "Blue" );

		Assert.IsTrue( fs.FileExists( "common.txt" ) );
		Assert.AreEqual( fs.ReadAllText( "common.txt" ), "Green" );
	}

	[TestMethod]
	public void CreateDeleteDirectories()
	{
		Sandbox.EngineFileSystem.Root.CreateDirectory( "NewFolder" );
		Assert.IsTrue( Sandbox.EngineFileSystem.Root.DirectoryExists( "NewFolder" ) );

		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "A" ) );
		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B/C" ) );
		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B/C/D/E" ) );
		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B/C/D/E/F" ) );
		Sandbox.EngineFileSystem.Root.CreateDirectory( "A/B/C/D/E/F" );
		Assert.IsTrue( Sandbox.EngineFileSystem.Root.DirectoryExists( "A" ) );
		Assert.IsTrue( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B/C" ) );
		Assert.IsTrue( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B/C/D/E" ) );
		Assert.IsTrue( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B/C/D/E/F" ) );

		Sandbox.EngineFileSystem.Root.CreateDirectory( "NewFolder" );

		Sandbox.EngineFileSystem.Root.DeleteDirectory( "NewFolder" );
		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "NewFolder" ) );

		Sandbox.EngineFileSystem.Root.DeleteDirectory( "A/B/C", true );
		Assert.IsTrue( Sandbox.EngineFileSystem.Root.DirectoryExists( "A" ) );
		Assert.IsTrue( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B" ) );
		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B/C" ) );
		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B/C/D/E" ) );
		Assert.IsFalse( Sandbox.EngineFileSystem.Root.DirectoryExists( "A/B/C/D/E/F" ) );
	}

	[TestMethod]
	public void Temporary()
	{
		var files = Sandbox.EngineFileSystem.Temporary.FindFile( "/", "*", true ).ToArray();
		Assert.AreEqual( 0, files.Length );

		var str = "Hello this is some\ntext in a file";
		Sandbox.EngineFileSystem.Temporary.WriteAllText( "textfile.txt", str );

		Assert.AreEqual( str, Sandbox.EngineFileSystem.Temporary.ReadAllText( "textfile.txt" ) );

		files = Sandbox.EngineFileSystem.Temporary.FindFile( "/", "*", true ).ToArray();
		Assert.AreEqual( 1, files.Length );

		using ( var stream = Sandbox.EngineFileSystem.Temporary.OpenWrite( "binary.bin" ) )
		using ( var writer = new System.IO.BinaryWriter( stream ) )
		{
			for ( int i = 0; i < 1024 * 64; i++ )
			{
				writer.Write( i );
			}
		}

		using ( var stream = Sandbox.EngineFileSystem.Temporary.OpenRead( "binary.bin" ) )
		using ( var reader = new System.IO.BinaryReader( stream ) )
		{
			for ( int i = 0; i < 1024 * 64; i++ )
			{
				Assert.AreEqual( i, reader.ReadInt32() );
			}
		}
	}

	[TestMethod]
	public void ReadAllBytes()
	{
		var data = Sandbox.EngineFileSystem.Root.ReadAllBytes( "Addons/Green/green.txt" );
		Assert.AreNotEqual( 0, data.Length );
	}
}
