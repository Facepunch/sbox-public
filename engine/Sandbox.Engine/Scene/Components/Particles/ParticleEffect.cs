using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Defines and holds particles. This is the core of the particle system.
/// </summary>
[Expose]
[Title( "Particle Effect" )]
[Category( "Particles" )]
[Icon( "shower" )]
[EditorHandle( "materials/gizmo/particles.png" )]
public sealed partial class ParticleEffect : Component, Component.ExecuteInEditor, Component.ITemporaryEffect, Component.ITintable
{
	/// <summary>
	/// The maximum number of particles that can exist in this effect at once.
	/// </summary>
	[Property, Header( "Limits" )]
	public int MaxParticles { get; set; } = 1000;

	/// <summary>
	/// The lifetime of each particle, in seconds.
	/// </summary>
	[Property]
	public ParticleFloat Lifetime { get; set; } = 1.0f;

	/// <summary>
	/// Scales the simulation time for this effect.
	/// </summary>
	[Property, Header( "Time" ), Range( 0, 1 )]
	public float TimeScale { get; set; } = 1.0f;

	/// <summary>
	/// How many seconds to pre-warm this effect by when creating.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float PreWarm { get; set; } = 0.0f;

	/// <summary>
	/// The delay before a particle starts after being emitted, in seconds.
	/// </summary>
	[Property]
	public ParticleFloat StartDelay { get; set; } = 0.0f;

	/// <summary>
	/// Per-particle time scale multiplier. Allows each particle to have a unique simulation speed.
	/// </summary>
	[Property]
	public ParticleFloat PerParticleTimeScale { get; set; } = 1.0f;

	public enum TimingMode
	{
		/// <summary>
		/// Use game simulation time (affected by game time scale).
		/// </summary>
		GameTime,

		/// <summary>
		/// Use real-world time (ignores game time scale).
		/// </summary>
		RealTime,
	}

	/// <summary>
	/// How time is updated for this effect.
	/// </summary>
	[Property]
	public TimingMode Timing { get; set; } = TimingMode.GameTime;

	/// <summary>
	/// The initial velocity of the particle when it is created. This is applied before any forces are applied.
	/// </summary>
	[Property, Feature( "Move" )]
	public ParticleVector3 InitialVelocity { get; set; }

	/// <summary>
	/// Apply an element of random velocity to the particle when it is created, in a random direction.
	/// </summary>
	[Title( "Random Velocity" )]
	[Property, Feature( "Move", Icon = "animation", Description = "The spatial properties of each particle" )]
	public ParticleFloat StartVelocity { get; set; } = 0.0f;

	/// <summary>
	/// The damping factor applied to particle velocity over time.
	/// This reduces the velocity of particles, simulating resistance or drag.
	/// </summary>
	[Property, Feature( "Move" )]
	public ParticleFloat Damping { get; set; } = 0.0f;

	/// <summary>
	/// Move this delta constantly. Ignores velocity, collisions and drag.
	/// </summary>
	[Property, Feature( "Move" )]
	public ParticleVector3 ConstantMovement { get; set; }

	[Hide, JsonIgnore]
	[Obsolete( "Use LocalSpace instead" )]
	[Property, Feature( "Move" )]
	public SimulationSpace Space { get; set; }

	/// <summary>
	/// When 1 particles will be moved in local space relative to the emitter GameObject's transform. 
	/// This allows particles to be emitted in a local space, like a fire effect that moves with the player, but the particles can slowly move to world space.
	/// </summary>
	[Property, Feature( "Move" )]
	public ParticleFloat LocalSpace { get; set; } = 0.0f;

	/// <summary>
	/// Enables or disables rotation for particles.
	/// </summary>
	[Property, FeatureEnabled( "Rotation", Icon = "flip_camera_android", Description = "The rotation of the particles (not the emitter)" )]
	public bool ApplyRotation { get; set; } = false;

