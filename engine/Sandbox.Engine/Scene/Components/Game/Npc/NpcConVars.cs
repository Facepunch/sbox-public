namespace Sandbox.Npcs;

/// <summary>
/// Console variables that control NPC AI globally.
/// </summary>
public static class NpcConVars
{
	/// <summary>
	/// When off, NPCs drop what they are doing and stand idle: no schedules, no moves, no looking around.
	/// </summary>
	[ConVar( "npc_enabled", ConVarFlags.Replicated, Help = "Enable or disable NPC AI. Disabled NPCs stand idle." )]
	public static bool Enabled { get; set; } = true;

	/// <summary>
	/// When on, NPCs don't sense players.
	/// </summary>
	[ConVar( "npc_notarget", ConVarFlags.Replicated, Help = "When enabled, NPCs cannot target players." )]
	public static bool NoTarget { get; set; } = false;
}
