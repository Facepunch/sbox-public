namespace Sandbox.Npcs;

/// <summary>
/// Console variables that control NPC AI behaviour globally.
/// </summary>
public static class NpcConVars
{
	/// <summary>
	/// When disabled, all NPC AI thinking is paused — they just stand idle.
	/// </summary>
	[ConVar( "sandbox.ai.enabled", ConVarFlags.Replicated | ConVarFlags.Saved, Help = "Enable or disable NPC AI thinking." )]
	public static bool Enabled { get; set; } = true;

	/// <summary>
	/// When enabled, NPCs cannot target players.
	/// </summary>
	[ConVar( "sandbox.ai.notarget", ConVarFlags.Replicated | ConVarFlags.Saved, Help = "When enabled, NPCs cannot target players." )]
	public static bool NoTarget { get; set; } = false;
}