	/// <summary>
	/// The pitch rotation of the particles.
	/// </summary>
	[Property, Feature( "Rotation" )]
	public ParticleFloat Pitch { get; set; } = 0.0f;

	/// <summary>
	/// The yaw rotation of the particles.
	/// </summary>
	[Property, Feature( "Rotation" )]
	public ParticleFloat Yaw { get; set; } = 0.0f;

	/// <summary>
	/// The roll rotation of the particles.
	/// </summary>
	[Property, Feature( "Rotation" )]
	public ParticleFloat Roll { get; set; } = 0.0f;

	/// <summary>
	/// Enables or disables color application for particles.
	/// </summary>
	[Property, FeatureEnabled( "Color", Icon = "color_lens", Description = "The visual properties of each particle" )]
	public bool ApplyColor { get; set; } = false;

	/// <summary>
	/// Enables or disables alpha application for particles.
	/// </summary>
	[Property, Feature( "Color" )]
	public bool ApplyAlpha { get; set; } = false;

	/// <summary>
	/// The tint color applied to particles.
	/// </summary>
	[Property, Feature( "Color" )]
	public Color Tint { get; set; } = Color.White;

	/// <summary>
	/// The gradient used to color particles over their lifetime.
	/// </summary>
	[Property, Feature( "Color" )]
	public ParticleGradient Gradient { get; set; } = Color.White;

	/// <summary>
	/// The brightness multiplier applied to particles.
	/// </summary>
	[Property, Feature( "Color" )]
	public ParticleFloat Brightness { get; set; } = 1.0f;

	/// <summary>
	/// The alpha transparency of particles.
	/// </summary>
	[Property, Feature( "Color" )]
	public ParticleFloat Alpha { get; set; } = 1.0f;

	/// <summary>
	/// Enables or disables shape application for particles.
	/// </summary>
	[Property, FeatureEnabled( "Shape", Icon = "crop", Description = "The scale/size of each particle" )]
	public bool ApplyShape { get; set; } = false;

	/// <summary>
	/// The scale of particles.
	/// </summary>
	[Property, Feature( "Shape" )]
	public ParticleFloat Scale { get; set; } = 1.0f;

	/// <summary>
	/// The stretch factor of particles, affecting their aspect ratio.
	/// </summary>
	[Property, Feature( "Shape" )]
	public ParticleFloat Stretch { get; set; } = 0.0f;

	/// <summary>
	/// Enables or disables the application of forces to particles.
	/// </summary>
	[Property, FeatureEnabled( "Force", Icon = "cloud_sync", Description = "A force that is applied to each particle in the effect" )]
	public bool Force { get; set; }

	/// <summary>
	/// The direction of the force applied to particles.
	/// </summary>
	[Property, Feature( "Force" )]
	public Vector3 ForceDirection { get; set; }

	/// <summary>
	/// The scale of the force applied to each particle.
	/// This multiplier determines the intensity of the force applied to particles.
	/// </summary>
	[Property, Feature( "Force" )]
	public ParticleFloat ForceScale { get; set; } = 1.0f;

	/// <summary>
	/// The orbital force applied to particles, causing them to rotate around a point.
	/// </summary>
	[Property, Feature( "Force" )]
	public ParticleVector3 OrbitalForce { get; set; }

	/// <summary>
	/// The pull strength of the orbital force, drawing particles closer to the center.
	/// </summary>
	[Property, Feature( "Force" )]
	public ParticleFloat OrbitalPull { get; set; }

	/// <summary>
	/// The simulation space in which forces are applied.
	/// Forces can be applied in either local space (relative to the emitter) or world space.
	/// </summary>
	[Title( "Space" )]
	[Property, Feature( "Force" )]
	public SimulationSpace ForceSpace { get; set; }

	/// <summary>
	/// Enables or disables collision behavior for particles.
	/// </summary>
	[Property, FeatureEnabled( "Collision", Icon = "file_download", Description = "What should happen when particles collide with the world" )]
	public bool Collision { get; set; }

