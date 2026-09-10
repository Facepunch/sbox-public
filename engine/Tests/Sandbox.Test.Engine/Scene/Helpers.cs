using System;
using Sandbox.Utility;

namespace SceneTests;

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
	/// Creates a <see cref="SceneFile"/> with the given <paramref name="resourcePath"/>, populates it with game objects as given
	/// by <paramref name="gameObjectsJson"/>, then loads it with <see cref="Scene.Load(SceneLoadOptions)"/>.
	/// </summary>
	public static Scene LoadSceneFromJson( string resourcePath, params string[] gameObjectsJson )
	{
		var scene = new Scene();

		using var _ = scene.Push();

		var sceneFile = new SceneFile { GameObjects = gameObjectsJson.Select( Sandbox.Json.ParseToJsonObject ).ToArray() };

		sceneFile.RegisterWeakResourceId( resourcePath );

		var options = new SceneLoadOptions();
		options.SetScene( sceneFile );

		scene.Load( options );

		return scene;
	}
}
