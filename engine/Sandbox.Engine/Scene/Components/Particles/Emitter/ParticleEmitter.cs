namespace Sandbox;

/// <summary>
/// Creates particles. Should be attached to a <see cref="ParticleEffect"/>.
/// </summary>
public abstract class ParticleEmitter : Component, Component.ExecuteInEditor, Component.ITemporaryEffect
{
	/// <summary>
	/// Whether the emitter should restart after finishing
	/// </summary>
	[Property, Group( "Emitter" )] public bool Loop { get; set; } = true;

	/// <summary>
	/// Whether to destroy the GameObject when the emitter finishes (only applies when Loop is false)
	/// </summary>
	[Property, Group( "Emitter" )] public bool DestroyOnEnd { get; set; } = false;

	/// <summary>
	/// How long the emitter should run for, after the Delay
	/// </summary>
	[Property, Group( "Emitter" )] public ParticleFloat Duration { get; set; } = 10.0f;

	/// <summary>
	/// How many seconds to wait before the emitter starts
	/// </summary>
	[Property, Group( "Emitter" )] public ParticleFloat Delay { get; set; } = 0.0f;

	/// <summary>
	/// How many particles to emit, in a burst
	/// </summary>
	[Property, Range( 0, 1000 ), Group( "Emitter" ), Title( "Initial Burst" )] public ParticleFloat Burst { get; set; } = 100.0f;

	/// <summary>
	/// How many particles to emit over time
	/// </summary>
	[Property, Range( 0, 1000 ), Group( "Emitter" )] public ParticleFloat Rate { get; set; } = 0.0f;

	/// <summary>
	/// How many particles to emit per 100 units moved
	/// </summary>
	[Property, Range( 0, 1000 ), Group( "Emitter" )] public ParticleFloat RateOverDistance { get; set; } = 0.0f;

	/// <summary>
	/// 0-1, the life time of the emitter
	/// </summary>
	public float Delta { get; private set; }


	/// <summary>
	/// True if we're doing a burst
	/// </summary>
	public bool IsBursting { get; private set; }

	/// <summary>
	/// 0-1, a random number to be used for this loop of the emitter
	/// </summary>
	public float EmitRandom { get; private set; }

	public float time;
	float emitted;
	bool burstPending;
	bool suspended;

	float evaluatedDuration;
	float evaluatedDelay;
	float evaluatedBurst;
	protected float evaluatedRateOverDistance;

	ParticleEffect target;

	protected override void OnEnabled()
	{
		suspended = false;

		ResetEmitter();

		target = Components.GetInAncestorsOrSelf<ParticleEffect>();
		if ( target is not null )
		{
			target.OnPreStep += OnParticleStep;
		}
		else
		{
			Log.Warning( $"No particle effect found for {this}" );
		}

		lastPos = null;
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		if ( target is not null )
		{
			target.OnPreStep -= OnParticleStep;
		}

		target = null;
	}

	public void ResetEmitter()
	{
		emitted = 0;
		time = 0;
		EmitRandom = Random.Shared.Float( 0, 1 );
		burstPending = true;

		evaluatedDuration = Duration.Evaluate( 0, Random.Shared.Float( 0, 1 ) );
		evaluatedDelay = Delay.Evaluate( 0, Random.Shared.Float( 0, 1 ) );
		evaluatedBurst = Burst.Evaluate( 0, Random.Shared.Float( 0, 1 ) );
	}

	bool IsStarted => time - evaluatedDelay >= 0;
	bool IsFinished => !burstPending && time > (evaluatedDuration + evaluatedDelay);

	void OnParticleStep( float delta )
	{
		if ( !target.IsValid() ) return;
		if ( !target.Active ) return;
		if ( suspended ) return;

		time += delta;

		float runTime = time - evaluatedDelay;
		Delta = 0;

		// not started yet
		if ( !IsStarted )
			return;

		if ( IsFinished )
		{
			if ( !Loop )
			{
				if ( Scene.IsEditor && !GameObject.HasFlagOrParent( GameObjectFlags.NotSaved ) )
				{
					// TODO - if this is selected
					ResetEmitter();
				}

				if ( DestroyOnEnd && !Scene.IsEditor && target.Particles.Count == 0 )
				{
					GameObject.Destroy();
				}

				return;
			}

			ResetEmitter();
			return;
		}

		if ( burstPending )
		{
			burstPending = false;
			var burstCount = GetBurstCount();
			if ( burstCount > 0 )
			{
				IsBursting = true;
				OnBurst();
				IsBursting = false;
			}
		}

		Delta = time.Remap( evaluatedDelay, evaluatedDuration + evaluatedDelay, 0, 1 );

		evaluatedRateOverDistance = RateOverDistance.Evaluate( Delta, EmitRandom );
		if ( evaluatedRateOverDistance > 0 )
		{
			EmitOverDistance();
		}

		float targetEmission = GetRateCount() * runTime;
		while ( !target.IsFull && emitted < targetEmission )
		{
			emitted++;
			Emit( target );
		}
	}

	public abstract bool Emit( ParticleEffect target );

	/// <summary>
	/// Allows child emitters to override how many particles are in a burst
	/// </summary>
	/// <returns></returns>
	protected virtual int GetBurstCount()
	{
		return (int)evaluatedBurst;
	}

	/// <summary>
	/// Allows child emitters to override how many particles are in a rate
	/// </summary>
	/// <returns></returns>
	protected virtual int GetRateCount()
	{
		return (int)Rate.Evaluate( Delta, EmitRandom );
	}

	protected virtual void OnBurst()
	{
		var burstCount = GetBurstCount();

		for ( int i = 0; i < burstCount; i++ )
		{
			if ( target.IsFull ) return;
			Delta = (float)i / (float)burstCount;
			Emit( target );
		}
	}

	Vector3? lastPos;
	float distanceTravelled;

	protected virtual void EmitOverDistance()
	{
		var pos = WorldPosition;

		if ( !lastPos.HasValue )
		{
			lastPos = pos;
			return;
		}

		var delta = (lastPos.Value - pos).Length;
		var particlePerUnit = 100.0f / evaluatedRateOverDistance;
		lastPos = pos;

		distanceTravelled += delta;

		while ( distanceTravelled > particlePerUnit )
		{
			distanceTravelled -= particlePerUnit;

			if ( !target.IsFull )
			{
				Emit( target );
			}
		}
	}

	/// <summary>
	/// Return true if we haven't finished emitting
	/// </summary>
	bool Component.ITemporaryEffect.IsActive
	{
		get
		{
			if ( suspended ) return false;
			if ( Loop ) return true;
			if ( burstPending ) return true;
			if ( !IsStarted ) return true;
			if ( IsFinished ) return false;
			if ( !target.IsValid() ) return false;

			return false;
		}
	}

	void ITemporaryEffect.DisableLooping()
	{
		suspended = true;
	}
}
