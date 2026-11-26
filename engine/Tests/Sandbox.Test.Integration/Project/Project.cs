using Sandbox.Diagnostics;
using System;
using System.IO;

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

	/// <summary>
	/// A locked recommendations file can wait until the next solution refresh.
	/// </summary>
	[TestMethod]
	public async Task GenerateSolutionWithLockedRecommendations()
	{
		var root = Directory.CreateTempSubdirectory( "sbox_vscode_" );
		var previousProject = Project.Current;
		Project project = null;

		try
		{
			var code = root.CreateSubdirectory( "Code" );
			var config = Path.Combine( root.FullName, ".sbproj" );
			File.WriteAllText( config, """{ "Title": "Workspace", "Type": "game", "Org": "local", "Ident": "workspace", "Schema": 1 }""" );
			project = Project.AddFromFile( config );
			Assert.IsNotNull( project );
			Project.Current = project;

			var vscode = root.CreateSubdirectory( ".vscode" );
			var extensions = Path.Combine( vscode.FullName, "extensions.json" );
			var settings = Path.Combine( vscode.FullName, "settings.json" );

			File.WriteAllText( extensions, "{}" );
			using ( File.Open( extensions, FileMode.Open, FileAccess.Read, FileShare.None ) )
			{
				await Project.GenerateSolution();
			}

			Assert.AreEqual( "{}", File.ReadAllText( extensions ) );
			StringAssert.Contains( File.ReadAllText( settings ), "slang.additionalSearchPaths" );
			Assert.IsTrue( File.Exists( Path.Combine( code.FullName, "workspace.csproj" ) ) );
			Assert.IsTrue( File.Exists( Path.Combine( root.FullName, "workspace.slnx" ) ) );

			await Project.GenerateSolution();
			StringAssert.Contains( File.ReadAllText( extensions ), "ms-dotnettools.csdevkit" );

			var timestamp = new DateTime( 2000, 1, 1, 0, 0, 0, DateTimeKind.Utc );
			File.SetLastWriteTimeUtc( extensions, timestamp );
			File.SetLastWriteTimeUtc( settings, timestamp );
			await Project.GenerateSolution();
			Assert.AreEqual( timestamp, File.GetLastWriteTimeUtc( extensions ) );
			Assert.AreEqual( timestamp, File.GetLastWriteTimeUtc( settings ) );
		}
		finally
		{
			Project.Current = previousProject;
			if ( project is not null )
				Project.Remove( project );

			root.Delete( true );
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
