using Sandbox;
using Sandbox.UI;

namespace SboxMenu;

/// <summary>
/// Holds the modals on the menu's world panel in VR. The overlay they normally go on is drawn flat on the
/// screen, where the headset can't see it. Uses the overlay's stylesheet so they look the same.
/// </summary>
[StyleSheet( "/Overlay/MenuOverlay.cs.scss" )]
public sealed class VRModalHost : Panel
{
	public VRModalHost()
	{
		Style.Position = PositionMode.Absolute;
		Style.Left = 0;
		Style.Top = 0;
		Style.Right = 0;
		Style.Bottom = 0;
		Style.ZIndex = 900;
		Style.PointerEvents = PointerEvents.None;

		// Modals reach far past their parent to cover the screen - keep them on the panel
		Style.Overflow = OverflowMode.Hidden;
	}
}
