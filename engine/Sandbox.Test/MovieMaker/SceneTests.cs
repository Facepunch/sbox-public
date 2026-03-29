using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Sandbox.MovieMaker.Properties;
using Sandbox.SceneTests;

namespace TestMovieMaker;

#nullable enable

public abstract class SceneTests
{
	private IDisposable? _sceneScope;
	private TypeLibrary? _oldTypeLibrary;
	private bool _oldMemberPropertyUseDelegate;

	[TestInitialize]
	public void TestInitialize()
	{
		_sceneScope = new Scene().Push();
		_oldTypeLibrary = Game.TypeLibrary;
		_oldMemberPropertyUseDelegate = MemberProperty.UseDelegate;

		Game.TypeLibrary = new TypeLibrary();
		Game.TypeLibrary.AddAssembly( typeof( Vector3 ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		Game.TypeLibrary.AddAssembly( typeof( SceneTests ).Assembly, false );
		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );

		Game.TypeLibrary = Game.TypeLibrary;
	}

	[TestCleanup]
	public void TestCleanup()
	{
		_sceneScope?.Dispose();

		Game.TypeLibrary = _oldTypeLibrary;
		MemberProperty.UseDelegate = _oldMemberPropertyUseDelegate;
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
