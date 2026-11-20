[Alias( "thruster" )]
[Title( "#tool.entity.thrusterentity" )]
public sealed class ThrusterEntity : Component, IPlayerControllable
{
	[Property, Range( 0, 1 )]
	public GameObject OnEffect { get; set; }

	[Property, ClientEditable, Range( 0, 1 ), Title( "#tool.setting.power" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Power { get; set; } = 0.5f;

	[Property, ClientEditable, Title( "#tool.setting.invert" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool Invert { get; set; } = false;

	[Property, ClientEditable, Title( "#tool.setting.hide_effects" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool HideEffects { get; set; } = false;

	/// <summary>
	/// While this input is active we'll apply thrust
	/// </summary>
	[Property, ClientEditable, Title( "#tool.setting.activate" ), Description( "#tool.setting.thrusterentity.activate.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Activate { get; set; }

	/// <summary>
	/// While this input is active we'll apply thrust in the opposite direction
	/// </summary>
	[Property, ClientEditable, Title( "#tool.setting.reverse" ), Description( "#tool.setting.thrusterentity.reverse.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Reverse { get; set; }

	/// <summary>
	/// The fallback sound for all thrusters.
	/// </summary>
	private static SoundDefinition _defaultSound => ResourceLibrary.Get<SoundDefinition>( "entities/thruster/sounds/thruster_basic.sndef" );

	/// <summary>
	/// Looping sound played while the thruster is active.
	/// </summary>
	[Property, ClientEditable, Metadata( SoundDefinition.Thruster ), Group( "#tool.setting.group.sound" ), Title( "#tool.setting.thruster_sound" ), Description( "#tool.setting.thrusterentity.thrustersound.description" ), Order( 2 )]
	public SoundDefinition ThrusterSound { get; set; }

	/// <summary>
	/// Current thrust output, -1 to 1. Updated every control frame.
	/// </summary>
	public float ThrustAmount { get; private set; }

	private SoundHandle _thrusterSound;

	protected override void OnEnabled()
	{
		base.OnEnabled();

		OnEffect?.Enabled = false;
	}

	protected override void OnDisabled()
	{
		_state = false;
		StopThrusterSound();
	}

	protected override void OnUpdate()
	{
		if ( _state )
		{
			if ( !_thrusterSound.IsValid() )
				StartThrusterSound();
		}
		else
		{
			if ( _thrusterSound.IsValid() )
				StopThrusterSound();
		}
	}

	[SignalInput( Id = nameof( Activate ), Default = true )]
	public void ActivateSignal( float amount ) => ApplyThrust( amount );

	[SignalInput( Id = nameof( Reverse ) )]
	public void ReverseSignal( float amount ) => ApplyThrust( -amount );

	public void OnControl()
	{
		var thrust = (Activate.GetAnalog() - Reverse.GetAnalog()).Clamp( -1f, 1f );
		ApplyThrust( thrust );
	}

	private void ApplyThrust( float thrust )
	{
		AddThrust( thrust );
		UpdateThrustState( thrust );
	}

	void AddThrust( float amount )
	{
		if ( amount.AlmostEqual( 0.0f ) ) return;

		var body = GetComponent<Rigidbody>();
		if ( body == null ) return;

		body.ApplyImpulse( WorldRotation.Up * -10000 * amount * Power * (Invert ? -1f : 1f) );
	}

	bool _state;

	[Rpc.Broadcast]
	public void SetActiveState( bool state )
	{
		if ( _state == state ) return;

		_state = state;

		if ( !HideEffects )
			OnEffect?.Enabled = state;
	}

	private void StartThrusterSound()
	{
		if ( _thrusterSound.IsValid() )
			StopThrusterSound();

		var sound = ThrusterSound ?? _defaultSound;
		if ( sound is null ) return;

		_thrusterSound = sound.Play( WorldPosition, GameObject );
	}

	private void StopThrusterSound()
	{
		if ( _thrusterSound.IsValid() )
		{
			_thrusterSound.Stop( 0.5f );
			_thrusterSound = default;
		}
	}

	private void UpdateThrustState( float analog )
	{
		ThrustAmount = analog;

		var active = MathF.Abs( analog ) > 0.1f;
		if ( active != _state )
		{
			if ( active )
				Sandbox.Services.Stats.Increment( "tool.thruster.activate", 1 );

			SetActiveState( active );
		}
	}
}
