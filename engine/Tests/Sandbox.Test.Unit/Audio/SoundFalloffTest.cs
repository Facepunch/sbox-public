using Sandbox.Audio;
using System;
using System.Text.Json.Nodes;

namespace AudioTests;

[TestClass]
public class SoundFalloffTest
{
	const float M = 39.37f;

	static float Db( float g ) => 20f * MathF.Log10( MathF.Max( g, 1e-6f ) );

	static float Physical( float meters, float minDistance = 0f, float distance = 15_000f )
		=> SoundFalloff.Gain( SoundFalloffMode.Physical, SoundFalloff.ClassicCurve, minDistance, distance, meters * M );

	[TestMethod]
	public void PhysicalHalvesPerDoubling()
	{
		Assert.AreEqual( 1f, Physical( 0.5f, M ), 1e-4f );
		Assert.AreEqual( 1f, Physical( 1f, M ), 1e-4f );
		Assert.AreEqual( 0.5f, Physical( 2f, M ), 1e-4f );
		Assert.AreEqual( 0.25f, Physical( 4f, M ), 1e-4f );
		Assert.AreEqual( 0.1f, Physical( 10f, M ), 1e-4f );
	}

	[TestMethod]
	public void DistanceOnlyFadesTheEnd()
	{
		// Max distance doesn't touch the falloff before the last half, trails off over it, and nothing is heard past it.
		Assert.AreEqual( Physical( 45f, M, 1000 * M ), Physical( 45f, M, 100 * M ), 1e-5f );
		Assert.IsTrue( Physical( 60f, M, 100 * M ) < Physical( 60f, M, 1000 * M ) );
		Assert.IsTrue( Physical( 90f, M, 100 * M ) < Physical( 75f, M, 100 * M ) * 0.5f );
		Assert.AreEqual( 0f, Physical( 100f, M, 100 * M ), 1e-6f );
		Assert.AreEqual( 0f, Physical( 150f, M, 100 * M ), 1e-6f );
	}

	[TestMethod]
	public void AutoMinDistanceStaysCloseToTheClassicCurve()
	{
		// Unauthored sounds pick their min distance from the distance; at the default distance that should sound about like before.
		var classic = SoundFalloff.ClassicCurve;
		foreach ( var meters in new[] { 5f, 10f, 20f, 40f, 80f } )
		{
			float before = Db( classic.Evaluate( meters * M / 15_000f ) );
			float after = Db( Physical( meters ) );
			Console.WriteLine( $"{meters,4} m: classic {before,6:F1} dB, physical {after,6:F1} dB" );
			Assert.AreEqual( before, after, 6f, $"at {meters} m" );
		}
	}

	[TestMethod]
	public void CustomCurveIsUntouched()
	{
		var curve = new Curve( new( 0, 1 ), new( 1, 0 ) );
		Assert.AreEqual( curve.Evaluate( 0.3f ), SoundFalloff.Gain( SoundFalloffMode.Curve, curve, 0f, 1000f, 300f ), 1e-5f );
	}

	[TestMethod]
	public void SettingACustomCurveMakesTheHandleUseIt()
	{
		using var handle = new SoundHandle();
		Assert.AreEqual( SoundFalloffMode.Physical, handle.FalloffMode );
		handle.Falloff = SoundFalloff.ClassicCurve;
		Assert.AreEqual( SoundFalloffMode.Physical, handle.FalloffMode );
		handle.Falloff = new Curve( new( 0, 1 ), new( 1, 0 ) );
		Assert.AreEqual( SoundFalloffMode.Curve, handle.FalloffMode );
	}

	[TestMethod]
	public void UpgraderKeepsAuthoredCurves()
	{
		var stock = JsonNode.Parse( """{ "Distance": 6312, "Falloff": [ { "x": 0, "y": 1, "in": 0, "out": -1.8, "mode": "Mirrored" }, { "x": 0.05, "y": 0.22, "in": 3.5, "out": -3.5, "mode": "Mirrored" }, { "x": 0.2, "y": 0.04, "in": 0.16, "out": -0.16, "mode": "Mirrored" }, { "x": 1, "y": 0, "in": 0, "out": 0, "mode": "Mirrored" } ] }""" ).AsObject();
		SoundEvent.Upgrader_v2( stock );
		Assert.AreEqual( "Physical", (string)stock["FalloffMode"] );
		Assert.AreEqual( 0f, (float)stock["MinDistance"] );

		var missing = JsonNode.Parse( """{ "Distance": 1024 }""" ).AsObject();
		SoundEvent.Upgrader_v2( missing );
		Assert.AreEqual( "Physical", (string)missing["FalloffMode"] );

		var custom = JsonNode.Parse( """{ "Distance": 43445, "Falloff": [ { "x": 0, "y": 1, "in": 0, "out": 0, "mode": "Mirrored" }, { "x": 1, "y": 0, "in": 0, "out": 0, "mode": "Mirrored" } ] }""" ).AsObject();
		SoundEvent.Upgrader_v2( custom );
		Assert.AreEqual( "Curve", (string)custom["FalloffMode"] );
	}
}
