namespace SceneTests.Components;

/// <summary>
/// Exercises native water forces on submerged bodies.
/// </summary>
[TestClass]
public class WaterBuoyancyTests
{
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
