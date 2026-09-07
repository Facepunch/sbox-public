using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox.Physics;

/// <summary>
/// 2D physics joint backed by Box2D.
/// </summary>
internal sealed class PhysicsJoint2d : PhysicsJointInternal
{
	b2JointId _jointId;
	internal b2JointId JointId
	{
		get => Box2d.b2Joint_IsValid( _jointId )
			? _jointId
			: throw new InvalidOperationException( "This PhysicsJoint has been removed and is no longer valid." );
		set => _jointId = value;
	}
	GCHandle _handle;

	public override bool IsValid => Box2d.b2Joint_IsValid( _jointId );

	public override PhysicsBody Body1 { get; }
	public override PhysicsBody Body2 { get; }

	readonly PhysicsBody2d _body1;
	readonly PhysicsBody2d _body2;

	internal PhysicsJoint2d( PhysicsWorld2d world, b2JointId jointId, PhysicsBody2d body1, PhysicsBody2d body2 )
	{
		World = world;
		JointId = jointId;
		Body1 = body1?.Owner;
		Body2 = body2?.Owner;

		_body1 = body1;
		_body2 = body2;
		_body1?.RegisterJoint( this );
		_body2?.RegisterJoint( this );

		_handle = GCHandle.Alloc( this );
		Box2d.b2Joint_SetUserData( JointId, GCHandle.ToIntPtr( _handle ) );
	}

	internal static PhysicsJoint2d FromUserData( b2JointId jointId )
	{
		if ( !Box2d.b2Joint_IsValid( jointId ) )
			return null;

		var ptr = Box2d.b2Joint_GetUserData( jointId );
		if ( ptr == IntPtr.Zero ) return null;
		return GCHandle.FromIntPtr( ptr ).Target as PhysicsJoint2d;
	}

	internal override PhysicsJointType JointType => Box2d.b2Joint_GetType( JointId ) switch
	{
		b2JointType.b2_weldJoint => PhysicsJointType.WELD_JOINT,
		b2JointType.b2_distanceJoint => PhysicsJointType.SPRING,
		b2JointType.b2_revoluteJoint => PhysicsJointType.REVOLUTE_JOINT,
		b2JointType.b2_prismaticJoint => PhysicsJointType.PRISMATIC_JOINT,
		b2JointType.b2_motorJoint => PhysicsJointType.MOUSE_JOINT,
		b2JointType.b2_wheelJoint => PhysicsJointType.WHEEL_JOINT,
		_ => PhysicsJointType.NULL_JOINT,
	};


	public override void GetLocalFrameA( out Vector3 position, out Rotation rotation )
	{
		Transform frame = Box2d.b2Joint_GetLocalFrameA( JointId );
		position = frame.Position;
		rotation = frame.Rotation;
	}

	public override void GetLocalFrameB( out Vector3 position, out Rotation rotation )
	{
		Transform frame = Box2d.b2Joint_GetLocalFrameB( JointId );
		position = frame.Position;
		rotation = frame.Rotation;
	}

	public override void SetLocalFrameA( Vector3 position, Rotation rotation )
	{
		Box2d.b2Joint_SetLocalFrameA( JointId, new Transform( position, rotation ) );
	}

	public override void SetLocalFrameB( Vector3 position, Rotation rotation )
	{
		Box2d.b2Joint_SetLocalFrameB( JointId, new Transform( position, rotation ) );
	}

	public override void Remove()
	{
		_body1?.UnregisterJoint( this );
		_body2?.UnregisterJoint( this );

		if ( _handle.IsAllocated )
			_handle.Free();

		if ( !Box2d.b2Joint_IsValid( _jointId ) ) return;

		Box2d.b2DestroyJoint( JointId, true );
		JointId = default;
	}

	public override bool Collisions
	{
		get => Box2d.b2Joint_GetCollideConnected( JointId );
		set => Box2d.b2Joint_SetCollideConnected( JointId, value );
	}

