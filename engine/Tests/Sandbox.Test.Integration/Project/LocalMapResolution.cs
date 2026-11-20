using System;
using System.IO;
using System.Text.Json;
using Sandbox.DataModel;

namespace ProjectTests;

/// <summary>
/// Exercises package discovery and installation for maps shipped as projects.
/// </summary>
[TestClass]
public class LocalMapResolutionTest
{
	/// <summary>
	/// A discovered scene map mounts its assets on first use and reports the scene to the game loader.
	/// Map projects without a primary asset retain their legacy content type.
	/// </summary>
	[TestMethod]
	public async Task SceneMapMountsFromDisk()
	{
		var root = Path.Combine( Environment.CurrentDirectory, "core", $"test_map_{Guid.NewGuid():N}" );
		var ident = $"local.{Path.GetFileName( root )}";
		var configPath = Path.Combine( root, ".sbproj" );
		const string scenePath = "maps/test.scene";
		var config = new ProjectConfig { Title = "Test Map", Type = "map", Org = "local", Ident = Path.GetFileName( root ), Schema = 1 };
		Project project = null;

		try
		{
			Directory.CreateDirectory( Path.Combine( root, "Assets", "maps" ) );
			File.WriteAllText( Path.Combine( root, "Assets", scenePath ), "{}" );
			File.WriteAllText( configPath, JsonSerializer.Serialize( config ) );
			project = Project.AddFromFile( configPath, active: false );
			Assert.AreEqual( "content", project.Package.TypeName );
			Project.Remove( project );

			config.Metadata["PrimaryAsset"] = scenePath;
			File.WriteAllText( configPath, JsonSerializer.Serialize( config ) );
			project = Project.AddFromFile( configPath, active: false );
			var map = await Package.FetchAsync( ident, false, useCache: false );
			Assert.AreSame( project.Package, map );
			Assert.AreEqual( "map", map.TypeName );
			Assert.AreEqual( scenePath, map.PrimaryAsset );

			var mounted = await PackageManager.InstallAsync( new PackageLoadOptions( ident, ident ) );
			Assert.AreSame( map, mounted.Package );
			Assert.IsTrue( mounted.FileSystem.FileExists( map.PrimaryAsset ) );
		}
		finally
		{
			PackageManager.UnmountTagged( ident );
			if ( project is not null ) Project.Remove( project );
			Directory.Delete( root, true );
		}
	}
}
