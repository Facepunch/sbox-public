using Facepunch.XR;

namespace Sandbox.VR;

/// <summary>
/// Represents a VR controller, along with its transform, velocity, and inputs.
/// </summary>
public sealed partial record VRController : TrackedObject
{
	internal Vector3 Position => Transform.Position;
	internal Rotation Rotation => Transform.Rotation;

	internal VRSystem.TrackedControllerType _type;

	internal VRController( TrackedDevice trackedDevice ) : base( trackedDevice ) { }

	private Transform _transform;
	private Transform _aimTransform;

	/// <summary>
	/// The grip pose transform in world space (centered on palm/grip).
	/// </summary>
	public override Transform Transform => Input.VR.Anchor.ToWorld( _transform );

	/// <summary>
	/// The aim pose transform in world space (pointing forward).
	/// </summary>
	public override Transform AimTransform => Input.VR.Anchor.ToWorld( _aimTransform );

	/// <summary>
	/// Is this controller currently being represented using full hand tracking?
	/// </summary>
	public bool IsHandTracked { get; internal set; }

	internal override void Update()
	{
		base.Update();

		UpdateHaptics();

		Trigger = new AnalogInput( Trigger, VRSystem.FloatAction.Trigger, _trackedDevice.InputSource );
		Grip = new AnalogInput( Grip, VRSystem.FloatAction.Grip, _trackedDevice.InputSource );
		Joystick = new AnalogInput2D( Joystick, VRSystem.Vector2Action.Joystick, _trackedDevice.InputSource );
		JoystickPress = new DigitalInput( JoystickPress, VRSystem.BooleanAction.JoystickPress, _trackedDevice.InputSource );
		ButtonA = new DigitalInput( ButtonA, VRSystem.BooleanAction.ButtonA, _trackedDevice.InputSource );
		ButtonB = new DigitalInput( ButtonB, VRSystem.BooleanAction.ButtonB, _trackedDevice.InputSource );

		_handJoints.Pose = VRSystem.GetHandPoseState( _trackedDevice.InputSource, MotionRange.Hand );
		_conformingJoints.Pose = VRSystem.GetHandPoseState( _trackedDevice.InputSource, MotionRange.Controller );
		IsHandTracked = _handJoints.Pose.handPoseLevel == HandPoseLevel.FullyTracked;

		_transform = _trackedDevice.Transform;
		_aimTransform = _trackedDevice.AimTransform;
	}

	/// <summary>
	/// Retrieves or creates a cached model that can be used to render this controller.
	/// </summary>
	public Model GetModel()
	{
		return Model.Cube;
	}
}
