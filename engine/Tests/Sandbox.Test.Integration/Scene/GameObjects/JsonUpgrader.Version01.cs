using Sandbox.Internal;
using SceneTests;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Sandbox.Json;

namespace SceneTests.GameObjects;

[TestClass]
public class JsonUpgrader01Test
{
	TypeLibrary TypeLibrary;

	private TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		// Replace TypeLibrary / NodeLibrary with mocked ones, store the originals

		_oldTypeLibrary = Game.TypeLibrary;

		TypeLibrary = new Sandbox.Internal.TypeLibrary();
		TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( PrefabFile ).Assembly, false );
		JsonUpgrader.UpdateUpgraders( TypeLibrary );

		Game.TypeLibrary = TypeLibrary;
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Make sure our mocked TypeLibrary doesn't leak out, restore old ones
		Game.TypeLibrary = _oldTypeLibrary;
	}


	/// <summary>
	/// https://github.com/Facepunch/sbox/issues/2086
	/// </summary>
	[TestMethod]
	public void UpgraderHandlesReferencesToPrefabRoot()
	{
		var saveLocation = "__prefab_with_root_reference.prefab";

		var prefabReferencingRootContent = """
		{
			"__guid":"df54d9dc-b030-4522-9f11-9ab78c43f485",
			"__version":1,
			"Flags":0,
			"Name":"target_dummy",
			"Position":"0,0,0",
			"Rotation":"0,0,0,1",
			"Scale":"1,1,1",
			"Tags":"actor",
			"Enabled":true,
			"NetworkMode":1,
			"NetworkInterpolation":true,
			"NetworkOrphaned":1,
			"OwnerTransfer":1,
			"Components":[
				{
					"__type":"Sandbox.SkinnedModelRenderer",
					"__guid":"09f3e7f2-0358-461c-b7f1-8b637704284b",
					"AnimationGraph":null,
					"BodyGroups":18446744073709551615,
					"BoneMergeTarget":null,
					"CreateAttachments":false,
					"CreateBoneObjects":false,
					"MaterialGroup":null,
					"MaterialOverride":null,
					"Model":null,
					"Morphs":{

					},
					"OnComponentDestroy":null,
					"OnComponentDisabled":null,
					"OnComponentEnabled":null,
					"OnComponentFixedUpdate":null,
					"OnComponentStart":null,
					"OnComponentUpdate":null,
					"Parameters":{
						"bools":{

						},
						"ints":{

						},
						"floats":{

						},
						"vectors":{

						},
						"rotations":{

						}
					},
					"PlaybackRate":1,
					"RenderOptions":{
						"GameLayer":true,
						"OverlayLayer":false,
						"BloomLayer":false,
						"AfterUILayer":false
					},
					"RenderType":"On",
					"Sequence":{
						"Name":null,
						"Looping":true
					},
					"Tint":"1,1,1,1",
					"UseAnimGraph":true
				},
				{
					"__type":"Sandbox.ModelHitboxes",
					"__guid":"f427226a-c81e-4064-8c66-32e6411b1883",
					"OnComponentDestroy":null,
					"OnComponentDisabled":null,
					"OnComponentEnabled":null,
					"OnComponentFixedUpdate":null,
					"OnComponentStart":null,
					"OnComponentUpdate":null,
					"Renderer":{
						"_type":"component",
						"component_id":"09f3e7f2-0358-461c-b7f1-8b637704284b",
						"go":"df54d9dc-b030-4522-9f11-9ab78c43f485",
						"component_type":"SkinnedModelRenderer"
					},
					"Target":{
						"_type":"gameobject",
						"go":"df54d9dc-b030-4522-9f11-9ab78c43f485"
					}
				}
			],
			"Children":[

			]
		}
		""";

		string oldPrefabInstanceJson = """
		{
			"__guid": "5d6dad9b-96d1-45c3-a7c4-1412b8570422",
			"Flags": 0,
			"Name": "target_dummy (1)",
			"Position": "4.408689,0.0000001473241,32.37395",
			"Tags": "particles",
			"Enabled": true,
			"Rotation": "0,0,0,1",
			"Scale": "1,1,1",
			"__Prefab": "__prefab_with_root_reference.prefab",
			"__PrefabVariables": {}
		}
		""";


		using var prefabRegistration = SceneTests.Helpers.RegisterPrefabFromJson( saveLocation, prefabReferencingRootContent );

		var scene = Helpers.LoadSceneFromJson( "__prefab_instance_upgrade_test.prefab", oldPrefabInstanceJson );
		using var sceneScope = scene.Push();

		// Check tghat the modelhitbox component target points to the prefab instance root (target_dummy (1))
		var modelHitbox = scene.Components.Get<ModelHitboxes>( FindMode.EverythingInSelfAndDescendants );

		Assert.IsNotNull( modelHitbox, "ModelHitboxes component not found" );
		Assert.AreEqual( "5d6dad9b-96d1-45c3-a7c4-1412b8570422", modelHitbox.Target.Id.ToString(), "ModelHitboxes target ID does not match the prefab instance root" );
	}
}

