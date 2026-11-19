namespace Editor;

public partial class SceneEditorSession
{
	/// <summary>Whether an object is a scene-local instance root.</summary>
	public bool IsInstance( GameObject gameObject ) =>
		gameObject.IsValid() && !gameObject.IsDestroyed && gameObject.Scene == Scene &&
		Scene.InstanceState.Instances.ContainsKey( gameObject.Id );

	/// <summary>Groups the selection into a linked instance without creating an asset.</summary>
	public GameObject CreateInstance()
	{
		if ( IsPrefabSession || IsPlaying ) return null;
		var selected = Selection.OfType<GameObject>().Where( x => x != Scene ).ToHashSet();
		var objects = selected.Where( x => !selected.Any( other => other != x && x.IsAncestor( other ) ) ).ToArray();
		if ( objects.Length == 0 || (objects.Length == 1 && IsInstance( objects[0] )) ) return null;
		if ( objects.Any( x => x.Parent.IsValid() && x.Parent.IsPrefabInstance ) )
		{
			Log.Warning( "Unlink the containing prefab before creating a scene instance inside it." );
			return null;
		}

		using var sceneScope = Scene.Push();
		using var undo = UndoScope( "Create Instance" )
			.WithGameObjectChanges( objects, GameObjectUndoFlags.Properties )
			.WithGameObjectCreations().Push();

		var first = objects[0];
		var root = new GameObject( first.Parent, true, "Instance" );
		root.WorldTransform = objects.Length == 1
			? first.WorldTransform
			: new Transform( BBox.FromBoxes( objects.Select( x => x.GetBounds() ) ).Center );
		first.AddSibling( root, false );
		foreach ( var gameObject in objects )
			gameObject.SetParent( root, true );
		Scene.CreateInstance( root );
		root.MakeNameUnique();
		Selection.Clear();
		Selection.Add( root );
		return root;
	}

	/// <summary>Gives one copy its own shared definition.</summary>
	public void MakeInstanceUnique( GameObject root )
	{
		if ( !IsInstance( root ) ) return;
		using var sceneScope = Scene.Push();
		using var undo = UndoScope( "Make Instance Unique" )
			.WithGameObjectChanges( root, GameObjectUndoFlags.All ).Push();
		Scene.CreateInstance( root );
	}

	/// <summary>Removes the link while keeping the group and its contents.</summary>
	public void UnlinkInstance( GameObject root )
	{
		if ( !IsInstance( root ) ) return;
		using var undo = UndoScope( "Unlink Instance" )
			.WithGameObjectChanges( root, GameObjectUndoFlags.All ).Push();
		Scene.UnlinkInstance( root );
	}

	/// <summary>Finds the instance definitions directly touched by an edit.</summary>
	internal HashSet<GameObject> GetEditedInstances( IEnumerable<GameObject> objects )
	{
		if ( Scene.InstanceState.Instances.IsEmpty ) return [];
		var roots = new HashSet<GameObject>();
		foreach ( var gameObject in objects )
		{
			for ( var current = gameObject; current.IsValid() && current != Scene; current = current.Parent )
			{
				if ( IsInstance( current ) ) roots.Add( current );
			}
		}
		return roots;
	}

	/// <summary>Finds linked copies that must participate in the same undo operation.</summary>
	internal IEnumerable<GameObject> GetAffectedInstances( IEnumerable<GameObject> roots )
	{
		var sources = Scene.DependentInstanceSources(
			roots.Select( x => Scene.InstanceState.Instances[x.Id].Source ) );
		return Scene.InstanceState.Instances
			.Where( x => sources.Contains( x.Value.Source ) )
			.Select( x => Scene.Directory.FindByGuid( x.Key ) )
			.Where( x => x.IsValid() );
	}

	/// <summary>Unlinks a component-free instance and dissolves its grouping object.</summary>
	public void UngroupInstance( GameObject root )
	{
		if ( !IsInstance( root ) || root.Components.Count != 0 ) return;
		var children = root.Children.ToArray();
		using var sceneScope = Scene.Push();
		using var undo = UndoScope( "Ungroup Instance" )
			.WithGameObjectChanges( children, GameObjectUndoFlags.Properties )
			.WithGameObjectDestructions( root ).Push();
		Scene.UnlinkInstance( root );
		foreach ( var child in children )
			root.AddSibling( child, true );
		root.Destroy();
		Selection.Clear();
		foreach ( var child in children )
			Selection.Add( child );
	}
}
