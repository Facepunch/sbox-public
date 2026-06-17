
namespace PhysicsTests;

[TestClass]
public class MassTest
{
	/// <summary>
	/// Tests to ensure mass override doesn't get reset
	/// </summary>
	[TestMethod]
	public void MassOverride()
	{
		var world = new PhysicsWorld();

		var body = new PhysicsBody( world );

		var massOverride = 10.0f;
		body.Mass = massOverride;
		body.BodyType = PhysicsBodyType.Dynamic;
		body.AddSphereShape( 0, 100 );

		Assert.AreEqual( massOverride, body.Mass );

		massOverride = 1234.0f;
		body.Mass = 0;
		var massComputed = body.Mass;
		body.Mass = massOverride;

		Assert.AreEqual( massOverride, body.Mass );

		body.Mass = 0;

		Assert.AreEqual( massComputed, body.Mass );

		body.Mass = massOverride;
		body.LocalMassCenter = Vector3.Up * 100;

		Assert.AreEqual( massOverride, body.Mass );
	}
}
