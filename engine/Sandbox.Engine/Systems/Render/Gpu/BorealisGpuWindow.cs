using Borealis.Gpu;
using NativeEngine;

namespace Sandbox;

/// <summary>
/// An SDL window presenting on the process's Borealis.Gpu device (<c>-borealisgpu</c>): a swap chain on the window, and a
/// render target the window's frame is drawn into before it's copied into the image presented. The frame is drawn with
/// <see cref="Graphics"/> in a block of its own on the device, as a texture painter's is, so nothing of it runs on
/// Source 2's device.
/// </summary>
internal sealed class BorealisGpuWindow : IDisposable
{
	readonly BorealisGpuRenderBackend backend;
	readonly SwapChain swapChain;
	Texture target;

	/// <summary>The size frames are drawn at, in pixels.</summary>
	public Vector2 Size { get; private set; }

	/// <summary>The texture the last frame was drawn into: what was presented.</summary>
	public Texture Target => target;

	public BorealisGpuWindow( BorealisGpuRenderBackend backend, IntPtr window, bool vsync )
	{
		this.backend = backend;

		// The engine's SDL window, wrapped by Borealis.Sdl (the same SDL3); the engine keeps owning it. Its surface is made from
		// the window system's handles: the splash's window isn't made for Vulkan, which SDL's own surface call requires
		sdl = Borealis.Sdl.SdlWindow.Wrap( window );
		var (width, height) = sdl.PixelSize;
		swapChain = backend.CreateSwapChain( SurfaceOf( sdl.NativeWindow ), Math.Max( width, 1 ), Math.Max( height, 1 ), vsync );
		Size = new Vector2( width, height );
		timestamps = CreateTimestamps( backend, out timestampMs );
	}

	BorealisGpuWindow( BorealisGpuRenderBackend backend, SurfaceSource surface, IntPtr nativeWindow, Vector2 pixelSize, bool vsync )
	{
		this.backend = backend;
		NativeWindow = nativeWindow;
		swapChain = backend.CreateSwapChain( surface, Math.Max( (int)pixelSize.x, 1 ), Math.Max( (int)pixelSize.y, 1 ), vsync );
		Size = pixelSize;
		timestamps = CreateTimestamps( backend, out timestampMs );
	}

	/// <summary>
	/// A window that isn't SDL's, by its window system handle: an editor widget's native window, which its owner keeps
	/// owning. On Windows a Win32 <c>HWND</c>; on Linux an X11 window id, as Qt's xcb platform gives it (under a Wayland
	/// session too, through XWayland: Qt has no Wayland platform here). Null without a window.
	/// </summary>
	public static BorealisGpuWindow FromNativeWindow( BorealisGpuRenderBackend backend, IntPtr window, Vector2 pixelSize, bool vsync )
	{
		if ( window == IntPtr.Zero ) return null;
		return new BorealisGpuWindow( backend, NativeSurfaceOf( window ), window, pixelSize, vsync );
	}

	static SurfaceSource NativeSurfaceOf( IntPtr window )
	{
		if ( OperatingSystem.IsWindows() ) return SurfaceSource.Win32( GetWindowLongPtrW( window, GWLP_HINSTANCE ), window );
		if ( OperatingSystem.IsLinux() ) return SurfaceSource.Xlib( X11Display, window );
		throw new PlatformNotSupportedException( "Borealis.Gpu can't present to native windows on this platform" );
	}

	/// <summary>
	/// <see cref="FromNativeWindow"/> for a widget whose native window changed, its old one disposed first: null, with a
	/// warning, when the window can't be presented to, rather than failing the frame that moves it.
	/// </summary>
	public static BorealisGpuWindow MoveToNativeWindow( BorealisGpuRenderBackend backend, IntPtr window, Vector2 pixelSize )
	{
		try
		{
			return FromNativeWindow( backend, window, pixelSize, vsync: false );
		}
		catch ( GpuException e ) when ( !e.IsDeviceLost )
		{
			Log.Warning( $"Borealis.Gpu: no swap chain on a widget's new native window: {e.Message}" );
			return null;
		}
	}

