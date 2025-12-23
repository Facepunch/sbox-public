using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Adds physics properties to an object. Requires a collider to be attached to the same object.
/// </summary>
[Expose]
[Title( "Rigid Body" )]
[Category( "Physics" )]
[Icon( "panorama_fish_eye" )]
[Alias( "PhysicsComponent" )]
sealed public partial class Rigidbody : Component, Component.ExecuteInEditor, IGameObjectNetworkEvents, IScenePhysicsEvents
{
	bool _gravity = true;

	/// <summary>
	/// Is gravity enabled or not.
	/// </summary>
	[Property]
	public bool Gravity
	{
		get => _gravity;
		set
		{
			if ( _gravity == value )
				return;

			_gravity = value;

			if ( PhysicsBody is not null )
			{
				PhysicsBody.GravityEnabled = _gravity;
			}
		}
	}

	float _gravityScale = 1.0f;

	/// <summary>
	/// Scale the gravity relative to <see cref="PhysicsWorld.Gravity"/>. 2 is double the gravity, etc.
	/// </summary>
	[Property]
	public float GravityScale
	{
		get => _gravityScale;
		set
		{
			if ( _gravityScale == value )
				return;

			_gravityScale = value;

			if ( PhysicsBody is not null )
			{
				PhysicsBody.GravityScale = _gravityScale;
			}
		}
	}

	private float _linearDamping;

	[Property]
	public float LinearDamping
	{
		get => _linearDamping;
		set
		{
			if ( _linearDamping == value )
				return;

			_linearDamping = value;

			if ( _body.IsValid() )
			{
				_body.LinearDamping = _linearDamping;
			}
		}
	}

	private float _angularDamping;

	[Property]
	public float AngularDamping
	{
		get => _angularDamping;
		set
		{
			if ( _angularDamping == value )
				return;

			_angularDamping = value;

			if ( _body.IsValid() )
			{
				_body.AngularDamping = _angularDamping;
			}
		}
	}

	float _massOverride;

	/// <summary>
	/// Override mass for this body, only when value is more than zero
	/// </summary>
	[Property, Title( "Mass Override" ), Group( "Mass" )]
	public float MassOverride
	{
		get => _massOverride;
		set
		{
			if ( _massOverride == value )
				return;

			_massOverride = value;

			if ( _body.IsValid() )
			{
				_body.Mass = _massOverride;
			}
		}
	}

	[Property, ReadOnly, Group( "Mass" ), JsonIgnore]
	public float Mass => _body.IsValid() ? _body.Mass : default;

	[Property, Group( "Mass" ), MakeDirty]
	public bool OverrideMassCenter { get; set; }

	[Property, Title( "Mass Center Override" ), Group( "Mass" ), ShowIf( nameof( OverrideMassCenter ), true ), MakeDirty]
	public Vector3 MassCenterOverride { get; set; }

	/// <summary>
	/// Center of mass for this rigidbody in local space coordinates.
	/// </summary>
	[Property, ReadOnly, Group( "Mass" ), JsonIgnore]
	public Vector3 MassCenter => _body.IsValid() ? _body.LocalMassCenter : default;

	[Property, MakeDirty]
	public PhysicsLock Locking { get; set; }

	[Property]
	public bool StartAsleep { get; set; }

	[Property, MakeDirty]
	public RigidbodyFlags RigidbodyFlags { get; set; }

	/// <summary>
	/// Whether this rigidbody can deal damage to damageable objects on high-speed impacts.
	/// </summary>
	[Property, Group( "Impact Damage" )]
	public bool EnableImpactDamage { get; set; } = true;

	/// <summary>
	/// The minimum speed required for an impact to cause damage.
	/// </summary>
	[Property, Title( "Minimum Speed" ), Group( "Impact Damage" ), ShowIf( nameof( EnableImpactDamage ), true )]
	public float MinImpactDamageSpeed { get; set; } = 500f;

	/// <summary>
	/// The amount of damage this rigidbody deals to other objects when it collides at high speed.
	/// If set to 0 or less, this will be calculated from the mass of the rigidbody.
	/// </summary>
	[Property, Group( "Impact Damage" ), ShowIf( nameof( EnableImpactDamage ), true )]
	public float ImpactDamage { get; set; } = 0f;

	PhysicsBody _body;
	CollisionEventSystem _collisionEvents;

	/// <summary>
	/// The game object source for finding collision listeners.
	/// </summary>
	internal GameObject GameObjectSource;

