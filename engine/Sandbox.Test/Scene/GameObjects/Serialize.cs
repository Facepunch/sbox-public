using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace GameObjects;

[TestClass]
public class SerializeTest
{
	TypeLibrary TypeLibrary;
	NodeLibrary NodeLibrary;

	private TypeLibrary _oldTypeLibrary;
	private NodeLibrary _oldNodeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		// Replace TypeLibrary / NodeLibrary with mocked ones, store the originals

		_oldTypeLibrary = Game.TypeLibrary;
		_oldNodeLibrary = Game.NodeLibrary;

		TypeLibrary = new Sandbox.Internal.TypeLibrary();
		TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( OldVersioningComponent ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( InheritedPropertyComponent ).Assembly, false );
		JsonUpgrader.UpdateUpgraders( TypeLibrary );

		NodeLibrary = new NodeLibrary( new TypeLoader( () => TypeLibrary ) );
		NodeLibrary.AddAssembly( typeof( LogNodes ).Assembly );
		NodeLibrary.AddAssembly( typeof( Scene ).Assembly ); // engine

		Game.TypeLibrary = TypeLibrary;
		Game.NodeLibrary = NodeLibrary;
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Make sure our mocked TypeLibrary / NodeLibrary doesn't leak out, restore old ones

		Game.TypeLibrary = _oldTypeLibrary;
		Game.NodeLibrary = _oldNodeLibrary;
	}

	/// <summary>
	/// Fails the test if type library isn't initialized, or doesn't contain the given types.
	/// </summary>
	private void AssertTypeLibraryReady( params Type[] expectedTypes )
	{
		Assert.IsNotNull( TypeLibrary, "TypeLibrary hasn't been mocked" );

		foreach ( var type in expectedTypes )
		{
			Assert.IsNotNull( TypeLibrary.GetType( type ), "TypeLibrary hasn't been given the game assembly" );
		}
	}

	[TestMethod]
	public void SerializeSingle()
	{
		AssertTypeLibraryReady( typeof( ModelRenderer ), typeof( ComponentIdTest ) );

		using var scope = new Scene().Push();

		var go1 = new GameObject();
		go1.Name = "My Game Object";
		go1.LocalTransform = new Transform( Vector3.Up, Rotation.Identity, 10 );

		var go1comp1 = go1.Components.Create<ComponentIdTest>();
		var go1comp2 = go1.Components.Create<ComponentIdTest>();

		go1comp1.Other = go1comp2;
		go1comp2.Other = go1comp1;

		var model = go1.Components.Create<ModelRenderer>();
		model.Model = Model.Load( "models/dev/box.vmdl" );
		model.Tint = Color.Red;

		var node = go1.Serialize();

		System.Console.WriteLine( node );
		SceneUtility.MakeIdGuidsUnique( node );

		var go2 = new GameObject();
		go2.Deserialize( node );

		var go2comps = go2.Components.GetAll<ComponentIdTest>().ToArray();
		var go2comp1 = go2comps[0];
		var go2comp2 = go2comps[1];

		Assert.AreNotEqual( go1comp1.Id, go2comp1.Id );
		Assert.AreNotEqual( go1comp2.Id, go2comp2.Id );

		Assert.AreEqual( go2comp1, go2comp2.Other );
		Assert.AreEqual( go2comp2, go2comp1.Other );

		Assert.AreNotEqual( go1.Id, go2.Id );
		Assert.AreEqual( go1.Name, go2.Name );
		Assert.AreEqual( go1.Enabled, go2.Enabled );
		Assert.AreEqual( go1.LocalTransform, go2.LocalTransform );
		Assert.AreEqual( go1.Components.Count, go2.Components.Count );
		Assert.AreEqual( go1.Components.Get<ModelRenderer>().Model, go2.Components.Get<ModelRenderer>().Model );
		Assert.AreEqual( go1.Components.Get<ModelRenderer>().Tint, go2.Components.Get<ModelRenderer>().Tint );
		Assert.AreEqual( go1.Components.Get<ModelRenderer>().MaterialOverride, go2.Components.Get<ModelRenderer>().MaterialOverride );
	}

	#region Cloning

	[TestMethod]
	public void CloneWithReferences()
	{
		AssertTypeLibraryReady( typeof( ModelRenderer ), typeof( ComponentIdTest ) );

		using var scope = new Scene().Push();

		var go1 = new GameObject();
		go1.Name = "My Game Object";

		var go1comp1 = go1.Components.Create<ComponentIdTest>();
		var go1comp2 = go1.Components.Create<ComponentIdTest>();
		var go1comp3 = go1.Components.Create<ComponentIdTest>();

		go1comp1.Other = go1comp3;
		go1comp2.Other = go1comp1;
		go1comp3.Other = go1comp2;

		var go2 = go1.Clone();

		var go2comps = go2.Components.GetAll<ComponentIdTest>().ToArray();
		Assert.AreEqual( 3, go2comps.Length );

		var go2comp1 = go2comps[0];
		var go2comp2 = go2comps[1];
		var go2comp3 = go2comps[2];

		Assert.IsNotNull( go2comp1 );
		Assert.IsNotNull( go2comp2 );
		Assert.IsNotNull( go2comp3 );

		Assert.AreEqual( go2comp1.Id, go2comp1.Id );
		Assert.AreEqual( go2comp2.Id, go2comp2.Id );
		Assert.AreEqual( go2comp3.Id, go2comp3.Id );

		Assert.IsNotNull( go2comp1.Other );
		Assert.IsNotNull( go2comp2.Other );
		Assert.IsNotNull( go2comp3.Other );

		Assert.AreEqual( go2comp3.Id, go2comp1.Other.Id );
		Assert.AreEqual( go2comp1.Id, go2comp2.Other.Id );
		Assert.AreEqual( go2comp2.Id, go2comp3.Other.Id );
	}

	/// <summary>
	/// Cloned components need a deep copy of reference type properties. Here, the source
	/// object has a <see cref="List{T}"/>, and its clone shouldn't reference the same
	/// list. Otherwise, after cloning, when the clone modifies its list, the source
	/// object would see those changes.
	/// </summary>
	[TestMethod]
	public void CloneReferenceTypeProperty()
	{
		AssertTypeLibraryReady( typeof( ComponentWithListProperty ) );

		using var scope = new Scene().Push();

		var source = new GameObject( true, "Source" );
		var sourceComp = source.AddComponent<ComponentWithListProperty>();

		sourceComp.List = [1, 2, 3];

		var clone = source.Clone();
		var cloneComp = clone.GetComponent<ComponentWithListProperty>();

		Assert.IsNotNull( cloneComp?.List );

		// Critical test: lists can't be the same reference
		Assert.AreNotSame( sourceComp.List, cloneComp!.List );

		Assert.AreEqual( sourceComp.List.Count, cloneComp.List.Count );
		Assert.AreEqual( sourceComp.List[1], cloneComp.List[1] );
	}

	/// <summary>
	/// Cloned components can contain user type properties, which themselves can contain
	/// <see cref="GameObject"/> references.
	/// </summary>
	/// <param name="selfReference">
	/// If true, the cloned object contains a reference to itself. Otherwise, it references
	/// an external object in the scene.
	/// </param>
	[TestMethod]
	[DataRow( true ), DataRow( false )]
	public void CloneUserTypeWithReference( bool selfReference )
	{
		AssertTypeLibraryReady( typeof( ComponentWithNestedReference ) );

		using var scope = new Scene().Push();

		var source = new GameObject( true, "Source" );
		var sourceComp = source.AddComponent<ComponentWithNestedReference>();

		var referenced = selfReference ? source : new GameObject( true, "Referenced" );

		sourceComp.UserType = new UserTypeWithReference( referenced );

		var clone = source.Clone();
		var cloneComp = clone.GetComponent<ComponentWithNestedReference>();

		var expectedReference = selfReference ? clone : referenced;

		Assert.AreSame( expectedReference, cloneComp?.UserType?.Reference );
	}

	/// <summary>
	/// LineRenders use a list of <see cref="GameObject"/> references. When cloning a LineRender,
	/// we need to ensure that the cloned object references the correct <see cref="GameObject"/> instances.
	/// </summary>
	/// <param name="selfReference">
	/// If true, the cloned object contains a reference to itself. Otherwise, it references
	/// an external object in the scene.
	/// </param>
	[TestMethod]
	[DataRow( true ), DataRow( false )]
	public void CloneLineRendererWithReferenceInPoints( bool selfReference )
	{
		AssertTypeLibraryReady( typeof( ComponentWithNestedReference ) );

		using var scope = new Scene().Push();

		var source = new GameObject( true, "Source" );
		var sourceComp = source.AddComponent<LineRenderer>();
		sourceComp.Points = new();

		var referenced = selfReference ? source : new GameObject( true, "Referenced" );

		sourceComp.Points.Add( referenced );

		var clone = source.Clone();
		var cloneComp = clone.GetComponent<LineRenderer>();

		var expectedReference = selfReference ? clone : referenced;

		Assert.AreSame( expectedReference, cloneComp?.Points?.FirstOrDefault() );
	}

	/// <summary>
	/// If we clone a Component with a property using [RequireComponent].
	/// The cloned Go should also have a reference to the cloned counterpart of the required component.
	/// Even if the property is not marked with [Property] or [JsonInclude].
	/// </summary>
	[TestMethod]
	public void CloneRquiredComponentProperty()
	{
		AssertTypeLibraryReady( typeof( ModelRenderer ), typeof( ComponentIdTest ) );

		using var scope = new Scene().Push();

		var original = new GameObject();
		original.Name = "My Game Object";

		var originalComp = original.Components.Create<ComponentWithRequiredComponent>();

		var clone = original.Clone();

		var cloneComp = clone.GetComponent<ComponentWithRequiredComponent>();

		Assert.IsNotNull( cloneComp );
		Assert.AreNotEqual( originalComp.Id, cloneComp.Id );
	}

	/// <summary>
	/// There was a regression when cloning a list of user defined objects, if the object had a property that was named "Prefab".
	/// This could lead to a deserilziation issue in UpdateClonedIdsInJson when trying to rewire the GameObjectReferences.
	/// https://github.com/Facepunch/sbox-public/issues/2480
	/// </summary>
	[TestMethod]
	public void CloneComponentWithPrefabList()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var prefabBasic = Prefab.Prefabs.BasicPrefab;

		var go = scene.CreateObject();

		go.Components.Create<ComponentWithPrefabList>();
		go.Components.Get<ComponentWithPrefabList>().Decorations.Add( new ComponentWithPrefabList.PrafbEntry() { Prefab = prefabBasic } );

		var clone = go.Clone();

		Assert.AreEqual( 1, clone.Components.Get<ComponentWithPrefabList>().Decorations.Count );

		var clonedPrefabRef = clone.Components.Get<ComponentWithPrefabList>().Decorations[0].Prefab;
		Assert.AreEqual( prefabBasic, clonedPrefabRef );
	}

	/// <summary>
	/// Cloned components can contain delegates implemented as action graphs. These delegates contain a
	/// reference to the <see cref="GameObject"/> that contains them, equivalent to a "this" parameter.
	/// Clones of these delegates need to be retargeted to the cloned object.
	/// </summary>
	[TestMethod]
	public void CloneActionGraphProperty()
	{
		AssertTypeLibraryReady( typeof( ComponentWithActionGraph ) );

		using var sceneScope = new Scene().Push();

		var source = new GameObject( true, "Source" );
		var sourceComp = source.AddComponent<ComponentWithActionGraph>();

		var action = CreateActionGraphDelegateWithTarget<Func<string>>( source );
		var graph = action.Graph;

		sourceComp.Func = action.Delegate;

		RewireGraphToReturnNameOfObject( graph.TargetOutput! );

		var clone = source.Clone( Transform.Zero, name: "Clone" );
		var cloneComp = clone.GetComponent<ComponentWithActionGraph>();

		var cloneAction = cloneComp.Func.GetActionGraphInstance();

		// Clone should be re-using the same graph instance...
		Assert.AreSame( graph, cloneAction!.Graph );

		// ...but should return its own name
		Assert.AreEqual( "Source", sourceComp.Func() );
		Assert.AreEqual( "Clone", cloneComp.Func() );
	}

	/// <summary>
	/// Cloned components can contain delegates implemented as action graphs. These graphs can contain
	/// GameObject / Component references (scene.ref nodes), which should be accounted for when cloning the GameObject
	/// containing the graphs. We should handle them the same as GameObject references in Component properties.
	/// </summary>
	/// <param name="selfReference">
	/// If true, the cloned object contains a reference to itself. Otherwise, it references an external object in the scene.
	/// </param>
	[TestMethod]
	[DataRow( true ), DataRow( false )]
	public void CloneActionGraphSceneReference( bool selfReference )
	{
		AssertTypeLibraryReady( typeof( ComponentWithActionGraph ) );

		using var sceneScope = new Scene().Push();

		var source = new GameObject( true, "Source" );
		var sourceComp = source.AddComponent<ComponentWithActionGraph>();

		var referenced = selfReference ? source : new GameObject( true, "Referenced" );

		var action = CreateActionGraphDelegateWithTarget<Func<string>>( source );
		var graph = action.Graph;

		sourceComp.Func = action.Delegate;

		// Create a scene.ref node that references a GameObject

		var sceneRef = graph.AddNode( "scene.ref" );

		sceneRef.Properties["gameobject"].Value = GameObjectReference.FromInstance( referenced );

		RewireGraphToReturnNameOfObject( sceneRef.Outputs.Result );

		var clone = source.Clone( Transform.Zero, name: "Clone" );
		var cloneComp = clone.GetComponent<ComponentWithActionGraph>();

		var expectedReference = selfReference ? clone : referenced;

		var cloneAction = cloneComp.Func.GetActionGraphInstance();

		// Clone should be re-using the same graph instance...
		Assert.AreSame( graph, cloneAction!.Graph );

		// ...but should return the correct name of the referenced object
		Assert.AreEqual( referenced.Name, sourceComp.Func() );
		Assert.AreEqual( expectedReference.Name, cloneComp.Func() );
	}

	#endregion

	[TestMethod]
	public void SerializeWithComponentIds()
	{
		AssertTypeLibraryReady( typeof( ModelRenderer ), typeof( ComponentIdTest ) );

		using var scope = new Scene().Push();

		var go1 = new GameObject();
		go1.Name = "My Game Object";

		var go1comp1 = go1.Components.Create<ComponentIdTest>();
		var go1comp2 = go1.Components.Create<ComponentIdTest>();
		var go1comp3 = go1.Components.Create<ComponentIdTest>();

		go1comp1.Other = go1comp3;
		go1comp2.Other = go1comp1;
		go1comp3.Other = go1comp2;

		var node = go1.Serialize();

		System.Console.WriteLine( node );
		SceneUtility.MakeIdGuidsUnique( node );

		var go2 = new GameObject();
		go2.Deserialize( node );

		var go2comps = go2.Components.GetAll<ComponentIdTest>().ToArray();
		Assert.AreEqual( 3, go2comps.Length );

		var go2comp1 = go2comps[0];
		var go2comp2 = go2comps[1];
		var go2comp3 = go2comps[2];

		Assert.IsNotNull( go2comp1 );
		Assert.IsNotNull( go2comp2 );
		Assert.IsNotNull( go2comp3 );

		Assert.AreEqual( go2comp1.Id, go2comp1.Id );
		Assert.AreEqual( go2comp2.Id, go2comp2.Id );
		Assert.AreEqual( go2comp3.Id, go2comp3.Id );

		Assert.IsNotNull( go2comp1.Other );
		Assert.IsNotNull( go2comp2.Other );
		Assert.IsNotNull( go2comp3.Other );

		Assert.AreEqual( go2comp3.Id, go2comp1.Other.Id );
		Assert.AreEqual( go2comp1.Id, go2comp2.Other.Id );
		Assert.AreEqual( go2comp2.Id, go2comp3.Other.Id );
	}

	[TestMethod]
	public void SerializeWithChildren()
	{
		using var scope = new Scene().Push();

		var timer = new ScopeTimer( "Creation" );
		var go1 = new GameObject();
		go1.Name = "My Game Object";
		go1.LocalTransform = new Transform( Vector3.Up, Rotation.Identity, 10 );

		int childrenCount = 15000;

		for ( int i = 0; i < childrenCount; i++ )
		{
			var child = new GameObject();
			child.Name = $"Child {i}";
			child.LocalTransform = new Transform( Vector3.Random * 1000 );
			child.Parent = go1;

			child.Components.Create<ModelRenderer>();
		}

		timer.Dispose();
		timer = new ScopeTimer( "Serialize" );

		var node = go1.Serialize();


		timer.Dispose();

		using ( new ScopeTimer( "MakeGameObjectsUnique" ) )
		{
			SceneUtility.MakeIdGuidsUnique( node );
		}

		timer = new ScopeTimer( "Deserialize" );
		//System.Console.WriteLine( node );

		var go2 = new GameObject();
		go2.Deserialize( node );

		timer.Dispose();

		Assert.AreNotEqual( go1.Id, go2.Id );
		Assert.AreEqual( go1.Name, go2.Name );
		Assert.AreEqual( go1.Enabled, go2.Enabled );
		Assert.AreEqual( go1.LocalTransform, go2.LocalTransform );
		Assert.AreEqual( go2.Children.Count, childrenCount );
	}

	[TestMethod]
	public void VersionedComponent()
	{
		AssertTypeLibraryReady( typeof( OldVersioningComponent ), typeof( NewVersioningComponent ) );

		var scene = new Scene();

		using var scope = scene.Push();

		var go1 = new GameObject();
		go1.Name = "My Game Object";
		go1.LocalTransform = new Transform( Vector3.Up, Rotation.Identity, 10 );

		var versioning = go1.Components.Create<OldVersioningComponent>( true );
		versioning.OldProperty = "My Value";

		var node = go1.Serialize();

		System.Console.WriteLine( "Before adjusting JSON" );
		System.Console.WriteLine( node );

		// This json parsing bit is a bit of a nightmare to work with
		// But basically all I'm doing is changing the type of the first component (defined above) to NewVersioningComponent
		// Then we can test a proper upgrade cycle.

		node.Remove( "Components", out var componentArrayNode );
		var componentNode = componentArrayNode as JsonArray;

		var versioningComponentNode = componentNode.First();
		versioningComponentNode["__type"] = "NewVersioningComponent";

		// Replace the first component array node with our new node.
		// I'm using RemoveAt and Add because you can't use the array indexer on JsonObjects otherwise 
		// it'll cry about the node already having a parent.
		componentNode.RemoveAt( 0 );
		componentNode.Add( versioningComponentNode );

		// Replace the component array too.
		node["Components"] = componentNode;

		SceneUtility.MakeIdGuidsUnique( node );

		var go2 = new GameObject();
		go2.Deserialize( node );

		var node2 = go2.Serialize();

		System.Console.WriteLine( node2 );

		var go2ComponentNode = node2["Components"] as JsonArray;
		var go2versioningComponentNode = go2ComponentNode.First();

		Assert.IsTrue( go2versioningComponentNode["OldProperty"] is null );
		Assert.IsTrue( go2versioningComponentNode["MyProperty"].ToString() == "My Value" );
	}

	[TestMethod]
	public void DeserializeMissingFieldShouldRespectCodeInitializer()
	{
		AssertTypeLibraryReady( typeof( ComponentWithInitializer ) );

		using var scope = new Scene().Push();

		var go1 = new GameObject();
		go1.Name = "My Game Object";
		go1.LocalTransform = new Transform( Vector3.Up, Rotation.Identity, 10 );

		var go1comp1 = go1.Components.Create<ComponentWithInitializer>();

		// JSON with missing fields
		var node = go1.Serialize();

		node["Components"].AsArray()[0].AsObject().Remove( "ReferenceTypeWithIntializer" );
		node["Components"].AsArray()[0].AsObject().Remove( "ValueTypeWithIntializer" );
		node["Components"].AsArray()[0].AsObject().Remove( "TypeWithCustomJsonPopulator" );

		System.Console.WriteLine( node );
		SceneUtility.MakeIdGuidsUnique( node );

		var go2 = new GameObject();
		go2.Deserialize( node );

		var go2comp1 = go2.Components.Get<ComponentWithInitializer>();

		// Both the comp created via code and the one created from json should have the
		// code defined initializer set
		Assert.AreEqual( ComponentWithInitializer.TestReferenceType, go1comp1.ReferenceTypeWithIntializer );
		Assert.AreEqual( ComponentWithInitializer.TestReferenceType, go2comp1.ReferenceTypeWithIntializer );

		Assert.AreEqual( ComponentWithInitializer.TestValueType, go1comp1.ValueTypeWithIntializer );
		Assert.AreEqual( ComponentWithInitializer.TestValueType, go2comp1.ValueTypeWithIntializer );

		Assert.AreEqual( ComponentWithInitializer.TestRotation, go1comp1.TypeWithCustomJsonPopulator );
		Assert.AreEqual( ComponentWithInitializer.TestRotation, go2comp1.TypeWithCustomJsonPopulator );
	}

	[TestMethod]
	public void DeserializeNullFields()
	{
		AssertTypeLibraryReady( typeof( ComponentWithInitializer ) );

		using var scope = new Scene().Push();

		var go1 = new GameObject();
		go1.Name = "My Game Object";
		go1.LocalTransform = new Transform( Vector3.Up, Rotation.Identity, 10 );

		var go1comp1 = go1.Components.Create<ComponentWithInitializer>();
		go1comp1.ReferenceTypeWithIntializer = null;

		var node = go1.Serialize();

		System.Console.WriteLine( node );
		SceneUtility.MakeIdGuidsUnique( node );

		var go2 = new GameObject();
		go2.Deserialize( node );

		var go2comp1 = go2.Components.Get<ComponentWithInitializer>();

		// Field should be set to null after deserialize
		Assert.AreEqual( null, go1comp1.ReferenceTypeWithIntializer );
	}

	[TestMethod]
	public void DeserializeNullFieldsIntoExistingObject()
	{
		AssertTypeLibraryReady( typeof( ComponentWithInitializer ) );

		using var scope = new Scene().Push();

		var go1 = new GameObject();
		go1.Name = "My Game Object";
		go1.LocalTransform = new Transform( Vector3.Up, Rotation.Identity, 10 );

		var go1comp1 = go1.Components.Create<ComponentWithInitializer>();
		go1comp1.ReferenceTypeWithIntializer = null;

		var node = go1.Serialize();

		System.Console.WriteLine( node );
		SceneUtility.MakeIdGuidsUnique( node );

		var go2 = new GameObject();
		go2.Name = "My Game Object";
		go2.LocalTransform = new Transform( Vector3.Up, Rotation.Identity, 10 );

		var go2comp2 = go1.Components.Create<ComponentWithInitializer>();
		// make sure field is not null
		go2comp2.ReferenceTypeWithIntializer = ComponentWithInitializer.TestReferenceType;

		// desserialize data with null field into existing object
		go2.Deserialize( node );

		go2comp2 = go2.Components.Get<ComponentWithInitializer>();

		// Should now be null
		Assert.AreEqual( null, go2comp2.ReferenceTypeWithIntializer );
	}

	[TestMethod]
	public void DeserializeEventOrder()
	{
		AssertTypeLibraryReady( typeof( OrderTestComponent ) );

		JsonObject SceneSerialized = null;

		{
			var scene = new Scene();
			using var sceneScope = scene.Push();

			var go = scene.CreateObject();
			var o = go.Components.Create<OrderTestComponent>( false );

			SceneSerialized = scene.Serialize();
		}

		{
			var scene = new Scene();
			using var sceneScope = scene.Push();

			scene.Deserialize( SceneSerialized );

			var o = scene.GetAllObjects( true ).Select( x => x.GetComponent<OrderTestComponent>( true ) ).Where( x => x.IsValid() ).First();

			Assert.AreEqual( 1, o.AwakeCalls );
			Assert.AreEqual( 0, o.EnabledCalls );
			Assert.AreEqual( 0, o.DisabledCalls );
		}
	}

	[TestMethod]
	public void SerializesInheritedProperties()
	{
		using var scope = new Scene().Push();

		var go1 = new GameObject();
		go1.Name = "GameObject With Inherited Property Component";
		var baseClassComponent = go1.Components.Create<InheritedPropertyComponentBase>();
		baseClassComponent.SetPrivateFlag( true );

		var derivedComponent = go1.Components.Create<InheritedPropertyComponent>();
		derivedComponent.SetPrivateFlag( true );

		var node1 = go1.Serialize();

		var componentsArray1 = node1["Components"].AsArray();
		var baseComponentJson = componentsArray1[0].AsObject();
		var derivedComponentJson = componentsArray1[1].AsObject();

		Assert.IsTrue( baseComponentJson.GetPropertyValue( "Flag", false ) );
		Assert.IsTrue( derivedComponentJson.GetPropertyValue( "Flag", false ) );

		SceneUtility.MakeIdGuidsUnique( node1 );

		// Hack: pretend we're from dynamic assembly so that setting private
		// properties will be allowed.
		var type1 = Game.TypeLibrary.GetType<InheritedPropertyComponentBase>();
		var type2 = Game.TypeLibrary.GetType<InheritedPropertyComponent>();
		type1.IsDynamicAssembly = true;
		type2.IsDynamicAssembly = true;

		var go2 = new GameObject();
		go2.Deserialize( node1 );

		var baseComp1 = go2.GetComponent<InheritedPropertyComponentBase>();
		var baseComp2 = go2.GetComponent<InheritedPropertyComponent>();

		Assert.IsTrue( baseComp1.Flag );
		Assert.IsTrue( baseComp2.Flag );
	}

	[TestMethod]
	public void SerializesComponentEnabledFlag()
	{
		AssertTypeLibraryReady( typeof( ModelRenderer ) );

		using var scope = new Scene().Push();

		var go1 = new GameObject();
		go1.Name = "Game Object With Enabled and Disabled Component";
		var enabledComponent = go1.Components.Create<ModelRenderer>( true );
		var disabledComponent = go1.Components.Create<ModelRenderer>( false );

		// Ensure the component is enabled
		Assert.IsTrue( enabledComponent.Enabled );

		var node1 = go1.Serialize();

		// Verify enabled flag is in JSON and set to true
		var componentsArray1 = node1["Components"].AsArray();
		var componentJson1 = componentsArray1[0].AsObject();
		Assert.IsTrue( componentJson1.ContainsKey( Component.JsonKeys.Enabled ), "Enabled flag missing for enabled component" );
		Assert.AreEqual( true, componentJson1[Component.JsonKeys.Enabled].GetValue<bool>() );

		// Ensure the component is disabled
		Assert.IsFalse( disabledComponent.Enabled );

		// Verify enabled flag is in JSON and set to false
		var componentsArray2 = node1["Components"].AsArray();
		var componentJson2 = componentsArray2[1].AsObject();
		Assert.IsTrue( componentJson2.ContainsKey( Component.JsonKeys.Enabled ), "Enabled flag missing for disabled component" );
		Assert.AreEqual( false, componentJson2[Component.JsonKeys.Enabled].GetValue<bool>() );
	}

	#region Action Graph Helpers

	/// <summary>
	/// Creates an action graph that implements a delegate of type <typeparamref name="T"/>, which has
	/// a target parameter that references <paramref name="target"/> (like a "this" parameter in C#).
	/// </summary>
	private ActionGraphDelegate<T> CreateActionGraphDelegateWithTarget<T>( GameObject target )
		where T : Delegate
	{
		var graph = ActionGraph.CreateEmpty( NodeLibrary );

		// Copy parameters from T, but include a target parameter that defaults to the target game object
		graph.SetParameters( NodeBinding.FromDelegateType( typeof( T ), NodeLibrary )
			.With( InputDefinition.Target( typeof( GameObject ), target ) ) );

		// Add input (entry point) / output (return) nodes to the graph
		graph.AddRequiredNodes();

		return graph.CreateDelegate<T>();
	}

	/// <summary>
	/// Given <paramref name="gameObjectOutput"/>, which is a value output in an <see cref="ActionGraph"/>,
	/// rewire the graph so it returns the name of whatever that game object is.
	/// </summary>
	private void RewireGraphToReturnNameOfObject( Node.Output gameObjectOutput )
	{
		var graph = gameObjectOutput.Node.ActionGraph;

		// Create node to get the name of whatever GameObject is in gameObjectOutput

		var getName = graph.AddNode( NodeLibrary.Property );

		getName.Properties.Type.Value = typeof( GameObject );
		getName.Properties.Name.Value = nameof( GameObject.Name );
		getName.Inputs.Target.SetLink( gameObjectOutput );

		// Return that name right after the graph entry point node (InputNode)

		graph.PrimaryOutputNode!.Inputs.Signal.SetLink( graph.InputNode!.Outputs.Signal );
		graph.PrimaryOutputNode.Inputs.Result.SetLink( getName.Outputs.Result );
	}

	#endregion
}

