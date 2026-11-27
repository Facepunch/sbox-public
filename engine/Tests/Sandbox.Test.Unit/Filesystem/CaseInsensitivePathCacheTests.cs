using System;
using System.IO;
using Sandbox;

namespace FilesystemTests;

/// <summary>
/// The Linux path casing cache (<see cref="CaseInsensitivePhysicalFileSystem"/>): lookups ignoring case, misses kept while their
/// folder is unchanged, and files changed outside the engine found again. Linux only: elsewhere the disk ignores case itself.
/// </summary>
[TestClass]
public class CaseInsensitivePathCacheTests
{
	DirectoryInfo dir;

	[TestInitialize]
	public void Init()
	{
		if ( !OperatingSystem.IsLinux() ) Assert.Inconclusive( "Case-sensitive disks only" );
		dir = Directory.CreateTempSubdirectory( "sbox-case-cache" );
	}

	[TestCleanup]
	public void Cleanup() => dir?.Delete( true );

	string Real( string relative ) => Path.Combine( dir.FullName, relative );

	[TestMethod]
	public void ResolvesAnyCaseToTheCaseOnDisk()
	{
		Directory.CreateDirectory( Real( "Models/Props" ) );
		File.WriteAllText( Real( "Models/Props/Crate.vmdl" ), "" );
		using var fs = new CaseInsensitivePhysicalFileSystem();

		Assert.AreEqual( Real( "Models/Props/Crate.vmdl" ), fs.ResolvePathCasing( Real( "models/PROPS/crate.VMDL" ) ) );
		Assert.AreEqual( Real( "Models/Props/Crate.vmdl" ), fs.ResolvePathCasing( Real( "models//props/crate.vmdl/" ) ) );

		// Already right: the same string back, not a copy
		var exact = Real( "Models/Props/Crate.vmdl" );
		Assert.AreSame( exact, fs.ResolvePathCasing( exact ) );
	}

	[TestMethod]
	public void NamesThatDifferOnlyByCaseKeepTheirOwn()
	{
		Directory.CreateDirectory( Real( "code" ) );
		Directory.CreateDirectory( Real( "Code" ) );
		using var fs = new CaseInsensitivePhysicalFileSystem();

		Assert.AreEqual( Real( "code" ), fs.ResolvePathCasing( Real( "code" ) ) );
		Assert.AreEqual( Real( "Code" ), fs.ResolvePathCasing( Real( "Code" ) ) );
	}

	[TestMethod]
	public void AMissKeepsWhatItCouldResolve()
	{
		Directory.CreateDirectory( Real( "Materials" ) );
		using var fs = new CaseInsensitivePhysicalFileSystem();

		Assert.AreEqual( Real( "Materials/new/Thing.vmat" ), fs.ResolvePathCasing( Real( "materials/new//Thing.vmat" ) ) );
		Assert.AreEqual( Real( "Materials/new/Thing.vmat" ), fs.ResolvePathCasing( Real( "materials/new//Thing.vmat" ) ) );
	}

	[TestMethod]
	public void AMissedFileMadeOutsideTheEngineIsFound()
	{
		Directory.CreateDirectory( Real( "Sounds" ) );
		using var fs = new CaseInsensitivePhysicalFileSystem();
		var asked = Real( "sounds/door.vsnd" );

		Assert.AreEqual( Real( "Sounds/door.vsnd" ), fs.ResolvePathCasing( asked ) );

		File.WriteAllText( Real( "Sounds/Door.vsnd" ), "" );
		Assert.AreEqual( Real( "Sounds/Door.vsnd" ), fs.ResolvePathCasing( asked ) );
	}

	[TestMethod]
	public void AMissedFolderMadeOutsideTheEngineIsFound()
	{
		using var fs = new CaseInsensitivePhysicalFileSystem();
		var asked = Real( "addons/red/red.txt" );
		Assert.AreEqual( asked, fs.ResolvePathCasing( asked ) );

		Directory.CreateDirectory( Real( "Addons/Red" ) );
		File.WriteAllText( Real( "Addons/Red/Red.txt" ), "" );
		Assert.AreEqual( Real( "Addons/Red/Red.txt" ), fs.ResolvePathCasing( asked ) );
	}

