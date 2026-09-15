
namespace Sandbox.Physics;

internal static class PhysicsEngine
{
	internal static void OnActive( PhysicsBodyInternal physicsBody, Transform transform, Vector3 velocity, Vector3 linearVelocity, bool fellAsleep, bool wentOutOfBounds )
	{
		physicsBody.OnActive( transform, velocity, linearVelocity, fellAsleep, wentOutOfBounds );
	}

	internal static void OnPhysicsJointBreak( PhysicsJoint3d joint3d )
	{
		if ( !joint3d.IsValid() ) return;

		joint3d.InternalJointBroken();
	}
}
