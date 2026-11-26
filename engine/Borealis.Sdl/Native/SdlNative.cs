using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

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
	public static delegate* unmanaged< nint, SdlDisplayMode*, byte > SDL_SetWindowFullscreenMode;
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
	public static delegate* unmanaged< uint > SDL_CreateProperties;
	public static delegate* unmanaged< uint, void > SDL_DestroyProperties;
	public static delegate* unmanaged< uint, byte*, nint, byte > SDL_SetPointerProperty;
	public static delegate* unmanaged< uint, byte*, byte, byte > SDL_SetBooleanProperty;
	public static delegate* unmanaged< uint, nint > SDL_CreateWindowWithProperties;

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
		if ( Volatile.Read( ref library ) != 0 ) return true;

		lock ( loadLock )
		{
			if ( library != 0 ) return true;

			foreach ( var candidate in Candidates() )
			{
				var rooted = System.IO.Path.IsPathRooted( candidate );
				if ( rooted && !File.Exists( candidate ) ) continue;
				if ( !NativeLibrary.TryLoad( candidate, out var handle ) ) continue;
				try
				{
					var loadedFrom = rooted ? System.IO.Path.GetFullPath( candidate ) : candidate;
					if ( !TryResolve( handle ) ) continue;
					LoadedFrom = loadedFrom;
					Volatile.Write( ref library, handle );
					return true;
				}
				finally
				{
					if ( library == 0 ) NativeLibrary.Free( handle );
				}
			}

			return false;
		}
	}

	public static void Load()
	{
		if ( !TryLoad() ) throw new SdlException( $"SDL3 wasn't found ({string.Join( ", ", FileNames )}): set SdlLibrary.Path, or put it beside the application" );
	}

	static bool TryResolve( nint handle )
	{
		bool Find( string name, out nint proc ) => NativeLibrary.TryGetExport( handle, name, out proc );

		if ( !Find( nameof( SDL_InitSubSystem ), out var initSubSystem ) ||
			!Find( nameof( SDL_QuitSubSystem ), out var quitSubSystem ) ||
			!Find( nameof( SDL_GetError ), out var getError ) ||
			!Find( nameof( SDL_GetVersion ), out var getVersion ) ||
			!Find( nameof( SDL_SetHint ), out var setHint ) ||
			!Find( nameof( SDL_GetCurrentVideoDriver ), out var getCurrentVideoDriver ) ||
			!Find( nameof( SDL_free ), out var free ) ||
			!Find( nameof( SDL_CreateWindow ), out var createWindow ) ||
			!Find( nameof( SDL_DestroyWindow ), out var destroyWindow ) ||
			!Find( nameof( SDL_GetWindowID ), out var getWindowId ) ||
			!Find( nameof( SDL_GetWindowFlags ), out var getWindowFlags ) ||
			!Find( nameof( SDL_SetWindowTitle ), out var setWindowTitle ) ||
			!Find( nameof( SDL_GetWindowTitle ), out var getWindowTitle ) ||
			!Find( nameof( SDL_GetWindowSize ), out var getWindowSize ) ||
			!Find( nameof( SDL_GetWindowSizeInPixels ), out var getWindowSizeInPixels ) ||
			!Find( nameof( SDL_SetWindowSize ), out var setWindowSize ) ||
			!Find( nameof( SDL_GetWindowPixelDensity ), out var getWindowPixelDensity ) ||
			!Find( nameof( SDL_GetWindowDisplayScale ), out var getWindowDisplayScale ) ||
			!Find( nameof( SDL_SetWindowFullscreen ), out var setWindowFullscreen ) ||
			!Find( nameof( SDL_SetWindowFullscreenMode ), out var setWindowFullscreenMode ) ||
			!Find( nameof( SDL_SyncWindow ), out var syncWindow ) ||
			!Find( nameof( SDL_ShowWindow ), out var showWindow ) ||
			!Find( nameof( SDL_HideWindow ), out var hideWindow ) ||
			!Find( nameof( SDL_MinimizeWindow ), out var minimizeWindow ) ||
			!Find( nameof( SDL_MaximizeWindow ), out var maximizeWindow ) ||
			!Find( nameof( SDL_RestoreWindow ), out var restoreWindow ) ||
			!Find( nameof( SDL_GetDisplayForWindow ), out var getDisplayForWindow ) ||
			!Find( nameof( SDL_SetWindowRelativeMouseMode ), out var setWindowRelativeMouseMode ) ||
			!Find( nameof( SDL_GetWindowRelativeMouseMode ), out var getWindowRelativeMouseMode ) ||
			!Find( nameof( SDL_StartTextInput ), out var startTextInput ) ||
			!Find( nameof( SDL_StopTextInput ), out var stopTextInput ) ||
			!Find( nameof( SDL_GetWindowProperties ), out var getWindowProperties ) ||
			!Find( nameof( SDL_GetPointerProperty ), out var getPointerProperty ) ||
			!Find( nameof( SDL_GetNumberProperty ), out var getNumberProperty ) ||
			!Find( nameof( SDL_CreateProperties ), out var createProperties ) ||
			!Find( nameof( SDL_DestroyProperties ), out var destroyProperties ) ||
			!Find( nameof( SDL_SetPointerProperty ), out var setPointerProperty ) ||
			!Find( nameof( SDL_SetBooleanProperty ), out var setBooleanProperty ) ||
			!Find( nameof( SDL_CreateWindowWithProperties ), out var createWindowWithProperties ) ||
			!Find( nameof( SDL_PollEvent ), out var pollEvent ) ||
			!Find( nameof( SDL_WaitEventTimeout ), out var waitEventTimeout ) ||
			!Find( nameof( SDL_GetDisplays ), out var getDisplays ) ||
			!Find( nameof( SDL_GetPrimaryDisplay ), out var getPrimaryDisplay ) ||
			!Find( nameof( SDL_GetDisplayName ), out var getDisplayName ) ||
			!Find( nameof( SDL_GetDisplayBounds ), out var getDisplayBounds ) ||
			!Find( nameof( SDL_GetDisplayContentScale ), out var getDisplayContentScale ) ||
			!Find( nameof( SDL_GetCurrentDisplayMode ), out var getCurrentDisplayMode ) ||
			!Find( nameof( SDL_SetClipboardText ), out var setClipboardText ) ||
			!Find( nameof( SDL_GetClipboardText ), out var getClipboardText ) ||
			!Find( nameof( SDL_ShowCursor ), out var showCursor ) ||
			!Find( nameof( SDL_HideCursor ), out var hideCursor ) ||
			!Find( nameof( SDL_CreateSystemCursor ), out var createSystemCursor ) ||
			!Find( nameof( SDL_SetCursor ), out var setCursor ) ||
			!Find( nameof( SDL_DestroyCursor ), out var destroyCursor ) ||
			!Find( nameof( SDL_Vulkan_LoadLibrary ), out var vulkanLoadLibrary ) ||
			!Find( nameof( SDL_Vulkan_UnloadLibrary ), out var vulkanUnloadLibrary ) ||
			!Find( nameof( SDL_Vulkan_GetInstanceExtensions ), out var vulkanGetInstanceExtensions ) ||
			!Find( nameof( SDL_Vulkan_CreateSurface ), out var vulkanCreateSurface ) )
			return false;

		// Check the version only after finding SDL3 exports: SDL2's SDL_GetVersion has a different signature.
		if ( ((delegate* unmanaged< int >)getVersion)() < 3_002_000 ) return false;

		SDL_InitSubSystem = (delegate* unmanaged< uint, byte >)initSubSystem;
		SDL_QuitSubSystem = (delegate* unmanaged< uint, void >)quitSubSystem;
		SDL_GetError = (delegate* unmanaged< byte* >)getError;
		SDL_GetVersion = (delegate* unmanaged< int >)getVersion;
		SDL_SetHint = (delegate* unmanaged< byte*, byte*, byte >)setHint;
		SDL_GetCurrentVideoDriver = (delegate* unmanaged< byte* >)getCurrentVideoDriver;
		SDL_free = (delegate* unmanaged< void*, void >)free;

		SDL_CreateWindow = (delegate* unmanaged< byte*, int, int, ulong, nint >)createWindow;
		SDL_DestroyWindow = (delegate* unmanaged< nint, void >)destroyWindow;
		SDL_GetWindowID = (delegate* unmanaged< nint, uint >)getWindowId;
		SDL_GetWindowFlags = (delegate* unmanaged< nint, ulong >)getWindowFlags;
		SDL_SetWindowTitle = (delegate* unmanaged< nint, byte*, byte >)setWindowTitle;
		SDL_GetWindowTitle = (delegate* unmanaged< nint, byte* >)getWindowTitle;
		SDL_GetWindowSize = (delegate* unmanaged< nint, int*, int*, byte >)getWindowSize;
		SDL_GetWindowSizeInPixels = (delegate* unmanaged< nint, int*, int*, byte >)getWindowSizeInPixels;
		SDL_SetWindowSize = (delegate* unmanaged< nint, int, int, byte >)setWindowSize;
		SDL_GetWindowPixelDensity = (delegate* unmanaged< nint, float >)getWindowPixelDensity;
		SDL_GetWindowDisplayScale = (delegate* unmanaged< nint, float >)getWindowDisplayScale;
		SDL_SetWindowFullscreen = (delegate* unmanaged< nint, byte, byte >)setWindowFullscreen;
		SDL_SetWindowFullscreenMode = (delegate* unmanaged< nint, SdlDisplayMode*, byte >)setWindowFullscreenMode;
		SDL_SyncWindow = (delegate* unmanaged< nint, byte >)syncWindow;
		SDL_ShowWindow = (delegate* unmanaged< nint, byte >)showWindow;
		SDL_HideWindow = (delegate* unmanaged< nint, byte >)hideWindow;
		SDL_MinimizeWindow = (delegate* unmanaged< nint, byte >)minimizeWindow;
		SDL_MaximizeWindow = (delegate* unmanaged< nint, byte >)maximizeWindow;
		SDL_RestoreWindow = (delegate* unmanaged< nint, byte >)restoreWindow;
		SDL_GetDisplayForWindow = (delegate* unmanaged< nint, uint >)getDisplayForWindow;
		SDL_SetWindowRelativeMouseMode = (delegate* unmanaged< nint, byte, byte >)setWindowRelativeMouseMode;
		SDL_GetWindowRelativeMouseMode = (delegate* unmanaged< nint, byte >)getWindowRelativeMouseMode;
		SDL_StartTextInput = (delegate* unmanaged< nint, byte >)startTextInput;
		SDL_StopTextInput = (delegate* unmanaged< nint, byte >)stopTextInput;
		SDL_GetWindowProperties = (delegate* unmanaged< nint, uint >)getWindowProperties;
		SDL_GetPointerProperty = (delegate* unmanaged< uint, byte*, nint, nint >)getPointerProperty;
		SDL_GetNumberProperty = (delegate* unmanaged< uint, byte*, long, long >)getNumberProperty;
		SDL_CreateProperties = (delegate* unmanaged< uint >)createProperties;
		SDL_DestroyProperties = (delegate* unmanaged< uint, void >)destroyProperties;
		SDL_SetPointerProperty = (delegate* unmanaged< uint, byte*, nint, byte >)setPointerProperty;
		SDL_SetBooleanProperty = (delegate* unmanaged< uint, byte*, byte, byte >)setBooleanProperty;
		SDL_CreateWindowWithProperties = (delegate* unmanaged< uint, nint >)createWindowWithProperties;

		SDL_PollEvent = (delegate* unmanaged< SdlRawEvent*, byte >)pollEvent;
		SDL_WaitEventTimeout = (delegate* unmanaged< SdlRawEvent*, int, byte >)waitEventTimeout;

		SDL_GetDisplays = (delegate* unmanaged< int*, uint* >)getDisplays;
		SDL_GetPrimaryDisplay = (delegate* unmanaged< uint >)getPrimaryDisplay;
		SDL_GetDisplayName = (delegate* unmanaged< uint, byte* >)getDisplayName;
		SDL_GetDisplayBounds = (delegate* unmanaged< uint, SdlRect*, byte >)getDisplayBounds;
		SDL_GetDisplayContentScale = (delegate* unmanaged< uint, float >)getDisplayContentScale;
		SDL_GetCurrentDisplayMode = (delegate* unmanaged< uint, SdlDisplayMode* >)getCurrentDisplayMode;

		SDL_SetClipboardText = (delegate* unmanaged< byte*, byte >)setClipboardText;
		SDL_GetClipboardText = (delegate* unmanaged< byte* >)getClipboardText;
		SDL_ShowCursor = (delegate* unmanaged< byte >)showCursor;
		SDL_HideCursor = (delegate* unmanaged< byte >)hideCursor;
		SDL_CreateSystemCursor = (delegate* unmanaged< int, nint >)createSystemCursor;
		SDL_SetCursor = (delegate* unmanaged< nint, byte >)setCursor;
		SDL_DestroyCursor = (delegate* unmanaged< nint, void >)destroyCursor;

		SDL_Vulkan_LoadLibrary = (delegate* unmanaged< byte*, byte >)vulkanLoadLibrary;
		SDL_Vulkan_UnloadLibrary = (delegate* unmanaged< void >)vulkanUnloadLibrary;
		SDL_Vulkan_GetInstanceExtensions = (delegate* unmanaged< uint*, byte** >)vulkanGetInstanceExtensions;
		SDL_Vulkan_CreateSurface = (delegate* unmanaged< nint, nint, void*, ulong*, byte >)vulkanCreateSurface;
		return true;
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
