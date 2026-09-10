using Sandbox.Internal;
using System.Collections.Generic;

namespace SceneTests.GameObjects;

[TestClass]
public class CloneTest
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
		TypeLibrary.AddAssembly( typeof( ComponentWithPrefabSceneReference ).Assembly, false );
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
	/// When cloning something, you're meant to be able to start it as disabled.
	/// </summary>
	[TestMethod]
	public void CloneAsDisabled_Prefab()
	{
		var prefab = SceneTests.Prefab.PrefabTest.BasicPrefab;

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var b = prefab.Clone( new CloneConfig()
		{
			StartEnabled = false
		} );

		Assert.IsFalse( b.Enabled );
	}

	/// <summary>
	/// When cloning something, you're meant to be able to start it as disabled.
	/// </summary>
	[TestMethod]
	public void CloneAsDisabled_Prefab_Overloads()
	{
		var prefab = SceneTests.Prefab.PrefabTest.BasicPrefab;

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = scene.CreateObject();

		{
			var b = prefab.Clone( Transform.Zero, null, false );
			Assert.IsFalse( b.Enabled );
		}

		{
			var b = prefab.Clone( Transform.Zero, a, false );
			Assert.IsFalse( b.Enabled );
		}
	}

	[TestMethod]
	public void CloneAsDisabled_Prefab_DoesNotCallOnAwakeAndOnStart()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var prefab = SceneTests.Prefab.PrefabTest.GetPrefab( "onawakeonstart_disabled.prefab", OnAwakeOnStartTestPrefab );

		var clonedPrefab = prefab.Clone( new CloneConfig()
		{
			StartEnabled = false
		} );

		Assert.IsFalse( clonedPrefab.Enabled );

		var clonedComp = clonedPrefab.GetComponent<ComponentWithOnStartOnAwake>( true );
		Assert.IsNotNull( clonedComp );

		// need to tick to make sure all callbacks have a change to trigger
		scene.GameTick();

		Assert.IsFalse( clonedComp.WasAwakeCalled );
		Assert.IsFalse( clonedComp.WasStartCalled );
	}

	[TestMethod]
	public void CloneAsEnabled_Prefab_DoesCallOnAwakeAndOnStart()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var prefab = SceneTests.Prefab.PrefabTest.GetPrefab( "onawakeonstart.prefab", OnAwakeOnStartTestPrefab );

		var clonedPrefab = prefab.Clone( new CloneConfig()
		{
			StartEnabled = true
		} );

		Assert.IsTrue( clonedPrefab.Enabled );

		var clonedComp = clonedPrefab.GetComponent<ComponentWithOnStartOnAwake>( true );
		Assert.IsNotNull( clonedComp );

		// need to tick to make sure all callbacks have a change to trigger
		scene.GameTick();

		Assert.IsTrue( clonedComp.WasAwakeCalled );
		Assert.IsTrue( clonedComp.WasStartCalled );
	}


	/// <summary>
	/// When cloning a prefab, that contains a prefab we should respect the nested prefabs variables.
	/// Even variables of the nested prefab that reference the root of the outer prefab.
	/// </summary>
	[TestMethod]
	public void ClonePrefabWithNestedPrefabThatHasAPrefabVariableWhichReferencesTheOuterPrefabsRoot()
	{
		// Load the nested prefab into the resource register so the outer prefab can find it.
		var pfile = new PrefabFile();
		pfile.RegisterWeakResourceId( "nestedprefabwithgameobjectvariable.prefab" );
		pfile.LoadFromJson( NestedPrefabWithGameObjectVariable );

		Game.Resources.Register( pfile );

		var prefab = SceneTests.Prefab.PrefabTest.GetPrefab( "prefabwithnestedprefab.prefab", PrefabWithNestedPrefabThatHasAPrefabVariableWhichReferencesRoot );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();

		var prefabInstance = prefab.Clone( Transform.Zero, parent, true );
		Assert.AreEqual( 1, prefabInstance.Children.Count );
		Assert.IsNotNull( prefabInstance.Children[0] );
		Assert.IsNotNull( prefabInstance.Children[0].GetComponent<Sandbox.ManualHitbox>() );
		Assert.AreEqual( prefabInstance, prefabInstance.Children[0].GetComponent<Sandbox.ManualHitbox>().Target );

		Game.Resources.Unregister( pfile );
	}

	static readonly string PrefabWithNestedPrefabThatHasAPrefabVariableWhichReferencesRoot = """
			{
		  "RootObject": {
		    "__guid": "8ed60e70-d12f-45a7-a674-ede4d6349b67",
		    "Flags": 0,
		    "Name": "prefabwithnestedprefab",
		    "Enabled": true,
		    "Children": [
		      {
		        "__guid": "fb8e374d-0744-4d2d-a71f-d3c2502c1fd8",
		        "Flags": 0,
		        "Name": "Nested",
		        "Enabled": true,
		        "__Prefab": "nestedprefabwithgameobjectvariable.prefab",
		        "__PrefabVariables": {
		          "Target": {
		            "_type": "gameobject",
		            "go": "8ed60e70-d12f-45a7-a674-ede4d6349b67"
		          }
		        }
		      }
		    ],
		    "__variables": [],
		    "__properties": {
		      "FixedUpdateFrequency": 50,
		      "MaxFixedUpdates": 5,
		      "NetworkFrequency": 30,
		      "NetworkInterpolation": true,
		      "PhysicsSubSteps": 1,
		      "ThreadedAnimation": true,
		      "TimeScale": 1,
		      "UseFixedUpdate": true,
		      "Metadata": {},
		      "NavMesh": {
		        "Enabled": false,
		        "IncludeStaticBodies": true,
		        "IncludeKeyframedBodies": true,
		        "EditorAutoUpdate": true,
		        "AgentHeight": 64,
		        "AgentRadius": 16,
		        "AgentStepSize": 18,
		        "AgentMaxSlope": 40,
		        "ExcludedBodies": "",
		        "IncludedBodies": ""
		      }
		    }
		  },
		  "ShowInMenu": false,
		  "DontBreakAsTemplate": false,
		  "ResourceVersion": 1,
		  "__references": [],
		  "__version": 1
		}
		""";

	static readonly string NestedPrefabWithGameObjectVariable = """
		  {
		  "RootObject": {
		    "__guid": "fb8e374d-0744-4d2d-a71f-d3c2502c1fd8",
		    "Flags": 0,
		    "Name": "nestedprefabwithgameobjectvariable",
		    "Enabled": true,
		    "Components": [
		      {
		        "__type": "Sandbox.ManualHitbox",
		        "__guid": "e51182e3-5586-49cb-a90c-56b33506d04b",
		        "CenterA": "0,0,0",
		        "CenterB": "0,0,0",
		        "HitboxTags": "",
		        "OnComponentDestroy": null,
		        "OnComponentDisabled": null,
		        "OnComponentEnabled": null,
		        "OnComponentFixedUpdate": null,
		        "OnComponentStart": null,
		        "OnComponentUpdate": null,
		        "Radius": 10,
		        "Shape": "Sphere",
		        "Target": null
		      }
		    ],
		    "Children": [],
		    "__variables": [
		      {
		        "Id": "Target",
		        "Title": "Target",
		        "Description": null,
		        "Group": null,
		        "Order": 0,
		        "Targets": [
		          {
		            "Id": "e51182e3-5586-49cb-a90c-56b33506d04b",
		            "Property": "Target"
		          }
		        ]
		      }
		    ],
		    "__properties": {
		      "FixedUpdateFrequency": 50,
		      "MaxFixedUpdates": 5,
		      "NetworkFrequency": 30,
		      "NetworkInterpolation": true,
		      "PhysicsSubSteps": 1,
		      "ThreadedAnimation": true,
		      "TimeScale": 1,
		      "UseFixedUpdate": true,
		      "Metadata": {},
		      "NavMesh": {
		        "Enabled": false,
		        "IncludeStaticBodies": true,
		        "IncludeKeyframedBodies": true,
		        "EditorAutoUpdate": true,
		        "AgentHeight": 64,
		        "AgentRadius": 16,
		        "AgentStepSize": 18,
		        "AgentMaxSlope": 40,
		        "ExcludedBodies": "",
		        "IncludedBodies": ""
		      }
		    }
		  },
		  "ShowInMenu": false,
		  "DontBreakAsTemplate": false,
		  "ResourceVersion": 1,
		  "__references": [],
		  "__version": 1
		}
		""";

	public class ComponentWithPrefabSceneReference : Component
	{
		[Sandbox.Property]
		public PrefabScene PrefabRef { get; set; }
	}

	public class ComponentWithPrefabRootReference : Component
	{
		[Sandbox.Property]
		public GameObject PrefabRootRef { get; set; }
	}


	static readonly string OnAwakeOnStartTestPrefab = """"

		{
		  "RootObject": {
		    "Id": "fab370f8-2e2c-48cf-a523-e4be49723490",
		    "Name": "Object",
		    "Position": "788.8395,-1793.604,-1218.092",
		    "Enabled": true,
		    "Components": [
		      {
		        "__type": "ComponentWithOnStartOnAwake"
		      }
		    ]
		  },
		  "ShowInMenu": false,
		  "MenuPath": null,
		  "MenuIcon": null,
		  "__references": []
		}

		"""";
}

public class EmptyReferenceType
{
}

public class ReferenceTypeWithReferenceTypeProperty
{
	public EmptyReferenceType Reference;
}

public struct ValueTypeWithReferenceTypeProperty
{
	public EmptyReferenceType Reference;
}

public struct ValueTypeWithValueTypeProperty
{
	public Vector3 Value;
}

public struct ValueTypeWithCollectionProperty
{
	public List<int> Collection;
}

public class ComponentWithOnStartOnAwake : Component
{
	public bool WasAwakeCalled = false;
	public bool WasStartCalled = false;

	protected override void OnAwake()
	{
		base.OnAwake();
		WasAwakeCalled = true;
	}

	protected override void OnStart()
	{
		base.OnStart();
		WasStartCalled = true;
	}
}
