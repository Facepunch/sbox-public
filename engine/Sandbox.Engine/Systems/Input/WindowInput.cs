using NativeEngine;
using Sandbox.Utility;
using System.Runtime.InteropServices;

namespace Sandbox.Engine;

/// <summary>
/// Focus, mouse capture and text-input policy for the game and editor play surface.
/// </summary>
[SkipHotload]
internal static class WindowInput
{
	static bool initialized, active, focused, relative, imeAllowed, noIme, reclaimAttempted;
	static IntPtr editorWindow, editorHwnd, focusWindow;
	static IntPtr InputWindow => editorWindow != IntPtr.Zero ? editorWindow : GameWindow.Current?.Handle ?? IntPtr.Zero;

	internal static void Initialize()
	{
		if ( Application.IsHeadless ) return;
		initialized = true;
		active = focused = relative = reclaimAttempted = false;
		editorWindow = editorHwnd = focusWindow = IntPtr.Zero;
		noIme = CommandLine.HasSwitch( "-noime" );
		imeAllowed = !noIme;
		Sdl.SetHint( "SDL_TOUCH_MOUSE_EVENTS", "0" );
		// Each window owns its close policy, including panels which cancel or hide on close.
		Sdl.SetHint( "SDL_QUIT_ON_LAST_WINDOW_CLOSE", "0" );
		Sdl.SetHint( "SDL_IME_IMPLEMENTED_UI", "composition" );
		if ( !Application.IsHeadless )
		{
			SdlWindow.WarnIfFailed( Sdl.DisableScreenSaver() );
			SdlCursors.Initialize();
		}
		InputSystem.SetGameFocus( focused );
	}

	internal static bool HasMouseFocus() => focused;
	internal static bool IsAppActive() => active;
	internal static bool GetRelativeMouseMode() => relative;
	internal static IntPtr GetEditorMainWindow() => editorWindow;

	internal static void SetEditorMainWindow( IntPtr hwnd )
	{
		InputSystem.SetEditorMainWindow( hwnd );
		editorWindow = GameWindowNative.FromNativeHandle( hwnd );
		editorHwnd = hwnd;
	}

	internal static void OnEditorGameFocusChange( IntPtr hwnd, bool value )
	{
		if ( !initialized || Application.IsUnitTest ) return;
		focusWindow = value ? GameWindowNative.FromNativeHandle( hwnd ) : IntPtr.Zero;
		UpdateApplicationState( forceFocusUpdate: true );
	}

	/// <summary>
	/// Reads activation from the host and keeps game focus separate from application activation.
	/// </summary>
	internal static void UpdateApplicationState( bool forceFocusUpdate = false )
	{
		if ( !initialized || Application.IsUnitTest ) return;
		var tools = IToolsDll.Current;
		var window = Sdl.GetKeyboardFocus();
		var panelFocused = window != IntPtr.Zero && Sandbox.UI.PanelWindows.Find( window ) is not null;
		if ( tools is not null && window != editorWindow && !panelFocused ) ReclaimEditorKeyboardFocus();
		else reclaimAttempted = false;
		var hostActive = tools?.IsApplicationActive ?? (window != IntPtr.Zero);
		var isActive = hostActive || panelFocused;
		var gameWindow = GameWindow.Current?.Handle ?? IntPtr.Zero;
		var hasFocus = hostActive && !panelFocused && (tools is not null ? focusWindow != IntPtr.Zero : gameWindow != IntPtr.Zero && window == gameWindow);
		if ( !forceFocusUpdate && active == isActive && focused == hasFocus ) return;
		active = isActive;
		if ( !active ) Sdl.SetModState( 0 );
		SetFocus( hasFocus );
	}

	static void SetFocus( bool value )
	{
		focused = value;
		InputSystem.SetGameFocus( focused );
		InputRouter.OnWindowActive( focused );
		if ( focused ) SdlCursors.Restore();
		else
		{
			SetRelativeMouseMode( false );
			SdlCursors.ShowArrow();
		}
	}

	internal static void SetRelativeMouseMode( bool value )
	{
		if ( !initialized || Application.IsUnitTest ) return;

		// Focus can change while loading or running editor callbacks, after the last event poll.
		// Releasing capture must remain unconditional, including when SetFocus calls back here.
		if ( value ) UpdateApplicationState();

		var window = InputWindow;
		value &= active && focused;
		var actual = SdlWindow.SetRelativeMouseMode( window, value );
		if ( relative == actual ) return;
		relative = actual;
		IToolsDll.Current?.SetRelativeMouseOverride( relative );
	}

	internal static void SetIMEAllowed( bool value )
	{
		if ( !initialized || noIme || imeAllowed == value ) return;
		var window = InputWindow;
		if ( window == IntPtr.Zero ) return;
		if ( OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() )
		{
			if ( !value ) SdlWindow.WarnIfFailed( Sdl.ClearComposition( window ) );
		}
		else if ( !(value ? Sdl.StartTextInput( window ) : Sdl.StopTextInput( window )) )
		{
			Log.Warning( $"Couldn't change SDL text input: {Sdl.GetError()}" );
			return;
		}
		imeAllowed = value;
		if ( !value )
		{
			var area = new Sdl.Rect();
			Sdl.SetTextInputArea( window, ref area, 0 );
		}
	}

