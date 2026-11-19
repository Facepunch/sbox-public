using Editor;
using System;
using System.Collections.Generic;

namespace SceneTests;

[TestClass, DoNotParallelize]
public class BoxSelectionTests
{
	Scene _scene;
	IDisposable _scope;
	Sandbox.Internal.TypeLibrary _library;

	[TestInitialize]
	public void Initialize()
	{
		ThreadSafe.MarkMainThread();
		_library = Game.TypeLibrary;
		Game.TypeLibrary = SharedTypeLibrary.EngineAndTests;
		_scene = new Scene( true, false );
		_scope = _scene.PushScope();
	}

	[TestCleanup]
	public void Cleanup()
	{
		_scope.Dispose();
		_scene.Destroy();
		Game.TypeLibrary = _library;
	}

	static Frustum Region( float size = 100, Rotation? rotation = null )
	{
		var r = rotation ?? Rotation.Identity;
		return new Frustum( new Plane( r * Vector3.Right, -size ), new Plane( r * Vector3.Left, -size ),
			new Plane( r * Vector3.Down, -size ), new Plane( r * Vector3.Up, -size ),
			new Plane( r * Vector3.Forward, -size ), new Plane( r * Vector3.Backward, -size ) );
	}

	GameObject Object( Vector3 position, float size = 20, GameObject parent = null )
	{
		var go = new GameObject( parent ?? _scene );
		go.WorldPosition = position;
		go.Components.Create<BoxSelectionBounds>().LocalBounds = BBox.FromPositionAndSize( 0, size );
		return go;
	}

	[TestMethod]
	public void OnlyFullyContainedObjectsAreSelected()
	{
		var inside = Object( 0 );
		var partial = Object( Vector3.Left * 99 );
		var outside = Object( Vector3.Left * 200 );
		var hits = BoxSelection.Find( _scene, Region() );
		Assert.IsTrue( hits.SetEquals( [inside] ) );
		Assert.IsFalse( hits.Contains( partial ) );
		Assert.IsFalse( hits.Contains( outside ) );
	}

