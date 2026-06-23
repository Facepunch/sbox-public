using System;
using Sandbox.Rendering;

namespace SceneTests.Components;

[TestClass]
public class ParticleEffectTest
{
	/// <summary>
	/// A box emitter with an initial burst should spawn exactly that many particles on the
	/// first scene tick, because OnBurst emits GetBurstCount() particles in one go, and a
	/// zero Rate emits nothing afterwards. Burst is a ParticleFloat that ResetEmitter
	/// snapshots when the emitter is enabled, so the emitter must be reset after being
	/// configured. An effect with live particles reports itself as an active temporary
	/// effect.
	/// </summary>
	[TestMethod]
	public void BoxEmitter_BurstSpawnsParticles()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Burst = 25.0f;
		emitter.Rate = 0.0f;
		emitter.ResetEmitter();

		Assert.AreEqual( 0, effect.ParticleCount, "No particles should exist before the first tick" );

		scene.GameTick();

		Assert.AreEqual( 25, effect.Particles.Count, "Burst should emit exactly 25 particles" );
		Assert.IsTrue( ((Component.ITemporaryEffect)effect).IsActive, "Effect with live particles should be active" );
	}

	/// <summary>
	/// With no burst and a constant Rate the emitter emits particles over time. The emitter
	/// targets Rate * elapsedTime emissions, and the particle step delta is clamped to 1/30s
	/// per tick, so 30 ticks simulate roughly one second and produce roughly Rate particles.
	/// Burst is snapshotted by ResetEmitter at enable time, so the emitter is reset after
	/// zeroing it out.
	/// </summary>
	[TestMethod]
	public void RateEmission_GrowsOverTime()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Burst = 0.0f;
		emitter.Rate = 100.0f;
		emitter.ResetEmitter();

		for ( int i = 0; i < 3; i++ ) scene.GameTick();

		var earlyCount = effect.Particles.Count;
		Assert.IsTrue( earlyCount >= 5 && earlyCount <= 15, $"After 3 ticks (~0.1s) expected ~10 particles, got {earlyCount}" );

		for ( int i = 0; i < 27; i++ ) scene.GameTick();

		var lateCount = effect.Particles.Count;
		Assert.IsTrue( lateCount > earlyCount, "Particle count should grow while the emitter is running" );
		Assert.IsTrue( lateCount >= 90 && lateCount <= 110, $"After 30 ticks (~1s) expected ~100 particles, got {lateCount}" );
	}

	/// <summary>
	/// MaxParticles caps the total particle count. A burst larger than the cap stops emitting
	/// once the effect is full, and a continuous Rate can never push the count above the cap
	/// on subsequent ticks. The emitter is reset after configuration so the snapshotted
	/// burst matches the configured value.
	/// </summary>
	[TestMethod]
	public void MaxParticles_CapsEmission()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.MaxParticles = 10;
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Burst = 100.0f;
		emitter.Rate = 50.0f;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 10, effect.ParticleCount, "Burst should stop emitting when the effect is full" );
		Assert.IsTrue( effect.IsFull, "Effect should report itself as full" );

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.AreEqual( 10, effect.ParticleCount, "Rate emission must not exceed MaxParticles" );
	}

	/// <summary>
	/// Particles die once their accumulated age reaches their evaluated Lifetime. With a 0.2
	/// second lifetime and a 1/30s step the burst should still be fully alive after 3 ticks
	/// but completely expired well before 21 ticks. An empty, finished effect reports itself
	/// as an inactive temporary effect. The emitter is reset after configuration so the
	/// snapshotted burst matches the configured value.
	/// </summary>
	[TestMethod]
	public void Lifetime_ExpiryRemovesParticles()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 0.2f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Burst = 20.0f;
		emitter.Rate = 0.0f;
		emitter.Loop = false;
		emitter.ResetEmitter();

		for ( int i = 0; i < 3; i++ ) scene.GameTick();

		Assert.AreEqual( 20, effect.Particles.Count, "Particles should still be alive after 0.1s of simulation" );

		for ( int i = 0; i < 18; i++ ) scene.GameTick();

		Assert.AreEqual( 0, effect.ParticleCount, "All particles should have expired after 0.7s of simulation" );
		Assert.IsFalse( ((Component.ITemporaryEffect)effect).IsActive, "Effect with no particles should be inactive" );
	}

	/// <summary>
	/// A non-looping emitter bursts once and never again after its Duration elapses, so the
	/// particle count stays at the burst size. Once finished a non-looping emitter reports
	/// itself as an inactive temporary effect. Duration and Burst are snapshotted by
	/// ResetEmitter, so the emitter is reset after configuration.
	/// </summary>
	[TestMethod]
	public void NonLoopingEmitter_StopsAfterDuration()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Loop = false;
		emitter.Duration = 0.1f;
		emitter.Burst = 5.0f;
		emitter.Rate = 0.0f;
		emitter.ResetEmitter();

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.AreEqual( 5, effect.Particles.Count, "Non-looping emitter should only ever burst once" );
		Assert.IsFalse( ((Component.ITemporaryEffect)emitter).IsActive, "Finished non-looping emitter should be inactive" );
	}

	/// <summary>
	/// A looping emitter resets after its Duration elapses, which re-arms the initial burst,
	/// so over a second of simulation it bursts multiple times and the count climbs past a
	/// single burst's worth. A looping emitter always reports itself as active. Duration and
	/// Burst are snapshotted by ResetEmitter, so the emitter is reset after configuration.
	/// </summary>
	[TestMethod]
	public void LoopingEmitter_RestartsAfterDuration()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Loop = true;
		emitter.Duration = 0.1f;
		emitter.Burst = 5.0f;
		emitter.Rate = 0.0f;
		emitter.ResetEmitter();

		for ( int i = 0; i < 30; i++ ) scene.GameTick();

		Assert.IsTrue( effect.Particles.Count > 5, $"Looping emitter should have burst multiple times, got {effect.Particles.Count}" );
		Assert.IsTrue( ((Component.ITemporaryEffect)emitter).IsActive, "Looping emitter should always be active" );
	}

	/// <summary>
	/// The emitter Delay holds back all emission (including the initial burst) until the
	/// emitter clock passes the evaluated delay, after which the burst fires as normal.
	/// Delay and Burst are snapshotted by ResetEmitter, so the emitter is reset after
	/// configuration.
	/// </summary>
	[TestMethod]
	public void EmitterDelay_DefersEmission()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Loop = false;
		emitter.Delay = 0.5f;
		emitter.Burst = 8.0f;
		emitter.Rate = 0.0f;
		emitter.ResetEmitter();

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.AreEqual( 0, effect.ParticleCount, "Nothing should be emitted before the delay elapses" );

		for ( int i = 0; i < 25; i++ ) scene.GameTick();

		Assert.AreEqual( 8, effect.Particles.Count, "Burst should fire once the delay has elapsed" );
	}

	/// <summary>
	/// The effect's StartDelay puts freshly emitted particles into the DelayedParticles list
	/// instead of the active list. They count towards ParticleCount immediately, and migrate
	/// into the active Particles list once scene time passes their delayed BornTime. The
	/// emitter is reset after configuration so the snapshotted burst matches the configured
	/// value.
	/// </summary>
	[TestMethod]
	public void StartDelay_QueuesDelayedParticles()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;
		effect.StartDelay = 0.5f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Burst = 12.0f;
		emitter.Rate = 0.0f;
		emitter.Loop = false;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 0, effect.Particles.Count, "Delayed particles should not be active yet" );
		Assert.AreEqual( 12, effect.DelayedParticles.Count, "All burst particles should be queued as delayed" );
		Assert.AreEqual( 12, effect.ParticleCount, "ParticleCount should include delayed particles" );

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		Assert.AreEqual( 12, effect.Particles.Count, "Delayed particles should activate after the start delay" );
		Assert.AreEqual( 0, effect.DelayedParticles.Count, "Delayed list should be empty after activation" );
	}

	/// <summary>
	/// Box emitter spawn positions are random points inside a box of the configured Size,
	/// centered on the emitter and scaled by the GameObject's world scale. Every particle's
	/// start position must therefore lie within the scaled half-extents of the box.
	/// </summary>
	[TestMethod]
	public void BoxEmitter_SpawnPositionsInsideScaledVolume()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 200, 300 );
		go.WorldScale = 2.0f;

		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Burst = 64.0f;
		emitter.Rate = 0.0f;
		emitter.Size = new Vector3( 40, 60, 80 );
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 64, effect.Particles.Count );

		foreach ( var p in effect.Particles )
		{
			var offset = p.StartPosition - go.WorldPosition;

			Assert.IsTrue( MathF.Abs( offset.x ) <= 40.01f, $"x offset {offset.x} should be within scaled half extent 40" );
			Assert.IsTrue( MathF.Abs( offset.y ) <= 60.01f, $"y offset {offset.y} should be within scaled half extent 60" );
			Assert.IsTrue( MathF.Abs( offset.z ) <= 80.01f, $"z offset {offset.z} should be within scaled half extent 80" );
		}
	}

	/// <summary>
	/// Box emitter spawn offsets are rotated by the GameObject's world rotation. A degenerate
	/// box that only spans the local x axis, rotated 90 degrees of yaw, must produce spawn
	/// positions that deviate from the emitter only along the world y axis.
	/// </summary>
	[TestMethod]
	public void BoxEmitter_SpawnPositionsRespectRotation()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 10, 20, 30 );
		go.WorldRotation = Rotation.FromYaw( 90 );

		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Burst = 32.0f;
		emitter.Rate = 0.0f;
		emitter.Size = new Vector3( 100, 0, 0 );
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 32, effect.Particles.Count );

		foreach ( var p in effect.Particles )
		{
			var offset = p.StartPosition - go.WorldPosition;

			Assert.IsTrue( MathF.Abs( offset.x ) <= 0.01f, $"x offset {offset.x} should be ~0 after yaw rotation" );
			Assert.IsTrue( MathF.Abs( offset.y ) <= 50.01f, $"y offset {offset.y} should be within rotated half extent 50" );
			Assert.IsTrue( MathF.Abs( offset.z ) <= 0.01f, $"z offset {offset.z} should be ~0 after yaw rotation" );
		}
	}

	/// <summary>
	/// A sphere emitter with OnEdge enabled spawns every particle exactly on the surface of
	/// the sphere, so all start positions sit at the configured radius from the emitter.
	/// </summary>
	[TestMethod]
	public void SphereEmitter_OnEdgeSpawnsAtRadius()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 50, 50, 50 );

		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleSphereEmitter>();
		emitter.Burst = 32.0f;
		emitter.Rate = 0.0f;
		emitter.Radius = 30.0f;
		emitter.Velocity = 0.0f;
		emitter.OnEdge = true;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 32, effect.Particles.Count );

		foreach ( var p in effect.Particles )
		{
			var distance = p.StartPosition.Distance( go.WorldPosition );
			Assert.AreEqual( 30.0f, distance, 0.1f, "OnEdge particles should spawn exactly at the sphere radius" );
		}
	}

	/// <summary>
	/// A sphere emitter without OnEdge spawns particles within the sphere volume, so every
	/// start position lies at most the configured radius away from the emitter.
	/// </summary>
	[TestMethod]
	public void SphereEmitter_SpawnsInsideRadius()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( -100, 0, 200 );

		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleSphereEmitter>();
		emitter.Burst = 32.0f;
		emitter.Rate = 0.0f;
		emitter.Radius = 30.0f;
		emitter.Velocity = 0.0f;
		emitter.OnEdge = false;
		emitter.ResetEmitter();

		scene.GameTick();

		Assert.AreEqual( 32, effect.Particles.Count );

		foreach ( var p in effect.Particles )
		{
			var distance = p.StartPosition.Distance( go.WorldPosition );
			Assert.IsTrue( distance <= 30.1f, $"In-volume particle spawned {distance} away, beyond the 30 radius" );
		}
	}

	/// <summary>
	/// A ParticleAttractor accelerates particles towards its target every step, so after
	/// ticking the scene the average distance from the particles to the target must shrink.
	/// Force and MaxForce are constants and Randomness is zero, making the pull deterministic.
	/// </summary>
	[TestMethod]
	public void Attractor_ReducesAverageDistanceToTarget()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var targetGo = scene.CreateObject();
		targetGo.WorldPosition = Vector3.Zero;

		var attractor = go.Components.Create<ParticleAttractor>();
		attractor.Target = targetGo;

		effect.Emit( new Vector3( 200, 0, 0 ), 0.0f );
		effect.Emit( new Vector3( -200, 0, 0 ), 0.0f );
		effect.Emit( new Vector3( 0, 200, 0 ), 0.0f );
		effect.Emit( new Vector3( 0, 0, 200 ), 0.0f );

		var before = effect.Particles.Average( p => p.Position.Distance( targetGo.WorldPosition ) );

		for ( int i = 0; i < 60; i++ ) scene.GameTick();

		Assert.AreEqual( 4, effect.Particles.Count, "All particles should still be alive" );

		var after = effect.Particles.Average( p => p.Position.Distance( targetGo.WorldPosition ) );

		Assert.IsTrue( after < before - 10.0f, $"Attractor should pull particles closer: before {before}, after {after}" );
	}

	/// <summary>
	/// With ApplyColor and ApplyAlpha enabled the effect evaluates its Gradient and Alpha
	/// against each particle's life delta every step. A white-to-black range gradient and a
	/// 1-to-0 alpha range evaluated over life must match the particle's stored LifeDelta.
	/// </summary>
	[TestMethod]
	public void GradientAndAlpha_EvaluatedOverLife()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 1.0f;
		effect.ApplyColor = true;
		effect.ApplyAlpha = true;
		effect.Gradient = new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Range,
			Evaluation = ParticleGradient.EvaluationType.Life,
			ConstantA = Color.White,
			ConstantB = Color.Black
		};
		effect.Alpha = new ParticleFloat( 1.0f, 0.0f ) { Evaluation = ParticleFloat.EvaluationType.Life };

		effect.Emit( Vector3.Zero, 0.0f );

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		var p = effect.Particles.Single();
		Assert.IsTrue( p.LifeDelta > 0.0f && p.LifeDelta < 1.0f, $"Particle should be mid-life, got {p.LifeDelta}" );

		var expected = Color.Lerp( Color.White, Color.Black, p.LifeDelta );
		Assert.AreEqual( expected.r, p.Color.r, 0.01f, "Red channel should follow the life gradient" );
		Assert.AreEqual( expected.g, p.Color.g, 0.01f, "Green channel should follow the life gradient" );
		Assert.AreEqual( expected.b, p.Color.b, 0.01f, "Blue channel should follow the life gradient" );
		Assert.AreEqual( 1.0f - p.LifeDelta, p.Alpha, 0.01f, "Alpha should fade out over the particle's life" );
	}

	/// <summary>
	/// With ApplyShape enabled the effect evaluates its Scale against each particle every
	/// step. A linear 0-to-1 curve evaluated over life must produce a particle size equal
	/// to the particle's stored LifeDelta on every axis.
	/// </summary>
	[TestMethod]
	public void ScaleCurve_EvaluatedOverLife()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 1.0f;
		effect.ApplyShape = true;
		effect.Scale = new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve,
			CurveA = Curve.Linear,
			Evaluation = ParticleFloat.EvaluationType.Life
		};

		effect.Emit( Vector3.Zero, 0.0f );

		for ( int i = 0; i < 10; i++ ) scene.GameTick();

		var p = effect.Particles.Single();
		Assert.IsTrue( p.LifeDelta > 0.0f && p.LifeDelta < 1.0f, $"Particle should be mid-life, got {p.LifeDelta}" );

		Assert.AreEqual( p.LifeDelta, p.Size.x, 0.02f, "Size.x should follow the linear life curve" );
		Assert.AreEqual( p.LifeDelta, p.Size.y, 0.02f, "Size.y should follow the linear life curve" );
		Assert.AreEqual( p.LifeDelta, p.Size.z, 0.02f, "Size.z should follow the linear life curve" );
	}

	/// <summary>
	/// Pins the pure-managed ParticleFloat evaluation modes: constants ignore the inputs,
	/// Life ranges lerp by the delta, Seed ranges lerp by the fixed random, curves sample
	/// the keyframes, and IsNearlyZero only reports true for zero constants.
	/// </summary>
	[TestMethod]
	public void ParticleFloat_EvaluationModes()
	{
		ParticleFloat constant = 5.0f;
		Assert.AreEqual( ParticleFloat.ValueType.Constant, constant.Type );
		Assert.AreEqual( 5.0f, constant.Evaluate( 0.7f, 0.3f ), "Constants should ignore delta and random" );

		var rangeLife = new ParticleFloat( 0.0f, 10.0f ) { Evaluation = ParticleFloat.EvaluationType.Life };
		Assert.AreEqual( 5.0f, rangeLife.Evaluate( 0.5f, 0.9f ), 0.0001f, "Life ranges should lerp by the delta" );

		var rangeSeed = new ParticleFloat( 0.0f, 10.0f );
		Assert.AreEqual( ParticleFloat.EvaluationType.Seed, rangeSeed.Evaluation, "Two-value constructor should default to Seed evaluation" );
		Assert.AreEqual( 2.5f, rangeSeed.Evaluate( 0.9f, 0.25f ), 0.0001f, "Seed ranges should lerp by the fixed random" );

		var curve = new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve,
			CurveA = Curve.Linear,
			Evaluation = ParticleFloat.EvaluationType.Life
		};
		Assert.AreEqual( 0.25f, curve.Evaluate( 0.25f, 0.0f ), 0.02f, "Curves should sample at the delta" );

		Assert.IsTrue( ((ParticleFloat)0.0f).IsNearlyZero(), "A zero constant is nearly zero" );
		Assert.IsFalse( ((ParticleFloat)1.0f).IsNearlyZero(), "A non-zero constant is not nearly zero" );
	}

	/// <summary>
	/// Pins the pure-managed ParticleGradient evaluation modes: constants ignore the inputs,
	/// Life ranges lerp the two colors by the delta, and Particle evaluation lerps by the
	/// fixed per-particle random instead of the delta.
	/// </summary>
	[TestMethod]
	public void ParticleGradient_EvaluationModes()
	{
		ParticleGradient constant = Color.Red;
		Assert.AreEqual( ParticleGradient.ValueType.Constant, constant.Type );
		Assert.AreEqual( Color.Red, constant.Evaluate( 0.5f, 0.5f ), "Constants should ignore delta and random" );

		var range = new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Range,
			Evaluation = ParticleGradient.EvaluationType.Life,
			ConstantA = Color.White,
			ConstantB = Color.Black
		};

		var mid = range.Evaluate( 0.5f, 0.0f );
		Assert.AreEqual( 0.5f, mid.r, 0.001f, "Life ranges should lerp the color by the delta" );
		Assert.AreEqual( 0.5f, mid.g, 0.001f );
		Assert.AreEqual( 0.5f, mid.b, 0.001f );
		Assert.AreEqual( 1.0f, mid.a, 0.001f, "Lerping white to black should keep alpha at 1" );

		var perParticle = new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Range,
			Evaluation = ParticleGradient.EvaluationType.Particle,
			ConstantA = Color.White,
			ConstantB = Color.Black
		};

		Assert.AreEqual( Color.White, perParticle.Evaluate( 0.9f, 0.0f ), "Particle evaluation should use the fixed random, not the delta" );
	}

	/// <summary>
	/// Clear terminates and removes every particle immediately. ResetEmitters re-arms the
	/// emitter's pending burst (and re-evaluates the snapshotted Burst), so the next tick
	/// repopulates the effect from scratch.
	/// </summary>
	[TestMethod]
	public void Clear_RemovesParticles_AndResetEmittersRestarts()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Burst = 10.0f;
		emitter.Rate = 0.0f;
		emitter.Loop = false;
		emitter.ResetEmitter();

		scene.GameTick();
		Assert.AreEqual( 10, effect.Particles.Count );

		effect.Clear();
		Assert.AreEqual( 0, effect.ParticleCount, "Clear should remove all particles immediately" );

		effect.ResetEmitters();
		scene.GameTick();

		Assert.AreEqual( 10, effect.Particles.Count, "ResetEmitters should re-arm the burst so it fires again" );
	}

	/// <summary>
	/// Disabling the ParticleEffect component clears all of its particles, because
	/// OnDisabled calls Clear.
	/// </summary>
	[TestMethod]
	public void DisablingEffect_ClearsParticles()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.Lifetime = 10.0f;

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Burst = 10.0f;
		emitter.Rate = 0.0f;
		emitter.ResetEmitter();

		scene.GameTick();
		Assert.AreEqual( 10, effect.Particles.Count );

		effect.Enabled = false;

		Assert.AreEqual( 0, effect.ParticleCount, "Disabling the effect should clear all particles" );
	}

	/// <summary>
	/// Serializing a GameObject holding a ParticleEffect and ParticleBoxEmitter with
	/// non-default values and deserializing it into a fresh GameObject must preserve every
	/// property, including the custom ParticleFloat and ParticleGradient JSON formats.
	/// </summary>
	[TestMethod]
	public void SerializeRoundTrip_PreservesEffectAndEmitterProperties()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		effect.MaxParticles = 123;
		effect.TimeScale = 0.25f;
		effect.Timing = ParticleEffect.TimingMode.RealTime;
		effect.Lifetime = new ParticleFloat( 0.5f, 2.0f );
		effect.StartDelay = 0.75f;
		effect.ApplyShape = true;
		effect.Scale = 3.0f;
		effect.ApplyColor = true;
		effect.Tint = Color.Red;
		effect.Gradient = new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Range,
			Evaluation = ParticleGradient.EvaluationType.Life,
			ConstantA = Color.White,
			ConstantB = Color.Black
		};

		var emitter = go.Components.Create<ParticleBoxEmitter>();
		emitter.Loop = false;
		emitter.DestroyOnEnd = true;
		emitter.Duration = 4.5f;
		emitter.Delay = 1.5f;
		emitter.Burst = 42.0f;
		emitter.Rate = 7.0f;
		emitter.Size = new Vector3( 10, 20, 30 );
		emitter.OnEdge = true;

		var json = go.Serialize().ToJsonString();
		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		copy.Enabled = true;

		var fx = copy.GetComponent<ParticleEffect>();
		Assert.IsNotNull( fx, "Deserialized GameObject should have a ParticleEffect" );
		Assert.AreEqual( 123, fx.MaxParticles );
		Assert.AreEqual( 0.25f, fx.TimeScale );
		Assert.AreEqual( ParticleEffect.TimingMode.RealTime, fx.Timing );
		Assert.AreEqual( ParticleFloat.ValueType.Range, fx.Lifetime.Type );
		Assert.AreEqual( ParticleFloat.EvaluationType.Seed, fx.Lifetime.Evaluation );
		Assert.AreEqual( 0.5f, fx.Lifetime.ConstantA );
		Assert.AreEqual( 2.0f, fx.Lifetime.ConstantB );
		Assert.AreEqual( ParticleFloat.ValueType.Constant, fx.StartDelay.Type );
		Assert.AreEqual( 0.75f, fx.StartDelay.ConstantValue );
		Assert.IsTrue( fx.ApplyShape );
		Assert.AreEqual( 3.0f, fx.Scale.ConstantValue );
		Assert.IsTrue( fx.ApplyColor );
		Assert.AreEqual( Color.Red, fx.Tint );
		Assert.AreEqual( ParticleGradient.ValueType.Range, fx.Gradient.Type );
		Assert.AreEqual( ParticleGradient.EvaluationType.Life, fx.Gradient.Evaluation );
		Assert.AreEqual( Color.White, fx.Gradient.ConstantA );
		Assert.AreEqual( Color.Black, fx.Gradient.ConstantB );

		var em = copy.GetComponent<ParticleBoxEmitter>();
		Assert.IsNotNull( em, "Deserialized GameObject should have a ParticleBoxEmitter" );
		Assert.IsFalse( em.Loop );
		Assert.IsTrue( em.DestroyOnEnd );
		Assert.AreEqual( 4.5f, em.Duration.ConstantValue );
		Assert.AreEqual( 1.5f, em.Delay.ConstantValue );
		Assert.AreEqual( 42.0f, em.Burst.ConstantValue );
		Assert.AreEqual( 7.0f, em.Rate.ConstantValue );
		Assert.AreEqual( new Vector3( 10, 20, 30 ), em.Size );
		Assert.IsTrue( em.OnEdge );
	}

	/// <summary>
	/// ParticleSpriteRenderer requires a ParticleEffect via [RequireComponent], so creating
	/// it on a bare GameObject auto-creates the effect. Its defaults need no assets: no
	/// sprite, a transparent render texture, camera-facing billboards and unsorted particles.
	/// </summary>
	[TestMethod]
	public void SpriteRenderer_DefaultState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var renderer = go.Components.Create<ParticleSpriteRenderer>();

		Assert.IsNotNull( renderer.ParticleEffect, "RequireComponent should auto-create the ParticleEffect" );
		Assert.AreEqual( renderer.ParticleEffect, go.GetComponent<ParticleEffect>() );

		Assert.IsNull( renderer.Sprite );
		Assert.AreEqual( 1.0f, renderer.Scale );
		Assert.AreEqual( 0.0f, renderer.DepthFeather );
		Assert.AreEqual( 1.0f, renderer.FogStrength );
		Assert.AreEqual( ParticleSpriteRenderer.BillboardAlignment.LookAtCamera, renderer.Alignment );
		Assert.AreEqual( ParticleSpriteRenderer.ParticleSortMode.Unsorted, renderer.SortMode );
		Assert.IsFalse( renderer.IsSorted );
		Assert.IsFalse( renderer.IsAnimated );
		Assert.AreEqual( new Vector2( 0.5f, 0.5f ), renderer.Pivot );
		Assert.AreEqual( Texture.Transparent, renderer.RenderTexture );
		Assert.IsTrue( go.Tags.Has( "particles" ), "Sprite renderer should tag its GameObject with 'particles'" );
	}

	/// <summary>
	/// ParticleModelRenderer is a ParticleController, so enabling it next to an effect wires
	/// the ParticleEffect property. Its default model choice is the built-in cube, casting
	/// shadows, with a constant scale of one.
	/// </summary>
	[TestMethod]
	public void ModelRenderer_DefaultState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		var renderer = go.Components.Create<ParticleModelRenderer>();

		Assert.AreEqual( effect, renderer.ParticleEffect, "Controller should find the effect on its GameObject" );
		Assert.AreEqual( 1, renderer.Choices.Count );
		Assert.AreEqual( Model.Cube, renderer.Choices[0].Model );
		Assert.IsTrue( renderer.CastShadows );
		Assert.IsFalse( renderer.RotateWithGameObject );
		Assert.IsNull( renderer.MaterialOverride );
		Assert.AreEqual( ParticleFloat.ValueType.Constant, renderer.Scale.Type );
		Assert.AreEqual( 1.0f, renderer.Scale.ConstantValue );
	}

	/// <summary>
	/// ParticleLightRenderer defaults: every particle gets a light, capped at 8 lights, no
	/// shadows, white constant light color tinted by the particle color, constant scale 32.
	/// </summary>
	[TestMethod]
	public void LightRenderer_DefaultState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		var renderer = go.Components.Create<ParticleLightRenderer>();

		Assert.AreEqual( effect, renderer.ParticleEffect, "Controller should find the effect on its GameObject" );
		Assert.AreEqual( 1.0f, renderer.Ratio );
		Assert.AreEqual( 8, renderer.MaximumLights );
		Assert.IsFalse( renderer.CastShadows );
		Assert.IsTrue( renderer.UseParticleColor );
		Assert.AreEqual( 32.0f, renderer.Scale.ConstantValue );
		Assert.AreEqual( 1.0f, renderer.Attenuation.ConstantValue );
		Assert.AreEqual( 1.0f, renderer.Brightness.ConstantValue );
		Assert.AreEqual( ParticleGradient.ValueType.Constant, renderer.LightColor.Type );
		Assert.AreEqual( Color.White, renderer.LightColor.ConstantValue );
	}

	/// <summary>
	/// ParticleTrailRenderer defaults: 64 trail points spaced 8 units apart living 2 seconds,
	/// tinted and scaled from the particle, rendered opaque without shadows or wireframe.
	/// </summary>
	[TestMethod]
	public void TrailRenderer_DefaultState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var effect = go.Components.Create<ParticleEffect>();
		var renderer = go.Components.Create<ParticleTrailRenderer>();

		Assert.AreEqual( effect, renderer.ParticleEffect, "Controller should find the effect on its GameObject" );
		Assert.AreEqual( 64, renderer.MaxPoints );
		Assert.AreEqual( 8.0f, renderer.PointDistance );
		Assert.AreEqual( 2.0f, renderer.LifeTime );
		Assert.IsTrue( renderer.TintFromParticle );
		Assert.IsTrue( renderer.ScaleFromParticle );
		Assert.IsTrue( renderer.Opaque );
		Assert.IsFalse( renderer.CastShadows );
		Assert.IsFalse( renderer.Wireframe );
	}

	/// <summary>
	/// ParticleTextRenderer defaults: centered pivot, unit scale, bilinear filtering, no
	/// depth feathering, full fog strength and no additive blending. It also auto-creates
	/// its required ParticleEffect.
	/// </summary>
	[TestMethod]
	public void TextRenderer_DefaultState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var renderer = go.Components.Create<ParticleTextRenderer>();

		Assert.IsNotNull( renderer.ParticleEffect, "RequireComponent should auto-create the ParticleEffect" );
		Assert.AreEqual( new Vector2( 0.5f, 0.5f ), renderer.Pivot );
		Assert.AreEqual( 1.0f, renderer.Scale );
		Assert.AreEqual( 0.0f, renderer.DepthFeather );
		Assert.AreEqual( 1.0f, renderer.FogStrength );
		Assert.AreEqual( FilterMode.Bilinear, renderer.TextureFilter );
		Assert.IsFalse( renderer.Additive );
		Assert.IsFalse( renderer.FaceVelocity );
	}
}
