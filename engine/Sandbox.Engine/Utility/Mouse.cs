using Sandbox.Engine;
using System;

namespace Sandbox;

/// <summary>
/// Gives access to mouse position etc - Modified for Infinite Drag
/// </summary>
public static class Mouse
{
	internal static Vector2 _velocityHistory;
	private static bool _isWarping;

	/// <summary>
	/// Toggle for Blender-style wrapping.
	/// </summary>
	public static bool InfiniteDrag { get; set; } = true;

	/// <summary>
	/// Called once per frame
	/// </summary>
	static internal void Frame()
	{
		// Reset warping flag each frame
		_isWarping = false;

		_velocityHistory = _velocityHistory * 2.0f + Delta;
		_velocityHistory /= 3.0f;

		// Trigger wrap only when left clicking (dragging)
		// Input.Down is the global Sandbox input checker
		if ( InfiniteDrag && Input.Down( "mouse1" ) )
		{
			ApplyInfiniteWrap();
		}
	}

	private static void ApplyInfiniteWrap()
	{
		var pos = Position;
		var w = Screen.Width;
		var h = Screen.Height;
		var margin = 10;
		bool needsWarp = false;

		Vector2 newPos = pos;

		// Horizontal Wrap
		if ( pos.x <= 0 ) { newPos.x = w - margin; needsWarp = true; }
		else if ( pos.x >= w - 1 ) { newPos.x = margin; needsWarp = true; }

		// Vertical Wrap
		if ( pos.y <= 0 ) { newPos.y = h - margin; needsWarp = true; }
		else if ( pos.y >= h - 1 ) { newPos.y = margin; needsWarp = true; }

		if ( needsWarp )
		{
			_isWarping = true;

			// This is the direct engine call to teleport the mouse
			// We cast to int just to be perfectly safe with the Vector2 types
			Game.InputContext.SetMousePosition( new Vector2( (int)newPos.x, (int)newPos.y ) );
		}
	}

	public static Vector2 Velocity => _velocityHistory;

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

			// Bypass clamping if we are in the middle of a wrap so it doesn't get stuck
			if ( !_isWarping && !InfiniteDrag )
			{
				value.x = MathX.Clamp( value.x.Floor(), 0, Screen.Width - 1 );
				value.y = MathX.Clamp( value.y.Floor(), 0, Screen.Height - 1 );
			}

			Game.InputContext.SetMousePosition( new Vector2( (int)value.x, (int)value.y ) );
		}
	}

	/// <summary>
	/// Change in local clients' cursor position since last frame.
	/// </summary>
	[ActionGraphNode( "input.mouse.delta" ), Title( "Mouse Delta" ), Category( "Input" ), Icon( "mouse" )]
	public static Vector2 Delta
	{
		get
		{
			// If we just teleported, return zero so the camera/sliders don't flick
			if ( _isWarping ) return Vector2.Zero;
			return InputRouter.MouseCursorDelta;
		}
	}

	public static string CursorType
	{
		set => Game.InputContext.MouseCursor = value;
		get => Game.InputContext.MouseCursor;
	}

	public static bool Active => Visibility == MouseVisibility.Visible || (Visibility == MouseVisibility.Auto && Game.InputContext.MouseState == Engine.InputContext.InputState.UI);

	[Obsolete]
	public static bool Visible
	{
		get => Active;
		set => Visibility = value ? MouseVisibility.Visible : MouseVisibility.Auto;
	}

	public static MouseVisibility Visibility { get; set; } = MouseVisibility.Auto;
}

public enum MouseVisibility
{
	Visible,
	Auto,
	Hidden
}
