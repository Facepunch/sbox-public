/// <summary>
/// Server-controlled settings replicated to all clients.
/// </summary>
public static partial class ServerSettings
{
	/// <summary>
	/// When enabled, developer entities are visible in the spawn menu for all players.
	/// Always on in the editor regardless of this value.
	/// </summary>
	[Title( "Show Developer Entities" )]
	[Group( "Other" )]
	[ConVar( "sandbox.developer.entities", ConVarFlags.Replicated | ConVarFlags.GameSetting | ConVarFlags.Server, Help = "Show developer entities in the spawn menu." )]
	public static bool DeveloperEntities { get; set; } = false;

	/// <summary>
	/// On respawn, players get back the weapons they were carrying instead of the starter items.
	/// </summary>
	[Title( "Re-equip Last" )]
	[Group( "Gameplay" ), Section( "Players" ), Order( 3 )]
	[ConVar( "sandbox.reequiplast", ConVarFlags.Replicated | ConVarFlags.GameSetting | ConVarFlags.Server, Help = "Give players back the weapons they were carrying when they respawn." )]
	public static bool ReEquipLast { get; set; } = false;

	/// <summary>
	/// Players with the GodMode permission can use it even when this is off.
	/// </summary>
	[Title( "Allow God Mode" )]
	[Group( "Gameplay" ), Section( "Players" ), Order( 0 )]
	[ConVar( "sandbox.allow.godmode", ConVarFlags.Replicated | ConVarFlags.GameSetting | ConVarFlags.Server, Help = "Players can toggle god mode for themselves with the god command." )]
	public static bool AllowGodMode { get; set; } = false;

	/// <summary>
	/// See DamageBlame for how damage is traced back to a player.
	/// </summary>
	[Title( "Allow PvP" )]
	[Group( "Gameplay" ), Section( "Players" ), Order( 1 )]
	[ConVar( "sandbox.allow.pvp", ConVarFlags.Replicated | ConVarFlags.GameSetting | ConVarFlags.Server, Help = "Players can damage each other." )]
	public static bool AllowPvp { get; set; } = true;

	/// <summary>
	/// Players with the Noclip permission can use it even when this is off.
	/// </summary>
	[Title( "Allow Noclip" )]
	[Group( "Gameplay" ), Section( "Players" ), Order( 2 )]
	[ConVar( "sandbox.allow.noclip", ConVarFlags.Replicated | ConVarFlags.GameSetting | ConVarFlags.Server, Help = "Players can toggle noclip." )]
	public static bool AllowNoclip { get; set; } = true;

	/// <summary>
	/// Player ragdolls stay until a cleanup instead of despawning.
	/// </summary>
	[Title( "Keep Player Corpses" )]
	[Group( "Gameplay" ), Section( "World" ), Order( 11 )]
	[ConVar( "sandbox.keepcorpses.players", ConVarFlags.Replicated | ConVarFlags.GameSetting | ConVarFlags.Server, Help = "Player corpses stay until a cleanup instead of despawning." )]
	public static bool KeepPlayerCorpses { get; set; } = false;

	/// <summary>
	/// NPC ragdolls stay until a cleanup instead of despawning.
	/// </summary>
	[Title( "Keep NPC Corpses" )]
	[Group( "Gameplay" ), Section( "World" ), Order( 12 )]
	[ConVar( "sandbox.keepcorpses.npcs", ConVarFlags.Replicated | ConVarFlags.GameSetting | ConVarFlags.Server, Help = "NPC corpses stay until a cleanup instead of despawning." )]
	public static bool KeepNpcCorpses { get; set; } = false;

	[Permission, Title( "God Mode" ), Description( "Use god mode when it's off for everyone." )]
	public const string GodMode = "sandbox.godmode";

	[Permission, Title( "Noclip" ), Description( "Noclip when it's off for everyone." )]
	public const string Noclip = "sandbox.noclip";

	/// <summary>
	/// Returns true if developer entities should be shown.
	/// </summary>
	public static bool ShowDeveloperEntities => Game.IsEditor || DeveloperEntities;

	public static bool CanUseGodMode( Connection connection ) => Permissions.Allows( connection, AllowGodMode, GodMode );

	public static bool CanNoclip( Connection connection ) => Permissions.Allows( connection, AllowNoclip, Noclip );
}