	/// <summary>
	/// Set the caret rectangle in SDL window coordinates relative to the game/play window, translating to the editor top-level window when needed.
	/// </summary>
	internal static void SetIMETextLocation( int x, int y, int width, int height )
	{
		if ( !initialized || !imeAllowed || InputWindow == IntPtr.Zero ) return;
		// The editor text-input window is the top level, while the caret is relative to the play widget.
		var surfaceWindow = focusWindow != IntPtr.Zero ? focusWindow : GameWindow.Current?.Handle ?? IntPtr.Zero;
		if ( surfaceWindow != IntPtr.Zero && surfaceWindow != InputWindow )
		{
			if ( !Sdl.GetWindowPosition( surfaceWindow, out var sx, out var sy ) ||
				!Sdl.GetWindowPosition( InputWindow, out var wx, out var wy ) ) return;
			x += sx - wx;
			y += sy - wy;
		}
		var area = new Sdl.Rect { X = x, Y = y, Width = width, Height = height };
		Sdl.SetTextInputArea( InputWindow, ref area, 0 );
	}

	internal static void SetCursorPosition( int x, int y, IntPtr window )
	{
		if ( !initialized ) return;

		UpdateApplicationState();
		if ( !active || !focused ) return;

		if ( window == IntPtr.Zero ) window = focusWindow;
		if ( window != IntPtr.Zero ) Sdl.WarpMouseInWindow( window, x, y );
	}

	internal static void OnWindowDestroyed( IntPtr window )
	{
		if ( !initialized || (window != editorWindow && window != focusWindow) ) return;
		SetFocus( false );
		if ( editorWindow == window ) editorWindow = editorHwnd = IntPtr.Zero;
		if ( focusWindow == window ) focusWindow = IntPtr.Zero;
	}

	internal static void Shutdown()
	{
		if ( !initialized ) return;
		SetRelativeMouseMode( false );
		initialized = active = focused = relative = reclaimAttempted = false;
		InputSystem.SetGameFocus( focused );
		editorWindow = editorHwnd = focusWindow = IntPtr.Zero;
		SdlCursors.Shutdown();
		if ( !Application.IsHeadless ) SdlWindow.WarnIfFailed( Sdl.EnableScreenSaver() );
	}

	/// <summary>
	/// Give SDL's keyboard focus back to the editor window once it's the foreground window again. Tried once
	/// per loss, since the message goes on to Qt as well.
	/// <para>
	/// The editor's window procedure (engine2's InputSystem) hands SDL the window's keyboard messages and
	/// nothing else - never its activation. So SDL only has the editor focused because it was focused when
	/// it was registered, and anything of ours SDL can see being activated - a panel window like the
	/// performance hub - takes that focus for good. Without a focused window SDL still sends key presses
	/// but drops typed text, and relative mouse mode never engages. Feeding SDL's own window procedure the
	/// focus message it missed is what puts it back.
	/// </para>
	/// </summary>
	static void ReclaimEditorKeyboardFocus()
	{
		if ( reclaimAttempted || !OperatingSystem.IsWindows() || editorHwnd == IntPtr.Zero ) return;
		if ( GetForegroundWindow() != editorHwnd ) return;

		reclaimAttempted = true;

		// SDL registers every window it creates under this class, with its window procedure
		var windowClass = new WndClassEx { Size = (uint)Marshal.SizeOf<WndClassEx>() };
		if ( !GetClassInfoExW( GetModuleHandleW( null ), "SDL_app", ref windowClass ) ) return;

		const uint WM_SETFOCUS = 0x0007;
		CallWindowProcW( windowClass.WndProc, editorHwnd, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero );
	}

	[StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
	struct WndClassEx
	{
		public uint Size;
		public uint Style;
		public IntPtr WndProc;
		public int ClassExtra;
		public int WindowExtra;
		public IntPtr Instance;
		public IntPtr Icon;
		public IntPtr Cursor;
		public IntPtr Background;
		public IntPtr MenuName;
		public IntPtr ClassName;
		public IntPtr IconSmall;
	}

	[DllImport( "user32.dll" )]
	static extern IntPtr GetForegroundWindow();

	[DllImport( "user32.dll", CharSet = CharSet.Unicode )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool GetClassInfoExW( IntPtr instance, string className, ref WndClassEx windowClass );

	[DllImport( "user32.dll" )]
	static extern IntPtr CallWindowProcW( IntPtr windowProc, IntPtr window, uint message, IntPtr wParam, IntPtr lParam );

	[DllImport( "kernel32.dll", CharSet = CharSet.Unicode )]
	static extern IntPtr GetModuleHandleW( string moduleName );
}
