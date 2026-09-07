using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// 3D physics body backed by a native Box3D/Source 2 <see cref="IPhysicsBody"/> handle.
/// </summary>
internal sealed partial class PhysicsBody3d : PhysicsBodyInternal, IHandle
{
	internal IPhysicsBody native;

	void IHandle.HandleInit( IntPtr ptr )
	{
		native = ptr;
		World ??= native.GetWorld();
		World.RegisterBody( this );
	}

	void IHandle.HandleDestroy()
	{
		World?.OnBodyDestroyed( this );
		native = IntPtr.Zero;
	}
	bool IHandle.HandleValid() => !native.IsNull;

	public override bool IsValid => !native.IsNull;

	internal PhysicsBody3d( HandleCreationData _ ) { }

	public PhysicsBody3d( PhysicsWorld3d world )
	{
		World = world;

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			world.world.AddBody();
		}
	}

	/// <summary>
	/// Position of this body in world coordinates.
	/// </summary>
	public override Vector3 Position
	{
		get => native.GetPosition();
		set
		{
			native.SetPosition( value );
			Dirty();
		}
	}

	/// <summary>
	/// Rotation of the physics body in world space.
	/// </summary>
	public override Rotation Rotation
	{
		get => native.GetOrientation();
		set
		{
			native.SetOrientation( value );
			Dirty();
		}
	}

	/// <summary>
	/// Linear velocity of this body in world space.
	/// </summary>
	public override Vector3 Velocity
	{
		get => native.GetLinearVelocity();
		set => native.SetLinearVelocity( value );
	}

	/// <summary>
	/// Angular velocity of this body in world space.
	/// </summary>
	public override Vector3 AngularVelocity
	{
		get => native.GetAngularVelocity();
		set => native.SetAngularVelocity( value );
	}

	/// <summary>
	/// Center of mass for this physics body in world space coordinates.
	/// </summary>
	public override Vector3 MassCenter => native.GetMassCenter();

	/// <summary>
	/// Center of mass for this physics body relative to its <see cref="Position">origin</see>.
	/// </summary>
	public override Vector3 LocalMassCenter
	{
		get => native.GetLocalMassCenter();
		set => native.SetLocalMassCenter( value );
	}

	/// <summary>
	/// Is this physics body mass calculated or set directly.
	/// </summary>
	public override bool OverrideMassCenter
	{
		get => native.GetOverrideMassCenter();
		set => native.SetOverrideMassCenter( value );
	}

	/// <summary>
	/// Mass of this physics body.
	/// </summary>
	public override float Mass
	{
		get => native.GetMass();
		set => native.SetMass( value );
	}

	/// <summary>
	/// Whether gravity is enabled for this body or not.
	/// </summary>
	public override bool GravityEnabled
	{
		get => native.IsGravityEnabled();
		set => native.EnableGravity( value );
	}

	/// <summary>
	/// Scale the gravity relative to <see cref="PhysicsWorld.Gravity"/>. 2 is double the gravity, etc.
	/// </summary>
	public override float GravityScale
	{
		get => native.GetGravityScale();
		set => native.SetGravityScale( value * DefaultGravityScale );
	}

	/// <summary>
	/// Enables Touch callbacks on all <see cref="PhysicsShape">PhysicsShapes</see> of this body.
	/// Returns true if ANY of the physics shapes have touch events enabled.
	/// </summary>
	public override bool EnableTouch
	{
		get => native.IsTouchEventEnabled();
		set
		{
			if ( value )
			{
				native.EnableTouchEvents();
			}
			else
			{
				native.DisableTouchEvents();
			}
		}
	}

	// cache this, since it's called so much
	PhysicsBodyType? _bodyType3d;

	/// <summary>
	/// Movement type of physics body, either Static, Keyframed, Dynamic
	/// Note: If this body is networked and dynamic, it will return Keyframed on the client
	/// </summary>
	public override PhysicsBodyType BodyType
	{
		get
		{
			if ( !_bodyType3d.HasValue )
			{
				_bodyType3d = native.GetType_Native();
			}

			return _bodyType3d.Value;
		}
		set
		{
			if ( value == BodyType )
				return;

			native.SetType( value );
			_bodyType3d = default;

			Dirty();
		}
	}

	/// <summary>
	/// Whether this body is allowed to automatically go into "sleep" after a certain amount of time of inactivity.
	/// </summary>
	public override bool AutoSleep
	{
		set
		{
			if ( value ) native.EnableAutoSleeping();
			else native.DisableAutoSleeping();
		}
	}

	/// <summary>
	/// The speed threshold below which this body will be put to sleep.
	/// </summary>
	public override float SleepThreshold
	{
		get => native.GetSleepThreshold();
		set => native.SetSleepThreshold( value );
	}

	/// <summary>
	/// Transform of this physics body.
	/// </summary>
	public override Transform Transform
	{
		get => native.GetTransform();
		set
		{
			var tx = value.WithScale( 1 );
			if ( tx.AlmostEqual( Transform ) )
				return;

			native.SetTransform( tx.Position, tx.Rotation );
			Dirty();
		}
	}

	/// <summary>
	/// Move to a new position.
	/// </summary>
	public override void Move( Transform tx, float delta )
	{
		if ( UseController )
		{
			native.SetTargetTransform( tx.Position, tx.Rotation, delta );
		}
		else
		{
			bool transformChanged = !tx.AlmostEqual( Transform );

			native.SetTransform( tx.Position, tx.Rotation );

			if ( transformChanged )
			{
				Dirty();
			}
		}
	}

	/// <summary>
	/// Completely removes this physics body.
	/// </summary>
	public override void Remove()
	{
		if ( !this.IsValid() ) return;

		base.Remove();

		native = default;
	}

	/// <summary>
	/// Applies instant linear impulse (i.e. a bullet impact) to this body at its center of mass.
	/// </summary>
	public override void ApplyImpulse( Vector3 impulse )
	{
		native.ApplyLinearImpulse( impulse );
	}

	/// <summary>
	/// Applies instant linear impulse (i.e. a bullet impact) to this body at given position.
	/// </summary>
	public override void ApplyImpulseAt( Vector3 position, Vector3 velocity )
	{
		native.ApplyLinearImpulseAtWorldSpace( velocity, position );
	}

	/// <summary>
	/// Applies instant angular impulse (i.e. a bullet impact) to this body.
	/// </summary>
	public override void ApplyAngularImpulse( Vector3 impulse )
	{
		native.ApplyAngularImpulse( impulse );
	}

	/// <summary>
	/// Applies force to this body at the center of mass.
	/// </summary>
	public override void ApplyForce( Vector3 force ) => native.ApplyForce( force );

	/// <summary>
	/// Applies force to this body at given position.
	/// </summary>
	public override void ApplyForceAt( Vector3 position, Vector3 force ) => native.ApplyForceAt( force, position );

	/// <summary>
	/// Applies angular velocity to this body.
	/// </summary>
	public override void ApplyTorque( Vector3 force ) => native.ApplyTorque( force );

	public override void ApplyBuoyancy( Plane plane, float fluidDensity, float linearDrag, float angularDrag, Vector3 fluidVelocity, Vector3 gravity, float dt )
		=> native.ApplyBuoyancyImpulse( plane.Position, plane.Normal, fluidDensity, linearDrag, angularDrag, fluidVelocity, gravity, dt );

	/// <summary>
	/// Clear accumulated linear forces.
	/// </summary>
	public override void ClearForces() => native.ClearForces();

	/// <summary>
	/// Clear accumulated torque.
	/// </summary>
	public override void ClearTorque() => native.ClearTorque();

	/// <summary>
	/// Returns the world space velocity of a point of the object.
	/// </summary>
	public override Vector3 GetVelocityAtPoint( Vector3 point ) => native.GetVelocityAtPoint( point );

	/// <summary>
	/// Whether this body is enabled or not.
	/// </summary>
	public override bool Enabled
	{
		get => native.IsEnabled();
		set
		{
			if ( native.IsNull )
				return;

			if ( value ) native.Enable();
			else native.Disable();

			Dirty();
		}
	}

	/// <summary>
	/// Physics bodies automatically go to sleep after a certain amount of time of inactivity.
	/// </summary>
	public override bool Sleeping
	{
		get => native.IsSleeping();
		set
		{
			if ( value ) native.Sleep();
			else native.Wake();
		}
	}

	/// <summary>
	/// The physics group we belong to.
	/// </summary>
	public override PhysicsGroup PhysicsGroup
	{
		get
		{
			if ( native.IsNull ) return null;
			return native.GetAggregate();
		}
	}

	/// <summary>
	/// Returns the closest point to the given one between all shapes of this body.
	/// </summary>
	public override Vector3 FindClosestPoint( Vector3 vec ) => native.GetClosestPoint( vec );

	/// <summary>
	/// Generic linear damping.
	/// </summary>
	public override float LinearDamping
	{
		get => native.GetLinearDamping();
		set => native.SetLinearDamping( value );
	}

	/// <summary>
	/// Generic angular damping.
	/// </summary>
	public override float AngularDamping
	{
		get => native.GetAngularDamping();
		set => native.SetAngularDamping( value );
	}

	/// <summary>
	/// The diagonal elements of the local inertia tensor matrix.
	/// </summary>
	public override Vector3 Inertia => native.GetLocalInertiaVector();

	/// <summary>
	/// The orientation of the principal axes of local inertia tensor matrix.
	/// </summary>
	public override Rotation InertiaRotation => native.GetLocalInertiaOrientation();

	/// <summary>
	/// Sets the inertia tensor using the given moments and rotation.
	/// </summary>
	public override void SetInertiaTensor( Vector3 inertia, Rotation rotation ) => native.SetLocalInertia( inertia, rotation );

	/// <summary>
	/// Resets the inertia tensor to its calculated values.
	/// </summary>
	public override void ResetInertiaTensor() => native.ResetLocalInertia();

	/// <summary>
	/// Returns Axis-Aligned Bounding Box (AABB) of this physics body.
	/// </summary>
	public override BBox GetBounds() => native.BuildBounds();

	/// <summary>
	/// Returns average of densities for all physics shapes of this body.
	/// </summary>
	public override float Density => native.GetDensity();

	public override string SurfaceMaterial
	{
		get => base.SurfaceMaterial;
		set => native.SetMaterialIndex( value );
	}

	public override Surface Surface
	{
		get => base.Surface;
		set
		{
			if ( _surface == value ) return;

			_surface = value;
			native.SetMaterialIndex( _surface?.ResourceName );
		}
	}

	/// <summary>
	/// What is this body called in the group?
	/// </summary>
	public override string GroupName
	{
		get
		{
			return PhysicsGroup?.native.GetBodyName( GroupIndex );
		}
	}

	/// <summary>
	/// Return the index of this body in its PhysicsGroup
	/// </summary>
	public override int GroupIndex
	{
		get
		{
			return PhysicsGroup?.native.GetBodyIndex( this ) ?? 0;
		}
	}

	internal override void ResetProxy()
	{
		native.ResetProxy();
	}

	/// <summary>
	/// Enable enhanced continuous collision detection (CCD) for this body.
	/// </summary>
	public override bool EnhancedCcd
	{
		set => native.SetBullet( value );
	}

	public override PhysicsLock Locking
	{
		get => base.Locking;
		set
		{
			base.Locking = value;
			native.SetMotionLocks( value.X, value.Y, value.Z, value.Pitch, value.Yaw, value.Roll );
		}
	}
}
