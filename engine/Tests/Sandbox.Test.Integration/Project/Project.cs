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
