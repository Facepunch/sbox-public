namespace Sandbox;

public sealed partial class PlayerController : Component
{
	[Property, FeatureEnabled( "Input", Icon = "sports_esports", Description = "Default controls using AnalogMove and AnalogLook. Can optionally interact with any IPressable." )]
	public bool UseInputControls { get; set; } = true;

	[Property, Feature( "Input" )] public float WalkSpeed { get; set; } = 110;
	[Property, Feature( "Input" )] public float RunSpeed { get; set; } = 320;
	[Property, Feature( "Input" )] public float DuckedSpeed { get; set; } = 70;
	[Property, Feature( "Input" )] public float JumpSpeed { get; set; } = Movement.MovementMath.JumpSpeed;
	[Property, Feature( "Input" )] public float DuckedHeight { get; set; } = 36;

	/// <summary>
	/// The button that the player will hold to duck. Leave empty to disable ducking input
	/// (ducking can still be driven from code via <see cref="UpdateDucking"/>).
	/// </summary>
	[Property, Feature( "Input" ), InputAction, Category( "Ducking" )] public string DuckButton { get; set; } = "duck";

	/// <summary>
	/// How many seconds it takes to crouch while on the ground. Set to 0 to crouch instantly.
	/// </summary>
	[Property, Feature( "Input" ), Category( "Ducking" ), Range( 0f, 1f )]
	public float DuckDuration { get; set; } = 0.4f;

	/// <summary>
	/// How many seconds it takes to stand back up while on the ground. Set to 0 to stand instantly.
	/// </summary>
	[Property, Feature( "Input" ), Category( "Ducking" ), Range( 0f, 1f )]
	public float UnduckDuration { get; set; } = 0.2f;

	/// <summary>Input smoothing for non-walking modes.</summary>
	[Property, Feature( "Input" )] public float AccelerationTime { get; set; } = 0;

	/// <summary>Deceleration smoothing for non-walking modes.</summary>
	[Property, Feature( "Input" )] public float DeaccelerationTime { get; set; } = 0;

	/// <summary>
	/// Adds forward momentum when jumping from the ground at full sprint speed
	/// </summary>
	[Property, Feature( "Input" ), ToggleGroup( "EnableJumpBoost", Label = "Jump Boost" )]
	public bool EnableJumpBoost { get; set; }

	/// <summary>
	/// Extra momentum added when sprint jumping (as a fraction of RunSpeed)
	/// </summary>
	public float JumpBoost { get; set; } = 0.1f;

	/// <summary>
	/// The button that the player will press to use to run
	/// </summary>
	[Property, Feature( "Input" ), InputAction, Category( "Running" )] public string AltMoveButton { get; set; } = "run";

	/// <summary>
	/// If true then the player will run by default, and holding AltMoveButton will switch to walk
	/// </summary>
	[Property, Feature( "Input" ), Category( "Running" )] public bool RunByDefault { get; set; }

	/// <summary>
	/// How many seconds after leaving the ground the player can still jump. Set to 0 to disable.
	/// </summary>
	[Property, Feature( "Input" ), Category( "Jumping" ), Range( 0f, 1f )]
	public float CoyoteTime { get; set; } = 0;

	/// <summary>
	/// Keep jumping while the jump button is held, subject to the movement mode's jump limits.
	/// </summary>
	[Property, Feature( "Input" ), Category( "Jumping" )]
	public bool HoldToJump { get; set; }

	/// <summary>
	/// Allows to player to interact with things by "use"ing them. 
	/// Usually by pressing the "use" button.
	/// </summary>
	[Property, Feature( "Input" ), ToggleGroup( "EnablePressing", Label = "Enable Pressing" )] public bool EnablePressing { get; set; } = true;

	/// <summary>
	/// The button that the player will press to use things
	/// </summary>
	[Property, Feature( "Input" ), Group( "EnablePressing" ), InputAction] public string UseButton { get; set; } = "use";

	/// <summary>
	/// How far from the eye can the player reach to use things
	/// </summary>
	[Property, Feature( "Input" ), Group( "EnablePressing" )] public float ReachLength { get; set; } = 130;


	/// <summary>
	/// When true we'll move the camera around using the mouse
	/// </summary>
	[Property, Feature( "Input" ), Category( "Eye Angles" )] public bool UseLookControls { get; set; } = true;
	[Property, Feature( "Input" ), Category( "Eye Angles" )] public bool RotateWithGround { get; set; } = true;
	[Property, Feature( "Input" ), Category( "Eye Angles" ), Range( 0, 180 )] public float PitchClamp { get; set; } = 90;

	/// <summary>
	/// Allows modifying the eye angle sensitivity. Note that player preference sensitivity is already automatically applied, this is just extra.
	/// </summary>
	[Property, Feature( "Input" ), Category( "Eye Angles" ), Range( 0, 2 )] public float LookSensitivity { get; set; } = 1;

