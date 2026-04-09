using Sandbox;
using System.Text.Json.Nodes;

namespace Prefab;

/// <summary>
/// Tests for graceful handling of prefab instances whose source file has been deleted.
/// </summary>
[TestClass]
public class MissingPrefabTests
{
	// A scene that holds a single prefab instance referencing a non-existent file.
	// The patch contains a name override so we can verify round-trip preservation.
	private static string SceneWithMissingPrefab( string missingPath ) => $@"
	{{
		""__guid"": ""11111111-0000-4000-8000-000000000001"",
		""Name"": ""Root"",
		""Position"": ""0,0,0"",
		""Enabled"": true,
		""Components"": [],
		""Children"": [
			{{
				""__guid"": ""22222222-0000-4000-8000-000000000002"",
				""__version"": 2,
				""__Prefab"": ""{missingPath}"",
				""__PrefabInstancePatch"": {{
					""AddedObjects"": [],
					""RemovedObjects"": [],
					""PropertyOverrides"": [
						{{
							""Target"": {{ ""Type"": ""GameObject"", ""IdValue"": ""fab370f8-2e2c-48cf-a523-e4be49723490"" }},
							""Property"": ""Name"",
							""Value"": ""MyOverriddenName""
						}}
					],
					""MovedObjects"": []
				}},
				""__PrefabIdToInstanceId"": {{
					""fab370f8-2e2c-48cf-a523-e4be49723490"": ""22222222-0000-4000-8000-000000000002""
				}}
			}}
		]
	}}";

	[TestMethod]
	public void MissingPrefab_LoadsAsDisabledStub()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		root.Deserialize( Json.ParseToJsonObject( SceneWithMissingPrefab( "does_not_exist.prefab" ) ) );

		// Should have exactly one child — the stub
		Assert.AreEqual( 1, root.Children.Count );
		var stub = root.Children[0];

		// Stub must be disabled and flagged as Error
		Assert.IsFalse( stub.Enabled );
		Assert.IsTrue( stub.Flags.Contains( GameObjectFlags.Error ) );
	}

	[TestMethod]
	public void MissingPrefab_StubRetainsPrefabSource()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		const string missingPath = "gone_forever.prefab";

		var root = scene.CreateObject();
		root.Deserialize( Json.ParseToJsonObject( SceneWithMissingPrefab( missingPath ) ) );

		var stub = root.Children[0];

		// PrefabInstanceSource must still point to the original path
		Assert.AreEqual( missingPath, stub.PrefabInstanceSource );
	}

	[TestMethod]
	public void MissingPrefab_StubNameContainsPrefabPath()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		const string missingPath = "somewhere/my_prefab.prefab";

		var root = scene.CreateObject();
		root.Deserialize( Json.ParseToJsonObject( SceneWithMissingPrefab( missingPath ) ) );

		var stub = root.Children[0];

		// Name should communicate what is missing so the user can see it in the hierarchy
		Assert.IsTrue( stub.Name.Contains( missingPath ) );
	}

	[TestMethod]
	public void MissingPrefab_SerializesBackToOriginalFormat()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		const string missingPath = "roundtrip.prefab";

		var root = scene.CreateObject();
		root.Deserialize( Json.ParseToJsonObject( SceneWithMissingPrefab( missingPath ) ) );

		var stub = root.Children[0];

		// Re-serializing must produce a node with the original prefab keys intact
		var serialized = stub.Serialize();

		Assert.IsNotNull( serialized );
		Assert.IsNotNull( serialized[GameObject.JsonKeys.PrefabInstanceSource] );
		Assert.IsNotNull( serialized[GameObject.JsonKeys.PrefabInstancePatch] );
		Assert.AreEqual( missingPath, (string)serialized[GameObject.JsonKeys.PrefabInstanceSource] );
	}

	[TestMethod]
	public void MissingPrefab_PatchDataPreservedAfterRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		const string missingPath = "patch_preserved.prefab";

		var root = scene.CreateObject();
		root.Deserialize( Json.ParseToJsonObject( SceneWithMissingPrefab( missingPath ) ) );

		var stub = root.Children[0];
		var serialized = stub.Serialize();

		// The patch stored in the original JSON (containing the name override) must survive
		var patchNode = serialized[GameObject.JsonKeys.PrefabInstancePatch] as JsonObject;
		Assert.IsNotNull( patchNode );

		var overrides = patchNode["PropertyOverrides"] as JsonArray;
		Assert.IsNotNull( overrides );
		Assert.AreEqual( 1, overrides.Count );
	}

	[TestMethod]
	public void MissingPrefab_RefreshPatchDoesNotThrow()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		root.Deserialize( Json.ParseToJsonObject( SceneWithMissingPrefab( "still_missing.prefab" ) ) );

		var stub = root.Children[0];

		// This is the call that previously crashed via Assert.IsValid — must not throw
		stub.PrefabInstance.RefreshPatch();
	}
}
