using System.Linq;
using System.Text.Json.Nodes;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins the blob data contract the scene editor undo/redo snapshots rely on:
/// <see cref="GameObject.Deserialize"/> defers component PostDeserialize to the
/// <see cref="CallbackBatch"/> flush, so any blob context supplying that data (eg. a
/// MeshComponent's PolygonMesh) must outlive the batch. A per-object context disposed
/// before the flush would leave the mesh empty - the "create block, undo, redo,
/// block isn't there" bug.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SerializeBlobDataTest
{
	static GameObject CreateMeshObject()
	{
		var go = new GameObject( true, "Block" );
		var component = go.Components.Create<MeshComponent>();

		var mesh = new PolygonMesh();
		var a = mesh.AddVertex( new Vector3( 0, 0, 0 ) );
		var b = mesh.AddVertex( new Vector3( 64, 0, 0 ) );
		var c = mesh.AddVertex( new Vector3( 64, 64, 0 ) );
		var d = mesh.AddVertex( new Vector3( 0, 64, 0 ) );
		mesh.AddFace( a, b, c, d );

		component.Mesh = mesh;
		return go;
	}

	static JsonObject CaptureSnapshot( GameObject go )
	{
		// Same shape as SceneUndoSnapshot.GameObjectSnapshot capture
		using var blobs = BlobDataSerializer.Capture();
		var json = go.Serialize();
		blobs.SaveTo( json );
		return json;
	}

	[TestMethod]
	public void MeshSerializesAsBlobReference()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var state = CaptureSnapshot( CreateMeshObject() );
		var json = state.ToJsonString();

		Assert.IsTrue( json.Contains( "$blob" ), "Mesh should serialize as a blob reference under an active capture context" );
		Assert.IsTrue( state.ContainsKey( "__blobdata" ), "SaveTo should attach the blob data to the snapshot json" );
	}

	[TestMethod]
	public void UnchangedMeshSerializesIdentically()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = CreateMeshObject();

		// Serializing an unchanged mesh twice must produce byte-identical output ($blob id
		// and blob data), otherwise every scene save changes the file checksum and
		// triggers pointless recompiles.
		var state1 = CaptureSnapshot( go );
		var state2 = CaptureSnapshot( go );

		Assert.AreEqual( state1.ToJsonString(), state2.ToJsonString(), "Re-saving an unchanged mesh must produce identical output" );
	}

	[TestMethod]
	public void MeshSurvivesDeferredDeserialize()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = CreateMeshObject();
		var state = CaptureSnapshot( go );
		go.DestroyImmediate();

		// Same shape as the undo/redo lambdas: the shared blob context is declared before
		// the batch, so it's still open when the flush runs the deferred PostDeserialize.
		GameObject restored;
		using ( var blobs = BlobDataSerializer.LoadFromMemory( default ) )
		using ( CallbackBatch.Batch() )
		{
			restored = new GameObject();
			restored.DeserializeId( state );

			blobs.LoadFrom( state );
			restored.Deserialize( state, new GameObject.DeserializeOptions { IsRefreshing = true } );
		}

		var component = restored.Components.Get<MeshComponent>();
		Assert.IsNotNull( component );
		Assert.IsNotNull( component.Mesh, "Mesh should deserialize from blob data after the batch flush" );
		Assert.AreEqual( 4, component.Mesh.VertexHandles.Count() );
		Assert.AreEqual( 1, component.Mesh.FaceHandles.Count() );
	}

	[TestMethod]
	public void MultipleSnapshotsMergeIntoSharedContext()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		// Each object is captured in its own context, like the snapshot loop does
		var go1 = CreateMeshObject();
		var go2 = CreateMeshObject();
		var state1 = CaptureSnapshot( go1 );
		var state2 = CaptureSnapshot( go2 );
		go1.DestroyImmediate();
		go2.DestroyImmediate();

		GameObject restored1, restored2;
		using ( var blobs = BlobDataSerializer.LoadFromMemory( default ) )
		using ( CallbackBatch.Batch() )
		{
			restored1 = new GameObject();
			restored1.DeserializeId( state1 );
			restored2 = new GameObject();
			restored2.DeserializeId( state2 );

			blobs.LoadFrom( state1 );
			restored1.Deserialize( state1, new GameObject.DeserializeOptions { IsRefreshing = true } );
			blobs.LoadFrom( state2 );
			restored2.Deserialize( state2, new GameObject.DeserializeOptions { IsRefreshing = true } );
		}

		Assert.AreEqual( 4, restored1.Components.Get<MeshComponent>().Mesh?.VertexHandles.Count() );
		Assert.AreEqual( 4, restored2.Components.Get<MeshComponent>().Mesh?.VertexHandles.Count() );
	}

	[TestMethod]
	public void ComponentSnapshotPathRestoresMesh()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = CreateMeshObject();
		var component = go.Components.Get<MeshComponent>();

		// Same shape as SceneUndoSnapshot.ComponentSnapshot: capture, then restore with a
		// per-item context - valid because PostDeserialize is called synchronously inside it.
		JsonNode state;
		using ( var blobs = BlobDataSerializer.Capture() )
		{
			state = component.Serialize( new GameObject.SerializeOptions() );
			blobs.SaveTo( state );
		}

		component.Mesh = null;

		component.Deserialize( state.AsObject() );
		using ( BlobDataSerializer.LoadFrom( state ) )
		{
			component.PostDeserialize();
		}

		Assert.IsNotNull( component.Mesh );
		Assert.AreEqual( 4, component.Mesh.VertexHandles.Count() );
	}
}