	/// <summary>
	/// The chance that a particle will die upon collision.
	/// </summary>
	[Property, Feature( "Collision" )]
	public ParticleFloat DieOnCollisionChance { get; set; } = 0.0f;

	/// <summary>
	/// The radius used for collision detection.
	/// </summary>
	[Property, Feature( "Collision" )]
	public float CollisionRadius { get; set; } = 1.0f;

	/// <summary>
	/// The set of tags to ignore during collision detection.
	/// </summary>
	[Property, Feature( "Collision" )]
	public TagSet CollisionIgnore { get; set; }

	/// <summary>
	/// The bounce factor applied to particles upon collision.
	/// </summary>
	[Property, Feature( "Collision" )]
	public ParticleFloat Bounce { get; set; } = 1.0f;

	/// <summary>
	/// The friction factor applied to particles upon collision.
	/// </summary>
	[Property, Feature( "Collision" )]
	public ParticleFloat Friction { get; set; } = 1.0f;

	/// <summary>
	/// The bumpiness factor applied to particles upon collision.
	/// </summary>
	[Property, Feature( "Collision" )]
	public ParticleFloat Bumpiness { get; set; } = 0.0f;

	/// <summary>
	/// The strength of the push force applied to particles upon collision.
	/// </summary>
	[Property, Feature( "Collision" )]
	public ParticleFloat PushStrength { get; set; } = 0.0f;

	/// <summary>
	/// Enables or disables the use of a sheet sequence for particles.
	/// </summary>
	[Title( "Sheet" )]
	[Property, FeatureEnabled( "SheetSequence", Icon = "apps" )]
	public bool SheetSequence { get; set; }

	/// <summary>
	/// Which sequence to use.
	/// </summary>
	[Property, Feature( "SheetSequence" )]
	public ParticleFloat SequenceId { get; set; } = 0.0f;

	/// <summary>
	/// Allows control of the sequence time, which spans from 0 to 1 for one loop.
	/// </summary>
	[Property, Feature( "SheetSequence" ), Range( 0, 1 )]
	public ParticleFloat SequenceTime { get; set; } = 1.0f;

	/// <summary>
	/// Increment the sequence time by this much.
	/// </summary>
	[Property, Feature( "SheetSequence" )]
	public ParticleFloat SequenceSpeed { get; set; } = 1.0f;

	/// <summary>
	/// When enabled, snap to the nearest whole frame instead of blending between frames.
	/// </summary>
	[Property, Feature( "SheetSequence" ), Title( "Snap To Frame" )]
	public bool SnapToFrame { get; set; } = false;

	/// <summary>
	/// Enables or disables the use of prefabs for particles.
	/// </summary>
	[Property, FeatureEnabled( "Prefab", Icon = "widgets", Description = "Attach a prefab to a particle" )]
	public bool UsePrefabFeature { get; set; } = false;

	/// <summary>
	/// Will choose a random prefab to spawn from this list.
	/// </summary>
	[Header( "Follower" )]
	[Property, Feature( "Prefab" )]
	public List<GameObject> FollowerPrefab { get; set; }

	/// <summary>
	/// If 1 then we'll always spawn a prefab. If 0.5 then we'll spawn a prefab 50% of the time.
	/// </summary>
	[Property, Feature( "Prefab" ), Title( "Spawn Chance" )]
	public ParticleFloat FollowerPrefabChance { get; set; } = 1;

	/// <summary>
	/// When true the prefab will be destroyed at the end of the particle's life.
	/// </summary>
	[Property, Feature( "Prefab" ), Title( "Kill on death" )]
	public bool FollowerPrefabKill { get; set; } = true;

	/// <summary>
	/// Will choose a random prefab to spawn from this list.
	/// </summary>
	[Header( "On Collision" )]
	[Property, Feature( "Prefab" )]
	public List<GameObject> CollisionPrefab { get; set; }

