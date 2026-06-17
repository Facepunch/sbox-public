using Sandbox;

/// <summary>
/// Handles clicking on a crate to "bump" it in the clicked direction.
/// Uses a spring-based simulated tilt that always returns to the initial rotation.
/// </summary>
public sealed class RewardUnboxCrate : Component
{
	/// <summary>
	/// Maximum tilt angle in degrees when bumped.
	/// </summary>
	[Property] public float MaxTiltAngle { get; set; } = 15f;

	/// <summary>
	/// How quickly the box tilts toward the target angle (spring stiffness).
	/// </summary>
	[Property] public float SpringStiffness { get; set; } = 300f;

	/// <summary>
	/// Damping factor to prevent infinite oscillation.
	/// </summary>
	[Property] public float SpringDamping { get; set; } = 12f;

	/// <summary>
	/// How much force each click applies (additive impulse).
	/// </summary>
	[Property] public float BumpImpulse { get; set; } = 800f;

	/// <summary>
	/// Half-height of the box (distance from center to bottom edge).
	/// Used to pivot around the base. Set to match your model's actual half-height.
	/// </summary>
	[Property] public float BoxHalfHeight { get; set; } = 25f;

	/// <summary>
	/// The 4 flap GameObjects that open sequentially. Assign in editor.
	/// Each flap should be parented to the crate and pivot from its hinge edge.
	/// </summary>
	[Property] public List<GameObject> Flaps { get; set; } = new();

	/// <summary>
	/// Spring stiffness for flap opening animation.
	/// </summary>
	[Property] public float FlapSpringStiffness { get; set; } = 150f;

	/// <summary>
	/// Damping for flap spring.
	/// </summary>
	[Property] public float FlapSpringDamping { get; set; } = 10f;

	/// <summary>
	/// Minimum time (seconds) between opening each flap.
	/// The next flap won't open until this time has elapsed since the last one.
	/// </summary>
	[Property] public float FlapOpenDelay { get; set; } = 0.6f;

	/// <summary>
	/// Extra delay added per subsequent flap to build tension.
	/// Total delay for flap N = FlapOpenDelay + (N * FlapDelayEscalation)
	/// </summary>
	[Property] public float FlapDelayEscalation { get; set; } = 0.3f;

	/// <summary>
	/// Maximum shake intensity (degrees) applied during the buildup before a flap opens.
	/// Scales up with each subsequent flap.
	/// </summary>
	[Property] public float PreOpenShakeIntensity { get; set; } = 3f;

	/// <summary>
	/// Particle prefab GameObjects to clone on each click.
	/// Keep them disabled in the scene — they act as templates.
	/// Each clone will play its effect and should self-destruct when done.
	/// </summary>
	[Property] public List<GameObject> BurstParticles { get; set; } = new();

	/// <summary>
	/// The inner filling plane that rises as flaps open.
	/// </summary>
	[Property] public GameObject InnerFilling { get; set; }

	/// <summary>
	/// Finale particles to enable when the last flap opens (e.g. confetti rain).
	/// </summary>
	[Property] public List<GameObject> FinaleParticles { get; set; } = new();

	/// <summary>
	/// How far the inner filling rises (in local Z) when fully open.
	/// </summary>
	[Property] public float FillingRiseHeight { get; set; } = 15f;

	// Spring state: current angular displacement and velocity on two axes (pitch/roll)
	private Vector2 _angularDisplacement; // degrees of tilt on X and Y axes
	private Vector2 _angularVelocity;     // degrees/sec

	private Rotation _initialRotation;
	private Vector3 _initialPosition;

	// Flap state
	private int _nextFlapIndex;
	private float[] _flapTargetAngle;
	private float[] _flapCurrentAngle;
	private float[] _flapVelocity;
	private Rotation[] _flapInitialRotation;
	private int[] _flapAxis;       // 0=pitch, 1=yaw, 2=roll
	private float[] _flapDirection; // +1 or -1, the sign to open
	private float _lastFlapOpenTime = -999f;
	private bool _flapPending; // A flap open is queued waiting for the delay
	private Vector3 _fillingInitialLocalPos;

