namespace Sandbox.Movement;

#pragma warning disable CS0618

partial class MoveMode
{
	/// <summary>
	/// Update the animator which is available at Controller.Renderer.
	/// </summary>
	public virtual void UpdateAnimator( SkinnedModelRenderer renderer )
	{
		// Animation parameters

		OnUpdateAnimatorVelocity( renderer );
		OnUpdateAnimatorState( renderer );

		OnUpdateAnimatorLookDirection( renderer );

		UpdateRotationSpeed( renderer );

		// Renderer transform

		OnRotateRenderBody( renderer );
	}

	Vector3.SmoothDamped smoothedMove = new Vector3.SmoothDamped( 0, 0, 0.5f );
	Vector3.SmoothDamped smoothedWish = new Vector3.SmoothDamped( 0, 0, 0.5f );
	Vector3.SmoothDamped smoothedSkid = new Vector3.SmoothDamped( 0, 0, 0.5f );

	/// <summary>
	/// <para>
	/// Sets animation parameters on <paramref name="renderer"/> based on the current
	/// <see cref="PlayerController.Velocity"/> and <see cref="PlayerController.WishVelocity"/>.
	/// </para>
	/// </summary>
	protected virtual void OnUpdateAnimatorVelocity( SkinnedModelRenderer renderer )
	{
		var rot = renderer.WorldRotation;
		var vel = Controller.Velocity;
		var wishVel = Controller.WishVelocity;

		// skid
		{
			var skidAmount = 0.5f; // multiplier for moving skid
			var pushSkidAmount = 1.0f; // multiplier for sliding down a slope when standing still
			var skidDelay = 0.2f; // in seconds, longer means bigger gap between the velocities, more skidding

			// smooth version of our velocity
			var smoothed = vel;
			{
				smoothedMove.Target = smoothed;
				smoothedMove.SmoothTime = skidDelay;
				smoothedMove.Update( Time.Delta );
				smoothed = smoothedMove.Current;
			}

			// skid is the difference between our old velocity and our current velocity
			var skid = (smoothed - vel) * skidAmount;

			// if we're standing still, use our actual velity as the skid
			skid = Vector3.Lerp( skid, vel * pushSkidAmount, wishVel.Length.Remap( 100, 0, 0, 1 ) );

			// smooth our skidders
			smoothedSkid.Target = skid;
			smoothedSkid.SmoothTime = 0.5f;
			smoothedSkid.Update( Time.Delta );
			skid = smoothedSkid.Current;

			// convert to model space
			skid = GetLocalVelocity( rot, skid );

			SetAnimatorParameter( renderer, "skid_x", skid.x / 400.0f );
			SetAnimatorParameter( renderer, "skid_y", skid.y / 400.0f );
		}

		// move the legs
		{
			var smoothed = wishVel;
			{
				smoothedWish.Target = smoothed;
				smoothedWish.SmoothTime = 0.6f;
				smoothedWish.Update( Time.Delta );
				smoothed = smoothedWish.Current;

				// Stop walking if we're too slow
				smoothed = ApplyDeadZone( smoothed, 10 );
			}

			smoothed = GetLocalVelocity( rot, smoothed );

			SetAnimatorParameter( renderer, "move_groundspeed", smoothed.WithZ( 0f ).Length );
			SetAnimatorParameter( renderer, "move_x", smoothed.x );
			SetAnimatorParameter( renderer, "move_y", smoothed.y );
			SetAnimatorParameter( renderer, "move_z", smoothed.z );
		}

		// the player's wish
		{
			var local = GetLocalVelocity( rot, wishVel );

			SetAnimatorParameter( renderer, "wish_groundspeed", wishVel.WithZ( 0f ).Length );
			SetAnimatorParameter( renderer, "wish_x", local.x );
			SetAnimatorParameter( renderer, "wish_y", local.y );
			SetAnimatorParameter( renderer, "wish_z", local.z );
		}

	}

	#region OnUpdateAnimatorVelocity Helpers

	private static Vector3 ApplyDeadZone( Vector3 velocity, float minimum )
	{
		return velocity.IsNearlyZero( minimum ) ? 0f : velocity;
	}

	private static Vector3 GetLocalVelocity( Rotation rotation, Vector3 worldVelocity )
	{
		// TODO: this could be rotation.Inverse * worldVelocity

		var forward = rotation.Forward.Dot( worldVelocity );
		var sideward = rotation.Right.Dot( worldVelocity );

		return new Vector3( forward, sideward, worldVelocity.z );
	}

	#endregion

