using NativeEngine;
using Sandbox.Engine.Settings;
using Sandbox.Utility;
using static Sandbox.Engine.SdlWindow;

namespace Sandbox.Engine;

/// <summary>
/// Owns the game's SDL window and main swap chain. The engine borrows them while running.
/// </summary>
[SkipHotload]
internal sealed partial class GameWindow : IDisposable
{
	internal static GameWindow Current { get; private set; }

	SdlWindow window;
	readonly string title;
	GameStartupWindow startupWindow;
	WindowMode? windowMode;
	VideoMode? pendingMode;
	bool attached;
	bool renderingRequested;
	bool renderingInitialized;
	VideoMode startupVideoMode;

	internal IntPtr Handle => window.Handle;

	internal GameSurface Surface => new( window.Handle, window.SwapChain );

	internal string Title
	{
		set => window.Title = value;
	}

	internal GameWindow( string title, GameStartupWindow startupWindow = null )
	{
		ThreadSafe.AssertIsMainThread();
		var mode = VideoMode.FromCommandLine( RenderSettings.Instance );
		var fullscreen = mode.Window.Fullscreen;

		Sdl.SetHint( "SDL_MOUSE_FOCUS_CLICKTHROUGH", "1" );
		if ( OperatingSystem.IsLinux() )
		{
			if ( mode.Window.Borderless ) fullscreen = true;
			Sdl.SetHint( "SDL_VIDEO_X11_NET_WM_PING", "0" );
		}
		var initialStartupProgress = startupWindow?.Progress ?? 0.02f;
		var initialStartupStatus = startupWindow?.Status ?? "Initializing Vulkan";

		// The splash owns the window's software surface, which can't coexist with a swapchain. It keeps
		// drawing until the first frame, then CompleteStartup promotes the same window.
		this.startupWindow = startupWindow is { CanTransferToVulkan: true } ? startupWindow : null;
		if ( this.startupWindow is null ) startupWindow?.Dispose();
		startupVideoMode = mode;

		this.title = title;
		window = this.startupWindow is not null ? new SdlWindow( this.startupWindow.Handle ) : CreateVulkanWindow( title, mode );

		try
		{
			if ( this.startupWindow is not null )
			{
				windowMode = null;
				Screen.Size = window.PixelSize;
				if ( fullscreen ) pendingMode = mode;
				Current = this;
				UpdateStartupProgress( initialStartupProgress, initialStartupStatus );
				return;

			}

			InitializeVulkanWindow( mode );
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	void AttachSwapChain( VideoMode mode )
	{
		window.CreateSwapChain( "GameWindow", mode.Multisample, mode.VSync, mainWindow: true );

		GameWindowNative.Attach( window.Handle, window.SwapChain );
		attached = true;
		WarnIfFailed( Sdl.StartTextInput( window.Handle ) );
	}

	/// <summary>Promote the splash's window to the main swapchain. Must run before the first frame.</summary>
	internal void CompleteStartup()
	{
		if ( startupWindow is not { } splash ) return;
		ThreadSafe.AssertIsMainThread();

		startupFinished = true;
		startupWindow = null;
		if ( splash.TransferToVulkan() == IntPtr.Zero )
		{
			// The splash window can't be promoted, e.g. it was closed during startup, so start over with a fresh one
			splash.Detach();
			window.Dispose();
			window = CreateVulkanWindow( title, startupVideoMode );
			InitializeVulkanWindow( startupVideoMode );
		}
		else
		{
			AttachSwapChain( startupVideoMode );
		}
		Screen.UpdateFromEngine();

		if ( renderingRequested ) InitializeRendering();
	}

	static SdlWindow CreateVulkanWindow( string title, VideoMode mode )
	{
		var width = Math.Max( mode.Window.Width, 1 );
		var height = Math.Max( mode.Window.Height, 1 );
		return new SdlWindow( Sdl.CreateWindow( title, width, height, Sdl.WindowFlags.Hidden | Sdl.WindowFlags.Resizable | Sdl.WindowFlags.Vulkan ) );
	}

	void InitializeVulkanWindow( VideoMode mode )
	{
		var fullscreen = mode.Window.Fullscreen || (OperatingSystem.IsLinux() && mode.Window.Borderless);

		if ( !ApplyWindowMode( mode.Window ) )
		{
			pendingMode = mode;
		}

		if ( CommandLine.HasSwitch( "-x" ) || CommandLine.HasSwitch( "-y" ) )
		{
			WarnIfFailed( Sdl.GetWindowPosition( window.Handle, out var x, out var y ) );
			WarnIfFailed( Sdl.SetWindowPosition( window.Handle, CommandLine.GetSwitchInt( "-x", x ), CommandLine.GetSwitchInt( "-y", y ) ) );
			window.Sync();
		}

		AttachSwapChain( mode );

		Current = this;
		if ( CommandLine.HasSwitch( "-nowindow" ) ) return;

		if ( fullscreen && window.Show() )
		{
			pendingMode = mode;
			ApplyPendingMode();
		}
		if ( !fullscreen ) window.Show();
	}
	internal void QueueVideoMode( VideoMode mode )
	{
		ThreadSafe.AssertIsMainThread();
		pendingMode = mode;
	}

	internal void InvalidateWindowMode() => windowMode = null;

	/// <summary>Apply mode changes after the native frame has finished using the swap chain.</summary>
	internal void ApplyPendingMode()
	{
		if ( pendingMode is not { } video ) return;
		ThreadSafe.AssertIsMainThread();
		if ( (window.Flags & Sdl.WindowFlags.Minimized) != 0 ) return;
		var size = window.PixelSize;
		if ( size.x < 1 || size.y < 1 ) return;
		window.Flush();

		if ( !ApplyWindowMode( video.Window ) ) return;
		if ( !window.UpdateSwapChain( video.Multisample, video.VSync ) ) return;
		if ( pendingMode == video ) pendingMode = null;
	}

	bool ApplyWindowMode( WindowMode requested )
	{
		ThreadSafe.AssertIsMainThread();

		// Linux needs desktop fullscreen to hide desktop panels without switching display modes.
		if ( OperatingSystem.IsLinux() && (requested.Fullscreen || requested.Borderless) )
		{
			requested = requested with
			{
				Fullscreen = true,
				Width = 0,
				Height = 0
			};
		}

		if ( windowMode == requested ) return window.Sync();
		windowMode = null;

		if ( !window.SetFullscreen( requested.Fullscreen, requested.Width, requested.Height ) ) return false;
		if ( !requested.Fullscreen )
		{
			// Bounds must reflect the restored desktop, not the previous exclusive resolution.
			var bounds = SdlDisplay.GetBounds( SdlDisplay.ForWindow( window.Handle ), usable: !requested.Borderless );
			Check( bounds.Width > 0 && bounds.Height > 0 );
			var size = requested.GetSize( bounds.Size );
			var (width, height) = ((int)size.x, (int)size.y);
			WarnIfFailed( Sdl.SetWindowBordered( window.Handle, !requested.Borderless ) );
			WarnIfFailed( Sdl.SetWindowResizable( window.Handle, true ) );
			Check( Sdl.SetWindowSize( window.Handle, width, height ) );
			WarnIfFailed( Sdl.SetWindowPosition( window.Handle, (int)bounds.Left + ((int)bounds.Width - width) / 2, (int)bounds.Top + ((int)bounds.Height - height) / 2 ) );
			if ( !window.Sync() ) return false;
		}

		windowMode = requested;
		return true;
	}

	public void Dispose()
	{
		ThreadSafe.AssertIsMainThread();
		if ( window.Handle == IntPtr.Zero ) return;

		if ( Current == this ) Current = null;
		startupWindow?.Detach();
		startupWindow = null;
		if ( renderingInitialized )
		{
			CSceneSystem.SetMainSwapChain( default );
			renderingInitialized = false;
		}
		if ( attached )
		{
			GameWindowNative.Detach( window.Handle );
			attached = false;
		}
		window.Dispose();
	}
}
