using System.Collections;
using System.Reflection;

namespace SceneTests.Components;

[TestClass]
public class MeshComponentBuildTests
{
	Surface _previousSurface;
	bool _installedSurface;

	[TestInitialize]
	public void Initialize()
	{
		if ( Surface.FindByName( "default" ) is not null ) return;

		Surface.All.TryGetValue( 0, out _previousSurface );
		var surface = new Surface();
		surface.RegisterWeakResourceId( "surfaces/default.surface" );
		Surface.All[0] = surface;
		_installedSurface = true;
	}

	[TestCleanup]
	public void Cleanup()
	{
		if ( !_installedSurface ) return;
		if ( _previousSurface is not null )
			Surface.All[0] = _previousSurface;
		else
			Surface.All.Remove( 0 );
	}

	static PolygonMesh CreateBox( float size = 16 )
	{
		var mesh = new PolygonMesh();
		var vertices = new[]
		{
			mesh.AddVertex( new Vector3( 0, 0, 0 ) ),
			mesh.AddVertex( new Vector3( size, 0, 0 ) ),
			mesh.AddVertex( new Vector3( size, size, 0 ) ),
			mesh.AddVertex( new Vector3( 0, size, 0 ) ),
			mesh.AddVertex( new Vector3( 0, 0, size ) ),
			mesh.AddVertex( new Vector3( size, 0, size ) ),
			mesh.AddVertex( new Vector3( size, size, size ) ),
			mesh.AddVertex( new Vector3( 0, size, size ) )
		};
		mesh.AddFace( vertices[0], vertices[3], vertices[2], vertices[1] );
		mesh.AddFace( vertices[4], vertices[5], vertices[6], vertices[7] );
		mesh.AddFace( vertices[0], vertices[1], vertices[5], vertices[4] );
		mesh.AddFace( vertices[1], vertices[2], vertices[6], vertices[5] );
		mesh.AddFace( vertices[2], vertices[3], vertices[7], vertices[6] );
		mesh.AddFace( vertices[3], vertices[0], vertices[4], vertices[7] );
		return mesh;
	}

