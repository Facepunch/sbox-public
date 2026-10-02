using System;
using System.Runtime.InteropServices;
using NativeEngine;
using Sandbox.Engine;

namespace Editor;

/// <summary>
/// Gives Qt the same custom cursor as SDL so clicks cannot restore the editor's arrow.
/// </summary>
internal static class PlayWidgetCursor
{
	static SceneRenderingWidget widget;
	static IntPtr selected;
	static Pixmap pixmap, previousPixmap;
	static CursorShape previousCursor;

	internal static void Set( IntPtr cursor, ReadOnlySpan<byte> pixels, int width, int height, int hotX, int hotY )
	{
		if ( !OperatingSystem.IsWindows() ) return;
		var target = GameMode.PlayWidget;
		if ( pixels.IsEmpty || !target.IsValid() || !WindowInput.HasMouseFocus() )
		{
			Clear();
			return;
		}
		if ( widget == target && selected == cursor ) return;
		Clear();

		// The existing Qt binding uses the image centre as its hotspot. Pad transparently
		// so that centre lands on the game's hotspot without moving the visible image.
		var paddedWidth = 2 * Math.Max( hotX, width - 1 - hotX ) + 1;
		var paddedHeight = 2 * Math.Max( hotY, height - 1 - hotY ) + 1;
		var padded = new byte[checked( paddedWidth * paddedHeight * 4 )];
		var offsetX = paddedWidth / 2 - hotX;
		var offsetY = paddedHeight / 2 - hotY;
		for ( var y = 0; y < height; y++ )
			pixels.Slice( y * width * 4, width * 4 ).CopyTo( padded.AsSpan( ((y + offsetY) * paddedWidth + offsetX) * 4 ) );

		var image = new Pixmap( paddedWidth, paddedHeight );
		if ( !image.UpdateFromPixels( padded, paddedWidth, paddedHeight, ImageFormat.RGBA8888 ) ) return;
		widget = target;
		selected = cursor;
		previousCursor = target.Cursor;
		previousPixmap = target.PixmapCursor;
		pixmap = image;
		target.PixmapCursor = image;
	}

	internal static void Clear()
	{
		if ( widget.IsValid() && widget.PixmapCursor == pixmap )
		{
			if ( previousPixmap is not null ) widget.PixmapCursor = previousPixmap;
			else widget.Cursor = previousCursor;
		}
		widget = null;
		selected = IntPtr.Zero;
		pixmap = previousPixmap = null;
	}
}

/// <summary>
/// Keeps the game cursor while Windows and Qt process mouse clicks.
/// </summary>
[SkipHotload]
internal static class PlayWidgetCursorHook
{
	const uint CaptureChanged = 0x0215;
	const uint SetCursor = 0x0020;
	const uint Destroy = 0x0082;
	static readonly SubclassProc callback = OnMessage;
	static IntPtr window;

	internal static void Install( IntPtr handle )
	{
		if ( !OperatingSystem.IsWindows() ) return;
		Remove();
		if ( SetWindowSubclass( handle, callback, 1, 0 ) ) window = handle;
		else Log.Warning( "Couldn't install the play widget cursor hook." );
	}

	internal static void Remove()
	{
		if ( window == IntPtr.Zero ) return;
		if ( !RemoveWindowSubclass( window, callback, 1 ) )
			Log.Warning( "Couldn't remove the play widget cursor hook." );
		window = IntPtr.Zero;
	}

	static IntPtr OnMessage( IntPtr hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data )
	{
		// Prevent the default client-area cursor from appearing before a button event.
		if ( hwnd == window && message == SetCursor && (IntPtr)wParam == hwnd &&
			((long)lParam & 0xffff) == 1 && RestoreCursor() )
			return (IntPtr)1;

		if ( message == Destroy )
		{
			RemoveWindowSubclass( hwnd, callback, id );
			if ( window == hwnd ) window = IntPtr.Zero;
		}

		var result = DefSubclassProc( hwnd, message, wParam, lParam );
		// Button and capture handlers can replace the cursor directly, without WM_SETCURSOR.
		var buttonEvent = message is 0x0201 or 0x0202 or 0x0204 or 0x0205 or 0x0207 or 0x0208 or 0x020b or 0x020c;
		if ( hwnd == window && (buttonEvent || (message == CaptureChanged && lParam == 0)) ) RestoreCursor();
		return result;
	}

	static bool RestoreCursor()
	{
		if ( !WindowInput.HasMouseFocus() || Sdl.GetMouseFocus() != GameMode.PlayWindow ) return false;
		try
		{
			SdlCursors.Restore();
			return true;
		}
		catch ( Exception exception )
		{
			Log.Warning( exception, "Couldn't restore the play widget cursor." );
			return false;
		}
	}

	[UnmanagedFunctionPointer( CallingConvention.Winapi )]
	delegate IntPtr SubclassProc( IntPtr hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data );

	[DllImport( "comctl32.dll" )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool SetWindowSubclass( IntPtr hwnd, SubclassProc callback, nuint id, nuint data );

	[DllImport( "comctl32.dll" )]
	[return: MarshalAs( UnmanagedType.Bool )]
	static extern bool RemoveWindowSubclass( IntPtr hwnd, SubclassProc callback, nuint id );

	[DllImport( "comctl32.dll" )]
	static extern IntPtr DefSubclassProc( IntPtr hwnd, uint message, nuint wParam, nint lParam );
}
