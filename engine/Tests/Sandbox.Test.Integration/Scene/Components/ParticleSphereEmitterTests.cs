using System.Linq;

namespace SceneTests.Components;

[TestClass]
public class ParticleSphereEmitterTests
{
	[TestMethod]
	public void HollowSphereKeepsParticlesInShellAndMovesOutward()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 200, 300 );
		var effect = go.AddComponent<ParticleEffect>();
		var emitter = go.AddComponent<ParticleSphereEmitter>();
		emitter.Radius = 20;
		emitter.MinimumRadius = 15;
		emitter.Velocity = 0;
		emitter.RadialVelocity = new( 40, 60 );
		for ( var i = 0; i < 100; i++ ) emitter.Emit( effect );
		foreach ( var particle in effect.Particles )
		{
			var offset = particle.Position - go.WorldPosition;
			Assert.IsTrue( offset.Length >= 14.99f && offset.Length <= 20.01f );
			Assert.IsTrue( particle.Velocity.Length >= 39.99f && particle.Velocity.Length <= 60.01f );
			Assert.IsTrue( offset.Normal.Dot( particle.Velocity.Normal ) > 0.999f );
		}
		Assert.AreEqual( 100, effect.Particles.Count() );
	}

	[TestMethod]
	public void PointEmitterRetainsBiasedLocalRadialVelocity()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		go.WorldRotation = Rotation.FromPitch( 90 );
		var effect = go.AddComponent<ParticleEffect>();
		var emitter = go.AddComponent<ParticleSphereEmitter>();
		emitter.Radius = 0;
		emitter.Velocity = 0;
		emitter.RadialVelocity = 50;
		emitter.DistanceBias = Vector3.Up;
		emitter.OneSidedAxes = ParticleSphereEmitter.EmitAxes.Z;
		emitter.LocalDirection = true;
		for ( var i = 0; i < 20; i++ ) emitter.Emit( effect );
		foreach ( var particle in effect.Particles )
		{
			Assert.AreEqual( Vector3.Zero, particle.Position );
			Assert.IsTrue( particle.Velocity.Distance( go.WorldRotation * Vector3.Up * 50 ) < 0.01f );
		}
	}

	[TestMethod]
	public void InnerRadiusClampsToOuterRadius()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		var effect = go.AddComponent<ParticleEffect>();
		var emitter = go.AddComponent<ParticleSphereEmitter>();
		emitter.Radius = 10;
		emitter.MinimumRadius = 30;
		for ( var i = 0; i < 20; i++ ) emitter.Emit( effect );
		foreach ( var particle in effect.Particles ) Assert.AreEqual( 10, particle.Position.Length, 0.001f );
	}

	[TestMethod]
	public void NormalizedBiasPreservesShellRadiusAndWorldDirection()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		go.WorldRotation = Rotation.FromPitch( 90 );
		var effect = go.AddComponent<ParticleEffect>();
		var emitter = go.AddComponent<ParticleSphereEmitter>();
		emitter.MinimumRadius = emitter.Radius = 10;
		emitter.DistanceBias = new Vector3( 0, 0, 0.2f );
		emitter.NormalizeBias = true;
		emitter.OneSidedAxes = ParticleSphereEmitter.EmitAxes.Z;
		emitter.Velocity = 0;
		emitter.RadialVelocity = 30;
		for ( var i = 0; i < 20; i++ ) emitter.Emit( effect );
		foreach ( var particle in effect.Particles )
		{
			Assert.IsTrue( particle.Position.Distance( Vector3.Up * 10 ) < 0.01f );
			Assert.IsTrue( particle.Velocity.Distance( Vector3.Up * 30 ) < 0.01f );
		}
	}
}
