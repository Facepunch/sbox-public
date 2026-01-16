namespace Sandbox;

/// <summary>
/// Describes the behavior of network objects.
/// </summary>
[Flags, Expose]
public enum NetworkFlags
{
	None = 0,

	/// <summary>
	/// Disable network transform interpolation for this networked object.
	/// </summary>
	NoInterpolation = 1,

	/// <summary>
	/// Disable position synchronization for the transform of this networked object.
	/// </summary>
	NoPositionSync = 2,

	/// <summary>
	/// Disable rotation synchronization for the transform of this networked object.
	/// </summary>
	NoRotationSync = 4,

	/// <summary>
	/// Disable scale synchronization for the transform of this networked object.
	/// </summary>
	NoScaleSync = 8,

	/// <summary>
	/// Disable synchronization for the entire transform of this networked object.
	/// </summary>
	NoTransformSync = NoPositionSync | NoRotationSync | NoScaleSync
}
