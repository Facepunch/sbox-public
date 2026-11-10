using Sandbox.UI;

namespace UITests.Controls;

/// <summary>
/// What the menu tests share on top of <see cref="UiTesting"/>.
/// </summary>
static class MenuTesting
{
	/// <summary>
	/// Time doesn't pass in a test, so every delay and guard is off unless a test turns one on.
	/// </summary>
	public static void ResetTiming()
	{
		Menu.SubmenuOpenDelay = 0;
		Menu.SubmenuCloseDelay = 0;
		Menu.ReopenGuard = 0;
	}

	/// <summary>
	/// Presses a key on an open menu's list.
	/// </summary>
	public static void Key( Menu menu, string button )
	{
		menu.ListPanel.OnButtonTyped( new ButtonEvent( button, true, 0, default ) );
	}
}
