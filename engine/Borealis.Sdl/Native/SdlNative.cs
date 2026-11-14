using System.IO;
using System.Runtime.InteropServices;

namespace Borealis.Sdl.Native;

/// <summary>
/// The SDL3 library and the entry points Borealis.Sdl calls: our own loader, not a binding. Each function is looked up by
/// name into a function pointer once, when the library is loaded. Declarations follow SDL3's headers (3.2 and later,
/// whose ABI is stable): C <c>bool</c> is a byte, <c>SDL_WindowFlags</c> 64 bits, ids 32 bits, <c>VkInstance</c> a pointer
/// and <c>VkSurfaceKHR</c> 64 bits.
/// </summary>
internal static unsafe class SdlNative
{
	public const uint InitVideo = 0x20;
	public const uint InitEvents = 0x4000;

	static nint library;
	static readonly object loadLock = new();

	/// <summary>Where the library was loaded from, or null.</summary>
	public static string LoadedFrom { get; private set; }

	public static delegate* unmanaged< uint, byte > SDL_InitSubSystem;
	public static delegate* unmanaged< uint, void > SDL_QuitSubSystem;
	public static delegate* unmanaged< byte* > SDL_GetError;
	public static delegate* unmanaged< int > SDL_GetVersion;
	public static delegate* unmanaged< byte*, byte*, byte > SDL_SetHint;
	public static delegate* unmanaged< byte* > SDL_GetCurrentVideoDriver;
	public static delegate* unmanaged< void*, void > SDL_free;

	public static delegate* unmanaged< byte*, int, int, ulong, nint > SDL_CreateWindow;
	public static delegate* unmanaged< nint, void > SDL_DestroyWindow;
	public static delegate* unmanaged< nint, uint > SDL_GetWindowID;
	public static delegate* unmanaged< nint, ulong > SDL_GetWindowFlags;
	public static delegate* unmanaged< nint, byte*, byte > SDL_SetWindowTitle;
	public static delegate* unmanaged< nint, byte* > SDL_GetWindowTitle;
	public static delegate* unmanaged< nint, int*, int*, byte > SDL_GetWindowSize;
	public static delegate* unmanaged< nint, int*, int*, byte > SDL_GetWindowSizeInPixels;
	public static delegate* unmanaged< nint, int, int, byte > SDL_SetWindowSize;
	public static delegate* unmanaged< nint, float > SDL_GetWindowPixelDensity;
	public static delegate* unmanaged< nint, float > SDL_GetWindowDisplayScale;
	public static delegate* unmanaged< nint, byte, byte > SDL_SetWindowFullscreen;
	public static delegate* unmanaged< nint, byte > SDL_SyncWindow;
	public static delegate* unmanaged< nint, byte > SDL_ShowWindow;
	public static delegate* unmanaged< nint, byte > SDL_HideWindow;
	public static delegate* unmanaged< nint, byte > SDL_MinimizeWindow;
	public static delegate* unmanaged< nint, byte > SDL_MaximizeWindow;
	public static delegate* unmanaged< nint, byte > SDL_RestoreWindow;
	public static delegate* unmanaged< nint, uint > SDL_GetDisplayForWindow;
	public static delegate* unmanaged< nint, byte, byte > SDL_SetWindowRelativeMouseMode;
	public static delegate* unmanaged< nint, byte > SDL_GetWindowRelativeMouseMode;
	public static delegate* unmanaged< nint, byte > SDL_StartTextInput;
	public static delegate* unmanaged< nint, byte > SDL_StopTextInput;
	public static delegate* unmanaged< nint, uint > SDL_GetWindowProperties;
	public static delegate* unmanaged< uint, byte*, nint, nint > SDL_GetPointerProperty;
	public static delegate* unmanaged< uint, byte*, long, long > SDL_GetNumberProperty;

	public static delegate* unmanaged< SdlRawEvent*, byte > SDL_PollEvent;
	public static delegate* unmanaged< SdlRawEvent*, int, byte > SDL_WaitEventTimeout;

	public static delegate* unmanaged< int*, uint* > SDL_GetDisplays;
	public static delegate* unmanaged< uint > SDL_GetPrimaryDisplay;
	public static delegate* unmanaged< uint, byte* > SDL_GetDisplayName;
	public static delegate* unmanaged< uint, SdlRect*, byte > SDL_GetDisplayBounds;
	public static delegate* unmanaged< uint, float > SDL_GetDisplayContentScale;
	public static delegate* unmanaged< uint, SdlDisplayMode* > SDL_GetCurrentDisplayMode;

