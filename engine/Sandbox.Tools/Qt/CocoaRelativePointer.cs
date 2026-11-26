using System;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// Relative mouse motion on macOS, as SDL's Cocoa relative mode does it: the cursor is disconnected from the mouse, so it
/// stays where it was whatever the mouse does, and hidden. What the mouse moved by comes from
/// <c>CGGetLastMouseDelta</c>, read once a frame: Qt's mouse events carry the cursor's position, which doesn't
/// change. Main thread only.
/// </summary>
internal static class CocoaRelativePointer
{
	[StructLayout( LayoutKind.Sequential )]
	struct CGPoint
	{
		public double X, Y;
	}

	const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
	const string ObjC = "/usr/lib/libobjc.A.dylib";

	[DllImport( CoreGraphics )] static extern int CGAssociateMouseAndMouseCursorPosition( int connected );
	[DllImport( CoreGraphics )] static extern int CGWarpMouseCursorPosition( CGPoint position );
	[DllImport( CoreGraphics )] static extern void CGGetLastMouseDelta( out int deltaX, out int deltaY );

	[DllImport( ObjC )] static extern nint objc_getClass( string name );
	[DllImport( ObjC )] static extern nint sel_registerName( string name );
	[DllImport( ObjC, EntryPoint = "objc_msgSend" )] static extern void Send( nint target, nint selector );

	static bool active;

	/// <summary>
	/// Whether the cursor is held by <see cref="Begin"/>.
	/// </summary>
	internal static bool Active => active;

	/// <summary>
	/// Freeze and hide the cursor and start reporting the mouse's motion, until <see cref="End"/>. False if the window
	/// server refuses, with a warning.
	/// </summary>
	internal static bool Begin()
	{
		End();

		var error = CGAssociateMouseAndMouseCursorPosition( 0 );
		if ( error != 0 )
		{
			Log.Warning( $"Can't capture the mouse in the play widget: CGAssociateMouseAndMouseCursorPosition failed ({error})" );
			return false;
		}

		// Read once now, so motion from before this capture isn't reported as its first frame's
		CGGetLastMouseDelta( out _, out _ );

		// Balanced by End: hiding twice takes two shows
		Send( objc_getClass( "NSCursor" ), sel_registerName( "hide" ) );
		active = true;
		return true;
	}

	/// <summary>
	/// The mouse's motion since the last call, in points: down is positive, as SDL's relative mode reports it.
	/// </summary>
	internal static Vector2 TakeMotion()
	{
		if ( !active ) return default;

		CGGetLastMouseDelta( out var x, out var y );
		return new Vector2( x, y );
	}

	/// <summary>
	/// Reconnect the cursor to the mouse and show it.
	/// </summary>
	internal static void End()
	{
		if ( !active ) return;
		active = false;

		CGAssociateMouseAndMouseCursorPosition( 1 );
		Send( objc_getClass( "NSCursor" ), sel_registerName( "unhide" ) );
	}

	/// <summary>
	/// Move the cursor to a position in screen points, from the primary display's top left. Qt's own move posts a mouse
	/// event, which lands after the call returns.
	/// </summary>
	internal static void Warp( Vector2 position )
	{
		CGWarpMouseCursorPosition( new CGPoint { X = position.x, Y = position.y } );

		// A warp otherwise holds back the mouse's events for a moment, which SDL's warp cuts short the same way. While
		// captured, the cursor has to stay disconnected
		if ( !active ) CGAssociateMouseAndMouseCursorPosition( 1 );
	}
}
