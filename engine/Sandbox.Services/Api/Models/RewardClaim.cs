namespace Sandbox.Services;

/// <summary>
/// The full reward picture for a player: their windows (requirements + progress)
/// and any pending unclaimed offer they should resume. Returned by the rewards GET.
/// </summary>
public class RewardState
{
	/// <summary>The reward tracks with the player's progress against each.</summary>
	public RewardWindow[] Windows { get; set; }

	/// <summary>An open, unclaimed offer to resume — null if there isn't one.</summary>
	public RewardOffer Pending { get; set; }
}

/// <summary>
/// An open reward claim: the candidate items the player may choose from, and how
/// many of them they get to keep. Created by claiming; resolved by choosing.
/// </summary>
public class RewardOffer
{
	/// <summary>The drop this offer belongs to — pass it back when choosing.</summary>
	public long DropId { get; set; }

	/// <summary>How many of <see cref="Items"/> the player gets to keep.</summary>
	public int PickCount { get; set; }

	/// <summary>The items on offer.</summary>
	public RewardItem[] Items { get; set; }
}

/// <summary>A single item, with just enough to draw it in a picker.</summary>
public class RewardItem
{
	public long ItemDefId { get; set; }
	public string Name { get; set; }
	public string Icon { get; set; }
}

/// <summary>The player's pick(s) from an open offer.</summary>
public class RewardChoice
{
	public long DropId { get; set; }
	public long[] ItemDefIds { get; set; }
}

/// <summary>The outcome of choosing — the granted items, or an error reason.</summary>
public class RewardResult
{
	public bool Success { get; set; }

	/// <summary>Failure reason when <see cref="Success"/> is false (eg "already_claimed", "invalid_choice").</summary>
	public string Error { get; set; }

	/// <summary>The items granted to the player's inventory on success.</summary>
	public RewardItem[] Items { get; set; }
}
