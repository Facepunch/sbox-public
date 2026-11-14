using Borealis.Sdl.Native;
using static Borealis.Sdl.Native.SdlNative;

namespace Borealis.Sdl;

/// <summary>
/// How to make an <see cref="SdlWindow"/>.
/// </summary>
public sealed class SdlWindowOptions
{
	/// <summary>The title bar's text.</summary>
	public string Title { get; init; } = "Borealis";

	/// <summary>The size of the drawable area, in pixels, whatever the display's scale.</summary>
	public int Width { get; init; } = 1280;

	/// <summary>The size of the drawable area, in pixels, whatever the display's scale.</summary>
	public int Height { get; init; } = 720;

	/// <summary>Whether the user can resize it.</summary>
	public bool Resizable { get; init; } = true;

	/// <summary>Start hidden (for tests and offscreen work); <see cref="SdlWindow.Show"/> shows it.</summary>
	public bool Hidden { get; init; }

	/// <summary>Start fullscreen, borderless on the desktop's mode.</summary>
	public bool Fullscreen { get; init; }

	/// <summary>No border or title bar.</summary>
	public bool Borderless { get; init; }

	/// <summary>Whether Vulkan surfaces will be made for it. On by default.</summary>
	public bool Vulkan { get; init; } = true;
}

/// <summary>
/// The window system's own handles for a window, for code that makes surfaces itself instead of through
/// <see cref="SdlWindow.CreateVulkanSurface"/>.
/// </summary>
public readonly record struct SdlNativeWindow( SdlNativeWindow.Kinds Kind, nint Display, nint Window )
{
	/// <summary>Which window system the handles belong to.</summary>
	public enum Kinds
	{
		/// <summary>None this platform reports.</summary>
		Unknown,

		/// <summary><see cref="Display"/> is the <c>HINSTANCE</c>, <see cref="Window"/> the <c>HWND</c>.</summary>
		Win32,

		/// <summary><see cref="Display"/> is the Xlib <c>Display*</c>, <see cref="Window"/> the <c>Window</c> id.</summary>
		X11,

		/// <summary><see cref="Display"/> is the <c>wl_display*</c>, <see cref="Window"/> the <c>wl_surface*</c>.</summary>
		Wayland,

		/// <summary><see cref="Window"/> is the <c>NSWindow*</c>.</summary>
		Cocoa,
	}
}

/// <summary>
/// A window through SDL3: its size in pixels and window units, fullscreen, minimised state, and Vulkan surfaces for
/// Borealis.Gpu (<c>SurfaceSource.Custom( window.CreateVulkanSurface )</c>). Its events come from <see cref="Sdl.PollEvent"/>,
/// which also keeps <see cref="CloseRequested"/> and <see cref="Resized"/> current. Belongs to the thread that made it.
/// </summary>
public sealed unsafe class SdlWindow : IDisposable
{
	const ulong FlagFullscreen = 0x1;
	const ulong FlagHidden = 0x8;
	const ulong FlagBorderless = 0x10;
	const ulong FlagResizable = 0x20;
	const ulong FlagMinimized = 0x40;
	const ulong FlagMaximized = 0x80;
	const ulong FlagInputFocus = 0x200;
	const ulong FlagHighPixelDensity = 0x2000;
	const ulong FlagVulkan = 0x10000000;

	nint handle;
	readonly bool owned;

	/// <summary>The <c>SDL_Window*</c>.</summary>
	public nint Handle => handle;

	/// <summary>SDL's id for it, which its events carry in <see cref="SdlEvent.WindowId"/>.</summary>
	public uint Id { get; }

	/// <summary>Set when the user (or <see cref="RequestClose"/>, or a quit event) asked for it to close.</summary>
	public bool CloseRequested { get; private set; }

	/// <summary>
	/// Set when its size in pixels may have changed (resized, restored, maximised, fullscreen, moved display). Clear it
	/// once the swap chain matches <see cref="PixelSize"/>.
	/// </summary>
	public bool Resized { get; set; }

	/// <summary>Open a window. Starts SDL's video subsystem if nothing has yet.</summary>
	public SdlWindow( SdlWindowOptions options )
	{
		options ??= new SdlWindowOptions();
		Sdl.Init();

		try
		{
			var flags = FlagHidden | FlagHighPixelDensity;
			if ( options.Resizable ) flags |= FlagResizable;
			if ( options.Borderless ) flags |= FlagBorderless;
			if ( options.Vulkan ) flags |= FlagVulkan;

			fixed ( byte* title = Utf8( options.Title ?? "" ) )
				handle = SDL_CreateWindow( title, Math.Max( options.Width, 1 ), Math.Max( options.Height, 1 ), flags );

			if ( handle == 0 ) throw new SdlException( $"SDL_CreateWindow failed: {LastError}" );
			owned = true;
			Id = SDL_GetWindowID( handle );

			// Sizes are asked for in pixels: where a window unit is more than one pixel (Retina, scaled Wayland), shrink it
			SetPixelSize( options.Width, options.Height );
			if ( options.Fullscreen ) Fullscreen = true;
			if ( !options.Hidden ) Show();
		}
		catch
		{
			if ( handle != 0 ) SDL_DestroyWindow( handle );
			handle = 0;
			Sdl.Quit();
			throw;
		}

		Sdl.Register( this );
	}

