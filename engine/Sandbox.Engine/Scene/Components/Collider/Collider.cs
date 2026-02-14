namespace Sandbox;

[Expose]
public abstract partial class Collider : Component, Component.ExecuteInEditor, Component.IHasBounds
{
	internal readonly List<PhysicsShape> Shapes = new();

	CollisionEventSystem _collisionEvents;

	private bool _static;

	[Property, HideIf( nameof( IsConcave ), true )]
	public bool Static
	{
		get => _static || IsConcave;
		set
		{
			if ( IsConcave )
				return;

			if ( _static == value )
				return;

			_static = value;

			if ( !_keyframeBody.IsValid() )
				return;

			var isKeyframed = !Static && !Scene.IsEditor;
			_keyframeBody.BodyType = isKeyframed ? PhysicsBodyType.Keyframed : PhysicsBodyType.Static;
			_keyframeBody.UseController = isKeyframed;

			if ( isKeyframed ) ScenePhysicsSystem.Current?.AddKeyframe( this );
			else ScenePhysicsSystem.Current?.RemoveKeyframe( this );
		}
	}

	public virtual bool IsConcave => false;

	/// <summary>
	/// Return true if this collider is using dynamic physics.
	/// Returns false if this is a keyframe body, or a static physics body.
	/// </summary>
	public bool IsDynamic
	{
		get
		{
			if ( Static ) return false;
			if ( _keyframeBody.IsValid() ) return false;

			return true;
		}
	}



	float? _friction;
	float? _elasticity;
	float? _rollingResistance;

	/// <summary>
	/// Allows overriding the friction for this collider. This value 
	/// can exceed 1 to to give crazy grippy friction if you want it to, 
	/// but the normal value is between 0 and 1.
	/// </summary>
	[Property, Range( 0, 1 ), Group( "Surface Properties" )]
	public float? Friction
	{
		get => _friction;
		set
		{
			if ( _friction == value ) return;
			_friction = value;

			var friction = _friction is float f ? f : -1;

			foreach ( var shape in Shapes )
			{
				shape.Friction = friction;
			}
		}
	}

	/// <summary>
	/// Allows overriding the elasticity for this collider.
	/// Controls how bouncy this collider is.
	/// </summary>
	[Property, Range( 0, 1 ), Group( "Surface Properties" )]
	public float? Elasticity
	{
		get => _elasticity;
		set
		{
			if ( _elasticity == value ) return;
			_elasticity = value;

			var elasticity = _elasticity is float f ? f : -1;

			foreach ( var shape in Shapes )
			{
				shape.Elasticity = elasticity;
			}
		}
	}

	/// <summary>
	/// Allows overriding the rolling resistance for this collider.
	/// Controls how easily rolling shapes (sphere, capsule) roll on surfaces.
	/// </summary>
	[Property, Range( 0, 1 ), Group( "Surface Properties" )]
	public float? RollingResistance
	{
		get => _rollingResistance;
		set
		{
			if ( _rollingResistance == value ) return;
			_rollingResistance = value;

			var rollingResistance = _rollingResistance is float f ? f : -1;

			foreach ( var shape in Shapes )
			{
				shape.RollingResistance = rollingResistance;
			}
		}
	}

	Surface _surface;

	[Property]
	public Surface Surface
	{
		get => _surface;
		set
		{
			if ( _surface == value )
				return;

			_surface = value;

			foreach ( var shape in Shapes )
			{
				shape.Surface = _surface;
			}
		}
	}

	Vector3 _surfaceVelocity;

	/// <summary>
	/// Set the local velocity of the surface so things can slide along it, like a conveyor belt
	/// </summary>
	[Property, Title( "Velocity" ), Group( "Surface Properties" )]
	public Vector3 SurfaceVelocity
	{
		get => _surfaceVelocity;
		set
		{
			if ( _surfaceVelocity == value )
				return;

			_surfaceVelocity = value;

			foreach ( var shape in Shapes )
				shape.SurfaceVelocity = _surfaceVelocity;
		}
	}

	bool _isTrigger;
	[Property]
	public bool IsTrigger
	{
		get => _isTrigger;
		set
		{
			_isTrigger = value;

			Rebuild();
		}
	}

	/// <summary>
	/// Calculated local bounds of all physics shapes in this collider.
	/// </summary>
	public BBox LocalBounds { get; private set; }

	private bool _rebuilding;

