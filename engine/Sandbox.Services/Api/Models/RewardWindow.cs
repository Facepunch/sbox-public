namespace Sandbox.Services;

/// <summary>
/// Current weekly challenge and the player's achievement progress.
/// </summary>
public class RewardWindow
{
	/// <summary>
	/// Stable track identifier, retained as "drop" for existing clients.
	/// </summary>
	public string Key { get; set; }

	/// <summary>
	/// Display title of the challenge.
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// Continuous week number, with zero beginning Sunday 4 January 2026 at 00:00 UTC.
	/// </summary>
	public int WeekNumber { get; set; }

	/// <summary>
	/// Inclusive Sunday opening time in UTC.
	/// </summary>
	public DateTimeOffset StartsAt { get; set; }

	/// <summary>
	/// Exclusive deadline for opening this week's reward.
	/// </summary>
	public DateTimeOffset EndsAt { get; set; }

	/// <summary>
	/// True when the player can open a new reward for this week.
	/// </summary>
	public bool IsEligible { get; set; }

	/// <summary>
	/// Eligibility reason, including disabled, not_game_owner, untrusted and already_claimed.
	/// </summary>
	public string Reason { get; set; }

	/// <summary>
	/// Offer already opened for this week, whether or not its item has been chosen.
	/// </summary>
	public long? RewardDropId { get; set; }

	/// <summary>
	/// True when this week's chosen reward has been granted through Steam.
	/// </summary>
	public bool IsClaimed { get; set; }

	/// <summary>
	/// Every listed objective is required to earn the weekly reward.
	/// </summary>
	public RewardFacet[] Facets { get; set; }
}

/// <summary>
/// An achievement objective and its game, including credit for an existing unlock.
/// </summary>
public class RewardFacet
{
	/// <summary>
	/// Stable objective identifier.
	/// </summary>
	public string Key { get; set; }

	/// <summary>
	/// Combined game and achievement display name for older clients.
	/// </summary>
	public string Title { get; set; }

	/// <summary>
	/// Achievement's description of what the player must do.
	/// </summary>
	public string Description { get; set; }

	/// <summary>
	/// Published game identifier used for launch and detail links.
	/// </summary>
	public string GameIdent { get; set; }

	/// <summary>
	/// Display name of the game containing the achievement.
	/// </summary>
	public string GameTitle { get; set; }

	/// <summary>
	/// Achievement identifier within the game.
	/// </summary>
	public string AchievementName { get; set; }

	/// <summary>
	/// Achievement display name without the game title.
	/// </summary>
	public string AchievementTitle { get; set; }

	/// <summary>
	/// Achievement icon URL, when one is configured.
	/// </summary>
	public string Icon { get; set; }

	/// <summary>
	/// One if unlocked, otherwise zero.
	/// </summary>
	public double Current { get; set; }

	/// <summary>
	/// Required completion count, currently always one.
	/// </summary>
	public double Required { get; set; }

	/// <summary>
	/// Whether the player has unlocked this achievement.
	/// </summary>
	public bool Met { get; set; }
}
