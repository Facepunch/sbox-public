using NativeEngine;
using Sandbox.Physics;
using System.Runtime.InteropServices;

namespace Sandbox;

/// <summary>
/// 2D physics body backed by a Box2D b2BodyId.
/// </summary>
internal sealed partial class PhysicsBody2d : PhysicsBodyInternal
{
	b2BodyId _bodyId;
	internal b2BodyId BodyId
	{
		get => Box2d.b2Body_IsValid( _bodyId )
			? _bodyId
			: throw new InvalidOperationException( "This PhysicsBody has been removed and is no longer valid." );
		set => _bodyId = value;
	}
	GCHandle _handle;
	readonly List<PhysicsShape2d> _shapes = [];
	readonly List<PhysicsJoint2d> _joints = [];

	b2MassData _baseMassData;
	float _massOverride;
	bool _overrideMassCenter;
	Vector3 _localMassCenterOverride;
	bool _overrideInertia;
	float _inertiaOverride;

	public override bool IsValid => Box2d.b2Body_IsValid( _bodyId );

	public PhysicsBody2d( PhysicsWorld2d world, b2BodyId bodyId )
	{
		World = world;
		BodyId = bodyId;

		_handle = GCHandle.Alloc( this );
		Box2d.b2Body_SetUserData( BodyId, GCHandle.ToIntPtr( _handle ) );
	}

	internal static PhysicsBody2d FromUserData( b2BodyId bodyId )
	{
		var ptr = Box2d.b2Body_GetUserData( bodyId );
		if ( ptr == IntPtr.Zero ) return null;
		return GCHandle.FromIntPtr( ptr ).Target as PhysicsBody2d;
	}

	public override Vector3 Position
	{
		get => Box2d.b2Body_GetPosition( BodyId );
		set => Box2d.b2Body_SetTransform( BodyId, value, Box2d.b2Body_GetRotation( BodyId ) );
	}

	public override Rotation Rotation
	{
		get => Box2d.b2Body_GetRotation( BodyId );
		set => Box2d.b2Body_SetTransform( BodyId, Box2d.b2Body_GetPosition( BodyId ), value );
	}

	public override Transform Transform
	{
		get => Box2d.b2Body_GetTransform( BodyId );
		set => Box2d.b2Body_SetTransform( BodyId, value.Position, value.Rotation );
	}

	public override void Move( Transform tx, float delta )
	{
		Box2d.b2Body_SetTargetTransform( BodyId, tx, delta, true );
	}

	public override Vector3 Velocity
	{
		get => Box2d.b2Body_GetLinearVelocity( BodyId );
		set => Box2d.b2Body_SetLinearVelocity( BodyId, value );
	}

	public override Vector3 AngularVelocity
	{
		get => new( 0, 0, Box2d.b2Body_GetAngularVelocity( BodyId ).RadianToDegree() );
		set => Box2d.b2Body_SetAngularVelocity( BodyId, value.z.DegreeToRadian() );
	}

	public override float Mass
	{
		get => Box2d.b2Body_GetMass( BodyId );
		set
		{
			_massOverride = value;
			UpdateMass();
		}
	}

	public override void RebuildMass()
	{
		Box2d.b2Body_ApplyMassFromShapes( BodyId );
		_baseMassData = Box2d.b2Body_GetMassData( BodyId );
		UpdateMass();
	}

	void UpdateMass()
	{
		var data = _baseMassData;

		if ( _massOverride > 0f && data.mass > 0f )
		{
			float ratio = _massOverride / data.mass;
			data.rotationalInertia *= ratio;
			data.mass = _massOverride;
		}

		if ( _overrideMassCenter )
			data.center = _localMassCenterOverride;

		if ( _overrideInertia )
			data.rotationalInertia = _inertiaOverride;

		Box2d.b2Body_SetMassData( BodyId, data );
	}

	public override bool GravityEnabled
	{
		get => base.GravityEnabled;
		set
		{
			base.GravityEnabled = value;
			Box2d.b2Body_SetGravityScale( BodyId, value ? base.GravityScale : 0f );
		}
	}

	public override float GravityScale
	{
		get => base.GravityScale;
		set
		{
			base.GravityScale = value;
			Box2d.b2Body_SetGravityScale( BodyId, GravityEnabled ? value : 0f );
		}
	}

	public override float LinearDamping
	{
		get => Box2d.b2Body_GetLinearDamping( BodyId );
		set => Box2d.b2Body_SetLinearDamping( BodyId, value );
	}

	public override float AngularDamping
	{
		get => Box2d.b2Body_GetAngularDamping( BodyId );
		set => Box2d.b2Body_SetAngularDamping( BodyId, value );
	}

	public override Vector3 MassCenter => Box2d.b2Body_GetWorldCenterOfMass( BodyId );

	public override Vector3 LocalMassCenter
	{
		get => Box2d.b2Body_GetLocalCenterOfMass( BodyId );
		set
		{
			_localMassCenterOverride = value;
			UpdateMass();
		}
	}

	public override bool OverrideMassCenter
	{
		get => _overrideMassCenter;
		set
		{
			if ( _overrideMassCenter == value )
				return;

			_overrideMassCenter = value;
			UpdateMass();
		}
	}

	public override Vector3 Inertia => new( 0, 0, Box2d.b2Body_GetRotationalInertia( BodyId ) );

	public override Rotation InertiaRotation => Rotation.Identity;

