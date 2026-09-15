
namespace Sandbox.Movement;

/// <summary>
/// A move mode for this character
/// </summary>
public abstract partial class MoveMode : Component
{
	public virtual bool AllowGrounding => false;
	public virtual bool AllowFalling => false;

	[RequireComponent]
	public PlayerController Controller { get; set; }

	/// <summary>
	/// Highest number becomes the new control mode
	/// </summary>
	public virtual int Score( PlayerController controller ) => 0;

	/// <summary>
	/// Called before the physics step is run
	/// </summary>
	public virtual void PrePhysicsStep()
	{

	}

	/// <summary>
	/// Called after the physics step is run
	/// </summary>
	public virtual void PostPhysicsStep()
	{

	}

	public virtual void UpdateRigidBody( Rigidbody body )
	{
		bool wantsGravity = false;

		// If we're standing still on a peice of ground, turn off gravity until
		// we move again. This stops us slowly slipping down surfaces.
		if ( !Controller.IsOnGround ) wantsGravity = true;
		if ( Controller.WithoutVertical( Controller.Velocity ).Length > 1 ) wantsGravity = true;
		if ( Controller.GroundVelocity.Length > 1 ) wantsGravity = true;
		if ( Controller.GroundIsDynamic ) wantsGravity = true;

		body.Gravity = wantsGravity;

		// when we're standing on the still ground and aren't wishing to move we apply a high linear damping to the body
		// this stops whatever momentum it had from dragging it slowly down hills.
		bool wantsbrakes = Controller.IsOnGround && Controller.WishVelocity.Length < 1 && Controller.GroundVelocity.Length < 1;
		body.LinearDamping = wantsbrakes ? (10.0f * Controller.BrakePower) : Controller.AirFriction;

		body.AngularDamping = 1f;
	}

	public virtual void AddVelocity()
	{
		var body = Controller.Body;
		var wish = Controller.WishVelocity;
		if ( wish.IsNearZeroLength ) return;

		var groundFriction = 0.25f + Controller.GroundFriction * 10;
		var groundVelocity = Controller.GroundVelocity;

		var vertical = body.Velocity.Dot( Controller.UpDirection );

		var velocity = (body.Velocity - Controller.GroundVelocity);
		var speed = velocity.Length;

		var maxSpeed = MathF.Max( wish.Length, speed );

		if ( Controller.IsOnGround )
		{
			var amount = 1 * groundFriction;
			velocity = velocity.AddClamped( wish * amount, wish.Length * amount );
		}
		else
		{
			var amount = 0.05f;
			velocity = velocity.AddClamped( wish * amount, wish.Length );
		}

		if ( velocity.Length > maxSpeed )
			velocity = velocity.Normal * maxSpeed;

		velocity += groundVelocity;

		if ( Controller.IsOnGround )
		{
			velocity = Controller.WithVertical( velocity, vertical );
		}

		body.Velocity = velocity;
	}

	/// <summary>
	/// This mode has just started
	/// </summary>
	public virtual void OnModeBegin()
	{

	}

	/// <summary>
	/// This mode has stopped. We're swapping to another move mode.
	/// </summary>
	public virtual void OnModeEnd( MoveMode next )
	{

	}

	/// <summary>
	/// If we're approaching a step, step up if possible
	/// </summary>
	protected void TrySteppingUp( float maxDistance )
	{
		Controller.TryStep( maxDistance );
	}

	/// <summary>
	/// If we're on the ground, make sure we stay there by falling to the ground
	/// </summary>
	protected void StickToGround( float maxDistance )
	{
		Controller.Reground( maxDistance );
	}

	public virtual bool IsStandableSurace( in SceneTraceResult result )
	{
		return false;
	}

	public virtual bool IsStandableSurface( in SceneTraceResult result )
	{
		return IsStandableSurace( result );
	}

	Vector3.SmoothDamped smoothedMovement;

	/// <summary>
	/// Read inputs, return WishVelocity
	/// </summary>
	public virtual Vector3 UpdateMove( Rotation eyes, Vector3 input )
	{
		// don't normalize, because analog input might want to go slow
		input = input.ClampLength( 1 );

		var direction = eyes * input;

		// Run if we're holding down alt move button
		bool run = Input.Down( Controller.AltMoveButton );

		// if Run is default, flip that logic
		if ( Controller.RunByDefault ) run = !run;

		// if we're running, use run speed, if not use walk speed
		var velocity = run ? Controller.RunSpeed : Controller.WalkSpeed;

		// if we're ducking, always use duck walk speed
		if ( Controller.IsDucking ) velocity = Controller.DuckedSpeed;

		if ( direction.IsNearlyZero( 0.1f ) )
		{
			direction = 0;
		}
		else
		{
			// Retain momentum, once we're moving, we're moving. Don't lerp between directions, only between speeds.
			smoothedMovement.Current = direction.Normal * smoothedMovement.Current.Length;
		}

		//
		// Smooth the wish velocity
		//
		smoothedMovement.Target = direction * velocity;
		smoothedMovement.SmoothTime = smoothedMovement.Target.Length < smoothedMovement.Current.Length ? Controller.DeaccelerationTime : Controller.AccelerationTime;
		smoothedMovement.Update( Time.Delta );

		// If it's near zero, just stop
		if ( smoothedMovement.Current.IsNearlyZero( 0.01f ) )
		{
			smoothedMovement.Current = 0;
		}

		//DebugOverlay.ScreenText( 200, $"{smoothedMovement.Current.Length}" );

		return smoothedMovement.Current;
	}
}