	SdlWindow( nint window )
	{
		handle = window;
		Id = SDL_GetWindowID( window );
	}

	/// <summary>
	/// A window someone else made and owns (the engine's), for its sizes and Vulkan surfaces. Disposing it leaves the
	/// window alone; its owner pumps its events.
	/// </summary>
	public static SdlWindow Wrap( nint sdlWindow )
	{
		if ( sdlWindow == 0 ) throw new ArgumentNullException( nameof( sdlWindow ) );
		SdlNative.Load();
		return new SdlWindow( sdlWindow );
	}

	void Require()
	{
		ObjectDisposedException.ThrowIf( handle == 0, this );
	}

	internal void OnEvent( in SdlEvent e )
	{
		switch ( e.Kind )
		{
			case SdlEventKind.WindowCloseRequested:
				CloseRequested = true;
				break;

			case SdlEventKind.WindowPixelSizeChanged or SdlEventKind.WindowRestored or SdlEventKind.WindowMaximized
				or SdlEventKind.WindowEnterFullscreen or SdlEventKind.WindowLeaveFullscreen or SdlEventKind.WindowDisplayChanged:
				Resized = true;
				break;
		}
	}

	/// <summary>Ask for it to close, as the close button does: sets <see cref="CloseRequested"/>.</summary>
	public void RequestClose() => CloseRequested = true;

	ulong Flags
	{
		get
		{
			Require();
			return SDL_GetWindowFlags( handle );
		}
	}

	/// <summary>The title bar's text.</summary>
	public string Title
	{
		get
		{
			Require();
			return SdlNative.String( SDL_GetWindowTitle( handle ) );
		}

		set
		{
			Require();
			fixed ( byte* title = Utf8( value ?? "" ) ) SDL_SetWindowTitle( handle, title );
		}
	}

	/// <summary>The drawable area's size in window units: what mouse positions are measured in.</summary>
	public (int Width, int Height) Size
	{
		get
		{
			Require();
			int w, h;
			SDL_GetWindowSize( handle, &w, &h );
			return (w, h);
		}
	}

	/// <summary>The drawable area's size in pixels: what a swap chain on it should be.</summary>
	public (int Width, int Height) PixelSize
	{
		get
		{
			Require();
			int w, h;
			SDL_GetWindowSizeInPixels( handle, &w, &h );
			return (w, h);
		}
	}

	/// <summary>Pixels per window unit: 1 on Windows and X11, 2 on Retina and on Wayland outputs scaled by 200%.</summary>
	public float PixelDensity
	{
		get
		{
			Require();
			var density = SDL_GetWindowPixelDensity( handle );
			return density > 0 ? density : 1;
		}
	}

	/// <summary>How much bigger than normal its content should be drawn, by the display's settings (1.5 for 150%).</summary>
	public float DisplayScale
	{
		get
		{
			Require();
			var scale = SDL_GetWindowDisplayScale( handle );
			return scale > 0 ? scale : 1;
		}
	}

	/// <summary>
	/// Resize the drawable area to this many pixels, as a user dragging the border would. Waits for the window system to
	/// apply it; a <see cref="SdlEventKind.WindowPixelSizeChanged"/> event follows.
	/// </summary>
	public void SetPixelSize( int width, int height )
	{
		Require();
		var density = PixelDensity;
		var w = Math.Max( (int)MathF.Round( width / density ), 1 );
		var h = Math.Max( (int)MathF.Round( height / density ), 1 );
		var (currentW, currentH) = Size;
		if ( w == currentW && h == currentH ) return;

		Check( SDL_SetWindowSize( handle, w, h ), "SDL_SetWindowSize" );
		SDL_SyncWindow( handle );
	}

	/// <summary>
	/// Fullscreen, borderless at the desktop's resolution, or windowed. Setting it waits for the window system to apply
	/// it; <see cref="Resized"/> is set once the change's events are polled.
	/// </summary>
	public bool Fullscreen
	{
		get => (Flags & FlagFullscreen) != 0;
		set
		{
			Require();
			Check( SDL_SetWindowFullscreen( handle, value ? (byte)1 : (byte)0 ), "SDL_SetWindowFullscreen" );
			SDL_SyncWindow( handle );
		}
	}

	/// <summary>Whether it's minimised: there's nothing to present to.</summary>
	public bool Minimized => (Flags & FlagMinimized) != 0;

	/// <summary>Whether it's maximised.</summary>
	public bool Maximized => (Flags & FlagMaximized) != 0;

	/// <summary>Whether it's shown.</summary>
	public bool Visible => (Flags & FlagHidden) == 0;

	/// <summary>Whether it has keyboard focus.</summary>
	public bool Focused => (Flags & FlagInputFocus) != 0;

