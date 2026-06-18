using Sandbox;
using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace SceneTests.Prefab;

/// <summary>
/// An object missing from a consumer's persisted __PrefabIdToInstanceId (e.g. added to an inner prefab
/// after the outer was saved) must keep a stable guid across prefab cache rebuilds, otherwise scene
/// references to it break and patch overrides targeting it are dropped.
/// </summary>
[TestClass]
public class GuidStability
{
	const string InnerPrefabPath = "___guid_stability_c.prefab";
	const string MiddlePrefabPath = "___guid_stability_b.prefab";
	const string OuterPrefabPath = "___guid_stability_a.prefab";

	// B contains an instance of C, but B's stored mapping omits C's child "X" (as if X was added later).
	// Rebuilding B's cache scene must give X the same guid each time.
	[TestMethod]
	public void CacheRebuild_MintsStableGuids_ForObjectsMissingFromStoredMapping()
	{
		using var innerPrefab = SceneTests.Helpers.RegisterPrefabFromJson( InnerPrefabPath, _innerPrefabSource );
		using var middlePrefab = SceneTests.Helpers.RegisterPrefabFromJson( MiddlePrefabPath, _middlePrefabSource );

		var middleFile = ResourceLibrary.Get<PrefabFile>( MiddlePrefabPath );
		var middleScene = (PrefabCacheScene)SceneUtility.GetPrefabScene( middleFile );

		var x = middleScene.GetAllObjects( false ).FirstOrDefault( go => go.Name == "X" );
		Assert.IsNotNull( x, "Expanded inner prefab object 'X' should exist in the cache scene" );

		var gameObjectId = x.Id;
		var componentId = x.Components.Get<ModelRenderer>().Id;

		// Simulate an editor restart / inner prefab edit cascade.
		middleScene.Load( middleFile );

		x = middleScene.GetAllObjects( false ).FirstOrDefault( go => go.Name == "X" );
		Assert.IsNotNull( x, "Expanded inner prefab object 'X' should exist in the cache scene after a rebuild" );

		Assert.AreEqual( gameObjectId, x.Id,
			"Guid minted for an unmapped inner prefab GameObject must be stable across cache rebuilds" );
		Assert.AreEqual( componentId, x.Components.Get<ModelRenderer>().Id,
			"Guid minted for an unmapped inner prefab Component must be stable across cache rebuilds" );
	}

	// A scene component references X deep inside scene -> A -> B -> C; it must survive a save/load with
	// prefab cache rebuilds in between.
	[TestMethod]
	public void SceneReference_IntoNestedInstance_SurvivesCacheRebuildAndSceneRoundTrip()
	{
		using var innerPrefab = SceneTests.Helpers.RegisterPrefabFromJson( InnerPrefabPath, _innerPrefabSource );
		using var middlePrefab = SceneTests.Helpers.RegisterPrefabFromJson( MiddlePrefabPath, _middlePrefabSource );
		using var outerPrefab = SceneTests.Helpers.RegisterPrefabFromJson( OuterPrefabPath, _outerPrefabSource );

		var outerFile = ResourceLibrary.Get<PrefabFile>( OuterPrefabPath );
		var outerScene = SceneUtility.GetPrefabScene( outerFile );

		JsonObject instanceJson;
		JsonObject referencerJson;
		Guid xId;

		{
			var scene = new Scene();
			using var sceneScope = scene.Push();

			var instance = outerScene.Clone( Vector3.Zero );

			var x = scene.GetAllObjects( false ).FirstOrDefault( go => go.Name == "X" );
			Assert.IsNotNull( x, "Deeply nested prefab object 'X' should exist in the scene instance" );
			xId = x.Id;

			var referencer = scene.CreateObject();
			referencer.Name = "Referencer";

			var line = referencer.AddComponent<LineRenderer>();
			line.Points = new() { x };

			// "Save the scene"
			instanceJson = instance.Serialize();
			referencerJson = referencer.Serialize();
		}

		// Simulate an editor restart: rebuild every prefab cache scene, inner to outer.
		foreach ( var path in new[] { InnerPrefabPath, MiddlePrefabPath, OuterPrefabPath } )
		{
			var file = ResourceLibrary.Get<PrefabFile>( path );
			((PrefabCacheScene)SceneUtility.GetPrefabScene( file )).Load( file );
		}

		// "Load the scene"
		var newScene = new Scene();
		using var newSceneScope = newScene.Push();

		var newInstance = newScene.CreateObject();
		newInstance.Deserialize( instanceJson );

		var newReferencer = newScene.CreateObject();
		newReferencer.Deserialize( referencerJson );

		var target = newReferencer.Components.Get<LineRenderer>().Points?.FirstOrDefault();

		Assert.IsTrue( target.IsValid(),
			"Scene reference into the nested prefab instance should still resolve after a prefab cache rebuild" );
		Assert.AreEqual( "X", target.Name );
		Assert.AreEqual( xId, target.Id, "The referenced object's guid changed across a save/load cycle" );
	}

