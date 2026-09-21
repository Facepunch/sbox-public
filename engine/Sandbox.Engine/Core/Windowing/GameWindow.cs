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

	readonly SdlWindow window;
	WindowMode? windowMode;
	VideoMode? pendingMode;
	bool attached;
	bool renderingInitialized;

	internal IntPtr Handle => window.Handle;

	internal GameSurface Surface => new( window.Handle, window.SwapChain );

	internal string Title
	{
		set => window.Title = value;
	}

	internal GameWindow( string title )
	{
		ThreadSafe.AssertIsMainThread();

		var mode = VideoMode.FromCommandLine( RenderSettings.Instance );
		var fullscreen = mode.Window.Fullscreen;

		Sdl.SetHint( "SDL_MOUSE_FOCUS_CLICKTHROUGH", "1" );
		if ( OperatingSystem.IsLinux() )
		{
			// Match ApplyWindowMode's desktop-fullscreen policy before SDL applies the request.
			if ( mode.Window.Borderless )
			{
				fullscreen = true;
			}

			// Loading can park the event loop long enough for X11 to report a hung application.
			Sdl.SetHint( "SDL_VIDEO_X11_NET_WM_PING", "0" );
		}
		window = new SdlWindow( Sdl.CreateWindow( title, Math.Max( mode.Window.Width, 1 ), Math.Max( mode.Window.Height, 1 ), Sdl.WindowFlags.Hidden | Sdl.WindowFlags.Resizable | Sdl.WindowFlags.Vulkan ) );

		try
		{
			if ( !ApplyWindowMode( mode.Window ) ) pendingMode = mode;

			if ( CommandLine.HasSwitch( "-x" ) || CommandLine.HasSwitch( "-y" ) )
			{
				WarnIfFailed( Sdl.GetWindowPosition( window.Handle, out var x, out var y ) );
				WarnIfFailed( Sdl.SetWindowPosition( window.Handle, CommandLine.GetSwitchInt( "-x", x ), CommandLine.GetSwitchInt( "-y", y ) ) );
				window.Sync();
			}

			window.CreateSwapChain( "GameWindow", mode.Multisample, mode.VSync, mainWindow: true );

			GameWindowNative.Attach( window.Handle, window.SwapChain );
			attached = true;
			WarnIfFailed( Sdl.StartTextInput( window.Handle ) );

			Current = this;
			if ( !CommandLine.HasSwitch( "-nowindow" ) )
			{
				if ( fullscreen && window.Show() )
				{
					// SDL applies fullscreen geometry when shown. Match it before drawing the splash.
					pendingMode = mode;
					ApplyPendingMode();
				}
				try { DrawStartupImage(); }
				catch ( Exception e ) { Log.Warning( e, "Couldn't draw the startup image" ); }
				if ( !fullscreen ) window.Show();
			}
		}
		catch
		{
			Dispose();
			throw;
		}
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
