using NativeEngine;

namespace Sandbox;

partial class PhysicsWorld2d
{
	static void SetBodies( ref b2JointDef baseDef, PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		baseDef.bodyIdA = (body1._body as PhysicsBody2d).BodyId;
		baseDef.bodyIdB = (body2._body as PhysicsBody2d).BodyId;
		baseDef.localFrameA = localFrame1;
		baseDef.localFrameB = localFrame2;
	}

	internal override Physics.FixedJoint CreateWeldJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		var def = Box2d.b2DefaultWeldJointDef();
		SetBodies( ref def.baseDef, body1, body2, localFrame1, localFrame2 );
		var jointId = Box2d.b2CreateWeldJoint( WorldId, def );
		return new( new PhysicsJoint2d( this, jointId, body1._body as PhysicsBody2d, body2._body as PhysicsBody2d ) );
	}

	internal override Physics.SpringJoint CreateSpringJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		var def = Box2d.b2DefaultDistanceJointDef();
		SetBodies( ref def.baseDef, body1, body2, localFrame1, localFrame2 );
		var jointId = Box2d.b2CreateDistanceJoint( WorldId, def );
		return new( new PhysicsJoint2d( this, jointId, body1._body as PhysicsBody2d, body2._body as PhysicsBody2d ) );
	}

	internal override Physics.HingeJoint CreateRevoluteJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		var def = Box2d.b2DefaultRevoluteJointDef();
		SetBodies( ref def.baseDef, body1, body2, localFrame1, localFrame2 );
		var jointId = Box2d.b2CreateRevoluteJoint( WorldId, def );
		return new( new PhysicsJoint2d( this, jointId, body1._body as PhysicsBody2d, body2._body as PhysicsBody2d ) );
	}

	internal override Physics.SliderJoint CreatePrismaticJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		var def = Box2d.b2DefaultPrismaticJointDef();
		SetBodies( ref def.baseDef, body1, body2, localFrame1, localFrame2 );
		var jointId = Box2d.b2CreatePrismaticJoint( WorldId, def );
		return new( new PhysicsJoint2d( this, jointId, body1._body as PhysicsBody2d, body2._body as PhysicsBody2d ) );
	}

	internal override Physics.BallSocketJoint CreateSphericalJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		var def = Box2d.b2DefaultRevoluteJointDef();
		SetBodies( ref def.baseDef, body1, body2, localFrame1, localFrame2 );
		var jointId = Box2d.b2CreateRevoluteJoint( WorldId, def );
		return new( new PhysicsJoint2d( this, jointId, body1._body as PhysicsBody2d, body2._body as PhysicsBody2d ) );
	}

	internal override Physics.ControlJoint CreateMotorJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		var def = Box2d.b2DefaultMotorJointDef();
		SetBodies( ref def.baseDef, body1, body2, localFrame1, localFrame2 );
		var jointId = Box2d.b2CreateMotorJoint( WorldId, def );
		return new( new PhysicsJoint2d( this, jointId, body1._body as PhysicsBody2d, body2._body as PhysicsBody2d ) );
	}

	internal override Physics.WheelJoint CreateWheelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
	{
		var def = Box2d.b2DefaultWheelJointDef();
		SetBodies( ref def.baseDef, body1, body2, localFrame1, localFrame2 );
		var jointId = Box2d.b2CreateWheelJoint( WorldId, def );
		return new( new PhysicsJoint2d( this, jointId, body1._body as PhysicsBody2d, body2._body as PhysicsBody2d ) );
	}

	internal override Physics.UprightJoint CreateParallelJoint( PhysicsBody body1, PhysicsBody body2, Transform localFrame1, Transform localFrame2 )
		=> throw new NotSupportedException( "Parallel joints are not supported in 2D physics" );

	internal override PhysicsJoint CreateFilterJoint( PhysicsBody body1, PhysicsBody body2 )
	{
		var def = Box2d.b2DefaultFilterJointDef();
		SetBodies( ref def.baseDef, body1, body2, Transform.Zero, Transform.Zero );
		var jointId = Box2d.b2CreateFilterJoint( WorldId, def );
		return new( new PhysicsJoint2d( this, jointId, body1._body as PhysicsBody2d, body2._body as PhysicsBody2d ) );
	}
}