	[TestMethod]
	public void AFileMadeThroughTheFileSystemIsFoundAtOnce()
	{
		Directory.CreateDirectory( Real( "Data" ) );
		var fs = new LocalFileSystem( dir.FullName );
		try
		{
			Assert.IsFalse( fs.FileExists( "data/save.json" ) );
			fs.WriteAllText( "Data/Save.json", "saved" );
			Assert.IsTrue( fs.FileExists( "data/save.json" ) );
			Assert.AreEqual( "saved", fs.ReadAllText( "DATA/SAVE.JSON" ) );
		}
		finally
		{
			fs.Dispose();
		}
	}

	/// <summary>
	/// A file renamed outside the engine with only its case changed keeps its folder's listing matching the old name: the
	/// lookup that finds it gone reads the folder again (hotload of a renamed file).
	/// </summary>
	[TestMethod]
	public void AFileRenamedOutsideTheEngineIsFoundAgain()
	{
		Directory.CreateDirectory( Real( "Textures" ) );
		File.WriteAllText( Real( "Textures/rock.png" ), "rock" );
		var fs = new LocalFileSystem( dir.FullName );
		try
		{
			Assert.IsTrue( fs.FileExists( "textures/Rock.png" ) );

			File.Move( Real( "Textures/rock.png" ), Real( "Textures/ROCK.png" ) );
			Assert.IsTrue( fs.FileExists( "textures/Rock.png" ) );
			Assert.AreEqual( "rock", fs.ReadAllText( "textures/Rock.png" ) );

			File.Move( Real( "Textures/ROCK.png" ), Real( "Textures/stone.png" ) );
			Assert.IsFalse( fs.FileExists( "textures/Rock.png" ) );
			Assert.IsTrue( fs.FileExists( "textures/Stone.png" ) );
		}
		finally
		{
			fs.Dispose();
		}
	}

	[TestMethod]
	public void AFolderRenamedOutsideTheEngineIsFoundAgain()
	{
		Directory.CreateDirectory( Real( "maps" ) );
		var fs = new LocalFileSystem( dir.FullName );
		try
		{
			Assert.IsTrue( fs.DirectoryExists( "Maps" ) );
			Directory.Move( Real( "maps" ), Real( "MAPS" ) );
			Assert.IsTrue( fs.DirectoryExists( "Maps" ) );
		}
		finally
		{
			fs.Dispose();
		}
	}

	/// <summary>A path asked again, found or not, costs no allocation: a load asks every mount for every file.</summary>
	[TestMethod]
	public void AskingAgainAllocatesNothing()
	{
		Directory.CreateDirectory( Real( "Models" ) );
		File.WriteAllText( Real( "Models/Crate.vmdl" ), "" );
		using var fs = new CaseInsensitivePhysicalFileSystem();
		var found = Real( "models/crate.vmdl" );
		var missing = Real( "models/barrel.vmdl" );
		var deep = Real( "models/props/barrel.vmdl" );
		var cased = Real( "Models/props/barrel.vmdl" );
		fs.ResolvePathCasing( found );
		fs.ResolvePathCasing( missing );
		fs.ResolvePathCasing( deep );
		Assert.AreSame( cased, fs.ResolvePathCasing( cased ) );

		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( int i = 0; i < 100; i++ )
		{
			fs.ResolvePathCasing( found );
			fs.ResolvePathCasing( missing );
			fs.ResolvePathCasing( deep );
			fs.ResolvePathCasing( cased );
		}

		Assert.AreEqual( 0, GC.GetAllocatedBytesForCurrentThread() - before );
	}

	[TestMethod]
	public void InvalidatingForgetsMisses()
	{
		Directory.CreateDirectory( Real( "Cfg" ) );
		using var fs = new CaseInsensitivePhysicalFileSystem();
		var asked = Real( "cfg/game.cfg" );
		Assert.AreEqual( Real( "Cfg/game.cfg" ), fs.ResolvePathCasing( asked ) );

		// Made with its folder's write time put back, so only dropping the caches can find it
		var written = Directory.GetLastWriteTimeUtc( Real( "Cfg" ) );
		File.WriteAllText( Real( "Cfg/Game.cfg" ), "" );
		Directory.SetLastWriteTimeUtc( Real( "Cfg" ), written );
		Assert.AreEqual( Real( "Cfg/game.cfg" ), fs.ResolvePathCasing( asked ) );

		fs.InvalidateCache();
		Assert.AreEqual( Real( "Cfg/Game.cfg" ), fs.ResolvePathCasing( asked ) );
	}
}
