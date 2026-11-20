/// <summary>
/// What the local player sees of physguns, edited from the Physgun tab of the inspect menu
/// </summary>
internal static partial class GamePreferences
{
	/// <summary>
	/// Display physgun beam cast from you and other players
	/// </summary>
	[ConVar( "sandbox.physgun_beams", ConVarFlags.Saved )]
	[Group( "Physgun Drawing" )]
	public static bool PhysgunBeams { get; set; } = true;

	/// <summary>
	/// Draw outlines on props when they are being held by physgun 
	/// </summary>
	[ConVar( "sandbox.physgun_outline", ConVarFlags.Saved )]
	[Group( "Physgun Drawing" )]
	public static bool PhysgunOutline { get; set; } = true;

	/// <summary>
	/// Show effect when prop is freezed
	/// </summary>
	[ConVar( "sandbox.physgun_freeze_effect", ConVarFlags.Saved )]
	[Group( "Physgun Drawing" )]
	public static bool PhysgunFreezeEffect { get; set; } = true;

	/// <summary>
	/// Show effect when prop is unfreezed
	/// </summary>
	[ConVar( "sandbox.physgun_unfreeze_effect", ConVarFlags.Saved )]
	[Group( "Physgun Drawing" )]
	public static bool PhysgunUnfreezeEffect { get; set; } = true;

	[ConVar( "sandbox.physgun_outline_width", ConVarFlags.Saved )]
	[Range( 0.05f, 1f ), Step( 0.05f ), Group( "Physgun Outline" )]
	public static float OutlineWidth { get; set; } = 0.2f;

	/// <summary>
	/// Opacity of the outline when it's showing a prop behind some geometry
	/// </summary>
	[ConVar( "sandbox.physgun_outline_obscured", ConVarFlags.Saved )]
	[Range( 0f, 1f ), Step( 0.05f ), Group( "Physgun Outline" )]
	public static float OutlineObscuredOpacity { get; set; } = 0.1f;

	/// <summary>
	/// Colour of your physgun's beam, its effects, its glow and the selection outline, as text. Empty for <see cref="Physgun.DefaultBeamColor"/>.
	/// Your player syncs it so everyone else sees your physgun in it, see <see cref="Player.PhysgunColor"/>.
	/// </summary>
	[ConVar( "sandbox.physgun_color", ConVarFlags.Saved )]
	[Group( "Physgun Colors" )]
	public static string PhysgunColor { get; set; } = "";

	/// <summary>
	/// Colour your physgun glows while it gravity pulls, as text. Empty for <see cref="Physgun.DefaultPullColor"/>.
	/// </summary>
	[ConVar( "sandbox.physgun_pull_color", ConVarFlags.Saved )]
	[Group( "Physgun Colors" )]
	public static string PhysgunPullColor { get; set; } = "";

	public static Color PhysgunColorValue => ParseColor( PhysgunColor ) ?? Physgun.DefaultBeamColor;
	public static Color PhysgunPullColorValue => ParseColor( PhysgunPullColor ) ?? Physgun.DefaultPullColor;
}
