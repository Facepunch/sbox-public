using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonTest.SceneUpgraders;

[TestClass]
public class PrefabFileJsonUpgrader01
{
	[TestMethod]
	public void UpgraderConvertsIdToGuidInPrefabHierarchy()
	{
		// Arrange - Create a prefab file JSON with "Id" properties at various levels
		string prefabJsonText = """
		{
			"Id": "a4889b20-837d-4244-83bd-0447c0aeaf71",
			"Name": "Test Prefab",
			"RootObject": {
				"Id": "b58c23e1-9467-4a50-aeef-3a304aac2726",
				"Name": "Root GameObject",
				"Position": "0,0,0",
				"Components": [
					{
						"Id": "c3e8f712-542b-41d5-8d82-7207f8065a03",
						"__type": "ModelRenderer",
						"Model": "models/citizen/citizen.vmdl"
					},
					{
						"Id": "d4f91e23-652a-4b66-9f83-830a06b75b04",
						"__type": "BoxCollider",
						"Size": "64,64,72"
					}
				],
				"Children": [
					{
						"Id": "e5a02d34-763b-4c77-a084-941b17c86c15",
						"Name": "Child Object",
						"Position": "0,0,100",
						"Components": [
							{
								"Id": "f6b13e45-874c-4d88-b195-a52c28d97d26",
								"__type": "PointLight",
								"Color": "1,0.5,0,1"
							}
						],
						"Children": []
					}
				]
			}
		}
		""";

		// Parse the JSON text into a JsonObject
		var prefabJson = JsonNode.Parse( prefabJsonText ).AsObject();

		// Act - Call the upgrader
		PrefabFile.Upgrader_v1( prefabJson );

		// Assert

		// 1. Check that top-level Id was converted to __guid
		Assert.IsFalse( prefabJson.ContainsKey( "Id" ), "Prefab file Id should be removed" );
		Assert.IsTrue( prefabJson.ContainsKey( "__guid" ), "Prefab file __guid should exist" );
		Assert.AreEqual( "a4889b20-837d-4244-83bd-0447c0aeaf71", prefabJson["__guid"].Deserialize<Guid>().ToString() );

		// 2. Check that RootObject Id was converted
		var rootObject = prefabJson["RootObject"].AsObject();
		Assert.IsFalse( rootObject.ContainsKey( "Id" ), "RootObject Id should be removed" );
		Assert.IsTrue( rootObject.ContainsKey( "__guid" ), "RootObject __guid should exist" );
		Assert.AreEqual( "b58c23e1-9467-4a50-aeef-3a304aac2726", rootObject["__guid"].Deserialize<Guid>().ToString() );

		// 3. Check that Component Ids were converted
		var components = rootObject["Components"].AsArray();
		var component1 = components[0].AsObject();
		Assert.IsFalse( component1.ContainsKey( "Id" ), "Component Id should be removed" );
		Assert.IsTrue( component1.ContainsKey( "__guid" ), "Component __guid should exist" );
		Assert.AreEqual( "c3e8f712-542b-41d5-8d82-7207f8065a03", component1["__guid"].Deserialize<Guid>().ToString() );

		var component2 = components[1].AsObject();
		Assert.IsFalse( component2.ContainsKey( "Id" ), "Component Id should be removed" );
		Assert.IsTrue( component2.ContainsKey( "__guid" ), "Component __guid should exist" );
		Assert.AreEqual( "d4f91e23-652a-4b66-9f83-830a06b75b04", component2["__guid"].Deserialize<Guid>().ToString() );

		// 4. Check that Child object Id was converted
		var childObject = rootObject["Children"][0].AsObject();
		Assert.IsFalse( childObject.ContainsKey( "Id" ), "Child object Id should be removed" );
		Assert.IsTrue( childObject.ContainsKey( "__guid" ), "Child object __guid should exist" );
		Assert.AreEqual( "e5a02d34-763b-4c77-a084-941b17c86c15", childObject["__guid"].Deserialize<Guid>().ToString() );

		// 5. Check that nested Component Id was converted
		var nestedComponent = childObject["Components"][0].AsObject();
		Assert.IsFalse( nestedComponent.ContainsKey( "Id" ), "Nested component Id should be removed" );
		Assert.IsTrue( nestedComponent.ContainsKey( "__guid" ), "Nested component __guid should exist" );
		Assert.AreEqual( "f6b13e45-874c-4d88-b195-a52c28d97d26", nestedComponent["__guid"].Deserialize<Guid>().ToString() );

		// 6. Verify other properties remain unchanged
		Assert.AreEqual( "Test Prefab", prefabJson["Name"].GetValue<string>() );
		Assert.AreEqual( "Root GameObject", rootObject["Name"].GetValue<string>() );
		Assert.AreEqual( "0,0,0", rootObject["Position"].GetValue<string>() );
		Assert.AreEqual( "ModelRenderer", component1["__type"].GetValue<string>() );
		Assert.AreEqual( "models/citizen/citizen.vmdl", component1["Model"].GetValue<string>() );
		Assert.AreEqual( "BoxCollider", component2["__type"].GetValue<string>() );
		Assert.AreEqual( "Child Object", childObject["Name"].GetValue<string>() );
		Assert.AreEqual( "PointLight", nestedComponent["__type"].GetValue<string>() );
		Assert.AreEqual( "1,0.5,0,1", nestedComponent["Color"].GetValue<string>() );
	}
}
