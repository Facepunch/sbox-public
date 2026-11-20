/// <summary>
/// The local user's preferences in Deathmatch
/// </summary>
internal static partial class GamePreferences
{
	/// <summary>
	/// Enables automatic switching to better weapons on item pickup
	/// </summary>
	[ConVar( "sandbox.autoswitch", ConVarFlags.UserInfo | ConVarFlags.Saved )]
	public static bool AutoSwitch { get; set; } = true;

	/// <summary>
	/// The weapon prefab the NPCs we spawn are armed with, empty keeps their own. See <see cref="Player.NpcWeapon"/>.
	/// </summary>
	[ConVar( "sandbox.npc.weapon", ConVarFlags.Saved )]
	public static string NpcWeapon { get; set; } = "";

	/// <summary>
	/// Enables movement bob, weapon inertia and animation-driven camera motion.
	/// </summary>
	[ConVar( "sandbox.viewbob", ConVarFlags.Saved )]
	[Group( "Camera" )]
	public static bool ViewBobbing { get; set; } = true;

	/// <summary>
	/// Strength of the camera tilt when moving sideways
	/// </summary>
	[ConVar( "sandbox.viewtilt", ConVarFlags.Saved, Help = "Sideways camera tilt strength. 0 disables tilt, 1 restores the original strength." )]
	[Range( 0f, 1f ), Step( 0.05f ), Group( "Camera" )]
	public static float ViewTilt { get; set; } = 0.35f;

	/// <summary>
	/// Intensity of your camera's screenshake
	/// </summary>
	[ConVar( "sandbox.screenshake", ConVarFlags.Saved )]
	[Range( 0.1f, 2f ), Step( 0.1f ), Group( "Camera" )]
	public static float Screenshake { get; set; } = 0.3f;

	public static void Reset( string group )
	{
		foreach ( var property in Game.TypeLibrary.GetType( typeof( GamePreferences ) )?.Properties ?? [] )
		{
			if ( property.Group != group || property.GetCustomAttribute<ConVarAttribute>() is not { } convar ) continue;

			if ( property.GetCustomAttribute<DefaultValueAttribute>() is { } defaultValue )
				ConsoleSystem.SetValue( convar.Name, defaultValue.Value );
		}
	}

	/// <summary>
	/// A colour preference as text. Plain invariant numbers rather than hex, so HDR brightness above 1 survives.
	/// </summary>
	public static string ColorText( Color color ) => System.FormattableString.Invariant( $"{color.r},{color.g},{color.b},{color.a}" );

	/// <summary>
	/// A colour preference, null when it's empty for the default or can't be read. Hex from older saves reads too.
	/// </summary>
	public static Color? ParseColor( string text ) => string.IsNullOrWhiteSpace( text ) ? null : Color.Parse( text );
}
