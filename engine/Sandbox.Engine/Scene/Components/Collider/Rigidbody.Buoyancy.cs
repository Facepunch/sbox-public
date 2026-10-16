namespace Sandbox;

partial class Rigidbody
{
	/// <summary>
	/// Temporarily suppress fluid impulses while a movement mode owns the body's
	/// trajectory. The owning mode must release this when it ends or is disabled.
	/// </summary>
	internal bool SuppressBuoyancy { get; set; }

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
		if ( SuppressBuoyancy || !PhysicsBody.IsValid() )
			return;

		var gravity = Scene.PhysicsWorld.Gravity;

		PhysicsBody._body.ApplyBuoyancy( plane, fluidDensity, linearDrag, angularDrag, fluidVelocity, gravity, dt );
	}
}
