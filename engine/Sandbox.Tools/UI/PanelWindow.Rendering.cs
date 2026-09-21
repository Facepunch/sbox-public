using NativeEngine;
using Sandbox.UI;
using System;

namespace Editor;

public partial class PanelWindow
{
	bool _inFrame;

	/// <summary>
	/// Whether the OS window is on screen. A window is born hidden and appears once it has drawn
	/// its contents, so this is false for its first frame or two.
	/// </summary>
	public bool IsShown { get; private protected set; }

	/// <summary>
	/// Resize the window to fit the first thing on the surface. Popups use this so a menu is
	/// exactly as big as its contents, however far outside the parent window that ends up.
	/// </summary>
	public bool SizeToContents { get; set; }

	/// <summary>
	/// Simulate and draw. Called once a frame by the engine loop, and again from resize events
	/// while a drag has the main thread parked in a modal loop.
	/// </summary>
	bool IPanelWindow.Frame() => Frame();

	internal bool Frame()
	{
		// SDL mode changes can send resize events before the transition has completed.
		if ( _applyingFullscreen ) return false;
		if ( Handle == IntPtr.Zero )
		{
			try
			{
				if ( Surface is null || !CreateNativeWindow() ) return false;
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		if ( IsMinimized ) return false;

		// Resize events land mid-frame during a drag, and we draw from those too
		if ( _inFrame && !AllowNestedFrame ) return false;

		var wasInFrame = _inFrame;
		_inFrame = true;

		try
		{
			if ( SimulateFrame() )
			{
				// Scene panels queue their render during simulate - fill them in before we draw,
				// otherwise a panel that just resized draws a texture with nothing in it yet
				ScenePanel.RenderPending();

				DrawFrame();
				return true;
			}
		}
		finally
		{
			_inFrame = wasInFrame;
		}

		return false;
	}

	/// <summary>
	/// Let frames run inside a frame that's already running - see IPanelWindow.
	/// </summary>
	public bool AllowNestedFrame { get; set; }

	/// <summary>
	/// Whether the panel with the mouse captured is one of ours. While it is, the OS cursor is
	/// hidden and pinned and <see cref="Mouse.Delta"/> reports the movement.
	/// </summary>
	bool HasMouseCapture => Panel.MouseCapture is { } captured && captured.FindRootPanel() == Surface?.Root;

	void UpdateMouseCapture()
	{
		if ( HasMouseCapture != (PanelWindows.CaptureWindow == this) )
			PanelWindowInput.SetRelativeMouse( this, HasMouseCapture );
	}

	/// <summary>
	/// Drop a capture held by one of our panels. The window is going away or losing focus, and
	/// a capture that outlives either leaves the cursor hidden with nothing to release it.
	/// </summary>
	void ReleaseMouseCapture()
	{
		if ( HasMouseCapture ) Panel.MouseCapture.SetMouseCapture( false );
		if ( PanelWindows.CaptureWindow == this ) PanelWindowInput.SetRelativeMouse( this, false );
	}

	/// <summary>
	/// Tick, input and layout. Returns false if there is nothing to draw afterwards.
	/// </summary>
	bool SimulateFrame()
	{
		if ( !ApplyFullscreen() || Window is null || !Window.UpdateSwapChain() ) return false;

		UpdateMouseCapture();

		// Lay out and render at the actual swapchain size, including while a resize is pending.
		Surface.Size = Window.SwapChainSize;

		// A window that sizes to its contents has nothing to show until it has some - drawing
		// now would put an empty window on the screen at whatever size it happens to be
		if ( SizeToContents && Surface.Root.ChildrenCount == 0 )
			return false;

		if ( FollowsDisplayScale )
		{
			var scale = DisplayScale;

			// Dragged onto a display that scales differently - the limits the OS is holding were
			// worked out in the old display's units
			if ( scale != Surface.DpiScale )
			{
				Surface.DpiScale = scale;
				ApplySizeLimits();
			}
		}

		Surface.MouseInside = _mouseInside;
		Surface.MouseMoved( _mousePosition );

		Surface.Simulate();

		// Panels can close the window from an event - if that happened there's nothing to draw
		if ( Handle == IntPtr.Zero )
			return false;

		UpdateImeArea();

		// Resizing to fit makes this frame a write-off, we draw on the next one
		if ( SizeToContents && FitToContents() )
			return false;

		return true;
	}

	Rect _imeArea;

	/// <summary>
	/// Tell the OS where text is being typed in this window, so the IME candidate window sits
	/// next to the caret instead of on top of it.
	/// </summary>
	void UpdateImeArea()
	{
		if ( Surface.Focus is not { } focus ) return;

		// Surface pixels to window coordinates
		var rect = focus.ImeCaretRect;
		rect = new Rect( PixelsToWindow( rect.Position ), PixelsToWindow( rect.Size ) );

		if ( rect == _imeArea ) return;
		_imeArea = rect;

		var area = new Sdl.Rect { X = (int)rect.Left, Y = (int)rect.Top, Width = (int)rect.Width, Height = (int)rect.Height };
		Sdl.SetTextInputArea( Handle, ref area, 0 );
	}

	void DrawFrame()
	{
		_camera.AddToRenderList( Window.SwapChain, Surface.Size );

		Window.Present();

		// A window is created hidden so the user never sees it blank at the wrong size - the
		// first drawn frame is when it appears. Anything asked of it before now, like being
		// maximized, is applied by SDL as it's shown.
		if ( !IsShown )
		{
			OnFirstShow();
			IsShown = Window.Show();
		}

		ApplyCursorShape();
	}

	/// <summary>
	/// Whether the window follows its display's scale as it's dragged between displays.
	/// </summary>
	private protected virtual bool FollowsDisplayScale => true;

	/// <summary>
	/// The window has drawn its first frame and is about to be shown.
	/// </summary>
	private protected virtual void OnFirstShow() { }

	/// <summary>
	/// Shrink the window to whatever is on the surface. Returns true if we resized, in which case
	/// this frame is a write-off and we draw on the next one.
	/// </summary>
	bool FitToContents()
	{
		if ( Surface.Root.ChildrenCount == 0 ) return false;

		var content = Surface.Root.GetChild( 0 ).Box.Rect.Size;
		if ( content.x < 1 || content.y < 1 ) return false;

		// Round up - a window a fraction short clips what's in it
		var wanted = PixelsToWindow( new Vector2( MathF.Ceiling( content.x ), MathF.Ceiling( content.y ) ) );

		var width = (int)MathF.Ceiling( wanted.x );
		var height = (int)MathF.Ceiling( wanted.y );

		// Compared in window coordinates - they're the only sizes a window can take, and where one
		// is worth more than a pixel, comparing pixels never settles
		Sdl.GetWindowSize( Handle, out var currentWidth, out var currentHeight );
		if ( width == currentWidth && height == currentHeight ) return false;

		Sdl.SetWindowSize( Handle, width, height );

		return true;
	}
}
