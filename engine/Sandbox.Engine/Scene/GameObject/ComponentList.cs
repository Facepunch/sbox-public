using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// Flags to search for Components.
/// I've named this something generic because I think we can re-use it to search for GameObjects too.
/// </summary>
[Flags, Expose]
public enum FindMode
{
	/// <summary>
	/// Components that are enabled
	/// </summary>
	Enabled = 1,

	/// <summary>
	/// Components that are disabled
	/// </summary>
	Disabled = 2,

	/// <summary>
	/// Components in this object
	/// </summary>
	InSelf = 4,

	/// <summary>
	/// Components in our parent
	/// </summary>
	InParent = 8,

	/// <summary>
	/// Components in all ancestors (parent, their parent, their parent, etc)
	/// </summary>
	InAncestors = 16,

	/// <summary>
	/// Components in our children
	/// </summary>
	InChildren = 32,

	/// <summary>
	/// Components in all decendants (our children, their children, their children etc)
	/// </summary>
	InDescendants = 64,


	EnabledInSelf = Enabled | InSelf,
	EnabledInSelfAndDescendants = Enabled | InSelf | InDescendants,
	EnabledInSelfAndChildren = Enabled | InSelf | InChildren,

	DisabledInSelf = Disabled | InSelf,
	DisabledInSelfAndDescendants = Disabled | InSelf | InDescendants,
	DisabledInSelfAndChildren = Disabled | InSelf | InChildren,

	EverythingInSelf = Enabled | InSelf | Disabled,
	EverythingInSelfAndDescendants = Enabled | InSelf | Disabled | InDescendants,
	EverythingInSelfAndChildren = Enabled | InSelf | Disabled | InChildren,
	EverythingInSelfAndParent = Enabled | InSelf | Disabled | InParent,
	EverythingInSelfAndAncestors = Enabled | InSelf | Disabled | InAncestors,
	EverythingInAncestors = Enabled | Disabled | InAncestors,
	EverythingInChildren = Enabled | Disabled | InChildren,
	EverythingInDescendants = Enabled | Disabled | InDescendants,
}

public class ComponentList
{
	readonly GameObject go;

	/// <summary>
	/// This is the hard list of components.
	/// This isn't a HashSet because we need the order to stay.
	/// Lazily initialized so GameObjects without components don't allocate.
	/// </summary>
	List<Component> _internalList;
	List<Component> _list => _internalList ??= new List<Component>();

	internal ComponentList( GameObject o )
	{
		go = o;
	}

	/// <summary>
	/// Get all components, including disabled ones
	/// </summary>
	public IEnumerable<Component> GetAll()
	{
		return _internalList is null ? Enumerable.Empty<Component>() : _list;
	}

	/// <summary>
	/// Hotload has occurred
	/// </summary>
	internal void OnHotload()
	{
		if ( _internalList is null ) return;
		_list.RemoveAll( x => x is null );
	}

	/// <summary>
	/// Add a component of this type
	/// </summary>
	public Component Create( TypeDescription type, bool startEnabled = true )
	{
		if ( !type.TargetType.IsAssignableTo( typeof( Component ) ) )
			return null;

		using var batch = CallbackBatch.Batch();

		var t = type.Create<Component>();
		t.GameObject = go;
		_list.Add( t );

		t.Enabled = startEnabled;
		go.OnComponentAdded( t );

		return t;
	}

	/// <summary>
	/// Add a component of this type
	/// </summary>
	public T Create<T>( bool startEnabled = true ) where T : Component, new()
	{
		using var batch = CallbackBatch.Batch();
		var t = new T();

		t.GameObject = go;
		_list.Add( t );

		t.InitializeComponent();
		t.Enabled = startEnabled;
		go.OnComponentAdded( t );

		return t;
	}

	/// <summary>
	/// Add a component of this type
	/// </summary>
	internal Component Create( Type type, bool startEnabled = true )
	{
		if ( !type.IsAssignableTo( typeof( Component ) ) )
			return null;

		using var batch = CallbackBatch.Batch();
		var t = (Component)Activator.CreateInstance( type );

		t.GameObject = go;
		_list.Add( t );

		t.InitializeComponent();
		t.Enabled = startEnabled;
		go.OnComponentAdded( t );

		return t;
	}

	/// <summary>
	/// Get a component of this type
	/// </summary>
	public T Get<T>( FindMode search )
	{
		return GetAll<T>( search ).FirstOrDefault();
	}

	/// <summary>
	/// Get a component of this type
	/// </summary>
	public Component Get( Type type, FindMode find = FindMode.EnabledInSelf )
	{
		return GetAll( type, find ).FirstOrDefault();
	}

