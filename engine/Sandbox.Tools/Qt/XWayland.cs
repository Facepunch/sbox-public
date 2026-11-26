using System;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// Whether Qt's X server is XWayland: it has to be asked, since Qt only ever runs on xcb here.
/// </summary>
internal static class XWayland
{
	static bool? running;

	/// <summary>
	/// True when the X server the editor is a client of is XWayland, which advertises the <c>XWAYLAND</c> extension.
	/// </summary>
	internal static bool IsRunning => running ??= Query();

	static bool Query()
	{
		if ( !OperatingSystem.IsLinux() ) return false;

		var display = XOpenDisplay( IntPtr.Zero );
		if ( display == IntPtr.Zero ) return false;

		try { return XQueryExtension( display, "XWAYLAND", out _, out _, out _ ) != 0; }
		finally { XCloseDisplay( display ); }
	}

	[DllImport( "libX11.so.6" )] static extern IntPtr XOpenDisplay( IntPtr name );
	[DllImport( "libX11.so.6" )] static extern int XCloseDisplay( IntPtr display );
	[DllImport( "libX11.so.6", CharSet = CharSet.Ansi, BestFitMapping = false )]
	static extern int XQueryExtension( IntPtr display, string name, out int opcode, out int firstEvent, out int firstError );
}