	[Obsolete]
	protected virtual IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody )
	{
		return [];
	}

	/// <summary>
	/// Overridable in derived component to create shapes
	/// </summary>
	protected abstract IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local );

	internal override void OnEnabledInternal()
	{
		Assert.IsNull( _keyframeBody, "keyframeBody should be null - OnDisabled wasn't called" );
		Assert.NotNull( Scene, "Scene should not be null" );

		UpdatePhysicsBody();

		Transform.OnTransformChangedInternal += TransformChanged;
		ChangeBody();

		base.OnEnabledInternal();
	}

	internal override void OnDisabledInternal()
	{
		Transform.OnTransformChangedInternal -= TransformChanged;

		DisconnectBody();

		// Component disabled tells triggers to check for exits.
		base.OnDisabledInternal();

		// Dispose collision events last to hold onto touching for as long as possible.
		if ( !GameObject.IsDestroyed )
		{
			_collisionEvents?.Dispose();
			_collisionEvents = null;
		}
	}

	internal override void OnDestroyInternal()
	{
		DisconnectBody();

		base.OnDestroyInternal();

		// Dispose collision events last to hold onto touching for as long as possible.
		_collisionEvents?.Dispose();
		_collisionEvents = null;
	}

	void DestroyShapes()
	{
		foreach ( var shape in Shapes )
		{
			shape.Remove();
		}

		Shapes.Clear();
	}

	void UpdatePhysicsBody()
	{
		ChangeBody();
	}

	/// <summary>
	/// Called when a collider enters this trigger
	/// </summary>
	[Title( "On Collider Enter" ), Group( "Trigger" )]
	[ShowIf( nameof( IsTrigger ), true )]
	[Property]
	public Action<Collider> OnTriggerEnter { get; set; }

	/// <summary>
	/// Called when a collider exits this trigger
	/// </summary>
	[Title( "On Collider Exit" ), Group( "Trigger" )]
	[ShowIf( nameof( IsTrigger ), true )]
	[Property]
	public Action<Collider> OnTriggerExit { get; set; }

	/// <summary>
	/// Called when a gameobject enters this trigger
	/// </summary>
	[Title( "On Object Enter" ), Group( "Trigger" )]
	[ShowIf( nameof( IsTrigger ), true )]
	[Property]
	public Action<GameObject> OnObjectTriggerEnter { get; set; }

	/// <summary>
	/// Called when a gameobject exits this trigger
	/// </summary>
	[Title( "On Object Exit" ), Group( "Trigger" )]
	[ShowIf( nameof( IsTrigger ), true )]
	[Property]
	public Action<GameObject> OnObjectTriggerExit { get; set; }

	internal override void OnTagsUpdatedInternal()
	{
		foreach ( var shape in Shapes )
		{
			shape.Tags.SetFrom( GameObject.Tags );
		}

		base.OnTagsUpdatedInternal();
	}

	/// <summary>
	/// If we're a trigger, this will list all of the colliders that are touching us.
	/// If we're not a trigger, this will list all of the triggers that we are touching.
	/// </summary>
	public IEnumerable<Collider> Touching
	{
		get
		{
			if ( _collisionEvents is not null && _collisionEvents.Touching is not null )
				return _collisionEvents.Touching;

			if ( Rigidbody is not null )
				return Rigidbody.Touching;

			return Array.Empty<Collider>();
		}
	}

	protected virtual void RebuildImmediately()
	{
		if ( !Active ) return;
		if ( _rebuilding ) return;

		// destroy any old shapes
		DestroyShapes();

		// find our target body
		var body = PhysicsBody;

		// no physics body
		if ( !body.IsValid() )
			return;

		_rebuilding = true;

		if ( _keyframeBody.IsValid() )
		{
			var isKeyframed = !Static && !Scene.IsEditor;

			_keyframeBody.BodyType = isKeyframed ? PhysicsBodyType.Keyframed : PhysicsBodyType.Static;
			_keyframeBody.UseController = isKeyframed;

			if ( Static )
			{
				ScenePhysicsSystem.Current?.RemoveKeyframe( this );
			}
			else
			{
				ScenePhysicsSystem.Current?.AddKeyframe( this );
			}
		}
		else
		{
			// If we're in editor, check if the editor wants to simulate us
			if ( Scene.IsEditor )
			{
				var system = ScenePhysicsSystem.Current;
				body.BodyType = system is not null && system.HasRigidBody( Rigidbody ) ? PhysicsBodyType.Dynamic : PhysicsBodyType.Static;

				// Always considered dynamic for navmesh
				body.NavmeshBodyTypeOverride = PhysicsBodyType.Dynamic;
			}
			else
			{
				body.BodyType = Rigidbody.MotionEnabled ? PhysicsBodyType.Dynamic : PhysicsBodyType.Keyframed;
				body.NavmeshBodyTypeOverride = null;
			}
		}

		// update our keyframe immediately
		TeleportKeyframeBody( WorldTransform );

		var go = body.GameObject;

		if ( !IsProxy )
		{
			var tx = go.WorldTransform;
			tx.Position = body.Position;
			tx.Rotation = body.Rotation;
			go.WorldTransform = tx;
			go.Transform.ClearLocalInterpolation();
		}

		var world = Transform.TargetWorld;
		var local = go.Transform.TargetWorld.WithScale( 1.0f ).ToLocal( world );

		// create the new shapes
		body.native.SetTrigger( IsTrigger );
		Shapes.AddRange( CreatePhysicsShapes( body, local ) );
		body.native.SetTrigger( false );

		// configure shapes
		ConfigureShapes();

		// store the transform in which we were built
		_bodyWorld = GetTargetTransform();

		_rebuilding = false;
	}

	/// <summary>
	/// Apply any things that we an apply after they're created
	/// </summary>
	protected void ConfigureShapes()
	{
		ApplyColliderFlags();

		BBox? bounds = null;
		var invScale = 1.0f / Transform.World.SafeScale;

		foreach ( var shape in Shapes )
		{
			shape.Collider = this;
			shape.IsTrigger = IsTrigger;

			// Only override with a valid surface, otherwise it will override the surface set by model collision!
			if ( Surface.IsValid() )
			{
				shape.Surface = Surface;
			}

			if ( Friction.HasValue )
			{
				shape.Friction = Friction.Value;
			}

			if ( Elasticity.HasValue )
			{
				shape.Elasticity = Elasticity.Value;
			}

			if ( RollingResistance.HasValue )
			{
				shape.RollingResistance = RollingResistance.Value;
			}

			if ( Rigidbody is not null )
			{
				shape.EnableTouch = Rigidbody.CollisionEventsEnabled;
				shape.EnableTouchPersists = Rigidbody.CollisionUpdateEventsEnabled;
			}

			shape.Tags.SetFrom( GameObject.Tags );

			shape.SurfaceVelocity = SurfaceVelocity;

			var localBounds = shape.LocalBounds.Scale( invScale );
			bounds = bounds.HasValue ? bounds.Value.AddBBox( localBounds ) : localBounds;
		}

		LocalBounds = bounds ?? default;
	}

	[Obsolete]
	public void OnPhysicsChanged()
	{
	}

	protected void Rebuild()
	{
		RebuildImmediately();
	}

	Transform _bodyWorld;

	internal virtual Transform GetTargetTransform()
	{
		return WorldTransform;
	}

	internal virtual void UpdateShape()
	{
		Rebuild();
	}

	internal virtual void TransformChanged( GameTransform root )
	{
		var bodyWorld = GetTargetTransform();

		if ( bodyWorld.Scale != _bodyWorld.Scale )
		{
			UpdateShape();
		}
		else if ( Rigidbody.IsValid() && Rigidbody.GameObject != GameObject )
		{
			// Update shape if a child of the Rigidbody changed its transform
			if ( Rigidbody.GameObject.IsDescendant( root.GameObject ) && root.GameObject != Rigidbody.GameObject )
			{
				UpdateShape();
			}
		}

		if ( !_keyframeBody.IsValid() )
		{
			_bodyWorld = bodyWorld;

			return;
		}

		var targetTransform = bodyWorld.WithScale( 1.0f );

		if ( Scene.IsEditor || Static )
		{
			_keyframeBody.Transform = targetTransform;
		}
		else
		{
			if ( !Transform.InsideChangeCallback && targetTransform == _bodyWorld.WithScale( 1.0f ) )
			{
				TeleportKeyframeBody( targetTransform );
			}
		}

		_bodyWorld = bodyWorld;
	}

	internal void OnResize<T>( in WrappedPropertySet<T> p )
	{
		var property = Game.TypeLibrary.GetMemberByIdent( p.MemberIdent ) as PropertyDescription;
		var oldValue = property.GetValue( p.Object );
		var isTheSame = Equals( p.Value, oldValue );

		p.Setter( p.Value );

		if ( isTheSame )
			return;

		UpdateShape();
	}

	protected void CalculateLocalBounds()
	{
		var invScale = 1.0f / Transform.World.SafeScale;
		LocalBounds = BBox.FromBoxes( Shapes.Where( x => x.IsValid() )
			.Select( x => x.LocalBounds.Scale( invScale ) ) );
	}

	[AttributeUsage( AttributeTargets.Property )]
	[CodeGenerator( CodeGeneratorFlags.WrapPropertySet | CodeGeneratorFlags.Instance, "OnResize" )]
	internal class ResizeAttribute : Attribute
	{

	}

	/// <summary>
	/// Get the velocity of this collider at the specific point in world coordinates.
	/// </summary>
	public Vector3 GetVelocityAtPoint( Vector3 worldPoint )
	{
		if ( KeyBody.IsValid() )
		{
			return KeyBody.GetVelocityAtPoint( worldPoint );
		}

		if ( Rigidbody.IsValid() )
		{
			return Rigidbody.GetVelocityAtPoint( worldPoint );
		}

		return default;
	}

	/// <summary>
	/// Returns the closest point to the given one between all convex shapes of this body.
	/// </summary>
	public Vector3 FindClosestPoint( Vector3 worldPoint )
	{
		if ( KeyBody.IsValid() )
		{
			return KeyBody.FindClosestPoint( worldPoint );
		}

		if ( Rigidbody.IsValid() )
		{
			return Rigidbody.FindClosestPoint( worldPoint );
		}

		return worldPoint;
	}

	/// <summary>
	/// Get the world bounds of this object
	/// </summary>
	public BBox GetWorldBounds()
	{
		if ( Shapes.Count == 0 )
			return BBox.FromPositionAndSize( WorldPosition, 0.1f );

		if ( KeyBody.IsValid() )
		{
			var bounds = KeyBody.GetBounds();
			return bounds.Grow( -0.8f );
		}
		else
		{
			var bounds = BBox.FromBoxes( Shapes.Select( x => x.BuildBounds() ) );
			return bounds.Grow( -0.8f );
		}
	}
}
