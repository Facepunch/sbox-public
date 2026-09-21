using NativeEngine;
using Sandbox.Internal;
using Sandbox.UI;

namespace Sandbox.Engine;

/// <summary>
/// This is where input is sent to from the engine. This is the first place input is routed to.
/// From here it tries to route it to the menu, game menu and client - in that order. That should
/// really be abstracted out though, so we can use this properly in Standalone.
/// </summary>
internal static partial class InputRouter
{
	/// <summary>
	/// True if the cursor is visible
	/// </summary>
	public static bool MouseCursorVisible { get; private set; }

	/// <summary>
	/// The mouse cursor position. Or the last position if it's now invisible.
	/// </summary>
	public static Vector2 MouseCursorPosition { get; private set; }

	/// <summary>
	/// The mouse cursor delta
	/// </summary>
	public static Vector2 MouseCursorDelta { get; private set; }

	/// <summary>
	/// The panel we're keyboard focusing on
	/// </summary>
	public static IPanel KeyboardFocusPanel { get; set; }

	/// <summary>
	/// The position in which we entered capture/relative mode
	/// </summary>
	static Vector2? mouseCapturePosition;

	/// <summary>
	/// True if an "exit game" button is pressed, escape on keyboard
	/// </summary>
	public static bool EscapeIsDown { get; private set; }

	/// <summary>
	/// The escape button was pressed this frame. 
	/// The game is allowed to consume this. Then it will go to the menu.
	/// This is distinct from EscapeIsDown, because that is used to close the game when held down.
	/// </summary>
	public static bool EscapeWasPressed { get; set; }

	/// <summary>
	/// Shift+Escape was pressed in editor play mode. Kept separate so game input cannot consume it.
	/// </summary>
	internal static bool EditorPauseMenuWasPressed { get; set; }

	/// <summary>
	/// Time since escape was pressed
	/// </summary>
	static RealTimeSince TimeSinceEscapePressed { get; set; }

	/// <summary>
	/// Buttons that are currently pressed
	/// </summary>
	static HashSet<ButtonCode> PressedButtons = new HashSet<ButtonCode>();

	/// <summary>
	/// Controller buttons that are currently pressed
	/// </summary>
	static HashSet<GamepadCode> PressedControllerButtons = new HashSet<GamepadCode>();

	/// <summary>
	/// Returns the number of seconds escape has been held down
	/// </summary>
	public static float EscapeTime => EscapeIsDown ? TimeSinceEscapePressed.Relative : 0;

	/// <summary>
	/// Return the input contexts of each context, in order of priority
	/// </summary>
	static IEnumerable<InputContext> Contexts
	{
		get
		{
			if ( IMenuDll.Current is not null )
			{
				var menu = IMenuDll.Current.InputContext;
				if ( menu is not null ) yield return menu;
			}

			// if we even have a game menu!
			if ( IGameInstance.Current is not null )
			{
				var gamemenu = IGameInstanceDll.Current.InputContext;
				if ( gamemenu is not null ) yield return gamemenu;
			}
		}
	}

