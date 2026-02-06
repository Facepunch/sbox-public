using Sandbox;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Facepunch.XR;

internal static class Constants
{
	public const int MaxAppNameSize = 128;
	public const int MaxPathSize = 512;
	public const int MaxSystemNameSize = 256;
	public const int HandJointCount = 26;
}

internal enum GraphicsAPI
{
	Undefined = 0,
	Headless = 1,
	Vulkan = 2
}

internal enum EventType
{
	Unknown = 0,
	EventsLost = 1,
	InstanceLossPending = 2,
	InteractionProfileChanged = 3,
	ReferenceSpaceChangePending = 4,
	SessionStateChanged = 5,
}

[StructLayout( LayoutKind.Sequential )]
internal struct Event
{
	public EventType type;
	public IntPtr data;

	public T GetData<T>() where T : struct
	{
		return Marshal.PtrToStructure<T>( data );
	}
}

internal enum SessionState
{
	Unknown = 0,
	Idle = 1,
	Ready = 2,
	Synchronized = 3,
	Visible = 4,
	Focused = 5,
	Stopping = 6,
	LossPending = 7,
	Exiting = 8,
};

[StructLayout( LayoutKind.Sequential )]
internal struct SessionStateChangedEventData
{
	public SessionState state;
}

[StructLayout( LayoutKind.Sequential )]
internal struct VulkanInfo
{
	public IntPtr vkInstance;
	public IntPtr vkPhysicalDevice;
	public IntPtr vkDevice;
	public uint vkQueueIndex;
	public uint vkQueueFamilyIndex;
};

[StructLayout( LayoutKind.Sequential )]
internal struct TextureSubmitInfo
{
	public ulong image;
	public ulong depthImage;

	public uint sampleCount;

	public long format;
	public long depthFormat;

	public TrackedDevicePose poseLeft;
	public TrackedDevicePose poseRight;
};

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

enum DebugCallbackType
{
	Undefined = 0,
	Verbose = 1,
	Info = 2,
	Warning = 3,
	Error = 4
};

enum XRResult
{
	NotImplemented = -1000,
	InvalidViewIndex = -7,
	PathNotFound = -6,
	CompositorNotAttached = -5,
	FrameNotStarted = -4,
	ActionNotFound = -3,
	ActionSetNotFound = -2,
	ErrorOpenXR = -1,
	Error = 0,
	Success = 1,
	NoEventsPending = 2,
	NoSessionRunning = 3,
	NoFrameStarted = 4,
	NoFrameSubmitted = 5,
};

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct InstanceInfo
{
	public fixed byte appName[Constants.MaxAppNameSize];
	public bool useDebugMessenger;
	public GraphicsAPI graphicsApi;
	public fixed byte actionManifestPath[Constants.MaxPathSize];
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

[StructLayout( LayoutKind.Sequential )]
internal struct ViewInfo
{
	public float fovUp;
	public float fovDown;
	public float fovLeft;
	public float fovRight;

	public TrackedDevicePose pose;

	public static ViewInfo Zero => new ViewInfo()
	{
		fovUp = 45,
		fovDown = 45,
		fovLeft = 45,
		fovRight = 45,
		pose = new TrackedDevicePose()
		{
			posx = 0,
			posy = 0,
			posz = 0,
			rotx = 0,
			roty = 0,
			rotz = 0,
			rotw = 1
		}
	};
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct InstanceProperties
{
	public fixed byte systemName[Constants.MaxSystemNameSize];
	public bool supportsHandTracking;
}

[StructLayout( LayoutKind.Sequential )]
internal unsafe struct XrMatrix
{
	public float m0; //float[4][4]
	public float m1;
	public float m2;
	public float m3;
	public float m4;
	public float m5;
	public float m6;
	public float m7;
	public float m8;
	public float m9;
	public float m10;
	public float m11;
	public float m12;
	public float m13;
	public float m14;
	public float m15;

	public Matrix ToMatrix()
	{
		var numerics = new Matrix4x4(
			m0, m1, m2, m3,
			m4, m5, m6, m7,
			m8, m9, m10, m11,
			m12, m13, m14, m15
		);
		return new Matrix { _numerics = numerics };
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
