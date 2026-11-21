namespace Sandbox.VR;

/// <summary>
/// VR is dropped: there's no OpenXR runtime behind this any more, so no headset is ever found and VR is never active.
/// What the public VR API reads (<see cref="Input.VR"/>, the VR components, <c>Game.IsRunningInVR</c>) sees no headset.
/// </summary>
internal static partial class VRSystem
{
	public enum States
	{
		Inactive,
		Standby,
		Active
	}

	public static States State => States.Inactive;
	public static bool IsActive => false;

	public static float IPDInches => 0f;
	public static float RefreshRate => 0f;
	public static List<TrackedDevice> TrackedDevices { get; } = new();

	public static float WorldScale { get; set; } = 1.0f;
	public static bool HasHeadset => false;

	public static void Init() { }
	internal static void FrameStart() { }
	internal static void FrameEnd() { }
	public static void Enable() { }
	public static void Disable() { }

	internal static void Reset()
	{
		WorldScale = 1.0f;
	}

	internal static Transform GetHeadTransform() => Transform.Zero;
}