	/// <summary>The window system handle it presents to, made by <see cref="FromNativeWindow"/>; zero on an SDL window.</summary>
	public IntPtr NativeWindow { get; }

	const int GWLP_HINSTANCE = -6;

	[System.Runtime.InteropServices.DllImport( "user32.dll" )]
	static extern IntPtr GetWindowLongPtrW( IntPtr window, int index );

	static IntPtr x11Display;

	/// <summary>
	/// A connection of our own to the X server Qt's windows are on (<c>DISPLAY</c>, as Qt opens it): window ids belong to
	/// the server, so a surface made through it presents to Qt's window. Opened once and kept for the process.
	/// </summary>
	static IntPtr X11Display
	{
		get
		{
			if ( x11Display != IntPtr.Zero ) return x11Display;
			x11Display = XOpenDisplay( IntPtr.Zero );
			if ( x11Display == IntPtr.Zero ) throw new GpuException( $"Couldn't open the X display '{Environment.GetEnvironmentVariable( "DISPLAY" )}'" );
			return x11Display;
		}
	}

	[System.Runtime.InteropServices.DllImport( "libX11.so.6" )]
	static extern IntPtr XOpenDisplay( IntPtr name );

	static TimestampQueries CreateTimestamps( BorealisGpuRenderBackend backend, out double timestampMs )
	{
		timestampMs = 0;
		if ( !backend.Device.Capabilities.Timestamps ) return null;
		timestampMs = backend.Device.Capabilities.TimestampPeriod / 1e6;
		return backend.Device.CreateTimestamps( 2 * TimedFrames );
	}

	// A frame's GPU time: timestamps at the start of its first block and after the copy into the image, a pair for each of
	// the frames in flight, read back once the GPU is past them
	const int TimedFrames = 4;
	readonly TimestampQueries timestamps;
	readonly double timestampMs;
	ulong timedFrame;

	/// <summary>
	/// The GPU time of the latest frame read back (a few frames old), in milliseconds: from the start of its first block to
	/// the end of the copy into the presented image, every block and Borealis frame between included. Null without timestamps.
	/// </summary>
	public double? GpuFrameMs { get; private set; }

	/// <summary>Where the last camera that drew into the window was, for the title's stats (<see cref="BorealisGpuTitle"/>).</summary>
	public Vector3? CameraPosition { get; set; }

	/// <summary>Which frame <see cref="GpuFrameMs"/> was, counting from this window's first.</summary>
	public ulong GpuFrameNumber { get; private set; }

	int TimestampSlot => (int)(timedFrame % TimedFrames) * 2;

	readonly Borealis.Sdl.SdlWindow sdl;

	static SurfaceSource SurfaceOf( Borealis.Sdl.SdlNativeWindow native ) => native.Kind switch
	{
		Borealis.Sdl.SdlNativeWindow.Kinds.Win32 => SurfaceSource.Win32( native.Display, native.Window ),
		Borealis.Sdl.SdlNativeWindow.Kinds.X11 => SurfaceSource.Xlib( native.Display, native.Window ),
		Borealis.Sdl.SdlNativeWindow.Kinds.Wayland => SurfaceSource.Wayland( native.Display, native.Window ),
		_ => throw new PlatformNotSupportedException( $"Borealis.Gpu windows can't present on {native.Kind} windows" ),
	};

	/// <summary>
	/// Match the window's size in pixels. False while it's zero-sized, with nothing to draw.
	/// </summary>
	public bool Update( Vector2 pixelSize )
	{
		if ( pixelSize.x < 1 || pixelSize.y < 1 ) return false;
		if ( pixelSize == Size && target is not null ) return true;

		if ( pixelSize != Size ) swapChain.Resize( (int)pixelSize.x, (int)pixelSize.y );
		Size = pixelSize;
		target?.Dispose();
		target = Texture.CreateRenderTarget().WithSize( (int)pixelSize.x, (int)pixelSize.y ).WithFormat( ImageFormat.RGBA8888 ).Create( "Borealis.Gpu window" );
		return true;
	}

