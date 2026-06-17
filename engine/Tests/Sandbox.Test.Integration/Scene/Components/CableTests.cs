using System;

namespace SceneTests.Components;

/// <summary>
/// Pins CableComponent and CableNodeComponent: the cable polls its CableNodeComponent
/// children every update and regenerates a PolygonMesh on a sibling MeshComponent,
/// node data (position / radius scale / roll) feeds the parent cable, property setters
/// clamp and mark the mesh dirty, and legacy ControlPoints migrate to node children.
/// The render model only builds inside MeshComponent.OnEnabled with a mesh assigned,
/// but in the headless test host the native render mesh cannot be created (the same
/// GPU limitation RendererTests pins for SpriteRenderer) and the CallbackBatch swallows
/// the failure - so the cable stays pure mesh data in every runtime path here: Model,
/// scene object and physics shapes never appear.
/// </summary>
[TestClass]
public class CableComponentTest
{
	/// <summary>
	/// Creates a child GameObject of the given cable object at a local position and
	/// gives it a CableNodeComponent, mirroring what the editor cable tool produces.
	/// </summary>
	static GameObject CreateNode( Scene scene, GameObject cable, Vector3 localPosition )
	{
		var node = scene.CreateObject();
		node.SetParent( cable, false );
		node.LocalPosition = localPosition;
		node.Components.Create<CableNodeComponent>();
		return node;
	}

	/// <summary>
	/// Finds the scene object that was created by the given component, using the
	/// internal SceneObject.Component back-reference. Returns null when the component
	/// has no live scene object.
	/// </summary>
	static T FindSceneObjectFor<T>( Scene scene, Component component ) where T : SceneObject
	{
		return scene.SceneWorld.SceneObjects.OfType<T>().FirstOrDefault( x => x.Component == component );
	}

	/// <summary>
	/// Serializes a GameObject to json, destroys the original, then deserializes the json
	/// back into the scene and enables it - the standard save/load round trip idiom used
	/// by the integration tests.
	/// </summary>
	static GameObject SerializeRoundTrip( Scene scene, GameObject go )
	{
		var json = go.Serialize().ToJsonString();

		go.Destroy();
		scene.ProcessDeletes();

		var jsonObject = Json.ParseToJsonObject( json );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var clone = new GameObject( false );
		clone.Deserialize( jsonObject );
		clone.Enabled = true;

		return clone;
	}

	/// <summary>
	/// Ticks the scene until the condition holds. The cable throttles rebuilds on
	/// wall-clock RealTimeSince timers (a 0.03s preview throttle and a 0.08s edit-settle
	/// window), so a few real milliseconds must pass; the loop is bounded by a stopwatch
	/// so a broken condition fails fast instead of hanging.
	/// </summary>
	static void TickUntil( Scene scene, Func<bool> condition, string because )
	{
		var timer = System.Diagnostics.Stopwatch.StartNew();
		while ( !condition() && timer.ElapsedMilliseconds < 4000 )
		{
			scene.GameTick();
		}

		Assert.IsTrue( condition(), because );
	}

	/// <summary>
	/// Ticks the scene until the cable assigns a new PolygonMesh instance to its
	/// MeshComponent - every rebuild creates a fresh mesh, so a reference change is
	/// the regeneration signal - and returns the new mesh.
	/// </summary>
	static PolygonMesh TickUntilRebuilt( Scene scene, MeshComponent meshComponent, PolygonMesh previous )
	{
		TickUntil( scene, () => !ReferenceEquals( meshComponent.Mesh, previous ), "The cable should regenerate its mesh" );
		return meshComponent.Mesh;
	}

	/// <summary>
	/// A cable with two node children generates its mesh on the first update: it creates
	/// a MeshComponent sibling tagged "world" holding a fast-preview PolygonMesh - the
	/// first build always runs in editing mode, so path detail is capped at 1, giving
	/// 3 rings of 8 vertices and 16 quad faces with no end caps - whose bounds span the
	/// node positions plus the default 8 unit radius. Because the MeshComponent was
	/// created (and thus enabled) before the mesh was assigned, and the render mesh only
	/// builds inside OnEnabled outside the editor, the runtime-generated cable is pure
	/// data: no render Model, no scene object and no physics shapes exist.
	/// </summary>
	[TestMethod]
	public void GeneratesMeshDataOnFirstUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<CableComponent>();
		CreateNode( scene, go, new Vector3( 0, 0, 0 ) );
		CreateNode( scene, go, new Vector3( 100, 0, 0 ) );

