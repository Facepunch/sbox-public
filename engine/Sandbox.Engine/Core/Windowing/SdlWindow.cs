using NativeEngine;

namespace Sandbox.Engine;

/// <summary>
/// Owns an SDL window and its swap chain. The caller supplies window policy, rendering and disposal timing.
/// </summary>
[SkipHotload]
internal sealed class SdlWindow : IDisposable
{
	internal IntPtr Handle { get; private set; }
	internal SwapChainHandle_t SwapChain { get; private set; }
	internal Sdl.WindowFlags Flags => Sdl.GetWindowFlags( Handle );
	internal bool IsFocused => (Flags & Sdl.WindowFlags.InputFocus) != 0;

	RenderDisplayModeUsage fullscreenUsage = RenderDisplayModeUsage.CooperativeFullscreen;
	Vector2 swapChainSize;

	/// <summary>
	/// Take ownership of an SDL window on the main thread. Dispose it before render-device teardown.
	/// </summary>
	internal SdlWindow( IntPtr handle )
	{
		ThreadSafe.AssertIsMainThread();
		if ( handle == IntPtr.Zero )
			throw new InvalidOperationException( $"Couldn't create the SDL window: {Sdl.GetError()}" );
		Handle = handle;
	}

	internal string Title
	{
		set => WarnIfFailed( Sdl.SetWindowTitle( Handle, value ?? "" ) );
	}

	internal Vector2 PixelSize
	{
		get
		{
			Check( Sdl.GetWindowSizeInPixels( Handle, out var width, out var height ) );
			return new Vector2( width, height );
		}
	}

	/// <summary>The actual render size, retaining the last usable size while a resize is pending.</summary>
	internal Vector2 SwapChainSize
	{
		get
		{
			var mode = g_pRenderDevice.GetSwapChainInfo( SwapChain ).m_DisplayMode;
			if ( mode.m_nWidth > 0 && mode.m_nHeight > 0 )
				swapChainSize = new Vector2( mode.m_nWidth, mode.m_nHeight );
			return swapChainSize;
		}
	}

	RenderDisplayModeUsage ModeUsage
	{
		get
		{
			var flags = Flags;
			return (flags & Sdl.WindowFlags.Fullscreen) != 0 ? fullscreenUsage
				: (flags & Sdl.WindowFlags.Borderless) != 0 ? RenderDisplayModeUsage.BorderlessWindow : RenderDisplayModeUsage.BorderedWindow;
		}
	}

	/// <summary>
	/// Create the owned swapchain on the main thread after window creation and before rendering to it.
	/// </summary>
	internal void CreateSwapChain( string name, RenderMultisampleType multisample, bool vsync, bool mainWindow = false )
	{
		ThreadSafe.AssertIsMainThread();
		if ( SwapChain != default ) throw new InvalidOperationException( "This window already has a swap chain" );
		var size = PixelSize;
		var mode = new RenderDeviceInfo_t
		{
			m_DisplayMode = new() { m_nWidth = (int)size.x, m_nHeight = (int)size.y, m_Format = ImageFormat.RGBA8888 },
			m_nModeUsage = ModeUsage,
			m_nMultisampleType = RenderDeviceManager.GetBestMultisampleType( multisample ),
			m_bWaitForVSync = (byte)(vsync ? 1 : 0),
			m_bIsMainWindow = (byte)(mainWindow ? 1 : 0)
		};
		// Creation must not overlap a present on the render thread.
		g_pRenderDevice.Flush();
		SwapChain = g_pRenderDevice.CreateSwapChain( Handle, mode, name );
		if ( SwapChain == default )
			throw new InvalidOperationException( $"Couldn't create the swap chain for {name}" );
		swapChainSize = size;
	}