	/// <summary>
	/// When true the collision prefab will be aligned with the surface it collides with.
	/// </summary>
	[Property, Feature( "Prefab" ), Title( "Align With Surface" )]
	public bool CollisionPrefabAlign { get; set; }

	/// <summary>
	/// We will by default align to the particle's angle, but we can also randomize that.
	/// </summary>
	[Property, Feature( "Prefab" ), Title( "Rotation" ), ShowIf( "CollisionPrefabAlign", true )]
	public ParticleFloat CollisionPrefabRotation { get; set; }

	/// <summary>
	/// If 1 then we'll always spawn a prefab. If 0.5 then we'll spawn a prefab 50% of the time.
	/// </summary>
	[Property, Feature( "Prefab" ), Title( "Spawn Chance" )]
	public ParticleFloat CollisionPrefabChance { get; set; } = 1;

	/// <summary>
	/// Called any time a particle is destroyed.
	/// </summary>
	[Property, Header( "Actions" )]
	public Action<Particle> OnParticleDestroyed { get; set; }

	/// <summary>
	/// Called any time a particle is created.
	/// </summary>
	[Property]
	public Action<Particle> OnParticleCreated { get; set; }

	/// <summary>
	/// Active particles in the effect.
	/// Active particles are those currently being simulated and rendered.
	/// </summary>
	public List<Particle> Particles { get; } = new List<Particle>();

	/// <summary>
	/// Delayed particles in the effect.
	/// Delayed particles are those that have been emitted but are waiting to be activated based on their start delay.
	/// </summary>
	public List<Particle> DelayedParticles { get; } = new List<Particle>();

	/// <summary>
	/// The total number of particles in the effect, including both active and delayed particles.
	/// </summary>
	public int ParticleCount => Particles.Count + DelayedParticles.Count;

	/// <summary>
	/// Whether the particle effect has reached its maximum capacity.
	/// This is determined by comparing the total particle count to the <see cref="MaxParticles"/> property.
	/// </summary>
	public bool IsFull => ParticleCount >= MaxParticles;

	/// <summary>
	/// Whether the particle simulation is currently paused.
	/// When paused, particles will not update their positions, velocities, or other properties.
	/// </summary>
	[JsonIgnore]
	public bool Paused { get; set; }

	Transform lastTransform;

	ConcurrentQueue<Particle> deleteList = new ConcurrentQueue<Particle>();

	/// <summary>
	/// Called before the particles are stepped.
	/// This allows custom logic to be executed before the simulation advances.
	/// </summary>
	public Action<float> OnPreStep { get; set; }

	/// <summary>
	/// Called after the particles are stepped.
	/// This allows custom logic to be executed after the simulation advances.
	/// </summary>
	public Action<float> OnPostStep { get; set; }

	/// <summary>
	/// Called after each particle is stepped.
	/// This provides an opportunity to modify individual particles during the simulation.
	/// </summary>
	public Action<Particle, float> OnStep { get; set; }

	/// <summary>
	/// The bounding box that encompasses all active particles.
	/// This is useful for determining the spatial extent of the particle effect.
	/// </summary>
	public BBox ParticleBounds { get; internal set; }

	/// <summary>
	/// The size of the largest particle in the effect.
	/// This is determined by the maximum scale of any particle along its x, y, or z axis.
	/// </summary>
	public float MaxParticleSize { get; internal set; }

	Color ITintable.Color { get => Tint; set => Tint = value; }

	List<GameObject> _spawnedGameObjects = new();

	public enum SimulationSpace
	{
		/// <summary>
		/// Forces are applied in world space, independent of the emitter's position or rotation.
		/// </summary>
		World,

		/// <summary>
		/// Forces are applied in local space, relative to the emitter's position and rotation.
		/// </summary>
		Local
	}

