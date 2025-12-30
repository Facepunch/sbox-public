namespace Prefab;

public partial class Instances
{
	public static GameObject PrefabWithNetworkedChildren => Prefabs.GetPrefab( "PrefabWithNetworkedChildren.prefab", _prefabWithNetworkedChildrenSrc );

	static readonly string _prefabWithNetworkedChildrenSrc = """
          {
            "RootObject": {
              "__guid": "7b065572-4040-49bb-99e4-c231dd4eff7e",
              "__version": 1,
              "Flags": 0,
              "Name": "GameObjectPrefab",
              "Position": "0,0,0",
              "Rotation": "0,0,0,1",
              "Scale": "1,1,1",
              "Tags": "",
              "Enabled": true,
              "NetworkMode": 2,
              "NetworkInterpolation": true,
              "NetworkOrphaned": 0,
              "OwnerTransfer": 1,
              "Components": [],
              "Children": [
                {
                  "__guid": "528bc267-8e9b-4f56-a1eb-a1668c4f86f5",
                  "__version": 1,
                  "Flags": 0,
                  "Name": "ChildNetworkedGameObject",
                  "Position": "0,0,0",
                  "Rotation": "0,0,0,1",
                  "Scale": "1,1,1",
                  "Tags": "",
                  "Enabled": true,
                  "NetworkMode": 1,
                  "NetworkInterpolation": true,
                  "NetworkOrphaned": 0,
                  "OwnerTransfer": 1,
                  "Components": [],
                  "Children": [
                    {
                      "__guid": "94f363e2-d1d8-405e-897f-03ebe5ec207f",
                      "__version": 1,
                      "Flags": 0,
                      "Name": "2ndChildNetworkedGameObject",
                      "Position": "0,0,0",
                      "Rotation": "0,0,0,1",
                      "Scale": "1,1,1",
                      "Tags": "",
                      "Enabled": true,
                      "NetworkMode": 1,
                      "NetworkInterpolation": true,
                      "NetworkOrphaned": 0,
                      "OwnerTransfer": 1,
                      "Components": [],
                      "Children": []
                    }
                  ]
                }
              ]
            },
            "ShowInMenu": false,
            "MenuPath": null,
            "MenuIcon": null,
            "__references": []
          }

          """;

	public static GameObject NetworkedPrefabWithNetworkedChildren => Prefabs.GetPrefab( "NetworkedPrefabWithNetworkedChildren.prefab", _networkedPrefabWithNetworkedChildrenSrc );

	static readonly string _networkedPrefabWithNetworkedChildrenSrc = """
          {
            "RootObject": {
              "__guid": "d6a32664-76f6-4ee8-a781-cd4164517c63",
              "__version": 1,
              "Flags": 0,
              "Name": "GameObjectPrefab",
              "Position": "0,0,0",
              "Rotation": "0,0,0,1",
              "Scale": "1,1,1",
              "Tags": "",
              "Enabled": true,
              "NetworkMode": 1,
              "NetworkInterpolation": true,
              "NetworkOrphaned": 0,
              "OwnerTransfer": 1,
              "Components": [],
              "Children": [
                {
                  "__guid": "163f12c3-1e78-423c-bbaf-73d10aacaea6",
                  "__version": 1,
                  "Flags": 0,
                  "Name": "ChildNetworkedGameObject",
                  "Position": "0,0,0",
                  "Rotation": "0,0,0,1",
                  "Scale": "1,1,1",
                  "Tags": "",
                  "Enabled": true,
                  "NetworkMode": 1,
                  "NetworkInterpolation": true,
                  "NetworkOrphaned": 0,
                  "OwnerTransfer": 1,
                  "Components": [],
                  "Children": [
                    {
                      "__guid": "fa1c7df9-a0b4-434a-a558-85973381411c",
                      "__version": 1,
                      "Flags": 0,
                      "Name": "2ndChildNetworkedGameObject",
                      "Position": "0,0,0",
                      "Rotation": "0,0,0,1",
                      "Scale": "1,1,1",
                      "Tags": "",
                      "Enabled": true,
                      "NetworkMode": 1,
                      "NetworkInterpolation": true,
                      "NetworkOrphaned": 0,
                      "OwnerTransfer": 1,
                      "Components": [],
                      "Children": []
                    }
                  ]
                }
              ]
            },
            "ShowInMenu": false,
            "MenuPath": null,
            "MenuIcon": null,
            "__references": []
          }

          """;

	[TestMethod]
	public void TestSerializePrefabInstanceSingleNetworkObject()
	{
		var scene1 = new Scene();
		using var sceneScope1 = scene1.Push();

		var options = new GameObject.SerializeOptions
		{
			SingleNetworkObject = true
		};

		var instance1 = PrefabWithNetworkedChildren.Clone();
		var serialized1 = instance1.Serialize( options );

		var deserializedGo1 = new GameObject();
		deserializedGo1.Deserialize( serialized1 );

		var scene2 = new Scene();
		using var sceneScope2 = scene2.Push();

		Assert.AreEqual( 0, deserializedGo1.Children.Count );

		var instance2 = NetworkedPrefabWithNetworkedChildren.Clone();

		var serialized2 = instance2.Serialize( options );
		var deserializedGo2 = new GameObject();
		deserializedGo2.Deserialize( serialized2 );

		Assert.AreEqual( 0, deserializedGo2.Children.Count );
	}

	[TestMethod]
	public void TestSerializePrefabInstanceSceneForNetwork()
	{
		var scene1 = new Scene();

		using ( scene1.Push() )
		{
			PrefabWithNetworkedChildren.Clone();
			NetworkedPrefabWithNetworkedChildren.Clone();
		}

		var options = new GameObject.SerializeOptions
		{
			SceneForNetwork = true
		};

		var serializedScene = scene1.Serialize( options );

		var deserializedScene = new Scene();
		deserializedScene.Deserialize( serializedScene );

		Assert.AreEqual( 1, deserializedScene.Children.Count );
		Assert.AreEqual( 0, deserializedScene.Children[0].Children.Count );
	}
}
