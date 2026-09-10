using NativeEngine;
using Sandbox.Engine.Settings;
using Sandbox.UI;
using System;

namespace Editor;

/// <summary>
/// An OS window whose entire contents are panel UI. It owns the window, its swap chain and the UI
/// inside it. Its input comes straight from SDL and never touches the engine's input system -
/// there's no widget toolkit involved anywhere. A <see cref="PopupWindow"/> is one that hangs off
/// another, the way a menu does.
/// <para>
/// Editor only. A game has one window and draws its UI into that.
/// </para>
/// </summary>
public partial class PanelWindow : IDisposable, IPanelWindow
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

	SwapChainHandle_t _swapChain;
	SceneCamera _camera;
	SceneWorld _world;

	/// <summary>
	/// The OS window. Zero until it exists - a popup makes its own at the first frame boundary.
	/// </summary>
	internal IntPtr Handle { get; private protected set; }

	/// <summary>
	/// A popup - a transient window like a menu, dismissed by a click anywhere else.
	/// </summary>
	public virtual bool IsPopup => false;

	/// <summary>
	/// A window that never takes the keyboard or the mouse.
	/// </summary>
	public virtual bool IgnoresInput => false;

	/// <summary>
	/// The window this one hangs off, if it's a popup.
	/// </summary>
	internal virtual IPanelWindow ParentWindow => null;

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

	IntPtr IPanelWindow.Handle => Handle;
	UISurface IPanelWindow.Surface => Surface;
	bool IPanelWindow.IsPopup => IsPopup;
	bool IPanelWindow.IgnoresInput => IgnoresInput;
	IPanelWindow IPanelWindow.Parent => ParentWindow;

	/// <summary>
	/// The user asked to close the window. Return false to keep it open - an unsaved changes
	/// prompt, say. Otherwise it's disposed, or hidden when <see cref="HideOnClose"/> is set.
	/// </summary>
	public Func<bool> OnCloseRequested { get; set; }

	/// <summary>
	/// When the user closes the window, hide it instead of disposing it, so it can be shown again
	/// with everything still in it. Off by default: closing disposes.
	/// </summary>
	public bool HideOnClose { get; set; }

	/// <summary>
	/// The window moved, whether the user dragged it or code set <see cref="Position"/>.
	/// </summary>
	public Action OnMoved { get; set; }

	/// <summary>
	/// The window is on a different display than it was. Its scale may have changed with it.
	/// </summary>
	public Action OnDisplayChanged { get; set; }

	void IPanelWindow.Moved() => OnMoved?.InvokeWithWarning();

	/// <summary>
	/// The window's client size changed, whether the user dragged an edge or code set <see cref="Size"/>.
	/// </summary>
	public Action OnResized { get; set; }

	/// <summary>
	/// The window was minimized to the taskbar.
	/// </summary>
	public Action OnMinimized { get; set; }

	/// <summary>
	/// The window was maximized.
	/// </summary>
	public Action OnMaximized { get; set; }

	/// <summary>
	/// The window came back from being minimized or maximized.
	/// </summary>
	public Action OnRestored { get; set; }

	Vector2 _lastPixelSize;

	void IPanelWindow.Resized()
	{
		// SDL sends these for exposes too, and a resize drag sends several per frame
		var size = PixelSize;
		if ( size == _lastPixelSize ) return;

		_lastPixelSize = size;
		OnResized?.InvokeWithWarning();
	}

	/// <summary>
	/// The window became the active one - it has the keyboard, its title bar lit up.
	/// </summary>
	public Action OnActivated { get; set; }

	/// <summary>
	/// The window stopped being the active one: the user went to another window, ours or
	/// someone else's.
	/// </summary>
	public Action OnDeactivated { get; set; }

	void IPanelWindow.FocusChanged( bool focused )
	{
		// Alt-tabbing out of a look drag never sends the mouse up that would end it
		if ( !focused ) ReleaseMouseCapture();

		if ( focused ) OnActivated?.InvokeWithWarning();
		else OnDeactivated?.InvokeWithWarning();
	}

	void IPanelWindow.StateChanged( int state )
	{
		switch ( state )
		{
			case 1: OnMinimized?.InvokeWithWarning(); break;
			case 2: OnMaximized?.InvokeWithWarning(); break;
			default: OnRestored?.InvokeWithWarning(); break;
		}
	}
	void IPanelWindow.DisplayChanged() => OnDisplayChanged?.InvokeWithWarning();

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
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetTitle( Handle, value ?? "" );
		}
	}

	/// <summary>
	/// Size of the window's client area, in the units the UI inside it is authored in. The window
	/// on the desktop is this much again bigger on a display that scales.
	/// </summary>
	public Vector2 Size
	{
		get
		{
			if ( Handle == IntPtr.Zero ) return default;

			return PixelSize / Surface.DpiScale;
		}

		set
		{
			if ( Handle == IntPtr.Zero ) return;

			var window = UiToWindow( value );
			PanelWindowNative.SetSize( Handle, (int)MathF.Ceiling( window.x ), (int)MathF.Ceiling( window.y ) );
		}
	}

	/// <summary>
	/// Size of the window's client area in real pixels - what the swap chain is sized to, and what
	/// the surface lays its panels out in. Zero until there's an OS window to measure: a popup
	/// waits for a frame boundary to make one.
	/// </summary>
	internal Vector2 PixelSize
	{
		get
		{
			if ( Handle == IntPtr.Zero ) return default;

			PanelWindowNative.GetClientSize( Handle, out var w, out var h );
			return new Vector2( w, h );
		}
	}

	//
	// Three spaces meet at a window, and SDL hands us the one number that isn't obvious:
	//
	//   ui      - what panels are authored in, and what everything public here is measured in
	//   pixels  - what the surface lays out in and what the swap chain is sized to
	//   window  - what SDL reports input in and takes geometry in
	//
	// pixels = ui * Surface.DpiScale, and pixels = window * PixelDensity. On Windows a window
	// coordinate is already a pixel and the display scale carries everything; on a retina Mac the
	// density carries it instead. The three conversions below are the only places this matters -
	// nothing outside this class has to know either number exists.
	//

	/// <summary>
	/// Pixels to one of SDL's window coordinates. One on Windows, two on a retina Mac. This is not
	/// the display scale - a 1.75x Windows display has a display scale of 1.75 and a density of 1.
	/// </summary>
	float PixelDensity => Handle == IntPtr.Zero ? 1.0f : PanelWindowNative.GetPixelDensity( Handle );

	/// <summary>
	/// Authored UI units to window coordinates, for handing the OS a size or a size limit. Uses the
	/// same scale the surface lays out with, so a window can't disagree with what's inside it.
	/// </summary>
	Vector2 UiToWindow( Vector2 ui ) => ui * Surface.DpiScale / PixelDensity;

	/// <summary>
	/// Surface pixels to window coordinates, for handing the OS a position or a size.
	/// </summary>
	internal Vector2 PixelsToWindow( Vector2 pixels ) => pixels / PixelDensity;

	/// <summary>
	/// Window coordinates to surface pixels, for input arriving from SDL.
	/// </summary>
	Vector2 WindowToPixels( Vector2 window ) => window * PixelDensity;

	/// <summary>
	/// Position of the window on the desktop, in the OS's own coordinates - desktop pixels on
	/// Windows. Deliberately not the units <see cref="Size"/> is in: a desktop spanning displays
	/// that scale differently has no single UI unit to measure it in.
	/// </summary>
	public Vector2 Position
	{
		get
		{
			if ( Handle == IntPtr.Zero ) return default;

			PanelWindowNative.GetBounds( Handle, out var x, out var y, out _, out _ );
			return new Vector2( x, y );
		}

		set
		{
			if ( Handle == IntPtr.Zero ) return;

			PanelWindowNative.SetPosition( Handle, (int)value.x, (int)value.y );
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
			ApplySizeLimits();
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
			ApplySizeLimits();
		}
	}

	/// <summary>
	/// Hand the size limits to the OS in its own units. Re-applied when the display scale changes,
	/// because the same limit is a different number of window coordinates on a display that scales
	/// differently. Zero means no limit, which is what SDL wants too.
	/// </summary>
	void ApplySizeLimits()
	{
		if ( Handle == IntPtr.Zero ) return;

		var min = UiToWindow( MinSize );
		var max = UiToWindow( MaxSize );

		PanelWindowNative.SetMinSize( Handle, (int)min.x, (int)min.y );
		PanelWindowNative.SetMaxSize( Handle, (int)max.x, (int)max.y );
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
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetCanMaximize( Handle, value );
		}
	} = true;

	/// <summary>
	/// Whether the compositor draws its shadow around the window. A decorated window has one from
	/// its frame; a borderless window or a popup has to ask, and both do by default.
	/// </summary>
	public bool DropShadow
	{
		get => field;
		set
		{
			field = value;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetDropShadow( Handle, value );
		}
	}

	/// <summary>
	/// Whether the compositor rounds the window's corners, the way it rounds the OS's own menus.
	/// Popups ask for it. Windows 10 and earlier stay square.
	/// </summary>
	public bool RoundedCorners
	{
		get => field;
		set
		{
			field = value;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetRoundedCorners( Handle, value );
		}
	}

	/// <summary>
	/// Is the window on screen at all, as opposed to hidden with <see cref="Hide"/> or minimized?
	/// </summary>
	public bool IsVisible => Handle != IntPtr.Zero && PanelWindowNative.IsVisible( Handle );

	/// <summary>
	/// Is the window minimized to the taskbar?
	/// </summary>
	public bool IsMinimized => Handle != IntPtr.Zero && PanelWindowNative.IsMinimized( Handle );

	/// <summary>
	/// Keep the window above every window that isn't. Palettes, overlays, a video you want to
	/// keep watching while working in something else.
	/// </summary>
	public bool KeepOnTop
	{
		get => field;
		set
		{
			field = value;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetAlwaysOnTop( Handle, value );
		}
	}

	/// <summary>
	/// How see-through the whole window is, contents included. 1 is opaque, 0 is invisible.
	/// </summary>
	public float Opacity
	{
		get => field;
		set
		{
			field = Math.Clamp( value, 0, 1 );
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetOpacity( Handle, field );
		}
	} = 1;

	/// <summary>
	/// Whether the window gets a button in the taskbar. On by default; a palette or a floating
	/// panel that belongs to another window usually turns it off.
	/// </summary>
	public bool ShowInTaskbar
	{
		get => field;
		set
		{
			if ( field == value ) return;

			field = value;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetShowInTaskbar( Handle, value );
		}
	} = true;

	/// <summary>
	/// Whether the user can resize the window by its edges. Off makes the size whatever code says.
	/// </summary>
	public bool Resizable
	{
		get => field;
		set
		{
			field = value;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetResizable( Handle, value );
		}
	} = true;

	/// <summary>
	/// Fill the window's display. Borderless over the desktop unless <see cref="ExclusiveFullscreen"/>
	/// is set. Turning it off puts the window back where it was.
	/// </summary>
	public bool Fullscreen
	{
		get => field;
		set
		{
			field = value;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetFullscreen( Handle, value, ExclusiveFullscreen );
		}
	}

	/// <summary>
	/// Whether <see cref="Fullscreen"/> takes the display over with a real display mode, the way
	/// a game does, instead of a borderless window over the desktop. Off by default: borderless
	/// switches instantly and plays well with other windows and displays.
	/// </summary>
	public bool ExclusiveFullscreen
	{
		get => field;
		set
		{
			field = value;
			if ( Handle != IntPtr.Zero && Fullscreen ) PanelWindowNative.SetFullscreen( Handle, true, value );
		}
	}

	/// <summary>
	/// Is the window fullscreen right now, either way?
	/// </summary>
	public bool IsFullscreen => Handle != IntPtr.Zero && PanelWindowNative.IsFullscreen( Handle );

	/// <summary>
	/// The whole of the display the window is on, in the same desktop coordinates as <see cref="Position"/>.
	/// </summary>
	public Rect DisplayBounds
	{
		get
		{
			if ( Handle == IntPtr.Zero ) return default;

			PanelWindowNative.GetDisplayBounds( Handle, out var x, out var y, out var w, out var h );
			return new Rect( x, y, w, h );
		}
	}

	/// <summary>
	/// The part of the window's display that isn't under the taskbar or a dock, in desktop coordinates.
	/// </summary>
	public Rect DisplayWorkArea
	{
		get
		{
			if ( Handle == IntPtr.Zero ) return default;

			PanelWindowNative.GetDisplayWorkArea( Handle, out var x, out var y, out var w, out var h );
			return new Rect( x, y, w, h );
		}
	}

	/// <summary>
	/// Hold the window at this width to height ratio while the user resizes it - 16f / 9 keeps a
	/// video window the shape of its video. Null lets it be any shape.
	/// </summary>
	public float? AspectRatioLock
	{
		get => field;
		set
		{
			field = value is > 0 ? value : null;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetAspectRatio( Handle, field ?? 0 );
		}
	}

	/// <summary>
	/// The window this one belongs to. An owned window stays above its owner, minimizes with it,
	/// and is closed with it - a tool palette, a dialog. Null makes it a top level window again.
	/// </summary>
	public PanelWindow Owner
	{
		get => field;
		set
		{
			if ( value == this ) return;

			field = value;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetOwner( Handle, value?.Handle ?? IntPtr.Zero );
		}
	}

	/// <summary>
	/// Block the <see cref="Owner"/> from taking input while this window is open, the way a
	/// dialog does. Does nothing without an owner. Cleared on close, so the owner isn't left dead.
	/// </summary>
	public bool Modal
	{
		get => field;
		set
		{
			field = value;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetModal( Handle, value && Owner is not null );
		}
	}

	/// <summary>
	/// Whether the user can close the window: the caption button greys out and Alt+F4 does
	/// nothing. Code can still close it. Windows drawing their own chrome check this for their
	/// close button too.
	/// </summary>
	public bool CanClose
	{
		get => field;
		set
		{
			field = value;
			if ( Handle != IntPtr.Zero ) PanelWindowNative.SetCanClose( Handle, value );
		}
	} = true;

	/// <summary>
	/// Does this window have keyboard focus?
	/// </summary>
	public bool IsFocused => Handle != IntPtr.Zero && PanelWindowNative.IsFocused( Handle );

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
	/// True if we're drawing the title bar and borders ourselves. Popups always are.
	/// </summary>
	public bool Borderless => IsPopup || _borderless;

	readonly bool _borderless;

	/// <summary>
	/// Does this window's present wait for the display?
	/// </summary>
	public bool VSync { get; }

	/// <summary>
	/// Is the window maximized?
	/// </summary>
	public bool IsMaximized => Handle != IntPtr.Zero && PanelWindowNative.IsMaximized( Handle );

	/// <summary>
	/// For a window that makes its OS window later - see <see cref="CreateNativeWindow"/>.
	/// </summary>
	private protected PanelWindow()
	{
		ThreadSafe.AssertIsMainThread();
	}

	/// <summary>
	/// Open a window and start running UI in it. The size is in the units the UI inside it is
	/// authored in, the same as <see cref="Size"/> - on a display that scales, the window on the
	/// desktop comes out that much bigger, so what you asked for is what fits inside it.
	/// </summary>
	public PanelWindow( string title, Vector2 size ) : this( title, size, new Vector2( -1, -1 ), false )
	{
	}

	/// <summary>
	/// Open a window at a given desktop position, in the OS's own coordinates - see
	/// <see cref="Position"/>. Pass -1,-1 to let the OS place it.
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
		_borderless = borderless;
		Title = title;

		// The size asked for is in the units the UI is authored in, and the UI is drawn at the
		// display scale - the window has to carry that same scale or the contents it was sized
		// for do not fit. There is no window to ask yet, so ask the display it will open on.
		var displayScale = PanelWindowNative.GetDisplayScaleAt( (int)position.x, (int)position.y );
		var width = (int)MathF.Ceiling( size.x * displayScale );
		var height = (int)MathF.Ceiling( size.y * displayScale );

		Handle = PanelWindowNative.Create( title ?? "", (int)position.x, (int)position.y, width, height, borderless );
		if ( Handle == IntPtr.Zero )
			throw new Exception( "Couldn't create the window" );

		if ( borderless )
		{
			PanelWindowNative.EnableCustomChrome( Handle );
			DropShadow = true;
		}

		// No MSAA. Panel UI is 2D and alpha blended - it antialiases itself in the shaders, and a
		// multisampled swapchain costs a resolve every frame plus the multisampled colour and depth
		// images behind it (23MB for a 1100x660 window at 4x, more than the window's own buffers)
		CreateRenderer( "PanelWindow", (int)RenderMultisampleType.RENDER_MULTISAMPLE_NONE, VSync );

		var surface = new UISurface();

		// Before the first frame, so anything set on the window between here and then - a size
		// limit, say - converts against the scale the surface will actually lay out with
		surface.DpiScale = PanelWindowNative.GetContentsScale( Handle );

		Attach( surface );
	}

	/// <summary>
	/// Take the UI this window will run, and join the open windows.
	/// </summary>
	private protected void Attach( UISurface surface )
	{
		Surface = surface;
		Surface.OnCursorChanged = x => _cursor = x;
		Surface.Tooltips.Host = this;
		Surface.System.PopupHost = this;

		_all.Add( this );
		PanelWindows.Register( this );
	}

	/// <summary>
	/// The swap chain and the camera that draws the surface into it. Needs <see cref="Handle"/>.
	/// </summary>
	private protected void CreateRenderer( string name, int multisample, bool vsync )
	{
		_swapChain = PanelWindowNative.CreateSwapChain( Handle, multisample, vsync );
		_swapChainSize = PixelSize;

		_world = new SceneWorld();

		_camera = new SceneCamera( name )
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
	}

	/// <summary>
	/// Make the OS window at a frame boundary, for a window that couldn't at construction. Returns
	/// whether there's a window to draw now.
	/// </summary>
	private protected virtual bool CreateNativeWindow() => false;

	/// <summary>
	/// Destroy the OS window, at frame end, after its swap chain.
	/// </summary>
	private protected virtual void DestroyNativeWindow( IntPtr window ) => PanelWindowNative.Destroy( window );

	/// <summary>
	/// The window is closing. Its surface and popups are still there.
	/// </summary>
	private protected virtual void OnClosing() { }

	/// <summary>
	/// Close the window and delete its panels.
	/// </summary>
	public void Dispose()
	{
		if ( Surface is null )
			return;

		// Popups hanging off this window go first. The OS destroys owned windows with their
		// owner, and a swap chain has to be destroyed before its window - never after.
		CloseTooltip();
		OnClosing();
		ReleaseMouseCapture();

		if ( Modal && Handle != IntPtr.Zero ) PanelWindowNative.SetModal( Handle, false );

		foreach ( var child in _all.ToArray() )
		{
			if ( child is PopupWindow popup && popup.Parent == this ) popup.Dispose();
			else if ( child.Owner == this ) child.Dispose();
		}

		_all.Remove( this );
		PanelWindows.Unregister( this );

		Surface?.Dispose();
		Surface = null;

		_camera?.Dispose();
		_camera = null;

		_world?.Delete();
		_world = null;

		var chain = _swapChain;
		var window = Handle;

		_swapChain = default;
		Handle = IntPtr.Zero;

		if ( window == IntPtr.Zero )
			return;

		// Both go at frame end, the swap chain first - destroying it waits for its last present,
		// which needs the window it presented to still there
		EngineLoop.DisposeAtFrameEnd( new Sandbox.Utility.DisposeAction( () =>
		{
			if ( chain != default )
				g_pRenderDevice.DestroySwapChain( chain );

			DestroyNativeWindow( window );
		} ) );
	}

	/// <summary>
	/// The user clicked the window's close button.
	/// </summary>
	public void RequestClose()
	{
		if ( !CanClose )
			return;

		if ( OnCloseRequested is not null )
		{
			try
			{
				if ( !OnCloseRequested() ) return;
			}
			catch ( Exception e )
			{
				// A broken close guard shouldn't leave a window that can't be closed
				Log.Warning( e, e.Message );
			}
		}

		if ( HideOnClose )
		{
			Hide();
			return;
		}

		Dispose();
	}

	/// <summary>
	/// Minimize the window.
	/// </summary>
	public void Minimize()
	{
		if ( Handle != IntPtr.Zero ) PanelWindowNative.Minimize( Handle );
	}

	/// <summary>
	/// Fill the display.
	/// </summary>
	public void Maximize()
	{
		if ( Handle == IntPtr.Zero ) return;
		if ( !CanMaximize ) return;

		PanelWindowNative.Maximize( Handle );
	}

	/// <summary>
	/// Maximize the window, or put it back if it already is.
	/// </summary>
	public void ToggleMaximized()
	{
		if ( Handle == IntPtr.Zero ) return;

		if ( IsMaximized ) PanelWindowNative.Restore( Handle );
		else Maximize();
	}

	/// <summary>
	/// Take the window off screen without closing it. Everything in it stays; <see cref="Show"/>
	/// brings it back where it was.
	/// </summary>
	public void Hide()
	{
		if ( Handle == IntPtr.Zero ) return;
		PanelWindowNative.Hide( Handle );
	}

	/// <summary>
	/// Put a hidden window back on screen.
	/// </summary>
	public void Show()
	{
		if ( Handle == IntPtr.Zero ) return;
		PanelWindowNative.Show( Handle );
	}

	/// <summary>
	/// Put the window behind every other window. Focus stays where it is.
	/// </summary>
	public void SendToBack()
	{
		if ( Handle == IntPtr.Zero ) return;
		PanelWindowNative.SendToBack( Handle );
	}

	/// <summary>
	/// Raise the window above the others without taking focus. <see cref="Focus"/> is the one
	/// that focuses it too.
	/// </summary>
	public void BringToFront()
	{
		if ( Handle == IntPtr.Zero ) return;
		PanelWindowNative.BringToFront( Handle );
	}

	/// <summary>
	/// The icon in the title bar and on the taskbar button. The OS scales it, so 32 or 64 pixels
	/// square is plenty; it's copied, so the bitmap can go afterwards.
	/// </summary>
	public void SetIcon( Bitmap icon )
	{
		if ( Handle == IntPtr.Zero || icon is null ) return;

		var pixels = icon.GetPixels32();

		unsafe
		{
			fixed ( Color32* p = pixels )
			{
				PanelWindowNative.SetIcon( Handle, icon.Width, icon.Height, (IntPtr)p );
			}
		}
	}

	/// <summary>
	/// The window's outer rectangle in desktop coordinates.
	/// </summary>
	Rect DesktopBounds
	{
		get
		{
			PanelWindowNative.GetBounds( Handle, out var x, out var y, out var w, out var h );
			return new Rect( x, y, w, h );
		}
	}

	/// <summary>
	/// Centre the window on its <see cref="Owner"/>, or on the usable part of its display when
	/// it has none. Either way it ends up on screen.
	/// </summary>
	public void MoveToCenter()
	{
		if ( Handle == IntPtr.Zero ) return;

		var area = Owner is { IsOpen: true } owner ? owner.DesktopBounds : DisplayWorkArea;
		var bounds = DesktopBounds;
		Position = area.Position + (area.Size - bounds.Size) * 0.5f;

		SnapToDisplay();
	}

	/// <summary>
	/// Move the window the least distance that puts all of it on the usable part of its display.
	/// A window taller or wider than the display keeps its top left on screen.
	/// </summary>
	public void SnapToDisplay()
	{
		if ( Handle == IntPtr.Zero ) return;

		var area = DisplayWorkArea;
		var bounds = DesktopBounds;

		var x = Math.Max( area.Left, Math.Min( bounds.Left, area.Right - bounds.Width ) );
		var y = Math.Max( area.Top, Math.Min( bounds.Top, area.Bottom - bounds.Height ) );

		if ( x == bounds.Left && y == bounds.Top ) return;
		Position = new Vector2( x, y );
	}

	/// <summary>
	/// Flash the taskbar button to get the user's attention. Briefly by default; until focused
	/// keeps going until they click the window.
	/// </summary>
	public void FlashTaskbar( bool untilFocused = false )
	{
		if ( Handle == IntPtr.Zero ) return;
		PanelWindowNative.Flash( Handle, untilFocused ? 2 : 1 );
	}

	/// <summary>
	/// Stop a <see cref="FlashTaskbar"/> that's still going.
	/// </summary>
	public void StopFlashing()
	{
		if ( Handle == IntPtr.Zero ) return;
		PanelWindowNative.Flash( Handle, 0 );
	}

	/// <summary>
	/// A small icon over the corner of the window's taskbar button - a badge for a count, a
	/// status. 16 pixels square is what the taskbar shows. Null clears it. The description is
	/// what a screen reader says for it.
	/// </summary>
	public void SetOverlayIcon( Bitmap icon, string description = null )
	{
		if ( Handle == IntPtr.Zero ) return;

		if ( icon is null )
		{
			PanelWindowNative.SetOverlayIcon( Handle, 0, 0, IntPtr.Zero, "" );
			return;
		}

		var pixels = icon.GetPixels32();

		unsafe
		{
			fixed ( Color32* p = pixels )
			{
				PanelWindowNative.SetOverlayIcon( Handle, icon.Width, icon.Height, (IntPtr)p, description ?? "" );
			}
		}
	}

	/// <summary>
	/// Bring the window to the front.
	/// </summary>
	public void Focus()
	{
		if ( Handle != IntPtr.Zero ) PanelWindowNative.SetForeground( Handle );
	}
}
