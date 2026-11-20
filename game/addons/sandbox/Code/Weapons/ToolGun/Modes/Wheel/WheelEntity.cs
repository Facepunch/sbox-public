[Title( "#tool.entity.wheelentity" )]
public sealed class WheelEntity : Component, IPlayerControllable
{
	[Property, Range( 0, 1 ), ClientEditable, Title( "#tool.setting.speed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Speed { get; set; } = 0.5f;

	[Property, Range( 0, 1 ), ClientEditable, Title( "#tool.setting.power" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public float Power { get; set; } = 0.5f;

	[Property, Range( 0, 1 ), ClientEditable, Title( "#tool.setting.reversed" ), Group( "#tool.setting.group.behavior" ), Order( 0 )]
	public bool Reversed { get; set; }

	[Property, ClientEditable, Title( "#tool.setting.forward" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Forward { get; set; }

	[Property, ClientEditable, Title( "#tool.setting.reverse" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Reverse { get; set; }

	[Property, ClientEditable, Title( "#tool.setting.brake" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput Brake { get; set; }

	[Property, ClientEditable, Title( "#tool.setting.turn_left" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput TurnLeft { get; set; }

	[Property, ClientEditable, Title( "#tool.setting.turn_right" ), Group( "#tool.setting.group.inputs" ), Order( 1 )]
	public ClientInput TurnRight { get; set; }

	private Vector3 _localAxle;
	private Vector3 _localUp;

	protected override void OnEnabled()
	{
		base.OnEnabled();

		var joint = GetComponentInChildren<WheelJoint>();
		if ( joint.IsValid() && joint.Body.IsValid() )
		{
			var bodyRotInv = joint.Body.WorldRotation.Inverse;
			_localAxle = bodyRotInv * WorldRotation.Right;
			_localUp = bodyRotInv * WorldRotation.Up;
		}
	}

	protected override void OnUpdate()
	{
		if ( SpawnMenuHost.GetActiveMode() is not ContextMenuHost ) return;

		var renderers = GameObject.GetComponentsInChildren<Renderer>();
		var outlines = Game.ActiveScene.GetAllComponents<HighlightOutline>();
		if ( !outlines.Any( outline => outline.Targets?.Any( target => renderers.Contains( target ) ) == true ) ) return;

		var joint = GetComponentInChildren<WheelJoint>();
		if ( !joint.IsValid() || !joint.Body.IsValid() || _localAxle == Vector3.Zero ) return;

		var spinAxis = joint.Body.WorldRotation * _localAxle;
		var stableUp = joint.Body.WorldRotation * _localUp;

		var renderer = GameObject.GetComponentInChildren<ModelRenderer>();
		var overlayRadius = 0f;
		if ( renderer.IsValid() )
		{
			var size = renderer.Model.Bounds.Size;
			overlayRadius = MathF.Max( size.x, size.z ) * 0.01f * WorldScale.x;
		}

		WheelOverlay.DrawDirection( WorldPosition, spinAxis, stableUp, overlayRadius, Reversed );
	}

	private void ApplyInput( float forward, float reverse, float brake, float turnLeft, float turnRight )
	{
		if ( !Networking.IsHost ) return;

		var joint = GetComponentInChildren<WheelJoint>();
		if ( !joint.IsValid() ) return;

		var speed = (forward - reverse).Clamp( -1, 1 );

		if ( brake > 0.1f )
		{
			joint.EnableSpinMotor = true;
			joint.SpinMotorSpeed = 0;
			joint.MaxSpinTorque = 500000 * Power;
		}
		else if ( speed.AlmostEqual( 0f ) )
		{
			joint.EnableSpinMotor = false;
		}
		else
		{
			if ( Reversed ) speed = -speed;

			joint.EnableSpinMotor = true;
			joint.SpinMotorSpeed = -2000 * speed * Speed;
			joint.MaxSpinTorque = 200000 * Power;
		}

		var direction = (turnRight - turnLeft).Clamp( -1, 1 );
		if ( !direction.AlmostEqual( 0f ) )
		{
			joint.EnableSteering = true;
			joint.SteeringDampingRatio = 1f;
			joint.MaxSteeringTorque = 500000;
			joint.SteeringLimits = new Vector2( -45, 45 );
			joint.TargetSteeringAngle = 30 * direction;
		}
		else
		{
			joint.TargetSteeringAngle = 0;
		}
	}

	[SignalInput( Id = nameof( Forward ), Default = true )]
	public void ForwardSignal( float amount ) => ApplyDrive( amount );

	[SignalInput( Id = nameof( Reverse ) )]
	public void ReverseSignal( float amount ) => ApplyDrive( -amount );

	[SignalInput( Id = nameof( Brake ) )]
	public void BrakeSignal( bool active ) => ApplyInput( 0f, 0f, active ? 1f : 0f, 0f, 0f );

	[SignalInput( Id = nameof( TurnLeft ) )]
	public void TurnLeftSignal( float amount ) => ApplySteering( -amount );

	[SignalInput( Id = nameof( TurnRight ) )]
	public void TurnRightSignal( float amount ) => ApplySteering( amount );

	public void OnControl()
	{
		ApplyInput( Forward.GetAnalog(), Reverse.GetAnalog(), Brake.GetAnalog(), TurnLeft.GetAnalog(), TurnRight.GetAnalog() );
	}

	private void ApplyDrive( float speed ) => ApplyInput( speed, 0f, 0f, 0f, 0f );

	private void ApplySteering( float direction )
	{
		var joint = GetComponentInChildren<WheelJoint>();
		if ( !joint.IsValid() ) return;

		joint.EnableSteering = true;
		joint.SteeringDampingRatio = 1f;
		joint.MaxSteeringTorque = 500000;
		joint.SteeringLimits = new Vector2( -45, 45 );
		joint.TargetSteeringAngle = 30 * direction.Clamp( -1f, 1f );
	}
}
