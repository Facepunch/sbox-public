using Editor;
using System.Text.Json.Nodes;

namespace SceneTests;

public partial class SceneInstanceTest
{
	/// <summary>Hammer reads both clipboard formats as expanded objects without importing scene links.</summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void ClipboardObjectsCanBePastedWithoutAuthoringLinks( bool linked, bool malformedLinks )
	{
		var root = CreateReferencedHierarchy( false );
		var text = linked ? Copy( root ) : Json.Serialize( new[] { root.Serialize() } );
		if ( malformedLinks )
		{
			var package = JsonNode.Parse( text ).AsObject();
			package["Instances"] = "broken";
			text = package.ToJsonString();
		}
		var destination = Scene.CreateEditorScene();
		try
		{
			using var sceneScope = destination.Push();
			var objects = SceneInstanceClipboard.ReadObjects( text );
			var pasted = SceneInstanceClipboard.Paste( destination, objects.Single(), SceneInstanceState.Empty );
			AssertNestedReferences( pasted, false );
			Assert.AreNotEqual( root.Id, pasted.Id );
			Assert.IsTrue( destination.InstanceState.Instances.IsEmpty );
		}
		finally { destination.Destroy(); }
	}

	private string Copy( params GameObject[] roots ) =>
		SceneInstanceClipboard.Write( _scene, roots, roots.Select( x => x.Serialize() ) );

	/// <summary>Uses the editor's clipboard format and creation scope, without depending on the OS clipboard.</summary>
	private static GameObject[] Paste( EditingSession session, string text )
	{
		using var sceneScope = session.Scene.Push();
		var (objects, instances) = SceneInstanceClipboard.Read( session.Scene, text );
		using var undo = session.UndoScope( "Paste" ).WithGameObjectCreations().Push();
		return objects.Select( x => SceneInstanceClipboard.Paste( session.Scene, x, instances ) ).ToArray();
	}

	/// <summary>Undoing a nested paste removes its links, and redo restores references with the same IDs.</summary>
	[TestMethod]
	public void NestedPasteUndoRedoRestoresReferencesAndIds()
	{
		var original = CreateReferencedHierarchy( false );
		var pasted = Paste( _session, Copy( original ) ).Single();
		var pastedId = pasted.Id;
		var ids = pasted.GetAllObjects( false ).Select( x => x.Id ).ToArray();
		Undo();
		Assert.IsNull( _scene.Directory.FindByGuid( pastedId ) );
		Assert.IsFalse( ids.Any( _scene.InstanceState.Instances.ContainsKey ) );
		Redo();
		pasted = _scene.Directory.FindByGuid( pastedId );
		AssertNestedReferences( pasted, false );
		CollectionAssert.AreEqual( ids, pasted.GetAllObjects( false ).Select( x => x.Id ).ToArray() );
	}

	/// <summary>Same-scene paste uses current shared content, not the snapshot on the clipboard.</summary>
	[TestMethod]
	public void SameScenePasteUsesLatestDefinitionAndKeepsCopiesLinked()
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var text = Copy( root, copy );
		AddLateChild( root );
		using ( _session.UndoScope( "Edit after copying" ).WithComponentChanges( Data( Content( root ) ) ).Push() )
		{
			Data( Content( root ) ).Value = 42;
			Data( Content( root ) ).Target = LateChildren( root ).Single();
		}

		var pasted = Paste( _session, text );
		Assert.AreEqual( 2, pasted.Length );
		foreach ( var instance in pasted )
		{
			Assert.AreEqual( _scene.InstanceState.Instances[root.Id].Source, _scene.InstanceState.Instances[instance.Id].Source );
			Assert.AreEqual( 42, Data( Content( instance ) ).Value );
			Assert.AreSame( LateChildren( instance ).Single(), Data( Content( instance ) ).Target );
		}

		using ( _session.UndoScope( "Edit pasted copy" ).WithComponentChanges( Data( Content( pasted[0] ) ) ).Push() )
			Data( Content( pasted[0] ) ).Value = 71;
		Assert.AreEqual( 71, Data( Content( root ) ).Value );
		Assert.AreEqual( 71, Data( Content( pasted[1] ) ).Value );
	}

	/// <summary>Each cross-scene paste forks once, sharing within the batch but not with other pastes.</summary>
	[TestMethod]
	public void CrossScenePasteForksOncePerBatchAndUndoesLinkedEdits()
	{
		var root = CreateInstance();
		var text = Copy( root, root.Clone() );
		var destination = new EditingSession( Scene.CreateEditorScene() );
		using var sounds = destination.SuppressUndoSounds();
		try
		{
			var first = Paste( destination, text );
			var second = Paste( destination, text );
			var state = destination.Scene.InstanceState;
			Assert.AreEqual( state.Instances[first[0].Id].Source, state.Instances[first[1].Id].Source );
			Assert.AreNotEqual( state.Instances[first[0].Id].Source, state.Instances[second[0].Id].Source );
			Assert.AreNotEqual( _scene.InstanceState.Instances[root.Id].Source, state.Instances[first[0].Id].Source );
			using ( destination.UndoScope( "Edit imported copy" ).WithComponentChanges( Data( Content( first[0] ) ) ).Push() )
				Data( Content( first[0] ) ).Value = 42;
			Assert.AreEqual( 42, Data( Content( first[1] ) ).Value );
			Assert.AreEqual( 1, Data( Content( second[0] ) ).Value );
			Assert.AreEqual( 1, Data( Content( root ) ).Value );
			Assert.IsTrue( destination.UndoSystem.Undo() );
			destination.Scene.ProcessDeletes();
			Assert.AreEqual( 1, Data( Content( first[1] ) ).Value );
			Assert.IsTrue( destination.UndoSystem.Redo() );
			destination.Scene.ProcessDeletes();
			Assert.AreEqual( 42, Data( Content( first[1] ) ).Value );
		}
		finally { destination.Destroy(); }
	}

	/// <summary>Clipboard import preserves late object and component references inside or outside nested instances.</summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void ClipboardPreservesNestedReferences( bool foreign, bool external )
	{
		var original = CreateReferencedHierarchy( external );
		var text = Copy( original );
		var destination = foreign ? new EditingSession( Scene.CreateEditorScene() ) : _session;
		try
		{
			var pasted = Paste( destination, text ).Single();
			AssertNestedReferences( pasted, external );
			Assert.AreNotEqual( original.Id, pasted.Id );
		}
		finally
		{
			if ( foreign ) destination.Destroy();
		}
	}

	/// <summary>Ordinary selections retain the legacy JSON array format even in scenes containing instances.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OrdinaryClipboardKeepsLegacyFormatAndReferenceRemapping( bool includeInstances )
	{
		if ( includeInstances ) CreateInstance();
		var root = new GameObject( _scene, true, "Ordinary" );
		var child = new GameObject( root, true, "Child" );
		var data = root.AddComponent<InstanceComponent>();
		data.Target = child;
		data.Other = data;
		var count = _scene.InstanceState.Instances.Count;
		var text = Copy( root );
		Assert.IsInstanceOfType<JsonArray>( JsonNode.Parse( text ) );
		var pasted = Paste( _session, text ).Single();

		Assert.AreSame( pasted.Children.Single(), Data( pasted ).Target );
		Assert.AreSame( Data( pasted ), Data( pasted ).Other );
		Assert.AreNotEqual( root.Id, pasted.Id );
		Assert.AreNotEqual( child.Id, pasted.Children.Single().Id );
		Assert.AreEqual( count, _scene.InstanceState.Instances.Count );
	}
}