	void UpdateEyeAngles()
	{
		var input = Input.AnalogLook;

		input *= LookSensitivity;

		GameObject.Components.ExecuteEnabledInSelfAndDescendants<IEvents, Angles>( ref input, static ( IEvents x, ref Angles angles ) => x.OnEyeAngles( ref angles ) );

		var ee = EyeAngles;
		ee += input;
		ee.roll = 0;

		if ( PitchClamp > 0 )
		{
			ee.pitch = ee.pitch.Clamp( -PitchClamp, PitchClamp );
		}

		EyeAngles = ee;
	}

	void InputMove()
	{
		var rot = EyeAngles.ToRotation();
		WishVelocity = Mode.UpdateMove( rot, Input.AnalogMove );
	}

	void InputJump()
	{
		if ( !IsOnGround && !CanCoyoteJump ) return;
		if ( !Input.Pressed( "Jump" ) && !(HoldToJump && IsOnGround && Input.Down( "Jump" )) ) return;
		if ( JumpSpeed <= 0 ) return;

		if ( Mode is Movement.MoveModeWalk walk )
		{
			walk.RequestJump();
			return;
		}

		Jump( UpDirection * JumpSpeed );
		NotifyJumped();
	}

	internal void NotifyJumped()
	{
		_canCoyoteJump = false;
		OnJumped();

		IEvents.PostToGameObject( GameObject, x => x.OnJumped() );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly | NetFlags.Unreliable )]
	public void OnJumped()
	{
		if ( UseAnimatorControls && Renderer.IsValid() )
		{
			if ( Renderer.Animation.Graph is not null )
			{
				_jumpAnimationPending = true;
				return;
			}

#pragma warning disable CS0618
			Renderer.Set( "b_jump", true );
#pragma warning restore CS0618
		}
	}

	Vector3 bodyDuckOffset = 0;
	bool _duckInput;
	float _duckInputElapsed;
	internal bool IsDuckTransition { get; private set; }
	float CurrentDuckDuration => MathF.Max( 0, _duckInput ? DuckDuration : UnduckDuration );
	internal float DuckInputFraction
	{
		get
		{
			if ( !IsDuckTransition || !IsOnGround ) return IsDucking ? 1 : 0;
			var duration = CurrentDuckDuration;
			if ( duration <= 0 ) return _duckInput ? 1 : 0;
			var t = (_duckInputElapsed / duration).Clamp( 0, 1 );
			var fraction = t * t * (3 - 2 * t);
			return _duckInput ? fraction : 1 - fraction;
		}
	}

	internal void UpdateDuckInput( bool wantsDuck )
	{
		if ( wantsDuck != _duckInput )
		{
			_duckInput = wantsDuck;
			_duckInputElapsed = 0;
		}
		else _duckInputElapsed += Time.Delta;

		IsDuckTransition = wantsDuck != IsDucking;
		if ( !IsDuckTransition ) return;
		if ( !wantsDuck && !CanUnduck( out _ ) )
		{
			_duckInputElapsed = 0;
			IsDuckTransition = false;
			return;
		}
		var duration = CurrentDuckDuration;
		if ( Mode is Movement.MoveModeWalk && IsOnGround && duration > 0 && _duckInputElapsed <= duration ) return;
		UpdateDucking( wantsDuck );
		IsDuckTransition = wantsDuck != IsDucking;
	}

	bool CanUnduck( out Vector3 newPosition )
	{
		var scale = MathF.Abs( WorldTransform.UniformScale );
		newPosition = WorldPosition;
		if ( IsAirborne ) newPosition -= UpDirection * ((BodyHeight - DuckedHeight) * scale);
		var hull = BodyBox( 1, BodyHeight / CurrentHeight );
		if ( IsOnGround )
		{
			var up = UpDirection;
			var size = hull.Size - up.Abs() * (_groundedFeetLift / scale);
			var center = hull.Center + up * (_groundedFeetLift / scale * 0.5f);
			hull = new BBox( center - size * 0.5f, center + size * 0.5f );
		}
		var trace = Scene.Trace.Box( new BBox( hull.Mins * scale, hull.Maxs * scale ), WorldPosition, newPosition )
			.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).Run();
		return !trace.StartedSolid && !trace.Hit;
	}

	/// <summary>
	/// Gets the current character height from <see cref="BodyHeight"/> when standing,
	/// otherwise uses <see cref="DuckedHeight"/> when ducking.
	/// </summary>
	public float CurrentHeight => IsDucking ? DuckedHeight : BodyHeight;

	/// <summary>Changes posture immediately if headroom allows; default input uses timed transitions.</summary>
	public void UpdateDucking( bool wantsDuck )
	{
		if ( wantsDuck == IsDucking ) return;

		var unduckDelta = (BodyHeight - DuckedHeight) * MathF.Abs( WorldTransform.UniformScale );
		var newPosition = WorldPosition;

		if ( !wantsDuck && !CanUnduck( out newPosition ) ) return;

		IsDucking = wantsDuck;
		IsDuckTransition = false;
		WorldPosition = newPosition;

		// Air crouching preserves head position.
		if ( wantsDuck && IsAirborne )
		{
			WorldPosition += UpDirection * unduckDelta;
			Transform.ClearInterpolation();
			bodyDuckOffset = UpDirection * -unduckDelta;
		}

		UpdateBody();
	}
}
