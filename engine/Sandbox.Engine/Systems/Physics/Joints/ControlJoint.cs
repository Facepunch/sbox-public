namespace Sandbox.Physics;

/// <summary>
/// The control joint is designed to control the movement of a body while remaining responsive to collisions.  
/// A spring can be used to control position and rotation, while a velocity motor can control velocity and  
/// simulate friction in top-down games. Both methods can be combined — for example, a spring with friction.  
/// Position and velocity control each have configurable force and torque limits.
/// </summary>
public partial class ControlJoint : PhysicsJoint
{
	internal ControlJoint( PhysicsJointInternal joint ) : base( joint ) { }

	/// <summary>
	/// The desired relative linear velocity.
	/// </summary>
	public Vector3 LinearVelocity
	{
		get => _joint?.Motor_LinearVelocity ?? default;
		set => _joint?.Motor_LinearVelocity = value;
	}

	/// <summary>
	/// The desired relative angular velocity in radians per second.
	/// </summary>
	public Vector3 AngularVelocity
	{
		get => _joint?.Motor_AngularVelocity ?? default;
		set => _joint?.Motor_AngularVelocity = value;
	}

	/// <summary>
	/// The joint maximum force.
	/// </summary>
	public float MaxVelocityForce
	{
		get => _joint?.Motor_MaxVelocityForce ?? 0;
		set => _joint?.Motor_MaxVelocityForce = value;
	}

	/// <summary>
	/// The joint maximum torque.
	/// </summary>
	public float MaxVelocityTorque
	{
		get => _joint?.Motor_MaxVelocityTorque ?? 0;
		set => _joint?.Motor_MaxVelocityTorque = value;
	}

	/// <summary>
	/// The spring linear hertz stiffness and damping ratio.
	/// </summary>
	public PhysicsSpring LinearSpring
	{
		get
		{
			if ( _joint is null ) return default;
			return new PhysicsSpring
			{
				Frequency = _joint.Motor_LinearHertz,
				Damping = _joint.Motor_LinearDampingRatio,
				Maximum = _joint.Motor_MaxSpringForce
			};
		}
		set
		{
			if ( _joint is null ) return;
			_joint.Motor_LinearHertz = value.Frequency;
			_joint.Motor_LinearDampingRatio = value.Damping;
			_joint.Motor_MaxSpringForce = value.Maximum;
		}
	}

	/// <summary>
	/// The spring angular hertz stiffness and damping ratio.
	/// </summary>
	public PhysicsSpring AngularSpring
	{
		get
		{
			if ( _joint is null ) return default;
			return new PhysicsSpring
			{
				Frequency = _joint.Motor_AngularHertz,
				Damping = _joint.Motor_AngularDampingRatio,
				Maximum = _joint.Motor_MaxSpringTorque
			};
		}
		set
		{
			if ( _joint is null ) return;
			_joint.Motor_AngularHertz = value.Frequency;
			_joint.Motor_AngularDampingRatio = value.Damping;
			_joint.Motor_MaxSpringTorque = value.Maximum;
		}
	}
}
