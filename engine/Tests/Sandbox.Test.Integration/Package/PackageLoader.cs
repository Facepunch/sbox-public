using Sandbox.Internal;
using System;

namespace PackageTests;

[TestClass]
public partial class PackageLoaderTest
{
	[TestInitialize]
	public void TestInitialize()
	{
		Project.Clear();

		var dir = $"{Environment.CurrentDirectory}/.source2/test_download_cache/package_loader";
		AssetDownloadCache.Initialize( dir );
	}

	private (TypeLibrary TypeLibrary, Sandbox.PackageLoader PackageLoader, Sandbox.PackageLoader.Enroller Enroller) Preamble()
	{
		var library = new TypeLibrary();
		var packageLoader = new Sandbox.PackageLoader( "Test", GetType().Assembly );
		var enroller = packageLoader.CreateEnroller( "test-enroller" );

		enroller.OnAssemblyAdded = ( a ) =>
		{
			library.AddAssembly( a.Assembly, true );
		};

		return (library, packageLoader, enroller);
	}

	[TestMethod]
	[TestCategory( "LiveBackend" )]
	[DataRow( "facepunch.testbed" )]
	[DataRow( "facepunch.hc1" )]
	[DataRow( "facepunch.walker" )]
	[DataRow( "facepunch.opium_demo" )]
	//[DataRow( "garry.testbed" )]
	[DataRow( "carsonk.pizza_clicker" )]
	[DataRow( "facepunch.jumper" )]
	[DataRow( "brax.cargame" )]
	[DataRow( "carsonk.tetros_effect" )]
	[DataRow( "carsonk.cyka_game" )]
	[DataRow( "carsonk.squirtfire" )]
	[DataRow( "brick.jumpy" )]
	[DataRow( "carsonk.warehouse" )]
	//[DataRow( "carsonk.voip_roulette" )]
	[DataRow( "fish.sauna" )]
	[DataRow( "obc.sandtycoon" )]
	[DataRow( "playback.kitty_cinema" )]
	[DataRow( "apetavern.grubs" )]
	[DataRow( "swb.demo" )]
	[DataRow( "starpalms.europe_strike" )]
	[DataRow( "fdd.dark_descent" )]
	[DataRow( "fish.cat_harvest" )]
	[DataRow( "facepunch.sbdm" )]
	[DataRow( "facepunch.sandbox" )]
	public async Task LoadSingleGamePackage( string packageName )
	{
		var (library, packageLoader, enroller) = Preamble();

		Assert.AreEqual( 0, PackageManager.MountedFileSystem.FileCount, "No package files mounted" );

		var downloadOptions = new PackageLoadOptions
		{
			PackageIdent = packageName,
			ContextTag = "client",
			SkipAssetDownload = true
		};
		var p = await PackageManager.InstallAsync( downloadOptions );
		Assert.IsNotNull( p, "Package should not be null" );

		Assert.AreNotEqual( 0, PackageManager.MountedFileSystem.FileCount, "We have package files mounted" );

		Assert.AreEqual( 0, library.Types.Count, "No classes in our library" );

		// Load the assemblies into the context
		enroller.LoadPackage( packageName );

		Assert.AreNotEqual( 0, library.Types.Count, "Library has classes" );

		PackageManager.UnmountTagged( "client" );

		packageLoader.Dispose();

		Assert.AreEqual( 0, PackageManager.MountedFileSystem.FileCount, "Unmounted everything" );
	}

	async Task CompileProjects()
	{
		await Project.SyncWithPackageManager();
		await Project.CompileAsync();
	}

	[TestMethod]
	public async Task LoadRuntimeGamePackage()
	{
		var (library, packageLoader, enroller) = Preamble();

		Project.AddFromFileBuiltIn( "addons/base" );
		var project = Project.AddFromFile( "unittest/addons/spacewars" );


		await CompileProjects();


		Assert.AreEqual( 0, library.Types.Count, "Library has no classes" );



		enroller.LoadPackage( project.Package.FullIdent );

		Assert.AreNotEqual( 0, library.Types.Count, "Library has classes" );

		var gameClass = library.GetType( "SpaceWarsGameManager" );
		Assert.IsNotNull( gameClass, "Found game class" );

		enroller.Dispose();
		packageLoader.Dispose();

		PackageManager.UnmountAll();
		Assert.AreEqual( 0, PackageManager.MountedFileSystem.FileCount );
	}

	/// <summary>
	/// Trying to access <see cref="TypeLibrary"/> from static constructors must
	/// throw an <see cref="InvalidOperationException"/>.
	/// </summary>
	[TestMethod]
	public async Task StaticCtorTypeLibraryThrows()
	{
		var (library, packageLoader, enroller) = Preamble();

		Project.AddFromFileBuiltIn( "addons/base/" );

		var project = Project.AddFromFile( "unittest/addons/cctortest" );

		await Project.SyncWithPackageManager();
		await Project.CompileAsync();

		enroller.LoadPackage( project.Package.FullIdent );
		packageLoader.Tick();

		var property = library
			.GetType( "StaticCtorTest.Example" )
			.Properties
			.Single( x => x.Name == "ThrownException" );

		var exception = (Exception)property.GetValue( null );

		Assert.IsInstanceOfType<InvalidOperationException>( exception );

		Assert.IsTrue( exception.Message.Contains( "Disabled during static constructors." ) );

		enroller.Dispose();
		packageLoader.Dispose();

		PackageManager.UnmountAll();
		Assert.AreEqual( 0, PackageManager.MountedFileSystem.FileCount );
	}