	/// <summary>
	/// Get all components of this type
	/// </summary>
	public IEnumerable<Component> GetAll( Type type, FindMode find )
	{
		return GetAll<Component>( find ).Where( x => x.GetType().IsAssignableTo( type ) );
	}

	/// <summary>
	/// Get all components
	/// </summary>
	public IEnumerable<Component> GetAll( FindMode find ) => GetAll<Component>( find );

	/// <summary>
	/// Get a list of components on this game object, optionally recurse when deep is true
	/// </summary>
	public IEnumerable<T> GetAll<T>( FindMode find = FindMode.InSelf | FindMode.Enabled | FindMode.InDescendants )
	{
		if ( go.IsDestroyed ) return Enumerable.Empty<T>();

		var results = new List<T>( 16 );

		CollectAll( results, find );

		return results;
	}

	// This is an incredibly hot code path, even the slightest change should be verified with benchmarks.
	private void CollectAll<T>( List<T> results, FindMode find )
	{
		bool enabledOnly = find.Contains( FindMode.Enabled );
		bool disabledOnly = find.Contains( FindMode.Disabled );

		if ( enabledOnly == disabledOnly )
		{
			enabledOnly = false;
			disabledOnly = false;
		}

		if ( enabledOnly && !go.Enabled ) return;

		//
		// Find in self
		//
		if ( find.Contains( FindMode.InSelf ) && _internalList is not null )
		{
			for ( int i = 0; i < _list.Count; i++ )
			{
				var component = _list[i];
				if ( component is null ) continue;

				if ( enabledOnly && !component.Active ) continue;
				if ( disabledOnly && component.Active ) continue;

				if ( component is T c )
				{
					results.Add( c );
				}
			}
		}

		//
		// Find in children
		//
		if ( find.Contains( FindMode.InChildren ) || find.Contains( FindMode.InDescendants ) )
		{
			var childFlags = find | FindMode.InSelf;
			childFlags &= ~FindMode.InParent;
			childFlags &= ~FindMode.InAncestors;

			// If we're not searching all descendants then remove the InChildren flag
			if ( !find.Contains( FindMode.InDescendants ) )
			{
				childFlags &= ~FindMode.InChildren;
			}

			for ( int i = 0; i < go.Children.Count; i++ )
			{
				var child = go.Children[i];
				if ( child.IsValid() )
				{
					child.Components.CollectAll( results, childFlags );
				}
			}
		}

		//
		// Find in parent
		//
		if ( find.Contains( FindMode.InParent ) || find.Contains( FindMode.InAncestors ) )
		{
			var parentFlags = find | FindMode.InSelf;
			parentFlags &= ~FindMode.InChildren;
			parentFlags &= ~FindMode.InDescendants;

			// If we're not searching all ancestors then remove the InParent flag
			if ( !find.Contains( FindMode.InAncestors ) )
			{
				parentFlags &= ~FindMode.InParent;
			}

			if ( go.Parent is not null && go.Parent is PrefabScene or not Scene )
			{
				go.Parent.Components.CollectAll( results, parentFlags );
			}
		}
	}

	internal void Execute<T>( Action<T> action, FindMode find = FindMode.EnabledInSelfAndDescendants )
	{
		switch ( find )
		{
			// Most common case has a fast path
			case FindMode.EnabledInSelfAndDescendants:
				ExecuteEnabledInSelfAndDescendants( action );
				break;
			default:
				ExecuteGeneric( action, find );
				break;
		}
	}

	// Calling this directly is faster than going through Execute
	internal void ExecuteEnabledInSelfAndDescendants<T>( Action<T> action )
	{
		if ( !go.IsValid() || !go.Enabled ) return;

		if ( _internalList is not null )
		{
			// Check components on this GameObject
			for ( int i = 0; i < _list.Count; i++ )
			{
				var component = _list[i];
				if ( component is null ) continue;

				if ( component is T target && component.Active )
				{
					action.Invoke( target );
				}
			}
		}

		// Recurse to children
		for ( int i = go.Children.Count - 1; i >= 0; i-- )
		{
			if ( i >= go.Children.Count )
				continue;

			var child = go.Children[i];
			if ( !child.IsValid() ) continue;

			child.Components.ExecuteEnabledInSelfAndDescendants( action );
		}
	}

