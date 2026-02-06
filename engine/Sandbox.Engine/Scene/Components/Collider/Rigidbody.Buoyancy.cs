namespace Sandbox;

partial class Rigidbody
{
	/// <summary>
	/// Applies buoyancy and drag to the rigidbody relative to a plane to simulate things floating in water.
	/// </summary>
	public void ApplyBuoyancy( Plane plane, float dt )
	{
		if ( PhysicsBody.IsValid() == false ) return;

		var buoyancySum = 0.0f;
		var linearDragSum = 0.0f;
		var angularDragSum = 0.0f;
		var count = 0;

		foreach ( var shape in PhysicsBody.Shapes )
		{
			if ( shape.IsValid() == false ) continue;

			var surface = shape.Surface;
			buoyancySum += 1000.0f / surface.Density;
			linearDragSum += surface.FluidLinearDrag;
			angularDragSum += surface.FluidAngularDrag;
			count++;
		}

		if ( count == 0 ) return;

		var buoyancy = buoyancySum / count;
		var linearDrag = linearDragSum / count;
		var angularDrag = angularDragSum / count;
		var gravity = PhysicsBody.World.Gravity * PhysicsBody.GravityScale;

		PhysicsBody.native.ApplyBuoyancyImpulse( plane.Position, plane.Normal, buoyancy, linearDrag, angularDrag, Vector3.Zero, gravity, dt );
	}
}
