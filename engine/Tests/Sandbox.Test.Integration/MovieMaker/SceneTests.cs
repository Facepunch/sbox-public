using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using SceneTests;

namespace MovieMakerTests;

#nullable enable

public abstract class SceneTestBase
{
	private IDisposable? _sceneScope;
	private TypeLibrary? _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_sceneScope = new Scene().Push();
		_oldTypeLibrary = Game.TypeLibrary;

		Game.TypeLibrary = SharedTypeLibrary.SystemEngineAndTests;
		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );
	}

	[TestCleanup]
	public void TestCleanup()
	{
		_sceneScope?.Dispose();

		Game.TypeLibrary = _oldTypeLibrary;
	}

	protected static void RegisterSimplePrefab( string resourcePath, params IEnumerable<JsonObject> componentJson )
	{
		var name = Path.GetFileNameWithoutExtension( resourcePath ).ToTitleCase();

		var componentArray = componentJson
			.Select( JsonNode ( x ) =>
			{
				x["Id"] ??= Guid.NewGuid();
				return x;
			} )
			.ToArray();

		var rootJson = new JsonObject
		{
			{ "Id", Guid.NewGuid() },
			{ "Name", name },
			{ "Enabled", true },
			{ "NetworkMode", 2 },
			{ "Components", new JsonArray( componentArray ) }
		};

		Helpers.RegisterPrefabFromJson( resourcePath, rootJson.ToJsonString() );
	}
}
