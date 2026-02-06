using Facepunch.XR;

namespace Sandbox.VR;

internal static unsafe partial class VRSystem
{
	private static readonly string[] BooleanActionStrings =
	{
		"/actions/default/in/joystick_button",
		"/actions/default/in/button_a",
		"/actions/default/in/button_b"
	};

	public enum BooleanAction
	{
		JoystickPress,
		ButtonA,
		ButtonB
	};

	private static readonly string[] FloatActionStrings =
	{
		"/actions/default/in/grip",
		"/actions/default/in/trigger",
	};

	public enum FloatAction
	{
		Grip,
		Trigger
	};

	private static readonly string[] Vector2ActionStrings =
	{
		"/actions/default/in/joystick",
	};

	public enum Vector2Action
	{
		Joystick
	};

	private static readonly string[] PoseActionStrings =
	{
		"/actions/default/in/hand_pose",
		"/actions/default/in/aim_pose",
	};

	public enum PoseAction
	{
		/// <summary>
		/// The grip pose - centered on the palm/grip of the controller
		/// </summary>
		GripPose,

		/// <summary>
		/// The aim pose - pointing forward, suitable for aiming/pointing
		/// </summary>
		AimPose
	};

	private static readonly string[] HapticActionStrings =
	{
		"/actions/default/out/vibrate_left",
		"/actions/default/out/vibrate_right",
	};

	public enum HapticAction
	{
		LeftHandHaptics,
		RightHandHaptics
	};

	internal enum TrackedControllerType
	{
		Unknown,
		HTCVive,
		HTCViveFocus3,
		HTCViveCosmos,
		MetaTouch,
		ValveKnuckles,
		WindowsMixedReality,
		HPReverbG2,
		Generic
	}

	private static readonly Dictionary<string, TrackedControllerType> ControllerStringToTypeMap = new()
	{
		{ "unknown", TrackedControllerType.Unknown },
		{ "vive_controller", TrackedControllerType.HTCVive },
		{ "vive_focus3_controller", TrackedControllerType.HTCViveFocus3 },
		{ "vive_cosmos_controller", TrackedControllerType.HTCViveCosmos },
		{ "oculus_touch", TrackedControllerType.MetaTouch },
		{ "knuckles", TrackedControllerType.ValveKnuckles },
		{ "holographic_controller", TrackedControllerType.WindowsMixedReality },
		{ "hpmotioncontroller", TrackedControllerType.HPReverbG2 },
		{ "generic_tracked", TrackedControllerType.Generic },
	};

	internal static TrackedControllerType ControllerTypeFromString( string str )
	{
		if ( str == null )
			return TrackedControllerType.Unknown;

		if ( ControllerStringToTypeMap.TryGetValue( str, out var type ) )
			return type;

		return TrackedControllerType.Unknown;
	}

	internal enum HMDType
	{
		Unknown,
		HTC,
		Valve,
		Oculus,
		Pico,
		HP,
		WindowsMixedReality,
		Bigscreen,
		Pimax,
	}

	private static readonly Dictionary<string, HMDType> HMDStringToTypeMap = new()
	{
		{ "unknown", HMDType.Unknown },
		{ "HTC", HMDType.HTC },
		{ "Valve", HMDType.Valve },
		{ "Oculus", HMDType.Oculus },
		{ "Meta", HMDType.Oculus },
		{ "Pico", HMDType.Pico },
		{ "HP", HMDType.HP },
		{ "WindowsMR", HMDType.WindowsMixedReality },
		{ "Bigscreen", HMDType.Bigscreen },
		{ "Pimax", HMDType.Pimax }
	};

	internal static HMDType GetHMDType()
	{
		var identifier = GetSystemName();

		if ( identifier == null )
			return HMDType.Unknown;

		if ( HMDStringToTypeMap.TryGetValue( identifier, out var type ) )
			return type;

		return HMDType.Unknown;
	}

	internal static Transform GetOffsetForDeviceRole( TrackedDeviceRole role )
	{
		return role switch
		{
			TrackedDeviceRole.LeftHand => new Transform( new Vector3( 5f, -2f, -3f ), Rotation.From( 10, -10, 90 ) ),
			TrackedDeviceRole.RightHand => new Transform( new Vector3( 5f, 2f, -3f ), Rotation.From( 10, 10, -90 ) ),
			_ => Transform.Zero
		};
	}

	internal static TrackedDeviceRole GetTrackedDeviceRoleForInputSource( InputSource source )
	{
		return source switch
		{
			InputSource.LeftHand => TrackedDeviceRole.LeftHand,
			InputSource.RightHand => TrackedDeviceRole.RightHand,
			InputSource.Head => TrackedDeviceRole.Head,
			_ => TrackedDeviceRole.Unknown,
		};
	}

	internal static void TriggerHapticVibration( float duration, float frequency, float amplitude, InputSource source )
	{
		FpxrCheck( Input.TriggerHapticVibration( duration, frequency, amplitude, source ) );
	}

	internal static InputPoseHandState GetHandPoseState( InputSource source, MotionRange motionRange )
	{
		FpxrCheck( Input.GetHandPoseState( source, motionRange, out var state ) );
		return state;
	}

	/// <summary>
	/// Per-finger constants for curl calculation.
	/// minDistance/maxDistance define the range of tip-to-palm distances.
	/// </summary>
	private readonly struct FingerCurlConstants
	{
		public readonly float MinDistance;
		public readonly float MaxDistance;
		public readonly VRHandJoint TipJoint;

		public FingerCurlConstants( float minDistance, float maxDistance, VRHandJoint tipJoint )
		{
			MinDistance = minDistance;
			MaxDistance = maxDistance;
			TipJoint = tipJoint;
		}
	}

	private static readonly FingerCurlConstants[] FingerConstants = new FingerCurlConstants[]
	{
		new( 0.10922f, 0.1016f, VRHandJoint.ThumbTip ),   // Thumb
		new( 0.14478f, 0.0508f, VRHandJoint.IndexTip ),   // Index
		new( 0.14478f, 0.0508f, VRHandJoint.MiddleTip ),  // Middle
		new( 0.14478f, 0.0508f, VRHandJoint.RingTip ),    // Ring
		new( 0.11303f, 0.0889f, VRHandJoint.LittleTip ),  // Pinky
	};

	internal static float GetFingerCurl( InputSource source, FingerValue finger )
	{
		if ( source != InputSource.LeftHand && source != InputSource.RightHand )
			return 0.0f;

		int fingerIndex = (int)finger;
		if ( fingerIndex < 0 || fingerIndex >= FingerConstants.Length )
			return 0.0f;

		var constants = FingerConstants[fingerIndex];
		var handState = GetHandPoseState( source, MotionRange.Hand );

		var tipPose = handState[(int)constants.TipJoint].pose;
		var palmPose = handState[(int)VRHandJoint.Palm].pose;

		float dx = tipPose.posx - palmPose.posx;
		float dy = tipPose.posy - palmPose.posy;
		float dz = tipPose.posz - palmPose.posz;
		float distance = MathF.Sqrt( dx * dx + dy * dy + dz * dz );

		// Remap distance to 0-1 curl value
		float curl = (distance - constants.MinDistance) / (constants.MaxDistance - constants.MinDistance);
		return curl;
	}

	internal static bool HandTrackingSupported()
	{
		return InstanceProperties.supportsHandTracking;
	}
}
