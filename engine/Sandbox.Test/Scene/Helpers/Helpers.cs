using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using Sandbox.Internal;
using Sandbox.Network;
using Sandbox.Utility;
using System;
using System.Collections.Generic;

namespace Sandbox.SceneTests;

#nullable enable

/// <summary>
/// Helper methods for writing scene tests.
/// </summary>
internal static class Helpers
{
	/// <summary>
	/// Registers a prefab file in <see cref="ResourceLibrary"/> with the given <paramref name="resourcePath"/> and
	/// with a root object defined by <paramref name="rootObjectJson"/>. Dispose the return value to unregister it.
	/// </summary>
	public static IDisposable RegisterPrefabFromJson( string resourcePath, string rootObjectJson )
	{
		var wrappedRootObject = "{ \"RootObject\": " + rootObjectJson + "}";

		var prefabFile = new PrefabFile();
		prefabFile.LoadFromJson( wrappedRootObject );
		prefabFile.RegisterWeakResourceId( resourcePath );
		prefabFile.Register( resourcePath );

		return new DisposeAction( () => Game.Resources.Unregister( prefabFile ) );
	}

	/// <summary>
	/// Creates a <see cref="NodeLibrary"/> and makes it active, so you can create / serialize <see cref="ActionGraph"/> instances
	/// in tests.
	/// </summary>
	public static IDisposable PushNodeLibrary()
	{
		var oldNodeLib = Game.NodeLibrary;

		var nodeLib = Game.NodeLibrary = new NodeLibrary( new TypeLoader( () => Game.TypeLibrary ) );

		nodeLib.AddAssembly( typeof( LogNodes ).Assembly );
		nodeLib.AddAssembly( typeof( Scene ).Assembly );

		return new DisposeAction( () =>
		{
			Game.NodeLibrary = oldNodeLib;
		} );
	}

	/// <summary>
	/// Creates a <see cref="SceneFile"/> with the given <paramref name="resourcePath"/>, populates it with game objects as given
	/// by <paramref name="gameObjectsJson"/>, then loads it with <see cref="Scene.Load(SceneLoadOptions)"/>.
	/// </summary>
	public static Scene LoadSceneFromJson( string resourcePath, params string[] gameObjectsJson )
	{
		var scene = new Scene();

		using var _ = scene.Push();

		var sceneFile = new SceneFile { GameObjects = gameObjectsJson.Select( Json.ParseToJsonObject ).ToArray() };

		sceneFile.RegisterWeakResourceId( resourcePath );

		var options = new SceneLoadOptions();
		options.SetScene( sceneFile );

		scene.Load( options );

		return scene;
	}

	/// <summary>
	/// Gets the resource path of the action graph that implements the given <paramref name="delegate"/>.
	/// </summary>
	public static string? GetSourcePath( this Delegate @delegate )
	{
		return (@delegate.GetActionGraphInstance()?.Graph.SourceLocation as GameResourceSourceLocation)?.Resource.ResourcePath;
	}

	/// <summary>
	/// A <see cref="NetworkSystem"/> with a <see cref="TestConnection"/> so that messages broadcast
	/// from a host can be inspected. Dispose at the end of the test to clean up this network system.
	/// </summary>
	public record TestNetworkSystem( NetworkSystem Server, TestConnection Connection ) : IDisposable
	{
		/// <summary>
		/// Counts messages broadcast by the server with the given packed content type <typeparamref name="T"/>.
		/// </summary>
		public int GetMessageCount<T>() => Connection.Messages.Count( x => x.Payload is T );

		/// <summary>
		/// Dispose at the end of the test to clean up this network system.
		/// </summary>
		public void Dispose()
		{
			Server.GameSystem.Dispose();

			Networking.System = null;
		}
	}

	/// <summary>
	/// Creates and initializes a <see cref="NetworkSystem"/> with a <see cref="TestConnection"/> so that messages broadcast
	/// from a host can be inspected.
	/// </summary>
	public static TestNetworkSystem InitializeHostWithTestConnection()
	{
		var server = new NetworkSystem( "server", GlobalGameNamespace.TypeLibrary );

		Networking.System = server;

		server.InitializeHost();
		server.GameSystem = new SceneNetworkSystem( GlobalGameNamespace.TypeLibrary, server );

		var testConnection = new TestConnection();

		server.OnConnected( testConnection );
		server.AddConnection( testConnection, new UserInfo { UserData = new Dictionary<string, string>() } );

		testConnection.State = Connection.ChannelState.Connected;

		return new TestNetworkSystem( server, testConnection );
	}
}
