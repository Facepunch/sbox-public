namespace Sandbox.Movement;

[Icon( "transfer_within_a_station" ), Group( "Movement" ), Title( "MoveMode - Walk" ), Alias( "Sandbox.PhysicsCharacterMode.PhysicsCharacterWalkMode" )]
public partial class MoveModeWalk : MoveMode
{
	[Property] public int Priority { get; set; } = 0;

	[Property] public float GroundAngle { get; set; } = 45.572996f; // acos( 0.7 ) in degrees
	[Property] public float StepUpHeight { get; set; } = 18.0f;
	[Property] public float StepDownHeight { get; set; } = 18.0f;

	/// <summary>Allow input-driven acceleration while airborne.</summary>
	[Property, Group( "Air Movement" )] public bool EnableAirStrafing { get; set; } = true;

	[Property, Group( "Air Movement" )] public float AirAcceleration { get; set; } = 10.0f;

	/// <summary>Wish-direction speed cap; turning can still build total speed.</summary>
	[Property, Group( "Air Movement" )] public float AirSpeedCap { get; set; } = 30.0f;

	/// <summary>Allow immediate repeat jumps and takeoff speeds above RunSpeed.</summary>
	[Property, Group( "Jumping" )] public bool EnableBunnyHopping { get; set; } = true;

	/// <summary>Grounded seconds required when bunny hopping is disabled. Early requests are discarded.</summary>
	[Property, Group( "Jumping" ), Range( 0, 1 )] public float BunnyHopRecoveryTime { get; set; } = 0.1f;

	/// <summary>Fraction of ground-relative horizontal speed retained by a walking jump.</summary>
	[Property, Group( "Jumping" ), Range( 0, 1 )] public float JumpMomentumRetention { get; set; } = 1;

	[Property, Group( "Ground Movement" )] public float GroundAcceleration { get; set; } = 10;

	/// <summary>Ground friction, scaled by BrakePower.</summary>
	[Property, Group( "Ground Movement" )] public float Friction { get; set; } = 4;

	/// <summary>Minimum speed used to calculate ground friction.</summary>
	[Property, Group( "Ground Movement" )] public float StopSpeed { get; set; } = 100;

	/// <summary>Per-axis velocity limit, not a total speed cap.</summary>
	[Property] public float MaxVelocity { get; set; } = 3500;

	protected override bool SmoothInput => false;

	internal float SurfaceFriction { get; private set; } = 1;
	bool _jumpRequested;
	bool _physicsStep;
	bool _nativeGravityStep;
	Vector3 _movementStart;
	Vector3 _movementVelocity;

	public override bool AllowGrounding => true;
	public override bool AllowFalling => true;

	public override int Score( PlayerController controller ) => Priority;

	public override void AddVelocity()
	{
		Controller.WishVelocity = Controller.WithoutVertical( Controller.WishVelocity );
		var body = Controller.Body;
		var wish = Controller.WishVelocity;
		var wishSpeed = wish.Length;
		var direction = wishSpeed > 0 ? wish / wishSpeed : Vector3.Zero;
		if ( !Controller.IsOnGround )
		{
			if ( EnableAirStrafing )
				body.Velocity = MovementMath.AirAccelerate( body.Velocity, direction, wishSpeed,
					MathF.Max( AirAcceleration, 0 ), MathF.Max( AirSpeedCap, 0 ), SurfaceFriction, Time.Delta );
			return;
		}

		// Idle moving surfaces use native friction.
		var useContactFriction = Controller.GroundUsesContactFriction;
		if ( useContactFriction && wish.IsNearZeroLength )
			return;

		var normal = Controller.GroundNormal;
		var up = Controller.UpDirection;
		var normalUp = normal.Dot( up );
		Vector3 GroundDirection( Vector3 value )
		{
			var horizontal = Controller.WithoutVertical( value );
			return (horizontal * normalUp - up * horizontal.Dot( normal )).Normal;
		}

		var velocity = body.Velocity - Controller.GroundVelocity;
		var normalVelocity = normal * velocity.Dot( normal );
		var tangentVelocity = velocity - normalVelocity;
		// Static slopes preserve horizontal speed; physical supports use tangent speed.
		velocity = useContactFriction ? tangentVelocity : Controller.WithoutVertical( velocity );

		SurfaceFriction = MovementMath.SurfaceFriction( MathF.Max( Controller.GroundFriction, 0 ) );
		velocity = MovementMath.Friction( velocity, MathF.Max( Friction * Controller.BrakePower, 0 ),
			MathF.Max( StopSpeed, 0 ), SurfaceFriction, Time.Delta );
		velocity = MovementMath.Accelerate( velocity, useContactFriction ? GroundDirection( wish ) : direction, wishSpeed,
			MathF.Max( GroundAcceleration, 0 ), SurfaceFriction, Time.Delta );
		if ( velocity.Length < 1 ) velocity = 0;

		if ( useContactFriction )
		{
			// Preserve native support impulses.
			velocity += normalVelocity;
			if ( SurfaceFriction > 0 && body.Gravity )
			{
				var gravity = Scene.PhysicsWorld.Gravity * body.GravityScale;
				velocity -= (gravity - normal * gravity.Dot( normal )) * Time.Delta;
			}
		}
		else if ( normalUp > 0 )
		{
			velocity -= up * (velocity.Dot( normal ) / normalUp);
		}

		body.Velocity = velocity + Controller.GroundVelocity;
	}

	internal void RequestJump() => _jumpRequested = true;

