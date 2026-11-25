
/// <summary>
/// Drives a <see cref="HingeJoint"/> at a continuous angular velocity — a spinning axle,
/// turntable, or wheel that isn't a ground-contact wheel.
/// </summary>
[Title( "#tool.entity.motorentity" )]
public sealed class MotorEntity : Component, IPlayerControllable
{
	[Property, Range( 0, 1 ), ClientEditable, Title( "#tool.setting.speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Speed { get; set; } = 0.5f;

	[Property, Range( 0, 1 ), ClientEditable, Title( "#tool.setting.torque" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Torque { get; set; } = 0.5f;

	[Property, ClientEditable, Title( "#tool.setting.reversed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool Reversed { get; set; }

	/// <summary>
	/// Spins on its own as soon as it's created, without needing a driver or a wire signal.
	/// </summary>
	[Property, ClientEditable, Title( "#tool.setting.start_active" ), Description( "#tool.setting.motorentity.startactive.description" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool StartActive { get; set; } = true;

	[Property, ClientEditable, ToggleGroup( "Limited", Label = "#tool.setting.limited" ), Title( "#tool.setting.limited" ), Order( 2 )]
	public bool Limited { get; set; }

	[Property, ClientEditable, ToggleGroup( "Limited", Label = "#tool.setting.limited" ), Range( -180, 180 ), Title( "#tool.setting.min_angle" ), Order( 2 )]
	public float MinAngle { get; set; } = -90f;

	[Property, ClientEditable, ToggleGroup( "Limited", Label = "#tool.setting.limited" ), Range( -180, 180 ), Title( "#tool.setting.max_angle" ), Order( 2 )]
	public float MaxAngle { get; set; } = 90f;

	/// <summary>
	/// While held, spins forward regardless of <see cref="Reversed"/> or <see cref="StartActive"/>.
	/// </summary>
	[Property, ClientEditable, Title( "#tool.setting.forward" ), Description( "#tool.setting.motorentity.forward.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Forward { get; set; }

	/// <summary>
	/// While held, spins backward regardless of <see cref="Reversed"/> or <see cref="StartActive"/>.
	/// </summary>
	[Property, ClientEditable, Title( "#tool.setting.reverse" ), Description( "#tool.setting.motorentity.reverse.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Reverse { get; set; }

	[Property, ClientEditable, Title( "#tool.setting.toggle" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Toggle { get; set; }

	[Property]
	public HingeJoint Joint { get; set; }

	/// <summary>
	/// Emits the joint's current angle, normalized so a full turn is 1.0 — wire it into a
	/// gauge, or into another motor's Forward/Reverse to sync two axles.
	/// </summary>
	[SignalOutput, Title( "#tool.signal.angleoutput" ), Description( "#tool.signal.angleoutput.description" )]
	public SignalOutput AngleOutput { get; set; } = new();

	const float MaxDegreesPerSecond = 720f;
	const float MaxTorqueScale = 200000f;

	bool _active;
	float _lastEmitted = float.NaN;

	protected override void OnEnabled()
	{
		base.OnEnabled();
		_active = StartActive;
	}

	[SignalInput( Id = nameof( Forward ), Default = true )]
	public void ForwardSignal( float amount ) => Drive( amount, 0f );

	[SignalInput( Id = nameof( Reverse ) )]
	public void ReverseSignal( float amount ) => Drive( 0f, amount );

	[SignalInput( Id = nameof( Toggle ) )]
	public void ToggleSignal() => _active = !_active;

	public void OnControl()
	{
		if ( Toggle.Pressed() ) _active = !_active;

		Drive( Forward.GetAnalog(), Reverse.GetAnalog() );
	}

	void Drive( float forward, float reverse )
	{
		if ( !Networking.IsHost ) return;
		if ( !Joint.IsValid() ) return;

		Joint.MinAngle = Limited ? MinAngle : -100000f;
		Joint.MaxAngle = Limited ? MaxAngle : 100000f;

		var held = forward > 0.5f || reverse > 0.5f;

		if ( !held && !_active )
		{
			Joint.Motor = HingeJoint.MotorMode.Disabled;
			return;
		}

		var direction = held ? (forward > reverse ? 1f : -1f) : (Reversed ? -1f : 1f);

		Joint.Motor = HingeJoint.MotorMode.TargetVelocity;
		Joint.TargetVelocity = direction * Speed * MaxDegreesPerSecond;
		Joint.MaxTorque = Torque * MaxTorqueScale;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( !Joint.IsValid() ) return;

		var turns = Joint.Angle / 360f;
		if ( float.IsNaN( _lastEmitted ) || MathF.Abs( turns - _lastEmitted ) > 0.001f )
		{
			_lastEmitted = turns;
			AngleOutput.Emit( this, turns );
		}
	}
}