	Vector3 _lastVelocity;
	Vector3 _lastAngularVelocity;

	[Sync( SyncFlags.Query )]
	public Vector3 Velocity
	{
		get => _body?.Velocity ?? default;
		set
		{
			if ( _body.IsValid() && !IsProxy )
			{
				_body.Velocity = value;
			}

			_lastVelocity = value;
		}
	}

	[Sync( SyncFlags.Query )]
	public Vector3 AngularVelocity
	{
		get => _body?.AngularVelocity ?? default;
		set
		{
			if ( _body.IsValid() && !IsProxy )
			{
				_body.AngularVelocity = value;
			}

			_lastAngularVelocity = value;
		}
	}

	[Property, MakeDirty]
	public bool MotionEnabled { get; set; } = true;


	bool _collisionEventsEnabled = true;


	/// <summary>
	/// Enable or disable touch events. If you disable the events then ICollisionListener won't get any touch events
	/// and you won't get things like collision sounds.
	/// </summary>
	public bool CollisionEventsEnabled
	{
		get => _collisionEventsEnabled;

		set
		{
			_collisionEventsEnabled = value;

			if ( _body.IsValid() )
			{
				_body.EnableTouch = _collisionEventsEnabled;
			}
		}
	}

	bool _collisionUpdateEventsEnabled = false;

	/// <summary>
	/// Like CollisionEventsEnabled but means the OnCollisionUpdate gets called when the collision persists
	/// </summary>
	public bool CollisionUpdateEventsEnabled
	{
		get => _collisionUpdateEventsEnabled;

		set
		{
			_collisionUpdateEventsEnabled = value;

			if ( _body.IsValid() )
			{
				_body.EnableTouchPersists = _collisionUpdateEventsEnabled;
			}
		}
	}

	/// <inheritdoc cref="PhysicsBody.Sleeping"/>
	[Property, ReadOnly, JsonIgnore, Title( "Is Sleeping" ), Group( "State" )]
	public bool Sleeping
	{
		get => _body.IsValid() && _body.Sleeping;
		set
		{
			if ( !_body.IsValid() )
				return;

			_body.Sleeping = value;
		}
	}

	/// <summary>
	/// Gets or sets the inertia tensor for this body.  
	/// By default, the inertia tensor is automatically calculated from the shapes attached to the body.  
	/// Setting this property overrides the automatically calculated inertia tensor until <see cref="ResetInertiaTensor"/> is called.
	/// </summary>
	public Vector3 InertiaTensor
	{
		get => PhysicsBody.IsValid() ? PhysicsBody.Inertia : default;
		set
		{
			if ( !PhysicsBody.IsValid() ) return;
			PhysicsBody.SetInertiaTensor( value, PhysicsBody.InertiaRotation );
		}
	}

	/// <summary>
	/// Gets or sets the rotation applied to the inertia tensor.  
	/// Like <see cref="InertiaTensor"/>, this acts as an override to the automatically calculated inertia tensor rotation  
	/// and remains in effect until <see cref="ResetInertiaTensor"/> is called.
	/// </summary>
	public Rotation InertiaTensorRotation
	{
		get => PhysicsBody.IsValid() ? PhysicsBody.InertiaRotation : default;
		set
		{
			if ( !PhysicsBody.IsValid() ) return;
			PhysicsBody.SetInertiaTensor( PhysicsBody.Inertia, value );
		}
	}

