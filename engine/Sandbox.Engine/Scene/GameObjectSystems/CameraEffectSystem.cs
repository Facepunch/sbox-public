namespace Sandbox;

/// <summary>
/// Transient camera effects - screen shakes, punches and tilts. Fire and forget:
/// <c>CameraEffectSystem.Current.AddShake( Scene.Camera, 4, 40, 1 )</c>. The camera folds the
/// combined offsets in when it composes its view each tick - effects never touch the camera's
/// transform or the player's aim, so they can't accumulate or fight the game's camera code.
/// </summary>
public sealed class CameraEffectSystem : GameObjectSystem<CameraEffectSystem>
{
	/// <summary>
	/// Global multiplier on every camera effect - wire this to a screen shake preference. Zero
	/// disables them entirely.
	/// </summary>
	public float Scale { get; set; } = 1.0f;

	readonly List<BaseEffect> _effects = [];

	public CameraEffectSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, Tick, "TickCameraEffects" );
	}

	void Tick()
	{
		foreach ( var effect in _effects )
		{
			effect.Update( Time.Delta );
		}

		_effects.RemoveAll( x => x.IsDone );
	}

	/// <summary>
	/// Add a camera effect. A null <see cref="BaseEffect.Camera"/> applies to every camera.
	/// </summary>
	public T Add<T>( T effect ) where T : BaseEffect
	{
		_effects.Add( effect );
		return effect;
	}

	/// <summary>Remove every active effect.</summary>
	public void Clear() => _effects.Clear();

	/// <summary>
	/// The classic screen shake (Half-Life's env_shake) - every 1/<paramref name="frequency"/> seconds
	/// a new random offset within <paramref name="amplitude"/> world units, settling to nothing over
	/// <paramref name="duration"/>. A duration of zero shakes until <see cref="BaseEffect.Stop"/>.
	/// </summary>
	public ScreenShake AddShake( CameraComponent camera, float amplitude, float frequency, float duration )
	{
		return Add( new ScreenShake { Camera = camera, Amplitude = amplitude, Frequency = frequency, Duration = duration } );
	}

	/// <summary>
	/// A world shake with distance falloff - full <paramref name="amplitude"/> at
	/// <paramref name="position"/> fading to nothing at <paramref name="radius"/>. Affects every
	/// camera in range. Use for explosions.
	/// </summary>
	public ScreenShake AddShake( Vector3 position, float radius, float amplitude, float frequency, float duration )
	{
		return Add( new ScreenShake { Epicenter = position, Radius = radius, Amplitude = amplitude, Frequency = frequency, Duration = duration } );
	}

	/// <summary>
	/// A punch along a camera-local direction that oscillates and dies out - <see cref="Vector3.Up"/>
	/// bounces the view upward, <paramref name="frequency"/> scales how often it oscillates before
	/// it lapses.
	/// </summary>
	public BaseEffect AddPunch( CameraComponent camera, Vector3 direction, float amplitude, float frequency = 1f, float duration = 0.3f, float fovAmplitude = 0f )
	{
		return Add( new ScreenPunch { Camera = camera, Direction = direction, Amplitude = amplitude, Frequency = frequency, Duration = duration, FovAmplitude = fovAmplitude } );
	}

	/// <summary>
	/// An angular punch - the view kicks to <paramref name="angles"/> and bounces back,
	/// <paramref name="frequency"/> scaling how often it oscillates before it lapses. Melee swings,
	/// landings, launches.
	/// </summary>
	public BaseEffect AddPunch( CameraComponent camera, Angles angles, float frequency = 1f, float duration = 0.3f, float fovAmplitude = 0f )
	{
		return Add( new ScreenPunch { Camera = camera, Amplitude = 0f, AngleAmplitude = angles, Frequency = frequency, Duration = duration, FovAmplitude = fovAmplitude } );
	}

	/// <summary>
	/// Tilt the camera by <paramref name="angle"/>, eased in over <paramref name="easeTime"/> and back
	/// out before <paramref name="duration"/> ends.
	/// </summary>
	public BaseEffect AddTilt( CameraComponent camera, Angles angle, float duration, float easeTime = 0.2f )
	{
		return Add( new ScreenTilt { Camera = camera, Angle = angle, Duration = duration, EaseTime = easeTime } );
	}

	/// <summary>
	/// The combined effect offsets for this camera this frame - the position is camera-local
	/// (<see cref="Vector3.Forward"/> pushes the view forward). The camera reads these once per
	/// tick, when it composes its view.
	/// </summary>
	public void QueryOffsets( CameraComponent camera, out Vector3 position, out Angles angles, out float fieldOfView )
	{
		position = default;
		angles = default;
		fieldOfView = 0f;

		if ( _effects.Count == 0 || Scale <= 0f )
			return;

		foreach ( var effect in _effects )
		{
			var scale = effect.ScaleFor( camera ) * Scale;
			if ( scale <= 0f )
				continue;

			effect.Evaluate( scale, ref position, ref angles, ref fieldOfView );
		}
	}

	/// <summary>
	/// A transient camera effect, living in the <see cref="CameraEffectSystem"/>. Targets one
	/// <see cref="Camera"/>, every camera (null), or radiates from an <see cref="Epicenter"/> with
	/// distance falloff. Derive to make your own effects.
	/// </summary>
	public abstract class BaseEffect
	{
		/// <summary>The camera this applies to - null applies to every camera.</summary>
		public CameraComponent Camera { get; set; }

		/// <summary>
		/// When set, the effect radiates from this world position - full strength at the epicenter,
		/// nothing beyond <see cref="Radius"/>.
		/// </summary>
		public Vector3? Epicenter { get; set; }

		/// <summary>How far the effect reaches from <see cref="Epicenter"/>.</summary>
		public float Radius { get; set; } = 512f;

		/// <summary>Seconds the effect runs. Zero or less runs until <see cref="Stop"/>.</summary>
		public float Duration { get; set; } = 1f;

		/// <summary>Seconds this effect has been running.</summary>
		public float TimeAlive { get; private set; }

		/// <summary>Runs until <see cref="Stop"/> rather than expiring.</summary>
		public bool IsInfinite => Duration <= 0f;

		bool _stopped;

		/// <summary>
		/// True when the effect has expired or been stopped, and is about to be removed. An effect
		/// whose target camera has been destroyed is done too.
		/// </summary>
		public virtual bool IsDone => _stopped || (!IsInfinite && TimeAlive > Duration) || (Camera is not null && !Camera.IsValid());

		/// <summary>End the effect now.</summary>
		public void Stop() => _stopped = true;

		/// <summary>1 at the start of the effect's life falling to 0 at the end. Infinite effects hold 1.</summary>
		protected float Fraction => IsInfinite ? 1f : (1f - TimeAlive / Duration).Clamp( 0f, 1f );

		/// <summary>
		/// Advance the effect - called once a frame by the system.
		/// </summary>
		public virtual void Update( float delta )
		{
			TimeAlive += delta;
		}

		/// <summary>
		/// How strongly this effect applies to a camera - 0 when it targets a different camera, fading
		/// with distance from the <see cref="Epicenter"/> when one is set. Override for custom falloff.
		/// </summary>
		public virtual float ScaleFor( CameraComponent camera )
		{
			// A null target means every camera - a destroyed one doesn't.
			if ( Camera is not null && Camera != camera )
				return 0f;

			if ( Epicenter is { } epicenter )
				return 1f - (camera.WorldPosition.Distance( epicenter ) / MathF.Max( Radius, 1f )).Clamp( 0f, 1f );

			return 1f;
		}

		/// <summary>
		/// Accumulate this effect's contribution. <paramref name="position"/> is camera-local -
		/// <see cref="Vector3.Forward"/> pushes the view forward, <see cref="Vector3.Up"/> up.
		/// </summary>
		public abstract void Evaluate( float scale, ref Vector3 position, ref Angles angles, ref float fieldOfView );
	}

	/// <summary>
	/// The classic screen shake - a new random offset every 1/frequency seconds, blended by a
	/// squared-falloff sine envelope whose frequency ramps up as it dies, amplitude decaying as it
	/// goes. Position plus a little roll, never pitch or yaw. Half-Life's env_shake, byte for byte.
	/// </summary>
	public sealed class ScreenShake : BaseEffect
	{
		/// <summary>How far the view gets thrown, in world units. Settable while the shake runs.</summary>
		public float Amplitude { get; set; } = 4f;

		/// <summary>Direction changes per second - low is a slow lurch, high is a violent tremble.</summary>
		public float Frequency { get; set; } = 40f;

		float _untilNextShake;
		Vector3 _offset;
		float _roll;

		public override void Update( float delta )
		{
			base.Update( delta );

			_untilNextShake -= delta;

			if ( _untilNextShake <= 0f )
			{
				_untilNextShake = 1f / MathF.Max( Frequency, 0.01f );

				_offset = System.Random.Shared.VectorInCube( Amplitude );

				_roll = System.Random.Shared.Float( -Amplitude * 0.25f, Amplitude * 0.25f );
			}

			// Settle a little every frame - less for higher frequency shakes.
			if ( !IsInfinite )
				Amplitude -= Amplitude * delta / (Duration * MathF.Max( Frequency, 0.01f ));
		}

		public override void Evaluate( float scale, ref Vector3 position, ref Angles angles, ref float fieldOfView )
		{
			var fraction = Fraction;

			// The envelope falls off squared while its frequency ramps up - the classic settle.
			var frequency = fraction > 0f ? Frequency / fraction : 0f;
			fraction *= fraction * MathF.Sin( Time.Now * frequency );

			position += _offset * (fraction * scale);
			angles.roll += _roll * fraction * scale;
		}
	}

	/// <summary>
	/// A punch that oscillates and lapses out - <c>sin( t·3π·frequency )·(1 - t)</c> over the
	/// duration, along a camera-local direction and/or an angular kick, with an optional FOV punch.
	/// </summary>
	sealed class ScreenPunch : BaseEffect
	{
		public Vector3 Direction { get; set; } = Vector3.Up;
		public float Amplitude { get; set; } = 4f;
		public float Frequency { get; set; } = 1f;
		public Angles AngleAmplitude { get; set; }
		public float FovAmplitude { get; set; } = 0f;

		public override void Evaluate( float scale, ref Vector3 position, ref Angles angles, ref float fieldOfView )
		{
			// y = sin( f·x )·(1 - x/3π), the lifetime mapped over 0..3π.
			var t = 1f - Fraction;
			var y = MathF.Sin( t * 3f * MathF.PI * Frequency ) * Fraction;

			position += Direction * (Amplitude * y * scale);
			angles += AngleAmplitude * (y * scale);
			fieldOfView += FovAmplitude * y * scale;
		}
	}

	/// <summary>
	/// Tilts the camera to an angle and back - eased in over EaseTime, held, then eased back out
	/// before the duration ends.
	/// </summary>
	sealed class ScreenTilt : BaseEffect
	{
		public Angles Angle { get; set; } = new( 0f, 0f, 15f );
		public float EaseTime { get; set; } = 0.2f;

		public override void Evaluate( float scale, ref Vector3 position, ref Angles angles, ref float fieldOfView )
		{
			var ease = MathF.Max( EaseTime, 0.001f );
			var interp = TimeAlive / ease;

			// Ease back out as the end approaches.
			if ( !IsInfinite )
				interp = MathF.Min( interp, (Duration - TimeAlive) / ease );

			interp = Sandbox.Utility.Easing.SineEaseInOut( interp.Clamp( 0f, 1f ) );

			angles += Angle * (interp * scale);
		}
	}
}