	public override void SetInertiaTensor( Vector3 inertia, Rotation rotation )
	{
		_inertiaOverride = inertia.z;
		_overrideInertia = true;
		UpdateMass();
	}

	public override void ResetInertiaTensor()
	{
		if ( !_overrideInertia )
			return;

		_overrideInertia = false;
		UpdateMass();
	}

	public override float SleepThreshold
	{
		get => Box2d.b2Body_GetSleepThreshold( BodyId );
		set => Box2d.b2Body_SetSleepThreshold( BodyId, value );
	}

	public override bool AutoSleep
	{
		set => Box2d.b2Body_EnableSleep( BodyId, value );
	}

	public override PhysicsBodyType BodyType
	{
		get => Box2d.b2Body_GetType( BodyId ) switch
		{
			b2BodyType.b2_dynamicBody => PhysicsBodyType.Dynamic,
			b2BodyType.b2_kinematicBody => PhysicsBodyType.Keyframed,
			_ => PhysicsBodyType.Static
		};
		set
		{
			var b2Type = value switch
			{
				PhysicsBodyType.Dynamic => b2BodyType.b2_dynamicBody,
				PhysicsBodyType.Keyframed => b2BodyType.b2_kinematicBody,
				_ => b2BodyType.b2_staticBody
			};
			Box2d.b2Body_SetType( BodyId, b2Type );
			RebuildMass();
		}
	}

	public override bool Sleeping
	{
		get => !Box2d.b2Body_IsAwake( BodyId );
		set
		{
			Box2d.b2Body_SetAwake( BodyId, !value );
		}
	}

	public override bool Enabled
	{
		get => Box2d.b2Body_IsEnabled( BodyId );
		set
		{
			if ( value ) Box2d.b2Body_Enable( BodyId );
			else Box2d.b2Body_Disable( BodyId );
		}
	}

	public override bool EnhancedCcd
	{
		set => Box2d.b2Body_SetBullet( BodyId, value );
	}

	public override PhysicsLock Locking
	{
		set
		{
			Box2d.b2Body_SetMotionLocks( BodyId, new b2MotionLocks
			{
				linearX = value.X ? (byte)1 : (byte)0,
				linearY = value.Y ? (byte)1 : (byte)0,
				angularZ = value.Yaw ? (byte)1 : (byte)0
			} );
		}
	}

	public override bool EnableTouch
	{
		get
		{
			foreach ( var shape in Shapes )
				if ( shape.EnableTouch ) return true;
			return false;
		}
		set
		{
			foreach ( var shape in Shapes )
				shape.EnableTouch = value;
		}
	}

	public override void ApplyForce( Vector3 force )
		=> Box2d.b2Body_ApplyForceToCenter( BodyId, force, true );

	public override void ApplyForceAt( Vector3 position, Vector3 force )
		=> Box2d.b2Body_ApplyForce( BodyId, force, position, true );

	public override void ApplyImpulse( Vector3 impulse )
		=> Box2d.b2Body_ApplyLinearImpulseToCenter( BodyId, impulse, true );

	public override void ApplyImpulseAt( Vector3 position, Vector3 velocity )
		=> Box2d.b2Body_ApplyLinearImpulse( BodyId, velocity, position, true );

	public override void ApplyTorque( Vector3 force )
		=> Box2d.b2Body_ApplyTorque( BodyId, force.z, true );

	public override void ApplyAngularImpulse( Vector3 impulse )
		=> Box2d.b2Body_ApplyAngularImpulse( BodyId, impulse.z, true );

	public override void ClearForces()
		=> Box2d.b2Body_ClearForces( BodyId );

	public override void ClearTorque()
		=> Box2d.b2Body_ClearForces( BodyId );

	public override Vector3 GetVelocityAtPoint( Vector3 point )
		=> Box2d.b2Body_GetWorldPointVelocity( BodyId, point );

	public override BBox GetBounds()
	{
		var count = ShapeCount;
		if ( count <= 0 )
			return default;

		var bounds = new BBox();
		bool first = true;

		foreach ( var shape2d in _shapes )
		{
			if ( !shape2d.IsValid )
				continue;

			var aabb = Box2d.b2Shape_GetAABB( shape2d.ShapeId );
			Vector3 min = aabb.lowerBound;
			Vector3 max = aabb.upperBound;

			if ( first )
			{
				bounds = new BBox( min, max );
				first = false;
			}
			else
			{
				bounds = bounds.AddPoint( min );
				bounds = bounds.AddPoint( max );
			}
		}

		return bounds;
	}

	public override float Density
	{
		get
		{
			var count = ShapeCount;
			if ( count <= 0 )
				return 0;

			float total = 0;
			foreach ( var shape2d in _shapes )
			{
				if ( shape2d.IsValid )
					total += Box2d.b2Shape_GetDensity( shape2d.ShapeId );
			}

			return total / count;
		}
	}

	internal void RegisterJoint( PhysicsJoint2d joint ) => _joints.Add( joint );
	internal void UnregisterJoint( PhysicsJoint2d joint ) => _joints.Remove( joint );

	public override void Remove()
	{
		if ( this.IsValid() )
		{
			foreach ( var joint in _joints.ToArray() )
				joint.Remove();

			foreach ( var shape in _shapes.ToArray() )
				shape.Remove();

			_shapes.Clear();

			base.Remove();

			Box2d.b2DestroyBody( BodyId );
			BodyId = default;
		}

		if ( _handle.IsAllocated )
			_handle.Free();
	}
}
