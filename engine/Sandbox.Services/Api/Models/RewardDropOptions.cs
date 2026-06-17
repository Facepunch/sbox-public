namespace Sandbox.Services;

/// <summary>
/// The set of items offered to a player for a reward drop, and how many they
/// get to pick. Authored server-side, shipped to the client so the picker UI
/// and server validate against the same shape.
/// </summary>
public struct RewardDropOptions
{
	/// <summary>
	/// The ItemDefIds the player may choose from. For a single-item drop this
	/// is a single-element list.
	/// </summary>
	public List<long> ItemDefIds { get; set; }

	/// <summary>
	/// How many items the player gets to take from <see cref="ItemDefIds"/>.
	/// </summary>
	public int PickCount { get; set; }
}
