namespace Sandbox;

partial class Rigidbody
{
	/// <summary>
	/// Applies buoyancy and drag to the rigidbody relative to a plane to simulate things floating in water.
	/// </summary>
	public void ApplyBuoyancy( Plane plane, float dt )
	{
		ApplyBuoyancy( plane, 1000.0f, 0.1f, 0.5f, Vector3.Zero, dt );
	}

	/// <summary>
	/// Applies buoyancy and drag to the rigidbody relative to a plane to simulate things floating in water.
	/// </summary>
	public void ApplyBuoyancy( Plane plane, float fluidDensity, float linearDrag, float angularDrag, Vector3 fluidVelocity, float dt )
	{
		if ( !PhysicsBody.IsValid() ) return;

		var gravity = Scene.PhysicsWorld.Gravity;

		PhysicsBody.native.ApplyBuoyancyImpulse( plane.Position, plane.Normal, fluidDensity, linearDrag, angularDrag, fluidVelocity, gravity, dt );
	}
}
