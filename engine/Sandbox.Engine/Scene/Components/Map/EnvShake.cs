namespace Sandbox.Mapping;

/// <summary>
/// Shakes players' screens - and optionally physics objects - around a point. Half-Life's
/// env_shake. Call <see cref="StartShake"/> to set it off on every player, and it runs a
/// <see cref="Duration"/> second cycle, the curves shaping it over that time. <see cref="Loop"/>
/// repeats the cycle until turned off.
/// </summary>
[EditorHandle( Icon = "vibration" )]
[Category( "Mapping" ), Alias( "env_shake" ), Icon( "vibration" )]
public sealed class EnvShake : Component, Component.ITemporaryEffect
{
	/// <summary>
	/// Is the shake running? Local to this machine - map-placed shakes start everywhere because the
	/// scene ships with it set, but toggling it at runtime only affects this peer. Use
	/// <see cref="StartShake"/>/<see cref="StopShake"/> to run one on every player. A non-looping
	/// shake turns itself off when its cycle ends.
	/// </summary>
	[Property]
	public bool On { get; set; } = true;

	/// <summary>Restart the cycle when it ends, shaking until turned off.</summary>
	[Property]
	public bool Loop { get; set; }

	/// <summary>Seconds one shake cycle lasts - the curves play out over this.</summary>
	[Property]
	public float Duration { get; set; } = 1f;

	/// <summary>How far the view gets thrown over the cycle, in world units.</summary>
	[Property]
	public ParticleFloat Amplitude { get; set; } = 4f;

	/// <summary>
	/// Direction changes per second over the cycle - low is a slow lurch, high is a violent tremble.
	/// </summary>
	[Property]
	public ParticleFloat Frequency { get; set; } = 40f;

	/// <summary>
	/// Overall strength over the cycle - fade the whole shake in or out without retuning the others.
	/// </summary>
	[Property]
	public ParticleFloat Scale { get; set; } = 1f;

	/// <summary>
	/// How far the shake reaches, fading to nothing at the edge. The view ignores this when
	/// <see cref="GlobalShake"/> is set - physics always uses it.
	/// </summary>
	[Property]
	public float Radius { get; set; } = 512f;

	/// <summary>Shake every player in the scene at full strength, no matter where they are.</summary>
	[Property]
	public bool GlobalShake { get; set; }

	/// <summary>Shake the view. Turn off for a physics-only shake.</summary>
	[Property]
	public bool ShakeView { get; set; } = true;

	/// <summary>Throw physics objects around within <see cref="Radius"/> while shaking.</summary>
	[Property]
	public bool ShakePhysics { get; set; }

	/// <summary>Rumble controllers along with the shake.</summary>
	[Property]
	public bool Rumble { get; set; } = true;

	Effect _shake;
	bool _running;
	float _cycleTime;
	float _amplitudeNow;
	float _frequencyNow;
	float _seedAmplitude;
	float _seedFrequency;
	float _seedScale;
	TimeUntil _nextPhysicsKick;
	TimeUntil _nextRumble;

	/// <summary>Set the shake off on everyone, restarting the cycle if it's already running.</summary>
	[Rpc.Broadcast]
	public void StartShake()
	{
		On = true;
		_cycleTime = 0f;
	}

	/// <summary>Stop a running shake on everyone.</summary>
	[Rpc.Broadcast]
	public void StopShake() => On = false;

	// While shaking, keep temporary effect objects (explosion prefabs) alive. Asked to die, stop
	// looping so the current cycle is the last.
	bool Component.ITemporaryEffect.IsActive => On;
	void Component.ITemporaryEffect.DisableLooping() => Loop = false;

	protected override void OnUpdate()
	{
		// On is the whole state machine - toggled from the editor, game code or the RPCs above.
		if ( !On )
		{
			StopRunning();
			return;
		}

		if ( !_running )
			StartRunning();

		// ShakeView can flip while running - keep the view effect in step.
		if ( ShakeView && _shake is null )
		{
			_shake = Scene.GetSystem<CameraEffectSystem>().Add( new Effect { Source = this, Duration = 0f } );
		}
		else if ( !ShakeView && _shake is not null )
		{
			_shake.Stop();
			_shake = null;
		}

		_cycleTime += Time.Delta;

		var duration = MathF.Max( Duration, 0.01f );
		if ( _cycleTime >= duration )
		{
			if ( Loop )
			{
				_cycleTime %= duration;
				RollSeeds();
			}
			else
			{
				On = false;
				StopRunning();
				return;
			}
		}

		// Sample the values at this point in the cycle - the view effect and physics read the results.
		var t = _cycleTime / duration;
		var scale = Scale.Evaluate( t, _seedScale );
		_amplitudeNow = Amplitude.Evaluate( t, _seedAmplitude ) * scale;
		_frequencyNow = Frequency.Evaluate( t, _seedFrequency );

		if ( Rumble && _nextRumble )
		{
			_nextRumble = 0.25f;
			RumbleController();
		}
	}

