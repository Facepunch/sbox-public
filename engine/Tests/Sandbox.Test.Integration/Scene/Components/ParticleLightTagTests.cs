using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SceneTests.Components;

[TestClass]
public class ParticleLightTagTests
{
	[TestMethod]
	public void LiveLightsFollowLocalAndInheritedTagChanges()
	{
		using var f = new Fixture();
		f.AssertTags( "grandparent", "parent", "emitter" );
		f.Emitter.Tags.Add( "local" );
		f.Parent.Tags.Remove( "parent" );
		f.Grandparent.Tags.Add( "inherited" );
		f.Step();
		f.AssertTags( "grandparent", "inherited", "emitter", "local" );

		f.Emitter.Tags.RemoveAll();
		f.Parent.Tags.RemoveAll();
		f.Grandparent.Tags.RemoveAll();
		f.Step();
		f.AssertTags();
	}

	[TestMethod]
	public void RemovingAncestorTagKeepsTheLocalCopy()
	{
		using var f = new Fixture();
		f.Emitter.Tags.Add( "shared" );
		f.Parent.Tags.Add( "shared" );
		f.Step();
		f.Parent.Tags.Remove( "shared" );
		f.Step();
		f.AssertTags( "grandparent", "parent", "emitter", "shared" );
		f.Emitter.Tags.Remove( "shared" );
		f.Step();
		f.AssertTags( "grandparent", "parent", "emitter" );
	}

	[TestMethod]
	public void DirectAndAncestorReparentingRefreshLiveLights()
	{
		using var f = new Fixture();
		var other = f.Scene.CreateObject();
		other.Tags.Add( "other" );
		// The emitter's immediate parent stays the same here; its ancestry changes.
		f.Parent.Parent = other;
		f.Step();
		f.AssertTags( "other", "parent", "emitter" );

		f.Emitter.Parent = f.Grandparent;
		f.Step();
		f.AssertTags( "grandparent", "emitter" );

		f.Emitter.Parent = f.Scene;
		f.Step();
		f.AssertTags( "emitter" );
	}

	[TestMethod]
	public void ReenabledRendererUsesChangesMadeWhileDisabled()
	{
		using var f = new Fixture();
		f.Renderer.Enabled = false;
		Assert.AreEqual( 0, f.Lights.Length );
		f.Parent.Tags.Remove( "parent" );
		f.Grandparent.Tags.Add( "disabled_change" );
		f.Renderer.Enabled = true;
		f.Emit();
		f.Step();
		f.AssertTags( "grandparent", "disabled_change", "emitter" );
	}

	[TestMethod]
	public void ReenabledHierarchyUsesItsNewAncestry()
	{
		using var f = new Fixture();
		f.Parent.Enabled = false;
		var other = f.Scene.CreateObject();
		other.Tags.Add( "other" );
		f.Parent.Parent = other;
		f.Parent.Tags.RemoveAll();
		f.Parent.Enabled = true;
		f.Emit();
		f.Step();
		f.AssertTags( "other", "emitter" );
	}

	[TestMethod]
	public void SceneTagMembershipKeepsExistingSetFromBehavior()
	{
		using var f = new Fixture();
		f.Emitter.Tags.Add( "shared" );
		f.Step();
		f.Scene.Tags.Add( "shared" );
		f.Emitter.Tags.Remove( "shared" );
		f.Step();
		// GameTags.Has includes scene tags, while TryGetAll stops at the scene. Keep the
		// established SetFrom semantics rather than replacing the source with a flattened set.
		f.AssertTags( "grandparent", "parent", "emitter", "shared" );
		f.Scene.Tags.Remove( "shared" );
		f.Step();
		f.AssertTags( "grandparent", "parent", "emitter" );
	}

	[TestMethod]
	public void InvalidLightDoesNotConsumeTheTagVersion()
	{
		using var f = new Fixture();
		int? applied = null;
		var invalid = new ScenePointLight( f.Scene.SceneWorld, Vector3.Zero, 100, Color.White );
		invalid.Delete();
		f.Renderer.UpdateTags( invalid, ref applied );
		Assert.IsNull( applied );

		var valid = new ScenePointLight( f.Scene.SceneWorld, Vector3.Zero, 100, Color.White );
		try
		{
			f.Renderer.UpdateTags( valid, ref applied );
			Assert.IsNotNull( applied );
			Assert.IsTrue( valid.Tags.Has( "grandparent" ) );
		}
		finally { valid.Delete(); }
	}

