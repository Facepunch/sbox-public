using System;

namespace SceneTests.Components;

/// <summary>
/// Exercises native water forces on submerged bodies.
/// </summary>
[TestClass]
public class WaterBuoyancyTests
{
	/// <summary>
	/// Floating bodies follow the moving wave surface instead of settling at the trigger's crest envelope.
	/// </summary>
	[TestMethod]
	public void FloatingBodiesFollowWaves()
	{
		Assert.IsTrue( FloatingHeightRange( false ) < 1, "Flat water did not settle." );
		Assert.IsTrue( FloatingHeightRange( true ) > 8, "Waves did not move the floating body." );
	}

	/// <summary>
	/// Measure a floating cube after its initial drop has settled.
	/// </summary>
	static float FloatingHeightRange( bool waves )
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.Effects = false;
			water.Waves = waves;
			water.WaveHeight = 24;
			water.WaveWidth = 1200;

			var obj = scene.CreateObject();
			obj.Components.Create<BoxCollider>().Scale = new Vector3( 16 );
			var body = obj.Components.Create<Rigidbody>();
			body.MassOverride = 10;

			float lowest = float.PositiveInfinity;
			float highest = float.NegativeInfinity;
			for ( int i = 0; i < 600; i++ )
			{
				scene.GameTick( 1.0 / 50 );
				if ( i < 300 )
					continue;

				lowest = MathF.Min( lowest, obj.WorldPosition.z );
				highest = MathF.Max( highest, obj.WorldPosition.z );
			}

			return highest - lowest;
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Native shape volume and mass determine whether a submerged body rises or sinks.
	/// </summary>
	[TestMethod]
	public void NativeDensityMakesLightBodiesRiseAndHeavyBodiesSink()
	{
		var light = Simulate( mass: 10 );
		var heavy = Simulate( mass: 100 );
		Assert.IsTrue( light.z > 0, $"Light body did not rise: {light}" );
		Assert.IsTrue( heavy.z < 0, $"Heavy body did not sink: {heavy}" );
	}

	/// <summary>
	/// Simulate a sixteen-inch cube submerged in a pool and return its velocity.
	/// </summary>
	static Vector3 Simulate( float mass )
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			scene.CreateObject().Components.Create<WaterBody>().Effects = false;

			var obj = scene.CreateObject();
			obj.WorldPosition = Vector3.Down * 100;
			obj.Components.Create<BoxCollider>().Scale = new Vector3( 16 );
			var body = obj.Components.Create<Rigidbody>();
			body.MassOverride = mass;
			body.LinearDamping = 0;
			body.AngularDamping = 0;
			for ( int i = 0; i < 20; i++ )
				scene.GameTick( 1.0 / 50 );

			Assert.IsTrue( body.Touching.Any( x => x.Tags.Contains( "water" ) ), "The simulation did not establish native water contacts." );
			return body.Velocity;
		}
		finally
		{
			scene.Destroy();
		}
	}
}
