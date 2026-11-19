namespace Sandbox.Npcs;

/// <summary>
/// The built-in faction names -- an NPC's faction is its identity to others, like a Source
/// engine NPC's class. These are plain strings so UGC NPCs can reuse them or invent their own;
/// this list is just the common set so content interoperates.
/// </summary>
public static class Factions
{
	public const string Player = "player";
	public const string Ally = "ally";
	public const string Enemy = "enemy";
	public const string Citizen = "citizen";
	public const string Monster = "monster";
}