	// The derivation is a compatibility contract: deterministic, and distinct per (seed, prefab guid).
	[TestMethod]
	public void DeriveInstanceGuid_IsDeterministicAndDistinct()
	{
		var seedA = Guid.Parse( "11111111-1111-4111-8111-111111111111" );
		var seedB = Guid.Parse( "22222222-2222-4222-8222-222222222222" );
		var prefabGuidA = Guid.Parse( "33333333-3333-4333-8333-333333333333" );
		var prefabGuidB = Guid.Parse( "44444444-4444-4444-8444-444444444444" );

		var derived = PrefabInstanceData.DeriveInstanceGuid( seedA, prefabGuidA );

		Assert.AreNotEqual( Guid.Empty, derived );
		Assert.AreEqual( derived, PrefabInstanceData.DeriveInstanceGuid( seedA, prefabGuidA ) );
		Assert.AreNotEqual( derived, PrefabInstanceData.DeriveInstanceGuid( seedB, prefabGuidA ) );
		Assert.AreNotEqual( derived, PrefabInstanceData.DeriveInstanceGuid( seedA, prefabGuidB ) );
	}

	// Two sibling instances of the same prefab, both missing the same inner object, must derive distinct
	// guids for it - no directory collisions.
	[TestMethod]
	public void DuplicateInstances_DeriveDistinctGuids_ForUnmappedObjects()
	{
		using var innerPrefab = SceneTests.Helpers.RegisterPrefabFromJson( InnerPrefabPath, _innerPrefabSource );
		using var middlePrefab = SceneTests.Helpers.RegisterPrefabFromJson( MiddlePrefabPath, _middlePrefabSource );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var holder = scene.CreateObject();
		holder.Deserialize( Json.ParseToJsonObject( _holderWithTwoMiddleInstances ) );

		var xs = scene.GetAllObjects( false ).Where( go => go.Name == "X" ).ToArray();

		Assert.AreEqual( 2, xs.Length, "Both instances should contain the expanded inner prefab object 'X'" );
		Assert.AreNotEqual( xs[0].Id, xs[1].Id, "Unmapped objects in duplicate instances must not share derived guids" );
		Assert.AreNotEqual( xs[0].Components.Get<ModelRenderer>().Id, xs[1].Components.Get<ModelRenderer>().Id );
	}

	// Two collapsed instances of B whose stored mappings only cover B's root; everything else is derived.
	static readonly string _holderWithTwoMiddleInstances = """"
	{
		"__guid": "0d000000-0000-4000-8000-000000000001",
		"Name": "Holder",
		"Enabled": true,
		"Components": [],
		"Children": [
			{
				"__guid": "0d000000-0000-4000-8000-0000000000b1",
				"__version": 1,
				"__Prefab": "___guid_stability_b.prefab",
				"__PrefabInstancePatch": {
					"AddedObjects": [],
					"RemovedObjects": [],
					"PropertyOverrides": [],
					"MovedObjects": []
				},
				"__PrefabIdToInstanceId": {
					"0b000000-0000-4000-8000-000000000001": "0d000000-0000-4000-8000-0000000000b1"
				}
			},
			{
				"__guid": "0d000000-0000-4000-8000-0000000000b2",
				"__version": 1,
				"__Prefab": "___guid_stability_b.prefab",
				"__PrefabInstancePatch": {
					"AddedObjects": [],
					"RemovedObjects": [],
					"PropertyOverrides": [],
					"MovedObjects": []
				},
				"__PrefabIdToInstanceId": {
					"0b000000-0000-4000-8000-000000000001": "0d000000-0000-4000-8000-0000000000b2"
				}
			}
		]
	}
	"""";

	// C - innermost prefab, with child "X" and a component.
	static readonly string _innerPrefabSource = """"
	{
		"__guid": "0c000000-0000-4000-8000-000000000001",
		"Name": "Object",
		"Enabled": true,
		"Components": [],
		"Children": [
			{
				"__guid": "0c000000-0000-4000-8000-00000000000a",
				"Name": "X",
				"Enabled": true,
				"Components": [
					{
						"__type": "ModelRenderer",
						"__guid": "0c000000-0000-4000-8000-00000000000b",
						"Tint": "1,0,0,1"
					}
				],
				"Children": []
			}
		]
	}
	"""";

	// B - a collapsed instance of C whose stored mapping covers only C's root (X was added to C later).
	static readonly string _middlePrefabSource = """"
	{
		"__guid": "0b000000-0000-4000-8000-000000000001",
		"Name": "Object",
		"Enabled": true,
		"Components": [],
		"Children": [
			{
				"__guid": "0b000000-0000-4000-8000-0000000000c0",
				"__version": 1,
				"__Prefab": "___guid_stability_c.prefab",
				"__PrefabInstancePatch": {
					"AddedObjects": [],
					"RemovedObjects": [],
					"PropertyOverrides": [],
					"MovedObjects": []
				},
				"__PrefabIdToInstanceId": {
					"0c000000-0000-4000-8000-000000000001": "0b000000-0000-4000-8000-0000000000c0"
				}
			}
		]
	}
	"""";

	// A - a collapsed instance of B covering B's root and the C-instance root, but nothing inside C.
	static readonly string _outerPrefabSource = """"
	{
		"__guid": "0a000000-0000-4000-8000-000000000001",
		"Name": "Object",
		"Enabled": true,
		"Components": [],
		"Children": [
			{
				"__guid": "0a000000-0000-4000-8000-0000000000b0",
				"__version": 1,
				"__Prefab": "___guid_stability_b.prefab",
				"__PrefabInstancePatch": {
					"AddedObjects": [],
					"RemovedObjects": [],
					"PropertyOverrides": [],
					"MovedObjects": []
				},
				"__PrefabIdToInstanceId": {
					"0b000000-0000-4000-8000-000000000001": "0a000000-0000-4000-8000-0000000000b0",
					"0b000000-0000-4000-8000-0000000000c0": "0a000000-0000-4000-8000-0000000000c1"
				}
			}
		]
	}
	"""";
}