	/// <summary>Whether it was made for Vulkan surfaces.</summary>
	public bool IsVulkan => (Flags & FlagVulkan) != 0;

	/// <summary>Show it.</summary>
	public void Show()
	{
		Require();
		Check( SDL_ShowWindow( handle ), "SDL_ShowWindow" );
	}

	/// <summary>Hide it.</summary>
	public void Hide()
	{
		Require();
		Check( SDL_HideWindow( handle ), "SDL_HideWindow" );
	}

	/// <summary>Minimise it.</summary>
	public void Minimize()
	{
		Require();
		Check( SDL_MinimizeWindow( handle ), "SDL_MinimizeWindow" );
		SDL_SyncWindow( handle );
	}

	/// <summary>Maximise it.</summary>
	public void Maximize()
	{
		Require();
		Check( SDL_MaximizeWindow( handle ), "SDL_MaximizeWindow" );
		SDL_SyncWindow( handle );
	}

	/// <summary>Restore it from minimised or maximised.</summary>
	public void Restore()
	{
		Require();
		Check( SDL_RestoreWindow( handle ), "SDL_RestoreWindow" );
		SDL_SyncWindow( handle );
	}

	/// <summary>The display it's mostly on, or null.</summary>
	public SdlDisplay Display
	{
		get
		{
			Require();
			var id = SDL_GetDisplayForWindow( handle );
			return id == 0 ? null : SdlDisplay.From( id );
		}
	}

	/// <summary>
	/// Relative mouse mode: the cursor is hidden and held in the window, and motion events report movement only, for
	/// mouse look.
	/// </summary>
	public bool RelativeMouse
	{
		get
		{
			Require();
			return SDL_GetWindowRelativeMouseMode( handle ) != 0;
		}

		set
		{
			Require();
			Check( SDL_SetWindowRelativeMouseMode( handle, value ? (byte)1 : (byte)0 ), "SDL_SetWindowRelativeMouseMode" );
		}
	}

	/// <summary>Start <see cref="SdlEventKind.TextInput"/> events for this window (and the on-screen keyboard where there is one).</summary>
	public void StartTextInput()
	{
		Require();
		Check( SDL_StartTextInput( handle ), "SDL_StartTextInput" );
	}

	/// <summary>Stop text input events.</summary>
	public void StopTextInput()
	{
		Require();
		Check( SDL_StopTextInput( handle ), "SDL_StopTextInput" );
	}

	/// <summary>The window system's own handles for it.</summary>
	public SdlNativeWindow NativeWindow
	{
		get
		{
			Require();
			var properties = SDL_GetWindowProperties( handle );
			if ( properties == 0 ) return default;

			nint Pointer( string name )
			{
				fixed ( byte* n = Utf8( name ) ) return SDL_GetPointerProperty( properties, n, 0 );
			}

			long Number( string name )
			{
				fixed ( byte* n = Utf8( name ) ) return SDL_GetNumberProperty( properties, n, 0 );
			}

			if ( Pointer( "SDL.window.win32.hwnd" ) is var hwnd and not 0 )
				return new( SdlNativeWindow.Kinds.Win32, Pointer( "SDL.window.win32.instance" ), hwnd );

			if ( Pointer( "SDL.window.wayland.surface" ) is var surface and not 0 )
				return new( SdlNativeWindow.Kinds.Wayland, Pointer( "SDL.window.wayland.display" ), surface );

			if ( Number( "SDL.window.x11.window" ) is var xwindow and not 0 )
				return new( SdlNativeWindow.Kinds.X11, Pointer( "SDL.window.x11.display" ), (nint)xwindow );

			if ( Pointer( "SDL.window.cocoa.window" ) is var nswindow and not 0 )
				return new( SdlNativeWindow.Kinds.Cocoa, 0, nswindow );

			return default;
		}
	}

	/// <summary>
	/// Make a Vulkan surface (<c>VkSurfaceKHR</c>) for it on a <c>VkInstance</c> that has <see cref="Sdl.VulkanInstanceExtensions"/>
	/// enabled. The caller owns the surface and destroys it with <c>vkDestroySurfaceKHR</c>; Borealis.Gpu's swap chain does.
	/// Fits <c>SurfaceSource.Custom</c> as it is.
	/// </summary>
	public ulong CreateVulkanSurface( nint vkInstance )
	{
		Require();
		if ( !IsVulkan ) throw new SdlException( "This window wasn't made for Vulkan (SdlWindowOptions.Vulkan)" );

		ulong surface;
		Check( SDL_Vulkan_CreateSurface( handle, vkInstance, null, &surface ), "SDL_Vulkan_CreateSurface" );
		return surface;
	}

	/// <summary>Close the window (unless it was <see cref="Wrap"/>ped), after any swap chain on it is gone.</summary>
	public void Dispose()
	{
		if ( handle == 0 ) return;

		if ( owned )
		{
			Sdl.Unregister( this );
			SDL_DestroyWindow( handle );
			Sdl.Quit();
		}

		handle = 0;
	}
}
