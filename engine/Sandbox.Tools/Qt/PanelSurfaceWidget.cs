using Sandbox.Diagnostics;
using Sandbox.UI;
using System;

namespace Editor;

/// <summary>
/// Engine panels drawn inside a Qt widget: a <see cref="UISurface"/> on the widget's own swap chain, with the widget's
/// mouse and keyboard handed to it. For putting panel UI in an editor dock, where a <see cref="SurfaceWindow"/> would be
/// a window of its own. Tooltips and popups draw inside the surface, since there's no panel window to own theirs.
/// </summary>
internal class PanelSurfaceWidget : Frame
{
	static readonly HashSet<PanelSurfaceWidget> All = new();

	/// <summary>
	/// What's drawn. Put panels in <see cref="UISurface.Root"/>.
	/// </summary>
	public UISurface Surface { get; }

	/// <summary>
	/// Draw at most this many times a second, as <see cref="SurfaceWindow.MaxFramesPerSecond"/>. Zero draws every frame.
	/// </summary>
	public float MaxFramesPerSecond { get; set; }

	/// <summary>
	/// Where the widget's frames are counted, as <see cref="SurfaceWindow.FrameTiming"/>.
	/// </summary>
	public PerformanceStats.Timings FrameTiming { get; set; }

	public Color BackgroundColor { get; set; } = Color.Black;

	/// <summary>
	/// Called each time the widget is about to draw, before the surface lays out.
	/// </summary>
	public Action OnPreFrame { get; set; }

	BorealisGpuWindow _borealis;
	bool _winIdChanged;
	RealTimeSince _sinceFrame;
	bool _mouseInside;
	bool _stopped;
	bool _renderFailed;

	public PanelSurfaceWidget( Widget parent = null ) : base( parent )
	{
		// Same as SceneRenderingWidget: a native window that only the swap chain draws into
		SetFlag( Flag.WA_NativeWindow, true );
		SetFlag( Flag.WA_PaintOnScreen, true );
		SetFlag( Flag.WA_NoSystemBackground, true );
		SetFlag( Flag.WA_OpaquePaintEvent, true );
		OnPaintOverride = () => true;

		FocusMode = FocusMode.Click;
		MouseTracking = true;

		Surface = new UISurface();
		Surface.OnCursorChanged = name => Cursor = CursorFor( name );

		// The widget presents on Borealis.Gpu, as a panel window does
		_borealis = BorealisGpuWindow.FromNativeWindow( (BorealisGpuRenderBackend)RenderBackend.Default, _widget.winId(), Size * DpiScale, vsync: false );
		All.Add( this );
	}

	internal override void OnDestroyingLater()
	{
		ReleaseSwapChain();
		base.OnDestroyingLater();
	}

	internal override void NativeDestroying() => ReleaseSwapChain();

	/// <summary>
	/// Qt reset the widget's native window id or gave it one; the next frame looks, as
	/// <see cref="SceneRenderingWidget.OnWinIdChanged"/> does.
	/// </summary>
	internal override void OnWinIdChanged()
	{
		base.OnWinIdChanged();
		_winIdChanged = true;
	}

	/// <summary>Keep the swap chain on the window it presents to, or move it to the widget's new one.</summary>
	bool FollowNativeWindow()
	{
		if ( !_winIdChanged || _stopped ) return _borealis is not null;
		_winIdChanged = false;

		var window = _widget.winId();
		if ( _borealis is not null && _borealis.NativeWindow == window ) return true;

		_borealis?.Dispose();
		_borealis = null;
		_borealis = BorealisGpuWindow.MoveToNativeWindow( (BorealisGpuRenderBackend)RenderBackend.Default, window, Size * DpiScale );
		return _borealis is not null;
	}

	internal override void NativeShutdown()
	{
		base.NativeShutdown();
		All.Remove( this );
		Surface.Dispose();
	}

	void ReleaseSwapChain()
	{
		if ( _stopped ) return;
		_stopped = true;

		// Destroying the swap chain waits for the device: the native window is still alive
		_borealis?.Dispose();
		_borealis = null;
	}

	internal static void ShutdownRendering()
	{
		foreach ( var widget in All.ToArray() )
			widget.ReleaseSwapChain();
	}

