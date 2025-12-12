using System.Runtime.CompilerServices;
using Sandbox.ActionGraphs;
using System.Threading;

namespace Sandbox;

/// <summary>
/// An object in the scene. Functionality is added using Components. A GameObject has a transform, which explains its position,
/// rotation and scale, relative to its parent. It also has a name, and can be enabled or disabled. When disabled, the GameObject
/// is still in the scene, but the components don't tick and are all disabled.
/// </summary>
[Expose, ActionGraphIgnore, ActionGraphExposeWhenCached]
public partial class GameObject : IJsonConvert, IComponentLister, BytePack.ISerializer
{
	/// <summary>
	/// The scene that this GameObject is in.
	/// </summary>
	[ActionGraphInclude]
	public Scene Scene { get; private set; }

	GameTransform _gameTransform;

	/// <summary>
	/// Our position relative to our parent, or the scene if we don't have any parent.
	/// </summary>
	public GameTransform Transform => _gameTransform;

	/// <summary>
	/// The GameObject's name is usually used for debugging, and for finding it in the scene.
	/// </summary>
	[Property, ActionGraphInclude]
	public string Name
	{
		get => _name;
		set
		{
			_name = value ?? "Untitled Object";

			UpdateHumanReadableId();
		}
	}
	private string _name = "Untitled Object";

	/// <summary>
	/// Returns true of this is a root object. Root objects are parented to the scene.
	/// </summary>
	public bool IsRoot => Parent is Scene;

	/// <summary>
	/// Return the root GameObject. The root is the object that is parented to the scene - which could very much be this object.
	/// </summary>
	public GameObject Root
	{
		get
		{
			if ( IsRoot ) return this;
			return Parent?.Root;
		}
	}

	bool _enabled = true;

	/// <summary>
	/// This token source is expired when leaving the game session, or when the GameObject is disabled/destroyed.
	/// </summary>
	CancellationTokenSource enabledTokenSource;

	/// <summary>
	/// This token is cancelled when the GameObject ceases to exist, or is disabled
	/// </summary>
	public CancellationToken EnabledToken => enabledTokenSource?.Token ?? CancellationToken.None;

	/// <summary>
	/// Access components on this GameObject
	/// </summary>
	public ComponentList Components { get; private set; }

	/// <summary>
	/// Is this gameobject enabled?
	/// </summary>
	[Property, ActionGraphInclude]
	public bool Enabled
	{
		get => _enabled;
		set
		{
			if ( _enabled == value )
				return;

			_enabled = value;

			UpdateEnabledStatus();
		}
	}

	internal TaskSource Task { get; set; }

	internal SceneRefGizmo SceneRefGizmo { get; set; }

	/// <summary>
	/// Create a new GameObject with the given name. Will be created enabled.
	/// </summary>
	public GameObject( string name ) : this( null, true, name )
	{
	}

	/// <summary>
	/// Create a new GameObject with the given enabled state and name.
	/// </summary>
	public GameObject( bool enabled, string name ) : this( null, enabled, name )
	{
	}

	/// <summary>
	/// Create a new GameObject with the given parent, enabled state and name.
	/// </summary>
	public GameObject( GameObject parent, bool enabled = true, string name = null )
	{
		Scene = this as Scene ?? parent?.Scene ?? Game.ActiveScene;
		parent ??= Scene;

		ThreadSafe.AssertIsMainThread();

		_gameTransform = new GameTransform( this );
		Components = new ComponentList( this );
		Tags = new GameTags( this );
		_enabled = enabled;

		Id = Guid.NewGuid();
		Name = name ?? "GameObject";
		Parent = parent;

		if ( Application.IsEditor )
		{
			// What a fucking joke
			SceneRefGizmo = new SceneRefGizmo( this );
		}

		// seems like this is called automaically in OnEnabled?
		if ( enabled )
		{
			CreateTaskSource();
		}

		SceneMetrics.GameObjectsCreated++;
	}

