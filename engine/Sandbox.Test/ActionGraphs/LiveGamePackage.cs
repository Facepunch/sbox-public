using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;

namespace ActionGraphs;

[TestClass]
public class LiveGamePackage
{
	/// <summary>
	/// Asserts that all the ActionGraphs referenced by a given scene in a downloaded
	/// package have no errors.
	/// </summary>
	[TestMethod]
	[DataRow( "fish.sauna", 76972L, "scenes/finland.scene", 14,
		"d174cab5-7a05-476c-a545-4db2fd685032", // Prefab references game object from other scene
		"e9ac7c29-ff9f-4c3c-8d9d-7228c4711248", // Inventory method changed parameter types
		"462927b9-1f01-4ba8-9f6b-2e1e6a5934e4"  // Inventory method changed parameter types
	)]
	public void AssertNoGraphErrorsInScene( string packageName, long? version, string scenePath, int graphCount, params string[] ignoreGuids )
	{
		var dir = $"{Environment.CurrentDirectory}/.source2/test_download_cache/actiongraph";

		AssetDownloadCache.Initialize( dir );

		PackageManager.UnmountAll();
		// Let's make sure we have base content mounted
		IGameInstanceDll.Current?.Bootstrap();

		var ignoreGuidSet = new HashSet<Guid>( ignoreGuids.Select( Guid.Parse ) );

		var packageIdent = version is { } v ? $"{packageName}#{v}" : packageName;

		// Use the production loading logic - run blocking to ensure it completes
		var loadTask = GameInstanceDll.Current.LoadGamePackageAsync( packageIdent, GameLoadingFlags.Host, CancellationToken.None );
		SyncContext.RunBlocking( loadTask );

		Assert.IsNotNull( GameInstanceDll.gameInstance, "Game instance should be loaded" );
		Assert.AreNotEqual( 0, PackageManager.MountedFileSystem.FileCount, "We have package files mounted" );
		Assert.AreNotEqual( 0, GlobalGameNamespace.TypeLibrary.Types.Count, "Library has classes" );

		var sceneFile = ResourceLibrary.Get<SceneFile>( scenePath );
		Assert.IsNotNull( sceneFile, "Target scene exists" );

		ActionGraphDebugger.Enabled = true;

		Game.ActiveScene = new Scene();
		Game.ActiveScene.LoadFromFile( sceneFile.ResourcePath );

		var graphs = ActionGraphDebugger.GetAllGraphs();
		Assert.AreEqual( graphCount, graphs.Count, "Scene has expected graph count" );

		var anyErrors = false;

		foreach ( var graph in graphs )
		{
			Console.WriteLine( $"{graph.Guid}: {graph.Title} {(ignoreGuidSet.Contains( graph.Guid ) ? "(IGNORED)" : "")}" );

			foreach ( var message in graph.Messages )
			{
				Console.WriteLine( $"  {message}" );
			}

			if ( !ignoreGuidSet.Contains( graph.Guid ) )
			{
				anyErrors |= graph.HasErrors();
			}
		}

		ActionGraphDebugger.Enabled = false;

		Assert.IsFalse( anyErrors, "No unexpected graph errors" );

		GameInstanceDll.Current?.CloseGame();
	}
}
