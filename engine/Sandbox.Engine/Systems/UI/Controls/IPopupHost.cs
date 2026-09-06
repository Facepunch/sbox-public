namespace Sandbox.UI;

/// <summary>
/// Where a UI's popups open. A game has none and popups float inside the panel root. A window
/// hosting a surface implements this to open each popup in an OS window of its own, so menus and
/// dropdowns can hang outside the window like the OS's do.
/// </summary>
internal interface IPopupHost
{
	/// <summary>
	/// A popup wants to appear next to <paramref name="source"/>. Put it somewhere it can be
	/// seen. The host parents it; the popup has not been positioned.
	/// </summary>
	void ShowPopup( Popup popup, Panel source, Popup.PositionMode position, float offset );

	/// <summary>
	/// The popup is being deleted. Take down whatever was showing it.
	/// </summary>
	void HidePopup( Popup popup );
}