	/// <summary>
	/// Draw every visible widget. Called in the render phase, with the scene rendering widgets.
	/// </summary>
	internal static void RenderAll()
	{
		foreach ( var widget in All.ToArray() )
		{
			if ( !widget.IsValid() || !widget.Visible ) continue;

			try
			{
				widget.Render();
			}
			catch ( Exception e )
			{
				if ( widget._renderFailed ) continue;
				widget._renderFailed = true;
				Log.Error( e, $"Exception rendering a panel surface" );
			}
		}
	}

	void Render()
	{
		if ( !FollowNativeWindow() ) return;
		if ( MaxFramesPerSecond > 0 && _sinceFrame < 1f / MaxFramesPerSecond ) return;
		_sinceFrame = 0;

		using var _ = FrameTiming is { } timing ? timing.Scope() : default;

		OnPreFrame?.Invoke();

		// The surface works in pixels, like a window's swap chain
		var dpi = DpiScale;
		var size = Size * dpi;
		if ( size.x < 1 || size.y < 1 ) return;

		Surface.Size = size;
		Surface.DpiScale = dpi;
		Surface.MouseInside = _mouseInside;
		Surface.Simulate();

		// Scene panels queue their render while simulating; fill them in before drawing
		ScenePanel.RenderPending();

		// The whole frame on Borealis.Gpu, as a panel window's
		if ( !_borealis.Update( size ) ) return;
		_borealis.Present( BackgroundColor, true, Surface.Render );
		_renderFailed = false;
	}

	protected override void OnMouseEnter()
	{
		base.OnMouseEnter();
		_mouseInside = true;
	}

	protected override void OnMouseLeave()
	{
		base.OnMouseLeave();
		_mouseInside = false;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		_mouseInside = true;
		Surface.MouseMoved( e.LocalPosition * DpiScale );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		// Clicking away from a menu closes it, as it does in a panel window
		BasePopup.CloseAll( Surface.FindPanelAt( e.LocalPosition * DpiScale ) );
		Surface.MouseMoved( e.LocalPosition * DpiScale );
		Surface.SetMouseButton( e.Button, true, e.KeyboardModifiers );
		e.Accepted = true;
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		Surface.MouseMoved( e.LocalPosition * DpiScale );
		Surface.SetMouseButton( e.Button, false, e.KeyboardModifiers );
		e.Accepted = true;
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		Surface.SetMouseButton( e.Button, true, e.KeyboardModifiers );
		Surface.SetDoubleClick( e.Button );
		e.Accepted = true;
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		// Qt counts 120 a notch; the surface takes notches, as SDL reports them
		Surface.SetMouseWheel( new Vector2( 0, e.Delta / 120f ), e.KeyboardModifiers );
		e.Accept();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		// The same button codes the SDL path sends, so text entry sees enter, escape and the arrows as it expects
		Surface.SetKey( Sandbox.Engine.KeyTranslation.VirtualKeyToButtonCode( (int)e.NativeKeyCode ), true, e.KeyboardModifiers );
		if ( !string.IsNullOrEmpty( e.Text ) && !e.HasCtrl && !e.HasAlt ) Surface.TypeText( e.Text );
		e.Accepted = true;
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		Surface.SetKey( Sandbox.Engine.KeyTranslation.VirtualKeyToButtonCode( (int)e.NativeKeyCode ), false, e.KeyboardModifiers );
		e.Accepted = true;
	}

	/// <summary>
	/// The panels ask for CSS cursors; Qt has shapes.
	/// </summary>
	static CursorShape CursorFor( string name ) => name switch
	{
		"pointer" => CursorShape.Finger,
		"text" => CursorShape.IBeam,
		"row-resize" or "ns-resize" or "n-resize" or "s-resize" => CursorShape.SizeV,
		"col-resize" or "ew-resize" or "e-resize" or "w-resize" => CursorShape.SizeH,
		"nwse-resize" or "nw-resize" or "se-resize" => CursorShape.SizeFDiag,
		"nesw-resize" or "ne-resize" or "sw-resize" => CursorShape.SizeBDiag,
		"move" or "all-scroll" => CursorShape.SizeAll,
		"grab" => CursorShape.OpenHand,
		"grabbing" => CursorShape.ClosedHand,
		"not-allowed" or "no-drop" => CursorShape.Forbidden,
		"wait" => CursorShape.Wait,
		"progress" => CursorShape.Busy,
		"crosshair" => CursorShape.Cross,
		_ => CursorShape.None,
	};
}
