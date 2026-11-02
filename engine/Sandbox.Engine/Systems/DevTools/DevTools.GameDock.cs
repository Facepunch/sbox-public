using NativeEngine;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The dev tools docked along the bottom of the game window, the way a browser docks its dev tools: a
/// <see cref="UISurface"/> of their own, laid out over the whole window and drawn last in the game window's overlay
/// pass, over the game and the menu.
/// <para>
/// <see cref="InputRouter"/> hands the dock the mouse while the cursor is over it, and the keyboard while one of its
/// panels has focus. While it's open the cursor stays visible, so it can be reached in a game that captures the mouse;
/// such a game gets no mouse until the dock closes or pops out.
/// </para>
/// </summary>
internal static partial class DevTools
{
	[ConVar( "devtools_dock_height", ConVarFlags.Protected | ConVarFlags.Saved, Min = 120, Help = "Height of the dev tools docked along the bottom of the game window" )]
	internal static float DockHeight { get; set; } = 360;

	static UISurface _dockSurface;
	static RealTimeSince _sinceDockFrame;
	static string _dockCursor;

	/// <summary>
	/// A mouse button that went down on the dock. The dock keeps the mouse until it's released, so a drag can leave it.
	/// </summary>
	static ButtonCode? _dockButton;

	/// <summary>
	/// Are the dev tools docked in the game window right now?
	/// </summary>
	internal static bool GameDockOpen => _dockSurface is not null;

	static void SimulateGameDock()
	{
		if ( _dockSurface is null )
		{
			_dockSurface = new UISurface();
			_dockSurface.OnCursorChanged = name => _dockCursor = name;
			BuildRoot( _dockSurface.Root, Host.GameDock );
			_sinceDockFrame = float.MaxValue;
		}

		// Laid out at the window's rate, like the popped out window; drawn every game frame from what was built last
		if ( _sinceDockFrame < 1f / WindowFrameRate ) return;
		_sinceDockFrame = 0;

		var surface = _dockSurface;
		surface.Size = Screen.Size;
		surface.DpiScale = Screen.DesktopScale;
		surface.Scale = WindowScale;
		surface.MouseInside = GameDockHasMouse( InputRouter.MouseCursorPosition );
		surface.MouseMoved( InputRouter.MouseCursorPosition );
		surface.Simulate();
	}

	static void CloseGameDock()
	{
		if ( _dockSurface is null ) return;

		_dockSurface.Dispose();
		_dockSurface = null;
		_dockCursor = null;
		_dockButton = null;
	}

	/// <summary>
	/// Draw the dock. Called from the game window's overlay pass, inside its render block.
	/// </summary>
	internal static void RenderGameDock()
	{
		if ( _dockSurface is null || !IsOpen ) return;
		_dockSurface.Render();
	}

	/// <summary>
	/// Does the dock take the mouse at this position, in window pixels? Over any of its panels, or while a button that
	/// went down on it is held.
	/// </summary>
	internal static bool GameDockHasMouse( Vector2 position )
	{
		if ( _dockSurface is not { } surface ) return false;
		if ( _dockButton is not null ) return true;

		var hit = ((IPanel)surface.Root).GetPanelAt( position, true, true );
		return hit is not null && hit != surface.Root;
	}

	/// <summary>
	/// The cursor the dock wants at this position, or null where it doesn't have the mouse.
	/// </summary>
	internal static string GameDockCursor( Vector2 position ) => GameDockHasMouse( position ) ? _dockCursor ?? "arrow" : null;

	/// <summary>
	/// The dock's panel with keyboard focus, if it has one. Keys go to it rather than the game.
	/// </summary>
	internal static Panel GameDockFocus => _dockSurface?.Focus;

	/// <summary>
	/// A mouse button went down or up in the game window. Returns true if the dock took it.
	/// </summary>
	internal static bool GameDockMouseButton( ButtonCode button, bool down, Vector2 position, KeyboardModifiers modifiers )
	{
		if ( _dockSurface is not { } surface ) return false;

		if ( !down )
		{
			if ( _dockButton != button ) return false;
			_dockButton = null;
			surface.SetMouseButton( button, false, modifiers );
			return true;
		}

		if ( !GameDockHasMouse( position ) )
		{
			// Clicking the game puts the keyboard back in it, and closes the dock's menus
			surface.System.ClearFocus();
			if ( surface.Root.Children.Any( x => x is BasePopup ) ) BasePopup.CloseAll();
			return false;
		}

		_dockButton = button;
		surface.MouseMoved( position );
		BasePopup.CloseAll( ((IPanel)surface.Root).GetPanelAt( position, true, true ) as Panel );
		surface.SetMouseButton( button, true, modifiers );
		return true;
	}

	/// <summary>
	/// The mouse wheel moved. Returns true if the dock took it.
	/// </summary>
	internal static bool GameDockMouseWheel( Vector2 delta, Vector2 position, KeyboardModifiers modifiers )
	{
		if ( !GameDockHasMouse( position ) ) return false;
		_dockSurface.SetMouseWheel( delta, modifiers );
		return true;
	}

	/// <summary>
	/// A key went down or up. Returns true if the dock took it, which it does while one of its panels has focus.
	/// </summary>
	internal static bool GameDockKey( ButtonCode key, bool down, KeyboardModifiers modifiers )
	{
		if ( GameDockFocus is null ) return false;

		_dockSurface.SetKey( key, down, modifiers );

		// Escape leaves the dock's text boxes, handing the keyboard back to the game
		if ( down && key == ButtonCode.KEY_ESCAPE ) _dockSurface.System.ClearFocus();
		return true;
	}

	/// <summary>
	/// Text was typed. Returns true if the dock took it.
	/// </summary>
	internal static bool GameDockText( string text )
	{
		if ( GameDockFocus is null ) return false;
		_dockSurface.TypeText( text );
		return true;
	}
}