	public static delegate* unmanaged< byte*, byte > SDL_SetClipboardText;
	public static delegate* unmanaged< byte* > SDL_GetClipboardText;
	public static delegate* unmanaged< byte > SDL_ShowCursor;
	public static delegate* unmanaged< byte > SDL_HideCursor;
	public static delegate* unmanaged< int, nint > SDL_CreateSystemCursor;
	public static delegate* unmanaged< nint, byte > SDL_SetCursor;
	public static delegate* unmanaged< nint, void > SDL_DestroyCursor;

	public static delegate* unmanaged< byte*, byte > SDL_Vulkan_LoadLibrary;
	public static delegate* unmanaged< void > SDL_Vulkan_UnloadLibrary;
	public static delegate* unmanaged< uint*, byte** > SDL_Vulkan_GetInstanceExtensions;
	public static delegate* unmanaged< nint, nint, void*, ulong*, byte > SDL_Vulkan_CreateSurface;

	/// <summary>The library's file names on this platform, most specific first.</summary>
	public static string[] FileNames => OperatingSystem.IsWindows() ? ["SDL3.dll"]
		: OperatingSystem.IsMacOS() ? ["libSDL3.0.dylib", "libSDL3.dylib"]
		: ["libSDL3.so.0", "libSDL3.so"];

	/// <summary>The engine's native folder for this platform, beside <c>game/bin/managed</c>.</summary>
	static string PlatformFolder => OperatingSystem.IsWindows() ? "win64" : OperatingSystem.IsMacOS() ? "osxarm64" : "linuxsteamrt64";

	/// <summary>
	/// Where to look, in order: the path set on <see cref="SdlLibrary.Path"/>; the application's and this assembly's
	/// folders, and the engine's native folder beside each (<c>../win64</c>, <c>../linuxsteamrt64</c>); then the system's
	/// own search (the loader path, <c>LD_LIBRARY_PATH</c>, the Steam runtime). A process that already has SDL3 loaded gets
	/// that same library back from the system loader.
	/// </summary>
	public static IEnumerable<string> Candidates()
	{
		if ( SdlLibrary.Path is { Length: > 0 } path ) yield return path;

		var folders = new List<string>();
		void AddFolder( string folder )
		{
			if ( string.IsNullOrEmpty( folder ) ) return;
			folder = System.IO.Path.GetFullPath( folder );
			if ( !folders.Contains( folder, StringComparer.OrdinalIgnoreCase ) ) folders.Add( folder );
		}

		AddFolder( AppContext.BaseDirectory );
		AddFolder( System.IO.Path.GetDirectoryName( typeof( SdlNative ).Assembly.Location ) );

		foreach ( var folder in folders )
		{
			foreach ( var name in FileNames )
			{
				yield return System.IO.Path.Combine( folder, name );
				yield return System.IO.Path.Combine( folder, "..", PlatformFolder, name );
			}
		}

		foreach ( var name in FileNames ) yield return name;
	}

	public static bool TryLoad()
	{
		if ( library != 0 ) return true;

		lock ( loadLock )
		{
			if ( library != 0 ) return true;

			foreach ( var candidate in Candidates() )
			{
				var rooted = System.IO.Path.IsPathRooted( candidate );
				if ( rooted && !File.Exists( candidate ) ) continue;
				if ( !NativeLibrary.TryLoad( candidate, out var handle ) ) continue;
				if ( !NativeLibrary.TryGetExport( handle, "SDL_InitSubSystem", out _ ) ) continue;

				Resolve( handle );
				LoadedFrom = rooted ? System.IO.Path.GetFullPath( candidate ) : candidate;
				library = handle;
				return true;
			}

			return false;
		}
	}

	public static void Load()
	{
		if ( !TryLoad() ) throw new SdlException( $"SDL3 wasn't found ({string.Join( ", ", FileNames )}): set SdlLibrary.Path, or put it beside the application" );
	}