	private void ExecuteGeneric<T>( Action<T> action, FindMode find = FindMode.EnabledInSelfAndDescendants )
	{
		bool enabledOnly = find.Contains( FindMode.Enabled );
		bool disabledOnly = find.Contains( FindMode.Disabled );

		if ( enabledOnly == disabledOnly )
		{
			enabledOnly = false;
			disabledOnly = false;
		}

		if ( enabledOnly && !go.Enabled ) return;

		//
		// Execute in self
		//
		if ( find.Contains( FindMode.InSelf ) && _internalList is not null )
		{
			for ( int i = 0; i < _list.Count; i++ )
			{
				var component = _list[i];
				if ( component is null ) continue;

				if ( enabledOnly && !component.Active ) continue;
				if ( disabledOnly && component.Active ) continue;

				if ( component is T target )
				{
					action.Invoke( target );
				}
			}
		}

		//
		// Execute in children
		//
		if ( find.Contains( FindMode.InChildren ) || find.Contains( FindMode.InDescendants ) )
		{
			var childFlags = find | FindMode.InSelf;
			childFlags &= ~FindMode.InParent;
			childFlags &= ~FindMode.InAncestors;

			// If we're not searching all descendants then remove the InChildren flag
			if ( !find.Contains( FindMode.InDescendants ) )
			{
				childFlags &= ~FindMode.InChildren;
			}

			for ( int i = 0; i < go.Children.Count; i++ )
			{
				var child = go.Children[i];
				if ( child.IsValid() )
				{
					child.Components.Execute( action, childFlags );
				}
			}
		}

		//
		// Execute in parent
		//
		if ( find.Contains( FindMode.InParent ) || find.Contains( FindMode.InAncestors ) )
		{
			var parentFlags = find | FindMode.InSelf;
			parentFlags &= ~FindMode.InChildren;
			parentFlags &= ~FindMode.InDescendants;

			// If we're not searching all ancestors then remove the InParent flag
			if ( !find.Contains( FindMode.InAncestors ) )
			{
				parentFlags &= ~FindMode.InParent;
			}

			if ( go.Parent is not null && go.Parent is PrefabScene or not Scene )
			{
				go.Parent.Components.Execute( action, parentFlags );
			}
		}
	}

	/// <summary>
	/// Try to get this component
	/// </summary>
	public bool TryGet<T>( out T component, FindMode search = FindMode.EnabledInSelf )
	{
		component = Get<T>( search );

		return component is not null;
	}

	/// <summary>
	/// Allows linq style queries
	/// </summary>
	public Component FirstOrDefault( Func<Component, bool> value ) => _internalList is null ? null : _internalList.FirstOrDefault( value );

	/// <summary>
	/// Amount of components - including disabled
	/// </summary>
	public int Count => _internalList is null ? 0 : _internalList.Count;

	public void ForEach<T>( string name, bool includeDisabled, Action<T> action )
	{
		if ( _internalList is null ) return;

		if ( !includeDisabled && !go.Active ) return;

		for ( int i = _list.Count - 1; i >= 0 && i < _list.Count; i-- )
		{
			Component c = _list[i];

			if ( c is null )
			{
				_list.RemoveAt( i );
				continue;
			}

			if ( !includeDisabled && !c.Active )
				continue;

			if ( c is not T t )
				continue;

			try
			{
				action( t );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when calling {name} on {c}: {e.Message}" );
			}
		}
	}

	public void ForEach( string name, bool includeDisabled, Action<Component> action ) => ForEach<Component>( name, includeDisabled, action );

	internal void RemoveNull()
	{
		if ( _internalList is null ) return;
		_list.RemoveAll( x => x is null );
	}

	internal void OnDestroyedInternal( Component baseComponent )
	{
		if ( _list.Remove( baseComponent ) )
		{
			go.OnComponentRemoved( baseComponent );
		}
	}

	internal int IndexOf( Component baseComponent )
	{
		return _internalList is null ? -1 : _list.IndexOf( baseComponent );
	}

	/// <summary>
	/// Move the position of the component in the list by delta (-1 means up one, 1 means down one)
	/// </summary>
	public void Move( Component baseComponent, int delta )
	{
		var i = _list.IndexOf( baseComponent );
		if ( i < 0 ) return;

		i += delta;

		if ( i < 0 ) i = 0;
		if ( i >= _list.Count ) i = _list.Count - 1;

		// Move the element
		_list.RemoveAt( _list.IndexOf( baseComponent ) );
		_list.Insert( i, baseComponent );
	}

	/// <summary>
	/// Move the component to a specific index in the list.
	/// If a component is already at that index, it will be swapped with the component being moved.
	/// </summary>
	internal void MoveToIndex( Component comp, int targetIndex )
	{
		var compIndex = _list.IndexOf( comp );

		_list[compIndex] = _list[targetIndex];
		_list[targetIndex] = comp;
	}

	//
	// Easy Modes
	//

	/// <summary>
	/// Find component on this gameobject
	/// </summary>
	public T Get<T>( bool includeDisabled = false )
	{
		var f = FindMode.InSelf;
		if ( !includeDisabled ) f |= FindMode.Enabled;

		return Get<T>( f );
	}

