using System.Runtime.InteropServices;
using SdlException = Borealis.Sdl.SdlException;

namespace Sandbox.Engine;

/// <summary>
/// Gives SDL the input of Qt's native windows in the editor: the main window and the play widget. Qt owns them; SDL
/// adopts each one and subclasses its window procedure, then this subclasses it again to decide who sees each message.
/// Windows only: elsewhere nothing is wrapped.
/// </summary>
[SkipHotload]
internal static unsafe class QtSdlBridge
{
	const int GWLP_WNDPROC = -4;
	const uint WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_SETCURSOR = 0x0020, WM_NCACTIVATE = 0x0086;
	const uint WM_INPUT = 0x00FF, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104;
	const uint WM_SYSKEYUP = 0x0105, WM_UNICHAR = 0x0109, WM_SYSCOMMAND = 0x0112;
	const uint WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_ENDCOMPOSITION = 0x010E, WM_IME_COMPOSITION = 0x010F;
	const uint WM_IME_SETCONTEXT = 0x0281, WM_IME_NOTIFY = 0x0282, WM_IME_CONTROL = 0x0283, WM_IME_COMPOSITIONFULL = 0x0284;
	const uint WM_IME_SELECT = 0x0285, WM_IME_CHAR = 0x0286, WM_IME_REQUEST = 0x0288, WM_IME_KEYDOWN = 0x0290, WM_IME_KEYUP = 0x0291;
	const nint SC_KEYMENU = 0xF100;

	/// <param name="Window">SDL's window over the HWND.</param>
	/// <param name="QtProc">Qt's window procedure, from before SDL adopted the window.</param>
	/// <param name="SdlProc">SDL's window procedure, which chains to <paramref name="QtProc"/>.</param>
	/// <param name="SdlChain">Address where SDL keeps <paramref name="QtProc"/> to chain to, or zero if its layout is unknown.</param>
	readonly record struct Entry( Borealis.Sdl.SdlWindow Window, nint QtProc, nint SdlProc, nint SdlChain );

	static readonly Dictionary<IntPtr, Entry> windows = new();
	static IntPtr editorMainWindow;

	/// <summary>
	/// Have SDL adopt a Qt native window (its HWND) and route its messages. Returns the <c>SDL_Window*</c>, or zero where
	/// nothing is wrapped. The editor's main window gets its own routing: Qt manages it, SDL only takes its input.
	/// </summary>
	internal static IntPtr Register( IntPtr hwnd, bool isEditorMainWindow = false )
	{
		if ( !OperatingSystem.IsWindows() || Application.IsUnitTest || hwnd == IntPtr.Zero ) return IntPtr.Zero;
		ThreadSafe.AssertIsMainThread();

		if ( isEditorMainWindow ) editorMainWindow = hwnd;
		if ( windows.TryGetValue( hwnd, out var existing ) ) return existing.Window.Handle;

		var qtProc = GetWindowLongPtrW( hwnd, GWLP_WNDPROC );
		Borealis.Sdl.SdlWindow window;
		try { window = Borealis.Sdl.SdlWindow.Adopt( hwnd ); }
		catch ( SdlException e )
		{
			Log.Warning( e.Message );
			return IntPtr.Zero;
		}

		windows[hwnd] = new Entry( window, qtProc, GetWindowLongPtrW( hwnd, GWLP_WNDPROC ), FindSdlChain( hwnd, qtProc ) );
		SetWindowLongPtrW( hwnd, GWLP_WNDPROC, (nint)(delegate* unmanaged< nint, uint, nint, nint, nint >)&WindowProc );
		return window.Handle;
	}

	/// <summary>
	/// Destroy SDL's side of a registered window. Safe after Qt has destroyed the HWND.
	/// </summary>
	internal static void Unregister( IntPtr hwnd )
	{
		if ( !windows.Remove( hwnd, out var entry ) ) return;
		if ( editorMainWindow == hwnd ) editorMainWindow = IntPtr.Zero;
		WindowInput.OnWindowDestroyed( entry.Window.Handle );
		// SDL puts back the procedure it chained to, replacing ours
		entry.Window.Dispose();
	}

	/// <summary>
	/// SDL chains to Qt through <c>SDL_WindowData.wndproc</c>: its ninth pointer on SDL 3.4, with <c>hwnd</c> the second.
	/// Clearing it runs SDL alone. Only trust the layout when both fields hold what we expect.
	/// </summary>
	static nint FindSdlChain( IntPtr hwnd, nint qtProc )
	{
		var data = (nint*)GetPropW( hwnd, "SDL_WindowData" );
		if ( data != null && data[1] == hwnd && data[8] == qtProc ) return (nint)(&data[8]);
		Log.Warning( "SDL_WindowData doesn't have SDL 3.4's layout: Qt will see the editor's messages twice" );
		return 0;
	}

