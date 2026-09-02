using NativeEngine;
using Sandbox.Engine.Settings;
using Sandbox.UI;
using System;

namespace Editor;

/// <summary>
/// An OS window whose entire contents are panel UI. It owns the window, its swap chain and the UI
/// inside it. Its input comes straight from SDL and never touches the engine's input system -
/// there's no widget toolkit involved anywhere.
/// <para>
/// Editor only. A game has one window and draws its UI into that.
/// </para>
/// </summary>
public sealed partial class PanelWindow : IDisposable, IPanelWindow
{
	static readonly List<PanelWindow> _all = new();

	/// <summary>
	/// Every window that's currently open.
	/// </summary>
	public static IReadOnlyList<PanelWindow> All => _all;

	/// <summary>
	/// The window the OS is giving keyboard input to, if it's one of ours.
	/// </summary>
	public static PanelWindow Focused
	{
		get
		{
			for ( int i = 0; i < _all.Count; i++ )
			{
				if ( _all[i].IsFocused ) return _all[i];
			}

			return null;
		}
	}

	/// <summary>
	/// The window a panel is being shown in, if it's one of ours.
	/// </summary>
	public static PanelWindow FromPanel( Panel panel )
	{
		var root = panel?.FindRootPanel();
		if ( root is null ) return null;

		foreach ( var window in _all )
		{
			if ( window.Root == root ) return window;
		}

		return null;
	}

	/// <summary>
	/// Close every window.
	/// </summary>
	internal static void DisposeAll()
	{
		foreach ( var window in _all.ToArray() )
		{
			window.Dispose();
		}
	}

	IntPtr _window;
	SwapChainHandle_t _swapChain;
	SceneCamera _camera;
	SceneWorld _world;

	/// <summary>
	/// The UI running in this window. Engine machinery - tool code wants <see cref="Root"/>.
	/// </summary>
	internal UISurface Surface { get; private set; }

	/// <summary>
	/// The panel everything in this window hangs off.
	/// </summary>
	public RootPanel Root => Surface?.Root;

	/// <summary>
	/// Where the cursor is, in this window's pixels.
	/// </summary>
	public Vector2 MousePosition => Surface?.MousePosition ?? 0;

	IntPtr IPanelWindow.Handle => _window;
	UISurface IPanelWindow.Surface => Surface;

	/// <summary>
	/// Called when the user clicks the window's close button. The window closes if this is null.
	/// </summary>
	public Action OnCloseRequested { get; set; }

	/// <summary>
	/// What the window clears to before the UI is drawn.
	/// </summary>
	public Color BackgroundColor
	{
		get => _camera?.BackgroundColor ?? Color.Black;
		set { if ( _camera is not null ) _camera.BackgroundColor = value; }
	}

	/// <summary>
	/// The window's title bar text.
	/// </summary>
	public string Title
	{
		get => field;
		set
		{
			field = value;
			if ( _window != IntPtr.Zero ) PanelWindowNative.SetTitle( _window, value ?? "" );
		}
	}

	/// <summary>
	/// Size of the window's client area, in pixels.
	/// </summary>
	public Vector2 Size
	{
		get
		{
			if ( _window == IntPtr.Zero ) return _pendingSize;

			PanelWindowNative.GetClientSize( _window, out var w, out var h );
			return new Vector2( w, h );
		}

		set
		{
			if ( _window == IntPtr.Zero ) return;

			PanelWindowNative.SetSize( _window, (int)value.x, (int)value.y );
		}
	}

	/// <summary>
	/// Position of the window on the desktop, in pixels.
	/// </summary>
	public Vector2 Position
	{
		get
		{
			if ( _window == IntPtr.Zero ) return _pendingPosition;

			PanelWindowNative.GetBounds( _window, out var x, out var y, out _, out _ );
			return new Vector2( x, y );
		}

		set
		{
			if ( _window == IntPtr.Zero ) return;

			PanelWindowNative.SetPosition( _window, (int)value.x, (int)value.y );
		}
	}

	/// <summary>
	/// The smallest the user can resize the window to. Zero means no limit.
	/// </summary>
	public Vector2 MinSize
	{
		get => field;
		set
		{
			field = value;
			if ( _window != IntPtr.Zero ) PanelWindowNative.SetMinSize( _window, (int)value.x, (int)value.y );
		}
	}

	/// <summary>
	/// The largest the user can resize the window to. Zero means no limit.
	/// </summary>
	public Vector2 MaxSize
	{
		get => field;
		set
		{
			field = value;
			if ( _window != IntPtr.Zero ) PanelWindowNative.SetMaxSize( _window, (int)value.x, (int)value.y );
		}
	}

	/// <summary>
	/// Whether the OS is allowed to maximize the window - the caption button, double clicking
	/// the title bar, Win+Up, snap. Windows drawing their own chrome check this for their
	/// maximize button too.
	/// </summary>
	public bool CanMaximize
	{
		get => field;
		set
		{
			field = value;
			if ( _window != IntPtr.Zero ) PanelWindowNative.SetCanMaximize( _window, value );
		}
	} = true;

	/// <summary>
	/// Does this window have keyboard focus?
	/// </summary>
	public bool IsFocused => _window != IntPtr.Zero && PanelWindowNative.IsFocused( _window );

