using Facepunch.XR;

namespace Sandbox.VR;

/// <summary>
/// Describes a tracked VR device
/// </summary>
internal record TrackedDevice
{
	/// <summary>
	/// This device's grip pose transform in absolute space (centered on palm/grip)
	/// </summary>
	public Transform Transform;

	/// <summary>
	/// This device's aim pose transform in absolute space (pointing forward)
	/// </summary>
	public Transform AimTransform;

	/// <summary>
	/// Velocity in tracker space in inch/s
	/// </summary>
	public Vector3 Velocity;

	/// <summary>
	/// Angular velocity in degrees/s
	/// </summary>
	public Angles AngularVelocity;

	/// <summary>
	/// Where is this device (left hand, right hand, left ankle, chest, etc.)?
	/// </summary>
	public TrackedDeviceRole DeviceRole;

	/// <summary>
	/// What type of device is this (HMD, controller, tracker, etc.)?
	/// </summary>
	public TrackedDeviceType DeviceType;

	/// <summary>
	/// The input source for this device
	/// </summary>
	internal InputSource InputSource;

	/// <summary>
	/// Handle we should use internally for performance-sensitive calls
	/// </summary>
	internal ulong InputSourceHandle;

	/// <summary>
	/// Is this tracked device currently active (connected)?
	/// </summary>
	internal bool IsActive;

	/// <summary>
	/// Index we can use when referring to poses retrieved through WaitGetPose and similar functions
	/// </summary>
	internal uint DeviceIndex;

	public TrackedDevice( InputSource inputSource )
	{
		InputSource = inputSource;

		DeviceRole = VRSystem.GetTrackedDeviceRoleForInputSource( InputSource );
	}

	/// <summary>
	/// Update this tracked device's position, velocity, etc.
	/// </summary>
	public virtual void Update()
	{
		var gripPoseState = VRSystem.GetPoseActionState( VRSystem.PoseAction.GripPose, InputSource );
		var aimPoseState = VRSystem.GetPoseActionState( VRSystem.PoseAction.AimPose, InputSource );

		Transform = gripPoseState.pose.GetTransform();
		AimTransform = aimPoseState.pose.GetTransform();
		IsActive = gripPoseState.isActive;

		DeviceType = GetDeviceType();
	}

	private TrackedDeviceType GetDeviceType()
	{
		return InputSource switch
		{
			InputSource.Unknown => TrackedDeviceType.Invalid,
			InputSource.Head => TrackedDeviceType.Hmd,
			InputSource.LeftHand => TrackedDeviceType.Controller,
			InputSource.RightHand => TrackedDeviceType.Controller,
			_ => TrackedDeviceType.Invalid,
		};
	}

	public TrackedObject GetTrackedObject()
	{
		return new TrackedObject( this );
	}
}