public class InheritedPropertyComponentBase : Component
{
	[Sandbox.Property] public bool Flag { get; private set; }

	public void SetPrivateFlag( bool value )
	{
		Flag = value;
	}
}

public sealed class InheritedPropertyComponent : InheritedPropertyComponentBase
{
}

public class ComponentIdTest : Component
{
	[Sandbox.Property] public ComponentIdTest Other { get; set; }
}

public class OldVersioningComponent : Component
{
	[Sandbox.Property] public string OldProperty { get; set; }

	public override int ComponentVersion => 0;
}

public class NewVersioningComponent : Component
{
	[Sandbox.Property] public string MyProperty { get; set; }

	public override int ComponentVersion => 1;

	[JsonUpgrader( typeof( NewVersioningComponent ), 1 )]
	public static void MyPropertyUpgrade( JsonObject json )
	{
		System.Console.WriteLine( "Running MyPropertyUpgrade" );

		if ( json.Remove( "OldProperty", out var newNode ) )
		{
			json["MyProperty"] = newNode;
		}
		else
		{
			Assert.IsTrue( false, "Couldn't remove OldProperty from OldVersioningComponent data. Fix this!" );
		}
	}
}

public class ComponentWithInitializer : Component
{
	public static string TestReferenceType = "intialized";

