namespace Sandbox;

public sealed partial class PlayerController : Component
{
	/// <summary>
	/// The direction we're looking in input space.
	/// </summary>
	[Sync( SyncFlags.Interpolate )]
	public Angles EyeAngles { get; set; }

	/// <summary>
	/// The player's eye position, in first person mode
	/// </summary>
	public Vector3 EyePosition => EyeTransform.Position;

	/// <summary>
	/// The player's eye position, in first person mode
	/// </summary>
	public Transform EyeTransform { get; private set; }

	/// <summary>
	/// True if this player is ducking
	/// </summary>
	[Sync]
	public bool IsDucking { get; set; }

	/// <summary>
	/// The distance from the top of the head to the closest ceiling.
	/// </summary>
	public float Headroom { get; set; }


	protected override void OnUpdate()
	{
		UpdateGroundEyeRotation();

		if ( Scene.IsEditor )
			return;

		UpdateEyeTransform();

		if ( !IsProxy )
		{
			GameObject.RunEvent<IEvents>( x => x.PreInput() );

			if ( UseInputControls && UseLookControls )
			{
				UpdateEyeAngles();
				UpdateLookAt();
			}

			if ( UseCameraControls )
			{
				UpdateCameraPosition();
			}

			UpdateEyeTransform();
		}

		UpdateBodyVisibility();

		if ( UseAnimatorControls && Renderer.IsValid() )
		{
			UpdateAnimation( Renderer );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( Scene.IsEditor ) return;

		UpdateHeadroom();
		UpdateFalling();

		prevPosition = WorldPosition;

		if ( IsProxy ) return;
		if ( !UseInputControls ) return;

		InputMove();
		UpdateDucking( Input.Down( "duck" ) );
		InputJump();

		UpdateEyeTransform();
	}

	void UpdateHeadroom()
	{
		var tr = TraceBody( WorldPosition + Vector3.Up * CurrentHeight * 0.5f, WorldPosition + Vector3.Up * (100 + CurrentHeight * 0.5f), 0.75f, 0.5f );
		Headroom = tr.Distance;
	}

	bool _wasFalling = false;
	float fallDistance = 0;
	Vector3 prevPosition;

	void UpdateFalling()
	{
		if ( Mode is null || !Mode.AllowFalling )
		{
			_wasFalling = false;
			fallDistance = 0;
			return;
		}

		if ( !IsOnGround || _wasFalling )
		{
			var fallDelta = WorldPosition - prevPosition;
			if ( fallDelta.z < 0.0f )
			{
				_wasFalling = true;
				fallDistance -= fallDelta.z;
			}
		}

		if ( IsOnGround )
		{
			if ( _wasFalling && fallDistance > 1.0f )
			{
				IEvents.PostToGameObject( GameObject, x => x.OnLanded( fallDistance, Velocity ) );

				// play land sounds
				if ( EnableFootstepSounds )
				{
					var volume = Velocity.Length.Remap( 50, 800, 0.5f, 5 );
					var vel = Velocity.Length;

					PlayFootstepSound( WorldPosition, volume, 0 );
					PlayFootstepSound( WorldPosition, volume, 1 );
				}
			}

			_wasFalling = false;
			fallDistance = 0;
		}
	}

	Transform localGroundTransform;
	int groundHash;

	void UpdateGroundEyeRotation()
	{
		if ( GroundObject is null )
		{
			groundHash = default;
			return;
		}

		if ( !RotateWithGround )
		{
			groundHash = default;
			return;
		}

		var hash = HashCode.Combine( GroundObject );

		// Get out transform locally to the ground object
		var localTransform = GroundObject.WorldTransform.ToLocal( WorldTransform );

		// Work out the rotation delta chance since last frame
		var delta = localTransform.Rotation.Inverse * localGroundTransform.Rotation;

		// we only care about the yaw
		var deltaYaw = delta.Angles().yaw;

		//DebugDrawSystem.Current.Text( WorldPosition, $"{delta.Angles().yaw}" );

		// If we're on the same ground and we've rotated
		if ( hash == groundHash && deltaYaw != 0 )
		{
			// rotate the eye angles
			EyeAngles = EyeAngles.WithYaw( EyeAngles.yaw + deltaYaw );

			// rotate the body to avoid it animating to the new position
			if ( UseAnimatorControls && Renderer.IsValid() )
			{
				Renderer.WorldRotation *= new Angles( 0, deltaYaw, 0 );
			}
		}

		// Keep for next frame
		groundHash = hash;
		localGroundTransform = localTransform;
	}



}
