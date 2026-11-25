namespace Sandbox.Npcs;

/// <summary>
/// The built-in faction names -- an NPC's faction is its identity to others.
/// These are plain strings so games can reuse them or invent their own;
/// this list is just the common set so content interoperates.
/// <para>
/// An NPC that does not declare its own relationships takes these defaults from its faction. Allies like players
/// and attack enemies and monsters. Enemies attack players, allies and citizens. Citizens flee enemies and
/// monsters, and monsters attack everyone else. Anything not named here, and any other faction, is ignored.
/// </para>
/// </summary>
[Expose]
public static class Factions
{
	public const string Player = "player";
	public const string Ally = "ally";
	public const string Enemy = "enemy";
	public const string Citizen = "citizen";
	public const string Monster = "monster";

	// How each faction regards the others by default. A pair that is not listed is neutral.
	static readonly Dictionary<string, (string Other, Disposition Stance)[]> _defaultStances = new()
	{
		[Ally] = [(Player, Disposition.Friendly), (Enemy, Disposition.Hostile), (Monster, Disposition.Hostile)],
		[Enemy] = [(Player, Disposition.Hostile), (Ally, Disposition.Hostile), (Citizen, Disposition.Hostile)],
		[Citizen] = [(Enemy, Disposition.Fearful), (Monster, Disposition.Fearful)],
		[Monster] = [(Player, Disposition.Hostile), (Ally, Disposition.Hostile), (Citizen, Disposition.Hostile), (Enemy, Disposition.Hostile)],
	};

	internal static IReadOnlyList<(string Other, Disposition Stance)> GetDefaultStances( string faction )
	{
		return faction is not null && _defaultStances.TryGetValue( faction, out var stances ) ? stances : [];
	}
}
