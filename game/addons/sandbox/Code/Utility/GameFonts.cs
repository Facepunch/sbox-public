/// <summary>
/// Fonts that can be picked by a player. Font must be added either from engine or project "fonts" folder, then put its name here.
/// </summary>
public static class GameFonts
{
	public const string Default = "Inter";

	public static IReadOnlyList<string> Families { get; } =
	[
		"Inter",
		"Poppins",
		"Roboto",
		"Roboto Condensed",
		"Roboto Mono",
		"Inconsolata",
	];
}
