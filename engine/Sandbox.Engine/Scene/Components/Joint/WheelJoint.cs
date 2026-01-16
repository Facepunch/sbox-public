namespace Sandbox;

/// <summary>
/// The wheel joint can be used to simulate wheels on vehicles.
/// The wheel joint restricts body B to move along a local axis in body A. Body B is free to rotate.
/// Supports a linear spring, linear limits, and a rotational motor.
/// The assumption is that you will create this joint on the wheel body.This will enable suspension to be in the correct direction.
/// </summary>
[Expose]
[Title( "Wheel Joint" )]
[Category( "Physics" )]
[Icon( "tire_repair" )]
[EditorHandle( "materials/gizmo/tracked_object.png" )]
public sealed class WheelJoint : Joint
{
	/// <inheritdoc cref="Physics.WheelJoint.EnableSuspensionLimit"/>
	[Property, ToggleGroup( "EnableSuspensionLimit", Label = "Suspension Limit" ), ShowIf( nameof( EnableSuspension ), true ), ClientEditable]
	public bool EnableSuspensionLimit
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				_joint.EnableSuspensionLimit = !EnableSuspension || field;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SuspensionLimits"/>
	[Property, Group( "EnableSuspensionLimit" ), Title( "Translation Limits" ), Range( -25, 25 ), ShowIf( nameof( EnableSuspension ), true ), ClientEditable]
	public Vector2 SuspensionLimits
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				var v = EnableSuspension ? field : 0.0f;
				v = new Vector2( Math.Min( v.x, v.y ), Math.Max( v.x, v.y ) );

				_joint.SuspensionLimits = v;
				_joint.WakeBodies();
			}
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.EnableSpinMotor"/>
	[Property, ToggleGroup( "EnableSpinMotor", Label = "Motor" ), ClientEditable]
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
	[Property, Group( "EnableSpinMotor" ), Title( "Max Torque" ), ClientEditable]
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
	[Property, Group( "EnableSpinMotor" ), Title( "Speed" ), ClientEditable]
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
	[Property, ToggleGroup( "EnableSuspension", Label = "Suspension" ), ClientEditable]
	public bool EnableSuspension
	{
		get;
		set
		{
			if ( value == field ) return;

			field = value;

			if ( _joint.IsValid() )
			{
				UpdateSuspension();

				_joint.WakeBodies();
			}
		}
	}

	void UpdateSuspension()
	{
		_joint.EnableSuspension = EnableSuspension;

		if ( EnableSuspension )
		{
			// Suspension on, use user limits.
			_joint.EnableSuspensionLimit = EnableSuspensionLimit;

			var v = SuspensionLimits;
			v = new Vector2( Math.Min( v.x, v.y ), Math.Max( v.x, v.y ) );

			_joint.SuspensionLimits = v;
		}
		else
		{
			// Suspension off, limit it to zero.
			_joint.EnableSuspensionLimit = true;
			_joint.SuspensionLimits = 0.0f;
		}
	}

	/// <inheritdoc cref="Physics.WheelJoint.SuspensionHertz"/>
	[Property, Group( "EnableSuspension" ), Title( "Hertz" ), Range( 0, 30 ), Step( 1 ), ClientEditable]
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
	} = 10.0f;

	/// <inheritdoc cref="Physics.WheelJoint.SuspensionDampingRatio"/>
	[Property, Group( "EnableSuspension" ), Title( "Damping" ), Range( 0, 2 ), Step( 0.1f ), ClientEditable]
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
	} = 1.0f;

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
	} = 10.0f;

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
	} = 1.0f;

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
			localFrame1 = global::Transform.Zero;
			localFrame1.Position = point1.LocalPosition;
			localFrame1.Rotation = point1.LocalRotation * new Angles( 90, 0, 0 ) * new Angles( 0, 90, 0 ); // face the right way, steer the right way

			localFrame2 = global::Transform.Zero;
			localFrame2.Position = point2.Body.Transform.PointToLocal( point1.Transform.Position );
			localFrame2.Rotation = point2.Body.Transform.RotationToLocal( point1.Transform.Rotation * new Angles( 90, 0, 0 ) * new Angles( 0, 90, 0 ) ); // face the right way, steer the right way
		}

		if ( !Scene.IsEditor )
		{
			LocalFrame1 = localFrame1;
			LocalFrame2 = localFrame2;

			Attachment = AttachmentMode.LocalFrames;
		}

		point1.LocalTransform = localFrame1;
		point2.LocalTransform = localFrame2;

		_joint = PhysicsJoint.CreateWheel( point2, point1 );

		UpdateProperties();

		return _joint;
	}

	private void UpdateProperties()
	{
		if ( !_joint.IsValid() ) return;

		_joint.EnableSpinMotor = EnableSpinMotor;
		_joint.MaxSpinTorque = MaxSpinTorque;
		_joint.SpinMotorSpeed = SpinMotorSpeed;
		_joint.SuspensionHertz = SuspensionHertz;
		_joint.SuspensionDampingRatio = SuspensionDampingRatio;
		_joint.EnableSteering = EnableSteering;
		_joint.SteeringHertz = SteeringHertz;
		_joint.SteeringDampingRatio = SteeringDampingRatio;
		_joint.TargetSteeringAngle = TargetSteeringAngle;
		_joint.MaxSteeringTorque = MaxSteeringTorque;
		_joint.EnableSteeringLimit = EnableSteeringLimit;
		_joint.SteeringLimits = SteeringLimits;

		UpdateSuspension();

		_joint.WakeBodies();
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		Gizmo.Draw.IgnoreDepth = true;

		using var _ = Gizmo.Scope();

		Gizmo.Transform = Gizmo.Transform.WithScale( 1 );


		// axis
		{
			using var __ = Gizmo.Scope();
			Gizmo.Transform = Gizmo.Transform with { Rotation = Gizmo.Transform.Rotation * new Angles( 0, 0, RealTime.Now * 45 ) };
			Gizmo.Draw.Color = Gizmo.Colors.Green;
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.LineCapsule( new Capsule( Vector3.Forward * -5.0f, Vector3.Forward * 5.0f, 1 ) );
		}

		if ( EnableSuspension && SuspensionLimits != default )
		{
			var v = SuspensionLimits;
			v = new Vector2( Math.Min( v.x, v.y ), Math.Max( v.x, v.y ) );

			using var __ = Gizmo.Scope();

			var top = Vector3.Left * v.y;
			var bottom = Vector3.Left * v.x;

			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Color = Gizmo.Colors.Forward.WithAlpha( 0.3f );

			Gizmo.Draw.LineCapsule( new Capsule( top, bottom, 1 ) );
		}
	}
}