	static void Resolve( nint handle )
	{
		nint Need( string name ) => NativeLibrary.TryGetExport( handle, name, out var proc ) ? proc : throw new SdlException( $"This SDL3 has no {name}: Borealis.Sdl needs SDL 3.2 or later" );

		SDL_InitSubSystem = (delegate* unmanaged< uint, byte >)Need( "SDL_InitSubSystem" );
		SDL_QuitSubSystem = (delegate* unmanaged< uint, void >)Need( "SDL_QuitSubSystem" );
		SDL_GetError = (delegate* unmanaged< byte* >)Need( "SDL_GetError" );
		SDL_GetVersion = (delegate* unmanaged< int >)Need( "SDL_GetVersion" );
		SDL_SetHint = (delegate* unmanaged< byte*, byte*, byte >)Need( "SDL_SetHint" );
		SDL_GetCurrentVideoDriver = (delegate* unmanaged< byte* >)Need( "SDL_GetCurrentVideoDriver" );
		SDL_free = (delegate* unmanaged< void*, void >)Need( "SDL_free" );

		SDL_CreateWindow = (delegate* unmanaged< byte*, int, int, ulong, nint >)Need( "SDL_CreateWindow" );
		SDL_DestroyWindow = (delegate* unmanaged< nint, void >)Need( "SDL_DestroyWindow" );
		SDL_GetWindowID = (delegate* unmanaged< nint, uint >)Need( "SDL_GetWindowID" );
		SDL_GetWindowFlags = (delegate* unmanaged< nint, ulong >)Need( "SDL_GetWindowFlags" );
		SDL_SetWindowTitle = (delegate* unmanaged< nint, byte*, byte >)Need( "SDL_SetWindowTitle" );
		SDL_GetWindowTitle = (delegate* unmanaged< nint, byte* >)Need( "SDL_GetWindowTitle" );
		SDL_GetWindowSize = (delegate* unmanaged< nint, int*, int*, byte >)Need( "SDL_GetWindowSize" );
		SDL_GetWindowSizeInPixels = (delegate* unmanaged< nint, int*, int*, byte >)Need( "SDL_GetWindowSizeInPixels" );
		SDL_SetWindowSize = (delegate* unmanaged< nint, int, int, byte >)Need( "SDL_SetWindowSize" );
		SDL_GetWindowPixelDensity = (delegate* unmanaged< nint, float >)Need( "SDL_GetWindowPixelDensity" );
		SDL_GetWindowDisplayScale = (delegate* unmanaged< nint, float >)Need( "SDL_GetWindowDisplayScale" );
		SDL_SetWindowFullscreen = (delegate* unmanaged< nint, byte, byte >)Need( "SDL_SetWindowFullscreen" );
		SDL_SyncWindow = (delegate* unmanaged< nint, byte >)Need( "SDL_SyncWindow" );
		SDL_ShowWindow = (delegate* unmanaged< nint, byte >)Need( "SDL_ShowWindow" );
		SDL_HideWindow = (delegate* unmanaged< nint, byte >)Need( "SDL_HideWindow" );
		SDL_MinimizeWindow = (delegate* unmanaged< nint, byte >)Need( "SDL_MinimizeWindow" );
		SDL_MaximizeWindow = (delegate* unmanaged< nint, byte >)Need( "SDL_MaximizeWindow" );
		SDL_RestoreWindow = (delegate* unmanaged< nint, byte >)Need( "SDL_RestoreWindow" );
		SDL_GetDisplayForWindow = (delegate* unmanaged< nint, uint >)Need( "SDL_GetDisplayForWindow" );
		SDL_SetWindowRelativeMouseMode = (delegate* unmanaged< nint, byte, byte >)Need( "SDL_SetWindowRelativeMouseMode" );
		SDL_GetWindowRelativeMouseMode = (delegate* unmanaged< nint, byte >)Need( "SDL_GetWindowRelativeMouseMode" );
		SDL_StartTextInput = (delegate* unmanaged< nint, byte >)Need( "SDL_StartTextInput" );
		SDL_StopTextInput = (delegate* unmanaged< nint, byte >)Need( "SDL_StopTextInput" );
		SDL_GetWindowProperties = (delegate* unmanaged< nint, uint >)Need( "SDL_GetWindowProperties" );
		SDL_GetPointerProperty = (delegate* unmanaged< uint, byte*, nint, nint >)Need( "SDL_GetPointerProperty" );
		SDL_GetNumberProperty = (delegate* unmanaged< uint, byte*, long, long >)Need( "SDL_GetNumberProperty" );

		SDL_PollEvent = (delegate* unmanaged< SdlRawEvent*, byte >)Need( "SDL_PollEvent" );
		SDL_WaitEventTimeout = (delegate* unmanaged< SdlRawEvent*, int, byte >)Need( "SDL_WaitEventTimeout" );

		SDL_GetDisplays = (delegate* unmanaged< int*, uint* >)Need( "SDL_GetDisplays" );
		SDL_GetPrimaryDisplay = (delegate* unmanaged< uint >)Need( "SDL_GetPrimaryDisplay" );
		SDL_GetDisplayName = (delegate* unmanaged< uint, byte* >)Need( "SDL_GetDisplayName" );
		SDL_GetDisplayBounds = (delegate* unmanaged< uint, SdlRect*, byte >)Need( "SDL_GetDisplayBounds" );
		SDL_GetDisplayContentScale = (delegate* unmanaged< uint, float >)Need( "SDL_GetDisplayContentScale" );
		SDL_GetCurrentDisplayMode = (delegate* unmanaged< uint, SdlDisplayMode* >)Need( "SDL_GetCurrentDisplayMode" );

		SDL_SetClipboardText = (delegate* unmanaged< byte*, byte >)Need( "SDL_SetClipboardText" );
		SDL_GetClipboardText = (delegate* unmanaged< byte* >)Need( "SDL_GetClipboardText" );
		SDL_ShowCursor = (delegate* unmanaged< byte >)Need( "SDL_ShowCursor" );
		SDL_HideCursor = (delegate* unmanaged< byte >)Need( "SDL_HideCursor" );
		SDL_CreateSystemCursor = (delegate* unmanaged< int, nint >)Need( "SDL_CreateSystemCursor" );
		SDL_SetCursor = (delegate* unmanaged< nint, byte >)Need( "SDL_SetCursor" );
		SDL_DestroyCursor = (delegate* unmanaged< nint, void >)Need( "SDL_DestroyCursor" );

		SDL_Vulkan_LoadLibrary = (delegate* unmanaged< byte*, byte >)Need( "SDL_Vulkan_LoadLibrary" );
		SDL_Vulkan_UnloadLibrary = (delegate* unmanaged< void >)Need( "SDL_Vulkan_UnloadLibrary" );
		SDL_Vulkan_GetInstanceExtensions = (delegate* unmanaged< uint*, byte** >)Need( "SDL_Vulkan_GetInstanceExtensions" );
		SDL_Vulkan_CreateSurface = (delegate* unmanaged< nint, nint, void*, ulong*, byte >)Need( "SDL_Vulkan_CreateSurface" );
	}

