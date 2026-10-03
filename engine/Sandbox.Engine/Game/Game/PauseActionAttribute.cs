namespace Sandbox;

/// <summary>
/// A button of your game's own on the pause menu, along its top bar beside Resume - a vote, a
/// scoreboard, a "back to the lobby". Put it on a static method that takes nothing; pressing the
/// button calls it.
/// </summary>
/// <example>
/// <code>
/// public static class MapVote
/// {
///     [PauseAction( "Vote map", Icon = "how_to_vote", DismissOnPress = true )]
///     public static void Open() => VoteScreen.Open();
/// }
/// </code>
/// </example>
[AttributeUsage( AttributeTargets.Method, AllowMultiple = false )]
public sealed class PauseActionAttribute : Attribute
{
	/// <summary>
	/// What the button says.
	/// </summary>
	public string Title { get; }

	/// <summary>
	/// A Material icon name to go with it, or null for none.
	/// </summary>
	public string Icon { get; set; }

	/// <summary>
	/// Shown when the button's hovered, or null for none.
	/// </summary>
	public string Tooltip { get; set; }

	/// <summary>
	/// Where it goes among the game's other actions - lower first.
	/// </summary>
	public int Order { get; set; }

	/// <summary>
	/// Close the pause menu once it's pressed, back to the game - for an action that opens something
	/// of the game's own, or only makes sense played.
	/// </summary>
	public bool DismissOnPress { get; set; }

	public PauseActionAttribute( string title )
	{
		Title = title;
	}
}