	//
	// Game (runtime)
	//   <- addon (runtime)
	//
	// LiveBackend keeps this out of PR runs: the compile currently produces zero classes in
	// the test harness (pre-existing breakage shared with the rest of the package cluster -
	// SingleDownload/CompileCodeArchive fail the same way in LiveBackend runs).
	[TestMethod]
	[TestCategory( "LiveBackend" )]
	public async Task Hotloading()
	{
		// This test rewrites a committed fixture file - capture the original
		// contents so we can restore them whatever happens.
		const string shipFile = "unittest/addons/spacewars/code/MySpaceShip.cs";
		var originalShipSource = System.IO.File.ReadAllBytes( shipFile );

		try
		{
			await HotloadingBody();
		}
		finally
		{
			System.IO.File.WriteAllBytes( shipFile, originalShipSource );
		}
	}

	/// <summary>
	/// The actual <see cref="Hotloading"/> test body - split out so the caller can
	/// restore the fixture file in a finally block.
	/// </summary>
	private async Task HotloadingBody()
	{
		var (library, packageLoader, enroller) = Preamble();

		bool switchEvent = false;
		enroller.OnAssemblyRemoved = ( a ) =>
		{
			switchEvent = true;
		};

		System.IO.File.WriteAllText( "unittest/addons/spacewars/code/MySpaceShip.cs", """"""

			using Sandbox;

			namespace PackageTests;

			class BlueSpaceShip : BaseSpaceShip
			{
				public override void ShootLaser()
				{
					Log.Info( "Shooting a laser on Blue Space Ship!" );
				}
			}
			

			"""""" );

		// Need to have base addon
		Project.AddFromFileBuiltIn( "addons/base/" );
		var spacewars = Project.AddFromFile( "unittest/addons/spacewars" );
		await Project.SyncWithPackageManager();
		await Project.CompileAsync();

		Assert.AreEqual( 0, library.Types.Count, "Library has no classes" );

		enroller.LoadPackage( spacewars.Package.FullIdent );

		Assert.AreNotEqual( 0, library.Types.Count, "Library has classes" );

		var gameClass = library.GetType( "SpaceWarsGameManager" );
		Assert.IsNotNull( gameClass, "Found game class" );

		var blueShipClass = library.GetType( "BlueSpaceShip" );
		Assert.IsNotNull( blueShipClass, "Found BlueSpaceShip class" );

		packageLoader.Tick();

		//
		// Lets trigger a Fast Hotload
		//

		System.IO.File.WriteAllText( "unittest/addons/spacewars/code/MySpaceShip.cs", """"""

			using Sandbox;

			namespace PackageTests;

			class BlueSpaceShip : BaseSpaceShip
			{
				public override void ShootLaser()
				{
					Log.Info( "This method is changed! Should just use fast hotload!" );
				}
			}
			

			"""""" );

		Assert.IsTrue( await FileWatch.TickUntilFileChanged( "*myspaceship.cs" ) );

		await Project.CompileAsync();

		Assert.IsTrue( await FileWatch.TickUntilFileChanged( "/.bin/package.local.spacewars.dll" ) );
		Assert.IsTrue( !switchEvent, "Switch Event should not have been triggered yet" );

		packageLoader.Tick();
		Assert.IsTrue( !switchEvent, "Switch Event should not have been triggered yet (no full hotload)" );

		//
		// Lets trigger a full Hotload
		//

		System.IO.File.WriteAllText( "unittest/addons/spacewars/code/MySpaceShip.cs", """"""

			using Sandbox;

			namespace PackageTests;

			class BlueSpaceShip : BaseSpaceShip
			{

				public void ExtraMethod()
				{
					Log.Info( "Adding an extra method should mean that Fast Hotload doesn't work.."  );
				}

				public override void ShootLaser()
				{
					Log.Info( "This method is changed! Should just use fast hotload!" );
				}
			}
			

			"""""" );

		Assert.IsTrue( await FileWatch.TickUntilFileChanged( "*myspaceship.cs" ) );

		await Project.CompileAsync();

		Assert.IsTrue( await FileWatch.TickUntilFileChanged( "/.bin/package.local.spacewars.dll" ) );
		Assert.IsTrue( !switchEvent, "Switch Event should not have been triggered yet" );

		packageLoader.Tick();

		Assert.IsTrue( switchEvent, "Switch Event should have been triggered by a full hotload" );

		//Assert.IsTrue( Project.CompileAsync )

		packageLoader.Dispose();

		PackageManager.UnmountAll();
		Assert.AreEqual( 0, PackageManager.MountedFileSystem.FileCount );
	}

	/// <summary>
	/// <see cref="Sandbox.PackageLoader.LoadPendingChanges"/> uses <see cref="Package.SortByReferences"/> to
	/// ensure referenced packages are loaded before the packages that reference them.
	/// </summary>
	[TestMethod]
	public void SortByReferences()
	{
		var org = new Package.Organization { Ident = "testorg" };

		var basePackage = new Package
		{
			Org = org,
			Ident = "base"
		};

		var package1 = new Package
		{
			Org = org,
			Ident = "package1",
			PackageReferences = new[] { "testorg.base", "otherorg.example" }
		};

		var package2 = new Package
		{
			Org = org,
			Ident = "package2",
			PackageReferences = new[] { "testorg.base", "testorg.package1" }
		};

		var sorted = Package.SortByReferences( new[] { package2, basePackage, package1 } )
			.ToArray();

		Assert.AreEqual( 0, Array.IndexOf( sorted, basePackage ) );
		Assert.AreEqual( 1, Array.IndexOf( sorted, package1 ) );
		Assert.AreEqual( 2, Array.IndexOf( sorted, package2 ) );
	}
}
