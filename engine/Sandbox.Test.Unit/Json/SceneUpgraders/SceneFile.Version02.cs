using System.Text.Json.Nodes;

namespace JsonTest.SceneUpgraders;

[TestClass]
public class SceneFileJsonUpgrader02
{
	[TestMethod]
	public void UpgraderMovesTitleAndDescriptionToMetadata()
	{
		// Arrange - Create a scene file JSON with Title and Description at root level
		string sceneJsonText = """
		{
			"__guid": "a4889b20-837d-4244-83bd-0447c0aeaf71",
			"Name": "Test Scene",
			"Title": "My Awesome Scene",
			"Description": "This is a test scene with a title and description",
			"SceneProperties": {
				"Gravity": "0,0,-800"
			},
			"GameObjects": [
				{
					"__guid": "b58c23e1-9467-4a50-aeef-3a304aac2726",
					"Name": "Existing Object",
					"Position": "0,0,0",
					"Components": []
				}
			]
		}
		""";

		// Parse the JSON text into a JsonObject
		var sceneJson = JsonNode.Parse( sceneJsonText ).AsObject();

		// Act - Call the upgrader
		SceneFile.Upgrader_v2( sceneJson );

		// Assert

		// 1. Check that Title and Description values were preserved in the SceneProperties.Metadata
		var sceneProperties = sceneJson["SceneProperties"].AsObject();
		Assert.IsTrue( sceneProperties.ContainsKey( "Metadata" ), "SceneProperties.Metadata should exist" );

		var metadata = sceneProperties["Metadata"].AsObject();
		Assert.AreEqual( "My Awesome Scene", metadata["Title"].GetValue<string>() );
		Assert.AreEqual( "This is a test scene with a title and description", metadata["Description"].GetValue<string>() );

		// 2. Check that a new GameObject with SceneInformation component was added
		var gameObjects = sceneJson["GameObjects"].AsArray();
		Assert.AreEqual( 2, gameObjects.Count, "Should have one additional GameObject" );

		// The new GameObject should be at index 0 (inserted at the start)
		var infoObject = gameObjects[0].AsObject();
		Assert.AreEqual( "Scene Information", infoObject["Name"].GetValue<string>() );
		Assert.IsTrue( infoObject["Enabled"].GetValue<bool>() );

		// 3. Check the SceneInformation component has the title and description
		var components = infoObject["Components"].AsArray();
		Assert.AreEqual( 1, components.Count, "Should have one component" );

		var sceneInfoComponent = components[0].AsObject();
		Assert.AreEqual( "SceneInformation", sceneInfoComponent["__type"].GetValue<string>() );
		Assert.AreEqual( "My Awesome Scene", sceneInfoComponent["Title"].GetValue<string>() );
		Assert.AreEqual( "This is a test scene with a title and description", sceneInfoComponent["Description"].GetValue<string>() );

		// 4. Original GameObject should still exist
		var originalObject = gameObjects[1].AsObject();
		Assert.AreEqual( "Existing Object", originalObject["Name"].GetValue<string>() );
		Assert.AreEqual( "b58c23e1-9467-4a50-aeef-3a304aac2726", originalObject["__guid"].ToString() );
	}
}
