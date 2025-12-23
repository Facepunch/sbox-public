using Sandbox.Internal;
using System.Collections.Generic;

namespace GameObjects;

[TestClass]
public class CloneTests
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
	/// When cloning a GameObject that has child GameObjects that are being destroyed, they shouldn't be serialized, or cloned.
	/// </summary>
	[TestMethod]
	public void DestroyBCloneASameFrame()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = scene.CreateObject();
		var b = scene.CreateObject();
		b.SetParent( a );

		// Make sure we have one child on a
		Assert.AreEqual( 1, a.Children.Count );

		// Mark b as destroyed, won't actually be destroyed until next game tick
		b.Destroy();

		// Clone a, we shouldn't have any children because we are destroying b
		var c = a.Clone();

		Assert.AreEqual( 0, c.Children.Count );
	}

	/// <summary>
	/// When cloning something, you're meant to be able to start it as disabled.
	/// </summary>
	[TestMethod]
	public void CloneAsDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = scene.CreateObject();

		var b = a.Clone( new CloneConfig()
		{
			StartEnabled = false
		} );

		Assert.IsFalse( b.Enabled );
	}

	/// <summary>
	/// When cloning something, you're meant to be able to start it as disabled.
	/// </summary>
	[TestMethod]
	public void CloneAsDisabled_Prefab()
	{
		var prefab = Prefab.Prefabs.BasicPrefab;

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var b = prefab.Clone( new CloneConfig()
		{
			StartEnabled = false
		} );

		Assert.IsFalse( b.Enabled );
	}

	/// <summary>
	/// Make sure components with NotCloned flag are not cloned
	/// </summary>
	[TestMethod]
	public void ComponentNotClonedFlag()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = scene.CreateObject();
		var comp = a.AddComponent<ManualHitbox>();
		comp.Flags |= ComponentFlags.NotCloned;

		var clone = a.Clone();

		Assert.IsNull( clone.GetComponent<ManualHitbox>() );
	}

	/// <summary>
	/// When cloning something, you're meant to be able to start it as disabled.
	/// </summary>
	[TestMethod]
	public void CloneAsDisabled_Overloads()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var a = scene.CreateObject();

		{
			var b = a.Clone( Transform.Zero, null, false );
			Assert.IsFalse( b.Enabled );
		}

		{
			var b = a.Clone( Transform.Zero, a, false );
			Assert.IsFalse( b.Enabled );
		}
	}

	/// <summary>
	/// When cloning something, you're meant to be able to start it as disabled.
	/// </summary>
	[TestMethod]
	public void CloneAsDisabled_Prefab_Overloads()
	{
		var prefab = Prefab.Prefabs.BasicPrefab;

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
	public void CloneAsDisabled_DoesNotCallOnAwakeAndOnStart()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.AddComponent<ComponentWithOnStartOnAwake>();

		var clonedGo = go.Clone( new CloneConfig()
		{
			StartEnabled = false
		} );

		Assert.IsFalse( clonedGo.Enabled );

		var clonedComp = clonedGo.GetComponent<ComponentWithOnStartOnAwake>( true );
		Assert.IsNotNull( clonedComp );

		// need to tick to make sure all callbacks have a change to trigger
		scene.GameTick();

		Assert.IsFalse( clonedComp.WasAwakeCalled );
		Assert.IsFalse( clonedComp.WasStartCalled );
	}

	[TestMethod]
	public void CloneAsDisabled_Prefab_DoesNotCallOnAwakeAndOnStart()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var prefab = Prefab.Prefabs.GetPrefab( "onawakeonstart_disabled.prefab", OnAwakeOnStartTestPrefab );

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
	public void CloneAsEnabled_DoesCallOnAwakeAndOnStart()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.AddComponent<ComponentWithOnStartOnAwake>();

		var clonedGo = go.Clone( new CloneConfig()
		{
			StartEnabled = true
		} );

		Assert.IsTrue( clonedGo.Enabled );

		var clonedComp = clonedGo.GetComponent<ComponentWithOnStartOnAwake>( true );
		Assert.IsNotNull( clonedComp );

		// need to tick to make sure all callbacks have a change to trigger
		scene.GameTick();

		Assert.IsTrue( clonedComp.WasAwakeCalled );
		Assert.IsTrue( clonedComp.WasStartCalled );
	}

	[TestMethod]
	public void CloneAsEnabled_Prefab_DoesCallOnAwakeAndOnStart()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var prefab = Prefab.Prefabs.GetPrefab( "onawakeonstart.prefab", OnAwakeOnStartTestPrefab );

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
		pfile.SetIdFromResourcePath( "nestedprefabwithgameobjectvariable.prefab" );
		pfile.LoadFromJson( NestedPrefabWithGameObjectVariable );

		Game.Resources.Register( pfile );

		var prefab = Prefab.Prefabs.GetPrefab( "prefabwithnestedprefab.prefab", PrefabWithNestedPrefabThatHasAPrefabVariableWhichReferencesRoot );

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

	/// <summary>
	/// When cloning a prefab, that has a PrefabScene property that references the prefab itself, we should be able to clone it.
	/// The cloned PrefabScene property  should still point to the PrefabScene.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CloneSelfReferencingPrefabSceneProperty( bool breakBeforeClone )
	{
		// Load the nested prefab into the resource register so the prefab can find it.
		var pfile = new PrefabFile();
		pfile.SetIdFromResourcePath( "prefabwithselfreference.prefab" );
		pfile.LoadFromJson( PrefabWithSelfReference );
		Game.Resources.Register( pfile );

		var prefab = SceneUtility.GetPrefabScene( pfile );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		GameObject gameObjectToClone = prefab;
		// We also want to test cloning fore prefabs 
		if ( breakBeforeClone )
		{
			// This clone is part of the setup and not part of the actual test
			gameObjectToClone = prefab.Clone();
			gameObjectToClone.BreakFromPrefab();
		}

		var prefabInstance = prefab.Clone();
		Assert.IsNotNull( prefabInstance.GetComponent<ComponentWithPrefabSceneReference>() );
		Assert.IsNotNull( prefab.GetComponent<ComponentWithPrefabSceneReference>().PrefabRef );
		Assert.AreEqual( prefab.GetComponent<ComponentWithPrefabSceneReference>().PrefabRef, prefabInstance.GetComponent<ComponentWithPrefabSceneReference>().PrefabRef );

		Game.Resources.Unregister( pfile );
	}

	static readonly string PrefabWithSelfReference = """
	{
	  "RootObject": {
	    "__guid": "1390a3b8-bbc3-4c73-9e18-2edc5b9c2587",
	    "Flags": 0,
	    "Name": "prefabwithselfreference",
	    "Enabled": true,
	    "Components": [
	      {
	        "__type": "ComponentWithPrefabSceneReference",
	        "__guid": "f1982f17-e2d2-4ee0-b24f-07558f309b78",
	        "PrefabRef": {
	          "_type": "gameobject",
	          "prefab": "prefabwithselfreference.prefab"
	        }
	      }
	    ],
	    "Children": [],
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
	  "MenuPath": null,
	  "MenuIcon": null,
	  "DontBreakAsTemplate": false,
	  "ResourceVersion": 1,
	  "__version": 1
	}
	""";

	public class ComponentWithPrefabSceneReference : Component
	{
		[Sandbox.Property]
		public PrefabScene PrefabRef { get; set; }
	}

	/// <summary>
	/// When cloning a prefab, that has a GameObject property that references the root of the prefab and therefore the prefab itself, we should be able to clone it.
	/// To match old cloning behvaiour, the cloned property should still reference the cloned root GameObject and no longer the PrefabScene.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ClonePrefabRootReferencingGameObjectProperty( bool breakBeforeClone )
	{
		// Load the nested prefab into the resource register so the prefab can find it.
		var pfile = new PrefabFile();
		pfile.SetIdFromResourcePath( "prefabwithRootreference.prefab" );
		pfile.LoadFromJson( PrefabWithRootReference );
		Game.Resources.Register( pfile );

		var prefab = SceneUtility.GetPrefabScene( pfile );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		GameObject gameObjectToClone = prefab;
		// We also want to test cloning fore prefabs 
		if ( breakBeforeClone )
		{
			// This clone is part of the setup and not part of the actual test
			gameObjectToClone = prefab.Clone();
			gameObjectToClone.BreakFromPrefab();
		}

		var prefabInstance = prefab.Clone();
		Assert.IsNotNull( prefabInstance.GetComponent<ComponentWithPrefabRootReference>() );
		Assert.IsNotNull( prefab.GetComponent<ComponentWithPrefabRootReference>().PrefabRootRef );

		Assert.AreNotEqual( prefab.GetComponent<ComponentWithPrefabRootReference>().PrefabRootRef, prefabInstance.GetComponent<ComponentWithPrefabRootReference>().PrefabRootRef );
		Assert.IsInstanceOfType( prefabInstance.GetComponent<ComponentWithPrefabRootReference>().PrefabRootRef, typeof( GameObject ) );

		Game.Resources.Unregister( pfile );
	}

	public class ComponentWithPrefabRootReference : Component
	{
		[Sandbox.Property]
		public GameObject PrefabRootRef { get; set; }
	}


	static readonly string PrefabWithRootReference = """
	{
	  "RootObject": {
	    "__guid": "1390a3b8-bbc3-4c73-9e18-2edc5b9c2587",
	    "Flags": 0,
	    "Name": "prefabwithRootreference",
	    "Enabled": true,
	    "Components": [
	      {
	        "__type": "ComponentWithPrefabRootReference",
	        "__guid": "f1982f17-e2d2-4ee0-b24f-07558f309b78",
	        "PrefabRootRef": {
	          "_type": "gameobject",
	          "prefab": "prefabwithRootreference.prefab"
	        }
	      }
	    ],
	    "Children": [],
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
	  "MenuPath": null,
	  "MenuIcon": null,
	  "DontBreakAsTemplate": false,
	  "ResourceVersion": 1,
	  "__version": 1
	}
	""";

	/// <summary>
	/// We would like to clone some types by copy.
	/// But we can only do that if they don't have any reference types in them.
	/// </summary>
	[TestMethod]
	public void ReflectionQueryCache_IsTypeClonableByCopy()
	{
		Assert.IsTrue( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( int ) ) );
		Assert.IsTrue( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( Vector3 ) ) );

		Assert.IsTrue( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( ValueTypeWithValueTypeProperty ) ) );

		Assert.IsFalse( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( List<> ) ) );
		Assert.IsFalse( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( GameObject ) ) );
		Assert.IsFalse( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( Component ) ) );

		Assert.IsFalse( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( EmptyReferenceType ) ) );

		Assert.IsFalse( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( ReferenceTypeWithReferenceTypeProperty ) ) );

		Assert.IsFalse( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( ValueTypeWithReferenceTypeProperty ) ) );

		Assert.IsFalse( ReflectionQueryCache.IsTypeCloneableByCopy( typeof( ValueTypeWithCollectionProperty ) ) );
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
