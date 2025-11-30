using static Sandbox.Component;

namespace Sandbox;

/// <summary>
/// Used to abstract the listening of collision events
/// </summary>
class CollisionEventSystem : IDisposable
{
	private readonly PhysicsBody _body;
	private readonly GameObject _gameObject;

	public Action<Collision> OnCollisionStart;
	public Action<CollisionStop> OnCollisionStop;
	public Action<Collision> OnCollisionUpdate;

	public IReadOnlyCollection<Collider> Touching => _touchingColliders is null ? Array.Empty<Collider>() : _touchingColliders;

	private record struct TouchingPair( Collider Self, Collider Other, GameObject GameObject );
	private HashSet<TouchingPair> _touchingPairs;
	private HashSet<Collider> _touchingColliders;
	private HashSet<GameObject> _touchingObjects;

	private HashSet<TouchingPair> TouchingPairs => _touchingPairs ??= new();
	private HashSet<Collider> TouchingColliders => _touchingColliders ??= new();
	private HashSet<GameObject> TouchingObjects => _touchingObjects ??= new();

	public CollisionEventSystem( PhysicsBody body, GameObject go = null )
	{
		_body = body;
		_gameObject = go ?? body.GameObject;

		_body.OnIntersectionStart += OnIntersectionStart;
		_body.OnIntersectionEnd += OnIntersectionEnd;
		_body.OnIntersectionUpdate += OnIntersectionUpdate;
		_body.OnTriggerBegin += OnTriggerBegin;
		_body.OnTriggerEnd += OnTriggerEnd;
	}

	private void OnTriggerBegin( PhysicsIntersection c )
	{
		var self = new CollisionSource( c.Self );
		var other = new CollisionSource( c.Other );
		var gameObject = other.Body.GameObject;

		OnObjectTriggerStart( self.Collider, gameObject );
		OnColliderTriggerStart( self.Collider, other.Collider, gameObject );
	}

	private void OnTriggerEnd( PhysicsIntersectionEnd c )
	{
		var self = new CollisionSource( c.Self );
		var other = new CollisionSource( c.Other );
		var gameObject = other.Body.GameObject;

		OnColliderTriggerStop( self.Collider, other.Collider, gameObject );

		if ( IsTouching( other.Component ) )
			return;

		OnObjectTriggerStop( self.Collider, gameObject );
	}

	private void OnIntersectionStart( PhysicsIntersection c )
	{
		var self = new CollisionSource( c.Self );
		var other = new CollisionSource( c.Other );
		var o = new Collision( self, other, c.Contact );

		OnCollisionStart?.Invoke( o );

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ICollisionListener>( x => x.OnCollisionStart( o ) );
	}

	private bool IsTouching( Component other )
	{
		return false;
	}

	private void OnIntersectionEnd( PhysicsIntersectionEnd c )
	{
		var self = new CollisionSource( c.Self );
		var other = new CollisionSource( c.Other );
		var o = new CollisionStop( self, other );

		OnCollisionStop?.Invoke( o );

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ICollisionListener>( x => x.OnCollisionStop( o ) );
	}

	private void OnIntersectionUpdate( PhysicsIntersection c )
	{
		var self = new CollisionSource( c.Self );
		var other = new CollisionSource( c.Other );
		var o = new Collision( self, other, c.Contact );

		OnCollisionUpdate?.Invoke( o );

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ICollisionListener>( x => x.OnCollisionUpdate( o ) );
	}

	private void OnColliderTriggerStart( Collider self, Collider other, GameObject go )
	{
		if ( self is null )
			return;

		if ( other is null )
			return;

		bool pairExists = false;
		foreach ( var pair in TouchingPairs )
		{
			if ( pair.Other == other )
			{
				pairExists = true;
				break;
			}
		}

		if ( !pairExists )
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

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ITriggerListener>( x => x.OnTriggerEnter( self, other ) );
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

		bool pairExists = false;
		foreach ( var pair in TouchingPairs )
		{
			if ( pair.Other == other )
			{
				pairExists = true;
				break;
			}
		}

		if ( !pairExists )
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

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ITriggerListener>( x => x.OnTriggerExit( self, other ) );
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

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ITriggerListener>( x => x.OnTriggerEnter( self, other ) );
	}

	private void OnObjectTriggerStop( Collider self, GameObject other )
	{
		if ( self is null )
			return;

		if ( other is null )
			return;

		bool pairExists = false;
		foreach ( var pair in TouchingPairs )
		{
			if ( pair.GameObject == other )
			{
				pairExists = true;
				break;
			}
		}

		if ( pairExists )
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

		_gameObject.Components.ExecuteEnabledInSelfAndDescendants<ITriggerListener>( x => x.OnTriggerExit( self, other ) );
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

		if ( !_body.IsValid() )
			return;

		_body.OnIntersectionStart -= OnIntersectionStart;
		_body.OnIntersectionEnd -= OnIntersectionEnd;
		_body.OnIntersectionUpdate -= OnIntersectionUpdate;
		_body.OnTriggerBegin -= OnTriggerBegin;
		_body.OnTriggerEnd -= OnTriggerEnd;
	}
}