		Assert.IsNull( go.Components.Get<MeshComponent>( true ), "No mesh exists before the first update" );

		scene.GameTick();

		var meshComponent = go.Components.Get<MeshComponent>( true );
		Assert.IsNotNull( meshComponent, "The first update should create a MeshComponent" );
		Assert.IsTrue( meshComponent.Enabled, "Two nodes produce a mesh, so the component stays enabled" );
		Assert.IsTrue( go.Tags.Has( "world" ), "MeshComponent tags its GameObject with 'world'" );

		var mesh = meshComponent.Mesh;
		Assert.IsNotNull( mesh, "The generated PolygonMesh should be assigned" );
		Assert.AreEqual( 24, mesh.VertexHandles.Count(), "Fast preview: 3 path rings x 8 sides" );
		Assert.AreEqual( 16, mesh.FaceHandles.Count(), "Fast preview: 2 segments x 8 sides, no caps" );

		var bounds = mesh.CalculateBounds();
		Assert.IsTrue( bounds.Mins.Distance( new Vector3( 0, -8, -8 ) ) < 0.01f, $"Bounds mins should be the start node minus the radius, got {bounds.Mins}" );
		Assert.IsTrue( bounds.Maxs.Distance( new Vector3( 100, 8, 8 ) ) < 0.01f, $"Bounds maxs should be the end node plus the radius, got {bounds.Maxs}" );

