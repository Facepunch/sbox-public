using System;

namespace SceneTests.Components;

/// <summary>
/// A camera effect contributing a fixed, deterministic offset - for testing the system's targeting,
/// falloff and scaling without the built-in effects' randomness.
/// </summary>
public sealed class FixedOffsetEffect : CameraEffectSystem.BaseEffect
{
	public Vector3 Offset = new( 0, 0, 10 );

	public override void Evaluate( float scale, ref Vector3 position, ref Angles angles, ref float fieldOfView )
	{
		position += Offset * scale;
	}
}

/// <summary>
/// Pins the CameraEffectSystem contract: effect lifetimes, Stop and infinite effects, the global
/// Scale knob, per-camera targeting, epicenter falloff, and the deterministic envelope math of the
/// built-in punch and tilt.
/// </summary>
[TestClass]
public class CameraEffectSystemTests
{
	static CameraComponent CreateCamera( Scene scene, Vector3 position = default )
	{
		var go = scene.CreateObject();
		go.WorldPosition = position;
		return go.Components.Create<CameraComponent>();
	}

	/// <summary>
	/// Effects expire when their duration elapses, and Stop ends them early. A duration of zero runs
	/// until stopped.
	/// </summary>
	[TestMethod]
	public void LifetimesAndStop()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var system = CameraEffectSystem.Get( scene );
		var cam = CreateCamera( scene );

		var timed = system.AddShake( cam, 4, 40, duration: 1f );
		Assert.IsFalse( timed.IsDone );
		timed.Update( 1.1f );
		Assert.IsTrue( timed.IsDone, "Expired after its duration" );

		var infinite = system.AddShake( cam, 4, 40, duration: 0f );
		infinite.Update( 100f );
		Assert.IsFalse( infinite.IsDone, "Infinite effects don't expire" );
		infinite.Stop();
		Assert.IsTrue( infinite.IsDone, "Stop ends an infinite effect" );

