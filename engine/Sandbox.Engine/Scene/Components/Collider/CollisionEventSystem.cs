using static Sandbox.Component;

namespace Sandbox;

/// <summary>
/// Used to abstract the listening of collision events
/// </summary>
class CollisionEventSystem : IDisposable
{
	private PhysicsBody _body;
	private GameObject _gameObject;
	private Rigidbody _rigidbody;
	private bool _disposed;

	public IReadOnlyCollection<Collider> Touching => _touchingColliders is null ? Array.Empty<Collider>() : _touchingColliders.Keys;

	/// <summary>
	/// Append contacts without boxing the dictionary's enumerator.
	/// </summary>
	internal void GetTouching( List<Collider> result )
	{
		if ( _touchingColliders is null )
			return;

		foreach ( var collider in _touchingColliders.Keys )
			result.Add( collider );
	}

	private record struct TouchingPair( Collider Self, Collider Other, GameObject GameObject );
	private HashSet<TouchingPair> _touchingPairs;

	// How many touching pairs we have with each other collider and GameObject
	private Dictionary<Collider, int> _touchingColliders;
	private Dictionary<GameObject, int> _touchingObjects;

	private HashSet<TouchingPair> TouchingPairs => _touchingPairs ??= new();
	private Dictionary<Collider, int> TouchingColliders => _touchingColliders ??= new();
	private Dictionary<GameObject, int> TouchingObjects => _touchingObjects ??= new();

	public CollisionEventSystem( PhysicsBody body, Rigidbody rigidbody = null )
	{
		_body = body;
		_rigidbody = rigidbody;
		_gameObject = rigidbody?.GameObjectSource ?? body.GameObject;
		_body.Listener = this;
	}

	/// <summary>
	/// Swap to a new physics body without reallocating delegates.
	/// </summary>
	public void Rebind( PhysicsBody newBody )
	{
		if ( _body.IsValid() )
			_body.Listener = null;

		_body = newBody;
		_gameObject = _rigidbody?.GameObjectSource ?? newBody.GameObject;
		_body.Listener = this;
	}

	internal void OnTriggerBegin( in CollisionSource self, in CollisionSource other )
	{
		var gameObject = other.Body.GameObject;

		OnObjectTriggerStart( self.Collider, gameObject );
		OnColliderTriggerStart( self.Collider, other.Collider, gameObject );
	}

	internal void OnTriggerEnd( in CollisionSource self, in CollisionSource other )
	{
		var gameObject = other.Body.GameObject;

		OnColliderTriggerStop( self.Collider, other.Collider, gameObject );
		OnObjectTriggerStop( self.Collider, gameObject );
	}

	internal void OnIntersectionStart( in Collision o )
	{
		_rigidbody?.HandleImpactDamage( o );

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ICollisionListener, Collision>( o, static ( x, c ) => x.OnCollisionStart( c ) );
	}

	internal void OnIntersectionEnd( in CollisionStop o )
	{
		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ICollisionListener, CollisionStop>( o, static ( x, c ) => x.OnCollisionStop( c ) );
	}

	internal void OnIntersectionUpdate( in Collision o )
	{
		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ICollisionListener, Collision>( o, static ( x, c ) => x.OnCollisionUpdate( c ) );
	}

	private void OnColliderTriggerStart( Collider self, Collider other, GameObject go )
	{
		if ( self is null )
			return;

		if ( other is null )
			return;

		// An object enter listener can disable us, still send the enter but don't track a pair we'll never exit
		if ( !_disposed )
		{
			if ( !TouchingPairs.Add( new TouchingPair( self, other, go ) ) )
				return;

			// Watch the other collider from our first pair with it, when it's disabled its shapes go away without sending trigger exits
			if ( TouchingColliders.TryAdd( other, 1 ) )
				other.AddTouchWatcher( this );
			else
				TouchingColliders[other]++;

			if ( go is not null )
				TouchingObjects[go]++;
		}

		if ( !self.IsValid() )
			return;

		if ( !other.IsValid() )
			return;

		if ( !self.IsTrigger )
			return;

		self.OnTriggerEnter?.InvokeWithWarning( other );

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ITriggerListener, (Collider self, Collider other)>( (self, other), static ( x, s ) => x.OnTriggerEnter( s.self, s.other ) );
	}