	public GameObject( bool enabled ) : this( enabled, "GameObject" )
	{
	}

	public GameObject() : this( true, "GameObject" )
	{
	}

	public override string ToString()
	{
		return $"GameObject:{Name}";
	}

	/// <summary>
	/// Creates a new task source. Any Waits etc created by Task will be cancelled
	/// when the GameObject is disabled, or destroyed, or the game is exited.
	/// </summary>
	private void CreateTaskSource()
	{
		// cancel any previous tasks
		CancelTaskSource();

		enabledTokenSource = TaskSource.CreateLinkedTokenSource();
		Task = TaskSource.Create( enabledTokenSource.Token );
	}

	/// <summary>
	/// Cancel this task source
	/// </summary>
	private void CancelTaskSource()
	{
		enabledTokenSource?.Cancel();
		enabledTokenSource?.Dispose();
		enabledTokenSource = null;
		Task.Expire();
	}

	GameObject _parent;

	[ActionGraphInclude( AutoExpand = true )]
	public GameObject Parent
	{
		get => _parent;
		set
		{
			//
			// Scenes can't be parented, just ignore
			//
			if ( this is Scene ) return;

			//
			// If we set parent to null, switch it 
			//
			value ??= Scene;

			if ( _parent == value )
				return;

			if ( !CanChangeParent( value ) )
				return;

			SetParentInternal( value );

			if ( _net is null )
				return;

			Msg_SetParent( value.Id, false );
		}
	}

	/// <summary>
	/// Handles all internal common logic for setting the parent of this <see cref="GameObject"/>.
	/// </summary>
	void SetParentInternal( GameObject parent )
	{
		Assert.NotNull( parent );
		Assert.AreNotEqual( _parent, parent );

		if ( parent.IsAncestor( this ) )
		{
			Log.Warning( $"Illegal parentage" );
			return;
		}

		// Special handling when reparenting part of prefab instances
		// may need to convert nested prefab instances to full prefab instances
		var movedPartOfPrefabInstance = IsPrefabInstance && !IsOutermostPrefabInstanceRoot && parent is not null && (!parent.IsPrefabInstance || parent.OutermostPrefabInstanceRoot != OutermostPrefabInstanceRoot);
		if ( movedPartOfPrefabInstance )
		{
			if ( IsNestedPrefabInstanceRoot )
			{
				PrefabInstance.ConvertNestedToFullPrefabInstance();
			}
			else
			{
				PrefabInstanceData.ConvertTopLevelNestedToFullPrefabInstances( this );
			}
		}

		var oldParent = _parent;
		oldParent?.RemoveChild( this );

		_parent = parent;

		Assert.True( Scene == _parent.Scene, "Can't parent to a GameObject in a different Scene" );
		_parent.Children.Add( this );

		// Special handling when reparenting part of prefab instances part two
		// need to clear ourselves from the old prefab instances mappings
		if ( movedPartOfPrefabInstance )
		{
			oldParent.OutermostPrefabInstanceRoot.PrefabInstance.RemoveHierarchyFromLookup( this );
		}

		OnParentChanged( oldParent, _parent );
	}

	/// <summary>
	/// Do we have authority over this <see cref="GameObject"/>? If it's networked, we have
	/// authority if we're the network root, and we're not a proxy.
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	internal bool HasAuthority()
	{
		if ( !IsValid )
			return false;

		return !IsNetworkRoot || !IsProxy;
	}

	/// <summary>
	/// Can we change our parent to the specified <see cref="GameObject"/>?
	/// </summary>
	/// <param name="newParent">The parent to become a child of.</param>
	/// <returns></returns>
	private bool CanChangeParent( GameObject newParent )
	{
		if ( _net is null )
			return true;

		if ( !Networking.IsHost && !_net.HasControl( Connection.Local ) )
			return false;

		if ( newParent._net is null )
			return true;

		return Networking.IsHost || newParent._net.HasControl( Connection.Local );
	}

