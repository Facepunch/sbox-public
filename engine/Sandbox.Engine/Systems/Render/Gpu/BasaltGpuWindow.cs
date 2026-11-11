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

		var properties = Sdl.GetWindowProperties( window );
		var hwnd = Sdl.GetPointerProperty( properties, "SDL.window.win32.hwnd", IntPtr.Zero );
		var instance = Sdl.GetPointerProperty( properties, "SDL.window.win32.instance", IntPtr.Zero );
		if ( hwnd == IntPtr.Zero ) throw new PlatformNotSupportedException( "Basalt.Gpu windows need a Win32 window for now" );

		Sdl.GetWindowSizeInPixels( window, out var width, out var height );
		swapChain = backend.CreateSwapChain( SurfaceSource.Win32( instance, hwnd ), Math.Max( width, 1 ), Math.Max( height, 1 ), vsync );
		Size = new Vector2( width, height );
	}

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
			if ( present ) backend.CopyToSwapChain( target, swapChain );
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

	/// <summary>
	/// Present the image <see cref="EndFrame"/> drew. False when no frame was drawn since the last.
	/// </summary>
	public bool PresentFrame()
	{
		if ( !swapChain.Current.IsValid ) return false;
		swapChain.Present();
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
		ReleaseTarget();
		overlay?.Dispose();
		overlay = null;
	}
}