	/// <summary>
	/// Draw a frame with <paramref name="draw"/> (see <see cref="Draw(RenderBackend, Texture, Color, bool, Action)"/>) and
	/// present it. False when there was no image to draw into, minimised.
	/// </summary>
	public bool Present( Color background, bool clear, Action draw )
	{
		if ( target is null ) return false;
		backend.BeforeAcquire( swapChain );
		if ( !swapChain.Acquire( out _ ) ) return false;

		Draw( backend, target, background, clear, draw, swapChain );
		swapChain.Present();
		return true;
	}

	/// <summary>
	/// Samples per pixel the cameras drawing into this window render their scenes with (the game's anti-aliasing setting).
	/// Their frames are resolved before post processing; the window's own target has one.
	/// </summary>
	public int Samples { get; set; } = 1;

	/// <summary>Whether the swap chain presents with vsync.</summary>
	public bool VSync => swapChain.PresentMode == PresentMode.Fifo;

	/// <summary>Present with vsync or as soon as possible from now on. Recreates the swap chain's images when it changes.</summary>
	public void SetVSync( bool vsync )
	{
		if ( VSync == vsync ) return;
		swapChain.SetPresentMode( vsync ? PresentMode.Fifo : BorealisGpuRenderBackend.NoVSyncMode );
	}

	/// <summary>
	/// The window whose frame is being drawn now, between <see cref="BeginFrame"/> and <see cref="EndFrame"/>: what a camera
	/// rendering into the game window draws into on Borealis.Gpu.
	/// </summary>
	public static BorealisGpuWindow Drawing { get; private set; }

	/// <summary>
	/// Start a frame drawn in several blocks (a game window: each camera, then the overlay): acquire the next image and clear
	/// the target to <paramref name="background"/>. False when there's no image to draw into, minimised. Draw with
	/// <see cref="Draw(BorealisGpuView, Action, bool)"/> and blocks of the caller's own into <see cref="Target"/>, then <see cref="EndFrame"/>.
	/// </summary>
	public bool BeginFrame( Color background )
	{
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		if ( target is null ) return false;
		backend.BeforeAcquire( swapChain );
		if ( !swapChain.Acquire( out _ ) ) return false;

		Drawing = this;
		using var scope = Graphics.Scope.CreateOn( backend );
		Graphics.RenderTarget = TargetOfWindow();
		Graphics.Clear( background, true, false, false );
		if ( timestamps is not null ) backend.WriteTimestamp( timestamps, TimestampSlot, PipelineStage.TopOfPipe, reset: 2 );
		return true;
	}

	/// <summary>
	/// A block drawing into the window's target through <paramref name="view"/> (a camera's frame, or the overlay's), with its
	/// targets bound. <paramref name="present"/> copies the target into the acquired image last.
	/// </summary>
	public void Draw( BorealisGpuView view, Action draw, bool present = false )
	{
		using var scope = Graphics.Scope.CreateOnView( backend, view );
		view.Bind();
		try
		{
			draw();
		}
		finally
		{
			// The acquired image has to be presented whatever the drawing did, or the next frame can't acquire
			if ( present )
			{
				backend.CopyToSwapChain( target, swapChain );
				if ( timestamps is not null ) backend.WriteTimestamp( timestamps, TimestampSlot + 1, PipelineStage.AllCommands );
			}
		}
	}

	BorealisGpuView overlay;

	// The window's target as a render target, made again only when the target is; and the overlay EndFrame draws, which the
	// one delegate handed to Draw reads, so a frame allocates neither
	(Texture Texture, RenderTarget Target) windowTarget;
	Action overlayDraw, drawOverlay;

	RenderTarget TargetOfWindow()
	{
		if ( !ReferenceEquals( windowTarget.Texture, target ) ) windowTarget = (target, RenderTarget.From( target ));
		return windowTarget.Target;
	}