	// Sound state
	private SoundHandle _rustleLoop;
	private float _rustleVolume;
	private float _rustleTargetVolume;
	private SoundHandle _drumrollLoop;

	/// <summary>
	/// True once all flaps have been opened.
	/// </summary>
	public bool IsFullyOpen => Flaps.Count > 0 && _nextFlapIndex >= Flaps.Count;

	/// <summary>
	/// How many flaps have been opened so far.
	/// </summary>
	public int OpenFlapCount => _nextFlapIndex;

	protected override void OnStart()
	{
		// Randomize starting yaw in 90-degree increments so flaps open in a different order each time
		var randomYaw = Game.Random.Int( 0, 3 ) * 90f;
		WorldRotation *= Rotation.FromYaw( randomYaw );

		_initialRotation = WorldRotation;
		_initialPosition = WorldPosition;

		// Initialize flap spring state
		int count = Flaps.Count;
		_flapTargetAngle = new float[count];
		_flapCurrentAngle = new float[count];
		_flapVelocity = new float[count];
		_flapInitialRotation = new Rotation[count];
		_flapAxis = new int[count];
		_flapDirection = new float[count];

		for ( int i = 0; i < count; i++ )
		{
			if ( Flaps[i] != null )
			{
				_flapInitialRotation[i] = Flaps[i].LocalRotation;
				DetermineFlapAxis( i, Flaps[i].LocalRotation.Angles() );
			}
		}

		if ( InnerFilling != null )
			_fillingInitialLocalPos = InnerFilling.LocalPosition;

		// Start the rustle loop at zero volume
		_rustleLoop = Sound.Play( "cardboard_rustle_loop", WorldPosition );
		_rustleLoop.Volume = 0f;
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();

		if ( _rustleLoop.IsValid() ) _rustleLoop.Stop( 0.1f );
		if ( _drumrollLoop.IsValid() ) _drumrollLoop.Stop( 0.1f );
	}

	protected override void OnUpdate()
	{
		// Handle click detection via raycasting from camera
		if ( Input.Pressed( "attack1" ) )
		{
			var camera = Scene.Camera;
			if ( camera == null ) return;

			var ray = camera.ScreenPixelToRay( Mouse.Position );
			var tr = Scene.Trace.Ray( ray, 5000f )
				.WithTag( "crate" )
				.Run();

			if ( tr.Hit && tr.GameObject.Root == GameObject )
			{
				OnBumped( tr.HitPosition, tr.Normal );
			}
		}

		// Simulate spring physics for the tilt
		SimulateSpring();

		// Apply the tilt rotation, pivoting around the bottom edge of the box.
		// We rotate the center point around a pivot at the base.
		var tiltRotation = Rotation.FromPitch( _angularDisplacement.x )
			* Rotation.FromRoll( _angularDisplacement.y );

		// The pivot is at the bottom center of the box in local space
		var pivotOffset = _initialRotation * Vector3.Up * BoxHalfHeight;

		// Calculate where the center ends up after rotating around the bottom pivot
		var pivotPoint = _initialPosition - pivotOffset;
		var rotatedOffset = (_initialRotation * tiltRotation) * Vector3.Up * BoxHalfHeight;

		WorldRotation = _initialRotation * tiltRotation;
		WorldPosition = pivotPoint + rotatedOffset;

		// Check if a pending flap can now open
		float currentDelay = FlapOpenDelay + (MathF.Max( 0, _nextFlapIndex ) * FlapDelayEscalation);
		if ( _flapPending )
		{
			float elapsed = Time.Now - _lastFlapOpenTime;

			if ( elapsed >= currentDelay )
			{
				DoOpenNextFlap();
				EmitBurstParticles();
				_flapPending = false;
			}
			else
			{
				// Shake the box with increasing intensity as we approach the open moment
				// Progress 0->1 over the delay, intensity scales with flap index
				float progress = elapsed / currentDelay;
				float flapScale = (_nextFlapIndex + 1f) / Flaps.Count; // Later flaps shake more
				float intensity = PreOpenShakeIntensity * progress * progress * flapScale;

				// Gentle box shake
				var shakeDir = new Vector2(
					Game.Random.Float( -1f, 1f ),
					Game.Random.Float( -1f, 1f )
				).Normal;
				_angularVelocity += shakeDir * intensity * 15f;

				// Shake the next flap's pitch more visibly
				if ( _nextFlapIndex < Flaps.Count && Flaps[_nextFlapIndex] != null )
				{
					_flapVelocity[_nextFlapIndex] += Game.Random.Float( -1f, 1f ) * intensity * 80f;
				}
			}
		}

		// Simulate flap springs
		SimulateFlaps();

		// Fade rustle volume toward target, then decay target back to zero
		_rustleVolume = MathX.Lerp( _rustleVolume, _rustleTargetVolume, Time.Delta * 8f );
		_rustleTargetVolume = MathX.Lerp( _rustleTargetVolume, 0f, Time.Delta * 3f );

		if ( _rustleLoop.IsValid() )
		{
			_rustleLoop.Volume = _rustleVolume;
			_rustleLoop.Position = WorldPosition;
		}
	}