	private void OnColliderTriggerStop( Collider self, Collider other, GameObject go )
	{
		if ( self is null )
			return;

		if ( other is null )
			return;

		var pair = new TouchingPair( self, other, go );
		if ( _touchingPairs is null || !_touchingPairs.Contains( pair ) )
			return;

		// Another shape pair of these colliders can still be overlapping
		foreach ( var shape in self.Shapes )
		{
			foreach ( var otherShape in other.Shapes )
			{
				if ( shape.IsTouching( otherShape, true ) )
					return;
			}
		}

		_touchingPairs.Remove( pair );

		if ( --_touchingColliders[other] == 0 )
		{
			_touchingColliders.Remove( other );
			other.RemoveTouchWatcher( this );
		}

		if ( go is not null )
			_touchingObjects[go]--;

		if ( !self.IsValid() )
			return;

		if ( !other.IsValid() )
			return;

		if ( !self.IsTrigger )
			return;

		self.OnTriggerExit?.InvokeWithWarning( other );

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ITriggerListener, (Collider self, Collider other)>( (self, other), static ( x, s ) => x.OnTriggerExit( s.self, s.other ) );
	}

	private void OnObjectTriggerStart( Collider self, GameObject other )
	{
		if ( self is null )
			return;

		if ( other is null )
			return;

		// Collider pairs with it count up from zero
		if ( !TouchingObjects.TryAdd( other, 0 ) )
			return;

		if ( !self.IsValid() )
			return;

		if ( !other.IsValid() )
			return;

		if ( !self.IsTrigger )
			return;

		self.OnObjectTriggerEnter?.InvokeWithWarning( other );

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ITriggerListener, (Collider self, GameObject other)>( (self, other), static ( x, s ) => x.OnTriggerEnter( s.self, s.other ) );
	}

	private void OnObjectTriggerStop( Collider self, GameObject other )
	{
		if ( self is null )
			return;

		if ( other is null )
			return;

		// Still touching it through another pair
		if ( _touchingObjects is null || !_touchingObjects.TryGetValue( other, out var pairs ) || pairs > 0 )
			return;

		_touchingObjects.Remove( other );

		if ( !self.IsValid() )
			return;

		if ( !other.IsValid() )
			return;

		if ( !self.IsTrigger )
			return;

		self.OnObjectTriggerExit?.InvokeWithWarning( other );

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ITriggerListener, (Collider self, GameObject other)>( (self, other), static ( x, s ) => x.OnTriggerExit( s.self, s.other ) );
	}

	/// <summary>
	/// A collider we're touching was disabled, end every pair with an inactive collider.
	/// </summary>
	internal void RemoveDeactivated()
	{
		if ( _touchingPairs is null )
			return;

		List<TouchingPair> ended = null;
		foreach ( var pair in _touchingPairs )
		{
			if ( pair.Other.Active )
				continue;

			(ended ??= new()).Add( pair );
		}

		if ( ended is null )
			return;

		// One throwing listener shouldn't leave the other pairs touching
		foreach ( var pair in ended )
		{
			try
			{
				OnColliderTriggerStop( pair.Self, pair.Other, pair.GameObject );
			}
			catch ( Exception e )
			{
				Log.Warning( e, e.Message );
			}

			try
			{
				OnObjectTriggerStop( pair.Self, pair.GameObject );
			}
			catch ( Exception e )
			{
				Log.Warning( e, e.Message );
			}
		}
	}

	public void Dispose()
	{
		_disposed = true;

		if ( _touchingColliders is not null )
		{
			foreach ( var other in _touchingColliders.Keys )
				other.RemoveTouchWatcher( this );
		}

		_touchingPairs?.Clear();
		_touchingColliders?.Clear();
		_touchingObjects?.Clear();

		if ( !_body.IsValid() ) return;

		if ( _body.Listener == this ) _body.Listener = null;
	}
}