	void DrawOverlay()
	{
		Graphics.Attributes.Set( "UIGammaOutput", true );
		overlayDraw();
	}

	/// <summary>
	/// Finish the frame <see cref="BeginFrame"/> started: <paramref name="draw"/> (the overlay) last, over the whole window
	/// and writing gamma-encoded output as UI does, in the block that copies the target into the acquired image. Present it
	/// with <see cref="PresentFrame"/>. With <paramref name="screenshots"/> the screenshots asked for are taken from it.
	/// </summary>
	public void EndFrame( Action draw, bool screenshots = true )
	{
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		try
		{
			overlay ??= new BorealisGpuView( backend );
			overlay.Target = TargetOfWindow();
			overlay.Viewport = new Rect( 0, 0, Size.x, Size.y );
			overlayDraw = draw;
			try
			{
				Draw( overlay, drawOverlay ??= DrawOverlay, present: true );
			}
			finally
			{
				overlayDraw = null;
			}

			// The frame as it's presented, for screenshots asked for during it
			if ( screenshots ) ScreenshotService.ProcessFrame( backend, target );
		}
		finally
		{
			Drawing = null;
		}
	}

	/// <summary>
	/// Present the image <see cref="EndFrame"/> drew. False when no frame was drawn since the last.
	/// </summary>
	public bool PresentFrame()
	{
		using var rendererAllocations = Borealis.RendererAllocations.Measure();
		if ( !swapChain.Current.IsValid ) return false;
		try
		{
			swapChain.Present();
		}
		catch ( GpuException e ) when ( e.IsDeviceLost )
		{
			// Said once, with what the GPU was doing when it was lost, and the process exits as native's does
			BorealisGpuDeviceLost.Check( backend.Device );
			throw;
		}

		// The oldest pair in flight, the frame TimedFrames - 1 back
		if ( timestamps is not null )
		{
			Span<ulong> ticks = stackalloc ulong[2];
			var oldest = timedFrame - (TimedFrames - 1);
			if ( timedFrame >= TimedFrames - 1 && timestamps.TryRead( (int)(oldest % TimedFrames) * 2, ticks ) && ticks[1] >= ticks[0] )
			{
				GpuFrameMs = (ticks[1] - ticks[0]) * timestampMs;
				GpuFrameNumber = oldest;
			}
			timedFrame++;
		}

		return true;
	}

	/// <summary>
	/// Draw a window's frame into <paramref name="target"/> on <paramref name="device"/>, as a UI-only camera draws it: the
	/// background cleared (when <paramref name="clear"/>), then <paramref name="draw"/>, with the UI's shaders writing
	/// gamma-encoded output (blended in gamma space like a browser, on an 8-bit target bound through a linear view).
	/// </summary>
	public static void Draw( RenderBackend device, Texture target, Color background, bool clear, Action draw ) => Draw( device, target, background, clear, draw, null );

	static void Draw( RenderBackend device, Texture target, Color background, bool clear, Action draw, SwapChain present )
	{
		using var scope = Graphics.Scope.CreateOn( device );
		Graphics.RenderTarget = RenderTarget.From( target );
		if ( clear ) Graphics.Clear( background, true, false, false );
		scope.Attributes.Set( "UIGammaOutput", true );
		try
		{
			draw();
		}
		finally
		{
			// The acquired image has to be presented whatever the drawing did, or the next frame can't acquire
			if ( present is not null ) ((BorealisGpuRenderBackend)device).CopyToSwapChain( target, present );
		}
	}

	/// <summary>Let go of the frame's target now: the window is closing, and its swap chain goes later.</summary>
	public void ReleaseTarget()
	{
		target?.Dispose();
		target = null;
	}

	public void Dispose()
	{
		backend.Forget( swapChain );
		swapChain.Dispose();
		sdl?.Dispose();
		timestamps?.Dispose();
		ReleaseTarget();
		overlay?.Dispose();
		overlay = null;
	}
}
