

public class LinkedGameObjectBuilder
{
	public List<GameObject> Objects { get; } = new();
	private readonly HashSet<GameObject> _visited = new();

	/// <summary>
	/// Reject players and any objects with players as descendants. This is used for the duplicator to avoid accidentally duping the player and all their attachments.
	/// </summary>
	public bool RejectPlayers { get; set; } = false;

	/// <summary>
	/// Adds a GameObject. Won't find connections.
	/// </summary>
	public bool Add( GameObject obj )
	{
		if ( !obj.IsValid() ) return false;
		if ( obj.Tags.Contains( "world" ) ) return false;
		if ( _visited.Contains( obj ) ) return false;
		if ( obj.GetComponent<MapInstance>() is not null ) return false;
		if ( RejectPlayers && HasDescendantWithTag( obj, "player" ) ) return false;

		_visited.Add( obj );
		Objects.Add( obj );
		return true;
	}

	/// <summary>
	/// Component lists for <see cref="AddConnected"/>, one per recursion depth, so walking doesn't allocate.
	/// </summary>
	private readonly List<List<Component>> _componentLists = new();
	private int _depth;

	/// <summary>
	/// Add a GameObject with all connected GameObjects
	/// </summary>
	public void AddConnected( GameObject source )
	{
		if ( !source.IsValid() ) return;

		//
		// we're only interested in root objects
		//
		source = source.Root;

		// If we can't add this then don't add children
		// because we must have already added it, or it's the world.
		if ( !Add( source ) ) return;

		if ( _componentLists.Count <= _depth ) _componentLists.Add( new List<Component>() );
		var components = _componentLists[_depth];
		components.Clear();
		CollectComponents( source, components );

		_depth++;

		try
		{
			// One pass per kind, in the same order as before: bodies, colliders, links, filters
			foreach ( var component in components )
			{
				if ( component is Rigidbody rb ) AddJoints( rb.Joints );
			}

			foreach ( var component in components )
			{
				if ( component is Collider collider ) AddJoints( collider.Joints );
			}

			foreach ( var component in components )
			{
				if ( component is ManualLink link && link.Body.IsValid() )
					AddConnected( link.Body );
			}

			// If any children have a physics filter, also connect them.
			foreach ( var component in components )
			{
				if ( component is PhysicsFilter filter && filter.GameObject != source )
					AddConnected( filter.GameObject );
			}
		}
		finally
		{
			_depth--;
			components.Clear();
		}
	}

	private void AddJoints( IReadOnlySet<Joint> joints )
	{
		if ( joints.Count == 0 ) return;

		// It's a HashSet underneath - enumerate it as one so it doesn't box
		if ( joints is HashSet<Joint> set )
		{
			foreach ( var joint in set )
			{
				AddConnected( joint.Object1 );
				AddConnected( joint.Object2 );
			}

			return;
		}

		foreach ( var joint in joints )
		{
			AddConnected( joint.Object1 );
			AddConnected( joint.Object2 );
		}
	}

	/// <summary>
	/// Every active component on this GameObject and its enabled descendants, in hierarchy order - what
	/// <c>GetComponentsInChildren&lt;Component&gt;()</c> finds, without allocating.
	/// </summary>
	public static void CollectComponents( GameObject gameObject, List<Component> results )
	{
		if ( !gameObject.IsValid() || !gameObject.Enabled ) return;

		// It's a List underneath - empty GameObjects hand back something else, with nothing in it
		if ( gameObject.Components.GetAll() is List<Component> components )
		{
			foreach ( var component in components )
			{
				if ( component is not null && component.Active )
					results.Add( component );
			}
		}

		foreach ( var child in gameObject.Children )
			CollectComponents( child, results );
	}

	public void RemoveDeletedObjects()
	{
		Objects.RemoveAll( x => !x.IsValid() || x.IsDestroyed );
		_visited.RemoveWhere( x => !x.IsValid() || x.IsDestroyed );
	}

	static bool HasDescendantWithTag( GameObject obj, string tag )
	{
		foreach ( var child in obj.Children )
		{
			if ( child.Tags.Has( tag ) ) return true;
			if ( HasDescendantWithTag( child, tag ) ) return true;
		}
		return false;
	}

	public void Clear()
	{
		Objects.Clear();
		_visited.Clear();
	}
}