	static Mesh[] RenderMeshes( PolygonMesh mesh )
	{
		var submeshes = (IEnumerable)typeof( PolygonMesh ).GetField( "_submeshes", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( mesh );
		return submeshes.Cast<object>().Select( x => (Mesh)x.GetType().GetProperty( "Mesh" ).GetValue( x ) ).ToArray();
	}

	static void AssertCollision( MeshComponent component, MeshComponent.CollisionType collision )
	{
		Assert.IsNotNull( component.Model );
		Assert.IsTrue( component.Model.MeshCount > 0 );
		var parts = component.Model.Physics?.Parts ?? [];
		Assert.AreEqual( collision == MeshComponent.CollisionType.Mesh ? 1 : 0, parts.Sum( p => p.Meshes.Count ) );
		Assert.AreEqual( collision == MeshComponent.CollisionType.Hull ? 1 : 0, parts.Sum( p => p.Hulls.Count ) );
		Assert.AreEqual( collision == MeshComponent.CollisionType.None ? 0 : 1, component.Shapes.Count );
		Assert.IsTrue( component.Shapes.All( s => collision == MeshComponent.CollisionType.Mesh ? s.IsMeshShape : s.IsHullShape ) );
		Assert.IsTrue( component.Model.Trace.Ray( new Vector3( 8, 8, 32 ), new Vector3( 8, 8, -16 ) ).Run().Hit );
	}

	[TestMethod]
	[DataRow( MeshComponent.CollisionType.None, false )]
	[DataRow( MeshComponent.CollisionType.Mesh, false )]
	[DataRow( MeshComponent.CollisionType.Hull, false )]
	[DataRow( MeshComponent.CollisionType.None, true )]
	[DataRow( MeshComponent.CollisionType.Mesh, true )]
	[DataRow( MeshComponent.CollisionType.Hull, true )]
	public void BatchedEnableBuildsOnlyFinalMeshAndCollision( MeshComponent.CollisionType collision, bool hasRigidbody )
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			MeshComponent component;
			var discarded = CreateBox( 32 );
			var selected = CreateBox();
			Model enabledModel = null;
			using ( CallbackBatch.Batch() )
			{
				var gameObject = scene.CreateObject();
				if ( hasRigidbody )
					gameObject.AddComponent<Rigidbody>();
				component = gameObject.AddComponent<MeshComponent>();
				Assert.IsTrue( component.Active );
				Assert.IsFalse( component.PhysicsBody.IsValid() );
				component.Mesh = discarded;
				component.Mesh = selected;
				component.Collision = collision;
				component.OnComponentEnabled = () => enabledModel = component.Model;
				Assert.IsNull( component.Model );
				Assert.AreEqual( 0, component.Shapes.Count );
				Assert.IsTrue( discarded.IsDirty );
				Assert.IsTrue( selected.IsDirty );
			}
			AssertCollision( component, collision );
			Assert.IsTrue( discarded.IsDirty );
			Assert.IsFalse( selected.IsDirty );
			Assert.AreSame( enabledModel, component.Model );
			component.RebuildMesh();
			Assert.AreSame( enabledModel, component.Model );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	[DataRow( true, true )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( false, false )]
	public void ParentRigidbodyEnableOrderPreservesCollision( bool editor, bool rigidbodyFirst )
	{
		var scene = editor ? Scene.CreateEditorScene() : new Scene();
		try
		{
			using var scope = scene.Push();
			MeshComponent component;
			using ( CallbackBatch.Batch() )
			{
				var parent = scene.CreateObject();
				if ( rigidbodyFirst )
					parent.AddComponent<Rigidbody>();
				var child = scene.CreateObject();
				child.Parent = parent;
				component = child.AddComponent<MeshComponent>();
				component.Mesh = CreateBox();
				component.Collision = MeshComponent.CollisionType.Hull;
				if ( !rigidbodyFirst )
					parent.AddComponent<Rigidbody>();
				Assert.IsNull( component.Model );
			}

			AssertCollision( component, MeshComponent.CollisionType.Hull );
			var model = component.Model;
			component.Enabled = false;
			component.Enabled = true;
			Assert.AreSame( model, component.Model );
			AssertCollision( component, MeshComponent.CollisionType.Hull );
			component.Collision = MeshComponent.CollisionType.None;
			AssertCollision( component, MeshComponent.CollisionType.None );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void CollisionChangesReuseRenderMeshes()
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<MeshComponent>( false );
			component.Mesh = CreateBox();
			component.Enabled = true;
			var renderMeshes = RenderMeshes( component.Mesh );
			Assert.IsTrue( renderMeshes.Length > 0 && renderMeshes.All( m => m is not null ) );
			foreach ( var collision in new[] { MeshComponent.CollisionType.Mesh, MeshComponent.CollisionType.Hull, MeshComponent.CollisionType.None, MeshComponent.CollisionType.Mesh } )
			{
				component.Collision = collision;
				AssertCollision( component, collision );
				CollectionAssert.AreEqual( renderMeshes, RenderMeshes( component.Mesh ) );
				Assert.IsFalse( component.Mesh.IsDirty );
			}

			component.HideInGame = true;
			component.Collision = MeshComponent.CollisionType.Hull;
			AssertCollision( component, MeshComponent.CollisionType.Hull );
			Assert.IsFalse( scene.SceneWorld.SceneObjects.Any( x => x.Component == component && x.IsValid() ) );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void EditorReplacementRemovalAndReenable()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<MeshComponent>();
			component.Mesh = CreateBox();
			AssertCollision( component, MeshComponent.CollisionType.Mesh );
			var previous = component.Model;
			component.Mesh = CreateBox();
			Assert.AreNotSame( previous, component.Model );
			AssertCollision( component, MeshComponent.CollisionType.Mesh );

			component.Mesh = null;
			Assert.IsNull( component.Model );
			Assert.AreEqual( 0, component.Shapes.Count );
			Assert.IsFalse( scene.SceneWorld.SceneObjects.Any( x => x.Component == component && x.IsValid() ) );

			component.Enabled = false;
			var mesh = CreateBox();
			component.Mesh = mesh;
			component.Collision = MeshComponent.CollisionType.Hull;
			Assert.IsNull( component.Model );
			Assert.IsTrue( mesh.IsDirty );
			component.Enabled = true;
			AssertCollision( component, MeshComponent.CollisionType.Hull );

			var state = component.GameObject.Serialize();
			state["Components"].AsArray()[0]["Mesh"] = Json.ParseToJsonObject( Json.Serialize( CreateBox( 24 ) ) );
			using ( CallbackBatch.Batch() )
				component.GameObject.Deserialize( state, new GameObject.DeserializeOptions { IsRefreshing = true } );
			Assert.IsFalse( component.Mesh.IsDirty );
			Assert.AreEqual( 24f, component.Model.Bounds.Maxs.x, 0.01f );
			AssertCollision( component, MeshComponent.CollisionType.Hull );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void RuntimeReplacementStillWaitsForEnable()
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<MeshComponent>( false );
			var original = CreateBox();
			component.Mesh = original;
			component.Enabled = true;
			var model = component.Model;
			var replacement = CreateBox( 24 );
			component.Mesh = replacement;
			component.RebuildMesh();
			Assert.AreSame( model, component.Model );
			component.Collision = MeshComponent.CollisionType.Hull;
			Assert.AreEqual( 16f, component.Model.Bounds.Maxs.x, 0.01f );
			Assert.IsTrue( replacement.IsDirty );
			component.Enabled = false;
			component.Enabled = true;
			Assert.IsFalse( replacement.IsDirty );
			Assert.AreEqual( 24f, component.Model.Bounds.Maxs.x, 0.01f );
			AssertCollision( component, MeshComponent.CollisionType.Hull );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void EmptyEnabledMeshCanBuildLater()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<MeshComponent>();
			Assert.IsTrue( component.PhysicsBody.IsValid() );
			Assert.IsNull( component.Model );
			component.Mesh = new PolygonMesh();
			Assert.AreEqual( 0, component.Model.MeshCount );
			component.Collision = MeshComponent.CollisionType.Hull;
			component.Mesh = CreateBox();
			AssertCollision( component, MeshComponent.CollisionType.Hull );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CleanReenableReusesModel( bool editVertexData )
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<MeshComponent>();
			component.Mesh = CreateBox();
			var model = component.Model;
			var meshes = RenderMeshes( component.Mesh );
			component.Enabled = false;
			if ( editVertexData )
			{
				Assert.IsTrue( component.Mesh.GetFaceVerticesConnectedToFace( component.Mesh.TriangleToFace( 0 ), out var vertices ) );
				component.Mesh.SetVertexColor( vertices[0], Color.Red );
				Assert.IsTrue( component.Mesh.IsVertexDataDirty );
				Assert.IsFalse( component.Mesh.IsDirty );
			}
			component.Enabled = true;
			Assert.IsFalse( component.Mesh.IsVertexDataDirty );
			Assert.AreSame( model, component.Model );
			CollectionAssert.AreEqual( meshes, RenderMeshes( component.Mesh ) );
			AssertCollision( component, MeshComponent.CollisionType.Mesh );
			Assert.IsTrue( scene.SceneWorld.SceneObjects.Any( x => x.Component == component && x.IsValid() ) );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void DisabledMeshWaitsForNativeBody()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<MeshComponent>();
			component.Mesh = CreateBox();
			component.Enabled = false;
			Assert.IsFalse( component.PhysicsBody.IsValid() );
			var mesh = CreateBox( 24 );
			var previous = component.Model;
			component.Mesh = mesh;
			component.Collision = MeshComponent.CollisionType.Hull;
			Assert.AreSame( previous, component.Model );
			Assert.IsTrue( mesh.IsDirty );
			using ( CallbackBatch.Batch() )
			{
				component.Enabled = true;
				component.RebuildMesh();
				Assert.IsTrue( component.Active );
				Assert.IsFalse( component.PhysicsBody.IsValid() );
				Assert.AreSame( previous, component.Model );
				Assert.IsTrue( mesh.IsDirty );
			}
			Assert.IsFalse( mesh.IsDirty );
			Assert.AreEqual( 24f, component.Model.Bounds.Maxs.x, 0.01f );
			AssertCollision( component, MeshComponent.CollisionType.Hull );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void DestroyBeforeEnableDoesNotBuild()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var mesh = CreateBox();
			MeshComponent component;
			using ( CallbackBatch.Batch() )
			{
				component = scene.CreateObject().AddComponent<MeshComponent>();
				component.Mesh = mesh;
				component.Destroy();
			}
			Assert.IsTrue( mesh.IsDirty );
			Assert.IsNull( component.Model );
			Assert.AreEqual( 0, component.Shapes.Count );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void ParameterlessRebuildSkipsCollision()
	{
		var model = CreateBox().Rebuild();
		Assert.IsTrue( model.MeshCount > 0 );
		var parts = model.Physics?.Parts ?? [];
		Assert.AreEqual( 0, parts.Sum( p => p.Meshes.Count ) );
		Assert.AreEqual( 0, parts.Sum( p => p.Hulls.Count ) );
		Assert.IsTrue( model.Trace.Ray( new Vector3( 8, 8, 32 ), new Vector3( 8, 8, -16 ) ).Run().Hit );
	}
}