	void ApplyJumpMomentum()
	{
		var retention = JumpMomentumRetention.Clamp( 0, 1 );
		if ( EnableBunnyHopping && retention == 1 ) return;

		var body = Controller.Body;
		var horizontal = Controller.WithoutVertical( body.Velocity - Controller.GroundVelocity );
		var retained = EnableBunnyHopping ? horizontal : horizontal.ClampLength( MathF.Max( Controller.RunSpeed, 0 ) );
		body.Velocity += retained * retention - horizontal;
	}

	internal void BeginPhysicsStep()
	{
		_physicsStep = true;
		var body = Controller.Body;
		var up = Controller.UpDirection;
		var gravity = -Scene.PhysicsWorld.Gravity.Dot( up );
		var jumpRequested = _jumpRequested;
		_jumpRequested = false;
		if ( !EnableBunnyHopping && Controller.IsOnGround
			&& Controller.TimeSinceUngrounded < MathF.Max( BunnyHopRecoveryTime, 0 ) )
			jumpRequested = false;

		if ( !Controller.IsOnGround || jumpRequested )
			body.Velocity = Controller.WithVertical( body.Velocity,
				MovementMath.StartGravity( body.Velocity.Dot( up ), gravity, body.GravityScale, Time.Delta ) );

		if ( jumpRequested && !(Controller.IsDucking && Controller.IsDuckTransition)
			&& (Controller.IsOnGround || Controller.CanCoyoteJump) )
		{
			ApplyJumpMomentum();
			var jumpBoost = Controller.CalculateJumpBoost( up * Controller.JumpSpeed );
			var vertical = MovementMath.Jump( body.Velocity.Dot( up ), Controller.JumpSpeed, Controller.IsDucking || Controller.IsDuckTransition );
			OnJump();
			Controller.PreventGrounding( 0 );
			body.Velocity = Controller.WithVertical( body.Velocity,
				MovementMath.FinishGravity( vertical, gravity, body.GravityScale, Time.Delta ) ) + jumpBoost;
			Controller.NotifyJumped();
		}
		body.Velocity = MovementMath.CheckVelocity( body.Velocity, MathF.Max( MaxVelocity, 0 ) );
		_nativeGravityStep = body.Gravity;
	}

	internal void FinishPhysicsStep()
	{
		if ( !_physicsStep ) return;
		_physicsStep = false;
		var body = Controller.Body;
		var up = Controller.UpDirection;
		var vertical = body.Velocity.Dot( up );
		// Slow upward motion has reduced air control.
		SurfaceFriction = Controller.IsOnGround
			? MovementMath.SurfaceFriction( MathF.Max( Controller.GroundFriction, 0 ) )
			: vertical > 0 && vertical <= MovementMath.NonJumpVelocity ? 0.25f : 1;
		// Moving supports already applied native gravity.
		if ( !Controller.IsOnGround && !_nativeGravityStep )
			body.Velocity = Controller.WithVertical( body.Velocity,
				MovementMath.FinishGravity( vertical, -Scene.PhysicsWorld.Gravity.Dot( up ), body.GravityScale, Time.Delta ) );
		else if ( Controller.IsOnGround && !Controller.GroundUsesContactFriction )
			body.Velocity = Controller.WithVertical( body.Velocity, Controller.GroundVelocity.Dot( up ) );
		body.Velocity = MovementMath.CheckVelocity( body.Velocity, MathF.Max( MaxVelocity, 0 ) );
	}

	public override void OnModeEnd( MoveMode next )
	{
		_jumpRequested = false;
		_physicsStep = false;
		_previousSlideContact = null;
		base.OnModeEnd( next );
	}

	public override void UpdateRigidBody( Rigidbody body )
	{
		base.UpdateRigidBody( body );

		body.LinearDamping = 0;
		if ( Controller.IsOnGround && Controller.GroundUsesContactFriction )
		{
			body.Gravity = true;
			return;
		}

		// Split gravity in air; retain native drift on frictionless ground.
		body.Gravity = Controller.IsOnGround && Controller.GroundFriction <= 0;
		Controller.FeetCollider.Friction = 0;
	}

	public override void PrePhysicsStep()
	{
		base.PrePhysicsStep();

		// Apply drag once before either solver, so static contact correction cannot undo it.
		if ( !Controller.IsOnGround )
			Controller.Body.Velocity /= 1 + MathF.Max( Controller.AirFriction, 0 ) * Time.Delta;

		if ( Controller.IsOnGround && StepUpHeight > 0 )
		{
			TrySteppingUp( StepUpHeight );
		}
		_movementStart = Controller.Body.PhysicsBody.Position;
		_movementVelocity = Controller.Body.Velocity;
		PrepareStaticMove();
	}

	public override void PostPhysicsStep()
	{
		base.PostPhysicsStep();
		FinishStaticMove();
		FinishStaticWallImpact();
		ResolveStaticOverlap();
		StickToGround( StepDownHeight );
	}

	public override bool IsStandableSurface( in SceneTraceResult result )
	{
		if ( Vector3.GetAngle( Controller.UpDirection, result.Normal ) > GroundAngle )
			return false;

		return true;
	}

	public override Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		if ( Scene.Is2D )
			return base.UpdateMove( Rotation.Identity, Vector3.Backward * input.y );

		eyes = eyes.Angles() with { pitch = 0 };

		return base.UpdateMove( eyes, input );
	}

	public override Transform CalculateEyeTransform()
	{
		var transform = base.CalculateEyeTransform();
		if ( Controller.IsDuckTransition && Controller.IsOnGround )
		{
			var height = Controller.BodyHeight - (Controller.BodyHeight - Controller.DuckedHeight) * Controller.DuckInputFraction;
			transform.Position = Controller.WorldPosition + Controller.UpDirection * (height - Controller.EyeDistanceFromTop);
		}
		return transform;
	}
}