	bool isWarmed;

	protected override void OnEnabled()
	{
		base.OnEnabled();

		isWarmed = false;
		lastTransform = WorldTransform;
	}

	protected override void OnDisabled()
	{
		Clear();
	}

	public void Clear()
	{
		foreach ( var p in Particles.ToArray() )
		{
			Terminate( p );
		}

		Particles.Clear();
		DelayedParticles.Clear();

		foreach ( var go in _spawnedGameObjects )
		{
			if ( !go.IsValid() ) continue;
			go.Destroy();
		}
		_spawnedGameObjects.Clear();
	}

	public void ResetEmitters()
	{
		Components.ExecuteEnabledInSelfAndDescendants<ParticleEmitter>( e => e.ResetEmitter() );
	}

	long _maxDistance;
	long _maxSize;
	float _timeDelta;
	SceneTrace _trace;
	bool _parentMoved;
	Transform _worldTx;
	Vector3 _worldForce;

	internal void UpdateParticle( int index )
	{
		var p = Particles[index];

		// keep updating deathtime, incase we're in the editor and they're changing shit
		p.DeathTime = p.BornTime + Lifetime.Evaluate( p.Rand( 155, 100 ), p.Rand( 145, 100 ) );

		float delta = MathX.Remap( p.BornTime + p.Age, p.BornTime, p.DeathTime );
		p.LifeDelta = delta;

		var timeScale = PerParticleTimeScale.Evaluate( p, 3355 ) * _timeDelta * p.TimeScale;
		var frame = p.Frame;

		p.Age += timeScale;
		p.Frame++;

		// delay - not spawned yet (BornTime is in the future)
		if ( p.LifeDelta < 0 )
			return;

		var damping = Damping.Evaluate( p, 8234 );
		var forceScale = ForceScale.Evaluate( p, 7723 );
		var localSpace = LocalSpace.Evaluate( p, 254 ).Clamp( 0, 1 );

		if ( _parentMoved && frame > 0 && localSpace > 0.001f )
		{
			var localPos = lastTransform.PointToLocal( p.Position );
			var worldPos = _worldTx.PointToWorld( localPos );

			var localVelocity = lastTransform.NormalToLocal( p.Velocity.Normal );
			var worldVelocity = _worldTx.NormalToWorld( localVelocity ) * p.Velocity.Length;

			p.Position = p.Position.LerpTo( worldPos, localSpace );
			p.Velocity = p.Velocity.LerpTo( worldVelocity, localSpace );
		}

		p.ApplyDamping( damping * timeScale );

		OnStep?.Invoke( p, p.LifeDelta );

		if ( Force && forceScale != 0.0f )
		{
			if ( !ForceDirection.IsNearlyZero() )
			{
				p.Velocity += forceScale * (ForceSpace == SimulationSpace.Local ? _worldForce : ForceDirection) * timeScale;
			}

			if ( !OrbitalForce.IsNearlyZero() )
			{
				var force = OrbitalForce.Evaluate( delta, p.Rand( 8363 ), p.Rand( 5216 ), p.Rand( 2323 ) );
				var localOffset = (_worldTx.Position - p.Position).Normal;
				var rotatedOffset = localOffset.RotateAround( 0, new Angles( force ) );
				var rotDelta = localOffset - rotatedOffset;

				p.Velocity += forceScale * rotDelta * timeScale;
			}

			if ( !OrbitalPull.IsNearlyZero() )
			{
				var localOffset = (_worldTx.Position - p.Position) / 100.0f;
				p.Velocity += forceScale * localOffset * timeScale * OrbitalPull.Evaluate( delta, p.Rand( 4333 ) );
			}
		}

		// Apply constant movement
		if ( !ConstantMovement.IsNearlyZero() )
		{
			var constantMovement = ConstantMovement.Evaluate( p, 4395 ) * _timeDelta;
			p.Position += constantMovement.LerpTo( _worldTx.NormalToWorld( constantMovement ) * constantMovement.Length, localSpace );
		}

		if ( Collision )
		{
			var bounce = Bounce.Evaluate( p, 3478 );
			var friction = Friction.Evaluate( p, 7579 );
			var bumpiness = Bumpiness.Evaluate( p, 2380 );
			var push = PushStrength.Evaluate( p, 5281 );
			var die = DieOnCollisionChance.Evaluate( p, 4582 ) > Random.Shared.Float( 0, 1 );
			var radius = MathF.Max( 0.01f, CollisionRadius );

			if ( Scene.IsEditor ) push = 0;

			var hitTime = Time.Now - p.HitTime;

			var collided = p.MoveWithCollision( bounce, friction, bumpiness, push, die, timeScale, radius, _trace, DeferredParticleForces );

			if ( collided && hitTime > 0.3f && UsePrefabFeature && CollisionPrefabChance.Evaluate( delta, Random.Shared.Float( 0, 1 ) ) > Random.Shared.Float( 0, 1 ) )
			{
				var prefabSource = Random.Shared.FromList( CollisionPrefab );
				if ( prefabSource is not null )
				{
					Rotation angle = p.Angles;
					Vector3 position = p.Position;

					if ( CollisionPrefabAlign )
					{
						angle = Rotation.LookAt( p.HitNormal, p.Angles.Forward );

						var rot = CollisionPrefabRotation.Evaluate( delta, Random.Shared.Float( 0, 1 ) );
						angle = Rotation.FromYaw( rot ) * angle;

						position = p.HitPos;
					}

					// Queue the collision prefabs to spawn on the main thread
					ParticleCollisionPrefabs.Add( new( prefabSource, position, angle ) );
				}
			}
		}
		else
		{
			p.Position += p.Velocity * timeScale;
		}

		if ( ApplyColor )
		{
			var brightness = Brightness.Evaluate( p, 4626 );

			p.Color = Tint * Gradient.Evaluate( p, 8752 ); // TODO, gradient, between two gradients etc
			p.Color *= new Color( brightness, 1.0f );
		}

		if ( ApplyAlpha )
		{
			p.Alpha = Alpha.Evaluate( p, 8525 );
		}

		if ( ApplyShape )
		{
			p.Size = Scale.Evaluate( p, 6211 );

			var aspect = Stretch.Evaluate( p, 62415 );
			if ( aspect < 0 )
			{
				p.Size.x *= aspect.Remap( 0, -1, 1, 2, false );
			}
			else if ( aspect > 0 )
			{
				p.Size.y *= aspect.Remap( 0, 1, 1, 2, false );
			}
		}

		if ( ApplyRotation )
		{
			p.Angles.pitch = Pitch.Evaluate( p, 2363 );
			p.Angles.yaw = Yaw.Evaluate( p, 8762 );
			p.Angles.roll = Roll.Evaluate( p, 3675 );
		}

		if ( SheetSequence )
		{
			p.SequenceTime.x = SequenceTime.Evaluate( p, 7234 );
			p.SequenceTime.y += SequenceSpeed.Evaluate( p, 1351 ) * timeScale;
			p.Sequence = (int)SequenceId.Evaluate( p, 1051 );
		}

		if ( delta >= 1.0f )
		{
			deleteList.Enqueue( p );

			if ( p.hasUpdated )
			{
				p.hasUpdated = false;
				p.OnDisabled();
			}

			return;
		}

		if ( !p.hasUpdated )
		{
			p.hasUpdated = true;
			p.OnEnabled();
		}

		p.OnUpdate( delta );

		if ( p.Follower.IsValid() )
		{
			p.Follower.WorldTransform = new Transform( p.Position, p.Angles, p.Follower.WorldScale );
		}

		var distanceFromOrigin = (long)Vector3.DistanceBetween( _worldTx.Position, p.Position );

		if ( Interlocked.Read( ref _maxDistance ) <= distanceFromOrigin )
		{
			Interlocked.Exchange( ref _maxDistance, distanceFromOrigin );
		}

		var size = (long)float.Max( p.Size.x, p.Size.y );

		if ( Interlocked.Read( ref _maxSize ) <= size )
		{
			Interlocked.Exchange( ref _maxSize, size );
		}
	}

