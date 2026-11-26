using System;
using System.IO;
using System.Threading;

namespace PackageTests;

[TestClass]
public class PackageManagerTest
{
	[TestInitialize]
	public void TestInitialize()
	{
		PackageManager.UnmountAll();

		var dir = $"{Environment.CurrentDirectory}/.source2/test_download_cache/package_management";

		if ( System.IO.Directory.Exists( dir ) )
			System.IO.Directory.Delete( dir, true );

		System.IO.Directory.CreateDirectory( dir );

		AssetDownloadCache.Initialize( dir );
	}

	/// <summary>
	/// Should throw an exception on invalid/missing package
	/// </summary>
	[TestMethod]
	[TestCategory( "LiveBackend" )]
	[DataRow( "missing.sandbox" )]
	[DataRow( "missing.metal_wheely_bin" )]
	[DataRow( "missing.br_standardarena" )]
	[DataRow( "facepunch.missing" )]
	[DataRow( "missing.grassworld" )]
	[DataRow( "missing" )]
	[DataRow( "-" )]
	public async Task NotFound( string packageIdent )
	{
		await Assert.ThrowsExceptionAsync<FileNotFoundException>( async () =>
			{
				await PackageManager.InstallAsync( new PackageLoadOptions( packageIdent, "test" ) );
			}
		);

	}

	/// <summary>
	/// Should throw an exception on invalid/missing package
	/// </summary>
	[TestMethod]
	[TestCategory( "LiveBackend" )]
	public async Task InstallEndToEnd()
	{
		{
			var files = PackageManager.MountedFileSystem.FindFile( "/", recursive: true ).ToArray();
			Assert.AreEqual( 0, files.Length );
		}

		await PackageManager.InstallAsync( new PackageLoadOptions( "facepunch.walker", "client" ) );
		var sandboxFiles = PackageManager.MountedFileSystem.FindFile( "/", recursive: true ).ToArray().Length;
		Console.WriteLine( $"client files: {sandboxFiles:n0}" );

		await PackageManager.InstallAsync( new PackageLoadOptions( "facepunch.pool", "server" ) );
		var poolFiles = PackageManager.MountedFileSystem.FindFile( "/", recursive: true ).ToArray().Length;
		Console.WriteLine( $"with server files: {poolFiles:n0}" );

		await PackageManager.InstallAsync( new PackageLoadOptions( "facepunch.pool", "client" ) );
		var poolFilesClient = PackageManager.MountedFileSystem.FindFile( "/", recursive: true ).ToArray().Length;
		Console.WriteLine( $"with server files: {poolFiles:n0}" );

		Assert.AreNotEqual( 0, sandboxFiles );
		Assert.AreNotEqual( 0, poolFiles );
		Assert.AreEqual( poolFilesClient, poolFiles );
		Assert.AreNotEqual( sandboxFiles, poolFiles );

		// remove client tagged packages
		PackageManager.UnmountTagged( "client" );
		var serverFiles = PackageManager.MountedFileSystem.FindFile( "/", recursive: true ).ToArray().Length;
		Console.WriteLine( $"unmount client: {serverFiles:n0}" );

		Assert.AreNotEqual( serverFiles, poolFiles );
		Assert.AreNotEqual( 0, serverFiles );

		// remove server tagged packages
		PackageManager.UnmountTagged( "server" );

		var remainingFiles = PackageManager.MountedFileSystem.FindFile( "/", recursive: true ).ToArray().Length;
		Console.WriteLine( $"unmount server: {remainingFiles:n0}" );

		Assert.AreEqual( 0, remainingFiles );
	}

	/// <summary>
	/// Legacy code archives in a published addon are ignored while its assets still mount.
	/// </summary>
	[TestMethod]
	public async Task PublishedAddonIgnoresCodeArchive()
	{
		var addon = new RemotePackage
		{
			Org = new Package.Organization { Ident = "test" },
			Ident = "legacyaddon",
			TypeName = "addon",
			Version = new PackageRevision()
		};
		await addon.Revision.DownloadManifestAsync( default );
		addon.Revision.Manifest.Files = [
			new ManifestSchema.File { Path = ".bin/legacy.cll", Crc = "0", Size = 0 },
			new ManifestSchema.File { Path = "legacy-addon.txt", Crc = "0", Size = 0 }
		];

		try
		{
			var installed = await PackageManager.InstallAsync( new PackageLoadOptions( addon.FullIdent, "game" ) { Package = addon } );
			Assert.IsTrue( installed.FileSystem.FileExists( "legacy-addon.txt" ) );
			Assert.AreEqual( 0, installed.AssemblyFileSystem.FileCount );
		}
		finally
		{
			PackageManager.UnmountTagged( "game" );
		}
	}