	public override float Strength
	{
		get => Box2d.b2Joint_GetForceThreshold( JointId );
		set => Box2d.b2Joint_SetForceThreshold( JointId, value > 0 ? value : float.MaxValue );
	}

	public override float AngularStrength
	{
		get => Box2d.b2Joint_GetTorqueThreshold( JointId );
		set => Box2d.b2Joint_SetTorqueThreshold( JointId, value > 0 ? value : float.MaxValue );
	}

	internal override float LinearImpulse
	{
		get
		{
			var force = Box2d.b2Joint_GetConstraintForce( JointId );
			return new Vector2( force.x, force.y ).Length;
		}
	}

	internal override float AngularImpulse => Box2d.b2Joint_GetConstraintTorque( JointId );

	internal override void WakeBodies()
	{
		Box2d.b2Joint_WakeBodies( JointId );
	}

	public override void SetAngularMotor( float targetVelocity, float maxTorque )
	{
		if ( Box2d.b2Joint_GetType( JointId ) != b2JointType.b2_revoluteJoint )
			return;

		Box2d.b2RevoluteJoint_EnableMotor( JointId, true );
		Box2d.b2RevoluteJoint_SetMotorSpeed( JointId, targetVelocity );
		Box2d.b2RevoluteJoint_SetMaxMotorTorque( JointId, maxTorque );
	}

	public override void SetAngularSpring( Vector3 parameters )
	{
		if ( Box2d.b2Joint_GetType( JointId ) != b2JointType.b2_revoluteJoint )
			return;

		Box2d.b2RevoluteJoint_EnableSpring( JointId, true );
		Box2d.b2RevoluteJoint_SetTargetAngle( JointId, parameters.x );
		Box2d.b2RevoluteJoint_SetSpringHertz( JointId, parameters.y );
		Box2d.b2RevoluteJoint_SetSpringDampingRatio( JointId, parameters.z );
	}


	public override PhysicsSpring SpringLinear
	{
		get
		{
			var type = Box2d.b2Joint_GetType( JointId );
			if ( type == b2JointType.b2_weldJoint )
				return new PhysicsSpring( Box2d.b2WeldJoint_GetLinearHertz( JointId ), Box2d.b2WeldJoint_GetLinearDampingRatio( JointId ) );

			if ( type == b2JointType.b2_distanceJoint )
				return new PhysicsSpring( Box2d.b2DistanceJoint_GetSpringHertz( JointId ), Box2d.b2DistanceJoint_GetSpringDampingRatio( JointId ) );

			return default;
		}
		set
		{
			var type = Box2d.b2Joint_GetType( JointId );
			if ( type == b2JointType.b2_weldJoint )
			{
				Box2d.b2WeldJoint_SetLinearHertz( JointId, value.Frequency );
				Box2d.b2WeldJoint_SetLinearDampingRatio( JointId, value.Damping );
			}
			else if ( type == b2JointType.b2_distanceJoint )
			{
				Box2d.b2DistanceJoint_SetLength( JointId, value.Maximum );
				Box2d.b2DistanceJoint_EnableSpring( JointId, true );
				Box2d.b2DistanceJoint_SetSpringHertz( JointId, value.Frequency );
				Box2d.b2DistanceJoint_SetSpringDampingRatio( JointId, value.Damping );
			}
		}
	}

	public override PhysicsSpring SpringAngular
	{
		get
		{
			if ( Box2d.b2Joint_GetType( JointId ) != b2JointType.b2_weldJoint ) return default;
			return new PhysicsSpring( Box2d.b2WeldJoint_GetAngularHertz( JointId ), Box2d.b2WeldJoint_GetAngularDampingRatio( JointId ) );
		}
		set
		{
			if ( Box2d.b2Joint_GetType( JointId ) != b2JointType.b2_weldJoint ) return;
			Box2d.b2WeldJoint_SetAngularHertz( JointId, value.Frequency );
			Box2d.b2WeldJoint_SetAngularDampingRatio( JointId, value.Damping );
		}
	}