	public void Step( float timeDelta )
	{
		PreStep( timeDelta );

		System.Threading.Tasks.Parallel.For( 0, Particles.Count, UpdateParticle );
		PostStep();
	}

	internal readonly record struct ParticleWork( ParticleEffect effect, int startIndex, int endIndex );

	internal void CollectWork( List<ParticleWork> work )
	{
		int count = Particles.Count;
		int chunkSize = 16;

		for ( int i = 0; i < count; i += chunkSize )
		{
			work.Add( new ParticleWork( this, i, Math.Min( i + chunkSize, count ) ) );
		}
	}

	internal void TryPreWarm()
	{
		if ( isWarmed ) return;

		isWarmed = true;

		float timeStep = 0.2f;
		if ( PreWarm < timeStep ) timeStep = PreWarm;

		for ( float i = 0; i < PreWarm; i += timeStep )
		{
			Step( timeStep );
		}
	}

	internal void PreStep( float timeDelta )
	{
		_maxDistance = 15;
		_maxSize = 1;
		_timeDelta = Paused ? 0.0f : timeDelta * TimeScale;
		_worldTx = WorldTransform;

		if ( ForceSpace == SimulationSpace.Local )
			_worldForce = _worldTx.Rotation * ForceDirection;

		Vector3 lastPos = lastTransform.Position;
		Transform deltaTransform = _worldTx.ToLocal( lastTransform );

		_parentMoved = deltaTransform != global::Transform.Zero;

		OnPreStep?.Invoke( _timeDelta );

		_trace = Scene.Trace.WithoutTags( CollisionIgnore );

		RunDelayedParticles();
	}

