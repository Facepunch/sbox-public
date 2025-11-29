namespace Sandbox;

public sealed partial class PlayerController : Component
{
	[Property, FeatureEnabled( "Input", Icon = "sports_esports", Description = "Default controls using AnalogMove and AnalogLook. Can optionally interact with any IPressable." )]
	public bool UseInputControls { get; set; } = true;

	[Property, Feature( "Input" )] public float WalkSpeed { get; set; } = 110;
	[Property, Feature( "Input" )] public float RunSpeed { get; set; } = 320;
	[Property, Feature( "Input" )] public float DuckedSpeed { get; set; } = 70;
	[Property, Feature( "Input" )] public float JumpSpeed { get; set; } = 300;
	[Property, Feature( "Input" )] public float DuckedHeight { get; set; } = 36;

	/// <summary>
	/// Amount of seconds it takes to get from your current speed to your requuested speed, if higher
	/// </summary>
	[Property, Feature( "Input" )] public float AccelerationTime { get; set; } = 0;

	/// <summary>
	/// Amount of seconds it takes to get from your current speed to your requuested speed, if lower
	/// </summary>
	[Property, Feature( "Input" )] public float DeaccelerationTime { get; set; } = 0;

	/// <summary>
	/// The button that the player will press to use to run
	/// </summary>
	[Property, Feature( "Input" ), InputAction, Category( "Running" )] public string AltMoveButton { get; set; } = "run";

	/// <summary>
	/// If true then the player will run by default, and holding AltMoveButton will switch to walk
	/// </summary>
	[Property, Feature( "Input" ), Category( "Running" )] public bool RunByDefault { get; set; }

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

	TimeSince timeSinceJump = 0;

	void UpdateEyeAngles()
	{
		var input = Input.AnalogLook;

		input *= LookSensitivity;

		IEvents.PostToGameObject( GameObject, x => x.OnEyeAngles( ref input ) );

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
		if ( TimeSinceGrounded > 0.33f ) return; // been off the ground for this many seconds, don't jump
		if ( !Input.Pressed( "Jump" ) ) return; // not pressing jump
		if ( timeSinceJump < 0.5f ) return; // don't jump too often
		if ( JumpSpeed <= 0 ) return;

		timeSinceJump = 0;
		Jump( Vector3.Up * JumpSpeed );
		OnJumped();

		IEvents.PostToGameObject( GameObject, x => x.OnJumped() );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly | NetFlags.Unreliable )]
	public void OnJumped()
	{
		if ( UseAnimatorControls && Renderer.IsValid() )
		{
			Renderer.Set( "b_jump", true );
		}
	}

	Vector3 bodyDuckOffset = 0;

	/// <summary>
	/// Gets the current character height from <see cref="BodyHeight"/> when standing,
	/// otherwise uses <see cref="DuckedHeight"/> when ducking.
	/// </summary>
	public float CurrentHeight => IsDucking ? DuckedHeight : BodyHeight;

	/// <summary>
	/// Called during FixedUpdate when UseInputControls is enabled. Will duck if requested.
	/// If not, and we're ducked, will unduck if there is room
	/// </summary>
	public void UpdateDucking( bool wantsDuck )
	{
		if ( wantsDuck == IsDucking ) return;

		var unduckDelta = CurrentHeight - DuckedHeight;

		// Can we unduck?
		if ( !wantsDuck )
		{
			if ( IsAirborne )
				return;

			if ( Headroom < unduckDelta )
				return;
		}

		IsDucking = wantsDuck;

		// if we're in the air, keep our head in the same position
		if ( wantsDuck && IsAirborne )
		{
			WorldPosition += Vector3.Up * unduckDelta;
			Transform.ClearInterpolation();
			bodyDuckOffset = Vector3.Up * -unduckDelta;
		}
	}
}