	private void OnParentChanged( GameObject oldParent, GameObject parent )
	{
		// Clear any local interpolation when our parent changes. This will also call TransformChanged.
		Transform.ClearLocalInterpolation();

		//
		// Tags could have changed
		//
		foreach ( var c in Components.GetAll( FindMode.EnabledInSelfAndDescendants ) )
		{
			c.OnTagsUpdatedInternal();
		}

		//
		// Network owner could have changed
		//
		UpdateNetworkRoot();

		//
		// Let components react to this
		//
		Components.ForEach( "OnParentChanged", false, c => c.OnParentChangedInternal( oldParent, parent ) );

		// We should tell our children and they should tell their children, propogate it down
		// as like a OnHeirachyChanged or something
	}

	[ActionGraphInclude( AutoExpand = true )]
	public List<GameObject> Children { get; } = new List<GameObject>();

	/// <summary>
	/// Is this gameobject active. For it to be active, it needs to be enabled, all of its ancestors
	/// need to be enabled, and it needs to be in a scene.
	/// </summary>
	[ActionGraphInclude]
	public bool Active => Enabled && Scene is not null && !IsNetworkCulled && (Parent?.Active ?? true);

	internal void ForEachChild( string name, bool includeDisabled, Action<GameObject> action )
	{
		for ( int i = Children.Count - 1; i >= 0; i-- )
		{
			if ( i >= Children.Count )
				continue;

			var c = Children[i];

			if ( c is null )
			{
				Children.RemoveAt( i );
				continue;
			}

			if ( !includeDisabled && !c.Active )
				continue;

			try
			{
				action( c );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when calling {name} on {c}: {e.Message}" );
			}
		}
	}

	/// <summary>
	/// Like the above, but faster, no allocations
	/// </summary>
	internal unsafe void ForEachChildFast<T>( string name, bool includeDisabled, delegate*< GameObject, ref T, void > action, ref T data ) where T : struct
	{
		for ( int i = Children.Count - 1; i >= 0; i-- )
		{
			if ( i >= Children.Count )
				continue;

			var c = Children[i];

			if ( c is null )
			{
				Children.RemoveAt( i );
				continue;
			}

			if ( !includeDisabled && !c.Active )
				continue;

			try
			{
				action( c, ref data );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when calling {name} on {c}: {e.Message}" );
			}
		}
	}


	/// <summary>
	/// Should be called whenever we change anything that we suspect might
	/// cause the active status to change on us, or our components. Don't call
	/// this directly. Only call it via SceneUtility.ActivateGameObject( this );
	/// </summary>
	internal void UpdateEnabledStatus()
	{
		using var batch = CallbackBatch.Batch();

		if ( _enabled )
		{
			CreateTaskSource();
		}
		else
		{
			CancelTaskSource();
		}

		Components.ForEach( "UpdateEnabledStatus", true, c => c.UpdateEnabledStatus() );
		ForEachChild( "UpdateEnabledStatus", true, c => c.UpdateEnabledStatus() );
	}

	internal void UpdateNetworkCulledState()
	{
		using var batch = CallbackBatch.Batch();

		Components.ForEach( "UpdateEnabledStatus", true, c => c.UpdateEnabledStatus() );
		ForEachChild( "UpdateEnabledStatus", true, c => c.UpdateNetworkCulledState() );
	}

	/// <summary>
	/// Returns true if the passed in object is a decendant of ours
	/// </summary>
	[ActionGraphInclude, Pure]
	public bool IsDescendant( GameObject decendant )
	{
		return decendant.IsAncestor( this );
	}

	/// <summary>
	/// Returns true if the passed in object is an ancestor
	/// </summary>
	[ActionGraphInclude, Pure]
	public bool IsAncestor( GameObject ancestor )
	{
		if ( ancestor == this ) return true;

		if ( Parent is not null )
		{
			return Parent.IsAncestor( ancestor );
		}

		return false;
	}

