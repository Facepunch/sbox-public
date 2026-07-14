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
	/// Verifies Has*Path resolves lowercase folders case-insensitively (CIPFS on Linux).
	/// </summary>
	[TestMethod]
	public void HasProjectPathsAreCaseInsensitive()
	{
		var project = Project.AddFromFile( "unittest/addons/case-insensitive/.sbproj" );

		// Asserts if (/Assets, /Code, /Editor) matches on-disk (/assets, /code, /editor)
		Assert.IsTrue( project.HasAssetsPath() );
		Assert.IsTrue( project.HasCodePath() );
		Assert.IsTrue( project.HasEditorPath() );
	}

	/// <summary>
	/// Verifies HasCodePath returns false when the Code folder is missing, even though
	/// GetCodePath still resolves a path.
	/// </summary>
	[TestMethod]
	public void HasCodePathReturnsFalseWhenMissing()
	{
		var project = Project.AddFromFile( "unittest/addons/testmap/.sbproj" );

		Assert.IsNotNull( project.GetCodePath() );
		Assert.IsFalse( project.HasCodePath() );
	}

	/// <summary>
	/// Verifies HasCodePath returns false without throwing when the project has no
	/// filesystem (RootDirectory set, ProjectFileSystem null).
	/// </summary>
	[TestMethod]
	public void HasCodePathReturnsFalseWhenFileSystemIsNull()
	{
		var project = new Project
		{
			RootDirectory = new System.IO.DirectoryInfo( "unittest/addons/testmap" )
		};

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

		var assemblies = PackageManager.ActivePackages
			.SelectMany( ap => ap.AssemblyFileSystem.FindFile( "/", "*.dll", true ) )
			.ToArray();

		Assert.AreEqual( 2, assemblies.Length );

		foreach ( var asm in assemblies )
		{
			Console.WriteLine( asm );
		}

	}
}
