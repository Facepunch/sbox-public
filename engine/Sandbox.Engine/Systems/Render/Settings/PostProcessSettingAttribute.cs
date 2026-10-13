namespace Sandbox;

/// <summary>
/// Puts a quality <see cref="ConVarAttribute">ConVar</see> on the post-processing ladder, next to
/// ambient occlusion and depth of field. While the game is loaded it gets a row in the graphics
/// settings, and the post-processing and graphics presets drive it.
/// <para>
/// The ConVar must be an <c>int</c>. It's written 0 for Off and 1, 2, 3 for Low, Medium and High,
/// the same scale as <c>r_ao_quality</c>. The row takes its name and description from
/// <see cref="TitleAttribute"/> and <see cref="DescriptionAttribute"/> on the property.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [ConVar( "r_godrays_quality", Min = 0, Max = 3 ), PostProcessSetting]
/// [Title( "God Rays" ), Description( "Light shafts through fog and foliage." )]
/// public static int GodRaysQuality { get; set; } = 3;
/// </code>
/// </example>
[AttributeUsage( AttributeTargets.Property )]
public sealed class PostProcessSettingAttribute : Attribute
{
}
