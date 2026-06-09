using Sandbox.Diagnostics;
using System;

namespace Projects;

[TestClass]
public class ProjectTests
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
	/// All Get*Path methods return absolute paths
	/// </summary>
	[TestMethod]
	public void GetProjectPathsAreAbsolute()
	{
		var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );

		Assert.IsTrue( System.IO.Path.IsPathRooted( project.GetRootPath() ) );
		Assert.IsTrue( System.IO.Path.IsPathRooted( project.GetCodePath() ) );
		Assert.IsTrue( System.IO.Path.IsPathRooted( project.GetAssetsPath() ) );
		Assert.IsTrue( System.IO.Path.IsPathRooted( project.GetEditorPath() ) );
		Assert.IsTrue( System.IO.Path.IsPathRooted( project.GetLocalizationPath() ) );
		Assert.IsTrue( System.IO.Path.IsPathRooted( project.GetProjectPath() ) );
	}

	/// <summary>
	/// Get*Path returns paths rooted under the project root
	/// </summary>
	[TestMethod]
	public void GetProjectPathsAreRootedUnderProject()
	{
		var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );
		var rootPath = project.GetRootPath();

		Assert.IsTrue( project.GetCodePath().StartsWith( rootPath ) );
		Assert.IsTrue( project.GetAssetsPath().StartsWith( rootPath ) );
		Assert.IsTrue( project.GetEditorPath().StartsWith( rootPath ) );
		Assert.IsTrue( project.GetLocalizationPath().StartsWith( rootPath ) );
	}

	/// <summary>
	/// GetProjectPath finds the .sbproj file via the ProjectFileSystem
	/// </summary>
	[TestMethod]
	public void GetProjectPathFindsSbproj()
	{
		var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );
		var projectPath = project.GetProjectPath();

		Assert.IsNotNull( projectPath );
		Assert.IsTrue( projectPath.EndsWith( ".sbproj" ) );
		Assert.IsTrue( System.IO.File.Exists( projectPath ) );
	}

	/// <summary>
	/// Has*Path returns true for all folders present in case-insensitive addon, where all
	/// folders are lowercase. On Linux, CIPFS must resolve the casing correctly.
	/// </summary>
	[TestMethod]
	public void HasProjectPathsAreCaseInsensitive()
	{
		var project = Project.AddFromFile( "unittest/addons/case-insensitive/case_insensitive.sbproj" );

		Assert.IsTrue( project.HasAssetsPath() );
		Assert.IsTrue( project.HasCodePath() );
		Assert.IsTrue( project.HasEditorPath() );
	}

	/// <summary>
	/// testmap has a valid project filesystem but no Code folder: GetCodePath still
	/// resolves a (would-be) path while HasCodePath reports false. Get and Has are
	/// decoupled.
	/// </summary>
	[TestMethod]
	public void HasCodePathReturnsFalseWhenMissing()
	{
		var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );

		Assert.IsNotNull( project.GetCodePath() );  // valid FS resolves the path...
		Assert.IsFalse( project.HasCodePath() );    // ...but the folder isn't there
	}

	/// <summary>
	/// HasCodePath returns false and does not throw when ProjectFileSystem is null
	/// (a project that was never loaded, or failed to load). RootDirectory is set to
	/// a real folder so the test still exercises the null-filesystem path even if a
	/// RootDirectory guard is later added to HasCodePath.
	/// </summary>
	[TestMethod]
	public void HasCodePathReturnsFalseWhenFileSystemIsNull()
	{
		var project = new Project
		{
			RootDirectory = new System.IO.DirectoryInfo( "unittest/addons/testmap" )
		};

		// ProjectFileSystem is null here; the null check must stay quiet, not throw.
		Assert.IsFalse( project.HasCodePath() );
		Assert.IsNull( project.GetCodePath() );
	}

	/// <summary>
	/// Find and load a local package
	/// </summary>
	[TestMethod]
	public async Task AddBaseAddon()
	{
		var project = Project.AddFromFileBuiltIn( "addons/base/.sbproj" );

		Assert.IsNotNull( project.ConfigFilePath );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

		await Project.SyncWithPackageManager();
		await Project.CompileAsync();
	}

	/*
	[TestMethod]
	public async Task OpenGameProject()
	{
		Project.AddFromFileBuiltIn( "addons/base/.sbproj" );

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

	/// <summary>
	/// Initialize the menu addon
	/// </summary>
	[TestMethod]
	public async Task MenuInitialization()
	{
		Project.AddFromFileBuiltIn( "addons/base/.sbproj" );

		var project = Project.AddFromFile( "addons/menu/.sbproj" );

		Assert.IsNotNull( project.ConfigFilePath );
		Assert.IsNotNull( project.GetRootPath() );
		Assert.IsNotNull( project.GetAssetsPath() );

		await Project.SyncWithPackageManager();
		await Project.CompileAsync();

		var assemblies = PackageManager.MountedFileSystem.FindFile( "/.bin/", "*.dll", false ).ToArray();

		Assert.AreEqual( 2, assemblies.Length );

		foreach ( var asm in assemblies )
		{
			Console.WriteLine( asm );
		}

	}
}
