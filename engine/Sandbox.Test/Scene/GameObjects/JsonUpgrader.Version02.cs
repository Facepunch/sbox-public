using Sandbox.Internal;
using Sandbox.SceneTests;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Sandbox.Json;

namespace GameObjects;

[TestClass]
public class JsonUpgrader02
{
	TypeLibrary TypeLibrary;

	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		// Replace TypeLibrary / NodeLibrary with mocked ones, store the originals

		_oldTypeLibrary = Game.TypeLibrary;

		TypeLibrary = new Sandbox.Internal.TypeLibrary();
		TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( PrefabFile ).Assembly, false );
		JsonUpgrader.UpdateUpgraders( TypeLibrary );

		Game.TypeLibrary = TypeLibrary;
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Make sure our mocked TypeLibrary doesn't leak out, restore old ones
		Game.TypeLibrary = _oldTypeLibrary;
	}

	[TestMethod]
	public void UpgraderConvertsInterpolationToFlags()
	{
		string oldFormatJson = """
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"Flags": 0,
			"Name": "piss (1)",
			"Position": "4.408689,0.0000001473241,32.37395",
			"Tags": "particles",
			"Enabled": true,
			"Rotation": "0,0,0,1",
			"NetworkInterpolation": false,
			"Scale": "1,1,1"
		}
		""";

		// Parse the JSON into a JsonObject that we can pass to the upgrader
		var jsonObject = JsonNode.Parse( oldFormatJson ).AsObject();

		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Act - Call the upgrader
		GameObject.Upgrader_v2( jsonObject );

		var go = new GameObject();
		go.Deserialize( jsonObject );

		Assert.IsTrue( go.Network.Flags.HasFlag( NetworkFlags.NoInterpolation ) );
		Assert.IsTrue( !go.Network.Flags.HasFlag( NetworkFlags.NoPositionSync ) );
	}

	[TestMethod]
	public void UpgraderConvertsInterpolationWithExistingFlags()
	{
		string oldFormatJson = $$"""
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"Flags": 0,
			"Name": "piss (1)",
			"Position": "4.408689,0.0000001473241,32.37395",
			"Tags": "particles",
			"Enabled": true,
			"Rotation": "0,0,0,1",
			"NetworkInterpolation": false,
			"NetworkFlags": {{(int)NetworkFlags.NoPositionSync}},
			"Scale": "1,1,1"
		}
		""";

		// Parse the JSON into a JsonObject that we can pass to the upgrader
		var jsonObject = JsonNode.Parse( oldFormatJson ).AsObject();

		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Act - Call the upgrader
		GameObject.Upgrader_v2( jsonObject );

		var go = new GameObject();
		go.Deserialize( jsonObject );

		Assert.IsTrue( go.Network.Flags.HasFlag( NetworkFlags.NoInterpolation ) );
		Assert.IsTrue( go.Network.Flags.HasFlag( NetworkFlags.NoPositionSync ) );
		Assert.IsTrue( !go.Network.Flags.HasFlag( NetworkFlags.NoRotationSync ) );
	}
}

