namespace Sandbox;

/// <summary>
/// Ticks the physics in FrameStage.PhysicsStep
/// </summary>
[Expose]
sealed class ParticleGameSystem : GameObjectSystem
{
	private List<ParticleEffect.ParticleWork> workList = new( 64 );

	public ParticleGameSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishUpdate, 0, UpdateParticles, "UpdateParticles" );
	}

	void UpdateParticles()
	{
		using var __ = PerformanceStats.Timings.Particles.Scope();

		var particles = Scene.GetAll<ParticleEffect>();
		if ( particles.Count() == 0 ) return;

		var timeDelta = MathX.Clamp( Time.Delta, 0.0f, 1.0f / 30.0f );
		var realTimeDelta = MathX.Clamp( RealTime.Delta, 0.0f, 1.0f / 30.0f );

		workList.Clear();

		foreach ( var p in particles )
		{
			var delta = p.Timing switch
			{
				ParticleEffect.TimingMode.GameTime => timeDelta,
				ParticleEffect.TimingMode.RealTime => realTimeDelta,
				_ => timeDelta // default to GameTime
			};

			p.TryPreWarm();
			p.PreStep( delta );
			p.CollectWork( workList );
		}

		if ( workList.Count > 0 )
		{
			System.Threading.Tasks.Parallel.ForEach( workList, ProcessWork );
		}

		foreach ( var p in particles )
		{
			p.SpawnDeferredParticleCollisionPrefabs();
			p.ApplyDeferredParticleForces();
			p.PostStep();
		}

		workList.Clear();
	}

	/// <summary>
	/// We process the particles in chunks, in parallel. We don't do one particle at a time because 
	/// it'd spend more time doing all the admin of giving them to threads than it would actually take.
	/// </summary>
	/// <param name="work"></param>
	private void ProcessWork( ParticleEffect.ParticleWork work )
	{
		for ( int i = work.startIndex; i < work.endIndex; i++ )
		{
			work.effect.UpdateParticle( i );
		}
	}
}
