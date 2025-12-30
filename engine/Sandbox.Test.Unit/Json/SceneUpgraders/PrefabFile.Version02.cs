using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonTest.SceneUpgraders;

[TestClass]
public class PrefabFileJsonUpgrader02
{
	[TestMethod]
	public void UpgraderAddsMissingArraysAndFlags()
	{
		// Arrange - Create a prefab file JSON with missing arrays and flags
		string prefabJsonText = """
		{
			"__guid": "a4889b20-837d-4244-83bd-0447c0aeaf71",
			"Name": "Test Prefab",
			"RootObject": {
				"__guid": "b58c23e1-9467-4a50-aeef-3a304aac2726",
				"Name": "Root GameObject",
				"Position": "0,0,0"
			}
		}
		""";

		// Parse the JSON text into a JsonObject
		var prefabJson = JsonNode.Parse( prefabJsonText ).AsObject();

		// Act - Call the upgrader
		PrefabFile.Upgrader_v2( prefabJson );

		// Assert

		// 1. Check that top-level properties were added
		Assert.IsTrue( prefabJson.ContainsKey( "Flags" ), "Prefab file should have Flags property" );
		Assert.AreEqual( 0, prefabJson["Flags"].GetValue<int>(), "Default Flags value should be 0" );
		Assert.IsTrue( prefabJson.ContainsKey( "Children" ), "Prefab file should have Children array" );
		Assert.IsNotNull( prefabJson["Children"].AsArray() );
		Assert.AreEqual( 0, prefabJson["Children"].AsArray().Count, "Top-level Children array should be empty" );
		Assert.IsTrue( prefabJson.ContainsKey( "Components" ), "Prefab file should have Components array" );
		Assert.IsNotNull( prefabJson["Components"].AsArray() );
		Assert.AreEqual( 0, prefabJson["Components"].AsArray().Count, "Top-level Components array should be empty" );

		// 2. Check that RootObject properties were added
		var rootObject = prefabJson["RootObject"].AsObject();
		Assert.IsTrue( rootObject.ContainsKey( "Flags" ), "RootObject should have Flags property" );
		Assert.AreEqual( 0, rootObject["Flags"].GetValue<int>(), "RootObject default Flags value should be 0" );
		Assert.IsTrue( rootObject.ContainsKey( "Children" ), "RootObject should have Children array" );
		Assert.IsNotNull( rootObject["Children"].AsArray() );
		Assert.AreEqual( 0, rootObject["Children"].AsArray().Count, "RootObject Children array should be empty" );
		Assert.IsTrue( rootObject.ContainsKey( "Components" ), "RootObject should have Components array" );
		Assert.IsNotNull( rootObject["Components"].AsArray() );
		Assert.AreEqual( 0, rootObject["Components"].AsArray().Count, "RootObject Components array should be empty" );

		// 3. Verify other properties remain unchanged
		Assert.AreEqual( "a4889b20-837d-4244-83bd-0447c0aeaf71", prefabJson["__guid"].Deserialize<Guid>().ToString() );
		Assert.AreEqual( "Test Prefab", prefabJson["Name"].GetValue<string>() );
		Assert.AreEqual( "b58c23e1-9467-4a50-aeef-3a304aac2726", rootObject["__guid"].Deserialize<Guid>().ToString() );
		Assert.AreEqual( "Root GameObject", rootObject["Name"].GetValue<string>() );
		Assert.AreEqual( "0,0,0", rootObject["Position"].GetValue<string>() );
	}

	[TestMethod]
	public void UpgraderPreservesExistingValuesAndProcessesHierarchy()
	{
		// Arrange - Create a prefab file with some existing values and some missing
		string prefabJsonText = """
		{
			"__guid": "a4889b20-837d-4244-83bd-0447c0aeaf71",
			"Name": "Complex Prefab",
			"Flags": 2,
			"RootObject": {
				"__guid": "b58c23e1-9467-4a50-aeef-3a304aac2726",
				"Name": "Root Object",
				"Components": [
					{
						"__guid": "c3e8f712-542b-41d5-8d82-7207f8065a03",
						"__type": "ModelRenderer"
					}
				],
				"Children": [
					{
						"__guid": "d4f91e23-652a-4b66-9f83-830a06b75b04",
						"Name": "Child With Flags",
						"Flags": 1
					},
					{
						"__guid": "e5a02d34-763b-4c77-a084-941b17c86c15",
						"Name": "Child With Components",
						"Components": [
							{
								"__guid": "f6b13e45-874c-4d88-b195-a52c28d97d26",
								"__type": "PointLight"
							}
						]
					}
				]
			}
		}
		""";

		// Parse the JSON text into a JsonObject
		var prefabJson = JsonNode.Parse( prefabJsonText ).AsObject();

		// Act - Call the upgrader
		PrefabFile.Upgrader_v2( prefabJson );

		// Assert

		// 1. Check that existing values are preserved
		Assert.AreEqual( 2, prefabJson["Flags"].GetValue<int>(), "Existing top-level Flags should be preserved" );

		// 2. Check Root Object
		var rootObject = prefabJson["RootObject"].AsObject();
		Assert.IsTrue( rootObject.ContainsKey( "Flags" ), "RootObject should now have Flags" );
		Assert.AreEqual( 0, rootObject["Flags"].GetValue<int>(), "RootObject should have default Flags value" );
		Assert.AreEqual( 1, rootObject["Components"].AsArray().Count, "RootObject should preserve existing Components" );
		Assert.AreEqual( 2, rootObject["Children"].AsArray().Count, "RootObject should preserve existing Children" );

		// 3. Check first child (had Flags, but missing Components and Children)
		var child1 = rootObject["Children"][0].AsObject();
		Assert.AreEqual( 1, child1["Flags"].GetValue<int>(), "Child1 should preserve existing Flags value" );
		Assert.IsTrue( child1.ContainsKey( "Components" ), "Child1 should now have Components array" );
		Assert.AreEqual( 0, child1["Components"].AsArray().Count, "Child1 Components array should be empty" );
		Assert.IsTrue( child1.ContainsKey( "Children" ), "Child1 should now have Children array" );
		Assert.AreEqual( 0, child1["Children"].AsArray().Count, "Child1 Children array should be empty" );

		// 4. Check second child (had Components, but missing Flags and Children)
		var child2 = rootObject["Children"][1].AsObject();
		Assert.IsTrue( child2.ContainsKey( "Flags" ), "Child2 should now have Flags property" );
		Assert.AreEqual( 0, child2["Flags"].GetValue<int>(), "Child2 should have default Flags value" );
		Assert.AreEqual( 1, child2["Components"].AsArray().Count, "Child2 should preserve existing Components" );
		Assert.IsTrue( child2.ContainsKey( "Children" ), "Child2 should now have Children array" );
		Assert.AreEqual( 0, child2["Children"].AsArray().Count, "Child2 Children array should be empty" );
	}
}
