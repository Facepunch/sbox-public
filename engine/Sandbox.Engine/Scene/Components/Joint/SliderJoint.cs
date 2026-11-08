namespace Sandbox;

/// <summary>
/// Restrict an object to one axis, relative to another object. Like a drawer opening.
/// </summary>
[Expose]
[Title( "Slider Joint" )]
[Category( "Physics" )]
[Icon( "open_in_full" )]
[EditorHandle( "materials/gizmo/slider.png" )]
public sealed class SliderJoint : Joint
{
	public enum MotorMode
	{
		Disabled,
		TargetPosition,
		TargetVelocity
	}

	/// <summary>Explicit limit state. Null retains automatic limits from the length range.</summary>
	[Property, Group( "Limit" )]
	public bool? LimitEnabled
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyLimits();
		}
	}

	/// <summary>
	/// Maximum length it should be allowed to go
	/// </summary>
	[Property]
	public float MaxLength
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyLimits();
		}
	}

	/// <summary>
	/// Minimum length it should be allowed to go
	/// </summary>
	[Property]
	public float MinLength
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyLimits();
		}
	}

	/// <summary>
	/// Slider friction
	/// </summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.Disabled )]
	public float Friction
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyMotor();
		}
	}

	/// <summary>Chooses friction, a position spring, or a velocity motor.</summary>
	[Property, Group( "Motor" )]
	public MotorMode Motor
	{
		get;
		set
		{
			if ( !Enum.IsDefined( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			if ( field == value ) return;
			field = value;
			ApplyMotor();
		}
	}

	/// <summary>Target offset along the slider axis.</summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetPosition )]
	public float TargetPosition
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyMotor();
		}
	}

	/// <summary>Position spring frequency in hertz.</summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetPosition )]
	public float Frequency
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyMotor();
		}
	} = 1;

	/// <summary>Position spring damping ratio.</summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetPosition )]
	public float DampingRatio
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyMotor();
		}
	} = 1;

	/// <summary>Target speed along the slider axis, in units per second.</summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetVelocity )]
	public float TargetVelocity
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyMotor();
		}
	}

	/// <summary>Maximum force the velocity motor may apply.</summary>
	[Property, Group( "Motor" ), ShowIf( nameof( Motor ), MotorMode.TargetVelocity )]
	public float MaxForce
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyMotor();
		}
	}

	Physics.SliderJoint _joint;

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

		_joint = PhysicsJoint.CreateSlider( point1, point2, MinLength, MaxLength );
		ApplyLimits();
		ApplyMotor();

		_joint.WakeBodies();

		return _joint;
	}

	void ApplyLimits()
	{
		if ( !_joint.IsValid() ) return;
		_joint.ConfigureLimits( new Vector2( MinLength, MaxLength ), LimitEnabled );
		_joint.WakeBodies();
	}

	void ApplyMotor()
	{
		if ( !_joint.IsValid() ) return;
		switch ( Motor )
		{
			case MotorMode.Disabled:
				_joint.Friction = Friction;
				break;
			case MotorMode.TargetPosition:
				_joint.SetTargetPosition( TargetPosition, Frequency, DampingRatio );
				break;
			case MotorMode.TargetVelocity:
				_joint.SetLinearMotor( TargetVelocity, MaxForce );
				break;
		}
		_joint.WakeBodies();
	}
}
