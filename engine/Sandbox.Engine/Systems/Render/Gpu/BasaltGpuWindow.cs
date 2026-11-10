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
		draw();

		if ( present is not null ) ((BasaltGpuRenderBackend)device).CopyToSwapChain( target, present );
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
	}
}