	public static int TestValueType = 42;

	public static Rotation TestRotation = Rotation.Identity; // 0,0,0,1

	[Sandbox.Property] public string ReferenceTypeWithIntializer = TestReferenceType;

	[Sandbox.Property] public int ValueTypeWithIntializer = TestValueType;

	[Sandbox.Property] public Rotation TypeWithCustomJsonPopulator = TestRotation;
}

public class ComponentWithListProperty : Component
{
	[Sandbox.Property] public List<int> List { get; set; }
}

public record UserTypeWithReference( GameObject Reference );

public class ComponentWithNestedReference : Component
{
	[Sandbox.Property]
	public UserTypeWithReference UserType { get; set; }
}

public class ComponentWithActionGraph : Component
{
	[Sandbox.Property]
	public Func<string> Func { get; set; }
}
public class ComponentWithRequiredComponent : Component
{
	[RequireComponent]
	public ComponentIdTest RequiredComponent { get; set; }
}


// https://github.com/Facepunch/sbox-public/issues/2480
public class ComponentWithPrefabList : Component
{
	[Sandbox.Property] public readonly List<PrafbEntry> Decorations = new();
	public class PrafbEntry
	{
		// Variables called Prefab caused issues when remapping prefabs to gameobject ids during cloning
		[KeyProperty]
		public GameObject Prefab { get; set; }

		[Range( 0, 1 ), KeyProperty]
		public float Probability { get; set; } = 1;
	}
}


