namespace Sandbox.Services;

/// <summary>
/// The player's response to a reward drop — which items they chose to take from
/// the <see cref="RewardDropOptions"/> originally offered. Sent up by the game
/// client and validated server-side before being persisted on the drop row.
/// Null until the client has actually picked.
/// </summary>
public class RewardDropChoices
{
	/// <summary>
	/// The ItemDefIds the player picked. Must be a subset of the offered
	/// <see cref="RewardDropOptions.ItemDefIds"/>, with length equal to
	/// <see cref="RewardDropOptions.PickCount"/>.
	/// </summary>
	public List<long> ItemDefIds { get; set; }
}
