namespace PhysicsTests;

[TestClass]
public class SubStepsTest
{
	/// <summary>
	/// Stepping without a world time must apply <see cref="PhysicsWorld.SubSteps"/> once, the same as stepping with one.
	/// </summary>
	[DataTestMethod]
	[DataRow( 4 )]
	[DataRow( 8 )]
	public void WorldSubStepsApplyOnceWithoutWorldTime( int worldSubSteps )
	{
		const float delta = 1.0f / 60.0f;
		const int steps = 30;

		var implicitTime = CreateFallingBody( worldSubSteps, out var implicitWorld );
		var explicitTime = CreateFallingBody( worldSubSteps, out var explicitWorld );
		var unscaled = CreateFallingBody( 1, out var unscaledWorld );

		try
		{
			for ( var i = 1; i <= steps; i++ )
			{
				implicitWorld.Step( delta );
				explicitWorld.Step( i * delta, delta, 1 );
				unscaledWorld.Step( i * delta, delta, worldSubSteps );
			}

			var expected = explicitTime.Position.z;
			Assert.AreEqual( expected, unscaled.Position.z, 0.001f );
			Assert.AreEqual( expected, implicitTime.Position.z, 0.001f );
		}
		finally
		{
			implicitWorld.Delete();
			explicitWorld.Delete();
			unscaledWorld.Delete();
		}
	}

	static PhysicsBody CreateFallingBody( int worldSubSteps, out PhysicsWorld world )
	{
		world = new PhysicsWorld
		{
			Gravity = Vector3.Down * 800,
			SleepingEnabled = false,
			SubSteps = worldSubSteps
		};

		var body = new PhysicsBody( world ) { BodyType = PhysicsBodyType.Dynamic, Mass = 1 };
		body.AddSphereShape( Vector3.Zero, 1 );
		return body;
	}
}
