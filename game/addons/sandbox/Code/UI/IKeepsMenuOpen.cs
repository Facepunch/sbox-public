namespace Sandbox;

/// <summary>
/// Add this to a popup panel so it can keep spawn menu up for as long as the popup exists.
/// </summary>
public interface IKeepsMenuOpen
{
	/// <summary>
	/// Whether the menu is held up right now. Set it to false to let the menu close as usual, for example while the popup is only hovered.
	/// </summary>
	bool KeepsMenuOpen => true;
}
