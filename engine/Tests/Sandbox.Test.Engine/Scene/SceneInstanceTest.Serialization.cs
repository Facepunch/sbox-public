using System;
using System.Text.Json.Nodes;

namespace SceneTests;

public partial class SceneInstanceTest
{
	/// <summary>Maps and other direct SceneFile readers do not need scene-instance authoring data.</summary>
	[TestMethod]
	public void SceneFileObjectsContainCompleteNestedInstances()
	{
		var original = CreateReferencedHierarchy( false );
		var file = _scene.CreateSceneFile();
		var loaded = new Scene();
		try
		{
			using var sceneScope = loaded.Push();
			using ( CallbackBatch.Isolated() )
			{
				foreach ( var json in file.GameObjects )
					new GameObject( loaded, false ).Deserialize( json );
			}
			AssertNestedReferences( loaded.Directory.FindByGuid( original.Id ), false );
			Assert.IsTrue( loaded.InstanceState.Instances.IsEmpty );
		}
		finally { loaded.Destroy(); }
	}

	/// <summary>Saving and entering play preserve edits made without an editor undo scope.</summary>
	[TestMethod]
	[DataRow( false, false, false )]
	[DataRow( false, false, true )]
	[DataRow( false, true, false )]
	[DataRow( false, true, true )]
	[DataRow( true, false, false )]
	[DataRow( true, false, true )]
	[DataRow( true, true, false )]
	[DataRow( true, true, true )]
	public void SavePreservesUnscopedEditsWithoutSynchronizingCopies( bool editCopy, bool nested, bool runtime )
	{
		var root = CreateInstance();
		var copy = root.Clone();
		if ( nested )
			Group( copy, new GameObject( _scene, true, "Holder" ) ).Clone();
		var authored = editCopy ? copy : root;
		Data( Content( editCopy ? root : copy ) ).Value = 71;
		Data( Content( authored ) ).Value = 42;
		new GameObject( authored, false, "Added outside undo" );
		var before = _scene.Serialize();
		var state = _scene.InstanceState;
		var values = _scene.GetAllComponents<InstanceComponent>().ToDictionary( x => x.Id, x => x.Value );
		var file = _scene.CreateSceneFile();
		Assert.AreSame( state, _scene.InstanceState );
		Assert.IsTrue( JsonNode.DeepEquals( before, _scene.Serialize() ) );
		var loaded = runtime ? new Scene() : Scene.CreateEditorScene();
		try
		{
			Assert.IsTrue( loaded.Load( file ) );
			foreach ( var id in new[] { root.Id, copy.Id } )
			{
				var restored = loaded.Directory.FindByGuid( id );
				Assert.AreEqual( id == authored.Id ? 2 : 1, restored.Children.Count );
			}
			foreach ( var (id, value) in values )
				Assert.AreEqual( value, ((InstanceComponent)loaded.Directory.FindComponentByGuid( id )).Value );
		}
		finally { loaded.Destroy(); }
	}

	/// <summary>A recursive no-undo hierarchy edit cannot block saving or entering play.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SavePreservesRecursiveUnscopedHierarchy( bool runtime )
	{
		var root = CreateInstance();
		var copy = root.Clone();
		copy.SetParent( root );
		var state = _scene.InstanceState;
		var file = _scene.CreateSceneFile();
		Assert.AreSame( state, _scene.InstanceState );
		Assert.AreSame( root, copy.Parent );
		var loaded = runtime ? new Scene() : Scene.CreateEditorScene();
		try
		{
			Assert.IsTrue( loaded.Load( file ) );
			Assert.AreSame( loaded.Directory.FindByGuid( root.Id ), loaded.Directory.FindByGuid( copy.Id ).Parent );
			Assert.AreEqual( 1, Data( Content( loaded.Directory.FindByGuid( copy.Id ) ) ).Value );
		}
		finally { loaded.Destroy(); }
	}

	/// <summary>Malformed authoring metadata cannot prevent the saved objects from loading.</summary>
	[TestMethod]
	[DataRow( false, "\"broken\"" )]
	[DataRow( true, "\"broken\"" )]
	[DataRow( false, "{}" )]
	[DataRow( true, "{}" )]
	[DataRow( false, "null" )]
	[DataRow( true, "null" )]
	[DataRow( false, "{\"Definitions\":{},\"Instances\":null}" )]
	[DataRow( true, "{\"Definitions\":{},\"Instances\":null}" )]
	public void MalformedInstanceMetadataDoesNotBlockLoad( bool runtime, string metadata )
	{
		var root = CreateInstance();
		Data( Content( root ) ).Value = 42;
		var file = _scene.CreateSceneFile();
		file.SceneProperties[Scene.InstancesKey] = JsonNode.Parse( metadata );
		var loaded = runtime ? new Scene() : Scene.CreateEditorScene();
		try
		{
			Assert.IsTrue( loaded.Load( file ) );
			Assert.AreEqual( 42, Data( Content( loaded.Directory.FindByGuid( root.Id ) ) ).Value );
			Assert.IsTrue( loaded.InstanceState.Instances.IsEmpty );
			loaded.CreateSceneFile();
		}
		finally { loaded.Destroy(); }
	}

