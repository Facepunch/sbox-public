using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using Sandbox.Internal;
using System;
using System.Collections.Generic;

namespace SceneTests.GameObjects;

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
		TypeLibrary.AddAssembly( typeof( ComponentIdTest ).Assembly, false );
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
		AssertTypeLibraryReady( typeof( LineRenderer ) );

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
	/// There was a regression when cloning a list of user defined objects, if the object had a property that was named "Prefab".
	/// This could lead to a deserilziation issue in UpdateClonedIdsInJson when trying to rewire the GameObjectReferences.
	/// https://github.com/Facepunch/sbox-public/issues/2480
	/// </summary>
	[TestMethod]
	public void CloneComponentWithPrefabList()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var prefabBasic = SceneTests.Prefab.PrefabTest.BasicPrefab;

		var go = scene.CreateObject();

		go.Components.Create<ComponentWithPrefabList>();
		go.Components.Get<ComponentWithPrefabList>().Decorations.Add( new ComponentWithPrefabList.PrafbEntry() { Prefab = prefabBasic } );

		var clone = go.Clone();

		Assert.AreEqual( 1, clone.Components.Get<ComponentWithPrefabList>().Decorations.Count );

		var clonedPrefabRef = clone.Components.Get<ComponentWithPrefabList>().Decorations[0].Prefab;
		Assert.AreEqual( prefabBasic, clonedPrefabRef );
	}

	#endregion
}

public class ComponentIdTest : Component
{
	[Sandbox.Property] public ComponentIdTest Other { get; set; }
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


