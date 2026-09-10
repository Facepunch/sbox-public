namespace Sandbox.Services;

/// <summary>
/// A reward track shown to the player: a set of requirements ("facets") with the
/// player's current progress, and whether they can claim a reward right now. The
/// reward window API returns an array of these — today there's one, but the shape
/// lets us add more tracks (daily/seasonal/event) without breaking the contract.
/// </summary>
public class RewardWindow
{
	/// <summary>Stable identifier for this window (eg "drop").</summary>
	public string Key { get; set; }

	/// <summary>Display title (eg "Reward Drop").</summary>
	public string Title { get; set; }

	/// <summary>True when every facet is met and the player can claim a reward.</summary>
	public bool IsEligible { get; set; }

	/// <summary>The individual requirements and the player's progress against each.</summary>
	public RewardFacet[] Facets { get; set; }
}

/// <summary>
/// A single requirement of a <see cref="RewardWindow"/> together with the player's
/// progress against it. Built for display — a checklist row / progress dots.
/// </summary>
public class RewardFacet
{
	/// <summary>Stable identifier — matches the eligibility reason codes.</summary>
	public string Key { get; set; }

	/// <summary>Short human title (eg "Play on 7 days").</summary>
	public string Title { get; set; }

	/// <summary>One-line explanation of what's required.</summary>
	public string Description { get; set; }

	/// <summary>The player's current value towards the requirement (capped at <see cref="Required"/>).</summary>
	public double Current { get; set; }

	/// <summary>The value needed to satisfy this facet.</summary>
	public double Required { get; set; }

	/// <summary>Whether the player currently meets this requirement.</summary>
	public bool Met { get; set; }
}
