namespace Sandbox;

/// <summary>
/// Describes the behaviour of network synchronization.
/// </summary>
[Flags]
public enum SyncFlags : uint
{
	/// <summary>
	/// The host has ownership over the value.
	/// </summary>
	FromHost = 1,

	/// <summary>
	/// Query this value for changes rather than counting on set being called. This is appropriate
	/// if the value returned by its getter can change without calling its setter.
	/// </summary>
	Query = 2,

	/// <summary>
	/// The value will be interpolated between ticks. This is currently only supported for <see cref="float"/>, <see cref="double"/>, <see cref="Angles"/>,
	/// <see cref="Rotation"/>, <see cref="Transform"/>, <see cref="Vector3"/>.
	/// </summary>
	Interpolate = 4,

	/// <summary>
	/// Non-owners may temporarily set this value for client-side prediction.
	/// The value will be overwritten when authoritative data arrives from the owner/host.
	/// </summary>
	Predicted = 8
}