	/// <summary>
	/// Installing a project over a published copy transfers its owners and removes published-only files.
	/// </summary>
	[TestMethod]
	public async Task ProjectReplacesMountedPublishedFiles()
	{
		Project.Clear();
		const string ident = "local.testmap";
		const string remoteFile = "package-tests/published-only.txt";
		var published = new RemotePackage
		{
			Org = new Package.Organization { Ident = "local" },
			Ident = "testmap",
			TypeName = "content",
			Version = new PackageRevision()
		};
		await published.Revision.DownloadManifestAsync( default );
		published.Revision.Manifest.Files = [new ManifestSchema.File { Path = remoteFile, Crc = "0", Size = 0 }];

		try
		{
			var previous = await PackageManager.InstallAsync( new PackageLoadOptions( ident, "published_owner" ) { Package = published } );
			Assert.IsTrue( PackageManager.MountedFileSystem.FileExists( remoteFile ) );

			var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );
			var installed = await PackageManager.InstallAsync( new PackageLoadOptions( ident, "project_owner" ) );
			Assert.AreSame( project.Package, installed.Package );
			Assert.IsFalse( PackageManager.ActivePackages.Contains( previous ) );
			Assert.IsFalse( PackageManager.MountedFileSystem.FileExists( remoteFile ) );

			PackageManager.UnmountTagged( "project_owner" );
			Assert.AreSame( installed, PackageManager.Find( ident ) );
			PackageManager.UnmountTagged( "published_owner" );
			Assert.IsNull( PackageManager.Find( ident ) );
		}
		finally
		{
			PackageManager.UnmountAll();
			Project.Clear();
		}
	}

	/// <summary>
	/// Find and load a local package
	/// </summary>
	[TestMethod]
	public async Task ProjectInstallsPackage()
	{
		Project.Clear();
		Assert.AreEqual( 0, PackageManager.MountedFileSystem.FileCount );
		Assert.AreEqual( 0, PackageManager.ActivePackages.Count );

		//
		// Adding a project also mounts it as a package with the tag "local"
		//
		var proj = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );
		await Project.SyncWithPackageManager();
		Assert.AreNotEqual( 0, PackageManager.MountedFileSystem.FileCount );
		Assert.AreEqual( 1, PackageManager.ActivePackages.Count );

		//
		// Installing it should make no difference at all
		//
		var fs = await PackageManager.InstallAsync( new PackageLoadOptions( "local.testmap", "test" ) );
		Assert.AreNotEqual( 0, fs.FileSystem.FileCount );
		Assert.AreNotEqual( 0, PackageManager.MountedFileSystem.FileCount );

		foreach ( var ap in PackageManager.ActivePackages )
		{
			Console.WriteLine( ap.Package.FullIdent );
		}

		Assert.AreEqual( 1, PackageManager.ActivePackages.Count );

		//
		// Removing the test tag shouldn't remove the package - because it also has the local tag
		//
		PackageManager.UnmountTagged( "test" );
		Assert.AreNotEqual( 0, PackageManager.MountedFileSystem.FileCount );

		//
		// Calling this again should change nothing
		//
		await Project.SyncWithPackageManager();
		Assert.AreNotEqual( 0, PackageManager.MountedFileSystem.FileCount );

		//
		// Removing or disabling the project should also remove the package
		//
		proj.Active = false;
		await Project.SyncWithPackageManager();
		Assert.AreEqual( 0, PackageManager.MountedFileSystem.FileCount );

		//
		// Making it active should add the files back
		//
		proj.Active = true;
		await Project.SyncWithPackageManager();
		Assert.AreNotEqual( 0, PackageManager.MountedFileSystem.FileCount );

		//
		// Clearing projects should remove everything
		//
		Project.Clear();
		Assert.AreEqual( 0, PackageManager.MountedFileSystem.FileCount );

	}

	/// <summary>
	/// Sometimes packages have the same files in them. When we download the packages at the same time
	/// we can get conflicts between the files - as they are trying to read/write to the same file.
	/// Test to make sure that Package.activeDownloadLocks is working properly.
	/// </summary>
	[TestMethod]
	[TestCategory( "LiveBackend" )]
	public async Task DownloadPackagesWithMatchingFiles()
	{
		var a = PackageManager.InstallAsync( new PackageLoadOptions( "titanovsky.ufrts_archery2_3", "fff" ) );
		var b = PackageManager.InstallAsync( new PackageLoadOptions( "titanovsky.ufrts_crates2", "fff" ) );

		await Task.WhenAll( a, b );

		Assert.AreNotEqual( 0, (await a).FileSystem.FileCount, "First package should have mounted files" );
		Assert.AreNotEqual( 0, (await b).FileSystem.FileCount, "Second package should have mounted files" );

		PackageManager.UnmountAll();
		Assert.AreEqual( 0, PackageManager.MountedFileSystem.FileCount );
	}
}
