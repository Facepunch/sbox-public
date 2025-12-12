using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonTest.SceneUpgraders;

[TestClass]
public class SceneFileJsonUpgrader01
{
	[TestMethod]
	public void UpgraderConvertsOldIdToGuid()
	{
		// Arrange - Create a scene file JSON with old "Id" format using JSON literals
		string sceneJsonText = """
		{
			"Id": "a4889b20-837d-4244-83bd-0447c0aeaf71",
			"Name": "Test Scene",
			"GameObjects": [
				{
					"Id": "b58c23e1-9467-4a50-aeef-3a304aac2726",
					"Name": "Root Object",
					"Position": "0,0,0",
					"Components": [
						{
							"Id": "c3e8f712-542b-41d5-8d82-7207f8065a03",
							"__type": "ModelRenderer"
						}
					],
					"Children": [
						{
							"id": "d2f71e9a-4c88-4b3e-94e3-282e171a2e4a",
							"Name": "Child Object"
						}
					]
				}
			]
		}
		""";

		// Parse the JSON text into a JsonObject
		var sceneJson = JsonNode.Parse( sceneJsonText ).AsObject();

		// Act - Call the upgrader
		SceneFile.Upgrader_v1( sceneJson );

		// Assert

		// 1. Check that root level Id was converted to __guid
		Assert.IsFalse( sceneJson.ContainsKey( "Id" ), "Root Id should be removed" );
		Assert.IsTrue( sceneJson.ContainsKey( "__guid" ), "Root __guid should exist" );
		Assert.AreEqual( "a4889b20-837d-4244-83bd-0447c0aeaf71", sceneJson["__guid"].Deserialize<Guid>().ToString() );

		// 2. Check GameObject Id conversion
		var gameObject = sceneJson["GameObjects"][0].AsObject();
		Assert.IsFalse( gameObject.ContainsKey( "Id" ), "GameObject Id should be removed" );
		Assert.IsTrue( gameObject.ContainsKey( "__guid" ), "GameObject __guid should exist" );
		Assert.AreEqual( "b58c23e1-9467-4a50-aeef-3a304aac2726", gameObject["__guid"].Deserialize<Guid>().ToString() );

		// 3. Check Component Id conversion
		var component = gameObject["Components"][0].AsObject();
		Assert.IsFalse( component.ContainsKey( "Id" ), "Component Id should be removed" );
		Assert.IsTrue( component.ContainsKey( "__guid" ), "Component __guid should exist" );
		Assert.AreEqual( "c3e8f712-542b-41d5-8d82-7207f8065a03", component["__guid"].Deserialize<Guid>().ToString() );

		// 4. Check Child Id conversion (lowercase variant)
		var childObject = gameObject["Children"][0].AsObject();
		Assert.IsFalse( childObject.ContainsKey( "id" ), "Child lowercase id should be removed" );
		Assert.IsTrue( childObject.ContainsKey( "__guid" ), "Child __guid should exist" );
		Assert.AreEqual( "d2f71e9a-4c88-4b3e-94e3-282e171a2e4a", childObject["__guid"].Deserialize<Guid>().ToString() );

		// 5. Verify other properties remain unchanged
		Assert.AreEqual( "Test Scene", sceneJson["Name"].GetValue<string>() );
		Assert.AreEqual( "Root Object", gameObject["Name"].GetValue<string>() );
		Assert.AreEqual( "Child Object", childObject["Name"].GetValue<string>() );
		Assert.AreEqual( "ModelRenderer", component["__type"].GetValue<string>() );
	}
}
