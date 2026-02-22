
namespace Sandbox.Physics;

internal static class PhysicsEngine
{
	internal static void OnActive( PhysicsBody physicsBody, Transform transform, Vector3 velocity, Vector3 linearVelocity, bool fellAsleep, bool wentOutOfBounds )
	{
		physicsBody.OnActive( transform, velocity, linearVelocity, fellAsleep, wentOutOfBounds );
	}

	internal static void OnPhysicsJointBreak( PhysicsJoint joint )
	{
		if ( !joint.IsValid() ) return;
		joint.InternalJointBroken();
	}
}
