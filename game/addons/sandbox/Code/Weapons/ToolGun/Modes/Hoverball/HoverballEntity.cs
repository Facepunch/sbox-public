[Alias( "hoverball" )]
[Title( "#tool.entity.hoverballentity" )]
public sealed class HoverballEntity : Component, IPlayerControllable
{
	/// <summary>
	/// The world Z position the hoverball is trying to maintain.
	/// </summary>
	[Property, Sync]
	public float TargetZ { get; private set; }

	/// <summary>
	/// How fast the target height changes when inputs are held.
	/// </summary>
	[Property, Sync, ClientEditable, Range( 0, 20 ), Title( "#tool.setting.speed" ), Description( "#tool.setting.hoverballentity.speed.description" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Speed { get; set; } = 1f;

	/// <summary>
	/// Horizontal air resistance applied while hovering. Also increases vertical damping.
	/// </summary>
	[Property, Sync, ClientEditable, Range( 0, 10 ), Title( "#tool.setting.air_resistance" ), Description( "#tool.setting.hoverballentity.airresistance.description" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float AirResistance { get; set; } = 0f;

	/// <summary>
	/// Is the hoverball on?
	/// </summary>
	[Property, Sync, ClientEditable, Title( "#tool.setting.is_enabled" ), Description( "#tool.setting.hoverballentity.isenabled.description" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool IsEnabled { get; private set; } = true;

	/// <summary>
	/// While held, raises the hover target.
	/// </summary>
	[Property, ClientEditable, Title( "#tool.setting.up" ), Description( "#tool.setting.hoverballentity.up.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Up { get; set; }

	/// <summary>
	/// While held, lowers the hover target.
	/// </summary>
	[Property, ClientEditable, Title( "#tool.setting.down" ), Description( "#tool.setting.hoverballentity.down.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Down { get; set; }

	/// <summary>
	/// Toggles the hoverball on/off
	/// </summary>
	[Property, ClientEditable, Title( "#tool.setting.toggle" ), Description( "#tool.setting.hoverballentity.toggle.description" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Toggle { get; set; }

	[Property]
	public GameObject OnEffect { get; set; }

	[Property, ClientEditable, Metadata( SoundDefinition.Hoverball ), Title( "#tool.setting.enable_sound" ), Group( "#tool.setting.group.sound" ), Order( 2 )]
	public SoundDefinition EnableSound { get; set; }

	[Property, ClientEditable, Metadata( SoundDefinition.Hoverball ), Title( "#tool.setting.disable_sound" ), Group( "#tool.setting.group.sound" ), Order( 2 )]
	public SoundDefinition DisableSound { get; set; }

	protected override void OnStart()
	{
		if ( !Networking.IsHost ) return;

		TargetZ = WorldPosition.z;

		var rb = GetComponent<Rigidbody>();
		if ( rb.IsValid() )
			rb.Gravity = !IsEnabled;
	}

	protected override void OnUpdate()
	{
		if ( OnEffect.IsValid() )
			OnEffect.Enabled = IsEnabled;
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		var rb = GetComponent<Rigidbody>();
		if ( !rb.IsValid() ) return;

		if ( !IsEnabled ) return;

		var pos = WorldPosition;
		var vel = rb.Velocity;
		var distance = TargetZ - pos.z;

		// Drive Z velocity toward a target proportional to distance
		var targetVelZ = Math.Clamp( distance * 20f, -400f, 400f );
		var newVelZ = vel.z + (targetVelZ - vel.z) * Math.Min( Time.Delta * 15f * (AirResistance + 1f), 1f );

		var newVel = vel.WithZ( newVelZ );

		// Horizontal air resistance
		if ( AirResistance > 0f )
		{
			var drag = Math.Min( AirResistance * Time.Delta * 5f, 1f );
			newVel = newVel.WithX( vel.x * (1f - drag) ).WithY( vel.y * (1f - drag) );
		}

		rb.Velocity = newVel;
	}

	[SignalInput( Id = nameof( Up ), Default = true )]
	public void UpSignal( float amount ) => MoveTarget( amount );

	[SignalInput( Id = nameof( Down ) )]
	public void DownSignal( float amount ) => MoveTarget( -amount );

	[SignalInput( Id = nameof( Toggle ) )]
	public void ToggleSignal() => DoToggle();

	public void OnControl()
	{
		if ( Toggle.Pressed() ) DoToggle();
		MoveTarget( Up.GetAnalog() - Down.GetAnalog() );
	}

	private void MoveTarget( float amount )
	{
		if ( !IsEnabled ) return;
		TargetZ += amount.Clamp( -1f, 1f ) * Time.Delta * Time.Delta * 5000f * Speed;
	}

	private void DoToggle()
	{
		IsEnabled = !IsEnabled;

		if ( IsEnabled )
			EnableSound?.Play( WorldPosition );
		else
			DisableSound?.Play( WorldPosition );

		var rb = GetComponent<Rigidbody>();
		if ( !rb.IsValid() ) return;

		if ( IsEnabled )
		{
			TargetZ = WorldPosition.z;
			rb.Gravity = false;
		}
		else
		{
			rb.Gravity = true;
		}
	}
}
