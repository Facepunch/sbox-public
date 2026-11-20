using Sandbox.Movement;

public sealed class NoclipMoveMode : Sandbox.Movement.MoveMode
{
	/// <summary>
	/// Scales <see cref="RunSpeed"/> and <see cref="WalkSpeed"/>. Read on the owner, who moves their own player.
	/// </summary>
	[Title( "Noclip Speed" ), Group( "Gameplay" ), ServerSettings.Section( "Players" ), Order( 7 ), Range( 0.1f, 10f ), Step( 0.1f )]
	[ConVar( "sandbox.noclip_speed", ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting, Help = "Multiplier on noclip flying speed." )]
	public static float SpeedScale { get; set; } = 1.0f;

	/// <summary>
	/// If true, the player will still collide with the world and other players. This probably
	/// means that the noclip mode is named wrong. But it's cool. It just becomes a fly around mode.
	/// </summary>
	[Property]
	public bool EnableCollision { get; set; }

	[Property]
	public float RunSpeed { get; set; } = 600;

	[Property]
	public float WalkSpeed { get; set; } = 200;

	protected override void OnUpdateAnimatorState( SkinnedModelRenderer renderer )
	{
		// Seated players keep the sitting animation
		if ( GetComponentInParent<ISitTarget>( false ) is not null )
			return;

		renderer.Set( "b_noclip", true );
		renderer.Set( "duck", 0f );
	}

	public override int Score( PlayerController controller )
	{
		return 1000;
	}

	public override void UpdateRigidBody( Rigidbody body )
	{
		body.Gravity = false;
		// AddVelocity handles braking; damping would also reduce the requested speed.
		body.LinearDamping = 0f;
		body.AngularDamping = 1f;

		body.Tags.Set( "noclip", !EnableCollision );
	}

	public override void AddVelocity()
	{
		var body = Controller.Body;
		var target = Controller.WishVelocity;
		const float responseTime = 0.075f;
		var fraction = 1f - MathF.Exp( -Time.Delta / responseTime );
		var velocity = Vector3.Lerp( body.Velocity, target, fraction );

		// Finish braking completely instead of retaining a tiny residual drift.
		body.Velocity = (velocity - target).IsNearlyZero( 0.01f ) ? target : velocity;
	}

	public override void OnModeBegin()
	{
		Controller.IsClimbing = true;
		Controller.Body.Gravity = false;

		if ( !IsProxy )
			Sandbox.Services.Stats.Increment( "move.noclip.use", 1 );
	}

	public override void OnModeEnd( MoveMode next )
	{
		Controller.IsClimbing = false;
		Controller.Body.Tags.Set( "noclip", false );
	}

	public override Transform CalculateEyeTransform()
	{
		var transform = base.CalculateEyeTransform();

		// Undo the camera lowering that IsDucking causes
		if ( Controller.IsDucking )
			transform.Position += Vector3.Up * (Controller.BodyHeight - Controller.DuckedHeight);

		return transform;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		// don't normalize, because analog input might want to go slow
		input = input.ClampLength( 1 );

		var direction = eyes * input;

		// Run if we're holding down alt move button
		bool run = Input.Down( Controller.AltMoveButton );

		// if Run is default, flip that logic
		if ( Controller.RunByDefault ) run = !run;

		// if we're running, use run speed, if not use walk speed
		var velocity = run ? RunSpeed * 2.0f : RunSpeed;

		// Duck and the walk modifier both slow movement in any direction.
		if ( Input.Down( "walk" ) || Input.Down( "duck" ) ) velocity = WalkSpeed;

		velocity *= SpeedScale;

		if ( direction.IsNearlyZero( 0.1f ) )
		{
			direction = 0;
		}

		// if we're hold down jump move upwards
		if ( Input.Down( "jump" ) ) direction += Vector3.Up;

		return direction * velocity;
	}

}