	public static void Frame()
	{
		var activeMouse = Contexts.FirstOrDefault( x => x.MouseState != InputContext.InputState.Ignore );
		var activeKeyboard = Contexts.FirstOrDefault( x => x.KeyboardState != InputContext.InputState.Ignore );

		// Capture mode could either come from being in game (in which case input is sent to the game)
		// or from a Panel.CaptureMode - in which case input is sent to the panel/ui
		bool mouseCaptureMode = activeMouse is not null && activeMouse.MouseState == InputContext.InputState.Game;
		mouseCaptureMode = mouseCaptureMode || (activeMouse?.MouseCapture ?? false);

		MouseCursorVisible = !mouseCaptureMode && (activeMouse is not null && activeMouse.MouseState == InputContext.InputState.UI);
		if ( !WindowInput.HasMouseFocus() ) MouseCursorVisible = true;

		if ( mouseCaptureMode )
		{
			// save the cursor position
			if ( mouseCapturePosition is null )
			{
				mouseCapturePosition = MouseCursorPosition;
			}

			WindowInput.SetRelativeMouseMode( true );
		}
		else
		{
			WindowInput.SetRelativeMouseMode( false );

			// restore cursor position
			if ( mouseCapturePosition is not null )
			{
				SetCursorPosition( mouseCapturePosition.Value );
				mouseCapturePosition = null;
			}
		}

		if ( activeMouse is not null && WindowInput.HasMouseFocus() && PanelWindows.Hovering is null && PanelWindows.CaptureWindow is null )
		{
			SdlCursors.SetCursor( MouseCursorVisible ? activeMouse.MouseCursor : "none" );
		}

		KeyboardFocusPanel = activeKeyboard?.KeyboardFocusPanel;
		WindowInput.SetIMEAllowed( KeyboardFocusPanel is not null );
		if ( KeyboardFocusPanel is not null )
		{
			var rect = KeyboardFocusPanel is Panel panel ? panel.ImeCaretRect : KeyboardFocusPanel.Rect;
			WindowInput.SetIMETextLocation( (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height );
		}

		MouseCursorDelta = 0;
		EscapeWasPressed = false;
		EditorPauseMenuWasPressed = false;

		// Only the UI that has the mouse gets to show a tooltip - the one underneath it loses its hover
		foreach ( var context in Contexts )
		{
			context.TargetUISystem?.Tooltips.SetHovered( context == activeMouse ? activeMouse.MouseFocusPanel as Panel : null, MouseCursorPosition );
		}
	}

	static void SetCursorPosition( Vector2 pos )
	{
		if ( !WindowInput.IsAppActive() ) return;
		if ( !WindowInput.HasMouseFocus() ) return;

		WindowInput.SetCursorPosition( (int)pos.x, (int)pos.y, GameWindow.Current?.Handle ?? IntPtr.Zero );
	}

	internal static void Shutdown()
	{
		KeyboardFocusPanel = null;
	}

	internal static void ShutdownUserCursors()
	{
		if ( Application.IsHeadless )
			return;

		SdlCursors.ShutdownUserCursors();
	}

	internal static void CreateUserCursor( BaseFileSystem filesystem, string name, string filepath, int hotX, int hotY )
	{
		Assert.False( Application.IsHeadless );

		if ( string.IsNullOrWhiteSpace( name ) )
			return;

		if ( string.IsNullOrWhiteSpace( filepath ) )
			return;

		if ( SdlCursors.HasUserCursor( name ) )
			return;

		if ( !filesystem.FileExists( filepath ) )
			return;

		SdlCursors.LoadCursorFromFile( filepath, name, hotX, hotY );
	}

	/// <summary>
	/// An input context wants to set the cursor position
	/// </summary>
	internal static void SetCursorPosition( InputContext inputContext, Vector2 vector2 )
	{
		var activeMouse = Contexts.Where( x => x.MouseState != InputContext.InputState.Ignore )
							.FirstOrDefault();

		if ( activeMouse != inputContext )
			return;

		// if this is set, we're in capture mode - so just update the position
		// which will update the position of the cursor when we come out of it
		if ( mouseCapturePosition is not null )
		{
			mouseCapturePosition = vector2;
			return;
		}

		SetCursorPosition( vector2 );
	}

	/// <summary>
	/// Return true if button is pressed
	/// </summary>
	public static bool IsButtonDown( ButtonCode code )
	{
		return PressedButtons.Contains( code );
	}

	/// <summary>
	/// Return true if button is pressed
	/// </summary>
	private static void SetButtonState( ButtonCode code, bool state )
	{
		if ( state ) PressedButtons.Add( code );
		else PressedButtons.Remove( code );
	}

	/// <summary>
	/// Return true if button is pressed
	/// </summary>
	public static bool IsButtonDown( GamepadCode code )
	{
		return PressedControllerButtons.Contains( code );
	}

	/// <summary>
	/// Return true if button is pressed
	/// </summary>
	private static void SetButtonState( GamepadCode code, bool state )
	{
		if ( state ) PressedControllerButtons.Add( code );
		else PressedControllerButtons.Remove( code );
	}

	internal static void CloseApplication()
	{
		Application.Exit();
	}
}
