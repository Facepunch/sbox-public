using NativeEngine;

namespace Sandbox;

partial class PhysicsWorld3d
{
	internal override Physics.FixedJoint CreateWeldJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
		=> new( world.AddWeldJoint( body1._body as PhysicsBody3d, body2._body as PhysicsBody3d, localFrame1, localFrame2 ) );

	internal override Physics.SpringJoint CreateSpringJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
		=> new( world.AddSpringJoint( body1._body as PhysicsBody3d, body2._body as PhysicsBody3d, localFrame1, localFrame2 ) );

	internal override Physics.HingeJoint CreateRevoluteJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
		=> new( world.AddRevoluteJoint( body1._body as PhysicsBody3d, body2._body as PhysicsBody3d, localFrame1, localFrame2 ) );

	internal override Physics.SliderJoint CreatePrismaticJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
		=> new( world.AddPrismaticJoint( body1._body as PhysicsBody3d, body2._body as PhysicsBody3d, localFrame1, localFrame2 ) );

	internal override Physics.BallSocketJoint CreateSphericalJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
		=> new( world.AddSphericalJoint( body1._body as PhysicsBody3d, body2._body as PhysicsBody3d, localFrame1, localFrame2 ) );

	internal override Physics.ControlJoint CreateMotorJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
		=> new( world.AddMotorJoint( body1._body as PhysicsBody3d, body2._body as PhysicsBody3d, localFrame1, localFrame2 ) );

	internal override Physics.WheelJoint CreateWheelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
		=> new( world.AddWheelJoint( body1._body as PhysicsBody3d, body2._body as PhysicsBody3d, localFrame1, localFrame2 ) );

	internal override Physics.UprightJoint CreateParallelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
		=> new( world.AddParallelJoint( body1._body as PhysicsBody3d, body2._body as PhysicsBody3d, localFrame1, localFrame2 ) );

	internal override PhysicsJoint CreateFilterJoint( PhysicsBody body1, PhysicsBody body2 )
		=> new( world.AddFilterJoint( body1._body as PhysicsBody3d, body2._body as PhysicsBody3d ) );
}
