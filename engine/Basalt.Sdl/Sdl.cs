using Basalt.Sdl.Native;
using System.Runtime.InteropServices;
using static Basalt.Sdl.Native.SdlNative;

namespace Basalt.Sdl;

/// <summary>
/// SDL's video and event subsystems for one process: events, displays, the clipboard, cursors and what Vulkan needs.
/// Call from the thread that makes the windows and pumps events (the main thread; macOS requires it).
/// </summary>
/// <remarks>
/// Basalt.Sdl shares the process's one SDL with anything else that uses it, the engine included. It only ever
/// initialises and quits the video and event subsystems, which SDL counts, so it never shuts down SDL under someone else
/// (<c>SDL_Quit</c> is never called). Only one side may pump events: a program that owns its loop calls
/// <see cref="PollEvent"/>; a host that pumps SDL itself passes the raw events it polls to <see cref="Translate"/>.
/// </remarks>
public static unsafe class Sdl
{
	static readonly object initLock = new();
	static int initCount;
	static bool vulkanLoaded;
	static readonly Dictionary<uint, SdlWindow> windows = new();
	static readonly Dictionary<SdlCursor, nint> cursors = new();

	/// <summary>Whether the video subsystem is up, through <see cref="Init"/> or a window.</summary>
	public static bool IsInitialized => initCount > 0;

	/// <summary>
	/// Start SDL's video and event subsystems, or count another user of them. Windows do this themselves. Throws
	/// <see cref="SdlException"/> when SDL3 is missing or there's no display to open windows on.
	/// </summary>
	public static void Init()
	{
		if ( !TryInit( out var error ) ) throw new SdlException( error );
	}

	/// <summary>
	/// <see cref="Init"/>, returning false with SDL's reason instead of throwing: no SDL3, or no display (a headless
	/// machine, an SSH session without a display).
	/// </summary>
	public static bool TryInit( out string error )
	{
		error = null;
		lock ( initLock )
		{
			if ( initCount > 0 )
			{
				initCount++;
				return true;
			}

			if ( !SdlNative.TryLoad() )
			{
				error = $"SDL3 wasn't found ({string.Join( ", ", SdlNative.FileNames )})";
				return false;
			}

			if ( SDL_InitSubSystem( InitVideo | InitEvents ) == 0 )
			{
				error = $"SDL couldn't start video: {LastError}";
				return false;
			}

			initCount = 1;
			return true;
		}
	}

	/// <summary>
	/// Let go of one <see cref="Init"/>. The last one shuts down the subsystems Basalt.Sdl started; SDL itself stays up
	/// if anything else in the process started it.
	/// </summary>
	public static void Quit()
	{
		lock ( initLock )
		{
			if ( initCount == 0 ) return;
			if ( --initCount > 0 ) return;

			foreach ( var cursor in cursors.Values ) SDL_DestroyCursor( cursor );
			cursors.Clear();

			if ( vulkanLoaded )
			{
				SDL_Vulkan_UnloadLibrary();
				vulkanLoaded = false;
			}

			SDL_QuitSubSystem( InitVideo | InitEvents );
		}
	}

	internal static void RequireInit()
	{
		if ( initCount == 0 ) throw new InvalidOperationException( "Sdl.Init first, or make a window" );
	}

	/// <summary>The SDL version running, as major.minor.patch.</summary>
	public static Version Version
	{
		get
		{
			SdlNative.Load();
			var v = SDL_GetVersion();
			return new Version( v / 1000000, v / 1000 % 1000, v % 1000 );
		}
	}

	/// <summary>The video driver in use: <c>windows</c>, <c>x11</c>, <c>wayland</c>, <c>cocoa</c>, <c>offscreen</c>...</summary>
	public static string VideoDriver => IsInitialized ? SdlNative.String( SDL_GetCurrentVideoDriver() ) : null;