	[UnmanagedCallersOnly]
	static nint WindowProc( nint hwnd, uint msg, nint wParam, nint lParam )
	{
		if ( !windows.TryGetValue( hwnd, out var entry ) ) return DefWindowProcW( hwnd, msg, wParam, lParam );

		// The editor owns window management, but SDL needs input and focus messages.
		// Relative mouse mode only becomes active on SDL's keyboard-focus window.
		if ( hwnd == editorMainWindow )
		{
			switch ( msg )
			{
				case WM_NCACTIVATE:
					{
						// SDL tracks foreground changes from WM_NCACTIVATE; it is the only message an already-active editor
						// window gets when it returns to the foreground. Qt owns the frame and the result.
						// lParam -1 stops SDL's DefWindowProc repainting it.
						var result = CallWindowProcW( entry.QtProc, hwnd, msg, wParam, lParam );
						CallSdl( hwnd, msg, wParam, -1, 0 );
						return result;
					}
				case WM_SETFOCUS or WM_KILLFOCUS:
					// Let Qt process focus first, then update SDL through the shared path below.
					// Otherwise opening a panel window clears SDL focus and capture never recovers.
					break;
				case WM_INPUT or WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP or WM_CHAR or WM_UNICHAR
					or WM_IME_CHAR or WM_IME_COMPOSITION or WM_IME_COMPOSITIONFULL or WM_IME_CONTROL or WM_IME_ENDCOMPOSITION
					or WM_IME_KEYDOWN or WM_IME_KEYUP or WM_IME_NOTIFY or WM_IME_REQUEST or WM_IME_SELECT or WM_IME_SETCONTEXT
					or WM_IME_STARTCOMPOSITION:
					// While the game has focus Qt must not see these, or it swallows our IME handling
					if ( WindowInput.HasMouseFocus() ) return CallSdl( hwnd, msg, wParam, lParam, 0 );
					return CallWindowProcW( entry.SdlProc, hwnd, msg, wParam, lParam );
				case WM_SYSCOMMAND when (wParam & 0xFFF0) == SC_KEYMENU:
					// Suppress system sounds and menu mode
					return 0;
				default:
					return CallWindowProcW( entry.QtProc, hwnd, msg, wParam, lParam );
			}
		}

		// SDL's own chain runs SDL then Qt, and the last to run wins. Run Qt first, so it still handles clicks and focus
		// (filtered at its level so they don't act as user input), then SDL without Qt, with full authority.
		var qtResult = CallWindowProcW( entry.QtProc, hwnd, msg, wParam, lParam );
		var sdlResult = CallSdl( hwnd, msg, wParam, lParam, qtResult );

		// This isn't setting the cursor again, only letting Qt enforce any override it has
		if ( msg == WM_SETCURSOR && windows.ContainsKey( hwnd ) )
			CallWindowProcW( entry.QtProc, hwnd, msg, wParam, lParam );

		return sdlResult;
	}

	/// <summary>
	/// SDL's window procedure without its chain to Qt. Returns <paramref name="fallback"/> if the window was unregistered
	/// while Qt handled the message, since SDL's data went with it.
	/// </summary>
	static nint CallSdl( nint hwnd, uint msg, nint wParam, nint lParam, nint fallback )
	{
		if ( !windows.TryGetValue( hwnd, out var entry ) ) return fallback;
		var chain = (nint*)entry.SdlChain;
		if ( chain == null ) return CallWindowProcW( entry.SdlProc, hwnd, msg, wParam, lParam );

		// Messages nest (SDL can send to its own window), so put back what was there rather than Qt's procedure
		var saved = *chain;
		*chain = 0;
		var result = CallWindowProcW( entry.SdlProc, hwnd, msg, wParam, lParam );
		if ( windows.ContainsKey( hwnd ) ) *chain = saved;
		return result;
	}

	[DllImport( "user32.dll" )] static extern nint GetWindowLongPtrW( nint hwnd, int index );
	[DllImport( "user32.dll" )] static extern nint SetWindowLongPtrW( nint hwnd, int index, nint value );
	[DllImport( "user32.dll" )] static extern nint CallWindowProcW( nint proc, nint hwnd, uint msg, nint wParam, nint lParam );
	[DllImport( "user32.dll" )] static extern nint DefWindowProcW( nint hwnd, uint msg, nint wParam, nint lParam );
	[DllImport( "user32.dll", CharSet = CharSet.Unicode )] static extern nint GetPropW( nint hwnd, string name );
}
