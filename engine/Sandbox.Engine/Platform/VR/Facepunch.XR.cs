using Sandbox;
using System.Runtime.InteropServices;

namespace Facepunch.XR;

[StructLayout( LayoutKind.Sequential )]
internal struct InputVector2ActionState
{
	public bool isActive;
	public float x;
	public float y;
	public bool changedThisFrame;
}

[StructLayout( LayoutKind.Sequential )]
internal struct InputFloatActionState
{
	public bool isActive;
	public float value;
	public bool changedThisFrame;
}

[StructLayout( LayoutKind.Sequential )]
internal struct InputBooleanActionState
{
	public bool isActive;
	public uint state;
	public bool changedThisFrame;
}

[StructLayout( LayoutKind.Sequential )]
internal struct InputPoseActionState
{
	public bool isActive;

	public TrackedDevicePose pose;
};

enum InputSource
{
	Unknown = 0,

	Head = 1,
	LeftHand = 2,
	RightHand = 3,
};

[StructLayout( LayoutKind.Sequential )]
internal struct TrackedDevicePose
{
	public float posx;
	public float posy;
	public float posz;

	public float rotx;
	public float roty;
	public float rotz;
	public float rotw;

	public Transform GetTransform()
	{
		// OpenXR uses a Cartesian right-handed coordinate system (y-up)
		// We use a right-handed coordinate system with z-up
		var up = posy;
		var forward = -posz;
		var left = -posx;

		var pos = new Vector3( forward, left, up ) * 1f.MeterToInch();

		var rx = -rotz;
		var ry = -rotx;
		var rz = roty;
		var rot = new Rotation( rx, ry, rz, rotw );

		return new Transform( pos, rot, 1.0f );
	}

	public static TrackedDevicePose FromTransform( Transform t )
	{
		var pose = new TrackedDevicePose();

		// OpenXR uses a Cartesian right-handed coordinate system (y-up)
		// We use a right-handed coordinate system with z-up
		var forward = t.Position.x;
		var left = t.Position.y;
		var up = t.Position.z;

		pose.posz = -forward * 1f.InchToMeter();
		pose.posx = -left * 1f.InchToMeter();
		pose.posy = up * 1f.InchToMeter();

		pose.rotz = -t.Rotation.x;
		pose.rotx = -t.Rotation.y;
		pose.roty = t.Rotation.z;
		pose.rotw = t.Rotation.w;

		return pose;
	}
}

internal enum HandPoseLevel
{
	Unknown = 0,
	Controller = 1,
	FullyTracked = 2
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct HandPoseJoint
{
	public TrackedDevicePose pose;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct InputPoseHandState
{
	public HandPoseLevel handPoseLevel;

	// 26 joints
	public HandPoseJoint joint0;
	public HandPoseJoint joint1;
	public HandPoseJoint joint2;
	public HandPoseJoint joint3;
	public HandPoseJoint joint4;
	public HandPoseJoint joint5;
	public HandPoseJoint joint6;
	public HandPoseJoint joint7;
	public HandPoseJoint joint8;
	public HandPoseJoint joint9;
	public HandPoseJoint joint10;
	public HandPoseJoint joint11;
	public HandPoseJoint joint12;
	public HandPoseJoint joint13;
	public HandPoseJoint joint14;
	public HandPoseJoint joint15;
	public HandPoseJoint joint16;
	public HandPoseJoint joint17;
	public HandPoseJoint joint18;
	public HandPoseJoint joint19;
	public HandPoseJoint joint20;
	public HandPoseJoint joint21;
	public HandPoseJoint joint22;
	public HandPoseJoint joint23;
	public HandPoseJoint joint24;
	public HandPoseJoint joint25;

	public readonly HandPoseJoint this[int index] => index switch
	{
		// I really really hate this - but we can't use `fixed` on non-primitive types
		// We don't want to use an array here either because otherwise we'll make an allocation
		// every frame
		0 => joint0,
		1 => joint1,
		2 => joint2,
		3 => joint3,
		4 => joint4,
		5 => joint5,
		6 => joint6,
		7 => joint7,
		8 => joint8,
		9 => joint9,
		10 => joint10,
		11 => joint11,
		12 => joint12,
		13 => joint13,
		14 => joint14,
		15 => joint15,
		16 => joint16,
		17 => joint17,
		18 => joint18,
		19 => joint19,
		20 => joint20,
		21 => joint21,
		22 => joint22,
		23 => joint23,
		24 => joint24,
		25 => joint25,
		_ => throw new IndexOutOfRangeException()
	};
}