	/// <summary>
	/// Set one of SDL's hints (<c>SDL_HINT_*</c> names, such as <c>SDL_VIDEO_DRIVER</c> to pick <c>x11</c> or
	/// <c>wayland</c>). Most must be set before <see cref="Init"/>.
	/// </summary>
	public static bool SetHint( string name, string value )
	{
		SdlNative.Load();
		fixed ( byte* n = Utf8( name ) )
		fixed ( byte* v = Utf8( value ) )
			return SDL_SetHint( n, v ) != 0;
	}

	/// <summary>
	/// The Vulkan instance extensions SDL needs to make surfaces for this video driver. Enable them on the instance
	/// (<c>DeviceOptions.InstanceExtensions</c> in Basalt.Gpu) before <see cref="SdlWindow.CreateVulkanSurface"/>.
	/// </summary>
	public static IReadOnlyList<string> VulkanInstanceExtensions
	{
		get
		{
			RequireInit();
			lock ( initLock )
			{
				if ( !vulkanLoaded )
				{
					Check( SDL_Vulkan_LoadLibrary( null ), "SDL_Vulkan_LoadLibrary" );
					vulkanLoaded = true;
				}
			}

			uint count;
			var names = SDL_Vulkan_GetInstanceExtensions( &count );
			if ( names == null ) throw new SdlException( $"SDL_Vulkan_GetInstanceExtensions failed: {LastError}" );

			var list = new string[count];
			for ( int i = 0; i < count; i++ ) list[i] = SdlNative.String( names[i] );
			return list;
		}
	}

	/// <summary>
	/// Take the next waiting event, if there is one, after letting the OS deliver new ones. Windows see their own events
	/// first: <see cref="SdlWindow.CloseRequested"/> and <see cref="SdlWindow.Resized"/> are updated before it returns.
	/// </summary>
	public static bool PollEvent( out SdlEvent e )
	{
		SdlRawEvent raw;
		if ( SDL_PollEvent( &raw ) == 0 )
		{
			e = default;
			return false;
		}

		e = TranslateRaw( raw );
		Dispatch( e );
		return true;
	}

	/// <summary>
	/// Wait up to <paramref name="timeoutMs"/> for an event (-1 forever), for tools that only redraw when something
	/// happens. Updates windows as <see cref="PollEvent"/> does.
	/// </summary>
	public static bool WaitEvent( out SdlEvent e, int timeoutMs )
	{
		SdlRawEvent raw;
		if ( SDL_WaitEventTimeout( &raw, timeoutMs ) == 0 )
		{
			e = default;
			return false;
		}

		e = TranslateRaw( raw );
		Dispatch( e );
		return true;
	}

	/// <summary>
	/// Handle every waiting event, for a program that only cares about its windows closing and resizing.
	/// </summary>
	public static void PumpEvents()
	{
		while ( PollEvent( out _ ) ) { }
	}

	/// <summary>
	/// An <c>SDL_Event</c> someone else polled, as a plain value: for a host that pumps SDL itself. The pointer is only
	/// read during the call.
	/// </summary>
	public static SdlEvent Translate( nint sdlEvent )
	{
		if ( sdlEvent == 0 ) throw new ArgumentNullException( nameof( sdlEvent ) );
		return TranslateRaw( *(SdlRawEvent*)sdlEvent );
	}

	/// <summary>The size of <c>SDL_Event</c>, in bytes, for hosts that hand events over.</summary>
	public static int RawEventSize => sizeof( SdlRawEvent );

