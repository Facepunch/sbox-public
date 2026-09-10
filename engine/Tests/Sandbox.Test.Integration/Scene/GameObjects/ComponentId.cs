using Editor;
using Sandbox.Utility;
using System;

namespace SceneTests.GameObjects;

[TestClass]
public class ComponentIdsTest
{
	Sandbox.Internal.TypeLibrary TypeLibrary;
	Sandbox.Internal.TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		// Replace TypeLibrary with a mocked one, store the original

		_oldTypeLibrary = Game.TypeLibrary;

		TypeLibrary = new Sandbox.Internal.TypeLibrary();
		TypeLibrary.AddAssembly( typeof( PrefabFile ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( ComponentIdsTest ).Assembly, false );
		JsonUpgrader.UpdateUpgraders( TypeLibrary );

		Game.TypeLibrary = TypeLibrary;
	}

	[TestCleanup]
	public void TestCleanup()
	{
		// Make sure our mocked TypeLibrary doesn't leak out, restore the old one

		Game.TypeLibrary = _oldTypeLibrary;
	}

	[TestMethod]
	public void Assignment()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp1 = go.Components.Create<TestComponent>();

		// Did we have a guid assigned? (AreNotEqual, not AreNotSame - boxed Guids are never reference-equal)
		Assert.AreNotEqual( Guid.Empty, comp1.Id );
	}

	void TestReferences( GameObject go )
	{
		Assert.AreEqual( 1, go.Children.Count );

		var firstChild = go.Children[0];
		Assert.AreEqual( 1, firstChild.Children.Count );

		var secondChild = firstChild.Children[0];
		var firstComponent = firstChild.Components.Get<SkinnedModelRenderer>();
		Assert.IsNotNull( firstComponent );

		var secondComponent = secondChild.Components.Get<SkinnedModelRenderer>();
		Assert.IsNotNull( secondComponent );

		Assert.AreEqual( firstComponent.BoneMergeTarget, secondComponent );
	}

	[TestMethod]
	public void LoadOldReferenceModelAndNew()
	{
		using var _ = SceneTests.Helpers.RegisterPrefabFromJson( "oldprefab.prefab", _oldPrefabSource );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var oldPrefab = ResourceLibrary.Get<PrefabFile>( "oldprefab.prefab" );
		var oldPrefabScene = SceneUtility.GetPrefabScene( oldPrefab );
		var clone = oldPrefabScene.Clone();
		TestReferences( clone );

		clone.BreakFromPrefab();

		EditorUtility.Prefabs.ConvertGameObjectToPrefab( clone, "newprefab.prefab", true );
		var newPrefab = ResourceLibrary.Get<PrefabFile>( "newprefab.prefab" );
		var newPrefabScene = SceneUtility.GetPrefabScene( newPrefab );

		using var __ = new DisposeAction( () => Game.Resources.Unregister( newPrefab ) );

		var serialized = newPrefab.Serialize().ToJsonString( Json.options );
		Assert.IsTrue( serialized.Contains( "\"component_id\"" ) );

		clone = newPrefabScene.Clone();
		TestReferences( clone );
	}

	static readonly string _oldPrefabSource = """"
	{
		"Id": "388c271f-a643-4a18-bb21-e2c4d0f6b21d",
		"Name": "OldPrefabReference",
		"Position": "-198.1127,-61.92563,243.9359",
		"Enabled": true,
		"NetworkMode": 2,
		"Children": [
			{
			"Id": "2162371d-3d3e-47e5-933a-7fdb50b7990b",
			"Name": "ChildObject",
			"Position": "0,0,0",
			"Enabled": true,
			"NetworkMode": 2,
			"Components": [
				{
				"__type": "SkinnedModelRenderer",
				"Id": "a7aa5a5d-8e04-4090-873d-3b08742e1121",
				"BodyGroups": 18446744073709551615,
				"BoneMergeTarget": {
					"_type": "component",
					"go": "2a9e084a-4d3e-43d7-a9a4-498d6eaf6db2",
					"component_type": "SkinnedModelRenderer"
				},
				"CreateBoneObjects": false,
				"RenderType": "On",
				"Tint": "1,1,1,1"
				}
			],
			"Children": [
				{
				"Id": "2a9e084a-4d3e-43d7-a9a4-498d6eaf6db2",
				"Name": "ChildChildObject",
				"Position": "0,0,0",
				"Enabled": true,
				"NetworkMode": 2,
				"Components": [
					{
					"__type": "SkinnedModelRenderer",
					"Id": "66a7232d-4662-4376-bb8f-b1e243d9cb66",
					"BodyGroups": 18446744073709551615,
					"CreateBoneObjects": false,
					"RenderType": "On",
					"Tint": "1,1,1,1"
					}
				]
				}
			]
			}
		]
	}
	"""";
}

public class TestComponent : Component
{

}
