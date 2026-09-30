using Sandbox.Engine;
using System;

namespace Editor;

/// <summary>
/// Registers a widget with the input system to use SDL and manages
/// inputs and focus as it relates to the editor's game widget.
/// </summary>
public static class GameMode
{
	static SceneRenderingWidget _inPlay;
	static IntPtr _playWindow;
	internal static IntPtr PlayWindow { get; private set; }
	internal static SceneRenderingWidget PlayWidget => _inPlay.IsValid() ? _inPlay : null;

	/// <summary>
	/// Is a render widget the active play widget
	/// </summary>
	internal static bool IsPlayWidget( SceneRenderingWidget widget ) => widget == _inPlay;

	/// <summary>
	/// Given a widget, register it for SDL input, and tell the engine this is the swapchain we have
	/// </summary>
	/// <param name="widget"></param>
	public static void SetPlayWidget( SceneRenderingWidget widget )
	{
		if ( _inPlay == widget ) return;

		ClearPlayMode();

		// Blur before registering so SDL's fresh wrapper can't snapshot this widget as its
		// keyboard focus window - relative mouse mode is driven from the main editor window
		widget.Blur();

		widget.Focused += WidgetFocused;
		widget.Blurred += WidgetBlurred;
		widget.MouseTracking = true;
		widget.MouseMove += OnPlayWidgetMouseMove;

		_playWindow = widget._widget.winId();
		NativeEngine.InputSystem.RegisterWindowWithSDL( _playWindow );
		PlayWindow = NativeEngine.GameWindowNative.FromNativeHandle( _playWindow );
		NativeEngine.GameWindowNative.SetRenderTarget( PlayWindow, widget.SwapChain );

		// The play widget is where the game renders, so make it the main window: flip the existing
		// m_bIsMainWindow flag so GetGPUFrameTimeMS reports the running game's GPU frame time.
		g_pRenderDevice.SetSwapChainIsMainWindow( widget.SwapChain, true );

		_inPlay = widget;

		widget.Focus();
	}

	public static void ClearPlayMode()
	{
		if ( _inPlay is null )
			return;

		var widget = _inPlay;
		_inPlay = null;

		widget.Focused -= WidgetFocused;
		widget.Blurred -= WidgetBlurred;
		widget.MouseMove -= OnPlayWidgetMouseMove;
		if ( widget.IsValid() )
		{
			widget.Blur();
			widget.MouseTracking = false;
		}

		// Teardown also runs after Qt destroys the widget, when winId() is no longer safe.
		Sandbox.Engine.WindowInput.OnEditorGameFocusChange( _playWindow, false );
		NativeEngine.GameWindowNative.SetRenderTarget( IntPtr.Zero, default );
		NativeEngine.InputSystem.UnregisterWindowFromSDL( _playWindow );
		_playWindow = default;
		PlayWindow = default;

		g_pRenderDevice.SetSwapChainIsMainWindow( widget.SwapChain, false );
	}

	/// <summary>
	/// When the editor gains focus of the game widget, tell the input system so it'll mouse capture (if it wants to)
	/// </summary>
	private static void WidgetFocused( FocusChangeReason reason )
	{
		if ( _inPlay is null )
			return;

		Sandbox.Engine.WindowInput.OnEditorGameFocusChange( _playWindow, true );
	}

	/// <summary>
	/// When the editor loses focus of the game widget, tell the input system so it stops trying to do mouse capture.
	/// </summary>
	private static void WidgetBlurred( FocusChangeReason reason )
	{
		if ( _inPlay is null )
			return;

		Sandbox.Engine.WindowInput.OnEditorGameFocusChange( _playWindow, false );
	}

	private static void OnPlayWidgetMouseMove( Vector2 local )
	{
		// SDL handles position when the widget is focused; only fill in the gap when unfocused.
		if ( _inPlay is null || _inPlay.IsFocused )
			return;

		var pos = new Vector2( (int)local.x, (int)local.y );
		var delta = pos - InputRouter.MouseCursorPosition;

		InputRouter.OnMousePositionChange( pos.x, pos.y, delta.x, delta.y );
	}
}