	public override float MinLength
	{
		get
		{
			var type = Box2d.b2Joint_GetType( JointId );
			if ( type == b2JointType.b2_distanceJoint ) return Box2d.b2DistanceJoint_GetMinLength( JointId );
			if ( type == b2JointType.b2_revoluteJoint ) return Box2d.b2RevoluteJoint_GetLowerLimit( JointId );
			if ( type == b2JointType.b2_prismaticJoint ) return Box2d.b2PrismaticJoint_GetLowerLimit( JointId );
			return 0;
		}
		set
		{
			var type = Box2d.b2Joint_GetType( JointId );
			if ( type == b2JointType.b2_distanceJoint )
			{
				Box2d.b2DistanceJoint_EnableLimit( JointId, true );
				Box2d.b2DistanceJoint_SetLengthRange( JointId, value, MaxLength );
			}
			else if ( type == b2JointType.b2_revoluteJoint )
			{
				var upper = Box2d.b2RevoluteJoint_GetUpperLimit( JointId );
				Box2d.b2RevoluteJoint_SetLimits( JointId, value, upper );
				Box2d.b2RevoluteJoint_EnableLimit( JointId, upper > value );
			}
			else if ( type == b2JointType.b2_prismaticJoint )
			{
				Box2d.b2PrismaticJoint_EnableLimit( JointId, true );
				Box2d.b2PrismaticJoint_SetLimits( JointId, value, Box2d.b2PrismaticJoint_GetUpperLimit( JointId ) );
			}
		}
	}

	public override float MaxLength
	{
		get
		{
			var type = Box2d.b2Joint_GetType( JointId );
			if ( type == b2JointType.b2_distanceJoint ) return Box2d.b2DistanceJoint_GetMaxLength( JointId );
			if ( type == b2JointType.b2_revoluteJoint ) return Box2d.b2RevoluteJoint_GetUpperLimit( JointId );
			if ( type == b2JointType.b2_prismaticJoint ) return Box2d.b2PrismaticJoint_GetUpperLimit( JointId );
			return 0;
		}
		set
		{
			var type = Box2d.b2Joint_GetType( JointId );
			if ( type == b2JointType.b2_distanceJoint )
			{
				Box2d.b2DistanceJoint_EnableLimit( JointId, true );
				Box2d.b2DistanceJoint_SetLengthRange( JointId, MinLength, value );
			}
			else if ( type == b2JointType.b2_revoluteJoint )
			{
				var lower = Box2d.b2RevoluteJoint_GetLowerLimit( JointId );
				Box2d.b2RevoluteJoint_SetLimits( JointId, lower, value );
				Box2d.b2RevoluteJoint_EnableLimit( JointId, value > lower );
			}
			else if ( type == b2JointType.b2_prismaticJoint )
			{
				Box2d.b2PrismaticJoint_EnableLimit( JointId, true );
				Box2d.b2PrismaticJoint_SetLimits( JointId, Box2d.b2PrismaticJoint_GetLowerLimit( JointId ), value );
			}
		}
	}