	protected override void OnDisabled()
	{
		StopRunning();
	}

	void StartRunning()
	{
		_running = true;
		_cycleTime = 0f;
		_nextRumble = 0f;
		_nextPhysicsKick = 0f;
		RollSeeds();
	}

	// Seeded and Range values hold steady within a cycle and re-roll on the next one, like a
	// particle's per-particle random.
	void RollSeeds()
	{
		_seedAmplitude = Random.Shared.Float();
		_seedFrequency = Random.Shared.Float();
		_seedScale = Random.Shared.Float();
	}

	void StopRunning()
	{
		_running = false;
		_shake?.Stop();
		_shake = null;
	}

	protected override void OnFixedUpdate()
	{
		if ( !_running )
			return;

		if ( !ShakePhysics || !Networking.IsHost )
			return;

		if ( !_nextPhysicsKick )
			return;

		_nextPhysicsKick = 1f / MathF.Max( _frequencyNow, 0.01f );

		KickPhysics();
	}

	/// <summary>
	/// Kick every physics object in the radius in a random, upward leaning direction - strongest
	/// at the epicenter, fading to nothing at the edge.
	/// </summary>
	void KickPhysics()
	{
		var sphere = new Sphere( WorldPosition, Radius );

		foreach ( var go in Scene.FindInPhysics( sphere ) )
		{
			foreach ( var rb in go.GetComponents<Rigidbody>() )
			{
				if ( rb.IsProxy || !rb.MotionEnabled )
					continue;

				var falloff = 1f - (rb.WorldPosition.Distance( WorldPosition ) / MathF.Max( Radius, 1f )).Clamp( 0f, 1f );
				if ( falloff <= 0f )
					continue;

				var direction = new Vector3(
					Random.Shared.Float( -1f, 1f ),
					Random.Shared.Float( -1f, 1f ),
					Random.Shared.Float( 0f, 1f ) ).Normal;

				rb.ApplyImpulse( direction * (rb.Mass * _amplitudeNow * falloff) );
			}
		}
	}

	void RumbleController()
	{
		var strength = (_amplitudeNow / 16f).Clamp( 0f, 1f ) * ScaleForLocalCamera();
		if ( strength <= 0f )
			return;

		Input.TriggerHaptics( strength, strength, duration: 300 );
	}

	/// <summary>Distance falloff for the local camera - 1 at the epicenter, 0 at the radius.</summary>
	float ScaleForLocalCamera()
	{
		if ( GlobalShake )
			return 1f;

		var camera = Scene.Camera;
		if ( !camera.IsValid() )
			return 0f;

		return 1f - (camera.WorldPosition.Distance( WorldPosition ) / MathF.Max( Radius, 1f )).Clamp( 0f, 1f );
	}

	/// <summary>
	/// The view shake - a new random direction every 1/frequency seconds, with the component's
	/// sampled amplitude applied every frame so the curves stay smooth between picks.
	/// </summary>
	sealed class Effect : CameraEffectSystem.BaseEffect
	{
		public EnvShake Source { get; init; }

		float _untilNextOffset;
		Vector3 _offset;
		float _roll;

		public override bool IsDone => base.IsDone || !Source.IsValid();

		public override float ScaleFor( CameraComponent camera )
		{
			if ( !Source.IsValid() )
				return 0f;

			if ( Source.GlobalShake )
				return 1f;

			return 1f - (camera.WorldPosition.Distance( Source.WorldPosition ) / MathF.Max( Source.Radius, 1f )).Clamp( 0f, 1f );
		}

		public override void Update( float delta )
		{
			base.Update( delta );

			_untilNextOffset -= delta;
			if ( _untilNextOffset > 0f )
				return;

			_untilNextOffset = 1f / MathF.Max( Source._frequencyNow, 0.01f );

			_offset = System.Random.Shared.VectorInCube( 1f );
			_roll = System.Random.Shared.Float( -0.25f, 0.25f );
		}

		public override void Evaluate( float scale, ref Vector3 position, ref Angles angles, ref float fieldOfView )
		{
			position += _offset * (Source._amplitudeNow * scale);
			angles.roll += _roll * Source._amplitudeNow * scale;
		}
	}

	protected override void DrawGizmos()
	{
		if ( !Gizmo.IsSelected )
			return;

		Gizmo.Draw.LineSphere( new Sphere( 0, Radius ), 16 );
	}
}
