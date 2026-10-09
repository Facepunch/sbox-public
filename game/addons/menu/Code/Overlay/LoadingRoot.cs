using MenuProject.Overlay.Overlays;
using Sandbox;

/// <summary>
/// The loading screen's own root, rather than a child of <see cref="MenuOverlay"/>. The overlay sizes
/// things by desktop scale; a plain root panel sizes them by screen height (1080 tall is 1:1, the same
/// as a ScreenPanel's ConsistentHeight) like the main menu does - and coming out of the menu, that's
/// how the loading screen should look. Made straight after the overlay so it draws over it.
/// </summary>
public sealed class LoadingRoot : RootPanel
{
	public static LoadingRoot Instance;

	public LoadingRoot()
	{
		// Only the loading screen itself takes the mouse, and only while it's up
		Style.PointerEvents = PointerEvents.None;
		Style.ZIndex = 1000;

		AddChild<LoadingOverlay>();
	}
}