	/// <summary>
	/// Enable enhanced continuous collision detection (CCD) for this body.
	/// When enabled, the body performs CCD against dynamic bodies
	/// (but not against other bodies with enhanced CCD enabled).
	/// This is useful for fast-moving objects like bullets or rockets
	/// that need reliable collision detection.
	/// </summary>
	[Advanced, Property]
	public bool EnhancedCcd
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			if ( _body.IsValid() ) _body.EnhancedCcd = value;
		}
	}

	/// <summary>
	/// Resets the inertia tensor and its rotation to the values automatically calculated from the attached colliders.  
	/// This removes any custom overrides set via <see cref="InertiaTensor"/> or <see cref="InertiaTensorRotation"/>.
	/// </summary>
	public void ResetInertiaTensor()
	{
		if ( !PhysicsBody.IsValid() ) return;
		PhysicsBody.ResetInertiaTensor();
	}

	internal Action<Collision> OnCollisionStart;
	internal Action<Collision> OnCollisionUpdate;
	internal Action<CollisionStop> OnCollisionStop;

	/// <summary>
	/// Gets the effective impact damage value. If ImpactDamage is not set,
	/// calculates a default value based on the rigidbody's mass.
	/// </summary>
	private float GetEffectiveImpactDamage()
	{
		if ( ImpactDamage > 0 )
			return ImpactDamage;

		// Calculate from mass if not explicitly set
		return _body.IsValid() ? _body.Mass / 10f : 10f;
	}

	/// <summary>
	/// Called when this rigidbody starts colliding with another object.
	/// Handles velocity-based damage for high-speed impacts.
	/// </summary>
	internal void HandleImpactDamage( Collision collision )
	{
		if ( !EnableImpactDamage ) return;
		if ( IsProxy ) return;

		var speed = collision.Contact.NormalSpeed;
		var minSpeed = MinImpactDamageSpeed;
		if ( minSpeed <= 0 )
			minSpeed = 500f;

		if ( speed <= minSpeed )
			return;

		var impactDmg = GetEffectiveImpactDamage();
		var damageMultiplier = speed / minSpeed;

		// I take damage from high speed impacts
		var selfDamageable = GameObject.GetComponentInParent<IDamageable>();
		if ( selfDamageable is not null )
		{
			var selfDamage = damageMultiplier * impactDmg;
			var selfDamageInfo = new DamageInfo( selfDamage, collision.Other.GameObject, collision.Other.GameObject )
			{
				Position = collision.Contact.Point
			};
			selfDamageInfo.Tags.Add( "impact" );

			selfDamageable.OnDamage( selfDamageInfo );
		}

		// The other object takes more damage
		var otherDamageable = collision.Other.GameObject?.GetComponentInParent<IDamageable>();
		if ( otherDamageable is null )
			return;

		var damage = damageMultiplier * impactDmg * 1.2f;
		var damageInfo = new DamageInfo( damage, GameObject, GameObject )
		{
			Position = collision.Contact.Point,
			Shape = collision.Other.Shape
		};
		damageInfo.Tags.Add( "impact" );

		otherDamageable.OnDamage( damageInfo );
	}

	void EnsureBodyCreated()
	{
		if ( _body.IsValid() ) return;

		_body = new PhysicsBody( Scene.PhysicsWorld );

		_body.Component = this;
		_body.Transform = WorldTransform;
		_body.AutoSleep = true;

		//defaults
		_body.EnableTouch = CollisionEventsEnabled;
		_body.EnableTouchPersists = CollisionUpdateEventsEnabled;

		// Apply velocities that were set before the body was created
		_body.Velocity = _lastVelocity;
		_body.AngularVelocity = _lastAngularVelocity;

		_body.EnhancedCcd = EnhancedCcd;

		// Make sure we clear these so we don't reapply them again later
		_lastVelocity = default;
		_lastAngularVelocity = default;

		UpdateBody();
	}

	protected override void OnEnabled()
	{
		Assert.NotNull( Scene, "Tried to create physics object but no scene" );
		Assert.NotNull( Scene.PhysicsWorld, "Tried to create physics object but no physics world" );

		EnsureBodyCreated();

		_collisionEvents?.Dispose();
		_collisionEvents = new CollisionEventSystem( _body, GameObjectSource );
		_collisionEvents.OnCollisionStart = ( c ) =>
		{
			HandleImpactDamage( c );
			OnCollisionStart?.Invoke( c );
		};
		_collisionEvents.OnCollisionUpdate = OnCollisionUpdate;
		_collisionEvents.OnCollisionStop = OnCollisionStop;

		Transform.OnTransformChanged += OnLocalTransformChanged;

		// Only set to sleep if we start asleep, the body should already be awake
		if ( StartAsleep )
		{
			_body.Sleeping = StartAsleep;
		}

		BroadcastToColliders();
	}

	internal override void OnDisabledInternal()
	{
		Transform.OnTransformChanged -= OnLocalTransformChanged;

		// Destroy shapes first so triggers can detect exits later.
		_body?.Remove();
		_body = null;

		FreeColliders();

		// Component disabled tells triggers to check for exits.
		base.OnDisabledInternal();

		// Dispose collision events last to hold onto touching for as long as possible.
		if ( GameObject.IsDestroyed )
		{
			_collisionEvents?.Dispose();
			_collisionEvents = null;
		}
	}

	internal override void OnDestroyInternal()
	{
		base.OnDestroyInternal();

		// Dispose collision events last to hold onto touching for as long as possible.
		_collisionEvents?.Dispose();
		_collisionEvents = null;
	}

	bool isUpdatingFromPhysics;

	internal void UpdateTransformFromBody()
	{
		if ( !_body.IsValid() ) return;
		if ( IsProxy ) return;

		var tx = WorldTransform;
		var target = _body.Transform.WithScale( tx.Scale );
		if ( target == tx ) return;

		isUpdatingFromPhysics = true;
		WorldTransform = target;
		isUpdatingFromPhysics = false;
	}

	/// <summary>
	/// Used for transforming a selected rigidbody in editor, if useful for gameplay this could be made public.
	/// </summary>
	internal Transform? TargetTransform { get; set; }

	/// <summary>
	/// Linear velocity before physics step. Internal until someone needs them.
	/// </summary>
	internal Vector3 PreVelocity { get; private set; }

	/// <summary>
	/// Angular velocity before physics step. Internal until someone needs them.
	/// </summary>
	internal Vector3 PreAngularVelocity { get; private set; }

	void IScenePhysicsEvents.PrePhysicsStep()
	{
		if ( !_body.IsValid() ) return;

		PreVelocity = _body.Velocity;
		PreAngularVelocity = _body.AngularVelocity;

		if ( TargetTransform.HasValue )
		{
			// Editor transform uses velocity to move.
			_body.Move( TargetTransform.Value, Time.Delta );
		}
		else if ( IsProxy && GameObject.NetworkMode == NetworkMode.Object )
		{
			// Make damn sure these are disabled.
			_body.MotionEnabled = false;
			_body.EnableCollisionSounds = false;

			// Networked proxy should use velocity to move to world transform.
			_body.Move( Transform.TargetWorld, Time.Delta );
		}
	}

	/// <summary>
	/// Called whenever the local transform of this component changes. This is used to update the physics body with the new position and rotation.
	/// </summary>
	void OnLocalTransformChanged()
	{
		if ( isUpdatingFromPhysics ) return;
		if ( IsProxy ) return;
		if ( Transform.InsideChangeCallback ) return;

		// Teleport physics body.
		if ( _body.IsValid() )
		{
			_body.Transform = WorldTransform;
		}
	}


	/// <summary>
	/// Get the actual physics body that was created by this component. You should be careful, this
	/// can of course be null when the object is not enabled or the physics world is not available.
	/// It might also get deleted and re-created, so best use this to access, but don't store it.
	/// </summary>
	public PhysicsBody PhysicsBody => _body;

	/// <summary>
	/// Returns the closest point to the given one between all convex shapes of this body.
	/// </summary>
	public Vector3 FindClosestPoint( in Vector3 position ) => _body?.FindClosestPoint( position ) ?? position;

	/// <summary>
	/// Returns the world space velocity of a point of the object. This is useful for objects rotating around their own axis/origin.
	/// </summary>
	public Vector3 GetVelocityAtPoint( in Vector3 position ) => _body?.GetVelocityAtPoint( position ) ?? Vector3.Zero;

	/// <summary>
	/// Applies force to this body at given position.
	/// </summary>
	public void ApplyForceAt( in Vector3 position, in Vector3 force ) => _body?.ApplyForceAt( position, force );

	/// <summary>
	/// Applies linear force to this body
	/// </summary>
	public void ApplyForce( in Vector3 force ) => _body?.ApplyForce( force );

	/// <summary>
	/// Applies angular velocity to this body.
	/// </summary>
	public void ApplyTorque( in Vector3 force ) => _body?.ApplyTorque( force );

	/// <summary>
	/// Applies instant linear impulse (i.e. a bullet impact) to this body at given position
	/// </summary>
	public void ApplyImpulseAt( in Vector3 position, in Vector3 force ) => _body?.ApplyImpulseAt( position, force );

	/// <summary>
	/// Applies instant linear impulse (i.e. a bullet impact) to this body
	/// </summary>
	public void ApplyImpulse( in Vector3 force ) => _body?.ApplyImpulse( force );

	/// <summary>
	/// Clear accumulated linear forces (<see cref="ApplyForce"/> and <see cref="ApplyForceAt"/>) during this physics frame that were not yet applied to the physics body.
	/// </summary>
	public void ClearForces() => _body?.ClearForces();

	/// <summary>
	/// Move body to this position in a way that cooperates with the physics system. This is quite
	/// good for things like grabbing and moving objects.
	/// </summary>
	public void SmoothMove( in Transform transform, float timeToArrive, float timeDelta )
	{
		if ( !PhysicsBody.IsValid() )
			return;

		PhysicsBody.SmoothMove( transform, timeToArrive, timeDelta );
	}

	/// <summary>
	/// Move body to this position in a way that cooperates with the physics system. This is quite
	/// good for things like grabbing and moving objects.
	/// </summary>
	public void SmoothMove( in Vector3 position, float timeToArrive, float timeDelta )
	{
		if ( !PhysicsBody.IsValid() )
			return;

		PhysicsBody.SmoothMove( position, timeToArrive, timeDelta );
	}

	/// <summary>
	/// Rotate the body to this position in a way that cooperates with the physics system.
	/// </summary>
	public void SmoothRotate( in Rotation rotation, float timeToArrive, float timeDelta )
	{
		if ( !PhysicsBody.IsValid() )
			return;

		PhysicsBody.SmoothRotate( rotation, timeToArrive, timeDelta );
	}

	/// <summary>
	/// Updates the physics body with the current properties of this component.
	/// </summary>
	internal void UpdateBody()
	{
		if ( !_body.IsValid() ) return;

		// If we're in editor, check if the editor wants to simulate us
		if ( Scene.IsEditor )
		{
			var system = Scene.GetSystem<ScenePhysicsSystem>();
			_body.BodyType = system is not null && system.HasRigidBody( this ) ? PhysicsBodyType.Dynamic : PhysicsBodyType.Static;

			// Always considered dynamic for navmesh
			_body.NavmeshBodyTypeOverride = PhysicsBodyType.Dynamic;
		}
		else
		{
			// Only enable motion when it's enabled and we're not a proxy.
			// Proxies should always be kinematic.
			_body.MotionEnabled = MotionEnabled && !IsProxy;

			// Reset whatever this is.
			_body.NavmeshBodyTypeOverride = null;
		}

		if ( IsProxy )
		{
			// Proxy doesn't need collision sounds, impacts should be networked.
			_body.EnableCollisionSounds = false;
		}
		else
		{
			// None of these properties matter on proxy.
			_body.EnableCollisionSounds = !RigidbodyFlags.Contains( RigidbodyFlags.DisableCollisionSounds );
			_body.Locking = Locking;
			_body.AngularDamping = AngularDamping;
			_body.LinearDamping = LinearDamping;
			_body.GravityEnabled = Gravity;
			_body.GravityScale = GravityScale;
			_body.Mass = MassOverride;
			_body.OverrideMassCenter = OverrideMassCenter;
			_body.LocalMassCenter = MassCenterOverride;
		}

		// Just always have controller enabled,
		// dynamic and kinematic both use controller to move in certain situations.
		_body.UseController = true;
	}

	protected override void OnDirty()
	{
		UpdateBody();
	}
	protected override void DrawGizmos()
	{
		base.DrawGizmos();

		if ( OverrideMassCenter && _body.IsValid() )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White;
			Gizmo.Draw.LineSphere( _body.MotionEnabled ? _body.LocalMassCenter : MassCenterOverride, 1, 4 );
		}
	}
	void IGameObjectNetworkEvents.StartControl()
	{
		if ( _body.IsValid() )
		{
			UpdateBody();

			_body.Velocity = _lastVelocity;
			_body.AngularVelocity = _lastAngularVelocity;
		}
	}

	void IGameObjectNetworkEvents.StopControl()
	{
		if ( _body.IsValid() )
		{
			UpdateBody();
		}
	}

	/// <summary>
	/// This is a list of all of the triggers that we are touching.
	/// </summary>
	public IEnumerable<Collider> Touching
	{
		get
		{
			if ( _collisionEvents is not null && _collisionEvents.Touching is not null )
				return _collisionEvents.Touching;

			return Array.Empty<Collider>();
		}
	}

	/// <summary>
	/// A list of joints that we're connected to, if any.
	/// </summary>
	public IReadOnlySet<Joint> Joints => _body?.Joints ?? ((IReadOnlySet<Joint>)ImmutableHashSet<Joint>.Empty);

	/// <summary>
	/// Get the world bounds of this object
	/// </summary>
	public BBox GetWorldBounds()
	{
		if ( !_body.IsValid() ) return BBox.FromPositionAndSize( WorldPosition, 0.1f );

		var bounds = _body.GetBounds();

		// shrink by B3_SPECULATIVE_DISTANCE
		bounds = bounds.Grow( -0.8f );

		return bounds;
	}
}


[Expose, Flags]
public enum RigidbodyFlags
{
	[Icon( "volume_off" ), Description( "Don't automatically play sounds when this object collides with another" )]
	DisableCollisionSounds = 1,
}