	[ActionGraphInclude]
	public void AddSibling( GameObject go, bool before, bool keepWorldPosition = true )
	{
		if ( this is Scene ) throw new InvalidOperationException( "Can't add a sibling to a scene!" );

		go.SetParent( Parent, keepWorldPosition );

		go.Parent.Children.Remove( go );
		var targetIndex = go.Parent.Children.IndexOf( this );
		if ( !before ) targetIndex++;
		go.Parent.Children.Insert( targetIndex, go );
	}

	[ActionGraphInclude]
	public void SetParent( GameObject value, bool keepWorldPosition = true )
	{
		if ( this is Scene ) throw new InvalidOperationException( "Can't set the parent of a scene!" );
		if ( value is null ) value = Scene;

		if ( Parent == value ) return;

		if ( !CanChangeParent( value ) )
			return;

		if ( keepWorldPosition )
		{
			var oldTransform = WorldTransform;
			SetParentInternal( value );
			WorldTransform = oldTransform;
		}
		else
		{
			SetParentInternal( value );
		}

		if ( _net is null )
			return;

		Msg_SetParent( value.Id, keepWorldPosition );
	}

	/// <summary>
	/// Set the parent of this GameObject from a remote change over the network.
	/// </summary>
	internal void SetParentFromNetwork( GameObject value, bool keepWorldPosition = false )
	{
		if ( this is Scene ) return;

		value ??= Scene;

		if ( _parent == value )
			return;

		if ( keepWorldPosition )
		{
			var oldTransform = WorldTransform;
			SetParentInternal( value ?? Scene );
			_gameTransform.SetWorldInternal( oldTransform );
		}
		else
		{
			SetParentInternal( value ?? Scene );
		}
	}

	IEnumerable<GameObject> GetSiblings()
	{
		if ( Parent is not null )
		{
			return Parent.Children.Where( x => x != this );
		}

		return Enumerable.Empty<GameObject>();
	}

	/// <summary>
	/// The human readable ID is the number in parentheses at the end of the name. (If it exists)
	/// There is no guarantee that this is unique, or sequential.
	/// </summary>
	private int _humanReadableId;

	// Method to update the cached ID whenever Name changes
	private void UpdateHumanReadableId()
	{
		string name = _name;
		int length = name.Length;

		// Quick check if name ends with ')'
		if ( length < 3 || name[length - 1] != ')' )
		{
			_humanReadableId = 0;
			return;
		}

		// Find opening parenthesis by scanning backward
		int openParenIndex = -1;
		for ( int i = length - 2; i >= 0; i-- )
		{
			if ( name[i] == '(' )
			{
				openParenIndex = i;
				break;
			}
		}

		if ( openParenIndex < 0 || openParenIndex == length - 2 )
		{
			_humanReadableId = 0;
			return;
		}

		// Parse number manually without substring allocation
		int result = 0;
		for ( int i = openParenIndex + 1; i < length - 1; i++ )
		{
			char c = name[i];
			if ( c < '0' || c > '9' )
			{
				_humanReadableId = 0;
				return; // Not a valid number
			}

			result = result * 10 + (c - '0');
		}

		_humanReadableId = result;
	}

	public void MakeNameUnique()
	{
		if ( Parent is null ) return;
		// If we are not in editor let's not do this if we have a lot of siblings as it becomes fairly expensive
		if ( Parent.Children.Count > 100 && !Scene.IsEditor ) return;

		// Extract base name first
		string baseName = Name;
		int parenIndex = baseName.LastIndexOf( '(' );
		if ( parenIndex > 0 && baseName.EndsWith( ')' ) )
		{
			baseName = baseName.Substring( 0, parenIndex ).TrimEnd();
		}

		// Single pass through siblings to check for duplicates and find highest number
		int highestNumber = 0;
		bool foundDuplicate = false;

		foreach ( var sibling in Parent.Children )
		{
			if ( sibling == this ) continue;

			// Check if sibling name starts with our base name
			if ( sibling.Name.StartsWith( baseName, StringComparison.Ordinal ) )
			{
				if ( sibling.Name.StartsWith( $"{baseName} (" ) && sibling.Name[^1] == ')' )
				{
					// Sibling has the same base name and a number in parentheses
					int siblingNumber = sibling._humanReadableId;
					if ( siblingNumber == _humanReadableId )
					{
						foundDuplicate = true;
					}
					if ( siblingNumber > highestNumber )
					{
						highestNumber = siblingNumber;
					}
				}
				else if ( sibling.Name == baseName )
				{
					// Exact match without parentheses
					foundDuplicate = true;
				}
			}
		}

		if ( !foundDuplicate )
		{
			// No duplicates found, keep original name
			return;
		}

		// Create new name with next available number
		Name = $"{baseName} ({highestNumber + 1})";
	}

