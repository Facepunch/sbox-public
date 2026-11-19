using Editor;
using Sandbox.Internal;
using System;

namespace SceneTests;

/// <summary>Checks editable mesh identity and blob data through linked-instance workflows.</summary>
[TestClass]
[DoNotParallelize]
public class SceneInstanceMeshTests
{
	private Scene _scene;
	private EditingSession _session;
	private TypeLibrary _previousTypeLibrary;
	private IDisposable _sceneScope;
	private IDisposable _undoSounds;

	/// <summary>Runs authoring commands without creating an editor dock.</summary>
	private sealed class EditingSession( Scene scene ) : GameEditorSession( null, scene )
	{
		public override bool IsPlaying => false;
	}

	[TestInitialize]
	public void Initialize()
	{
		ThreadSafe.MarkMainThread();
		_previousTypeLibrary = Game.TypeLibrary;
		Game.TypeLibrary = SharedTypeLibrary.EngineAndTests;
		JsonUpgrader.UpdateUpgraders( Game.TypeLibrary );
		_scene = Scene.CreateEditorScene();
		_scene.WantsSystemScene = false;
		_sceneScope = _scene.Push();
		_session = new EditingSession( _scene );
		_undoSounds = _session.SuppressUndoSounds();
	}

	[TestCleanup]
	public void Cleanup()
	{
		_undoSounds?.Dispose();
		_sceneScope?.Dispose();
		_session?.Destroy();
		_scene?.Destroy();
		Game.TypeLibrary = _previousTypeLibrary;
		_undoSounds = null;
		_sceneScope = null;
		_session = null;
		_scene = null;
	}

	private GameObject CreateMeshInstance()
	{
		var mesh = new PolygonMesh();
		mesh.AddFace(
			mesh.AddVertex( new Vector3( 0, 0, 0 ) ),
			mesh.AddVertex( new Vector3( 32, 0, 0 ) ),
			mesh.AddVertex( new Vector3( 0, 32, 0 ) ) );
		var body = new GameObject( _scene, true, "Mesh" );
		var component = body.AddComponent<MeshComponent>();
		component.Collision = MeshComponent.CollisionType.None;
		component.Mesh = mesh;
		_session.Selection.Set( body );
		return _session.CreateInstance();
	}

	private static MeshComponent Mesh( GameObject root ) =>
		root.GetAllObjects( false ).SelectMany( x => x.Components.GetAll<MeshComponent>( FindMode.EverythingInSelf ) ).Single();

	private static void AssertVertex( MeshComponent component, Vector3 position )
	{
		Assert.AreEqual( 3, component.Mesh.VertexHandles.Count() );
		Assert.AreEqual( 1, component.Mesh.FaceHandles.Count() );
		Assert.AreEqual( position, component.Mesh.GetVertexPosition( component.Mesh.VertexHandleFromIndex( 0 ) ) );
	}

	/// <summary>Synchronizing geometry must not replace the authored mesh or invalidate the tool's handles.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MeshEditPreservesAuthoredHandlesAndUpdatesPeers( bool nested )
	{
		var source = CreateMeshInstance();
		var copy = source.Clone();
		var authored = Mesh( nested ? copy : source );
		if ( nested )
		{
			_session.Selection.Set( copy );
			_session.Selection.Add( new GameObject( _scene, true, "Holder" ) );
			_session.CreateInstance().Clone();
		}

		var mesh = authored.Mesh;
		var vertex = mesh.VertexHandleFromIndex( 0 );
		var face = mesh.FaceHandles.Single();
		var position = new Vector3( 0, 0, 16 );
		using ( _session.UndoScope( "Move shared vertex" ).WithComponentChanges( authored ).Push() )
		{
			mesh.SetVertexPosition( vertex, position );
			authored.RebuildMesh();
		}

		Assert.AreSame( mesh, authored.Mesh );
		Assert.IsTrue( vertex.IsValid );
		Assert.IsTrue( face.IsValid );
		foreach ( var peer in _scene.GetAllComponents<MeshComponent>() )
			AssertVertex( peer, position );
		AssertVertex( Mesh( source.Clone() ), position );
	}

	/// <summary>Shared mesh undo restores blob-backed geometry on all copies, not just the edited component.</summary>
	[TestMethod]
	public void MeshEditUndoRedoRestoresGeometryInEveryCopy()
	{
		var source = CreateMeshInstance();
		var copy = source.Clone();
		var authored = Mesh( copy );
		var position = new Vector3( 0, 0, 16 );
		using ( _session.UndoScope( "Move shared vertex" ).WithComponentChanges( authored ).Push() )
			authored.Mesh.SetVertexPosition( authored.Mesh.VertexHandleFromIndex( 0 ), position );

		Assert.IsTrue( _session.UndoSystem.Undo() );
		_scene.ProcessDeletes();
		AssertVertex( Mesh( source ), Vector3.Zero );
		AssertVertex( Mesh( copy ), Vector3.Zero );
		Assert.IsTrue( _session.UndoSystem.Redo() );
		_scene.ProcessDeletes();
		AssertVertex( Mesh( source ), position );
		AssertVertex( Mesh( copy ), position );
	}

	/// <summary>Scene files retain mesh data when instance references are loaded for editing or expanded at runtime.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SceneFileRoundTripPreservesSharedMeshData( bool runtime )
	{
		var source = CreateMeshInstance();
		var copy = source.Clone();
		var authored = Mesh( source );
		var position = new Vector3( 0, 0, 16 );
		using ( _session.UndoScope( "Move shared vertex" ).WithComponentChanges( authored ).Push() )
			authored.Mesh.SetVertexPosition( authored.Mesh.VertexHandleFromIndex( 0 ), position );
		var file = _scene.CreateSceneFile();
		var loaded = runtime ? new Scene() : Scene.CreateEditorScene();
		try
		{
			var options = new SceneLoadOptions { ShowLoadingScreen = false };
			Assert.IsTrue( options.SetScene( file ) );
			Assert.IsTrue( loaded.Load( options ) );
			AssertVertex( Mesh( loaded.Directory.FindByGuid( source.Id ) ), position );
			AssertVertex( Mesh( loaded.Directory.FindByGuid( copy.Id ) ), position );
			Assert.AreEqual( !runtime, loaded.InstanceState.Instances.Any() );
		}
		finally { loaded.Destroy(); }
	}
}
