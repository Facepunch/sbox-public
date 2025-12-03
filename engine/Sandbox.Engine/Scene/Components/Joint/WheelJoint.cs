namespace Sandbox;

/// <inheritdoc cref="Physics.WheelJoint"/>
[Expose]
[Title( "Wheel Joint" )]
[Category( "Physics" )]
[Icon( "tire_repair" )]
[EditorHandle( "materials/gizmo/tracked_object.png" )]
public sealed class WheelJoint : Joint
{
	/// <inheritdoc cref="Physics.WheelJoint.EnableSuspensionLimit"/>
	[Property, ToggleGroup( "EnableSuspensionLimit", Label = "Suspension Limit" )]
	public bool EnableSuspensionLimit
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.EnableSuspensionLimit = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SuspensionLimits"/>
	[Property, Group( "EnableSuspensionLimit" ), Title( "Translation Limits" ), Range( -25, 25 )]
	public Vector2 SuspensionLimits
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.SuspensionLimits = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.EnableSpinMotor"/>
	[Property, ToggleGroup( "EnableSpinMotor", Label = "Motor" )]
	public bool EnableSpinMotor
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.EnableSpinMotor = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.MaxSpinTorque"/>
	[Property, Group( "EnableSpinMotor" ), Title( "Max Torque" )]
	public float MaxSpinTorque
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.MaxSpinTorque = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SpinMotorSpeed"/>
	[Property, Group( "EnableSpinMotor" ), Title( "Speed" )]
	public float SpinMotorSpeed
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.SpinMotorSpeed = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.EnableSuspension"/>
	[Property, ToggleGroup( "EnableSuspension", Label = "Suspension" )]
	public bool EnableSuspension
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.EnableSuspension = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SuspensionHertz"/>
	[Property, Group( "EnableSuspension" ), Title( "Hertz" ), Range( 0, 30 )]
	public float SuspensionHertz
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.SuspensionHertz = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SuspensionDampingRatio"/>
	[Property, Group( "EnableSuspension" ), Title( "Damping" ), Range( 0, 2 )]
	public float SuspensionDampingRatio
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.SuspensionDampingRatio = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.EnableSteering"/>
	[Property, ToggleGroup( "EnableSteering", Label = "Steering" )]
	public bool EnableSteering
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.EnableSteering = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SteeringHertz"/>
	[Property, Group( "EnableSteering" ), Title( "Hertz" ), Range( 0, 30 )]
	public float SteeringHertz
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.SteeringHertz = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SteeringDampingRatio"/>
	[Property, Group( "EnableSteering" ), Title( "Damping" ), Range( 0, 2 )]
	public float SteeringDampingRatio
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.SteeringDampingRatio = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.TargetSteeringAngle"/>
	[Property, Group( "EnableSteering" ), Title( "Target Angle" ), Range( -180, 180 )]
	public float TargetSteeringAngle
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.TargetSteeringAngle = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.MaxSteeringTorque"/>
	[Property, Group( "EnableSteering" ), Title( "Max Torque" )]
	public float MaxSteeringTorque
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.MaxSteeringTorque = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.EnableSteeringLimit"/>
	[Property, ToggleGroup( "EnableSteeringLimit", Label = "Steering Limit" ), ShowIf( nameof( EnableSteering ), true )]
	public bool EnableSteeringLimit
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.EnableSteeringLimit = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SteeringLimits"/>
	[Property, Group( "EnableSteeringLimit" ), Title( "Angle Limits" ), ShowIf( nameof( EnableSteering ), true ), Range( -180, 180 )]
	public Vector2 SteeringLimits
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.SteeringLimits = field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SpinSpeed"/>
	public float SpinSpeed => _joint.IsValid() ? _joint.SpinSpeed : default;

	/// <inheritdoc cref="Physics.WheelJoint.SpinTorque"/>
	public float SpinTorque => _joint.IsValid() ? _joint.SpinTorque : default;

	/// <inheritdoc cref="Physics.WheelJoint.SteeringAngle"/>
	public float SteeringAngle => _joint.IsValid() ? _joint.SteeringAngle : default;

	/// <inheritdoc cref="Physics.WheelJoint.SteeringTorque"/>
	public float SteeringTorque => _joint.IsValid() ? _joint.SteeringTorque : default;

	private Physics.WheelJoint _joint;

	protected override PhysicsJoint CreateJoint( PhysicsPoint point1, PhysicsPoint point2 )
	{
		var localFrame1 = LocalFrame1;
		var localFrame2 = LocalFrame2;

		if ( Attachment == AttachmentMode.Auto )
		{
			localFrame1 = point1.LocalTransform;
			localFrame2 = point2.LocalTransform;
		}

		if ( !Scene.IsEditor )
		{
			LocalFrame1 = localFrame1;
			LocalFrame2 = localFrame2;

			Attachment = AttachmentMode.LocalFrames;
		}

		point1.LocalTransform = localFrame1;
		point2.LocalTransform = localFrame2;

		_joint = PhysicsJoint.CreateWheel( point1, point2 );

		UpdateProperties();

		return _joint;
	}

	private void UpdateProperties()
	{
		if ( !_joint.IsValid() ) return;

		_joint.EnableSpinMotor = EnableSpinMotor;
		_joint.MaxSpinTorque = MaxSpinTorque;
		_joint.SpinMotorSpeed = SpinMotorSpeed;
		_joint.EnableSuspension = EnableSuspension;
		_joint.SuspensionHertz = SuspensionHertz;
		_joint.SuspensionDampingRatio = SuspensionDampingRatio;
		_joint.EnableSteering = EnableSteering;
		_joint.SteeringHertz = SteeringHertz;
		_joint.SteeringDampingRatio = SteeringDampingRatio;
		_joint.TargetSteeringAngle = TargetSteeringAngle;
		_joint.MaxSteeringTorque = MaxSteeringTorque;
		_joint.EnableSuspensionLimit = EnableSuspensionLimit;
		_joint.EnableSteeringLimit = EnableSteeringLimit;
		_joint.SuspensionLimits = SuspensionLimits;
		_joint.SteeringLimits = SteeringLimits;

		_joint.WakeBodies();
	}
}
