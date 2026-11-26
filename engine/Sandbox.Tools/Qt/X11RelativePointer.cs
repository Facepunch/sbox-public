using System;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// Relative mouse motion for a native X11 window, as SDL's X11 relative mode does it: the motion comes from XInput2 raw
/// events, which report what the mouse moved by however the pointer is held, and the pointer is kept on the window by
/// XFixes barriers on its edges and moved back to its middle once a frame. Pointer positions can't give the motion: the
/// pointer stops at the barriers, and a move back lands a frame later, after motion that was still relative to where it
/// was. Under XWayland, with the cursor hidden, the raw events come from the Wayland pointer's relative motion.
/// </summary>
internal static unsafe class X11RelativePointer
{
	const int BarrierPositiveX = 1, BarrierPositiveY = 2, BarrierNegativeX = 4, BarrierNegativeY = 8;
	const int GenericEvent = 35, XI_RawMotion = 17, XIAllMasterDevices = 1;

	// Our own connection: barriers belong to the client that makes them and go when it closes, and the raw events it
	// selects come to it rather than to Qt's
	static IntPtr display;
	static int xinputOpcode = -1;
	static bool warned;

	static nuint window;
	static (int Left, int Top, int Width, int Height) bounds;
	static readonly nuint[] barriers = new nuint[4];

	/// <summary>
	/// Whether the pointer is held by <see cref="Begin"/>.
	/// </summary>
	internal static bool Active => window != 0;

	/// <summary>
	/// Hold the pointer on a native window and start reporting its motion, until <see cref="End"/>. False when the X
	/// server can't (no XInput 2.2), with a warning.
	/// </summary>
	internal static bool Begin( IntPtr nativeWindow )
	{
		End();
		if ( nativeWindow == IntPtr.Zero || !Connect() ) return false;

		// Motion that arrived after the last capture's final TakeMotion is still queued: it isn't this capture's
		XSync( display, 1 );

		window = (nuint)nativeWindow;
		SelectRawMotion( true );
		Hold();
		XFlush( display );
		return true;
	}

	/// <summary>
	/// The motion since the last call, in the mouse's own units as SDL's relative mode reports them. Keeps the pointer
	/// on the window, which may have moved or changed size, and moves it back to the window's middle.
	/// </summary>
	internal static Vector2 TakeMotion()
	{
		if ( !Active ) return default;

		var motion = Vector2.Zero;
		var e = stackalloc byte[192]; // sizeof( XEvent )
		while ( XPending( display ) > 0 )
		{
			XNextEvent( display, e );
			if ( *(int*)e != GenericEvent || *(int*)(e + 32) != xinputOpcode || *(int*)(e + 36) != XI_RawMotion ) continue;
			if ( XGetEventData( display, e ) == 0 ) continue;

			try { motion += RawMotion( *(byte**)(e + 48) ); }
			finally { XFreeEventData( display, e ); }
		}

		Hold();
		XWarpPointer( display, 0, window, 0, 0, 0, 0, bounds.Width / 2, bounds.Height / 2 );
		XFlush( display );
		return motion;
	}

	/// <summary>
	/// Let the pointer go and stop reporting its motion.
	/// </summary>
	internal static void End()
	{
		if ( !Active ) return;

		SelectRawMotion( false );
		DestroyBarriers();
		XFlush( display );
		window = 0;
	}

	static bool Connect()
	{
		if ( display != IntPtr.Zero ) return xinputOpcode >= 0;

		display = XOpenDisplay( IntPtr.Zero );
		if ( display == IntPtr.Zero ) return Fail( "couldn't open the X display" );

		// 2.2: raw events reach the root window whoever grabs the pointer (Qt does, while captured)
		int major = 2, minor = 2;
		if ( XQueryExtension( display, "XInputExtension", out var opcode, out _, out _ ) == 0 || XIQueryVersion( display, ref major, ref minor ) != 0 || major * 10 + minor < 22 )
			return Fail( "the X server doesn't have XInput 2.2" );

		xinputOpcode = opcode;
		return true;
	}

	static bool Fail( string why )
	{
		if ( !warned ) Log.Warning( $"Can't capture the mouse in the play widget: {why}" );
		warned = true;
		return false;
	}

	static void SelectRawMotion( bool select )
	{
		var mask = stackalloc byte[3]; // XI_RawMotion's bit, 17
		mask[0] = mask[1] = mask[2] = 0;
		if ( select ) mask[XI_RawMotion >> 3] = 1 << (XI_RawMotion & 7);

		var eventMask = new XIEventMask { DeviceId = XIAllMasterDevices, MaskLength = 3, Mask = mask };
		XISelectEvents( display, XDefaultRootWindow( display ), &eventMask, 1 );
	}