	[TestMethod]
	public void EmptyContainersNeverInheritTheirChildrensBounds()
	{
		var parent = _scene.CreateObject();
		Object( Vector3.Left * 200, parent: parent );
		Object( Vector3.Right * 200, parent: parent );
		Assert.AreEqual( 0, BoxSelection.Find( _scene, Region() ).Count );
		var child = Object( 0, parent: parent );
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).SetEquals( [child] ) );
	}

	[TestMethod]
	public void DeepHierarchyVisitsEachObjectsBoundsOnlyOnce()
	{
		GameObject parent = _scene;
		for ( var i = 0; i < 512; ++i ) parent = new GameObject( parent );
		var child = Object( 0, parent: parent );
		BoxSelectionBounds.Reads = 0;
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).SetEquals( [child] ) );
		Assert.AreEqual( 1, BoxSelectionBounds.Reads );
	}

	[TestMethod]
	public void AnObjectsOwnBoundsDoNotExpandToIncludeChildren()
	{
		var parent = Object( 0 );
		Object( Vector3.Left * 300, parent: parent );
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).SetEquals( [parent] ) );
	}

	[TestMethod]
	[DataRow( "inactive" )]
	[DataRow( "hidden" )]
	[DataRow( "tag" )]
	public void HiddenParentsExcludeTheirSubtrees( string mode )
	{
		var parent = _scene.CreateObject();
		Object( 0, parent: parent );
		if ( mode == "inactive" ) parent.Enabled = false;
		if ( mode == "hidden" ) parent.Flags |= GameObjectFlags.Hidden;
		if ( mode == "tag" ) parent.Tags.Add( "hidden" );
		Assert.AreEqual( 0, BoxSelection.Find( _scene, Region() ).Count );
	}

	[TestMethod]
	public void InactiveAndHiddenComponentsContributeNoBounds()
	{
		var inactive = Object( 0 );
		inactive.Components.Get<BoxSelectionBounds>().Enabled = false;
		var hidden = Object( 0 );
		hidden.Components.Get<BoxSelectionBounds>().Flags |= ComponentFlags.Hidden;
		Assert.AreEqual( 0, BoxSelection.Find( _scene, Region() ).Count );
	}

	[TestMethod]
	public void ExplicitSelectionBasesAreSelectedThroughTheirChildren()
	{
		var parent = _scene.CreateObject();
		parent.Components.Create<BoxSelectionRoot>();
		Object( 0, parent: parent );
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).SetEquals( [parent] ) );
	}

	[TestMethod]
	public void NegativeAndNonuniformScalesUseTransformedGeometry()
	{
		var inside = Object( 0 );
		inside.WorldScale = new Vector3( -2, 3, .5f );
		var outside = Object( Vector3.Left * 95 );
		outside.WorldScale = new Vector3( -2, 3, .5f );
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).SetEquals( [inside] ) );
	}

	[TestMethod]
	public void InvalidFrustaAndNonfiniteBoundsSelectNothing()
	{
		var go = Object( 0 );
		Assert.AreEqual( 0, BoxSelection.Find( _scene, default ).Count );
		go.Components.Get<BoxSelectionBounds>().LocalBounds = new BBox( new Vector3( float.NaN ), new Vector3( float.PositiveInfinity ) );
		Assert.AreEqual( 0, BoxSelection.Find( _scene, Region() ).Count );
	}

	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void OrthographicAndPerspectiveCamerasSelectTheSameContainedObjects( bool ortho )
	{
		using var camera = new SceneCamera
		{
			Position = new Vector3( -1000, 0, 0 ),
			Rotation = Rotation.Identity,
			Size = 1000,
			Ortho = ortho,
			OrthoHeight = 400,
			FieldOfView = 60,
			ZNear = 1,
			ZFar = 5000
		};
		var inside = Object( 0 );
		Object( Vector3.Left * 1000 );
		Assert.IsTrue( BoxSelection.Find( _scene, camera.GetFrustum( new Rect( 400, 400, 200, 200 ) ) ).SetEquals( [inside] ) );
	}

	[TestMethod]
	public void MeshGeometryIsTestedWhenItsWorldBoundsOverestimateItsShape()
	{
		var go = _scene.CreateObject();
		var mesh = new PolygonMesh();
		mesh.AddFace( mesh.AddVertex( 0 ), mesh.AddVertex( new Vector3( 0, 80, 0 ) ), mesh.AddVertex( new Vector3( 0, 0, 80 ) ) );
		go.Components.Create<MeshComponent>().Mesh = mesh;
		var frustum = Region( 65, Rotation.From( 0, 0, 45 ) );
		Assert.IsFalse( frustum.IsInside( mesh.CalculateBounds() ) );
		Assert.IsTrue( BoxSelection.Find( _scene, frustum ).Contains( go ) );
	}

	[TestMethod]
	public void ModelNarrowPhaseReadsTheActualDrawCallGeometry()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Model readback requires the renderer." );
		var go = _scene.CreateObject();
		go.Components.Create<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );
		Assert.IsTrue( BoxSelection.Find( _scene, Region( 100 ) ).Contains( go ) );
		Assert.IsFalse( BoxSelection.Find( _scene, Region( 10 ) ).Contains( go ) );
		Assert.IsFalse( BoxSelection.Find( _scene, Region( 10 ), partially: true ).Contains( go ) );
		go.WorldPosition = Vector3.Left * 20;
		Assert.IsTrue( BoxSelection.Find( _scene, Region( 30 ), partially: true ).Contains( go ) );
		Assert.IsFalse( BoxSelection.Find( _scene, Region( 30 ) ).Contains( go ) );
	}

	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void SkinnedQueriesUseCurrentPoseBoundsRatherThanRestVertices( bool contained, bool partially )
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Skinned models require the renderer." );
		var go = _scene.CreateObject();
		go.WorldRotation = contained ? Rotation.From( 0, 0, 45 ) : Rotation.Identity;
		var renderer = go.Components.Create<SkinnedModelRenderer>();
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		// Control the live animation bounds independently of the model's unchanged rest geometry.
		var posedBounds = BBox.FromPositionAndSize( 0, contained ? new Vector3( 10, 36, 16 ) : new Vector3( 300 ) );
		renderer.SceneModel.animNative.Twin.m_localBounds = posedBounds;
		var frustum = Region( contained ? 20 : 100, go.WorldRotation );
		var worldBounds = posedBounds.Transform( go.WorldTransform );
		Assert.IsTrue( frustum.IsInside( worldBounds, true ) );
		Assert.IsFalse( frustum.IsInside( worldBounds ), "Exercise the narrow phase, including a rotated bounds overestimate." );
		Assert.AreEqual( contained, BoxSelection.Find( _scene, frustum, partially ).Contains( go ) );
	}

	[TestMethod]
	public void IntersectionModeTestsMeshVerticesRatherThanOnlyTheBoundingBox()
	{
		var go = _scene.CreateObject();
		var mesh = new PolygonMesh();
		mesh.AddFace( mesh.AddVertex( new Vector3( 0, 150, 0 ) ), mesh.AddVertex( new Vector3( 0, 0, 150 ) ), mesh.AddVertex( new Vector3( 0, 150, 150 ) ) );
		go.Components.Create<MeshComponent>().Mesh = mesh;
		Assert.IsTrue( Region().IsInside( mesh.CalculateBounds(), true ) );
		Assert.IsFalse( BoxSelection.Find( _scene, Region(), partially: true ).Contains( go ) );
		mesh.SetVertexPosition( mesh.VertexHandles.First(), 0 );
		Assert.IsTrue( BoxSelection.Find( _scene, Region(), partially: true ).Contains( go ) );
		Assert.IsFalse( BoxSelection.Find( _scene, Region() ).Contains( go ) );
	}

	[TestMethod]
	[DataRow( false, false, "b" )]
	[DataRow( true, false, "ab" )]
	[DataRow( false, true, "a" )]
	[DataRow( true, true, "ab" )]
	public void ModifierOperationsMatchHammer( bool add, bool remove, string expected )
	{
		var a = Object( 0 );
		var b = Object( 0 );
		var selection = new SelectionSystem();
		selection.Add( a );
		BoxSelection.Apply( selection, [b], add, remove );
		Assert.AreEqual( expected.Contains( 'a' ), selection.Contains( a ) );
		Assert.AreEqual( expected.Contains( 'b' ), selection.Contains( b ) );
		if ( add && remove )
		{
			BoxSelection.Apply( selection, [a, b], add, remove );
			Assert.AreEqual( 0, selection.Count );
		}
	}

	[TestMethod]
	public void RotatedBoundsUseTheirCornersWhenTheWorldBoxIsTooLarge()
	{
		var go = Object( 0 );
		go.WorldRotation = Rotation.From( 0, 0, 45 );
		var bounds = BBox.FromPositionAndSize( 0, new Vector3( 20, 120, 20 ) );
		go.Components.Get<BoxSelectionBounds>().LocalBounds = bounds;
		var frustum = Region( 65, go.WorldRotation );
		Assert.IsFalse( frustum.IsInside( bounds.Transform( go.WorldTransform ) ) );
		Assert.IsTrue( BoxSelection.Find( _scene, frustum ).Contains( go ) );
	}

	[TestMethod]
	public void MultipleComponentsRequireAllBoundsInFullModeAndAnyInIntersectionMode()
	{
		var go = Object( 0 );
		var other = go.Components.Create<BoxSelectionOtherBounds>();
		other.LocalBounds = BBox.FromPositionAndSize( Vector3.Left * 200, 20 );
		Assert.IsFalse( BoxSelection.Find( _scene, Region() ).Contains( go ) );
		Assert.IsTrue( BoxSelection.Find( _scene, Region(), partially: true ).Contains( go ) );
		other.Enabled = false;
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).Contains( go ) );
	}

	[TestMethod]
	public void SeparatedComponentsDoNotSelectTheSpaceBetweenThem()
	{
		var go = Object( 0 );
		go.Components.Get<BoxSelectionBounds>().LocalBounds = BBox.FromPositionAndSize( Vector3.Left * 200, 20 );
		go.Components.Create<BoxSelectionOtherBounds>().LocalBounds = BBox.FromPositionAndSize( Vector3.Right * 200, 20 );
		Assert.IsFalse( BoxSelection.Find( _scene, Region() ).Contains( go ) );
		Assert.IsFalse( BoxSelection.Find( _scene, Region(), partially: true ).Contains( go ) );
	}

	[TestMethod]
	public void NestedSelectionBasesMatchClickSelectionAndDoNotDuplicateResults()
	{
		var outer = _scene.CreateObject();
		outer.Components.Create<BoxSelectionRoot>();
		var inner = new GameObject( outer );
		inner.Components.Create<BoxSelectionRoot>();
		Object( 0, parent: inner );
		Object( 0, parent: inner );
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).SetEquals( [inner] ) );
	}

	[TestMethod]
	public void EmptyMeshesAreNeverSelectionTargets()
	{
		var go = _scene.CreateObject();
		go.Components.Create<MeshComponent>().Mesh = new PolygonMesh();
		Assert.IsFalse( BoxSelection.Find( _scene, Region() ).Contains( go ) );
		Assert.IsFalse( BoxSelection.Find( _scene, Region(), partially: true ).Contains( go ) );
	}

	[TestMethod]
	public void TransformedMeshQueriesAgreeWithVertexContainment()
	{
		var random = new Random( 81382 );
		var go = _scene.CreateObject();
		var mesh = new PolygonMesh();
		var positions = new[] { new Vector3( 0, -80, -20 ), new Vector3( 0, 80, -20 ), new Vector3( 0, 0, 80 ) };
		mesh.AddFace( positions.Select( p => mesh.AddVertex( p ) ).ToArray() );
		go.Components.Create<MeshComponent>().Mesh = mesh;
		float Next( float min, float max ) => min + (float)random.NextDouble() * (max - min);
		for ( var i = 0; i < 200; ++i )
		{
			go.WorldPosition = new Vector3( Next( -100, 100 ), Next( -100, 100 ), Next( -100, 100 ) );
			go.WorldRotation = Rotation.From( Next( -180, 180 ), Next( -180, 180 ), Next( -180, 180 ) );
			go.WorldScale = new Vector3( Next( .2f, 2 ), Next( .2f, 2 ) * (i % 2 == 0 ? -1 : 1), Next( .2f, 2 ) );
			var frustum = Region( 100, Rotation.From( Next( -90, 90 ), Next( -90, 90 ), Next( -90, 90 ) ) );
			var inside = positions.Count( p => frustum.IsInside( go.WorldTransform.PointToWorld( p ) ) );
			Assert.AreEqual( inside == positions.Length, BoxSelection.Find( _scene, frustum ).Contains( go ), $"full case {i}" );
			Assert.AreEqual( inside > 0, BoxSelection.Find( _scene, frustum, partially: true ).Contains( go ), $"intersection case {i}" );
		}
	}

	static Mesh Triangle( Vector3 offset, bool unreferencedVertex = false )
	{
		var mesh = new Mesh( Material.Load( "materials/default/white.vmat" ) );
		var vertices = new Vertex[]
		{
			new() { Position = offset + new Vector3( 0, -16, -16 ) },
			new() { Position = offset + new Vector3( 0, 16, -16 ) },
			new() { Position = offset + new Vector3( 0, 0, 16 ) },
			new() { Position = offset + new Vector3( 0, unreferencedVertex ? 500 : 0, 0 ) }
		};
		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );
		mesh.Bounds = new BBox( offset + new Vector3( -1, -16, -16 ), offset + new Vector3( 1, unreferencedVertex ? 500 : 16, 16 ) );
		return mesh;
	}

	[TestMethod]
	public void ModelsWithNoEnabledMeshGroupCannotPassTheBoundsFastPath()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Model creation requires the renderer." );
		var model = Model.Builder.AddMesh( Triangle( 0 ), "shape", 0 ).Create();
		var go = _scene.CreateObject();
		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = model;
		renderer.BodyGroups = 1UL << 63;
		Assert.IsFalse( BoxSelection.Find( _scene, Region() ).Contains( go ) );
		Assert.IsFalse( BoxSelection.Find( _scene, Region(), partially: true ).Contains( go ) );
	}

	[TestMethod]
	public void ModelReadbackIgnoresUnreferencedVerticesAndOtherBodyGroups()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Model readback requires the renderer." );
		var model = Model.Builder.AddMesh( Triangle( 0, true ), "shape", 0 )
			.AddMesh( Triangle( Vector3.Left * 300 ), "shape", 1 ).Create();
		var go = _scene.CreateObject();
		var renderer = go.Components.Create<ModelRenderer>();
		renderer.Model = model;
		var other = _scene.CreateObject();
		other.WorldPosition = Vector3.Right * 120;
		other.Components.Create<ModelRenderer>().Model = model;
		// Both bounds require a narrow test. Cached positions must remain in model space.
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).SetEquals( [go] ) );
		renderer.SetBodyGroup( "shape", 1 );
		Assert.IsFalse( BoxSelection.Find( _scene, Region() ).Contains( go ) );
		renderer.BodyGroups = 0;
		Assert.IsFalse( BoxSelection.Find( _scene, Region( 1000 ) ).Contains( go ) );
	}

	[TestMethod]
	public void RenderGeometryTakesPrecedenceOverInvisibleColliderBounds()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Model loading requires the renderer." );
		var go = Object( 0, 1000 );
		go.Components.Create<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).Contains( go ) );
	}

	[TestMethod]
	public void ARendererWithoutBoundsDoesNotSuppressTheBoundsFallback()
	{
		var go = Object( 0 );
		go.Components.Create<BoxSelectionUnboundedRenderer>();
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).Contains( go ) );
	}

	static void SetDrawnGizmoHandle( GameObject go )
	{
		using var gizmo = new Gizmo.Instance();
		using var scope = gizmo.Push();
		// Populate the viewport settings and type generation used by the handle visibility getter.
		typeof( GameObject ).GetMethod( "BuildGizmoDetails", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).Invoke( go, null );
		typeof( GameObject ).GetProperty( nameof( GameObject.HasGizmoHandle ) ).SetValue( go, true );
	}

	[TestMethod]
	public void DisabledGizmoHandlesDoNotRemainSelectableFromThePreviousFrame()
	{
		var go = _scene.CreateObject();
		SetDrawnGizmoHandle( go );
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).Contains( go ) );
		Assert.IsFalse( BoxSelection.Find( _scene, Region(), partially: false, includeGizmoHandles: false ).Contains( go ) );
	}

	[TestMethod]
	public void DisablingHandlesDoesNotPreventSelectionOfActualGeometry()
	{
		var go = Object( Vector3.Left * 200 );
		go.Components.Get<BoxSelectionBounds>().LocalBounds = BBox.FromPositionAndSize( Vector3.Right * 200, 20 );
		SetDrawnGizmoHandle( go );
		Assert.IsFalse( BoxSelection.Find( _scene, Region() ).Contains( go ) );
		Assert.IsTrue( BoxSelection.Find( _scene, Region(), partially: false, includeGizmoHandles: false ).Contains( go ) );
	}

	[TestMethod]
	public void SiblingTraversalRestoresSelectionBasesAndSkipsOnlyTheHiddenSubtree()
	{
		var hidden = _scene.CreateObject();
		hidden.Flags |= GameObjectFlags.Hidden;
		Object( 0, parent: hidden );
		var first = _scene.CreateObject();
		first.Components.Create<BoxSelectionRoot>();
		Object( 0, parent: first );
		var second = _scene.CreateObject();
		second.Components.Create<BoxSelectionRoot>();
		Object( 0, parent: second );
		var plain = Object( 0 );
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).SetEquals( [first, second, plain] ) );
	}

	[TestMethod]
	public void IntersectionModeStillRequiresContainmentOfNonMeshLeafBounds()
	{
		var go = Object( Vector3.Left * 99 );
		Assert.IsFalse( BoxSelection.Find( _scene, Region(), partially: true ).Contains( go ) );
	}

	[TestMethod]
	[DataRow( -60f )]
	[DataRow( 60f )]
	public void MissingModelsUseTheRenderedFallbackAtTheObjectsPosition( float y )
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Fallback rendering requires the renderer." );
		var go = _scene.CreateObject();
		go.WorldPosition = new Vector3( 0, y, 0 );
		var renderer = go.Components.Create<ModelRenderer>();
		Assert.IsNull( renderer.Model );
		Assert.IsNotNull( renderer.SceneObject.Model );
		Assert.IsTrue( Region().IsInside( renderer.SceneObject.Model.RenderBounds.Transform( go.WorldTransform ) ) );
		Assert.IsTrue( BoxSelection.Find( _scene, Region() ).Contains( go ) );
	}

	[TestMethod]
	public void ReplacingWithAnEmptyResultClearsThePreviousSelection()
	{
		var selection = new SelectionSystem();
		selection.Add( Object( 0 ) );
		BoxSelection.Apply( selection, [], false, false );
		Assert.AreEqual( 0, selection.Count );
	}
}

public sealed class BoxSelectionBounds : Component, Component.IHasBounds
{
	public static int Reads;
	public BBox LocalBounds
	{
		get { Reads++; return field; }
		set;
	}
}

[SelectionBase]
public sealed class BoxSelectionRoot : Component { }

public sealed class BoxSelectionOtherBounds : Component, Component.IHasBounds
{
	public BBox LocalBounds { get; set; }
}

public sealed class BoxSelectionUnboundedRenderer : Renderer { }
