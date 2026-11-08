using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// Access to mouse position, movement, input mode and cursor appearance.
/// </summary>
public static class Mouse
{
	internal static Vector2 _velocityHistory;

	/// <summary>
	/// Called once per frame
	/// </summary>
	static internal void Frame()
	{
		_velocityHistory = _velocityHistory * 2.0f + Delta;
		_velocityHistory /= 3.0f;
	}

	public static Vector2 Velocity
	{
		get => _velocityHistory;
	}


	/// <summary>
	/// The local client's cursor position, in pixels relative to the game window's top-left corner.
	/// In captured mode, accumulates relative movement instead of tracking the OS cursor.
	/// </summary>
	[ActionGraphNode( "input.mouse.pos" ), Title( "Mouse Position" ), Category( "Input" ), Icon( "mouse" )]
	public static Vector2 Position
	{
		get => InputRouter.MouseCursorPosition;

		set
		{
			if ( !WindowInput.IsAppActive() ) return;

			value.x = MathX.Clamp( value.x.Floor(), 0, Screen.Width - 1 );
			value.y = MathX.Clamp( value.y.Floor(), 0, Screen.Height - 1 );

			Game.InputContext.SetMousePosition( new Vector2( (int)value.x, (int)value.y ) );
		}

	}

	/// <summary>
	/// Mouse movement since the last frame. Captured input normally bypasses OS pointer speed and acceleration.
	/// Unaffected by <see cref="Preferences.Sensitivity"/>, which scales <see cref="Input.AnalogLook"/>.
	/// </summary>
	[ActionGraphNode( "input.mouse.delta" ), Title( "Mouse Delta" ), Category( "Input" ), Icon( "mouse" )]
	public static Vector2 Delta => UI.PanelWindows.CaptureWindow is not null ? UI.PanelWindows.CaptureDelta : InputRouter.MouseCursorDelta;


	/// <summary>
	/// Sets the cursor image. UI hover cursors can overwrite or reset this value.
	/// Use <c>"none"</c> to hide the image without changing input mode or pointer speed.
	/// </summary>
	public static string CursorType
	{
		set => Game.InputContext.MouseCursor = value;
		get => Game.InputContext.MouseCursor;
	}

	/// <summary>
	/// Whether the cursor can interact with UI. Can be true even when <see cref="CursorType"/> is <c>"none"</c>.
	/// </summary>
	public static bool Active => Visibility == MouseVisibility.Visible || (Visibility == MouseVisibility.Auto && Game.InputContext.MouseState == Engine.InputContext.InputState.UI);

	/// <summary>
	/// DEPRECATED. Use Mouse.Visibility instead.
	/// </summary>
	[Obsolete]
	public static bool Visible
	{
		get => Active;
		set => Visibility = value ? MouseVisibility.Visible : MouseVisibility.Auto;
	}

	/// <summary>
	/// Controls cursor interaction and capture. <see cref="MouseVisibility.Hidden"/> uses relative input,
	/// which normally bypasses OS pointer speed and acceleration.
	/// For a game-drawn cursor with OS pointer speed, use <see cref="MouseVisibility.Visible"/>
	/// with <c>Mouse.CursorType = "none"</c> and draw at <see cref="Position"/>.
	/// </summary>
	public static MouseVisibility Visibility { get; set; } = MouseVisibility.Auto;
}

/// <summary>
/// Mouse input mode. To hide only the cursor image, use <see cref="Mouse.CursorType"/> with <c>"none"</c>.
/// </summary>
public enum MouseVisibility
{
	/// <summary>
	/// Uses normal OS cursor movement and allows UI interaction, even with <c>Mouse.CursorType = "none"</c>.
	/// </summary>
	Visible,

	/// <summary>
	/// Uses cursor mode when visible screen UI wants pointer input; otherwise uses captured relative input.
	/// </summary>
	Auto,

	/// <summary>
	/// Captures relative movement for camera look, hides the cursor and disables normal screen UI pointer interaction.
	/// For a game-drawn cursor, use <see cref="Visible"/> with <c>Mouse.CursorType = "none"</c> to preserve OS pointer speed.
	/// </summary>
	Hidden
}
