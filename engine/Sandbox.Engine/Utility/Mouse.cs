using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// Gives access to mouse position etc
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
	/// Access to local clients' cursor position, relative to game windows' top left corner.
	/// </summary>
	[ActionGraphNode( "input.mouse.pos" ), Title( "Mouse Position" ), Category( "Input" ), Icon( "mouse" )]
	public static Vector2 Position
	{
		get => InputRouter.MouseCursorPosition;

		set
		{
			if ( !g_pInputService.IsAppActive() ) return;

			value.x = MathX.Clamp( value.x.Floor(), 0, Screen.Width - 1 );
			value.y = MathX.Clamp( value.y.Floor(), 0, Screen.Height - 1 );

			Game.InputContext.SetMousePosition( new Vector2( (int)value.x, (int)value.y ) );
		}

	}

	/// <summary>
	/// Change in local clients' cursor position since last frame.
	/// </summary>
	[ActionGraphNode( "input.mouse.delta" ), Title( "Mouse Delta" ), Category( "Input" ), Icon( "mouse" )]
	public static Vector2 Delta => InputRouter.MouseCursorDelta;


	/// <summary>
	/// Sets the cursor type used when the UI hasn't claimed the cursor (e.g. when the mouse
	/// falls through the UI onto the world). UI panel hover cursors take precedence over this.
	/// </summary>
	public static string CursorType
	{
		set => Game.InputContext.MouseCursor = value;
		get => Game.InputContext.MouseCursor;
	}

	/// <summary>
	/// Whether the local clients' cursor is active or not, meaning it can interact with UI elements, etc.
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
	/// The visibility state of the mouse cursor. Auto will only show the mouse when clickable UI elements are visible.
	/// </summary>
	public static MouseVisibility Visibility { get; set; } = MouseVisibility.Auto;
}

/// <summary>
/// The visibility state of the mouse cursor.
/// </summary>
public enum MouseVisibility
{
	/// <summary>
	/// The mouse is visible and can interact with UI elements.
	/// </summary>
	Visible,

	/// <summary>
	/// The mouse is only visible when UI elements with `pointer-events: auto` are on-screen.
	/// </summary>
	Auto,

	/// <summary>
	/// The mouse is locked to the game and cannot interact with UI elements.
	/// </summary>
	Hidden
}
