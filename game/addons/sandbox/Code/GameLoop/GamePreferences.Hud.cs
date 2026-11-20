/// <summary>
/// What the local player's HUD shows and how it looks, edited from the HUD tab of the inspect menu
/// </summary>
internal static partial class GamePreferences
{
	/// <summary>
	/// This controls visibility of all HUD sections, such as health, ammo, hotbar...
	/// </summary>
	[ConVar( "sandbox.hud_draw", ConVarFlags.Saved )]
	[Group( "HUD" )]
	public static bool DrawHud { get; set; } = true;

	[ConVar( "sandbox.hud_health", ConVarFlags.Saved )]
	[Group( "HUD" )]
	public static bool DrawHealth { get; set; } = true;

	[ConVar( "sandbox.hud_ammo", ConVarFlags.Saved )]
	[Group( "HUD" )]
	public static bool DrawAmmo { get; set; } = true;

	[ConVar( "sandbox.hud_hotbar", ConVarFlags.Saved )]
	[Group( "HUD" )]
	public static bool DrawHotbar { get; set; } = true;

	[ConVar( "sandbox.hud_color", ConVarFlags.Saved )]
	[Group( "Appearance" )]
	public static string HudColor { get; set; } = "";

	public static Color? HudColorOverride => ParseColor( HudColor );

	/// <summary>
	/// Intensity of glow on HUD elements
	/// </summary>
	[ConVar( "sandbox.hud_glow", ConVarFlags.Saved )]
	[Range( 0f, 1f ), Step( 0.05f ), Group( "Appearance" )]
	public static float HudGlow { get; set; } = 1f;

	/// <summary>
	/// Controls how large your HUD on screen will be
	/// </summary>
	[ConVar( "sandbox.hud_scale", ConVarFlags.Saved )]
	[Range( 0.5f, 1.5f ), Step( 0.05f ), Group( "Appearance" )]
	public static float HudScale { get; set; } = 1f;

	/// <summary>
	/// Spacing between HUD elements and screen borders
	/// </summary>
	[ConVar( "sandbox.hud_margin", ConVarFlags.Saved )]
	[Range( 0f, 10f ), Step( 0.25f ), Group( "Appearance" )]
	public static float HudMargin { get; set; } = 2f;

	/// <summary>
	/// Tool panel shows current tool name, its description, and available key hints
	/// </summary>
	[ConVar( "sandbox.hud_toolinfo", ConVarFlags.Saved )]
	[Group( "Interface" )]
	public static bool DrawToolInfo { get; set; } = true;

	/// <summary>
	/// Draw notifications on the right side of screen
	/// </summary>
	[ConVar( "sandbox.hud_notices", ConVarFlags.Saved )]
	[Group( "Interface" )]
	public static bool DrawNotices { get; set; } = true;

	[ConVar( "sandbox.hud_crosshair", ConVarFlags.Saved )]
	[Group( "Interface" )]
	public static bool DrawCrosshair { get; set; } = true;

	[ConVar( "sandbox.hud_killfeed", ConVarFlags.Saved )]
	[Group( "Interface" )]
	public static bool DrawKillFeed { get; set; } = true;

	/// <summary>
	/// These ones show up when you aim at interactable stuff, like chairs, NPCs and other entity things
	/// </summary>
	[ConVar( "sandbox.hud_interaction", ConVarFlags.Saved )]
	[Group( "Interface" )]
	public static bool DrawInteractionHints { get; set; } = true;

	[ConVar( "sandbox.hud_chat", ConVarFlags.Saved )]
	[Group( "Interface" )]
	public static bool DrawChatMessages { get; set; } = true;

	/// <summary>
	/// Draws the list of who is talking
	/// </summary>
	[ConVar( "sandbox.hud_voices", ConVarFlags.Saved )]
	[Group( "Interface" )]
	public static bool DrawVoices { get; set; } = true;

	/// <summary>
	/// Nameplates show up above player models, so you know who's standing in front of you 
	/// </summary>
	[ConVar( "sandbox.hud_nameplates", ConVarFlags.Saved )]
	[Group( "Interface" )]
	public static bool DrawNameplates { get; set; } = true;

	/// <summary>
	/// This shows up in inspect menu when you aim at the prop, it tells you who spawned this prop/entity/whatever...
	/// </summary>
	[ConVar( "sandbox.hud_ownerlabel", ConVarFlags.Saved )]
	[Group( "Interface" )]
	public static bool DrawOwnerLabel { get; set; } = true;

	/// <summary>
	/// When disabled, you need to hold the key to keep spawnlist/inspect menus open.
	/// When enabled, you just need to press the key once to open it, and then press it again to close the menu.
	/// </summary>
	[ConVar( "sandbox.menu_toggle", ConVarFlags.Saved )]
	[Group( "Menus" )]
	public static bool ToggleMenus { get; set; } = false;

	public static HudElement HiddenHud
	{
		get
		{
			var hidden = HudElement.None;
			if ( !DrawHud || !DrawHealth ) hidden |= HudElement.Vitals;
			if ( !DrawHud || !DrawAmmo ) hidden |= HudElement.Ammo | HudElement.AmmoPickups;
			if ( !DrawHud || !DrawHotbar ) hidden |= HudElement.Inventory;
			if ( !DrawCrosshair ) hidden |= HudElement.Crosshair;
			if ( !DrawToolInfo ) hidden |= HudElement.ToolInfo;
			if ( !DrawNotices ) hidden |= HudElement.Notices;
			if ( !DrawKillFeed ) hidden |= HudElement.KillFeed;
			if ( !DrawInteractionHints ) hidden |= HudElement.InteractionHints;
			if ( !DrawVoices ) hidden |= HudElement.Voices;
			if ( !DrawNameplates ) hidden |= HudElement.Nameplates;
			if ( !DrawOwnerLabel ) hidden |= HudElement.OwnerLabel;
			return hidden;
		}
	}
}