	/// <summary>
	/// The x and y of a raw event: valuators 0 and 1, of those its mask says it has, unaccelerated.
	/// </summary>
	static Vector2 RawMotion( byte* rawEvent )
	{
		// XIRawEvent: valuators.mask_len at 64, valuators.mask at 72, raw_values at 88
		var maskLength = *(int*)(rawEvent + 64);
		var mask = *(byte**)(rawEvent + 72);
		var values = *(double**)(rawEvent + 88);

		var motion = Vector2.Zero;
		for ( int bit = 0, value = 0; bit < maskLength * 8 && bit < 2; bit++ )
		{
			if ( (mask[bit >> 3] & (1 << (bit & 7))) == 0 ) continue;
			if ( bit == 0 ) motion.x = (float)values[value];
			else motion.y = (float)values[value];
			value++;
		}
		return motion;
	}

	/// <summary>
	/// Barriers on the window's edges as it is now, rebuilt when it moved or changed size.
	/// </summary>
	static void Hold()
	{
		var root = XDefaultRootWindow( display );
		if ( XGetGeometry( display, window, out _, out _, out _, out var width, out var height, out _, out _ ) == 0 ) return;
		if ( XTranslateCoordinates( display, window, root, 0, 0, out var left, out var top, out _ ) == 0 ) return;

		var now = (left, top, (int)width, (int)height);
		if ( now == bounds && barriers[0] != 0 ) return;

		DestroyBarriers();
		bounds = now;

		var right = left + (int)width - 1;
		var bottom = top + (int)height - 1;

		// Each barrier lets the pointer cross it only into the window
		barriers[0] = XFixesCreatePointerBarrier( display, root, left, top, right, top, BarrierPositiveY, 0, IntPtr.Zero );
		barriers[1] = XFixesCreatePointerBarrier( display, root, left, bottom, right, bottom, BarrierNegativeY, 0, IntPtr.Zero );
		barriers[2] = XFixesCreatePointerBarrier( display, root, left, top, left, bottom, BarrierPositiveX, 0, IntPtr.Zero );
		barriers[3] = XFixesCreatePointerBarrier( display, root, right, top, right, bottom, BarrierNegativeX, 0, IntPtr.Zero );
	}

	static void DestroyBarriers()
	{
		for ( var i = 0; i < barriers.Length; i++ )
		{
			if ( barriers[i] == 0 ) continue;
			XFixesDestroyPointerBarrier( display, barriers[i] );
			barriers[i] = 0;
		}
		bounds = default;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct XIEventMask
	{
		public int DeviceId;
		public int MaskLength;
		public byte* Mask;
	}

	[DllImport( "libX11.so.6" )] static extern IntPtr XOpenDisplay( IntPtr name );
	[DllImport( "libX11.so.6" )] static extern nuint XDefaultRootWindow( IntPtr display );
	[DllImport( "libX11.so.6" )] static extern int XFlush( IntPtr display );
	[DllImport( "libX11.so.6" )] static extern int XSync( IntPtr display, int discard );
	[DllImport( "libX11.so.6" )] static extern int XPending( IntPtr display );
	[DllImport( "libX11.so.6" )] static extern int XNextEvent( IntPtr display, byte* e );
	[DllImport( "libX11.so.6" )] static extern int XGetEventData( IntPtr display, byte* cookie );
	[DllImport( "libX11.so.6" )] static extern void XFreeEventData( IntPtr display, byte* cookie );

	[DllImport( "libX11.so.6", CharSet = CharSet.Ansi, BestFitMapping = false )]
	static extern int XQueryExtension( IntPtr display, string name, out int majorOpcode, out int firstEvent, out int firstError );

	[DllImport( "libX11.so.6" )]
	static extern int XWarpPointer( IntPtr display, nuint source, nuint destination, int sourceX, int sourceY, uint sourceWidth, uint sourceHeight, int destinationX, int destinationY );

	[DllImport( "libX11.so.6" )]
	static extern int XGetGeometry( IntPtr display, nuint drawable, out nuint root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth );

	[DllImport( "libX11.so.6" )]
	static extern int XTranslateCoordinates( IntPtr display, nuint source, nuint destination, int x, int y, out int destinationX, out int destinationY, out nuint child );

	[DllImport( "libXi.so.6" )] static extern int XIQueryVersion( IntPtr display, ref int major, ref int minor );
	[DllImport( "libXi.so.6" )] static extern int XISelectEvents( IntPtr display, nuint window, XIEventMask* masks, int count );

	[DllImport( "libXfixes.so.3" )]
	static extern nuint XFixesCreatePointerBarrier( IntPtr display, nuint window, int x1, int y1, int x2, int y2, int directions, int deviceCount, IntPtr devices );

	[DllImport( "libXfixes.so.3" )]
	static extern void XFixesDestroyPointerBarrier( IntPtr display, nuint barrier );
}
