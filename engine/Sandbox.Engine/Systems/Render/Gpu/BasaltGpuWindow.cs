using Basalt.Gpu;
using NativeEngine;

namespace Sandbox;

/// <summary>
/// An SDL window presenting on the process's Basalt.Gpu device (<c>-basaltgpu</c>): a swap chain on the window, and a
/// render target the window's frame is drawn into before it's copied into the image presented. The frame is drawn with
/// <see cref="Graphics"/> in a block of its own on the device, as a texture painter's is, so nothing of it runs on
/// Source 2's device.
/// </summary>
internal sealed class BasaltGpuWindow : IDisposable
{
	readonly BasaltGpuRenderBackend backend;
	readonly SwapChain swapChain;
	Texture target;

	/// <summary>The size frames are drawn at, in pixels.</summary>
	public Vector2 Size { get; private set; }

	/// <summary>The texture the last frame was drawn into: what was presented.</summary>
	public Texture Target => target;

	public BasaltGpuWindow( BasaltGpuRenderBackend backend, IntPtr window, bool vsync )
	{
		this.backend = backend;

		// The engine's SDL window, wrapped by Basalt.Sdl (the same SDL3); the engine keeps owning it. Its surface is made from
		// the window system's handles: the splash's window isn't made for Vulkan, which SDL's own surface call requires
		sdl = Basalt.Sdl.SdlWindow.Wrap( window );
		var (width, height) = sdl.PixelSize;
		swapChain = backend.CreateSwapChain( SurfaceOf( sdl.NativeWindow ), Math.Max( width, 1 ), Math.Max( height, 1 ), vsync );
		Size = new Vector2( width, height );

		if ( backend.Device.Capabilities.Timestamps )
		{
			timestamps = backend.Device.CreateTimestamps( 2 * TimedFrames );
			timestampMs = backend.Device.Capabilities.TimestampPeriod / 1e6;
		}
	}

	// A frame's GPU time: timestamps at the start of its first block and after the copy into the image, a pair for each of
	// the frames in flight, read back once the GPU is past them
	const int TimedFrames = 4;
	readonly TimestampQueries timestamps;
	readonly double timestampMs;
	ulong timedFrame;

	/// <summary>
	/// The GPU time of the latest frame read back (a few frames old), in milliseconds: from the start of its first block to
	/// the end of the copy into the presented image, every block and Basalt frame between included. Null without timestamps.
	/// </summary>
	public double? GpuFrameMs { get; private set; }

	/// <summary>Where the last camera that drew into the window was, for the title's stats (<see cref="BasaltGpuTitle"/>).</summary>
	public Vector3? CameraPosition { get; set; }

	/// <summary>Which frame <see cref="GpuFrameMs"/> was, counting from this window's first.</summary>
	public ulong GpuFrameNumber { get; private set; }

	int TimestampSlot => (int)(timedFrame % TimedFrames) * 2;

	readonly Basalt.Sdl.SdlWindow sdl;

	static SurfaceSource SurfaceOf( Basalt.Sdl.SdlNativeWindow native ) => native.Kind switch
	{
		Basalt.Sdl.SdlNativeWindow.Kinds.Win32 => SurfaceSource.Win32( native.Display, native.Window ),
		Basalt.Sdl.SdlNativeWindow.Kinds.X11 => SurfaceSource.Xlib( native.Display, native.Window ),
		Basalt.Sdl.SdlNativeWindow.Kinds.Wayland => SurfaceSource.Wayland( native.Display, native.Window ),
		_ => throw new PlatformNotSupportedException( $"Basalt.Gpu windows can't present on {native.Kind} windows" ),
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
		target = Texture.CreateRenderTarget().WithSize( (int)pixelSize.x, (int)pixelSize.y ).WithFormat( ImageFormat.RGBA8888 ).Create( "Basalt.Gpu window" );
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
		swapChain.SetPresentMode( vsync ? PresentMode.Fifo : PresentMode.Immediate );
	}

