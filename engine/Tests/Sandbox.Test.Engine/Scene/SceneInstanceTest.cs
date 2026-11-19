using Editor;
using Sandbox.Internal;
using System;
using System.Runtime.CompilerServices;

namespace SceneTests;

/// <summary>Exercises scene-local instances through the editor's normal commands and undo scopes.</summary>
[TestClass]
[DoNotParallelize]
public partial class SceneInstanceTest : SceneTest
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

	/// <summary>Simple shared data and references, without rendering or physics dependencies.</summary>
	public sealed class InstanceComponent : Component
	{
		[Property] public int Value { get; set; }
		[Property] public GameObject Target { get; set; }
		[Property] public InstanceComponent Other { get; set; }
	}

	[TestInitialize]
	public void Initialize()
	{
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

	/// <summary>Pending edit notifications must not retain a closed session and its undo history.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DestroyedEditingSessionsCanBeCollected( bool edited )
	{
		var reference = CreateDestroyedEditingSession( edited );
		GC.Collect();
		GC.WaitForPendingFinalizers();
		MainThread.RunQueues();
		GC.Collect();
		Assert.IsFalse( reference.IsAlive );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static WeakReference CreateDestroyedEditingSession( bool edited )
	{
		var session = new EditingSession( Scene.CreateEditorScene() );
		if ( edited ) session.HasUnsavedChanges = true;
		session.Destroy();
		return new WeakReference( session );
	}

	/// <summary>Creates linked content using the same grouping command as the editor.</summary>
	private GameObject CreateInstance()
	{
		var content = new GameObject( _scene, true, "Content" );
		content.AddComponent<InstanceComponent>().Value = 1;
		return Group( content );
	}

	private GameObject Group( params GameObject[] objects )
	{
		_session.Selection.Clear();
		foreach ( var gameObject in objects )
			_session.Selection.Add( gameObject );
		var root = _session.CreateInstance();
		Assert.IsNotNull( root );
		return root;
	}

	private static GameObject Content( GameObject root ) => root.Children.Single( x => x.Name == "Content" );
	private static InstanceComponent Data( GameObject gameObject ) => gameObject.Components.Get<InstanceComponent>( FindMode.EverythingInSelf );

	private void Undo()
	{
		Assert.IsTrue( _session.UndoSystem.Undo() );
		_scene.ProcessDeletes();
	}

	private void Redo()
	{
		Assert.IsTrue( _session.UndoSystem.Redo() );
		_scene.ProcessDeletes();
	}

	/// <summary>Grouping keeps selected objects in place, without duplicating selected descendants.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CreateFromSelectionPreservesHierarchyAndWorldTransforms( bool multiple )
	{
		var parent = new GameObject( _scene, true, "Parent" );
		parent.WorldTransform = new Transform( new Vector3( 32, 64, 16 ), Rotation.FromYaw( 30 ), 2 );
		var first = new GameObject( parent, true, "First" );
		first.LocalPosition = new Vector3( 10, 20, 30 );
		var descendant = new GameObject( first, true, "Descendant" );
		var second = new GameObject( parent, true, "Second" );
		second.LocalPosition = new Vector3( -20, 0, 40 );
		var firstTransform = first.WorldTransform;
		var secondTransform = second.WorldTransform;

		var root = multiple ? Group( first, descendant, second ) : Group( first, descendant );

		Assert.IsTrue( _session.IsInstance( root ) );
		Assert.IsFalse( root.IsPrefabInstance );
		Assert.AreSame( parent, root.Parent );
		Assert.AreSame( root, first.Parent );
		Assert.AreSame( first, descendant.Parent );
		Assert.AreSame( multiple ? root : parent, second.Parent );
		Assert.AreEqual( multiple ? 2 : 1, root.Children.Count );
		Assert.AreEqual( firstTransform, first.WorldTransform );
		Assert.AreEqual( secondTransform, second.WorldTransform );
		CollectionAssert.AreEqual( new[] { root }, _session.Selection.OfType<GameObject>().ToArray() );
	}

	/// <summary>Undoing creation restores ordinary objects, and redo restores the same instance identities.</summary>
	[TestMethod]
	public void CreateInstanceUndoRedoRestoresLinksAndSelection()
	{
		var content = new GameObject( _scene, true, "Content" );
		var contentId = content.Id;
		var root = Group( content );
		var rootId = root.Id;
		var source = _scene.InstanceState.Instances[rootId].Source;

		Undo();
		Assert.AreSame( _scene, _scene.Directory.FindByGuid( contentId ).Parent );
		Assert.IsTrue( _scene.InstanceState.Instances.IsEmpty );
		CollectionAssert.AreEqual( new[] { contentId }, _session.Selection.OfType<GameObject>().Select( x => x.Id ).ToArray() );

		Redo();
		root = _scene.Directory.FindByGuid( rootId );
		Assert.IsTrue( _session.IsInstance( root ) );
		Assert.AreSame( root, _scene.Directory.FindByGuid( contentId ).Parent );
		Assert.AreEqual( source, _scene.InstanceState.Instances[rootId].Source );
		CollectionAssert.AreEqual( new[] { root }, _session.Selection.OfType<GameObject>().ToArray() );
	}

	/// <summary>Editing either copy updates its peers without replacing authored objects or components.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ComponentEditPropagatesFromEitherCopyAndUndoes( bool editCopy )
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var authored = Data( Content( editCopy ? copy : root ) );
		var peer = Data( Content( editCopy ? root : copy ) );
		using ( _session.UndoScope( "Edit content" ).WithComponentChanges( authored ).Push() )
			authored.Value = 42;

		Assert.AreSame( authored, Data( Content( editCopy ? copy : root ) ) );
		Assert.AreSame( peer, Data( Content( editCopy ? root : copy ) ) );
		Assert.AreEqual( 42, peer.Value );
		Undo();
		Assert.AreEqual( 1, Data( Content( root ) ).Value );
		Assert.AreEqual( 1, Data( Content( copy ) ).Value );
		Redo();
		Assert.AreEqual( 42, Data( Content( root ) ).Value );
		Assert.AreEqual( 42, Data( Content( copy ) ).Value );
	}
}
