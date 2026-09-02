using NativeEngine;
using Sandbox.UI;
using System;

namespace Editor;

public sealed partial class PanelWindow
{
	Vector2 _swapChainSize;
	bool _inFrame;

	// Popups are born hidden and appear once there's something to see - see DrawFrame
	bool _shown = true;

	/// <summary>
	/// Resize the window to fit the first thing on the surface. Popups use this so a menu is
	/// exactly as big as its contents, however far outside the parent window that ends up.
	/// </summary>
	public bool SizeToContents { get; set; }

	/// <summary>
	/// Simulate and draw. Called once a frame by the engine loop, and again from resize events
	/// while a drag has the main thread parked in a modal loop.
	/// </summary>
	bool IPanelWindow.Frame( bool interactiveResize ) => Frame( interactiveResize );

	internal bool Frame( bool interactiveResize = false )
	{
		if ( _window == IntPtr.Zero )
		{
			if ( !_isPopup || Surface is null ) return false;

			CreateNativeWindow();
		}

		if ( PanelWindowNative.IsMinimized( _window ) ) return false;

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
	/// Tick, input, layout. Returns false if there's nothing to draw afterwards.
	/// </summary>
	bool SimulateFrame()
	{
		var size = Size;
		if ( size.x < 1 || size.y < 1 ) return false;

		if ( size != _swapChainSize )
			PanelWindowNative.ResizeSwapChain( _swapChain, (int)size.x, (int)size.y );

		// The swap chain is the canvas - lay out and render at whatever size it really is, so
		// the whole buffer gets painted even when a resize came through another path
		PanelWindowNative.GetSwapChainSize( _swapChain, out var chainWidth, out var chainHeight );
		if ( chainWidth > 0 && chainHeight > 0 ) _swapChainSize = new Vector2( chainWidth, chainHeight );

		Surface.Size = _swapChainSize;

		if ( !_isPopup )
			Surface.DpiScale = PanelWindowNative.GetContentsScale( _window );

		Surface.MouseInside = _mouseInside;
		Surface.MouseMoved( _mousePosition );

		Surface.Simulate();

		// Panels can close the window from an event - if that happened there's nothing to draw
		if ( _window == IntPtr.Zero )
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

		var scale = Surface.DpiScale;
		if ( scale <= 0 ) scale = 1;

		// Surface pixels to window points
		var rect = focus.ImeCaretRect;
		rect = new Rect( rect.Left / scale, rect.Top / scale, rect.Width / scale, rect.Height / scale );

		if ( rect == _imeArea ) return;
		_imeArea = rect;

		PanelWindowNative.SetTextInputArea( _window, (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height );
	}

	void DrawFrame()
	{
		_camera.OnRenderUI = Surface.Render;
		_camera.AddToRenderList( _swapChain, _swapChainSize );

		g_pRenderDevice.Present( _swapChain );

		// A popup is created hidden so the user never sees it blank at the wrong size - the
		// first drawn frame is when it appears
		if ( !_shown )
		{
			_shown = true;
			PanelWindowNative.Show( _window );
		}

		ApplyCursorShape();
	}

	/// <summary>
	/// Shrink the window to whatever is on the surface. Returns true if we resized, in which case
	/// this frame is a write-off and we draw on the next one.
	/// </summary>
	bool FitToContents()
	{
		if ( Surface.Root.ChildrenCount == 0 ) return false;

		var content = Surface.Root.GetChild( 0 ).Box.Rect.Size;
		if ( content.x < 1 || content.y < 1 ) return false;

		var wanted = new Vector2( MathF.Ceiling( content.x ), MathF.Ceiling( content.y ) );
		if ( (wanted - Surface.Size).Length < 1.0f ) return false;

		var scale = Surface.DpiScale;
		PanelWindowNative.SetSize( _window, (int)(wanted.x / scale), (int)(wanted.y / scale) );

		return true;
	}
}