	internal void PostStep()
	{
		while ( deleteList.TryDequeue( out var delete ) )
		{
			Terminate( delete );
		}

		ParticleBounds = BBox.FromPositionAndSize( _worldTx.Position, _maxDistance * 2.0f );
		MaxParticleSize = _maxSize;

		OnPostStep?.Invoke( _timeDelta );

		lastTransform = WorldTransform;
	}

	[Obsolete( "Pass in a delta" )]
	public Particle Emit( Vector3 position )
	{
		return Emit( position, Random.Shared.Float( 0, 1 ) );
	}

	void RunDelayedParticles()
	{
		for ( int i = DelayedParticles.Count - 1; i >= 0; i-- )
		{
			if ( DelayedParticles[i].BornTime > Time.Now ) continue;

			var p = DelayedParticles[i];

			Particles.Add( p );
			DelayedParticles.RemoveAt( i );

			SceneMetrics.ParticlesCreated++;

			try
			{
				OnParticleCreated?.Invoke( p );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
		}
	}

	readonly record struct ParticleCollisionPrefab( GameObject prefabSource, Vector3 position, Rotation rotation );
	ConcurrentBag<ParticleCollisionPrefab> ParticleCollisionPrefabs = [];

	internal readonly record struct DeferredParticleForce( PhysicsBody body, Vector3 position, Vector3 force );
	ConcurrentBag<DeferredParticleForce> DeferredParticleForces = [];

	internal void SpawnDeferredParticleCollisionPrefabs()
	{
		foreach ( var collision in ParticleCollisionPrefabs )
		{
			if ( !collision.prefabSource.IsValid() )
				continue;

			var go = collision.prefabSource.Clone( collision.position, collision.rotation );
			go.Flags |= GameObjectFlags.Absolute | GameObjectFlags.Hidden | GameObjectFlags.NotSaved;

			if ( Scene.IsEditor )
			{
				_spawnedGameObjects.Add( go );
			}
		}

		ParticleCollisionPrefabs.Clear();
	}

	internal void ApplyDeferredParticleForces()
	{
		foreach ( var force in DeferredParticleForces )
		{
			if ( force.body is not null && force.body.IsValid() )
			{
				force.body.ApplyForceAt( force.position, force.force );
			}
		}

		DeferredParticleForces.Clear();
	}

	/// <summary>
	/// Emit a particle at the given position.
	/// </summary>
	/// <param name="position">The position in which to spawn the particle</param>
	/// <param name="delta">The time delta of the spawn. The first spawned particle is 0, the last spawned particle is 1. This is used to evaluate the spawn particles like lifetime and delay.</param>
	/// <returns>A particle, will never be null. It's up to you to obey max particles.</returns>
	public Particle Emit( Vector3 position, float delta )
	{
		var localSpace = LocalSpace.Evaluate( 0, 254 ).Clamp( 0, 1 );
		var delay = StartDelay.Evaluate( delta, Random.Shared.Float() );

		var p = Particle.Create();

		p.Position = position;
		p.StartPosition = position;
		p.Radius = 1.0f;
		p.Velocity = Vector3.Random.Normal * StartVelocity.Evaluate( delta, Random.Shared.Float() );

		var initialVelocity = InitialVelocity.Evaluate( delta, Random.Shared.Float(), Random.Shared.Float(), Random.Shared.Float() );
		p.Velocity += initialVelocity.LerpTo( WorldTransform.NormalToWorld( initialVelocity ) * initialVelocity.Length, localSpace );

		p.BornTime += delay;
		p.DeathTime = p.BornTime + Lifetime.Evaluate( delta, p.Rand( 145, 100 ) );

		if ( UsePrefabFeature && FollowerPrefabChance.Evaluate( delta, Random.Shared.Float( 0, 1 ) ) > Random.Shared.Float() )
		{
			var prefabSource = Random.Shared.FromList( FollowerPrefab );
			if ( prefabSource.IsValid() )
			{
				p.Follower = prefabSource.Clone( position, p.Angles );
				p.Follower.Flags |= GameObjectFlags.Absolute | GameObjectFlags.Hidden | GameObjectFlags.NotSaved;

				if ( Scene.IsEditor )
				{
					_spawnedGameObjects.Add( p.Follower );
				}
			}
		}

		if ( delay > 0 )
		{
			DelayedParticles.Add( p );
		}
		else
		{
			Particles.Add( p );
			SceneMetrics.ParticlesCreated++;

			try
			{
				OnParticleCreated?.Invoke( p );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
		}

		return p;
	}

	public void Terminate( Particle p )
	{
		if ( p.hasUpdated )
		{
			p.hasUpdated = false;
			p.OnDisabled();
		}

		if ( p.Follower.IsValid() )
		{
			// If they have a TemporaryEffect then we will trust that to destroy
			if ( p.Follower.GetComponent<TemporaryEffect>() is { } te )
			{
				// but disable the looping effects so it doesn't live forever!
				ITemporaryEffect.DisableLoopingEffects( p.Follower );
			}
			else
			{
				p.Follower.Destroy();
			}

			p.Follower = null;
		}

		try
		{
			OnParticleDestroyed?.Invoke( p );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e );
		}

		Particles.Remove( p );

		if ( Particle.Pool.Count < 512 )
			Particle.Pool.Enqueue( p );

		SceneMetrics.ParticlesDestroyed++;
	}

	internal void OnControllerDisabled( Component c )
	{
		foreach ( var p in Particles )
		{
			p.DisableListenersForComponent( c );
		}
	}

	/// <summary>
	/// Should return true if we have active particles
	/// </summary>
	bool Component.ITemporaryEffect.IsActive => ParticleCount > 0;

}