	[TestMethod]
	public void WorkerUpdatesObserveTagInvalidation()
	{
		using var f = new Fixture();
		var field = typeof( Particle ).GetField( "_controllers", BindingFlags.Instance | BindingFlags.NonPublic );
		var pairs = f.Effect.Particles.Select( p =>
			(Particle: p, Light: ((List<Particle.BaseListener>)field.GetValue( p )).OfType<ParticleLight>().Single()) ).ToArray();
		f.Parent.Tags.Add( "worker" );
		JobDispatcher.For( pairs.Length, pairs, static ( items, i ) => items[i].Light.OnUpdate( items[i].Particle, 0 ) );
		f.AssertTags( "grandparent", "parent", "emitter", "worker" );
	}

	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void UnchangedParticleLightUpdatesDoNotAllocate()
	{
		using var f = new Fixture();
		var field = typeof( Particle ).GetField( "_controllers", BindingFlags.Instance | BindingFlags.NonPublic );
		var pairs = f.Effect.Particles.Select( p =>
			(Particle: p, Light: ((List<Particle.BaseListener>)field.GetValue( p )).OfType<ParticleLight>().Single()) ).ToArray();

		Allocations.AssertSettles( "unchanged particle light updates", () =>
		{
			for ( int i = 0; i < 1000; i++ )
				foreach ( var pair in pairs ) pair.Light.OnUpdate( pair.Particle, 0 );
		} );
		f.AssertTags( "grandparent", "parent", "emitter" );
	}

	/// <summary>
	/// A particle that dies hides its light for the next particle to take, so an effect that keeps emitting makes no lights
	/// (and no render objects) once it has its most: the next particles light up with the same lights, tags as they were.
	/// Disabling the renderer deletes the kept ones.
	/// </summary>
	[TestMethod]
	public void DeadParticlesLightsAreKeptForTheNext()
	{
		using var f = new Fixture();
		var first = f.Lights;
		Assert.AreEqual( 2, first.Length );

		foreach ( var p in f.Effect.Particles.ToArray() ) f.Effect.Terminate( p );
		f.Step();
		Assert.AreEqual( 2, f.Lights.Length, "the dead particles' lights are kept" );
		Assert.IsTrue( f.Lights.All( l => !l.RenderingEnabled ), "and hidden" );

		f.Emit();
		f.Step();
		CollectionAssert.AreEquivalent( first, f.Lights, "the new particles took the kept lights" );
		Assert.IsTrue( f.Lights.All( l => l.RenderingEnabled ), "shown again" );
		f.AssertTags( "grandparent", "parent", "emitter" );

		f.Renderer.Enabled = false;
		Assert.AreEqual( 0, f.Lights.Length, "disabling the renderer deletes them, kept or lit" );
	}

	/// <summary>Particles dying and others lighting up in their place take the kept lights, allocating nothing once warm.</summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void ReplacedParticlesLightsDoNotAllocate()
	{
		using var f = new Fixture();
		Allocations.AssertSettles( "particles' lights replaced", () =>
		{
			for ( int i = f.Effect.Particles.Count - 1; i >= 0; i-- ) f.Effect.Terminate( f.Effect.Particles[i] );
			f.Step();
			f.Emit();
			f.Step();
		} );
		f.AssertTags( "grandparent", "parent", "emitter" );
	}

	private sealed class Fixture : IDisposable
	{
		public Scene Scene { get; } = new();
		readonly IDisposable scope;
		public GameObject Grandparent { get; }
		public GameObject Parent { get; }
		public GameObject Emitter { get; }
		public ParticleEffect Effect { get; }
		public ParticleLightRenderer Renderer { get; }
		public ScenePointLight[] Lights => Scene.SceneWorld.SceneObjects.OfType<ScenePointLight>().ToArray();

		public Fixture()
		{
			// Token lookup is case-insensitive, but reverse lookup keeps the first spelling.
			// Other tests can register these names before this fixture does.
			_ = new StringToken( "Parent" );
			_ = new StringToken( "Other" );
			scope = Scene.Push();
			Grandparent = Scene.CreateObject();
			Grandparent.Tags.Add( "grandparent" );
			Parent = new GameObject( Grandparent );
			Parent.Tags.Add( "parent" );
			Emitter = new GameObject( Parent );
			Emitter.Tags.Add( "emitter" );
			Effect = Emitter.Components.Create<ParticleEffect>();
			Effect.Lifetime = 1000;
			Renderer = Emitter.Components.Create<ParticleLightRenderer>();
			Renderer.MaximumLights = 2;
			Emit();
			Step();
		}

		public void Emit()
		{
			Effect.Emit( Vector3.Zero, 0 );
			Effect.Emit( Vector3.One, 0 );
		}

		public void Step()
		{
			// Native light registration is immediate on this thread, so inspect it without a render loop.
			Effect.PreStep( 0.01f );
			for ( int i = 0; i < Effect.Particles.Count; i++ ) Effect.UpdateParticle( i );
			Effect.PostStep();
		}

		public void AssertTags( params string[] expected )
		{
			var lights = Lights;
			Assert.AreEqual( 2, lights.Length );
			// Compare tag identity, not the spelling retained by the process-wide reverse lookup.
			var expectedTokens = expected.Select( tag => new StringToken( tag ).Value ).ToArray();
			foreach ( var light in lights ) CollectionAssert.AreEquivalent( expectedTokens, light.Tags.GetTokens().ToArray() );
		}

		public void Dispose()
		{
			Scene.Destroy();
			scope.Dispose();
		}
	}
}