	internal static SdlEvent TranslateRaw( in SdlRawEvent raw )
	{
		var kind = KindOf( raw.Type );
		var e = new SdlEvent { Kind = kind, Type = raw.Type, Timestamp = raw.Timestamp };

		switch ( kind )
		{
			case SdlEventKind.Quit or SdlEventKind.Other:
				return e;

			case SdlEventKind.DisplayAdded or SdlEventKind.DisplayRemoved or SdlEventKind.DisplayChanged:
				return e with { DisplayId = raw.WindowId };

			case SdlEventKind.WindowMoved:
				return e with { WindowId = raw.WindowId, X = raw.Data1, Y = raw.Data2 };

			case SdlEventKind.WindowResized or SdlEventKind.WindowPixelSizeChanged:
				return e with { WindowId = raw.WindowId, Width = raw.Data1, Height = raw.Data2 };

			case SdlEventKind.WindowDisplayChanged:
				return e with { WindowId = raw.WindowId, DisplayId = (uint)raw.Data1 };

			case SdlEventKind.KeyDown or SdlEventKind.KeyUp:
				return e with
				{
					WindowId = raw.WindowId,
					Key = (SdlKey)raw.Scancode,
					Keycode = raw.Keycode,
					Modifiers = (SdlKeyModifiers)raw.Modifiers,
					Repeat = raw.KeyRepeat != 0,
				};

			case SdlEventKind.TextInput:
				return e with { WindowId = raw.WindowId, Text = Marshal.PtrToStringUTF8( raw.Text ) };

			case SdlEventKind.MouseMotion:
				return e with { WindowId = raw.WindowId, X = raw.MotionX, Y = raw.MotionY, DeltaX = raw.MotionXRel, DeltaY = raw.MotionYRel };

			case SdlEventKind.MouseButtonDown or SdlEventKind.MouseButtonUp:
				return e with { WindowId = raw.WindowId, Button = (SdlMouseButton)raw.Button, Clicks = raw.Clicks, X = raw.ButtonX, Y = raw.ButtonY };

			case SdlEventKind.MouseWheel:
				{
					var flip = raw.WheelDirection == 1 ? -1 : 1; // SDL_MOUSEWHEEL_FLIPPED: report what the user did
					return e with { WindowId = raw.WindowId, DeltaX = raw.WheelX * flip, DeltaY = raw.WheelY * flip, X = raw.WheelMouseX, Y = raw.WheelMouseY };
				}

			default:
				return e with { WindowId = raw.WindowId };
		}
	}

	static SdlEventKind KindOf( uint type ) => type switch
	{
		0x100 => SdlEventKind.Quit,
		0x151 or 0x154 or 0x155 or 0x156 or 0x157 or 0x158 => SdlEventKind.DisplayChanged,
		0x152 => SdlEventKind.DisplayAdded,
		0x153 => SdlEventKind.DisplayRemoved,
		0x202 => SdlEventKind.WindowShown,
		0x203 => SdlEventKind.WindowHidden,
		0x204 => SdlEventKind.WindowExposed,
		0x205 => SdlEventKind.WindowMoved,
		0x206 => SdlEventKind.WindowResized,
		0x207 => SdlEventKind.WindowPixelSizeChanged,
		0x209 => SdlEventKind.WindowMinimized,
		0x20A => SdlEventKind.WindowMaximized,
		0x20B => SdlEventKind.WindowRestored,
		0x20C => SdlEventKind.WindowMouseEnter,
		0x20D => SdlEventKind.WindowMouseLeave,
		0x20E => SdlEventKind.WindowFocusGained,
		0x20F => SdlEventKind.WindowFocusLost,
		0x210 => SdlEventKind.WindowCloseRequested,
		0x213 => SdlEventKind.WindowDisplayChanged,
		0x214 => SdlEventKind.WindowDisplayScaleChanged,
		0x216 => SdlEventKind.WindowOccluded,
		0x217 => SdlEventKind.WindowEnterFullscreen,
		0x218 => SdlEventKind.WindowLeaveFullscreen,
		0x219 => SdlEventKind.WindowDestroyed,
		0x300 => SdlEventKind.KeyDown,
		0x301 => SdlEventKind.KeyUp,
		0x303 => SdlEventKind.TextInput,
		0x400 => SdlEventKind.MouseMotion,
		0x401 => SdlEventKind.MouseButtonDown,
		0x402 => SdlEventKind.MouseButtonUp,
		0x403 => SdlEventKind.MouseWheel,
		_ => SdlEventKind.Other,
	};