	/// <summary>SDL's message for the last call that failed.</summary>
	public static string LastError => SDL_GetError == null ? "" : Marshal.PtrToStringUTF8( (nint)SDL_GetError() ) ?? "";

	/// <summary>Throw with SDL's message when a call returned false.</summary>
	public static void Check( byte result, string call )
	{
		if ( result == 0 ) throw new SdlException( $"{call} failed: {LastError}" );
	}

	/// <summary>A NUL-terminated UTF-8 copy of a string, for SDL to read.</summary>
	public static byte[] Utf8( string text ) => text is null ? null : System.Text.Encoding.UTF8.GetBytes( text + '\0' );

	public static string String( byte* text ) => text == null ? null : Marshal.PtrToStringUTF8( (nint)text );
}

/// <summary>
/// <c>SDL_Event</c>: a 128-byte union whose first 32 bits are its type. Only the members Borealis.Sdl reads are mapped,
/// at their offsets in SDL3's headers; pointers in it are SDL's until the next poll.
/// </summary>
[StructLayout( LayoutKind.Explicit, Size = 128 )]
internal struct SdlRawEvent
{
	[FieldOffset( 0 )] public uint Type;
	[FieldOffset( 8 )] public ulong Timestamp;

	/// <summary>windowID of window, keyboard, text and mouse events; displayID of display events.</summary>
	[FieldOffset( 16 )] public uint WindowId;

	// SDL_WindowEvent and SDL_DisplayEvent
	[FieldOffset( 20 )] public int Data1;
	[FieldOffset( 24 )] public int Data2;

	// SDL_KeyboardEvent
	[FieldOffset( 24 )] public int Scancode;
	[FieldOffset( 28 )] public uint Keycode;
	[FieldOffset( 32 )] public ushort Modifiers;
	[FieldOffset( 36 )] public byte KeyDown;
	[FieldOffset( 37 )] public byte KeyRepeat;

	// SDL_TextInputEvent
	[FieldOffset( 24 )] public nint Text;

	// SDL_MouseMotionEvent
	[FieldOffset( 24 )] public uint MotionState;
	[FieldOffset( 28 )] public float MotionX;
	[FieldOffset( 32 )] public float MotionY;
	[FieldOffset( 36 )] public float MotionXRel;
	[FieldOffset( 40 )] public float MotionYRel;

	// SDL_MouseButtonEvent
	[FieldOffset( 24 )] public byte Button;
	[FieldOffset( 25 )] public byte ButtonDown;
	[FieldOffset( 26 )] public byte Clicks;
	[FieldOffset( 28 )] public float ButtonX;
	[FieldOffset( 32 )] public float ButtonY;

	// SDL_MouseWheelEvent
	[FieldOffset( 24 )] public float WheelX;
	[FieldOffset( 28 )] public float WheelY;
	[FieldOffset( 32 )] public uint WheelDirection;
	[FieldOffset( 36 )] public float WheelMouseX;
	[FieldOffset( 40 )] public float WheelMouseY;
}

[StructLayout( LayoutKind.Sequential )]
internal struct SdlRect
{
	public int X, Y, W, H;
}

/// <summary><c>SDL_DisplayMode</c>.</summary>
[StructLayout( LayoutKind.Sequential )]
internal struct SdlDisplayMode
{
	public uint DisplayId;
	public uint Format;
	public int W, H;
	public float PixelDensity;
	public float RefreshRate;
	public int RefreshRateNumerator, RefreshRateDenominator;
	public nint Internal;
}