	/// <summary>Broken links keep their saved contents without preventing valid instances from loading.</summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void InvalidDefinitionLoadsSavedContents( bool recursive, bool runtime )
	{
		var broken = CreateInstance();
		var valid = CreateInstance();
		var file = _scene.CreateSceneFile();
		var state = _scene.InstanceState;
		var instance = state.Instances[broken.Id];
		var definition = state.Definitions[instance.Source];
		var definitions = recursive
			? state.Definitions.SetItem( instance.Source, definition with
			{
				Instances = definition.Instances.Add( definition.Object[GameObject.JsonKeys.Id].GetValue<Guid>(), instance with { Objects = instance.Objects.Clear() } )
			} )
			: state.Definitions.Remove( instance.Source );
		file.SceneProperties[Scene.InstancesKey] = Json.ToNode( state with { Definitions = definitions } );
		var loaded = runtime ? new Scene() : Scene.CreateEditorScene();
		try
		{
			Assert.IsTrue( loaded.Load( file ) );
			Assert.AreEqual( 1, Data( Content( loaded.Directory.FindByGuid( broken.Id ) ) ).Value );
			Assert.AreEqual( 1, Data( Content( loaded.Directory.FindByGuid( valid.Id ) ) ).Value );
			Assert.IsFalse( loaded.InstanceState.Instances.ContainsKey( broken.Id ) );
			Assert.AreEqual( !runtime, loaded.InstanceState.Instances.ContainsKey( valid.Id ) );
			// Saving the recovered scene must not attempt to export the broken definition.
			Data( Content( loaded.Directory.FindByGuid( valid.Id ) ) ).Value = 42;
			loaded.CreateSceneFile();
		}
		finally { loaded.Destroy(); }
	}

	/// <summary>Scene files preserve deep references in both editable and expanded runtime scenes.</summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void SceneFileRoundTripPreservesNestedReferences( bool runtime, bool external )
	{
		var original = CreateReferencedHierarchy( external );
		var file = _scene.CreateSceneFile();
		var loaded = runtime ? new Scene() : Scene.CreateEditorScene();
		try
		{
			var options = new SceneLoadOptions { ShowLoadingScreen = false };
			Assert.IsTrue( options.SetScene( file ) );
			Assert.IsTrue( loaded.Load( options ) );
			var restored = loaded.Directory.FindByGuid( original.Id );
			Assert.IsNotNull( restored );
			AssertNestedReferences( restored, external );
			if ( runtime )
			{
				Assert.IsTrue( loaded.InstanceState.Instances.IsEmpty );
				Assert.IsTrue( loaded.InstanceState.Definitions.IsEmpty );
				Assert.IsFalse( loaded.SerializeProperties().ContainsKey( Scene.InstancesKey ) );
			}
			else
			{
				Assert.AreEqual( _scene.InstanceState.Instances.Count, loaded.InstanceState.Instances.Count );
				using var sceneScope = loaded.Push();
				AssertNestedReferences( restored.Clone(), external );
			}
		}
		finally { loaded.Destroy(); }
	}

	/// <summary>Ordinary scene serialization remains unchanged when instances are present elsewhere.</summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OrdinaryObjectsAndReferencesRoundTripAlongsideInstances( bool includeInstances )
	{
		if ( includeInstances ) CreateInstance();
		var plain = new GameObject( _scene, true, "Ordinary" );
		var child = new GameObject( plain, true, "Ordinary child" );
		var data = plain.AddComponent<InstanceComponent>();
		data.Value = 42;
		data.Target = child;
		data.Other = data;
		var file = _scene.CreateSceneFile();
		var loaded = Scene.CreateEditorScene();
		try
		{
			Assert.IsTrue( loaded.Load( file ) );
			var restored = loaded.Directory.FindByGuid( plain.Id );
			Assert.AreEqual( 42, Data( restored ).Value );
			Assert.AreSame( restored.Children.Single(), Data( restored ).Target );
			Assert.AreSame( Data( restored ), Data( restored ).Other );
			Assert.IsFalse( loaded.InstanceState.Instances.ContainsKey( restored.Id ) );
			Assert.AreEqual( includeInstances, file.SceneProperties.ContainsKey( Scene.InstancesKey ) );
		}
		finally { loaded.Destroy(); }
	}

	/// <summary>Undoing and redoing a reference edit preserves mappings used by later clones.</summary>
	[TestMethod]
	public void NestedReferenceUndoRedoKeepsSubsequentClonesValid()
	{
		var (source, outer) = CreateNestedInstances( 3, 2 );
		AddLateChild( source );
		var holder = outer.Children.Single( x => x.Name == "Holder" );
		var reference = holder.AddComponent<InstanceComponent>();
		using ( _session.UndoScope( "Set nested reference" ).WithComponentChanges( reference ).Push() )
		{
			var targets = LateChildren( outer );
			reference.Target = targets[0];
			reference.Other = Data( targets[^1] );
		}
		Undo();
		holder = outer.Children.Single( x => x.Name == "Holder" );
		Assert.IsNull( Data( holder ).Target );
		Assert.IsNull( Data( holder ).Other );
		Redo();
		AssertNestedReferences( outer.Clone(), false );
	}

	/// <summary>Cloning into another scene forks sources rather than retaining cross-scene links.</summary>
	[TestMethod]
	public void CrossSceneCloneForksNestedSourcesAndPreservesReferences()
	{
		var original = CreateReferencedHierarchy( false );
		var destination = Scene.CreateEditorScene();
		try
		{
			using var sceneScope = destination.Push();
			var clone = original.Clone( original.WorldTransform, destination );
			AssertNestedReferences( clone, false );
			var sourceIds = _scene.InstanceState.Definitions.Keys.ToArray();
			Assert.IsFalse( destination.InstanceState.Definitions.Keys.Intersect( sourceIds ).Any() );
			Assert.AreSame( destination, clone.Scene );
		}
		finally { destination.Destroy(); }
	}
}
