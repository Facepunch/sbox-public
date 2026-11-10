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

	public IReadOnlyCollection<Collider> Touching => _touchingColliders is null ? Array.Empty<Collider>() : _touchingColliders;

	private record struct TouchingPair( Collider Self, Collider Other, GameObject GameObject );
	private HashSet<TouchingPair> _touchingPairs;
	private HashSet<Collider> _touchingColliders;
	private HashSet<GameObject> _touchingObjects;

	private HashSet<TouchingPair> TouchingPairs => _touchingPairs ??= new();
	private HashSet<Collider> TouchingColliders => _touchingColliders ??= new();
	private HashSet<GameObject> TouchingObjects => _touchingObjects ??= new();

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

		if ( !TouchingPairs.Any( x => x.Other == other ) )
		{
			TouchingColliders.Add( other );

			other.OnComponentDisabled += RemoveDeactivated;
		}

		if ( !TouchingPairs.Add( new TouchingPair( self, other, go ) ) )
			return;

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

		foreach ( var shape in self.Shapes )
		{
			if ( other.Shapes.Any( x => shape.IsTouching( x, true ) ) )
				return;
		}

		if ( !TouchingPairs.Remove( new TouchingPair( self, other, go ) ) )
			return;

		if ( !TouchingPairs.Any( x => x.Other == other ) )
		{
			TouchingColliders.Remove( other );

			other.OnComponentDisabled -= RemoveDeactivated;
		}

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

		if ( !TouchingObjects.Add( other ) )
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

		if ( TouchingPairs.Any( x => x.GameObject == other ) )
			return;

		if ( !TouchingObjects.Remove( other ) )
			return;

		if ( !self.IsValid() )
			return;

		if ( !other.IsValid() )
			return;

		if ( !self.IsTrigger )
			return;

		self.OnObjectTriggerExit?.InvokeWithWarning( other );

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ITriggerListener, (Collider self, GameObject other)>( (self, other), static ( x, s ) => x.OnTriggerExit( s.self, s.other ) );
	}

	private void RemoveDeactivated()
	{
		Action actions = default;

		foreach ( var pair in TouchingPairs )
		{
			if ( pair.Other.Active )
				continue;

			actions += () => OnColliderTriggerStop( pair.Self, pair.Other, pair.GameObject );
			actions += () => OnObjectTriggerStop( pair.Self, pair.GameObject );
		}

		actions?.InvokeWithWarning();
	}

	public void Dispose()
	{
		_touchingPairs?.Clear();
		_touchingColliders?.Clear();
		_touchingObjects?.Clear();

		if ( !_body.IsValid() ) return;

		if ( _body.Listener == this ) _body.Listener = null;
	}
}
