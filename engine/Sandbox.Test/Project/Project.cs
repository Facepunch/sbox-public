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
	/// GetRootPath returns a non-null absolute path
	/// </summary>
	[TestMethod]
	public void GetRootPathIsAbsolute()
	{
		var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );
		var rootPath = project.GetRootPath();

		Assert.IsNotNull( rootPath );
		Assert.IsTrue( System.IO.Path.IsPathRooted( rootPath ) );
	}

	/// <summary>
	/// Get*Path returns paths rooted under the project root
	/// </summary>
	[TestMethod]
	public void GetPathsAreRootedUnderProject()
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

		// Check .sbproj as ending and as full filename
		Assert.IsNotNull( projectPath );
		Assert.IsTrue( projectPath.EndsWith( ".sbproj" ) );
		Assert.AreEqual( ".sbproj", System.IO.Path.GetFileName( projectPath ) ); 
	}

	/// <summary>
	/// Has*Path returns correct existence based on which folders are present
	/// </summary>
	[TestMethod]
	public void HasPathReflectsActualFolders()
	{
		var project = Project.AddFromFile( "addons/base/.sbproj" );

		// at the time of writing this, /base/code is lowercase, so a CIPFS
		// test on Linux can do an existence check and should pass even when
		// asking for /base/Code instead.

		Assert.IsTrue( project.HasAssetsPath() );
		Assert.IsTrue( project.HasCodePath() );
		Assert.IsFalse( project.HasEditorPath() );
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
