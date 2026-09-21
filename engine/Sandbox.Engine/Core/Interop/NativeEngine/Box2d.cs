using System.Runtime.InteropServices;

[StructLayout( LayoutKind.Sequential )]
internal struct b2WorldId
{
	public ushort index1;
	public ushort generation;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2BodyId
{
	public int index1;
	public ushort world0;
	public ushort generation;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ShapeId : IEquatable<b2ShapeId>
{
	public int index1;
	public ushort world0;
	public ushort generation;

	public bool Equals( b2ShapeId other ) => index1 == other.index1 && world0 == other.world0 && generation == other.generation;
	public override bool Equals( object obj ) => obj is b2ShapeId other && Equals( other );
	public override int GetHashCode() => HashCode.Combine( index1, world0, generation );
	public static bool operator ==( b2ShapeId a, b2ShapeId b ) => a.Equals( b );
	public static bool operator !=( b2ShapeId a, b2ShapeId b ) => !a.Equals( b );
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ChainId
{
	public int index1;
	public ushort world0;
	public ushort generation;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2JointId
{
	public int index1;
	public ushort world0;
	public ushort generation;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ContactId
{
	public int index1;
	public ushort world0;
	public short padding;
	public uint generation;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2MotionLocks
{
	public byte linearX;
	public byte linearY;
	public byte angularZ;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2WorldDef
{
	public b2Vec2 gravity;
	public float restitutionThreshold;
	public float hitEventThreshold;
	public float contactHertz;
	public float contactDampingRatio;
	public float contactSpeed;
	public float maximumLinearSpeed;
	public IntPtr frictionCallback;
	public IntPtr restitutionCallback;
	[MarshalAs( UnmanagedType.I1 )] public bool enableSleep;
	[MarshalAs( UnmanagedType.I1 )] public bool enableContinuous;
	[MarshalAs( UnmanagedType.I1 )] public bool enableContactSoftening;
	public int workerCount;
	public IntPtr enqueueTask;
	public IntPtr finishTask;
	public IntPtr userTaskContext;
	public IntPtr userData;
	public b2Capacity capacity;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Capacity
{
	public int staticShapeCount;
	public int dynamicShapeCount;
	public int staticBodyCount;
	public int dynamicBodyCount;
	public int contactCount;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2BodyDef
{
	public b2BodyType type;
	public b2Vec2 position;
	public b2Rot rotation;
	public b2Vec2 linearVelocity;
	public float angularVelocity;
	public float linearDamping;
	public float angularDamping;
	public float gravityScale;
	public float sleepThreshold;
	public IntPtr name;
	public IntPtr userData;
	public b2MotionLocks motionLocks;
	[MarshalAs( UnmanagedType.I1 )] public bool enableSleep;
	[MarshalAs( UnmanagedType.I1 )] public bool isAwake;
	[MarshalAs( UnmanagedType.I1 )] public bool isBullet;
	[MarshalAs( UnmanagedType.I1 )] public bool isEnabled;
	[MarshalAs( UnmanagedType.I1 )] public bool allowFastRotation;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ShapeDef
{
	public IntPtr userData;
	public b2SurfaceMaterial material;
	public float density;
	public b2Filter filter;
	[MarshalAs( UnmanagedType.I1 )] public bool enableCustomFiltering;
	[MarshalAs( UnmanagedType.I1 )] public bool isSensor;
	[MarshalAs( UnmanagedType.I1 )] public bool enableSensorEvents;
	[MarshalAs( UnmanagedType.I1 )] public bool enableContactEvents;
	[MarshalAs( UnmanagedType.I1 )] public bool enableHitEvents;
	[MarshalAs( UnmanagedType.I1 )] public bool enablePreSolveEvents;
	[MarshalAs( UnmanagedType.I1 )] public bool invokeContactCreation;
	[MarshalAs( UnmanagedType.I1 )] public bool updateBodyMass;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ChainDef
{
	public IntPtr userData;
	public IntPtr points;
	public int count;
	public IntPtr materials;
	public int materialCount;
	public b2Filter filter;
	[MarshalAs( UnmanagedType.I1 )] public bool isLoop;
	[MarshalAs( UnmanagedType.I1 )] public bool enableSensorEvents;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2JointDef
{
	public IntPtr userData;
	public b2BodyId bodyIdA;
	public b2BodyId bodyIdB;
	public b2Transform localFrameA;
	public b2Transform localFrameB;
	public float forceThreshold;
	public float torqueThreshold;
	public float constraintHertz;
	public float constraintDampingRatio;
	public float drawScale;
	[MarshalAs( UnmanagedType.I1 )] public bool collideConnected;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2DistanceJointDef
{
	public b2JointDef baseDef;
	public float length;
	[MarshalAs( UnmanagedType.I1 )] public bool enableSpring;
	public float lowerSpringForce;
	public float upperSpringForce;
	public float hertz;
	public float dampingRatio;
	[MarshalAs( UnmanagedType.I1 )] public bool enableLimit;
	public float minLength;
	public float maxLength;
	[MarshalAs( UnmanagedType.I1 )] public bool enableMotor;
	public float maxMotorForce;
	public float motorSpeed;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2MotorJointDef
{
	public b2JointDef baseDef;
	public b2Vec2 linearVelocity;
	public float maxVelocityForce;
	public float angularVelocity;
	public float maxVelocityTorque;
	public float linearHertz;
	public float linearDampingRatio;
	public float maxSpringForce;
	public float angularHertz;
	public float angularDampingRatio;
	public float maxSpringTorque;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2FilterJointDef
{
	public b2JointDef baseDef;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2PrismaticJointDef
{
	public b2JointDef baseDef;
	[MarshalAs( UnmanagedType.I1 )] public bool enableSpring;
	public float hertz;
	public float dampingRatio;
	public float targetTranslation;
	[MarshalAs( UnmanagedType.I1 )] public bool enableLimit;
	public float lowerTranslation;
	public float upperTranslation;
	[MarshalAs( UnmanagedType.I1 )] public bool enableMotor;
	public float maxMotorForce;
	public float motorSpeed;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2RevoluteJointDef
{
	public b2JointDef baseDef;
	public float targetAngle;
	[MarshalAs( UnmanagedType.I1 )] public bool enableSpring;
	public float hertz;
	public float dampingRatio;
	[MarshalAs( UnmanagedType.I1 )] public bool enableLimit;
	public float lowerAngle;
	public float upperAngle;
	[MarshalAs( UnmanagedType.I1 )] public bool enableMotor;
	public float maxMotorTorque;
	public float motorSpeed;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2WeldJointDef
{
	public b2JointDef baseDef;
	public float linearHertz;
	public float angularHertz;
	public float linearDampingRatio;
	public float angularDampingRatio;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2WheelJointDef
{
	public b2JointDef baseDef;
	[MarshalAs( UnmanagedType.I1 )] public bool enableSpring;
	public float hertz;
	public float dampingRatio;
	[MarshalAs( UnmanagedType.I1 )] public bool enableLimit;
	public float lowerTranslation;
	public float upperTranslation;
	[MarshalAs( UnmanagedType.I1 )] public bool enableMotor;
	public float maxMotorTorque;
	public float motorSpeed;
	public int internalValue;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ExplosionDef
{
	public ulong maskBits;
	public b2Vec2 position;
	public float radius;
	public float falloff;
	public float impulsePerLength;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2SurfaceMaterial
{
	public float friction;
	public float restitution;
	public float rollingResistance;
	public float tangentSpeed;
	public ulong userMaterialId;
	public uint customColor;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ManifoldPoint
{
	public b2Vec2 clipPoint;
	public b2Vec2 anchorA;
	public b2Vec2 anchorB;
	public float separation;
	public float baseSeparation;
	public float normalImpulse;
	public float tangentImpulse;
	public float totalNormalImpulse;
	public float normalVelocity;
	public ushort id;
	[MarshalAs( UnmanagedType.I1 )] public bool persisted;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Manifold
{
	public b2Vec2 normal;
	public float rollingImpulse;

	public b2ManifoldPoint p0;
	public b2ManifoldPoint p1;

	public int pointCount;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ContactData
{
	public b2ContactId contactId;
	public b2ShapeId shapeIdA;
	public b2ShapeId shapeIdB;
	public b2Manifold manifold;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2BodyEvents
{
	public IntPtr moveEvents;
	public int moveCount;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2SensorBeginTouchEvent
{
	public b2ShapeId sensorShapeId;
	public b2ShapeId visitorShapeId;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2SensorEndTouchEvent
{
	public b2ShapeId sensorShapeId;
	public b2ShapeId visitorShapeId;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2SensorEvents
{
	public IntPtr beginEvents;
	public IntPtr endEvents;
	public int beginCount;
	public int endCount;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ContactEvents
{
	public IntPtr beginEvents;
	public IntPtr endEvents;
	public IntPtr hitEvents;
	public int beginCount;
	public int endCount;
	public int hitCount;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2JointEvent
{
	public b2JointId jointId;
	public IntPtr userData;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2JointEvents
{
	public IntPtr jointEvents;
	public int count;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2RayCastInput
{
	public b2Vec2 origin;
	public b2Vec2 translation;
	public float maxFraction;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Hull
{
	public b2Vec2 p0;
	public b2Vec2 p1;
	public b2Vec2 p2;
	public b2Vec2 p3;
	public b2Vec2 p4;
	public b2Vec2 p5;
	public b2Vec2 p6;
	public b2Vec2 p7;

	public int count;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ShapeProxy
{
	public b2Vec2 p0;
	public b2Vec2 p1;
	public b2Vec2 p2;
	public b2Vec2 p3;
	public b2Vec2 p4;
	public b2Vec2 p5;
	public b2Vec2 p6;
	public b2Vec2 p7;

	public int count;
	public float radius;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2MassData
{
	public float mass;
	public b2Vec2 center;
	public float rotationalInertia;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Circle
{
	public b2Vec2 center;
	public float radius;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Capsule
{
	public b2Vec2 center1;
	public b2Vec2 center2;
	public float radius;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Polygon
{
	public b2Vec2 v0;
	public b2Vec2 v1;
	public b2Vec2 v2;
	public b2Vec2 v3;
	public b2Vec2 v4;
	public b2Vec2 v5;
	public b2Vec2 v6;
	public b2Vec2 v7;

	public b2Vec2 n0;
	public b2Vec2 n1;
	public b2Vec2 n2;
	public b2Vec2 n3;
	public b2Vec2 n4;
	public b2Vec2 n5;
	public b2Vec2 n6;
	public b2Vec2 n7;

	public b2Vec2 centroid;
	public float radius;
	public int count;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Segment
{
	public b2Vec2 point1;
	public b2Vec2 point2;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2ChainSegment
{
	public b2Vec2 ghost1;
	public b2Segment segment;
	public b2Vec2 ghost2;
	public int chainId;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Vec2
{
	public float x;
	public float y;

	public static implicit operator b2Vec2( Vector2 v ) => new() { x = v.x, y = v.y };
	public static implicit operator b2Vec2( Vector3 v ) => new() { x = v.x, y = v.y };
	public static implicit operator Vector2( b2Vec2 v ) => new( v.x, v.y );
	public static implicit operator Vector3( b2Vec2 v ) => new( v.x, v.y );
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Rot
{
	public float c;
	public float s;

	public static implicit operator b2Rot( float radians ) => new() { c = MathF.Cos( radians ), s = MathF.Sin( radians ) };
	public static implicit operator b2Rot( Rotation r ) => r.Yaw().DegreeToRadian();
	public static implicit operator Rotation( b2Rot r ) => Rotation.FromYaw( MathF.Atan2( r.s, r.c ).RadianToDegree() );
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Transform
{
	public b2Vec2 p;
	public b2Rot q;

	public b2Vec2 TransformPoint( b2Vec2 v ) => new()
	{
		x = (q.c * v.x - q.s * v.y) + p.x,
		y = (q.s * v.x + q.c * v.y) + p.y
	};

	public static implicit operator b2Transform( Transform t ) => new() { p = t.Position, q = t.Rotation };
	public static implicit operator Transform( b2Transform t ) => new( t.p, t.q );
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2AABB
{
	public b2Vec2 lowerBound;
	public b2Vec2 upperBound;

	public static implicit operator BBox( b2AABB aabb ) => new( aabb.lowerBound, aabb.upperBound );
	public static implicit operator b2AABB( BBox bbox ) => new() { lowerBound = bbox.Mins, upperBound = bbox.Maxs };
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Filter
{
	public ulong categoryBits;
	public ulong maskBits;
	public int groupIndex;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2QueryFilter
{
	public ulong categoryBits;
	public ulong maskBits;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2TreeStats
{
	public int nodeVisits;
	public int leafVisits;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2RayResult
{
	public b2ShapeId shapeId;
	public b2Vec2 point;
	public b2Vec2 normal;
	public float fraction;
	public int nodeVisits;
	public int leafVisits;
	[MarshalAs( UnmanagedType.I1 )] public bool hit;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2CastOutput
{
	public b2Vec2 normal;
	public b2Vec2 point;
	public float fraction;
	public int iterations;
	[MarshalAs( UnmanagedType.I1 )] public bool hit;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Profile
{
	public float step;
	public float pairs;
	public float collide;
	public float solve;
	public float solverSetup;
	public float constraints;
	public float prepareConstraints;
	public float integrateVelocities;
	public float warmStart;
	public float solveImpulses;
	public float integratePositions;
	public float relaxImpulses;
	public float applyRestitution;
	public float storeImpulses;
	public float splitIslands;
	public float transforms;
	public float sensorHits;
	public float jointEvents;
	public float hitEvents;
	public float refit;
	public float bullets;
	public float sleepIslands;
	public float sensors;
}

[StructLayout( LayoutKind.Sequential )]
internal struct b2Counters
{
	public int bodyCount;
	public int shapeCount;
	public int contactCount;
	public int jointCount;
	public int islandCount;
	public int stackUsed;
	public int staticTreeHeight;
	public int treeHeight;
	public int byteCount;
	public int taskCount;

	public int colorCount0;
	public int colorCount1;
	public int colorCount2;
	public int colorCount3;
	public int colorCount4;
	public int colorCount5;
	public int colorCount6;
	public int colorCount7;
	public int colorCount8;
	public int colorCount9;
	public int colorCount10;
	public int colorCount11;
	public int colorCount12;
	public int colorCount13;
	public int colorCount14;
	public int colorCount15;
	public int colorCount16;
	public int colorCount17;
	public int colorCount18;
	public int colorCount19;
	public int colorCount20;
	public int colorCount21;
	public int colorCount22;
	public int colorCount23;
	public int awakeContactCount;
	public int recycledContactCount;
}

internal enum b2BodyType : int
{
	b2_staticBody = 0,
	b2_kinematicBody = 1,
	b2_dynamicBody = 2
}

internal enum b2ShapeType : int
{
	b2_circleShape = 0,
	b2_capsuleShape = 1,
	b2_segmentShape = 2,
	b2_polygonShape = 3,
	b2_chainSegmentShape = 4
}

internal enum b2JointType : int
{
	b2_distanceJoint = 0,
	b2_filterJoint = 1,
	b2_motorJoint = 2,
	b2_prismaticJoint = 3,
	b2_revoluteJoint = 4,
	b2_weldJoint = 5,
	b2_wheelJoint = 6
}
