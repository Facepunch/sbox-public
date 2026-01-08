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
	[DataRow( "fish.sauna", 105400L, "scenes/finland.scene", 304,
		"33319bc0-e128-4e9d-a45a-8dedd8e9cf81", // Unable to find node definition for 'event.endsession'
		"6e10f594-201e-4859-b750-77442dbf67a7", // Unable to find type 'EventAreaFinder'
		"ffc76fb6-66ea-43ca-a028-9521be9422b2"  // Unable to find type 'EventAreaFinder'
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

		Game.ActiveScene = new Scene();
		Game.ActiveScene.LoadFromFile( sceneFile.ResourcePath );

		var graphs = Game.NodeLibrary.GetGraphs().ToArray();
		var anyErrors = false;

		foreach ( var graph in graphs.OrderBy( x => x.Guid ) )
		{
			Console.WriteLine( $"{graph.Guid}: {graph.Title} {(ignoreGuidSet.Contains( graph.Guid ) ? "(IGNORED)" : "")} {graph.Nodes.Count} {graph.SourceLocation}" );

			foreach ( var message in graph.Messages )
			{
				Console.WriteLine( $"  {message}" );
			}

			if ( !ignoreGuidSet.Contains( graph.Guid ) )
			{
				anyErrors |= graph.HasErrors();
			}
		}

		Assert.AreEqual( graphCount, graphs.Length, "Scene has expected graph count" );
		Assert.IsFalse( anyErrors, "No unexpected graph errors" );

		GameInstanceDll.Current?.CloseGame();
	}
}