	/// <summary>
	/// Find this component, if it doesn't exist - create it.
	/// </summary>
	public T GetOrCreate<T>( FindMode flags = FindMode.EverythingInSelf ) where T : Component, new()
	{
		if ( TryGet<T>( out var component, flags ) )
			return component;

		return Create<T>();
	}

	/// <summary>
	/// Find component on this gameobject's ancestors or on self
	/// </summary>
	public T GetInAncestorsOrSelf<T>( bool includeDisabled = false )
	{
		var f = FindMode.InSelf | FindMode.InAncestors;
		if ( !includeDisabled ) f |= FindMode.Enabled;

		return Get<T>( f );
	}

	/// <summary>
	/// Find component on this gameobject's ancestors
	/// </summary>
	public T GetInAncestors<T>( bool includeDisabled = false )
	{
		var f = FindMode.InAncestors;
		if ( !includeDisabled ) f |= FindMode.Enabled;

		return Get<T>( f );
	}

	/// <summary>
	/// Find component on this gameobject's decendants or on self
	/// </summary>
	public T GetInDescendantsOrSelf<T>( bool includeDisabled = false )
	{
		var f = FindMode.InSelf | FindMode.InDescendants;
		if ( !includeDisabled ) f |= FindMode.Enabled;

		return Get<T>( f );
	}

	/// <summary>
	/// Find component on this gameobject's decendants
	/// </summary>
	public T GetInDescendants<T>( bool includeDisabled = false )
	{
		var f = FindMode.InDescendants;
		if ( !includeDisabled ) f |= FindMode.Enabled;

		return Get<T>( f );
	}

	/// <summary>
	/// Find component on this gameobject's immediate children or on self
	/// </summary>
	public T GetInChildrenOrSelf<T>( bool includeDisabled = false )
	{
		var f = FindMode.InSelf | FindMode.InChildren;
		if ( !includeDisabled ) f |= FindMode.Enabled;

		return Get<T>( f );
	}

	/// <summary>
	/// Find component on this gameobject's immediate children
	/// </summary>
	public T GetInChildren<T>( bool includeDisabled = false )
	{
		var f = FindMode.InChildren;
		if ( !includeDisabled ) f |= FindMode.Enabled;

		return Get<T>( f );
	}

	/// <summary>
	/// Find component on this gameobject's parent or on self
	/// </summary>
	public T GetInParentOrSelf<T>( bool includeDisabled = false )
	{
		var f = FindMode.InSelf | FindMode.InParent;
		if ( !includeDisabled ) f |= FindMode.Enabled;

		return Get<T>( f );
	}

	/// <summary>
	/// Find component on this gameobject's parent
	/// </summary>
	public T GetInParent<T>( bool includeDisabled = false )
	{
		var f = FindMode.InParent;
		if ( !includeDisabled ) f |= FindMode.Enabled;

		return Get<T>( f );
	}

	/// <summary>
	/// Find component on this gameobject with the specified id
	/// </summary>
	public Component Get( Guid id )
	{
		return GetAll().FirstOrDefault( x => x.Id.Equals( id ) );
	}

	/// <summary>
	/// Adds a special component that will keep information about a missing component.
	/// This component just holds the raw json of this component.
	/// </summary>
	internal void AddMissing( MissingComponent missing )
	{
		missing.GameObject = go;
		_list.Add( missing );
		go.OnComponentAdded( missing );
	}
}

/// <summary>
/// Interface for types that reference a <see cref="ComponentList"/>, to provide
/// convenience method for accessing that list.
/// </summary>
[Expose, Title( "Component List" ), Icon( "apps" )]
public interface IComponentLister
{
	[ActionGraphIgnore]
	ComponentList Components { get; }

	[Title( "Create {T|Component}" )]
	public T Create<T>( bool startEnabled = true ) where T : Component, new()
	{
		return Components.Create<T>( startEnabled );
	}

	[Pure, Title( "Get {T|Component}" )]
	public T Get<[HasImplementation( typeof( Component ) )] T>( FindMode search = FindMode.EnabledInSelf )
	{
		return Components.GetAll<T>( search ).FirstOrDefault();
	}

	[Pure, Title( "Try Get {T|Component}" )]
	public bool TryGet<[HasImplementation( typeof( Component ) )] T>( out T component,
		FindMode search = FindMode.EnabledInSelf )
	{
		return Components.TryGet( out component, search );
	}

	[Pure, Title( "Get All {T|Component}" )]
	public IEnumerable<T> GetAll<[HasImplementation( typeof( Component ) )] T>( FindMode search = FindMode.EnabledInSelf )
	{
		return Components.GetAll<T>( search );
	}

	[Title( "Get or Create {T|Component}" )]
	public T GetOrCreate<T>( FindMode flags = FindMode.EverythingInSelf ) where T : Component, new()
	{
		return Components.GetOrCreate<T>( flags );
	}
}