	/// <summary>
	/// The window whose frame is being drawn now, between <see cref="BeginFrame"/> and <see cref="EndFrame"/>: what a camera
	/// rendering into the game window draws into on Basalt.Gpu.
	/// </summary>
	public static BasaltGpuWindow Drawing { get; private set; }

	/// <summary>
	/// Start a frame drawn in several blocks (a game window: each camera, then the overlay): acquire the next image and clear
	/// the target to <paramref name="background"/>. False when there's no image to draw into, minimised. Draw with
	/// <see cref="Draw(BasaltGpuView, Action, bool)"/> and blocks of the caller's own into <see cref="Target"/>, then <see cref="EndFrame"/>.
	/// </summary>
	public bool BeginFrame( Color background )
	{
		if ( target is null ) return false;
		backend.BeforeAcquire( swapChain );
		if ( !swapChain.Acquire( out _ ) ) return false;

		Drawing = this;
		using var scope = Graphics.Scope.CreateOn( backend );
		Graphics.RenderTarget = RenderTarget.From( target );
		Graphics.Clear( background, true, false, false );
		if ( timestamps is not null ) backend.WriteTimestamp( timestamps, TimestampSlot, PipelineStage.TopOfPipe, reset: 2 );
		return true;
	}

	/// <summary>
	/// A block drawing into the window's target through <paramref name="view"/> (a camera's frame, or the overlay's), with its
	/// targets bound. <paramref name="present"/> copies the target into the acquired image last.
	/// </summary>
	public void Draw( BasaltGpuView view, Action draw, bool present = false )
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

	BasaltGpuView overlay;

	/// <summary>
	/// Finish the frame <see cref="BeginFrame"/> started: <paramref name="draw"/> (the overlay) last, over the whole window
	/// and writing gamma-encoded output as UI does, in the block that copies the target into the acquired image. Present it
	/// with <see cref="PresentFrame"/>.
	/// </summary>
	public void EndFrame( Action draw )
	{
		try
		{
			overlay ??= new BasaltGpuView( backend );
			overlay.Target = RenderTarget.From( target );
			overlay.Viewport = new Rect( 0, 0, Size.x, Size.y );
			Draw( overlay, () =>
			{
				Graphics.Attributes.Set( "UIGammaOutput", true );
				draw();
			}, present: true );

			// The frame as it's presented, for screenshots asked for during it
			ScreenshotService.ProcessFrame( backend, target );
		}
		finally
		{
			Drawing = null;
		}
	}

	// Whether a lost device's recent submissions were logged: once per process
	static bool deviceLostReported;

	/// <summary>
	/// Present the image <see cref="EndFrame"/> drew. False when no frame was drawn since the last.
	/// </summary>
	public bool PresentFrame()
	{
		if ( !swapChain.Current.IsValid ) return false;
		try
		{
			swapChain.Present();
		}
		catch ( GpuException e ) when ( e.Message.Contains( "DeviceLost" ) )
		{
			// Once: what was in flight, to find the submission the GPU stopped on
			if ( !deviceLostReported )
			{
				deviceLostReported = true;
				Log.Error( $"Basalt.Gpu device lost. Recent submissions: {backend.Device.DescribeRecentSubmissions()}" );
			}

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
	/// Draw a window's frame into <paramref name="target"/> on <paramref name="device"/>, as a UI-only camera draws it on
	/// Source 2: the background cleared (when <paramref name="clear"/>), then <paramref name="draw"/>, with the UI's
	/// shaders writing gamma-encoded output (<c>ui_gamma_blend</c> on an 8-bit target, bound through a linear view).
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
			if ( present is not null ) ((BasaltGpuRenderBackend)device).CopyToSwapChain( target, present );
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
		sdl.Dispose();
		timestamps?.Dispose();
		ReleaseTarget();
		overlay?.Dispose();
		overlay = null;
	}
}
