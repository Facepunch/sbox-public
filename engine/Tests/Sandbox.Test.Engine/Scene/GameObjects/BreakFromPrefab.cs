using Sandbox.Utility;
using System;

namespace SceneTests.GameObjects;

/// <summary>
/// Tests for converting nested instances to full instances (breaking, and reparenting out) on
/// nested and deeply nested instances, across a full save/reopen cycle (whole-scene serialize
/// then deserialize into a fresh scene).
/// </summary>
[TestClass]
[DoNotParallelize]
public class BreakFromPrefabTest : SceneTest
{
	// A leaf prefab carrying real content, reused as both the single-level inner prefab and the
	// deepest level of the three-level chain.
	private const string LeafPath = "test_break_leaf.prefab";
	private const string LeafRootGuid = "11111111-0000-4000-8000-000000000001";
	private const string LeafCompGuid = "11111111-0000-4000-8000-000000000002";

	// A single-level outer prefab: contains one nested instance of the leaf and a component on its
	// root that references that nested instance.
	private const string OuterPath = "test_break_outer.prefab";
	private const string OuterRootGuid = "22222222-0000-4000-8000-000000000001";
	private const string OuterCompGuid = "22222222-0000-4000-8000-000000000002";
	private const string OuterLeafGuid = "22222222-0000-4000-8000-000000000003";
	private const string OuterLeafCompGuid = "22222222-0000-4000-8000-000000000004";

	// The three-level chain: Level1 contains a Level2 instance, which contains a leaf instance.
	private const string Level2Path = "test_break_l2.prefab";
	private const string L2RootGuid = "33333333-0000-4000-8000-000000000001";
	private const string L2LeafGuid = "33333333-0000-4000-8000-000000000002";
	private const string L2LeafCompGuid = "33333333-0000-4000-8000-000000000003";

	private const string Level1Path = "test_break_l1.prefab";
	private const string L1RootGuid = "44444444-0000-4000-8000-000000000001";
	private const string L1L2Guid = "44444444-0000-4000-8000-000000000002";
	private const string L1LeafGuid = "44444444-0000-4000-8000-000000000003";
	private const string L1LeafCompGuid = "44444444-0000-4000-8000-000000000004";

	private static readonly string StatType = typeof( PrefabInstanceStatComponent ).FullName;
	private static readonly string RefType = typeof( RefHolderComponent ).FullName;

	private static readonly string LeafJson = $$"""
	{
		"__guid": "{{LeafRootGuid}}",
		"__version": 2,
		"Flags": 0,
		"Name": "Leaf",
		"Enabled": true,
		"Components": [
			{ "__type": "{{StatType}}", "__guid": "{{LeafCompGuid}}", "__enabled": true, "Number": 33, "Text": "leaf" }
		],
		"Children": []
	}
	""";

	private static readonly string OuterJson = $$"""
	{
		"__guid": "{{OuterRootGuid}}",
		"__version": 2,
		"Flags": 0,
		"Name": "Outer",
		"Enabled": true,
		"Components": [
			{ "__type": "{{RefType}}", "__guid": "{{OuterCompGuid}}", "__enabled": true, "Target": { "_type": "gameobject", "go": "{{OuterLeafGuid}}" } }
		],
		"Children": [
			{{NestedInstanceJson( OuterLeafGuid, LeafPath, LeafRootGuid, OuterLeafGuid, LeafCompGuid, OuterLeafCompGuid )}}
		]
	}
	""";

	private static readonly string Level2Json = $$"""
	{
		"__guid": "{{L2RootGuid}}",
		"__version": 2,
		"Flags": 0,
		"Name": "L2",
		"Enabled": true,
		"Components": [],
		"Children": [
			{{NestedInstanceJson( L2LeafGuid, LeafPath, LeafRootGuid, L2LeafGuid, LeafCompGuid, L2LeafCompGuid )}}
		]
	}
	""";

	// Level1's id lookup must cover every required id of the Level2 prefab cache scene, including
	// the expanded leaf instance objects.
	private static readonly string Level1Json = $$"""
	{
		"__guid": "{{L1RootGuid}}",
		"__version": 2,
		"Flags": 0,
		"Name": "L1",
		"Enabled": true,
		"Components": [],
		"Children": [
			{
				"__guid": "{{L1L2Guid}}",
				"__version": 2,
				"__Prefab": "{{Level2Path}}",
				"__PrefabInstancePatch": { "AddedObjects": [], "RemovedObjects": [], "PropertyOverrides": [], "MovedObjects": [] },
				"__PrefabIdToInstanceId": {
					"{{L2RootGuid}}": "{{L1L2Guid}}",
					"{{L2LeafGuid}}": "{{L1LeafGuid}}",
					"{{L2LeafCompGuid}}": "{{L1LeafCompGuid}}"
				}
			}
		]
	}
	""";