	/// <summary>
	/// Sets animation parameters on <paramref name="renderer"/> describing the movement style, like
	/// swimming, falling, or ducking.
	/// </summary>
	protected virtual void OnUpdateAnimatorState( SkinnedModelRenderer renderer )
	{
		if ( renderer.Animation.Graph is not null )
			renderer.Animation.Parameters.TrySet( "sit", "not_sitting" );
		else
			SetAnimatorParameter( renderer, "sit", 0 );
		SetAnimatorParameter( renderer, "b_swim", Controller.IsSwimming );
		SetAnimatorParameter( renderer, "b_climb", Controller.IsClimbing );
		SetAnimatorParameter( renderer, "b_grounded", Controller.IsOnGround || Controller.IsClimbing );

		var duck = Controller.Headroom.Remap( 25, 0, 0, 0.5f, true );

		if ( Controller.IsDucking )
		{
			duck *= 3.0f;
			duck += 1.0f;
		}

		SetAnimatorParameter( renderer, "duck", duck );
	}

	/// <summary>
	/// Set animation parameters on <paramref name="renderer"/> to look towards <see cref="CalculateEyeTransform"/>.
	/// </summary>
	protected virtual void OnUpdateAnimatorLookDirection( SkinnedModelRenderer renderer )
	{
		if ( renderer.Animation.Graph is not null )
		{
			OnUpdateAnimationGraph( renderer );
			return;
		}

		var eyesForward = Controller.EyeTransform.Forward;

		renderer.SetLookDirection( "aim_eyes", eyesForward, Controller.AimStrengthEyes );
		renderer.SetLookDirection( "aim_head", eyesForward, Controller.AimStrengthHead );
		renderer.SetLookDirection( "aim_body", eyesForward, Controller.AimStrengthBody );
	}

	/// <summary>
	/// Updates the <see cref="Component.WorldRotation"/> of <paramref name="renderer"/>.
	/// </summary>
	protected virtual void OnRotateRenderBody( SkinnedModelRenderer renderer )
	{
		if ( Scene.Is2D )
			return;

		var eyeAngles = Controller.EyeTransform.Rotation.Angles();

		var targetAngle = Rotation.FromYaw( eyeAngles.yaw );
		var velocity = Controller.WishVelocity.WithZ( 0 );

		var rotateDifference = renderer.WorldRotation.Distance( targetAngle );
		var oldRotation = renderer.WorldRotation;

		// We're over the limit - snap it 
		if ( rotateDifference > Controller.RotationAngleLimit )
		{
			var delta = 0.999f - Controller.RotationAngleLimit / rotateDifference;
			var newRotation = Rotation.Lerp( renderer.WorldRotation, targetAngle, delta );

			renderer.WorldRotation = newRotation;
		}

		// Otherwise only rotate while moving
		if ( velocity.Length > 10 )
		{
			// TODO: frame rate dependent

			renderer.WorldRotation = Rotation.Slerp( renderer.WorldRotation, targetAngle,
				Time.Delta * 2.0f * Controller.RotationSpeed * velocity.Length.Remap( 0, 100 ) );
		}

		AddRotationSpeed( oldRotation, renderer.WorldRotation );
	}

	private const float RotationSpeedUpdatePeriod = 0.1f;

	private float _animRotationSpeed;
	private TimeSince _timeSinceRotationSpeedUpdate;

	private void AddRotationSpeed( Rotation oldRotation, Rotation newRotation )
	{
		// TODO: this assumes we're always working with exactly one Renderer

		var oldYaw = oldRotation.Angles().yaw;
		var newYaw = newRotation.Angles().yaw;

		// TODO: why is this backwards?

		var deltaYaw = MathX.DeltaDegrees( newYaw, oldYaw );

		_animRotationSpeed = (_animRotationSpeed + deltaYaw).Clamp( -90f, 90f );
	}

	private void UpdateRotationSpeed( SkinnedModelRenderer renderer )
	{
		if ( _timeSinceRotationSpeedUpdate < RotationSpeedUpdatePeriod ) return;

		// TODO: why 5?

		SetAnimatorParameter( renderer, "move_rotationspeed", _animRotationSpeed * 5f );

		_timeSinceRotationSpeedUpdate = 0f;
		_animRotationSpeed = 0f;
	}

	protected static void SetAnimatorParameter( SkinnedModelRenderer renderer, string name, float value )
	{
		if ( renderer.Animation.Graph is not null )
			renderer.Animation.Parameters.TrySet( name, value );
		else
			renderer.Set( name, value );
	}

	protected static void SetAnimatorParameter( SkinnedModelRenderer renderer, string name, bool value )
	{
		if ( renderer.Animation.Graph is not null )
			renderer.Animation.Parameters.TrySet( name, value );
		else
			renderer.Set( name, value );
	}

	protected static void SetAnimatorParameter( SkinnedModelRenderer renderer, string name, int value )
	{
		if ( renderer.Animation.Graph is not null )
			renderer.Animation.Parameters.TrySet( name, value );
		else
			renderer.Set( name, value );
	}

}
