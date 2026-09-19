using Sandbox.Internal;
using SceneTests;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SceneTests.GameObjects;

[TestClass]
[DoNotParallelize]
public class JsonUpgrader03Test : SceneTest
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

	static JsonObject MakeGameObjectJson( string componentsJson, long flags = 0 )
	{
		var json = $$"""
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"Flags": {{flags}},
			"Name": "thing",
			"Position": "0,0,0",
			"Enabled": true,
			"Components": [ {{componentsJson}} ]
		}
		""";

		return JsonNode.Parse( json ).AsObject();
	}

	static GameObjectFlags GetFlags( JsonObject jsonObject )
	{
		return (GameObjectFlags)jsonObject["Flags"].Deserialize<long>();
	}

	[TestMethod]
	public void StaticColliderFlagsObjectStatic()
	{
		var jsonObject = MakeGameObjectJson( """
			{
				"__type": "Sandbox.ModelCollider",
				"__guid": "6d5f6a19-f1ae-4270-b2b7-6a0cd0abf0c1",
				"Static": true
			}
			""" );

		GameObject.Upgrader_v3( jsonObject );

		Assert.IsTrue( GetFlags( jsonObject ).Contains( GameObjectFlags.Static ) );
	}

	[TestMethod]
	public void NonStaticColliderLeavesObjectAlone()
	{
		var jsonObject = MakeGameObjectJson( """
			{
				"__type": "Sandbox.ModelCollider",
				"__guid": "6d5f6a19-f1ae-4270-b2b7-6a0cd0abf0c1",
				"Static": false
			}
			""" );

		GameObject.Upgrader_v3( jsonObject );

		Assert.IsFalse( GetFlags( jsonObject ).Contains( GameObjectFlags.Static ) );
	}

	[TestMethod]
	public void MeshComponentFlagsObjectStatic()
	{
		// MeshComponents are level geometry, presence alone marks it static
		var jsonObject = MakeGameObjectJson( """
			{
				"__type": "Sandbox.MeshComponent",
				"__guid": "6d5f6a19-f1ae-4270-b2b7-6a0cd0abf0c1",
				"Static": false,
				"Collision": "Hull"
			}
			""" );

		GameObject.Upgrader_v3( jsonObject );

		Assert.IsTrue( GetFlags( jsonObject ).Contains( GameObjectFlags.Static ) );
	}

	[TestMethod]
	public void RigidbodyNeverStatic()
	{
		// Rigidbody wins over the static collider
		var jsonObject = MakeGameObjectJson( """
			{
				"__type": "Sandbox.Rigidbody",
				"__guid": "6d5f6a19-f1ae-4270-b2b7-6a0cd0abf0c1",
				"MotionEnabled": false
			},
			{
				"__type": "Sandbox.ModelCollider",
				"__guid": "83b2e5a2-2f61-4d68-9d5e-0f4a53b6e1d2",
				"Static": true
			}
			""" );

		GameObject.Upgrader_v3( jsonObject );

		Assert.IsFalse( GetFlags( jsonObject ).Contains( GameObjectFlags.Static ) );
	}

	[TestMethod]
	public void ExistingFlagsArePreserved()
	{
		var jsonObject = MakeGameObjectJson( """
			{
				"__type": "Sandbox.BoxCollider",
				"__guid": "6d5f6a19-f1ae-4270-b2b7-6a0cd0abf0c1",
				"Static": true
			}
			""", flags: (long)GameObjectFlags.Hidden );

		GameObject.Upgrader_v3( jsonObject );

		var flags = GetFlags( jsonObject );

		Assert.IsTrue( flags.Contains( GameObjectFlags.Static ) );
		Assert.IsTrue( flags.Contains( GameObjectFlags.Hidden ), "upgrading must not clear existing flags" );
	}

	[TestMethod]
	public void VersionMachineryRunsUpgrader()
	{
		// No explicit call - the machinery should find it by version and stamp it
		var jsonObject = MakeGameObjectJson( """
			{
				"__type": "Sandbox.ModelCollider",
				"__guid": "6d5f6a19-f1ae-4270-b2b7-6a0cd0abf0c1",
				"Static": true
			}
			""" );

		JsonUpgrader.Upgrade( 2, jsonObject, typeof( GameObject ) );

		Assert.IsTrue( GetFlags( jsonObject ).Contains( GameObjectFlags.Static ) );
		Assert.AreEqual( 3, jsonObject["__version"].Deserialize<int>() );
	}
}