	public override float Angle
	{
		get
		{
			if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_revoluteJoint )
				return Box2d.b2RevoluteJoint_GetAngle( JointId );
			return 0;
		}
	}


	public override Vector3 Motor_LinearVelocity
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? (Vector3)Box2d.b2MotorJoint_GetLinearVelocity( JointId ) : default;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetLinearVelocity( JointId, value ); }
	}

	public override Vector3 Motor_AngularVelocity
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? new Vector3( 0, 0, Box2d.b2MotorJoint_GetAngularVelocity( JointId ) ) : default;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetAngularVelocity( JointId, value.z ); }
	}

	public override float Motor_MaxVelocityForce
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? Box2d.b2MotorJoint_GetMaxVelocityForce( JointId ) : 0;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetMaxVelocityForce( JointId, value ); }
	}

	public override float Motor_MaxVelocityTorque
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? Box2d.b2MotorJoint_GetMaxVelocityTorque( JointId ) : 0;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetMaxVelocityTorque( JointId, value ); }
	}

	public override float Motor_LinearHertz
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? Box2d.b2MotorJoint_GetLinearHertz( JointId ) : 0;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetLinearHertz( JointId, value ); }
	}

	public override float Motor_LinearDampingRatio
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? Box2d.b2MotorJoint_GetLinearDampingRatio( JointId ) : 0;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetLinearDampingRatio( JointId, value ); }
	}

	public override float Motor_AngularHertz
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? Box2d.b2MotorJoint_GetAngularHertz( JointId ) : 0;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetAngularHertz( JointId, value ); }
	}

	public override float Motor_AngularDampingRatio
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? Box2d.b2MotorJoint_GetAngularDampingRatio( JointId ) : 0;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetAngularDampingRatio( JointId, value ); }
	}

	public override float Motor_MaxSpringForce
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? Box2d.b2MotorJoint_GetMaxSpringForce( JointId ) : 0;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetMaxSpringForce( JointId, value ); }
	}

	public override float Motor_MaxSpringTorque
	{
		get => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ? Box2d.b2MotorJoint_GetMaxSpringTorque( JointId ) : 0;
		set { if ( Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_motorJoint ) Box2d.b2MotorJoint_SetMaxSpringTorque( JointId, value ); }
	}


	bool IsWheel => Box2d.b2Joint_GetType( JointId ) == b2JointType.b2_wheelJoint;

	public override bool Wheel_EnableSuspension
	{
		get => IsWheel && Box2d.b2WheelJoint_IsSpringEnabled( JointId );
		set { if ( IsWheel ) Box2d.b2WheelJoint_EnableSpring( JointId, value ); }
	}

	public override float Wheel_SuspensionHertz
	{
		get => IsWheel ? Box2d.b2WheelJoint_GetSpringHertz( JointId ) : 0;
		set { if ( IsWheel ) Box2d.b2WheelJoint_SetSpringHertz( JointId, value ); }
	}

	public override float Wheel_SuspensionDampingRatio
	{
		get => IsWheel ? Box2d.b2WheelJoint_GetSpringDampingRatio( JointId ) : 0;
		set { if ( IsWheel ) Box2d.b2WheelJoint_SetSpringDampingRatio( JointId, value ); }
	}

	public override bool Wheel_EnableSuspensionLimit
	{
		get => IsWheel && Box2d.b2WheelJoint_IsLimitEnabled( JointId );
		set { if ( IsWheel ) Box2d.b2WheelJoint_EnableLimit( JointId, value ); }
	}

	public override float Wheel_LowerSuspensionLimit => IsWheel ? Box2d.b2WheelJoint_GetLowerLimit( JointId ) : 0;
	public override float Wheel_UpperSuspensionLimit => IsWheel ? Box2d.b2WheelJoint_GetUpperLimit( JointId ) : 0;

	public override void Wheel_SetSuspensionLimits( float lower, float upper )
	{
		if ( IsWheel ) Box2d.b2WheelJoint_SetLimits( JointId, lower, upper );
	}

	public override bool Wheel_EnableSpinMotor
	{
		get => IsWheel && Box2d.b2WheelJoint_IsMotorEnabled( JointId );
		set { if ( IsWheel ) Box2d.b2WheelJoint_EnableMotor( JointId, value ); }
	}

	public override float Wheel_SpinMotorSpeed
	{
		get => IsWheel ? Box2d.b2WheelJoint_GetMotorSpeed( JointId ) : 0;
		set { if ( IsWheel ) Box2d.b2WheelJoint_SetMotorSpeed( JointId, value ); }
	}

	public override float Wheel_MaxSpinTorque
	{
		get => IsWheel ? Box2d.b2WheelJoint_GetMaxMotorTorque( JointId ) : 0;
		set { if ( IsWheel ) Box2d.b2WheelJoint_SetMaxMotorTorque( JointId, value ); }
	}

	public override float Wheel_SpinTorque => IsWheel ? Box2d.b2WheelJoint_GetMotorTorque( JointId ) : 0;
}