	// Compact JSON for a nested prefab instance child: __Prefab source, empty patch, id lookup.
	private static string NestedInstanceJson( string instanceGuid, string prefabPath, string prefabRootGuid, string instanceRootGuid, string prefabCompGuid, string instanceCompGuid )
	{
		return $$"""
		{
			"__guid": "{{instanceGuid}}",
			"__version": 2,
			"__Prefab": "{{prefabPath}}",
			"__PrefabInstancePatch": { "AddedObjects": [], "RemovedObjects": [], "PropertyOverrides": [], "MovedObjects": [] },
			"__PrefabIdToInstanceId": { "{{prefabRootGuid}}": "{{instanceRootGuid}}", "{{prefabCompGuid}}": "{{instanceCompGuid}}" }
		}
		""";
	}

	// Registers the leaf and single-level outer prefabs, returning the outer cached prefab scene.
	private static IDisposable RegisterNestedPrefabs( out PrefabScene outerScene )
	{
		var leaf = Helpers.RegisterPrefabFromJson( LeafPath, LeafJson );
		var outer = Helpers.RegisterPrefabFromJson( OuterPath, OuterJson );
		outerScene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( OuterPath ) );

		return new DisposeAction( () => { outer.Dispose(); leaf.Dispose(); } );
	}

	// Registers the three-level chain (Level1 -> Level2 instance -> leaf instance), returning the
	// Level1 cached prefab scene.
	private static IDisposable RegisterDeeplyNestedPrefabs( out PrefabScene level1Scene )
	{
		var leaf = Helpers.RegisterPrefabFromJson( LeafPath, LeafJson );
		var l2 = Helpers.RegisterPrefabFromJson( Level2Path, Level2Json );
		var l1 = Helpers.RegisterPrefabFromJson( Level1Path, Level1Json );
		level1Scene = SceneUtility.GetPrefabScene( ResourceLibrary.Get<PrefabFile>( Level1Path ) );

		return new DisposeAction( () => { l1.Dispose(); l2.Dispose(); leaf.Dispose(); } );
	}

	// Serializes the scene and deserializes into a fresh one, like saving to disk and reopening.
	private static Scene SaveAndReopen( Scene scene )
	{
		var json = scene.Serialize();

		var fresh = new Scene();
		using ( fresh.Push() )
		{
			fresh.Deserialize( json );
		}
		return fresh;
	}

	private static GameObject GetSceneObject( Scene scene, string name )
	{
		var go = scene.Children.FirstOrDefault( c => c.Name == name );
		Assert.IsNotNull( go, $"Expected scene to contain a top-level object named '{name}'" );
		return go;
	}

	// Breaking a single-level instance and reopening keeps the nested content and a reference into
	// it (the reported bug broke the reference and lost the components).
	[TestMethod]
	public void NestedInstanceSurvivesSaveReload()
	{
		using var registration = RegisterNestedPrefabs( out var outerScene );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var outer = outerScene.Clone();
		outer.Name = "Outer";
		var nested = outer.Children[0];

		Assert.IsTrue( nested.IsNestedPrefabInstanceRoot );
		Assert.AreSame( nested, outer.Components.Get<RefHolderComponent>().Target, "sanity: reference resolves before break" );

		outer.BreakFromPrefab();

		var reopened = SaveAndReopen( scene );
		using var reopenedScope = reopened.Push();

		var restored = GetSceneObject( reopened, "Outer" );
		Assert.IsFalse( restored.IsPrefabInstance, "the broken object must reload as a plain object" );

		var restoredNested = restored.Children[0];
		Assert.AreEqual( 33, restoredNested.Components.Get<PrefabInstanceStatComponent>().Number, "nested content must survive" );
		Assert.AreSame( restoredNested, restored.Components.Get<RefHolderComponent>().Target, "reference into the nested instance must still resolve" );
	}

	// A modified nested instance (property override plus an added component) keeps those changes
	// through break, save and reload.
	[TestMethod]
	public void ModifiedNestedInstanceSurvivesSaveReload()
	{
		using var registration = RegisterNestedPrefabs( out var outerScene );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var outer = outerScene.Clone();
		outer.Name = "Outer";
		var nested = outer.Children[0];

		nested.Components.Get<PrefabInstanceStatComponent>().Number = 99;
		nested.Components.Create<PrefabInstanceExtraComponent>().Label = "added-to-nested";
		outer.PrefabInstance.RefreshPatch();

		outer.BreakFromPrefab();

		var reopened = SaveAndReopen( scene );
		using var reopenedScope = reopened.Push();

		var restoredNested = GetSceneObject( reopened, "Outer" ).Children[0];

		Assert.AreEqual( 99, restoredNested.Components.Get<PrefabInstanceStatComponent>().Number, "property override must survive" );
		var extra = restoredNested.Components.Get<PrefabInstanceExtraComponent>();
		Assert.IsNotNull( extra, "added component must survive" );
		Assert.AreEqual( "added-to-nested", extra.Label );
	}

	// Clean baseline: breaking a freshly cloned three-level instance keeps the deeply nested
	// content. A clone has pristine lookups, so only the loaded-from-disk case below breaks.
	[TestMethod]
	public void DeeplyNestedClonedInstanceSurvivesSaveReload()
	{
		using var registration = RegisterDeeplyNestedPrefabs( out var level1Scene );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var l1 = level1Scene.Clone();
		l1.Name = "L1Instance";

		var l3 = l1.Children[0].Children[0];
		Assert.IsTrue( l3.IsNestedPrefabInstanceRoot, "sanity: chain is three levels deep" );
		Assert.AreEqual( 33, l3.Components.Get<PrefabInstanceStatComponent>().Number );

		l1.BreakFromPrefab();

		var reopened = SaveAndReopen( scene );
		using var reopenedScope = reopened.Push();

		var restoredL3 = GetSceneObject( reopened, "L1Instance" ).Children[0].Children[0];
		Assert.AreEqual( 33, restoredL3.Components.Get<PrefabInstanceStatComponent>().Number, "deeply nested content must survive break + save + reload" );
	}

	// Reproduces the reported bug: a deeply nested instance loaded from a saved scene (so its
	// mappings were rebuilt on load, not minted by Clone), broken at the outermost level, must not
	// leave the promoted instances looking fully modified (every property and component changed).
	[TestMethod]
	public void DeeplyNestedLoadedInstanceConvertsWithoutSpuriousModifications()
	{
		using var registration = RegisterDeeplyNestedPrefabs( out var level1Scene );

		// Author the scene, then save and reopen it so the instance comes back through the load
		// path (which rebuilds the nested mappings), exactly like the user's project.
		var authoringScene = new Scene();
		using ( authoringScene.Push() )
		{
			level1Scene.Clone().Name = "L1Instance";
		}

		var scene = SaveAndReopen( authoringScene );
		using var sceneScope = scene.Push();

		var l1 = GetSceneObject( scene, "L1Instance" );
		var l2 = l1.Children[0];
		var l3 = l2.Children[0];

		Assert.IsTrue( l1.IsOutermostPrefabInstanceRoot, "sanity: L1 is the outermost instance after reload" );
		Assert.IsTrue( l2.IsNestedPrefabInstanceRoot, "sanity: L2 is nested after reload" );
		Assert.IsTrue( l3.IsNestedPrefabInstanceRoot, "sanity: L3 is nested after reload" );
		Assert.IsFalse( l1.PrefabInstance.IsModified(), "sanity: nothing was modified by the user before the break" );

		l1.BreakFromPrefab();

		Assert.IsTrue( l2.IsOutermostPrefabInstanceRoot, "L2 should be promoted to a full instance" );
		Assert.IsFalse( l2.PrefabInstance.IsModified(), "promoted L2 instance must not be spuriously marked as modified after break" );
		Assert.IsFalse( l2.PrefabInstance.IsAddedGameObject( l3 ), "the nested L3 instance must not be treated as a newly added object after break" );
	}

	// Reparenting a loaded nested instance out of its parent promotes it the same way breaking does
	// (ConvertNestedToFullPrefabInstance), so it must not leave the promoted instance looking
	// fully modified either.
	[TestMethod]
	public void ReparentingLoadedNestedInstanceConvertsWithoutSpuriousModifications()
	{
		using var registration = RegisterDeeplyNestedPrefabs( out var level1Scene );

		var authoringScene = new Scene();
		using ( authoringScene.Push() )
		{
			level1Scene.Clone().Name = "L1Instance";
			new GameObject( true, "NeutralParent" );
		}

		var scene = SaveAndReopen( authoringScene );
		using var sceneScope = scene.Push();

		var l2 = GetSceneObject( scene, "L1Instance" ).Children[0];
		Assert.IsTrue( l2.IsNestedPrefabInstanceRoot, "sanity: L2 is nested after reload" );

		// Reparent the nested L2 out to a neutral object, promoting it to a full instance.
		l2.SetParent( GetSceneObject( scene, "NeutralParent" ) );

		Assert.IsTrue( l2.IsOutermostPrefabInstanceRoot, "L2 should be promoted to a full instance" );
		Assert.IsFalse( l2.PrefabInstance.IsModified(), "promoted L2 instance must not be spuriously marked as modified after reparent" );
	}
}