	/// <summary>
	/// Keep drawing at the display's frame rate even when nobody is looking at this window.
	/// Idle windows are paced right down - set this for one with something moving in it that
	/// has to keep moving, like a video or a live preview.
	/// </summary>
	public bool AlwaysFullFrameRate { get; set; }

	/// <summary>
	/// Is this window still open?
	/// </summary>
	public bool IsOpen => Surface is not null;

	/// <summary>
	/// True if we're drawing the title bar and borders ourselves.
	/// </summary>
	public bool Borderless { get; }

	/// <summary>
	/// Does this window's present wait for the display?
	/// </summary>
	public bool VSync { get; }

	/// <summary>
	/// Is the window maximized?
	/// </summary>
	public bool IsMaximized => _window != IntPtr.Zero && PanelWindowNative.IsMaximized( _window );

	/// <summary>
	/// Open a window and start running UI in it.
	/// </summary>
	public PanelWindow( string title, Vector2 size ) : this( title, size, new Vector2( -1, -1 ), false )
	{
	}

	/// <summary>
	/// Open a window at a given desktop position. Pass -1,-1 to let the OS place it.
	/// </summary>
	public PanelWindow( string title, Vector2 size, Vector2 position ) : this( title, size, position, false )
	{
	}

	/// <summary>
	/// Open a window. A borderless window has no OS title bar - draw your own, and mark the panels
	/// that should drag it with the <c>window-drag</c> class.
	/// </summary>
	public PanelWindow( string title, Vector2 size, Vector2 position, bool borderless ) : this( title, size, position, borderless, false )
	{
	}

	/// <summary>
	/// Open a window. With <paramref name="vsync"/> the window's present blocks for the display,
	/// which is what an app that has nothing else to do wants - the launcher paces itself on it.
	/// </summary>
	public PanelWindow( string title, Vector2 size, Vector2 position, bool borderless, bool vsync )
	{
		ThreadSafe.AssertIsMainThread();

		VSync = vsync;
		Borderless = borderless;
		Title = title;

		_window = PanelWindowNative.Create( title ?? "", (int)position.x, (int)position.y, (int)size.x, (int)size.y, borderless );
		if ( _window == IntPtr.Zero )
			throw new Exception( "Couldn't create the window" );

		if ( borderless )
			PanelWindowNative.EnableCustomChrome( _window );

		// No MSAA. Panel UI is 2D and alpha blended - it antialiases itself in the shaders, and a
		// multisampled swapchain costs a resolve every frame plus the multisampled colour and depth
		// images behind it (23MB for a 1100x660 window at 4x, more than the window's own buffers)
		_swapChain = PanelWindowNative.CreateSwapChain( _window, (int)RenderMultisampleType.RENDER_MULTISAMPLE_NONE, VSync );
		_swapChainSize = Size;

		_world = new SceneWorld();

		_camera = new SceneCamera( "PanelWindow" )
		{
			World = _world,
			BackgroundColor = Color.Black,
			ClearFlags = ClearFlags.All,
			EnablePostProcessing = false,
			ZNear = 1,
			ZFar = 1000,

			// A window is panels and nothing else - it doesn't need the scene pipeline
			UIOnly = true,
		};

		Surface = new UISurface();
		Surface.OnCursorChanged = x => _cursor = x;

		_all.Add( this );
		PanelWindows.Register( this );
	}

	/// <summary>
	/// Close the window and delete its panels.
	/// </summary>
	public void Dispose()
	{
		if ( Surface is null )
			return;

		_all.Remove( this );
		PanelWindows.Unregister( this );

		Surface?.Dispose();
		Surface = null;

		_camera?.Dispose();
		_camera = null;

		_world?.Delete();
		_world = null;

		if ( _swapChain != default )
		{
			var chain = _swapChain;
			_swapChain = default;
			EngineLoop.DisposeAtFrameEnd( new Sandbox.Utility.DisposeAction( () => g_pRenderDevice.DestroySwapChain( chain ) ) );
		}

		if ( _window != IntPtr.Zero )
		{
			if ( _isPopup ) PanelWindowNative.DestroyPopup( _window );
			else PanelWindowNative.Destroy( _window );

			_window = IntPtr.Zero;
		}
	}

	/// <summary>
	/// The user clicked the window's close button.
	/// </summary>
	public void RequestClose()
	{
		if ( OnCloseRequested is not null )
		{
			OnCloseRequested();
			return;
		}

		Dispose();
	}

	/// <summary>
	/// Minimize the window.
	/// </summary>
	public void Minimize()
	{
		if ( _window != IntPtr.Zero ) PanelWindowNative.Minimize( _window );
	}

	/// <summary>
	/// Maximize the window, or put it back if it already is.
	/// </summary>
	public void ToggleMaximized()
	{
		if ( _window == IntPtr.Zero ) return;
		if ( !CanMaximize ) return;

		if ( IsMaximized ) PanelWindowNative.Restore( _window );
		else PanelWindowNative.Maximize( _window );
	}

	/// <summary>
	/// Bring the window to the front.
	/// </summary>
	public void Focus()
	{
		if ( _window != IntPtr.Zero ) PanelWindowNative.SetForeground( _window );
	}
}