		var stopped = system.AddShake( cam, 4, 40, duration: 10f );
		stopped.Stop();
		Assert.IsTrue( stopped.IsDone, "Stop ends a timed effect early" );
	}

	/// <summary>
	/// Scale zero silences every effect - the accessibility off-switch.
	/// </summary>
	[TestMethod]
	public void ScaleZeroSilences()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var system = CameraEffectSystem.Get( scene );
		var cam = CreateCamera( scene );

		system.Add( new FixedOffsetEffect { Camera = cam, Duration = 10f } );

		system.Scale = 0f;
		system.QueryOffsets( cam, out var position, out _, out _ );
		Assert.AreEqual( Vector3.Zero, position );

		system.Scale = 1f;
		system.QueryOffsets( cam, out position, out _, out _ );
		Assert.AreEqual( 10f, position.z, 0.001f );

		system.Scale = 0.5f;
		system.QueryOffsets( cam, out position, out _, out _ );
		Assert.AreEqual( 5f, position.z, 0.001f, "Scale multiplies every effect" );
	}

	/// <summary>
	/// An effect targeting one camera contributes nothing to any other camera.
	/// </summary>
	[TestMethod]
	public void CameraTargeting()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var system = CameraEffectSystem.Get( scene );
		var camA = CreateCamera( scene );
		var camB = CreateCamera( scene );

		system.Add( new FixedOffsetEffect { Camera = camA, Duration = 10f } );

		system.QueryOffsets( camA, out var positionA, out _, out _ );
		system.QueryOffsets( camB, out var positionB, out _, out _ );

		Assert.AreEqual( 10f, positionA.z, 0.001f );
		Assert.AreEqual( Vector3.Zero, positionB, "Targeted effects skip other cameras" );
	}

	/// <summary>
	/// Epicenter effects fall off linearly with camera distance - full at the epicenter, half way at
	/// half the radius, nothing at and beyond the radius.
	/// </summary>
	[TestMethod]
	public void EpicenterFalloff()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var system = CameraEffectSystem.Get( scene );

		var atCenter = CreateCamera( scene, Vector3.Zero );
		var halfway = CreateCamera( scene, new Vector3( 256, 0, 0 ) );
		var outside = CreateCamera( scene, new Vector3( 600, 0, 0 ) );

		system.Add( new FixedOffsetEffect { Epicenter = Vector3.Zero, Radius = 512f, Duration = 10f } );

		system.QueryOffsets( atCenter, out var position, out _, out _ );
		Assert.AreEqual( 10f, position.z, 0.001f );

		system.QueryOffsets( halfway, out position, out _, out _ );
		Assert.AreEqual( 5f, position.z, 0.001f );

		system.QueryOffsets( outside, out position, out _, out _ );
		Assert.AreEqual( Vector3.Zero, position );
	}

	/// <summary>
	/// The punch envelope is deterministic - sin( t·3π·frequency ) · (1 - t). Half way through a
	/// one-oscillation punch it sits at exactly half amplitude on the far side, and it returns to
	/// zero at the end of its life.
	/// </summary>
	[TestMethod]
	public void PunchEnvelope()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var system = CameraEffectSystem.Get( scene );
		var cam = CreateCamera( scene );

		var punch = system.AddPunch( cam, Vector3.Up, amplitude: 8f, frequency: 1f, duration: 1f );

		punch.Update( 0.5f );
		system.QueryOffsets( cam, out var position, out _, out _ );
		Assert.AreEqual( -4f, position.z, 0.01f, "sin(1.5π) · 0.5 · 8" );

		punch.Update( 0.4999f );
		system.QueryOffsets( cam, out position, out _, out _ );
		Assert.AreEqual( 0f, position.z, 0.05f, "Lapses back to zero at the end" );
	}

	/// <summary>
	/// The tilt eases to exactly its full angle after the ease time and holds it. The angular punch
	/// starts from zero.
	/// </summary>
	[TestMethod]
	public void TiltEase()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var system = CameraEffectSystem.Get( scene );
		var cam = CreateCamera( scene );

		var tilt = system.AddTilt( cam, new Angles( 5, 0, 15 ), duration: 100f, easeTime: 0.25f );

		system.QueryOffsets( cam, out _, out var angles, out _ );
		Assert.AreEqual( 0f, angles.roll, 0.001f, "Starts from zero" );

		tilt.Update( 1f );
		system.QueryOffsets( cam, out _, out angles, out _ );
		Assert.AreEqual( 15f, angles.roll, 0.001f, "Held at full strength after easing in" );
		Assert.AreEqual( 5f, angles.pitch, 0.001f );
	}

	/// <summary>
	/// The classic shake never throws the camera further than its amplitude, on any axis, at any
	/// point in its life.
	/// </summary>
	[TestMethod]
	public void ShakeStaysWithinAmplitude()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var system = CameraEffectSystem.Get( scene );
		var cam = CreateCamera( scene );

		var amplitude = 4f;
		var shake = system.AddShake( cam, amplitude, frequency: 40f, duration: 1f );

		for ( int i = 0; i < 60; i++ )
		{
			shake.Update( 1f / 60f );
			system.QueryOffsets( cam, out var position, out var angles, out _ );

			Assert.IsTrue( MathF.Abs( position.x ) <= amplitude + 0.001f, $"x {position.x} exceeded amplitude" );
			Assert.IsTrue( MathF.Abs( position.y ) <= amplitude + 0.001f, $"y {position.y} exceeded amplitude" );
			Assert.IsTrue( MathF.Abs( position.z ) <= amplitude + 0.001f, $"z {position.z} exceeded amplitude" );
			Assert.IsTrue( MathF.Abs( angles.roll ) <= amplitude * 0.25f + 0.001f, $"roll {angles.roll} exceeded amplitude/4" );
		}
	}
}
