/// <summary>
/// Implement on any component that can appear as an attacker in the kill feed.
/// Examples: Player, Npc, explosive barrel, turret, whatever the fuck.
/// </summary>
public interface IKillSource
{
	/// <summary>
	/// Display name
	/// </summary>
	string DisplayName { get; }

	/// <summary>
	/// Steam ID for the local "is-me" highlight. Defaults to 0 (not a player).
	/// </summary>
	long SteamId => default;

	/// <summary>
	/// Entity-type tag passed as <c>attackerTags</c>.
	/// Return an empty string for plain player kills. Examples: "npc"
	/// </summary>
	string Tags => "";

	/// <summary>
	/// Called on the host when this source kills something.
	/// Credit kills, update stats, etc. Default is no-op.
	/// </summary>
	void OnKill( GameObject victim ) { }
}