	[ActionGraphInclude, Pure]
	public IEnumerable<GameObject> GetAllObjects( bool enabled )
	{
		if ( enabled && !Enabled )
			yield break;

		yield return this;

		foreach ( var child in Children.OfType<GameObject>().SelectMany( x => x.GetAllObjects( enabled ) ).ToArray() )
		{
			yield return child;
		}
	}

	[Obsolete( "EditLog is obsolete use Scene.Editor.UndoScope or Scene.Editor.AddUndo instead." )]
	public virtual void EditLog( string name, object source )
	{
	}

	/// <summary>
	/// This is slow, and somewhat innacurate. Don't call it every frame!
	/// </summary>
	[ActionGraphInclude, Pure]
	public BBox GetBounds()
	{
		var result = BBox.FromPositionAndSize( WorldPosition );

		Components.ExecuteEnabledInSelfAndDescendants<Component.IHasBounds>( x =>
		{
			if ( x is Component c )
			{
				result = result.AddBBox( x.LocalBounds.Transform( c.WorldTransform ) );
			}
			else
			{
				result = result.AddBBox( x.LocalBounds );
			}
		} );

		return result;
	}

	/// <summary>
	/// This is slow, and somewhat innacurate. Don't call it every frame!
	/// </summary>
	[ActionGraphInclude, Pure]
	public BBox GetLocalBounds()
	{
		var result = BBox.FromPositionAndSize( Vector3.Zero );

		Components.ExecuteEnabledInSelfAndDescendants<Component.IHasBounds>( x =>
		{
			// This is wrong for CHILDREN!
			result = result.AddBBox( x.LocalBounds );
		} );

		return result;
	}

	/// <summary>
	/// Get the GameObject after us,
	/// </summary>
	[ActionGraphInclude, Pure]
	public GameObject GetNextSibling( bool enabledOnly )
	{
		if ( Parent is null ) return null;
		var myIndex = Parent.Children.IndexOf( this );
		if ( myIndex < 0 ) return null;

		for ( int i = myIndex + 1; i < Parent.Children.Count; i++ )
		{
			if ( Parent.Children[i] is null ) continue;
			if ( enabledOnly && !Parent.Children[i].Enabled ) continue;
			return Parent.Children[i];
		}

		return null;
	}

	internal void OnComponentAdded( Component component )
	{
		if ( component is Component.INetworkVisible netVisible )
		{
			NetworkVisibility = netVisible;
		}

		Scene?.Directory?.Add( component );
		ClearInternalCache();
	}

	internal void OnComponentRemoved( Component component )
	{
		if ( component == NetworkVisibility )
		{
			NetworkVisibility = null;
		}

		Scene?.Directory?.Remove( component );
		ClearInternalCache();
	}

	/// <summary>
	/// Internal stuff only
	/// </summary>
	internal virtual void OnHotload()
	{
		ClearInternalCache();

		Components.OnHotload();
	}


	internal void ClearInternalCache()
	{
		handleBuilt = false;
	}

	private DebugOverlaySystem _debugOverlaySytem;

	/// <summary>
	/// Allows drawing of temporary debug shapes and text in the scene
	/// </summary>
	public DebugOverlaySystem DebugOverlay => _debugOverlaySytem ??= DebugOverlaySystem.Get( Scene );
}
