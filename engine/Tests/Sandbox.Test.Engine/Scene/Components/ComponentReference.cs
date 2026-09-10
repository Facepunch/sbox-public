using System;
using System.Text.Json.Nodes;

namespace SceneTests.Components;

/// <summary>
/// Tests for how components serialize as references (Component.Serialize.cs
/// JsonRead/JsonWrite and Component.Reference.cs): a component-typed property
/// is written as a small reference object and resolved back through the scene
/// directory, with fallbacks via the owning GameObject and the legacy
/// plain-guid format. Also covers deterministic id changes (Component.Id.cs).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ComponentReferenceTest : SceneTest
{
	/// <summary>
	/// Writing a component as JSON produces a reference object carrying the
	/// component id, the owning GameObject id and the component type name.
	/// </summary>
	[TestMethod]
	public void JsonWriteEmitsReferenceObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<RefTargetComponent>();

		var node = Json.ToNode( comp, typeof( Component ) ) as JsonObject;

		Assert.IsNotNull( node );
		Assert.AreEqual( "component", (string)node["_type"] );
		Assert.AreEqual( comp.Id, (Guid)node["component_id"] );
		Assert.AreEqual( go.Id, (Guid)node["go"] );
		Assert.AreEqual( Game.TypeLibrary.GetType( typeof( RefTargetComponent ) ).ClassName, (string)node["component_type"] );
	}

	/// <summary>
	/// A destroyed component serializes as JSON null instead of a dangling
	/// reference.
	/// </summary>
	[TestMethod]
	public void JsonWriteDestroyedComponentIsNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<RefTargetComponent>();
		comp.Destroy();

		var node = Json.ToNode( comp, typeof( Component ) );

		Assert.IsNull( node );
	}

	/// <summary>
	/// Reading a reference object back resolves to the exact same component
	/// instance via the scene directory.
	/// </summary>
	[TestMethod]
	public void JsonReadResolvesByComponentId()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<RefTargetComponent>();

		var node = Json.ToNode( comp, typeof( Component ) );
		var resolved = Json.FromNode<RefTargetComponent>( node );

		Assert.AreSame( comp, resolved );
	}

	/// <summary>
	/// When the component id is stale, resolution falls back to looking up the
	/// owning GameObject and finding a component of the referenced type on it.
	/// </summary>
	[TestMethod]
	public void JsonReadFallsBackToGameObjectLookup()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<RefTargetComponent>();

		var node = new JsonObject
		{
			["_type"] = "component",
			["component_id"] = Guid.NewGuid(), // unknown id
			["go"] = go.Id,
			["component_type"] = Game.TypeLibrary.GetType( typeof( RefTargetComponent ) ).ClassName
		};

		var resolved = Json.FromNode<RefTargetComponent>( node );

		Assert.AreSame( comp, resolved );
	}

	/// <summary>
	/// The legacy serialized form - a bare GameObject guid instead of a
	/// reference object - still resolves to the component of the target type on
	/// that object.
	/// </summary>
	[TestMethod]
	public void JsonReadResolvesLegacyGuidForm()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<RefTargetComponent>();

		var resolved = Json.FromNode<RefTargetComponent>( JsonValue.Create( go.Id ) );

		Assert.AreSame( comp, resolved );
	}

	/// <summary>
	/// FromInstance captures the component id, owning object id and class name,
	/// the type resolves back to the CLR type, and the explicit conversion to a
	/// GameObjectReference keeps the owning object id.
	/// </summary>
	[TestMethod]
	public void FromInstanceCapturesIdentity()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<RefTargetComponent>();

		var reference = ComponentReference.FromInstance( comp );

		Assert.AreEqual( comp.Id, reference.ComponentId );
		Assert.AreEqual( go.Id, reference.GameObjectId );
		Assert.AreEqual( Game.TypeLibrary.GetType( typeof( RefTargetComponent ) ).ClassName, reference.ComponentTypeName );
		Assert.AreEqual( typeof( RefTargetComponent ), reference.ResolveComponentType() );

		Assert.AreSame( comp, reference.Resolve( scene ) );

		// the parameterless overload resolves against the pushed active scene
		Assert.AreSame( comp, reference.Resolve() );

		var goReference = (GameObjectReference)reference;
		Assert.AreEqual( go.Id, goReference.GameObjectId );
	}

	/// <summary>
	/// A reference whose component and GameObject are both unknown resolves to
	/// null rather than throwing - this happens legitimately over the network.
	/// </summary>
	[TestMethod]
	public void ResolveReturnsNullForUnknownGameObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var node = new JsonObject
		{
			["_type"] = "component",
			["component_id"] = Guid.NewGuid(),
			["go"] = Guid.NewGuid()
		};

		var reference = Json.FromNode<ComponentReference>( node );

		Assert.IsNull( reference.Resolve( scene ) );
	}

	/// <summary>
	/// When the owning GameObject exists but doesn't carry a component of the
	/// referenced type, resolution throws instead of silently returning the
	/// wrong component.
	/// </summary>
	[TestMethod]
	public void ResolveThrowsWhenComponentMissingOnObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<RefTargetComponent>();

		var node = new JsonObject
		{
			["_type"] = "component",
			["go"] = go.Id,
			["component_type"] = Game.TypeLibrary.GetType( typeof( RefOtherComponent ) ).ClassName
		};

		var reference = Json.FromNode<ComponentReference>( node );

		Assert.ThrowsException<Exception>( () => reference.Resolve( scene ) );
	}

	/// <summary>
	/// A reference object of the wrong kind (e.g. a gameobject reference being
	/// read as a component) throws on resolve.
	/// </summary>
	[TestMethod]
	public void ResolveThrowsForWrongReferenceType()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var node = new JsonObject
		{
			["_type"] = "gameobject"
		};

		var reference = Json.FromNode<ComponentReference>( node );

		Assert.ThrowsException<Exception>( () => reference.Resolve( scene ) );
	}

	/// <summary>
	/// A component reference with no ids at all cannot be resolved and throws.
	/// </summary>
	[TestMethod]
	public void ResolveThrowsForEmptyReference()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var node = new JsonObject
		{
			["_type"] = "component"
		};

		var reference = Json.FromNode<ComponentReference>( node );

		Assert.ThrowsException<Exception>( () => reference.Resolve( scene ) );
	}

	/// <summary>
	/// SetDeterministicId re-registers the component in the scene directory
	/// under the new guid - the old guid no longer resolves.
	/// </summary>
	[TestMethod]
	public void SetDeterministicIdReindexesDirectory()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<RefTargetComponent>();

		var oldId = comp.Id;
		var newId = Guid.NewGuid();

		comp.SetDeterministicId( newId );

		Assert.AreEqual( newId, comp.Id );
		Assert.AreSame( comp, scene.Directory.FindComponentByGuid( newId ) );
		Assert.IsNull( scene.Directory.FindComponentByGuid( oldId ) );
	}
}

/// <summary>
/// Plain component used as the target of component references.
/// </summary>
public class RefTargetComponent : Component
{
}

/// <summary>
/// Component type that is registered in the TypeLibrary but never added to the
/// test objects, used to exercise failed type lookups on resolve.
/// </summary>
public class RefOtherComponent : Component
{
}
