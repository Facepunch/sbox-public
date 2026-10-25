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
	/// Poll the getter for changes that occur without calling the setter.
	/// Retained for compatibility; prefer assigning synchronized values through their property setters.
	/// </summary>
	[Obsolete( "SyncFlags.Query polling is obsolete. Assign synchronized values through their property setters instead." )]
	Query = 2,

	/// <summary>
	/// The value will be interpolated between ticks. This is currently only supported for <see cref="float"/>, <see cref="double"/>, <see cref="Angles"/>,
	/// <see cref="Rotation"/>, <see cref="Transform"/>, <see cref="Vector3"/>.
	/// </summary>
	Interpolate = 4
}
