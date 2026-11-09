using System.Runtime.InteropServices;

namespace BasaltGpuSample;

/// <summary>
/// A plain resizable Win32 window through user32, so the sample needs nothing but Basalt.Gpu. s&amp;box would hand Basalt.Gpu
/// its SDL window's handles instead.
/// </summary>
internal sealed unsafe partial class Win32Window : IDisposable
{
	const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
	const uint WS_VISIBLE = 0x10000000;
	const int CW_USEDEFAULT = unchecked((int)0x80000000);
	const uint WM_DESTROY = 0x0002;
	const uint WM_SIZE = 0x0005;
	const uint WM_CLOSE = 0x0010;
	const uint WM_KEYDOWN = 0x0100;
	const uint PM_REMOVE = 0x0001;
	const int SW_SHOW = 5;
	const int VK_ESCAPE = 0x1B;

	[StructLayout( LayoutKind.Sequential )]
	struct WNDCLASSEXW
	{
		public uint cbSize;
		public uint style;
		public delegate* unmanaged< nint, uint, nint, nint, nint > lpfnWndProc;
		public int cbClsExtra;
		public int cbWndExtra;
		public nint hInstance;
		public nint hIcon;
		public nint hCursor;
		public nint hbrBackground;
		public char* lpszMenuName;
		public char* lpszClassName;
		public nint hIconSm;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct MSG
	{
		public nint hwnd;
		public uint message;
		public nint wParam;
		public nint lParam;
		public uint time;
		public int ptX;
		public int ptY;
		public uint lPrivate;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct RECT
	{
		public int left, top, right, bottom;
	}

	[LibraryImport( "kernel32.dll", EntryPoint = "GetModuleHandleW" )]
	private static partial nint GetModuleHandle( char* name );

	[LibraryImport( "user32.dll", EntryPoint = "RegisterClassExW" )]
	private static partial ushort RegisterClassEx( WNDCLASSEXW* wndClass );

	[LibraryImport( "user32.dll", EntryPoint = "CreateWindowExW" )]
	private static partial nint CreateWindowEx( uint exStyle, char* className, char* windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param );

	[LibraryImport( "user32.dll", EntryPoint = "DefWindowProcW" )]
	private static partial nint DefWindowProc( nint hwnd, uint msg, nint wParam, nint lParam );

	[LibraryImport( "user32.dll", EntryPoint = "PeekMessageW" )]
	private static partial int PeekMessage( MSG* msg, nint hwnd, uint min, uint max, uint remove );

	[LibraryImport( "user32.dll" )]
	private static partial int TranslateMessage( MSG* msg );

	[LibraryImport( "user32.dll", EntryPoint = "DispatchMessageW" )]
	private static partial nint DispatchMessage( MSG* msg );

	[LibraryImport( "user32.dll" )]
	private static partial int DestroyWindow( nint hwnd );

	[LibraryImport( "user32.dll" )]
	private static partial int GetClientRect( nint hwnd, RECT* rect );

	[LibraryImport( "user32.dll" )]
	private static partial int SetWindowPos( nint hwnd, nint after, int x, int y, int cx, int cy, uint flags );

	[LibraryImport( "user32.dll" )]
	private static partial int AdjustWindowRect( RECT* rect, uint style, int menu );

	[LibraryImport( "user32.dll", EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16 )]
	private static partial int SetWindowText( nint hwnd, string text );

	[LibraryImport( "user32.dll", EntryPoint = "LoadCursorW" )]
	private static partial nint LoadCursor( nint instance, nint name );

	static Win32Window current;

	/// <summary>The module instance, for the surface.</summary>
	public nint Instance { get; }

	/// <summary>The window handle, for the surface.</summary>
	public nint Handle { get; private set; }

	/// <summary>Whether the user has closed it.</summary>
	public bool Closed { get; private set; }

	/// <summary>Set when the client area changes size; the renderer clears it once it has resized.</summary>
	public bool Resized { get; set; }

	public Win32Window( string title, int width, int height )
	{
		current = this;
		Instance = GetModuleHandle( null );

		fixed ( char* className = "BasaltGpuSample" )
		fixed ( char* text = title )
		{
			var wc = new WNDCLASSEXW
			{
				cbSize = (uint)sizeof( WNDCLASSEXW ),
				lpfnWndProc = &WndProc,
				hInstance = Instance,
				hCursor = LoadCursor( 0, 32512 ), // IDC_ARROW
				lpszClassName = className,
			};

			RegisterClassEx( &wc );

			var rect = new RECT { right = width, bottom = height };
			AdjustWindowRect( &rect, WS_OVERLAPPEDWINDOW, 0 );
			Handle = CreateWindowEx( 0, className, text, WS_OVERLAPPEDWINDOW | WS_VISIBLE, CW_USEDEFAULT, CW_USEDEFAULT,
				rect.right - rect.left, rect.bottom - rect.top, 0, 0, Instance, 0 );
		}

		if ( Handle == 0 ) throw new InvalidOperationException( $"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}" );
	}

	/// <summary>The client area's size.</summary>
	public (int Width, int Height) ClientSize
	{
		get
		{
			RECT rect;
			GetClientRect( Handle, &rect );
			return (rect.right - rect.left, rect.bottom - rect.top);
		}
	}

	/// <summary>Resize the client area, as a user dragging the border would.</summary>
	public void SetClientSize( int width, int height )
	{
		var rect = new RECT { right = width, bottom = height };
		AdjustWindowRect( &rect, WS_OVERLAPPEDWINDOW, 0 );
		SetWindowPos( Handle, 0, 0, 0, rect.right - rect.left, rect.bottom - rect.top, 0x0002 | 0x0004 ); // SWP_NOMOVE | SWP_NOZORDER
	}

	public string Title
	{
		set => SetWindowText( Handle, value );
	}

	/// <summary>Handle every waiting message.</summary>
	public void Pump()
	{
		MSG msg;
		while ( PeekMessage( &msg, 0, 0, 0, PM_REMOVE ) != 0 )
		{
			TranslateMessage( &msg );
			DispatchMessage( &msg );
		}
	}

	[UnmanagedCallersOnly]
	static nint WndProc( nint hwnd, uint msg, nint wParam, nint lParam )
	{
		var window = current;
		switch ( msg )
		{
			case WM_SIZE:
				if ( window is not null ) window.Resized = true;
				break;

			case WM_KEYDOWN when wParam == VK_ESCAPE:
			case WM_CLOSE:
				if ( window is not null ) window.Closed = true;
				return 0;

			case WM_DESTROY:
				if ( window is not null ) window.Closed = true;
				return 0;
		}

		return DefWindowProc( hwnd, msg, wParam, lParam );
	}

	public void Dispose()
	{
		if ( Handle != 0 ) DestroyWindow( Handle );
		Handle = 0;
		if ( current == this ) current = null;
	}
}
