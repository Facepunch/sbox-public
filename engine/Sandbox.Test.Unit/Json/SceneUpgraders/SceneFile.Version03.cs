using System.Text.Json.Nodes;

namespace JsonTest.SceneUpgraders;

[TestClass]
public class SceneFileJsonUpgrader03
{
	[TestMethod]
	public void UpgraderAddsMissingArraysAndFlags()
	{
		// Arrange - Create a scene file JSON with missing arrays and flags
		string sceneJsonText = """
		{
			"__guid": "a4889b20-837d-4244-83bd-0447c0aeaf71",
			"Name": "Test Scene v3",
			"GameObjects": [
				{
					"__guid": "b58c23e1-9467-4a50-aeef-3a304aac2726",
					"Name": "Object Missing Everything"
				},
				{
					"__guid": "c6d92f14-8e37-42a1-b5d3-9f21c8a8f982",
					"Name": "Object With Some Properties",
					"Flags": 2,
					"Children": [
						{
							"__guid": "d7e03a25-9f48-43b2-a6e4-0a32d9b9fa73",
							"Name": "Nested Child Missing Flags"
						}
					]
				},
				{
					"__guid": "e8f14b36-a059-44c3-b7f5-1b43ea0abc64",
					"Name": "Object With Components",
					"Components": [
						{
							"__guid": "f9025c47-b16a-45d4-c8g6-2c54fb1bcd75",
							"__type": "ModelRenderer"
						}
					]
				}
			]
		}
		""";

		// Parse the JSON text into a JsonObject
		var sceneJson = JsonNode.Parse( sceneJsonText ).AsObject();

		// Act - Call the upgrader
		SceneFile.Upgrader_v3( sceneJson );

		// Assert

		// 1. Check that root level has Children and Components arrays
		Assert.IsTrue( sceneJson.ContainsKey( "Children" ), "Root should have Children array" );
		Assert.IsTrue( sceneJson.ContainsKey( "Components" ), "Root should have Components array" );
		Assert.IsTrue( sceneJson.ContainsKey( "Flags" ), "Root should have Flags property" );
		Assert.AreEqual( 0, sceneJson["Flags"].GetValue<int>(), "Default Flags value should be 0" );

		var gameObjects = sceneJson["GameObjects"].AsArray();

		// 2. Check first GameObject (was missing everything)
		var object1 = gameObjects[0].AsObject();
		Assert.IsTrue( object1.ContainsKey( "Flags" ), "Object1 should have Flags property" );
		Assert.AreEqual( 0, object1["Flags"].GetValue<int>(), "Object1 default Flags value should be 0" );

		Assert.IsTrue( object1.ContainsKey( "Children" ), "Object1 should have Children array" );
		Assert.IsNotNull( object1["Children"].AsArray() );
		Assert.AreEqual( 0, object1["Children"].AsArray().Count, "Object1 Children array should be empty" );

		Assert.IsTrue( object1.ContainsKey( "Components" ), "Object1 should have Components array" );
		Assert.IsNotNull( object1["Components"].AsArray() );
		Assert.AreEqual( 0, object1["Components"].AsArray().Count, "Object1 Components array should be empty" );

		// 3. Check second GameObject (had Flags and Children, but not Components)
		var object2 = gameObjects[1].AsObject();
		Assert.IsTrue( object2.ContainsKey( "Flags" ), "Object2 should have Flags property" );
		Assert.AreEqual( 2, object2["Flags"].GetValue<int>(), "Object2 Flags value should be preserved" );

		Assert.IsTrue( object2.ContainsKey( "Components" ), "Object2 should have Components array" );
		Assert.IsNotNull( object2["Components"].AsArray() );
		Assert.AreEqual( 0, object2["Components"].AsArray().Count, "Object2 Components array should be empty" );

		// 4. Check nested child in second GameObject
		var nestedChild = object2["Children"][0].AsObject();
		Assert.IsTrue( nestedChild.ContainsKey( "Flags" ), "Nested child should have Flags property" );
		Assert.AreEqual( 0, nestedChild["Flags"].GetValue<int>(), "Nested child default Flags value should be 0" );

		Assert.IsTrue( nestedChild.ContainsKey( "Components" ), "Nested child should have Components array" );
		Assert.IsNotNull( nestedChild["Components"].AsArray() );
		Assert.AreEqual( 0, nestedChild["Components"].AsArray().Count, "Nested child Components array should be empty" );

		Assert.IsTrue( nestedChild.ContainsKey( "Children" ), "Nested child should have Children array" );
		Assert.IsNotNull( nestedChild["Children"].AsArray() );
		Assert.AreEqual( 0, nestedChild["Children"].AsArray().Count, "Nested child Children array should be empty" );

		// 5. Check third GameObject (had Components, but not Children and Flags)
		var object3 = gameObjects[2].AsObject();
		Assert.IsTrue( object3.ContainsKey( "Flags" ), "Object3 should have Flags property" );
		Assert.AreEqual( 0, object3["Flags"].GetValue<int>(), "Object3 default Flags value should be 0" );

		Assert.IsTrue( object3.ContainsKey( "Children" ), "Object3 should have Children array" );
		Assert.IsNotNull( object3["Children"].AsArray() );
		Assert.AreEqual( 0, object3["Children"].AsArray().Count, "Object3 Children array should be empty" );

		Assert.IsTrue( object3.ContainsKey( "Components" ), "Object3 should have Components array" );
		Assert.IsNotNull( object3["Components"].AsArray() );
		Assert.AreEqual( 1, object3["Components"].AsArray().Count, "Object3 Components array should have one item" );
		Assert.AreEqual( "ModelRenderer", object3["Components"][0]["__type"].GetValue<string>(), "Original component should be preserved" );
	}
}
