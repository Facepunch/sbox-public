using System;
using System.IO;
using System.Text.Json.Nodes;

namespace ResourceTests;

/// <summary>
/// The editor recompiles outdated assets while registering them on startup. Resources that are already loaded
/// get the new version right away, instead of a few frames later from the file watchers.
/// </summary>
[TestClass]
public class RecompiledResourceTests
{
	string _testPath;
	string _compiledScene;

	[TestInitialize]
	public void TestInitialize()
	{
		Project.Clear();

		var assetsPath = Project.AddFromFile( "unittest/addons/resources/.sbproj" ).GetAssetsPath();
		_compiledScene = Path.Combine( assetsPath, "test.scene_c" );

		_testPath = Path.Combine( assetsPath, $"recompiled_test_{Guid.NewGuid():N}" );
		Directory.CreateDirectory( _testPath );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Project.Clear();

		Directory.Delete( _testPath, recursive: true );
	}

	/// <summary>
	/// A resource recompiled after it was loaded, e.g. pulled while the editor was closed, gets the new version.
	/// </summary>
	[TestMethod]
	public void LoadsNewerCompiledFile()
	{
		var scene = RegisterLoadedScene( DateTime.UtcNow.AddMinutes( -1 ) );

		try
		{
			Assert.IsTrue( ResourceLoader.LoadIfRecompiled( scene, WriteCompiledScene(), WriteSource( "known source" ) ) );
			Assert.IsNull( scene.GameObjects, "Resource should have the recompiled data" );
		}
		finally
		{
			Game.Resources.Unregister( scene );
		}
	}

	/// <summary>
	/// A resource loaded after its compiled file was written is already up to date.
	/// </summary>
	[TestMethod]
	public void KeepsResourceLoadedAfterCompile()
	{
		var scene = RegisterLoadedScene( DateTime.UtcNow.AddMinutes( 1 ) );

		try
		{
			Assert.IsFalse( ResourceLoader.LoadIfRecompiled( scene, WriteCompiledScene(), WriteSource( "known source" ) ) );
			Assert.IsNotNull( scene.GameObjects );
		}
		finally
		{
			Game.Resources.Unregister( scene );
		}
	}

	/// <summary>
	/// A source changed outside of the editor is left to the file watchers, which report it as an external change.
	/// </summary>
	[TestMethod]
	public void LeavesExternalChangesToFileWatchers()
	{
		var scene = RegisterLoadedScene( DateTime.UtcNow.AddMinutes( -1 ) );

		try
		{
			Assert.IsFalse( ResourceLoader.LoadIfRecompiled( scene, WriteCompiledScene(), WriteSource( "changed outside the editor" ) ) );
			Assert.IsNotNull( scene.GameObjects );
		}
		finally
		{
			Game.Resources.Unregister( scene );
		}
	}

	/// <summary>
	/// Changes made in the editor and not saved yet aren't overwritten.
	/// </summary>
	[TestMethod]
	public void KeepsUnsavedChanges()
	{
		var scene = RegisterLoadedScene( DateTime.UtcNow.AddMinutes( -1 ) );
		scene.StateHasChanged();

		try
		{
			Assert.IsFalse( ResourceLoader.LoadIfRecompiled( scene, WriteCompiledScene(), WriteSource( "known source" ) ) );
			Assert.IsNotNull( scene.GameObjects );
		}
		finally
		{
			Game.Resources.Unregister( scene );
		}
	}

	SceneFile RegisterLoadedScene( DateTime loadedTime )
	{
		var scene = new SceneFile
		{
			GameObjects = [new JsonObject { ["Name"] = "outdated" }],
			LastSavedSourceHash = "known source".FastHash(),
			LastLoadedTime = loadedTime
		};

		scene.Register( $"{Path.GetFileName( _testPath )}/recompiled.scene" );
		return scene;
	}

	string WriteCompiledScene()
	{
		// Written rather than copied, so it gets the current time like a fresh compile
		var path = Path.Combine( _testPath, "recompiled.scene_c" );
		File.WriteAllBytes( path, File.ReadAllBytes( _compiledScene ) );
		return path;
	}

	string WriteSource( string contents )
	{
		var path = Path.Combine( _testPath, "recompiled.scene" );
		File.WriteAllText( path, contents );
		return path;
	}
}
