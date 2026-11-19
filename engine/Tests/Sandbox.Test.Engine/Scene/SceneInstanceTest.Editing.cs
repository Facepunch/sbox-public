using System.Text.Json.Nodes;

namespace SceneTests;

public partial class SceneInstanceTest
{
	/// <summary>Hierarchy drops capture the destination and its peers before moving existing objects.</summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void MoveExistingObjectIntoInstancePropagatesAndUndoes( bool linked, bool intoChild )
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var moved = linked ? CreateInstance() : new GameObject( _scene, true );
		moved.Name = "Moved";
		var movedId = moved.Id;
		var target = intoChild ? Content( root ) : root;
		var peerTarget = intoChild ? Content( copy ) : copy;
		using ( _session.UndoScope( "Change Game Object Order" )
			.WithGameObjectChanges( moved, GameObjectUndoFlags.Properties )
			.WithGameObjectChanges( target, GameObjectUndoFlags.Children ).Push() )
			moved.SetParent( target, true );

		var peerId = peerTarget.Children.Single( x => x.Name == "Moved" ).Id;
		Assert.AreSame( target, moved.Parent );
		Assert.AreNotEqual( movedId, peerId );
		Assert.AreEqual( linked, _session.IsInstance( _scene.Directory.FindByGuid( peerId ) ) );
		Undo();
		Assert.AreSame( _scene, _scene.Directory.FindByGuid( movedId ).Parent );
		Assert.IsNull( _scene.Directory.FindByGuid( peerId ) );
		Redo();
		Assert.AreSame( target, _scene.Directory.FindByGuid( movedId ).Parent );
		Assert.AreSame( peerTarget, _scene.Directory.FindByGuid( peerId ).Parent );
	}

	/// <summary>Dragging an existing linked copy into its source rejects direct and indirect cycles.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MoveExistingInstanceIntoItselfRollsBack( bool indirect )
	{
		var root = CreateInstance();
		var moved = root.Clone();
		if ( indirect ) moved = Group( moved, new GameObject( _scene, true, "Holder" ) );
		var state = _scene.InstanceState;
		var undoEntry = _session.UndoSystem.Back.Peek();
		var ids = _scene.GetAllObjects( false ).Select( x => x.Id ).ToArray();
		using ( _session.UndoScope( "Change Game Object Order" )
			.WithGameObjectChanges( moved, GameObjectUndoFlags.Properties )
			.WithGameObjectChanges( root, GameObjectUndoFlags.Children ).Push() )
			moved.SetParent( root, true );
		_scene.ProcessDeletes();

		Assert.AreSame( state, _scene.InstanceState );
		Assert.AreSame( undoEntry, _session.UndoSystem.Back.Peek() );
		Assert.AreSame( _scene, moved.Parent );
		CollectionAssert.AreEquivalent( ids, _scene.GetAllObjects( false ).Select( x => x.Id ).ToArray() );
	}

	/// <summary>Conflicting edits in different copies roll back atomically instead of choosing one.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ConflictingEditsAcrossCopiesRollBack( bool delete )
	{
		var first = new GameObject( _scene, true, "First" );
		first.AddComponent<InstanceComponent>().Value = 1;
		var second = new GameObject( _scene, true, "Second" );
		second.AddComponent<InstanceComponent>().Value = 1;
		var root = Group( first, second );
		var copy = root.Clone();
		var other = copy.Children.Single( x => x.Name == "Second" );
		var rootId = root.Id;
		var copyId = copy.Id;
		var state = _scene.InstanceState;
		var undoEntry = _session.UndoSystem.Back.Peek();
		var scope = _session.UndoScope( "Edit different copies" );
		if ( delete ) scope.WithGameObjectDestructions( [first, other] );
		else scope.WithComponentChanges( [Data( first ), Data( other )] );
		using ( scope.Push() )
		{
			if ( delete )
			{
				first.Destroy();
				other.Destroy();
			}
			else
			{
				Data( first ).Value = 42;
				Data( other ).Value = 71;
			}
		}
		_scene.ProcessDeletes();

		Assert.IsTrue( JsonNode.DeepEquals( Json.ToNode( state ), Json.ToNode( _scene.InstanceState ) ) );
		Assert.AreSame( undoEntry, _session.UndoSystem.Back.Peek() );
		foreach ( var id in new[] { rootId, copyId } )
		{
			var restored = _scene.Directory.FindByGuid( id );
			Assert.AreEqual( 2, restored.Children.Count );
			Assert.IsTrue( restored.Children.All( x => Data( x ).Value == 1 ) );
		}
	}

	/// <summary>The same property edit in multiple copies remains valid and reversible.</summary>
	[TestMethod]
	public void IdenticalEditsAcrossCopiesPropagateAndUndo()
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var peer = root.Clone();
		using ( _session.UndoScope( "Edit copies" ).WithComponentChanges( [Data( Content( root ) ), Data( Content( copy ) )] ).Push() )
		{
			Data( Content( root ) ).Value = 42;
			Data( Content( copy ) ).Value = 42;
		}
		Assert.AreEqual( 42, Data( Content( peer ) ).Value );
		Undo();
		Assert.IsTrue( new[] { root, copy, peer }.All( x => Data( Content( x ) ).Value == 1 ) );
		Redo();
		Assert.IsTrue( new[] { root, copy, peer }.All( x => Data( Content( x ) ).Value == 42 ) );
	}

	/// <summary>A definition lost during an edit cannot leave partially changed objects without undo.</summary>
	[TestMethod]
	public void MissingDefinitionDuringEditRollsBack()
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var state = _scene.InstanceState;
		var undoEntry = _session.UndoSystem.Back.Peek();
		using ( _session.UndoScope( "Edit with missing definition" ).WithComponentChanges( Data( Content( root ) ) ).Push() )
		{
			Data( Content( root ) ).Value = 42;
			_scene.InstanceState = state with { Definitions = state.Definitions.Remove( state.Instances[root.Id].Source ) };
		}
		Assert.AreSame( state, _scene.InstanceState );
		Assert.AreSame( undoEntry, _session.UndoSystem.Back.Peek() );
		Assert.AreEqual( 1, Data( Content( root ) ).Value );
		Assert.AreEqual( 1, Data( Content( copy ) ).Value );
	}

	/// <summary>Ordinary editing and creation must not synchronize an unrelated selected instance.</summary>
	[TestMethod]
	public void OrdinaryEditsAndUndoLeaveUnrelatedInstancesUnchanged()
	{
		var instance = CreateInstance();
		var copy = instance.Clone();
		var plain = new GameObject( _scene, true, "Ordinary" );
		var data = plain.AddComponent<InstanceComponent>();
		data.Value = 7;
		using ( _session.UndoScope( "Edit ordinary object" ).WithComponentChanges( data ).Push() )
			data.Value = 42;
		Undo();
		Assert.AreEqual( 7, data.Value );
		Redo();
		Assert.AreEqual( 42, data.Value );

		GameObject created;
		var instanceState = _scene.InstanceState;
		using ( _session.UndoScope( "Create outside selected instance" ).WithGameObjectCreations().Push() )
			created = new GameObject( _scene, true, "Outside" );
		Assert.AreSame( instanceState, _scene.InstanceState );
		var createdId = created.Id;
		Undo();
		Assert.IsNull( _scene.Directory.FindByGuid( createdId ) );
		Redo();
		Assert.AreSame( _scene, _scene.Directory.FindByGuid( createdId ).Parent );
		Assert.AreEqual( 1, Data( Content( instance ) ).Value );
		Assert.AreEqual( 1, Data( Content( copy ) ).Value );
		Assert.AreEqual( 1, instance.Children.Count );
		Assert.AreEqual( 1, copy.Children.Count );
	}

	/// <summary>Duplicating an instance restores the same links and child IDs through undo and redo.</summary>
	[TestMethod]
	public void DuplicateInstanceUndoRedoRestoresLinksAndIds()
	{
		var root = CreateInstance();
		GameObject copy;
		using ( _session.UndoScope( "Duplicate instance" ).WithGameObjectCreations().Push() )
			copy = root.Clone();
		var copyId = copy.Id;
		var childId = Content( copy ).Id;
		var source = _scene.InstanceState.Instances[root.Id].Source;
		Assert.AreEqual( source, _scene.InstanceState.Instances[copyId].Source );
		Undo();
		Assert.IsNull( _scene.Directory.FindByGuid( copyId ) );
		Assert.IsFalse( _scene.InstanceState.Instances.ContainsKey( copyId ) );
		Redo();
		copy = _scene.Directory.FindByGuid( copyId );
		Assert.IsTrue( _session.IsInstance( copy ) );
		Assert.AreEqual( source, _scene.InstanceState.Instances[copyId].Source );
		Assert.AreEqual( childId, Content( copy ).Id );
	}

	/// <summary>Deleting either the original or a copy leaves its peers editable and restores correctly.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DeleteInstanceUndoRedoKeepsPeersLinked( bool deleteCopy )
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var deleted = deleteCopy ? copy : root;
		var peer = deleteCopy ? root : copy;
		var deletedId = deleted.Id;
		var peerId = peer.Id;
		var childId = Content( deleted ).Id;
		using ( _session.UndoScope( "Delete instance" ).WithGameObjectDestructions( deleted ).Push() )
			deleted.Destroy();
		_scene.ProcessDeletes();
		Assert.IsTrue( _session.IsInstance( peer ) );
		Assert.IsFalse( _scene.ExportInstanceState( [deletedId, peerId] ).Instances.ContainsKey( deletedId ) );
		Undo();
		deleted = _scene.Directory.FindByGuid( deletedId );
		Assert.IsTrue( _session.IsInstance( deleted ) );
		Assert.AreEqual( childId, Content( deleted ).Id );
		Redo();
		Assert.IsNull( _scene.Directory.FindByGuid( deletedId ) );
		peer = _scene.Directory.FindByGuid( peerId );
		Assert.IsTrue( _session.IsInstance( peer ) );
		using ( _session.UndoScope( "Edit surviving copy" ).WithComponentChanges( Data( Content( peer ) ) ).Push() )
			Data( Content( peer ) ).Value = 42;
		Assert.AreEqual( 42, Data( Content( peer.Clone() ) ).Value );
	}

	/// <summary>Root settings stay local, including when restoring them through undo.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RootPropertiesStayIndependent( bool editCopy )
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var authored = editCopy ? copy : root;
		var peer = editCopy ? root : copy;
		var peerTransform = peer.WorldTransform;
		var peerName = peer.Name;
		var initialTransform = authored.WorldTransform;
		var initialName = authored.Name;
		using ( _session.UndoScope( "Move instance" ).WithGameObjectChanges( authored, GameObjectUndoFlags.Properties ).Push() )
		{
			authored.Name = "Independent";
			authored.WorldTransform = new Transform( new Vector3( 100, 200, 300 ), Rotation.FromYaw( 45 ), 2 );
			authored.Enabled = false;
			authored.Tags.Add( "local-setting" );
		}

		Assert.AreEqual( peerTransform, peer.WorldTransform );
		Assert.AreEqual( peerName, peer.Name );
		Assert.IsTrue( peer.Enabled );
		Assert.IsFalse( peer.Tags.Has( "local-setting" ) );
		Undo();
		Assert.AreEqual( initialTransform, authored.WorldTransform );
		Assert.AreEqual( initialName, authored.Name );
		Assert.IsTrue( authored.Enabled );
		Redo();
		Assert.AreEqual( "Independent", authored.Name );
		Assert.IsFalse( authored.Enabled );
		Assert.AreEqual( peerTransform, peer.WorldTransform );
	}

	/// <summary>Ordinary child properties remain shared, regardless of which copy is edited.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ChildPropertiesPropagateAndUndo( bool editCopy )
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var child = Content( editCopy ? copy : root );
		var peer = Content( editCopy ? root : copy );
		using ( _session.UndoScope( "Edit child" ).WithGameObjectChanges( child, GameObjectUndoFlags.Properties ).Push() )
		{
			child.Name = "Renamed";
			child.LocalPosition = new Vector3( 8, 16, 32 );
			child.Enabled = false;
		}

		Assert.AreEqual( "Renamed", peer.Name );
		Assert.AreEqual( child.LocalTransform, peer.LocalTransform );
		Assert.IsFalse( peer.Enabled );
		Undo();
		Assert.AreEqual( "Content", root.Children.Single().Name );
		Assert.AreEqual( "Content", copy.Children.Single().Name );
		Assert.IsTrue( root.Children.Single().Enabled );
		Redo();
		Assert.AreEqual( "Renamed", root.Children.Single().Name );
		Assert.IsFalse( copy.Children.Single().Enabled );
	}

	/// <summary>New disabled children and their components participate in propagation and undo.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CreateChildPropagatesAndUndoRedoPreservesIds( bool editCopy )
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var authored = editCopy ? copy : root;
		var peer = editCopy ? root : copy;
		using ( _session.UndoScope( "Add child" ).WithGameObjectCreations().Push() )
			new GameObject( authored, false, "Added" ).AddComponent<InstanceComponent>().Value = 17;

		var childId = authored.Children.Single( x => x.Name == "Added" ).Id;
		var peerChild = peer.Children.Single( x => x.Name == "Added" );
		var peerId = peerChild.Id;
		Assert.IsFalse( peerChild.Enabled );
		Assert.AreEqual( 17, Data( peerChild ).Value );
		Assert.AreNotEqual( childId, peerId );
		Undo();
		Assert.AreEqual( 1, authored.Children.Count );
		Assert.AreEqual( 1, peer.Children.Count );
		Redo();
		Assert.AreEqual( childId, authored.Children.Single( x => x.Name == "Added" ).Id );
		Assert.AreEqual( peerId, peer.Children.Single( x => x.Name == "Added" ).Id );
	}

	/// <summary>Deleting shared content is reversible in every copy.</summary>
	[TestMethod]
	public void DeleteChildPropagatesAndUndoRedoPreservesIds()
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var rootId = root.Id;
		var copyId = copy.Id;
		var childId = Content( root ).Id;
		var peerId = Content( copy ).Id;
		using ( _session.UndoScope( "Delete child" ).WithGameObjectDestructions( Content( root ) ).Push() )
			Content( root ).Destroy();
		_scene.ProcessDeletes();

		Assert.AreEqual( 0, root.Children.Count );
		Assert.AreEqual( 0, copy.Children.Count );
		Undo();
		root = _scene.Directory.FindByGuid( rootId );
		copy = _scene.Directory.FindByGuid( copyId );
		Assert.AreEqual( childId, Content( root ).Id );
		Assert.AreEqual( peerId, Content( copy ).Id );
		Redo();
		root = _scene.Directory.FindByGuid( rootId );
		copy = _scene.Directory.FindByGuid( copyId );
		Assert.AreEqual( 0, root.Children.Count );
		Assert.AreEqual( 0, copy.Children.Count );
	}

	/// <summary>Adding and deleting root or child components must not restore a same-type sibling by mistake.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ComponentCreationAndDeletionUndoOnlyTheIntendedComponent( bool onRoot )
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var owner = onRoot ? root : Content( root );
		var peer = onRoot ? copy : Content( copy );
		var ownerId = owner.Id;
		var peerOwnerId = peer.Id;
		InstanceComponent added;
		using ( _session.UndoScope( "Add component" ).WithComponentCreations().Push() )
		{
			added = owner.AddComponent<InstanceComponent>();
			added.Value = 99;
		}

		var id = added.Id;
		var peerId = peer.Components.GetAll<InstanceComponent>().Single( x => x.Value == 99 ).Id;
		Undo();
		Assert.IsFalse( owner.Components.GetAll<InstanceComponent>().Any( x => x.Value == 99 ) );
		Assert.IsFalse( peer.Components.GetAll<InstanceComponent>().Any( x => x.Value == 99 ) );
		Redo();
		added = (InstanceComponent)_scene.Directory.FindComponentByGuid( id );
		Assert.AreEqual( 99, added.Value );
		Assert.AreEqual( peerId, peer.Components.GetAll<InstanceComponent>().Single( x => x.Value == 99 ).Id );

		using ( _session.UndoScope( "Delete component" ).WithComponentDestructions( added ).Push() )
			added.Destroy();
		_scene.ProcessDeletes();
		Assert.IsFalse( peer.Components.GetAll<InstanceComponent>().Any( x => x.Value == 99 ) );
		Undo();
		Assert.AreEqual( 99, ((InstanceComponent)_scene.Directory.FindComponentByGuid( id )).Value );
		Assert.IsNotNull( _scene.Directory.FindComponentByGuid( peerId ) );
		Redo();
		Assert.IsNull( _scene.Directory.FindComponentByGuid( id ) );
		Assert.IsNull( _scene.Directory.FindComponentByGuid( peerId ) );
		if ( !onRoot )
		{
			Assert.AreEqual( 1, Data( _scene.Directory.FindByGuid( ownerId ) ).Value );
			Assert.AreEqual( 1, Data( _scene.Directory.FindByGuid( peerOwnerId ) ).Value );
		}
	}

	/// <summary>Hierarchy moves propagate without turning the moved child into a new object.</summary>
	[TestMethod]
	public void ReparentChildWithinInstancePropagatesAndUndoes()
	{
		var child = new GameObject( _scene, true, "Child" );
		var folder = new GameObject( _scene, true, "Folder" );
		var root = Group( child, folder );
		var copy = root.Clone();
		var peerChild = copy.Children.Single( x => x.Name == "Child" );
		using ( _session.UndoScope( "Reparent" ).WithGameObjectChanges( child, GameObjectUndoFlags.Properties ).Push() )
			child.SetParent( folder );

		Assert.AreSame( copy.Children.Single(), peerChild.Parent );
		Assert.AreEqual( "Folder", peerChild.Parent.Name );
		Undo();
		Assert.AreEqual( 2, root.Children.Count );
		Assert.AreEqual( 2, copy.Children.Count );
		Redo();
		Assert.AreEqual( "Folder", copy.Children.Single().Children.Single().Parent.Name );
	}

	/// <summary>Sibling order is shared content and survives undo.</summary>
	[TestMethod]
	public void ReorderChildrenPropagatesAndUndoes()
	{
		var first = new GameObject( _scene, true, "First" );
		var second = new GameObject( _scene, true, "Second" );
		var root = Group( first, second );
		var copy = root.Clone();
		using ( _session.UndoScope( "Reorder" ).WithGameObjectChanges( first, GameObjectUndoFlags.Properties ).Push() )
			second.AddSibling( first, false );

		CollectionAssert.AreEqual( new[] { "Second", "First" }, copy.Children.Select( x => x.Name ).ToArray() );
		Undo();
		CollectionAssert.AreEqual( new[] { "First", "Second" }, copy.Children.Select( x => x.Name ).ToArray() );
		Redo();
		CollectionAssert.AreEqual( new[] { "Second", "First" }, copy.Children.Select( x => x.Name ).ToArray() );
	}

	/// <summary>Making a copy unique is reversible and separates subsequent shared edits.</summary>
	[TestMethod]
	public void MakeUniqueUndoRedoSeparatesContent()
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var source = _scene.InstanceState.Instances[root.Id].Source;
		_session.MakeInstanceUnique( copy );
		var uniqueSource = _scene.InstanceState.Instances[copy.Id].Source;
		Assert.AreNotEqual( source, uniqueSource );
		Undo();
		Assert.AreEqual( source, _scene.InstanceState.Instances[copy.Id].Source );
		Redo();
		Assert.AreEqual( uniqueSource, _scene.InstanceState.Instances[copy.Id].Source );

		using ( _session.UndoScope( "Edit unique copy" ).WithComponentChanges( Data( Content( copy ) ) ).Push() )
			Data( Content( copy ) ).Value = 42;
		Assert.AreEqual( 1, Data( Content( root ) ).Value );
		Assert.AreEqual( 42, Data( Content( copy.Clone() ) ).Value );
	}

	/// <summary>Unlinking retains ordinary content while leaving the other copies linked.</summary>
	[TestMethod]
	public void UnlinkUndoRedoPreservesContentAndSeparatesEdits()
	{
		var root = CreateInstance();
		var copy = root.Clone();
		var child = Content( copy );
		_session.UnlinkInstance( copy );
		Assert.IsFalse( _session.IsInstance( copy ) );
		Assert.AreSame( child, Content( copy ) );
		Assert.IsTrue( _session.IsInstance( root ) );
		Undo();
		Assert.IsTrue( _session.IsInstance( copy ) );
		Redo();
		Assert.IsFalse( _session.IsInstance( copy ) );
		using ( _session.UndoScope( "Edit unlinked content" ).WithComponentChanges( Data( Content( copy ) ) ).Push() )
			Data( Content( copy ) ).Value = 42;
		Assert.AreEqual( 1, Data( Content( root ) ).Value );
		Assert.AreEqual( 42, Data( Content( copy ) ).Value );
	}

	/// <summary>Dissolving a group preserves world placement and leaves other instances intact.</summary>
	[TestMethod]
	public void UngroupUndoRedoRestoresHierarchyAndLinks()
	{
		var root = CreateInstance();
		root.WorldPosition = new Vector3( 64, 32, 16 );
		var copy = root.Clone();
		var copyId = copy.Id;
		var rootId = root.Id;
		var child = Content( root );
		var childId = child.Id;
		var transform = child.WorldTransform;
		_session.UngroupInstance( root );
		_scene.ProcessDeletes();
		Assert.AreSame( _scene, child.Parent );
		Assert.AreEqual( transform, child.WorldTransform );
		Assert.IsTrue( _session.IsInstance( _scene.Directory.FindByGuid( copyId ) ) );
		CollectionAssert.AreEqual( new[] { child }, _session.Selection.OfType<GameObject>().ToArray() );
		Undo();
		root = _scene.Directory.FindByGuid( rootId );
		Assert.IsTrue( _session.IsInstance( root ) );
		Assert.AreEqual( childId, Content( root ).Id );
		Assert.AreEqual( transform, Content( root ).WorldTransform );
		Redo();
		Assert.AreSame( _scene, _scene.Directory.FindByGuid( childId ).Parent );
		Assert.IsTrue( _session.IsInstance( _scene.Directory.FindByGuid( copyId ) ) );
	}

	/// <summary>Ungrouping must not silently discard components attached to the root.</summary>
	[TestMethod]
	public void UngroupWithRootComponentsDoesNothing()
	{
		var root = CreateInstance();
		using ( _session.UndoScope( "Add root component" ).WithComponentCreations().Push() )
			root.AddComponent<InstanceComponent>().Value = 42;
		var undoCount = _session.UndoSystem.Back.Count;
		_session.UngroupInstance( root );
		Assert.IsTrue( _session.IsInstance( root ) );
		Assert.AreSame( root, Content( root ).Parent );
		Assert.AreEqual( 42, Data( root ).Value );
		Assert.AreEqual( undoCount, _session.UndoSystem.Back.Count );
	}

	/// <summary>Rejecting recursive nesting restores both scene objects and authoring metadata.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RecursiveNestingRollsBackWithoutAnUndoEntry( bool indirect )
	{
		var root = CreateInstance();
		var nested = indirect ? Group( root.Clone(), new GameObject( _scene, true, "Holder" ) ) : root;
		var state = _scene.InstanceState;
		var undoEntry = _session.UndoSystem.Back.Peek();
		var ids = _scene.GetAllObjects( false ).Select( x => x.Id ).ToArray();
		using ( _session.UndoScope( "Recursive nesting" ).WithGameObjectCreations().Push() )
			nested.Clone().SetParent( root );
		_scene.ProcessDeletes();

		Assert.AreSame( state, _scene.InstanceState );
		Assert.AreSame( undoEntry, _session.UndoSystem.Back.Peek() );
		CollectionAssert.AreEquivalent( ids, _scene.GetAllObjects( false ).Select( x => x.Id ).ToArray() );
		Assert.AreEqual( 1, Data( Content( root ) ).Value );
	}
}
