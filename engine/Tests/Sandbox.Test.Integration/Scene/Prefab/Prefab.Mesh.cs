using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace SceneTests.Prefab;

public partial class InstancesTest
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ApplyingMeshInstanceToPrefabPreservesGeometry( bool ambientCapture )
	{
		const string path = "___prefab_mesh_apply.prefab";
		var scene = new Scene();
		using var scope = scene.Push();
		var instance = CreateMeshBlock( "ApplyMesh" );
		Editor.EditorUtility.Prefabs.ConvertGameObjectToPrefab( instance, path, true );
		var file = ResourceLibrary.Get<PrefabFile>( path );
		try
		{
			for ( var i = 0; i < 2; i++ )
			{
				using ( var capture = ambientCapture ? BlobDataSerializer.Capture() : null )
					Editor.EditorUtility.Prefabs.WriteInstanceToPrefab( instance, skipDiskWrite: true );
				instance.UpdateFromPrefab();
				var clone = GameObject.GetPrefab( path ).Clone();
				foreach ( var root in new[] { instance, clone } )
				{
					Assert.IsTrue( root.IsOutermostPrefabInstanceRoot );
					var mesh = root.Components.Get<MeshComponent>().Mesh;
					Assert.AreEqual( 4, mesh.VertexHandles.Count() );
					Assert.AreEqual( 1, mesh.FaceHandles.Count() );
				}
				clone.Destroy();
			}
		}
		finally
		{
			Game.Resources.Unregister( file );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void UnchangedMeshPrefabStaysUnmodifiedAfterRefreshAndRevert( bool binarySource )
	{
		const string path = "___prefab_mesh_unchanged.prefab";
		var scene = new Scene();
		using var scope = scene.Push();
		var source = CreateMeshBlock( "UnchangedMesh" );
		JsonObject root;
		byte[] data;
		using ( var capture = binarySource ? BlobDataSerializer.Capture() : null )
		{
			root = source.Serialize();
			data = capture?.ToByteArray();
		}
		var file = new PrefabFile { RootObject = root, BinaryData = data };
		file.RegisterWeakResourceId( path );
		file.Register( path );
		try
		{
			var instance = GameObject.GetPrefab( path ).Clone();
			for ( var i = 0; i < 3; i++ )
			{
				using ( BlobDataSerializer.Capture() )
					instance.PrefabInstance.RefreshPatch();
				Assert.IsFalse( Editor.EditorUtility.Prefabs.IsInstanceModified( instance ) );
				Assert.IsFalse( Editor.EditorUtility.Prefabs.IsGameObjectInstanceModified( instance ) );
				Editor.EditorUtility.Prefabs.RevertGameObjectInstanceChanges( instance );
				instance.UpdateFromPrefab();
				Assert.AreEqual( 4, instance.Components.Get<MeshComponent>().Mesh.VertexHandles.Count() );
			}
		}
		finally
		{
			Game.Resources.Unregister( file );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void PrefabMeshSurvivesEditingSavingAndReopening( bool ambientCapture )
	{
		const string path = "___prefab_mesh_resave.prefab";
		var scene = new Scene();
		using var scope = scene.Push();
		var source = new GameObject( "MeshPrefab" );
		var child = CreateMeshBlock( "MeshChild" );
		child.Parent = source;
		var vertices = child.Components.Get<MeshComponent>().Mesh.VertexHandles.Count();
		var faces = child.Components.Get<MeshComponent>().Mesh.FaceHandles.Count();
		Assert.IsTrue( vertices > 0 && faces > 0 );
		using var registration = Helpers.RegisterPrefabFromJson( path, source.Serialize().ToJsonString() );
		var file = ResourceLibrary.Get<PrefabFile>( path );
		var instance = GameObject.GetPrefab( path ).Clone();
		var editor = PrefabScene.CreateForEditing();
		try
		{
			Assert.IsTrue( editor.Load( file ) );
			for ( var i = 0; i < 2; i++ )
			{
				editor.Children.Single().Enabled = i != 0;
				using ( var capture = ambientCapture ? BlobDataSerializer.Capture() : null )
					editor.ToPrefabFile();

				// Round-trip the saved JSON after the save context has ended.
				file.RootObject = JsonNode.Parse( file.RootObject.ToJsonString() ).AsObject();
				Assert.IsTrue( editor.Load( file ) );
				instance.UpdateFromPrefab();
				foreach ( var root in new GameObject[] { editor, instance } )
				{
					var meshChild = root.Children.Single();
					Assert.AreEqual( i != 0, meshChild.Enabled );
					var component = meshChild.Components.Get<MeshComponent>( includeDisabled: true );
					Assert.IsNotNull( component );
					var mesh = component.Mesh;
					Assert.AreEqual( vertices, mesh.VertexHandles.Count() );
					Assert.AreEqual( faces, mesh.FaceHandles.Count() );
				}
			}
		}
		finally
		{
			editor.Destroy();
		}
	}

}
