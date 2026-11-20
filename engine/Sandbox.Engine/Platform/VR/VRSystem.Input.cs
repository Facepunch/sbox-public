using Facepunch.XR;

namespace Sandbox.VR;

internal static partial class VRSystem
{
	public enum BooleanAction
	{
		JoystickPress,
		ButtonA,
		ButtonB
	};

	public enum FloatAction
	{
		Grip,
		Trigger
	};

	public enum Vector2Action
	{
		Joystick
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

	internal static void TriggerHapticVibration( float duration, float frequency, float amplitude, InputSource source ) { }

	internal static InputPoseHandState GetHandPoseState( InputSource source, MotionRange motionRange ) => default;

	// No headset: every action reads as idle
	public static InputBooleanActionState GetBooleanActionState( BooleanAction action, InputSource inputSource ) => default;
	public static InputVector2ActionState GetVector2ActionState( Vector2Action action, InputSource inputSource ) => default;
	public static InputFloatActionState GetFloatActionState( FloatAction action, InputSource inputSource ) => default;
	public static InputPoseActionState GetPoseActionState( PoseAction action, InputSource inputSource ) => default;

	internal static float GetFingerCurl( InputSource source, FingerValue finger ) => 0.0f;

	internal static bool HandTrackingSupported() => false;
}