		Assert.IsNull( meshComponent.Model, "Outside the editor the render model only builds on enable, which ran before the mesh existed" );
		Assert.AreEqual( 0, meshComponent.Shapes.Count, "No model means no physics shapes" );
		Assert.IsNull( FindSceneObjectFor<SceneObject>( scene, meshComponent ), "No render model means no scene object" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Once the 0.08 second edit-settle window passes the cable rebuilds at full detail:
	/// the default path detail of 6 gives 8 rings of 8 vertices plus two 8-vertex end
	/// caps (80 vertices, 58 faces). Turning CapEnds off marks the mesh dirty and the
	/// next rebuild drops the caps (64 vertices, 56 faces), and setting Slack regenerates
	/// again with the path sagging downward by roughly the slack distance.
	/// </summary>
	[TestMethod]
	public void SettlesToFullDetailAndRegeneratesOnPropertyChange()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var cable = go.Components.Create<CableComponent>();
		CreateNode( scene, go, new Vector3( 0, 0, 0 ) );
		CreateNode( scene, go, new Vector3( 100, 0, 0 ) );

		scene.GameTick();

		var meshComponent = go.Components.Get<MeshComponent>( true );
		var preview = meshComponent.Mesh;
		Assert.AreEqual( 24, preview.VertexHandles.Count(), "The first build is a fast preview" );

		var settled = TickUntilRebuilt( scene, meshComponent, preview );
		Assert.AreEqual( 80, settled.VertexHandles.Count(), "Full detail: 8 rings x 8 sides plus 2 caps x 8 vertices" );
		Assert.AreEqual( 58, settled.FaceHandles.Count(), "Full detail: 7 segments x 8 sides plus 2 cap faces" );

		cable.CapEnds = false;
		var uncapped = TickUntilRebuilt( scene, meshComponent, settled );
		Assert.AreEqual( 64, uncapped.VertexHandles.Count(), "Without caps only the 8 rings remain" );
		Assert.AreEqual( 56, uncapped.FaceHandles.Count(), "Without caps only the tube faces remain" );

		cable.Slack = 50.0f;
		var slacked = TickUntilRebuilt( scene, meshComponent, uncapped );
		var bounds = slacked.CalculateBounds();
		Assert.IsTrue( bounds.Mins.z < -45.0f, $"Slack should sag the cable down by about the slack distance, got {bounds.Mins.z}" );
		Assert.IsTrue( bounds.Mins.z > -65.0f, $"The sag should not exceed slack plus the radius, got {bounds.Mins.z}" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Pins the cable's property surface: the defaults, and every setter's clamping
	/// behavior - Size only has a lower floor of 0.1 (no upper clamp), Subdivisions
	/// clamp to 3-32, PathDetail to 0-16, Slack to +/-512, TextureScale and
	/// TextureRepeatsCircumference keep their sign while clamping their magnitude
	/// (so 0 becomes 1/256), the texture offsets clamp to +/-1, the legacy FakeSlack
	/// property aliases Slack, and assigning null ControlPoints yields an empty array.
	/// </summary>
	[TestMethod]
	public void PropertyDefaultsAndClamping()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var cable = go.Components.Create<CableComponent>();

		Assert.AreEqual( 8.0f, cable.Size, "Default radius is pinned" );
		Assert.AreEqual( 8, cable.Subdivisions );
		Assert.AreEqual( 6, cable.PathDetail );
		Assert.AreEqual( 0.0f, cable.Slack );
		Assert.IsTrue( cable.CapEnds );
		Assert.AreEqual( CableComponent.CableTextureOrientation.Horizontal, cable.TextureOrientation );
		Assert.AreEqual( 0.25f, cable.TextureScale );
		Assert.AreEqual( 1.0f, cable.TextureRepeatsCircumference );
		Assert.AreEqual( 0.0f, cable.TextureOffsetAlongPath );
		Assert.AreEqual( 0.0f, cable.TextureOffsetCircumference );
		Assert.IsNull( cable.Material );
		Assert.AreEqual( 0, cable.ControlPoints.Length, "ControlPoints start empty" );

		cable.Size = 0.01f;
		Assert.AreEqual( 0.1f, cable.Size, "Size floors at 0.1" );
		cable.Size = 500.0f;
		Assert.AreEqual( 500.0f, cable.Size, "Size has no upper clamp despite the 64 range hint" );

		cable.Subdivisions = 1;
		Assert.AreEqual( 3, cable.Subdivisions );
		cable.Subdivisions = 100;
		Assert.AreEqual( 32, cable.Subdivisions );

		cable.PathDetail = -5;
		Assert.AreEqual( 0, cable.PathDetail );
		cable.PathDetail = 99;
		Assert.AreEqual( 16, cable.PathDetail );

		cable.Slack = 9999.0f;
		Assert.AreEqual( 512.0f, cable.Slack );
		cable.Slack = -9999.0f;
		Assert.AreEqual( -512.0f, cable.Slack );

		cable.TextureScale = 0.0f;
		Assert.AreEqual( 1.0f / 256.0f, cable.TextureScale, "Zero scale clamps to the minimum magnitude" );
		cable.TextureScale = -10.0f;
		Assert.AreEqual( -4.0f, cable.TextureScale, "The sign survives the magnitude clamp" );

		cable.TextureRepeatsCircumference = 0.0f;
		Assert.AreEqual( 1.0f / 256.0f, cable.TextureRepeatsCircumference );
		cable.TextureRepeatsCircumference = -100.0f;
		Assert.AreEqual( -32.0f, cable.TextureRepeatsCircumference );

		cable.TextureOffsetAlongPath = 5.0f;
		Assert.AreEqual( 1.0f, cable.TextureOffsetAlongPath );
		cable.TextureOffsetAlongPath = -5.0f;
		Assert.AreEqual( -1.0f, cable.TextureOffsetAlongPath );

		cable.TextureOffsetCircumference = 5.0f;
		Assert.AreEqual( 1.0f, cable.TextureOffsetCircumference );
		cable.TextureOffsetCircumference = -5.0f;
		Assert.AreEqual( -1.0f, cable.TextureOffsetCircumference );

		cable.LegacyFakeSlack = 600.0f;
		Assert.AreEqual( 512.0f, cable.Slack, "FakeSlack routes through the Slack clamp" );
		Assert.AreEqual( 512.0f, cable.LegacyFakeSlack, "FakeSlack reads back the shared slack field" );

		cable.ControlPoints = null;
		Assert.IsNotNull( cable.ControlPoints, "Null control points become an empty array" );
		Assert.AreEqual( 0, cable.ControlPoints.Length );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// CableNodeComponent data feeds the parent cable, which polls its children every
	/// update: raising a node's RadiusScale to 2 regenerates the mesh with the end ring
	/// at twice the cable radius, moving the node's local position regenerates with the
	/// path following it, and changing Roll regenerates too. The node's own setters clamp
	/// RadiusScale to 0.1-128 and Roll to +/-180.
	/// </summary>
	[TestMethod]
	public void NodeComponentsFeedParentCable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<CableComponent>();
		CreateNode( scene, go, new Vector3( 0, 0, 0 ) );
		var nodeB = CreateNode( scene, go, new Vector3( 100, 0, 0 ) );

		scene.GameTick();

		var meshComponent = go.Components.Get<MeshComponent>( true );
		var mesh = meshComponent.Mesh;

		var node = nodeB.Components.Get<CableNodeComponent>();
		Assert.AreEqual( 1.0f, node.RadiusScale, "Default radius scale is pinned" );
		Assert.AreEqual( 0.0f, node.Roll, "Default roll is pinned" );

		node.RadiusScale = 2.0f;
		mesh = TickUntilRebuilt( scene, meshComponent, mesh );

		var bounds = mesh.CalculateBounds();
		Assert.AreEqual( 16.0f, bounds.Maxs.z, 0.05f, "The end ring should be radius x scale = 16 units" );
		Assert.AreEqual( -16.0f, bounds.Mins.z, 0.05f );

		nodeB.LocalPosition = new Vector3( 100, 0, 50 );
		mesh = TickUntilRebuilt( scene, meshComponent, mesh );
		Assert.IsTrue( mesh.CalculateBounds().Maxs.z > 50.0f, "The path should follow the moved node upward" );

		node.Roll = 30.0f;
		mesh = TickUntilRebuilt( scene, meshComponent, mesh );

		node.RadiusScale = 0.01f;
		Assert.AreEqual( 0.1f, node.RadiusScale, "RadiusScale clamps at 0.1" );
		node.RadiusScale = 1000.0f;
		Assert.AreEqual( 128.0f, node.RadiusScale, "RadiusScale clamps at 128" );

		node.Roll = -999.0f;
		Assert.AreEqual( -180.0f, node.Roll );
		node.Roll = 999.0f;
		Assert.AreEqual( 180.0f, node.Roll );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A cable carrying only legacy ControlPoints migrates them on its first update:
	/// one child GameObject named "Cable Node N" with a CableNodeComponent is created
	/// per point at that local position, the ControlPoints array is emptied, and the
	/// mesh generates from the new nodes (3 nodes -> 5 fast-preview rings of 8 vertices).
	/// </summary>
	[TestMethod]
	public void LegacyControlPointsMigrateToNodeChildren()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var cable = go.Components.Create<CableComponent>();
		cable.ControlPoints = new[] { new Vector3( 0, 0, 0 ), new Vector3( 50, 0, 25 ), new Vector3( 100, 0, 0 ) };

		Assert.AreEqual( 0, go.Children.Count, "No node children exist before the first update" );

		scene.GameTick();

		Assert.AreEqual( 0, cable.ControlPoints.Length, "Migration empties the legacy points" );

		var nodes = go.Children.Where( x => x.Components.Get<CableNodeComponent>( true ) is not null ).ToList();
		Assert.AreEqual( 3, nodes.Count, "One node child per legacy control point" );
		Assert.AreEqual( "Cable Node 1", nodes[0].Name );
		Assert.AreEqual( "Cable Node 2", nodes[1].Name );
		Assert.AreEqual( "Cable Node 3", nodes[2].Name );
		Assert.IsTrue( nodes[1].LocalPosition.Distance( new Vector3( 50, 0, 25 ) ) < 0.001f, "Nodes sit at the legacy point positions" );

		var meshComponent = go.Components.Get<MeshComponent>( true );
		Assert.IsNotNull( meshComponent, "The migrated nodes generate a mesh in the same update" );
		Assert.AreEqual( 40, meshComponent.Mesh.VertexHandles.Count(), "Fast preview: 5 path rings x 8 sides for 3 nodes" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Re-enabling the generated MeshComponent while its mesh is assigned takes the
	/// render-model build path (RebuildRenderMesh runs from OnEnabled with a mesh set),
	/// but the headless test host cannot create native render meshes - the failure is
	/// swallowed by the CallbackBatch dispatch, like the SpriteRenderer GPU buffer case -
	/// so the component stays pure data: no render Model, no scene object, no physics
	/// shapes, and a ray dropped onto the cable passes straight through. Disabling the
	/// CableComponent does not touch the MeshComponent - the cable is only a generator,
	/// so the mesh simply stops regenerating - while destroying the GameObject
	/// invalidates the generated component.
	/// </summary>
	[TestMethod]
	public void MeshEnableDoesNotBuildRenderModelInTestHost()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var cable = go.Components.Create<CableComponent>();
		CreateNode( scene, go, new Vector3( 0, 0, 0 ) );
		CreateNode( scene, go, new Vector3( 100, 0, 0 ) );

		scene.GameTick();

		var meshComponent = go.Components.Get<MeshComponent>( true );
		Assert.IsNull( meshComponent.Model, "The first runtime build leaves the render model unbuilt" );

		meshComponent.Enabled = false;
		meshComponent.Enabled = true;

		Assert.IsNotNull( meshComponent.Mesh, "The generated mesh data survives the enable cycle" );
		Assert.IsNull( meshComponent.Model, "The render model still cannot build in the headless test host" );
		Assert.IsNull( FindSceneObjectFor<SceneObject>( scene, meshComponent ), "No render model means no scene object" );
		Assert.AreEqual( 0, meshComponent.Shapes.Count, "No model means no physics shapes" );

		var hit = scene.Trace.Ray( new Vector3( 50, 0, 100 ), new Vector3( 50, 0, -100 ) ).Run();
		Assert.IsFalse( hit.Hit, "With no physics shapes a ray dropped onto the cable passes straight through" );

		cable.Enabled = false;
		var meshBefore = meshComponent.Mesh;
		scene.GameTick();

		Assert.IsTrue( meshComponent.Enabled, "Disabling the cable leaves the generated MeshComponent alone" );
		Assert.AreSame( meshBefore, meshComponent.Mesh, "A disabled cable stops regenerating the mesh" );

		go.Destroy();
		scene.ProcessDeletes();

		Assert.IsFalse( meshComponent.IsValid(), "Destroying the GameObject destroys the generated MeshComponent" );
	}

	/// <summary>
	/// Dropping below two nodes tears the cable down: the rebuild produces a null mesh,
	/// which nulls the MeshComponent's mesh and disables it (without destroying it).
	/// Adding a second node back regenerates the mesh data and re-enables the component.
	/// That re-enable with the mesh already assigned is the path that builds the render
	/// model in game, but the headless test host cannot create native render meshes -
	/// the swallowed failure leaves the regrown cable data-only: no Model, no physics
	/// shapes, no scene object.
	/// </summary>
	[TestMethod]
	public void TearsDownAndRegrowsWithNodeCount()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<CableComponent>();
		CreateNode( scene, go, new Vector3( 0, 0, 0 ) );
		var nodeB = CreateNode( scene, go, new Vector3( 100, 0, 0 ) );

		scene.GameTick();

		var meshComponent = go.Components.Get<MeshComponent>( true );
		Assert.IsNotNull( meshComponent.Mesh );

		nodeB.Destroy();
		TickUntil( scene, () => !meshComponent.Enabled, "Dropping below two nodes should disable the generated mesh component" );

		Assert.IsNull( meshComponent.Mesh, "A single node cannot form a cable, so the mesh is nulled" );
		Assert.IsTrue( meshComponent.IsValid(), "The MeshComponent is disabled, not destroyed" );

		CreateNode( scene, go, new Vector3( 0, 100, 0 ) );
		TickUntil( scene, () => meshComponent.Enabled, "Regaining two nodes should rebuild and re-enable the mesh component" );

		Assert.IsNotNull( meshComponent.Mesh );
		Assert.IsNull( meshComponent.Model, "Even re-enabling with the mesh already assigned, the render model cannot build in the headless test host" );
		Assert.AreEqual( 0, meshComponent.Shapes.Count, "No model means the regrown cable has no physics shapes" );
		Assert.IsNull( FindSceneObjectFor<SceneObject>( scene, meshComponent ), "No render model means the regrown cable has no scene object" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// MakeEditableMesh bakes the cable into a plain editable mesh: it builds a full
	/// detail mesh (8 rings x 8 sides plus both end caps for two default nodes) onto the
	/// existing MeshComponent, destroys every node child and destroys the CableComponent
	/// itself. Outside the editor the undo scope is null and is safely skipped, and the
	/// render model still does not build because assigning a mesh at runtime never
	/// rebuilds it.
	/// </summary>
	[TestMethod]
	public void MakeEditableMeshBakesAndRemovesCable()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var cable = go.Components.Create<CableComponent>();
		CreateNode( scene, go, new Vector3( 0, 0, 0 ) );
		CreateNode( scene, go, new Vector3( 100, 0, 0 ) );

		scene.GameTick();

		var meshComponent = go.Components.Get<MeshComponent>( true );
		Assert.IsNotNull( meshComponent );

		cable.MakeEditableMesh();

		Assert.IsFalse( cable.IsValid(), "Baking destroys the cable component" );
		Assert.IsTrue( meshComponent.IsValid(), "The MeshComponent survives as the editable mesh" );
		Assert.AreEqual( 80, meshComponent.Mesh.VertexHandles.Count(), "Baking always builds the full detail mesh" );
		Assert.AreEqual( 58, meshComponent.Mesh.FaceHandles.Count() );
		Assert.IsNull( meshComponent.Model, "Assigning a mesh at runtime still does not build the render model" );

		scene.ProcessDeletes();

		Assert.AreEqual( 0, go.Children.Count, "Baking destroys every node child" );
		Assert.IsNull( go.Components.Get<CableComponent>( true ), "The cable component is gone from the object" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// A cable with non-default sizing, path, slack and texture settings, plus node
	/// children carrying radius scale and roll, survives a serialize/deserialize round
	/// trip. The generated MeshComponent round trips its PolygonMesh too. The clone
	/// enables with the mesh already deserialized - the path real saved scenes take in
	/// game, where the render model would build immediately - but the headless test
	/// host cannot create native render meshes, so the clone stays data-only with no
	/// Model, scene object or physics shapes.
	/// </summary>
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var material = Material.Load( "materials/default/white.vmat" );

		var go = scene.CreateObject();
		var cable = go.Components.Create<CableComponent>();
		cable.Size = 12.0f;
		cable.Subdivisions = 6;
		cable.PathDetail = 4;
		cable.Slack = 25.0f;
		cable.CapEnds = false;
		cable.TextureOrientation = CableComponent.CableTextureOrientation.Vertical;
		cable.TextureScale = 0.5f;
		cable.TextureRepeatsCircumference = 2.0f;
		cable.TextureOffsetAlongPath = 0.25f;
		cable.TextureOffsetCircumference = -0.5f;
		cable.Material = material;

		CreateNode( scene, go, new Vector3( 0, 0, 0 ) );
		var nodeB = CreateNode( scene, go, new Vector3( 0, 100, 0 ) );
		var nodeComponent = nodeB.Components.Get<CableNodeComponent>();
		nodeComponent.RadiusScale = 2.0f;
		nodeComponent.Roll = 45.0f;

		scene.GameTick();

		var vertexCount = go.Components.Get<MeshComponent>( true ).Mesh.VertexHandles.Count();

		var clone = SerializeRoundTrip( scene, go );
		var loaded = clone.Components.Get<CableComponent>();

		Assert.IsNotNull( loaded, "Deserialized GameObject should have a CableComponent" );
		Assert.AreEqual( 12.0f, loaded.Size );
		Assert.AreEqual( 6, loaded.Subdivisions );
		Assert.AreEqual( 4, loaded.PathDetail );
		Assert.AreEqual( 25.0f, loaded.Slack );
		Assert.IsFalse( loaded.CapEnds );
		Assert.AreEqual( CableComponent.CableTextureOrientation.Vertical, loaded.TextureOrientation );
		Assert.AreEqual( 0.5f, loaded.TextureScale );
		Assert.AreEqual( 2.0f, loaded.TextureRepeatsCircumference );
		Assert.AreEqual( 0.25f, loaded.TextureOffsetAlongPath );
		Assert.AreEqual( -0.5f, loaded.TextureOffsetCircumference );
		Assert.AreEqual( material.Name, loaded.Material?.Name, "Material should round trip by path" );

		var loadedNodes = clone.Children.Where( x => x.Components.Get<CableNodeComponent>( true ) is not null ).ToList();
		Assert.AreEqual( 2, loadedNodes.Count, "Both node children should round trip" );
		Assert.IsTrue( loadedNodes[1].LocalPosition.Distance( new Vector3( 0, 100, 0 ) ) < 0.001f );

		var loadedNode = loadedNodes[1].Components.Get<CableNodeComponent>();
		Assert.AreEqual( 2.0f, loadedNode.RadiusScale );
		Assert.AreEqual( 45.0f, loadedNode.Roll );

		var loadedMesh = clone.Components.Get<MeshComponent>( true );
		Assert.IsNotNull( loadedMesh, "The generated MeshComponent serializes with the cable" );
		Assert.IsTrue( loadedMesh.Enabled );
		Assert.AreEqual( vertexCount, loadedMesh.Mesh.VertexHandles.Count(), "The PolygonMesh should round trip its topology" );
		Assert.IsNull( loadedMesh.Model, "Even enabling with the mesh already deserialized, the render model cannot build in the headless test host" );
		Assert.IsNull( FindSceneObjectFor<SceneObject>( scene, loadedMesh ), "No render model means the clone has no scene object" );
		Assert.AreEqual( 0, loadedMesh.Shapes.Count, "No model means the clone has no physics shapes" );

		clone.Destroy();
		scene.ProcessDeletes();
	}
}
