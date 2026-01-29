using Sandbox;

namespace Prefab;

[TestClass]
/// <summary>
/// Tests for loading prefabs, focusing on edge cases like missing prefab references.
/// </summary>
public class InstanceLoadTests
{
	[TestMethod]
	/// <summary>
	/// https://github.com/Facepunch/sbox-public/issues/758
	/// https://github.com/Facepunch/sbox-public/issues/779
	/// </summary>
	public void RepeatedAttemptToLoadMissingPrefab_ShouldNotThrowException()
	{
		var missingPrefabPath = "window0003.prefab";
		var outerPrefabPath = "___outer_with_missing_prefab.prefab";
		var nestedPrefabPath = "___nested_with_missing_prefab.prefab";

		// First create a prefab that references a missing prefab
		using var nestedPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( nestedPrefabPath,
			CreatePrefabSourceWithMissingReference( missingPrefabPath ) );

		// Then create an outer prefab that includes both the nested prefab AND another reference 
		// to the same missing prefab - this creates the double-reference scenario
		using var outerPrefab = Sandbox.SceneTests.Helpers.RegisterPrefabFromJson( outerPrefabPath,
			CreateOuterPrefabSourceWithMultipleMissingReferences( missingPrefabPath, nestedPrefabPath ) );

		// Create a regular scene
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Attempt to load the outer prefab, which will trigger attempts to load the missing prefab
		// This should not throw an exception, even though the missing prefab is referenced twice
		var outerPrefabFile = ResourceLibrary.Get<PrefabFile>( outerPrefabPath );
		var outerPrefabScene = SceneUtility.GetPrefabScene( outerPrefabFile );
		var instance = outerPrefabScene.Clone( Vector3.Zero );

		// Verify basic structure - we should have at least one child that loaded successfully
		Assert.IsTrue( instance.Children.Count >= 1 );
		Assert.IsNotNull( instance.Components.Get<ModelRenderer>() );

		// Should pass without exceptions
	}

	private string CreatePrefabSourceWithMissingReference( string missingPrefabPath )
	{
		return $@"
        {{
            ""__guid"": ""dcba9876-fedc-4cba-9876-fedcba987654"",
            ""Name"": ""NestedPrefab"",
            ""Position"": ""0,0,0"",
            ""Enabled"": true,
            ""Components"": [
                {{
                    ""__type"": ""ModelRenderer"",
                    ""__guid"": ""98765432-1098-4765-4321-098765432109"",
                    ""BodyGroups"": 18446744073709551615,
                    ""MaterialGroup"": null,
                    ""MaterialOverride"": null,
                    ""Model"": null,
                    ""RenderType"": ""On"",
                    ""Tint"": ""1,0,1,1""
                }}
            ],
            ""Children"": [
                {{
                    ""__guid"": ""aaaaaaaa-bbbb-4ccc-dddd-eeeeeeeeeeee"",
                    ""__version"": 1,
                    ""__Prefab"": ""{missingPrefabPath}"",
                    ""__PrefabInstancePatch"": {{
                        ""AddedObjects"": [],
                        ""RemovedObjects"": [],
                        ""PropertyOverrides"": [],
                        ""MovedObjects"": []
                    }},
                    ""__PrefabIdToInstanceId"": {{}}
                }}
            ]
        }}";
	}

	private string CreateOuterPrefabSourceWithMultipleMissingReferences( string missingPrefabPath, string nestedPrefabPath )
	{
		return $@"
        {{
            ""__guid"": ""abcdef12-3456-4789-abcd-ef1234567890"",
            ""Name"": ""OuterPrefab"",
            ""Position"": ""0,0,0"",
            ""Enabled"": true,
            ""Components"": [
                {{
                    ""__type"": ""ModelRenderer"",
                    ""__guid"": ""12345678-9012-4345-6789-012345678901"",
                    ""BodyGroups"": 18446744073709551615,
                    ""MaterialGroup"": null,
                    ""MaterialOverride"": null,
                    ""Model"": null,
                    ""RenderType"": ""On"",
                    ""Tint"": ""0,1,0,1""
                }}
            ],
            ""Children"": [
                {{
                    ""__guid"": ""22222222-3333-4444-5555-666666666666"",
                    ""__version"": 1,
                    ""__Prefab"": ""{nestedPrefabPath}"",
                    ""__PrefabInstancePatch"": {{
                        ""AddedObjects"": [],
                        ""RemovedObjects"": [],
                        ""PropertyOverrides"": [],
                        ""MovedObjects"": []
                    }},
                    ""__PrefabIdToInstanceId"": {{}}
                }},
                {{
                    ""__guid"": ""77777777-8888-4999-aaaa-bbbbbbbbbbbb"",
                    ""__version"": 1,
                    ""__Prefab"": ""{missingPrefabPath}"",
                    ""__PrefabInstancePatch"": {{
                        ""AddedObjects"": [],
                        ""RemovedObjects"": [],
                        ""PropertyOverrides"": [],
                        ""MovedObjects"": []
                    }},
                    ""__PrefabIdToInstanceId"": {{}}
                }}
            ]
        }}";
	}
}
