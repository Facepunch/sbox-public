using Sandbox.Diagnostics;
using System;

namespace ProjectTests;

[TestClass]
public class ProjectTest
{
	[TestInitialize]
	public void TestInitialize()
	{
		Logging.Enabled = true;
		Project.Clear();
		var dir = $"{Environment.CurrentDirectory}/.source2/test_download_cache/project";
		AssetDownloadCache.Initialize( dir );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Project.Clear();
	}

	/// <summary>
	/// Find and load a local package
	/// </summary>
	[TestMethod]
	public void AddProject()
	{
		var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );

		Assert.IsNotNull( project.ConfigFilePath );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

	}

	/// <summary>
	/// Games compile against every library. One installed while the game is open has to be referenced
	/// straight away, or game code can't use it until the editor restarts.
	/// </summary>
	[TestMethod]
	public void AddingALibraryReferencesItFromOpenGames()
	{
		var game = Project.AddFromFile( "unittest/addons/spacewars/.sbproj" );

		var libraryFolder = System.IO.Path.Combine( System.IO.Path.GetTempPath(), $"sbox_library_{Guid.NewGuid():N}" );
		System.IO.Directory.CreateDirectory( System.IO.Path.Combine( libraryFolder, "Code" ) );
		System.IO.File.WriteAllText( System.IO.Path.Combine( libraryFolder, "testlibrary.sbproj" ),
			"""{ "Title": "Test Library", "Type": "library", "Org": "local", "Ident": "testlibrary", "Schema": 1 }""" );
		System.IO.File.WriteAllText( System.IO.Path.Combine( libraryFolder, "Code", "Library.cs" ), "public class TestLibraryType { }" );

		try
		{
			var library = Project.AddFromFile( System.IO.Path.Combine( libraryFolder, "testlibrary.sbproj" ) );
			Project.UpdateLibraryReferences();

			Assert.IsTrue( game.Compiler.HasReference( library.Package.AssemblyName, deep: true ), "the game's compiler doesn't reference the new library" );
		}
		finally
		{
			Project.Clear();
			System.IO.Directory.Delete( libraryFolder, true );
		}
	}

	/*
	[TestMethod]
	public async Task OpenGameProject()
	{

		var project = Project.AddFromFile( "unittest/addons/spacewars", false );

		var ct = new CancellationToken();
		await EditorUtility.Projects.OpenProject( project.Path, null, ct ); ;

		Assert.IsNotNull( project.Path );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

		var assemblies = PackageManager.MountedFileSystem.FindFile( "/.bin/", "*.dll" ).ToArray();

		Assert.AreEqual( 2, assemblies.Length );

		foreach ( var asm in assemblies )
		{
			Console.WriteLine( asm );
		}
	}
	*/

	// The menu addon compile is covered by AddonTests.BuiltInTest.NoWarnings.
}
