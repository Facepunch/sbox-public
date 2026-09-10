using Sandbox.Engine;

namespace Sandbox.UI;

/// <summary>
/// Input focus in the game's UI. Focus belongs to a UI system, and a panel always uses its own -
/// see <see cref="Panel.Focus"/>. This is the shorthand for the game's, which is the only one most
/// code has.
/// </summary>
public class InputFocus
{
	static UISystem System => GlobalContext.Current.UISystem;

	/// <summary>
	/// The panel that currently has input focus.
	/// </summary>
	public static Panel Current
	{
		get => System.CurrentFocus;
		internal set => System.CurrentFocus = value;
	}

	/// <summary>
	/// The panel that will have the input focus next.
	/// </summary>
	public static Panel Next
	{
		get => System.NextFocus;
		internal set => System.NextFocus = value;
	}

	/// <summary>
	/// Set the focus to this panel (or its nearest ancestor with AcceptsFocus).
	/// Note that <see cref="Current"/> won't change until the next frame.
	/// </summary>
	public static bool Set( Panel panel ) => System.SetFocus( panel );

	/// <summary>
	/// Clear focus away from this panel.
	/// </summary>
	public static bool Clear( Panel panel ) => System.ClearFocus( panel );

	/// <summary>
	/// Clear keyboard focus.
	/// </summary>
	public static bool Clear() => System.ClearFocus();

	internal static void Tick() => System.TickFocus();
}
