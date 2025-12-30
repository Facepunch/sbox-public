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