	/// <summary>
	/// Match the swapchain to the window on the main thread at a safe frame boundary.
	/// Returns false for a zero-sized window and true after an update or when unchanged; native update failure throws.
	/// </summary>
	internal bool UpdateSwapChain( RenderMultisampleType? multisample = null, bool? vsync = null )
	{
		ThreadSafe.AssertIsMainThread();
		var size = PixelSize;
		if ( size.x < 1 || size.y < 1 ) return false;

		var current = g_pRenderDevice.GetSwapChainInfo( SwapChain );
		var mode = current;
		mode.m_DisplayMode.m_nWidth = (int)size.x;
		mode.m_DisplayMode.m_nHeight = (int)size.y;
		mode.m_nModeUsage = ModeUsage;
		if ( multisample is { } samples ) mode.m_nMultisampleType = RenderDeviceManager.GetBestMultisampleType( samples );
		if ( vsync is { } wait ) mode.m_bWaitForVSync = (byte)(wait ? 1 : 0);

		// A matching mode is only reusable while native still has its render targets.
		if ( g_pRenderDevice.CanRenderToSwapChain( SwapChain )
			&& current.m_DisplayMode.m_nWidth == mode.m_DisplayMode.m_nWidth
			&& current.m_DisplayMode.m_nHeight == mode.m_DisplayMode.m_nHeight
			&& current.m_nModeUsage == mode.m_nModeUsage
			&& current.m_nMultisampleType == mode.m_nMultisampleType
			&& current.m_bWaitForVSync == mode.m_bWaitForVSync ) return true;

		if ( !g_pRenderDevice.UpdateSwapChain( SwapChain, mode ) )
			throw new InvalidOperationException( "Couldn't update the SDL window's swap chain" );
		return true;
	}

	internal bool Present() => g_pRenderDevice.Present( SwapChain );
	internal bool Show() => WarnIfFailed( Sdl.ShowWindow( Handle ) );
	internal bool Sync() => WarnIfFailed( Sdl.SyncWindow( Handle ) );

	/// <summary>Finish rendering before changing the window mode at the caller's frame boundary.</summary>
	internal void Flush()
	{
		if ( SwapChain == default ) return;
		g_pRenderDevice.Flush();
		g_pRenderDevice.ForceFlushGPU( SwapChain );
	}

	/// <summary>
	/// Apply fullscreen at a frame boundary. Zero dimensions select desktop fullscreen. A timeout
	/// leaves the request pending; callers must retry before sizing against the restored desktop.
	/// </summary>
	internal unsafe bool SetFullscreen( bool fullscreen, int width = 0, int height = 0, float refreshRate = 0 )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !Sync() ) return false;

		if ( fullscreen )
		{
			Sdl.DisplayMode mode = default;
			var exclusive = width > 0 && height > 0 && Sdl.GetClosestFullscreenDisplayMode( SdlDisplay.ForWindow( Handle ), width, height, refreshRate, false, out mode );
			// A saved resolution can become unavailable after changing displays. Null selects desktop fullscreen.
			Check( Sdl.SetWindowFullscreenMode( Handle, exclusive ? (IntPtr)(&mode) : IntPtr.Zero ) );
			// Exclusive modes can change in place, without restoring the desktop first.
			Check( Sdl.SetWindowFullscreen( Handle, true ) );
			fullscreenUsage = exclusive ? RenderDisplayModeUsage.ExclusiveFullscreen : RenderDisplayModeUsage.CooperativeFullscreen;
		}
		else
		{
			if ( (Flags & Sdl.WindowFlags.Fullscreen) != 0 )
			{
				Check( Sdl.SetWindowFullscreen( Handle, false ) );
				if ( !Sync() ) return false;
			}
			Check( Sdl.SetWindowFullscreenMode( Handle, IntPtr.Zero ) );
		}

		return Sync();
	}

	/// <summary>
	/// Request relative mouse mode on the main thread and return SDL's actual state, including on failure.
	/// </summary>
	internal static bool SetRelativeMouseMode( IntPtr window, bool relative )
	{
		if ( window == IntPtr.Zero ) return false;
		if ( Sdl.GetWindowRelativeMouseMode( window ) != relative )
			WarnIfFailed( Sdl.SetWindowRelativeMouseMode( window, relative ) );
		return Sdl.GetWindowRelativeMouseMode( window );
	}

	internal static void Check( bool success )
	{
		if ( !success ) throw new InvalidOperationException( $"SDL window: {Sdl.GetError()}" );
	}

	internal static bool WarnIfFailed( bool success )
	{
		if ( !success ) Log.Warning( $"SDL window: {Sdl.GetError()}" );
		return success;
	}

	/// <summary>
	/// Destroy the swapchain before its SDL window, on the main thread before render-device teardown.
	/// </summary>
	public void Dispose()
	{
		ThreadSafe.AssertIsMainThread();
		if ( Handle == IntPtr.Zero ) return;

		// The window must outlive the GPU work and Vulkan surface using it.
		if ( SwapChain != default )
		{
			g_pRenderDevice.DestroySwapChain( SwapChain );
			SwapChain = default;
		}
		Sdl.DestroyWindow( Handle );
		Handle = IntPtr.Zero;
	}
}