	private void OnBumped( Vector3 hitPosition, Vector3 hitNormal )
	{
		// Bump up the rustle volume on each click
		_rustleTargetVolume = MathF.Min( _rustleTargetVolume + 0.35f, 1f );

		// Start the drumroll on the first click (but not after fully open)
		if ( !_drumrollLoop.IsValid() && !IsFullyOpen )
		{
			_drumrollLoop = Sound.Play( "drumroll", WorldPosition );
			_drumrollLoop.Volume = 0.01f;
		}
		// Get hit point relative to box center, in local space
		var localHit = WorldTransform.PointToLocal( hitPosition );

		// Project onto XY plane (horizontal) and negate to push AWAY from click
		// Then map to tilt axes:
		//   localHit.x (forward) -> Roll (tips sideways)
		//   localHit.y (left)    -> Pitch (tips forward/back)
		var pushDir = new Vector2( -localHit.x, localHit.y ).Normal;

		// If the click was mostly on top/bottom, pick a random horizontal direction
		if ( pushDir.Length < 0.1f )
		{
			var angle = Game.Random.Float( 0f, MathF.PI * 2f );
			pushDir = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) );
		}

		_angularVelocity += pushDir * BumpImpulse;

		// Open the next flap in sequence (returns true if a flap actually opened)
		bool flapOpened = OpenNextFlap();

		// If no flap opened, reduce the bump impact significantly
		if ( !flapOpened )
		{
			_angularVelocity -= pushDir * BumpImpulse * 0.75f;
		}

		// Jolt already-open flaps so they feel alive
		JoltOpenFlaps();

		// Only burst particles when a flap opens
		if ( flapOpened )
		{
			EmitBurstParticles();
		}
	}

	private bool OpenNextFlap()
	{
		if ( Flaps == null || Flaps.Count == 0 ) return false;
		if ( _nextFlapIndex >= Flaps.Count ) return false;

		// If enough time has passed since the last flap, open immediately
		float currentDelay = FlapOpenDelay + (MathF.Max( 0, _nextFlapIndex ) * FlapDelayEscalation);
		if ( (Time.Now - _lastFlapOpenTime) >= currentDelay )
		{
			DoOpenNextFlap();
			return true;
		}
		else
		{
			// Queue it — will open once the delay elapses
			_flapPending = true;
			return false;
		}
	}

	private void DoOpenNextFlap()
	{
		if ( _nextFlapIndex >= Flaps.Count ) return;

		_flapTargetAngle[_nextFlapIndex] = Game.Random.Float( 150f, 170f );

		// Impulse the box in the direction the flap is opening (away from hinge)
		var flap = Flaps[_nextFlapIndex];
		if ( flap != null )
		{
			// Flap's local position relative to crate tells us which side it's on
			var localPos = flap.LocalPosition;
			var flapDir = new Vector2( -localPos.x, localPos.y ).Normal;
			_angularVelocity += flapDir * BumpImpulse;
		}

		_nextFlapIndex++;
		_lastFlapOpenTime = Time.Now;

		// Play flap open sound, pitched up one semitone per flap
		var flapSound = Sound.Play( "cardboard_flap_open", WorldPosition );
		flapSound.Pitch = MathF.Pow( 2f, (_nextFlapIndex - 1) / 12f );

		// Increase drumroll volume with each flap
		// Increase drumroll volume with each flap
		if ( _drumrollLoop.IsValid() && Flaps.Count > 0 && _nextFlapIndex < Flaps.Count )
		{
			_drumrollLoop.Volume = 0.01f + 0.2f * (_nextFlapIndex / (float)Flaps.Count);
		}

		// Enable finale particles and play box_open sound when the last flap opens
		if ( _nextFlapIndex >= Flaps.Count )
		{
			Sound.Play( "box_open", WorldPosition );

			// Fade out the rustle loop instead of hard-stopping (avoids cutting the flap sound)
			if ( _rustleLoop.IsValid() )
			{
				_rustleTargetVolume = 0f;
				_rustleVolume = 0f;
				_rustleLoop.Volume = 0f;
			}

			// Stop the drumroll on reveal and play the reveal sting
			if ( _drumrollLoop.IsValid() )
			{
				_drumrollLoop.Volume = 0f;
				_drumrollLoop.Stop();
				_drumrollLoop = default;
			}
			Sound.Play( "drumroll_reveal", WorldPosition );
			foreach ( var go in FinaleParticles )
			{
				if ( go != null )
					go.Enabled = true;
			}
		}
	}

	private void JoltOpenFlaps()
	{
		// Give a random velocity kick to any already-open flap
		for ( int i = 0; i < _nextFlapIndex && i < Flaps.Count; i++ )
		{
			if ( Flaps[i] == null ) continue;
			if ( _flapTargetAngle[i] <= 0f ) continue;

			// Small random impulse so each flap wobbles differently
			_flapVelocity[i] += Game.Random.Float( -500f, 500f );
		}
	}

	private void EmitBurstParticles()
	{
		if ( BurstParticles == null ) return;

		foreach ( var template in BurstParticles )
		{
			if ( template == null ) continue;

			// Clone the template at its original position/rotation and enable it
			var instance = template.Clone( new CloneConfig
			{
				Transform = template.WorldTransform,
				StartEnabled = true
			} );
			instance.NetworkMode = NetworkMode.Never;
		}
	}

	private void SimulateFlaps()
	{
		float dt = Time.Delta;

		// Track the active (shaking) flap's inward displacement to push neighbors
		float activeInwardAngle = 0f;
		if ( _nextFlapIndex < Flaps.Count && _flapTargetAngle[_nextFlapIndex] <= 0f )
		{
			activeInwardAngle = MathF.Min( _flapCurrentAngle[_nextFlapIndex], 0f );
		}

		for ( int i = 0; i < Flaps.Count; i++ )
		{
			if ( Flaps[i] == null ) continue;

			if ( _flapTargetAngle[i] <= 0f )
			{
				// Flap hasn't opened yet — but might be shaking from pre-open tension
				// Spring back toward 0 (closed) with any accumulated velocity
				if ( MathF.Abs( _flapVelocity[i] ) < 0.01f && MathF.Abs( _flapCurrentAngle[i] ) < 0.01f && i == _nextFlapIndex )
				{
					// Active flap with no motion, skip
				}
				else if ( i == _nextFlapIndex )
				{
					// This is the active shaking flap — simulate normally
					float springForce = -FlapSpringStiffness * _flapCurrentAngle[i] - FlapSpringDamping * _flapVelocity[i];
					_flapVelocity[i] += springForce * dt;
					_flapCurrentAngle[i] += _flapVelocity[i] * dt;
				}
				else
				{
					// Neighboring closed flap — bend sympathetically with the active flap's inward push
					float sympathyAngle = activeInwardAngle * 0.4f;
					float springForce = -FlapSpringStiffness * (_flapCurrentAngle[i] - sympathyAngle) - FlapSpringDamping * _flapVelocity[i];
					_flapVelocity[i] += springForce * dt;
					_flapCurrentAngle[i] += _flapVelocity[i] * dt;
				}

				Flaps[i].LocalRotation = _flapInitialRotation[i] * BuildFlapRotation( i, _flapCurrentAngle[i] );
				continue;
			}

			// Spring toward target angle
			float error = _flapTargetAngle[i] - _flapCurrentAngle[i];
			float force = FlapSpringStiffness * error - FlapSpringDamping * _flapVelocity[i];

			_flapVelocity[i] += force * dt;
			_flapCurrentAngle[i] += _flapVelocity[i] * dt;

			// Apply rotation on the detected axis relative to initial rotation
			Flaps[i].LocalRotation = _flapInitialRotation[i] * BuildFlapRotation( i, _flapCurrentAngle[i] );
		}

		// Rise the inner filling based on how many flaps are open (proportional)
		if ( InnerFilling != null && Flaps.Count > 0 )
		{
			float openProgress = _nextFlapIndex / (float)Flaps.Count;
			InnerFilling.LocalPosition = _fillingInitialLocalPos + Vector3.Up * FillingRiseHeight * openProgress;
		}
	}

	/// <summary>
	/// Determines which axis a flap rotates on and which direction it opens,
	/// based on its initial local angles. The axis with the largest absolute value
	/// is the hinge axis, and the sign tells us which way to open (opposite).
	/// </summary>
	private void DetermineFlapAxis( int index, Angles angles )
	{
		float absPitch = MathF.Abs( angles.pitch );
		float absYaw = MathF.Abs( angles.yaw );
		float absRoll = MathF.Abs( angles.roll );

		if ( absPitch >= absYaw && absPitch >= absRoll )
		{
			_flapAxis[index] = 0; // pitch
			_flapDirection[index] = angles.pitch > 0 ? -1f : 1f;
		}
		else if ( absYaw >= absPitch && absYaw >= absRoll )
		{
			_flapAxis[index] = 1; // yaw
			_flapDirection[index] = angles.yaw > 0 ? -1f : 1f;
		}
		else
		{
			_flapAxis[index] = 2; // roll
			_flapDirection[index] = angles.roll > 0 ? -1f : 1f;
		}
	}

	/// <summary>
	/// Builds a rotation for a flap on its detected axis and direction.
	/// </summary>
	private Rotation BuildFlapRotation( int index, float angle )
	{
		float directed = angle * _flapDirection[index];

		return _flapAxis[index] switch
		{
			0 => Rotation.FromPitch( directed ),
			1 => Rotation.FromYaw( directed ),
			2 => Rotation.FromRoll( directed ),
			_ => Rotation.Identity
		};
	}

	private void SimulateSpring()
	{
		float dt = Time.Delta;

		// Spring force pulls displacement back toward zero
		// F = -k*x - d*v (spring + damping)
		var springForce = -SpringStiffness * _angularDisplacement - SpringDamping * _angularVelocity;

		_angularVelocity += springForce * dt;
		_angularDisplacement += _angularVelocity * dt;

		// Clamp max displacement to prevent wild spinning
		float mag = _angularDisplacement.Length;
		if ( mag > MaxTiltAngle * 2f )
		{
			_angularDisplacement = _angularDisplacement.Normal * MaxTiltAngle * 2f;
		}
	}
}