	/// <summary>Let the windows Basalt.Sdl knows see their own events.</summary>
	static void Dispatch( in SdlEvent e )
	{
		if ( e.Kind == SdlEventKind.Quit )
		{
			lock ( windows )
			{
				foreach ( var window in windows.Values ) window.RequestClose();
			}

			return;
		}

		if ( e.WindowId == 0 ) return;

		SdlWindow target;
		lock ( windows ) windows.TryGetValue( e.WindowId, out target );
		target?.OnEvent( e );
	}

	internal static void Register( SdlWindow window )
	{
		lock ( windows ) windows[window.Id] = window;
	}

	internal static void Unregister( SdlWindow window )
	{
		lock ( windows )
		{
			if ( windows.TryGetValue( window.Id, out var current ) && current == window ) windows.Remove( window.Id );
		}
	}

	/// <summary>The window Basalt.Sdl knows by this id, or null.</summary>
	public static SdlWindow FindWindow( uint id )
	{
		lock ( windows ) return windows.GetValueOrDefault( id );
	}

	/// <summary>The connected displays.</summary>
	public static IReadOnlyList<SdlDisplay> Displays
	{
		get
		{
			RequireInit();
			int count;
			var ids = SDL_GetDisplays( &count );
			if ( ids == null ) return [];

			var list = new SdlDisplay[count];
			for ( int i = 0; i < count; i++ ) list[i] = SdlDisplay.From( ids[i] );
			SDL_free( ids );
			return list;
		}
	}

	/// <summary>The primary display, or null.</summary>
	public static SdlDisplay PrimaryDisplay
	{
		get
		{
			RequireInit();
			var id = SDL_GetPrimaryDisplay();
			return id == 0 ? null : SdlDisplay.From( id );
		}
	}

	/// <summary>The clipboard's text, or empty.</summary>
	public static string ClipboardText
	{
		get
		{
			RequireInit();
			var text = SDL_GetClipboardText();
			var value = SdlNative.String( text ) ?? "";
			SDL_free( text );
			return value;
		}

		set
		{
			RequireInit();
			fixed ( byte* text = Utf8( value ?? "" ) ) Check( SDL_SetClipboardText( text ), "SDL_SetClipboardText" );
		}
	}

	/// <summary>Show or hide the mouse cursor.</summary>
	public static void ShowCursor( bool visible )
	{
		RequireInit();
		Check( visible ? SDL_ShowCursor() : SDL_HideCursor(), visible ? "SDL_ShowCursor" : "SDL_HideCursor" );
	}

	/// <summary>Use one of the system's cursor shapes.</summary>
	public static void SetCursor( SdlCursor shape )
	{
		RequireInit();
		if ( !cursors.TryGetValue( shape, out var cursor ) )
		{
			cursor = SDL_CreateSystemCursor( (int)shape );
			if ( cursor == 0 ) throw new SdlException( $"SDL_CreateSystemCursor failed: {LastError}" );
			cursors[shape] = cursor;
		}

		Check( SDL_SetCursor( cursor ), "SDL_SetCursor" );
	}
}

/// <summary>
/// The system's cursor shapes (<c>SDL_SystemCursor</c>).
/// </summary>
public enum SdlCursor
{
	/// <summary>The default arrow.</summary>
	Default,

	/// <summary>A text I-beam.</summary>
	Text,

	/// <summary>Busy.</summary>
	Wait,

	/// <summary>A crosshair.</summary>
	Crosshair,

	/// <summary>Busy in the background: arrow and hourglass.</summary>
	Progress,

	/// <summary>Resize from the top-left or bottom-right corner.</summary>
	NwseResize,

	/// <summary>Resize from the top-right or bottom-left corner.</summary>
	NeswResize,

	/// <summary>Resize sideways.</summary>
	EwResize,

	/// <summary>Resize up and down.</summary>
	NsResize,

	/// <summary>Move.</summary>
	Move,

	/// <summary>Not allowed.</summary>
	NotAllowed,

	/// <summary>A pointing hand, for links.</summary>
	Pointer,
}
