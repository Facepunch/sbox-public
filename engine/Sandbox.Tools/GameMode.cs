using Sandbox.Engine;

namespace Editor;

/// <summary>
/// Registers a widget with the input system to use SDL and manages
/// inputs and focus as it relates to the editor's game widget.
/// </summary>
public static class GameMode
{
	static Widget _inPlay;

	/// <summary>
	/// Is a render widget the active play widget
	/// </summary>
	internal static bool IsPlayWidget( SceneRenderingWidget widget ) => widget == _inPlay;

	/// <summary>
	/// True while the play widget (the host's game view) has focus. Used by docked client
	/// tabs to know when the player has clicked back into the host's game.
	/// </summary>
	internal static bool PlayWidgetFocused => _inPlay?.IsFocused ?? false;

	/// <summary>
	/// Bind the engine's window state (what ties SDL input routing to a window) to the
	/// given window/swapchain. Docked clients borrow it while they hold input focus.
	/// </summary>
	internal static void SetEngineStateWindow( nint winId, SwapChainHandle_t swapChain )
	{
		g_pEngineServiceMgr.SetEngineState( winId, swapChain );
	}

	/// <summary>
	/// Point the engine's window state and game input focus back at the play widget, when
	/// a docked client releases input focus. Re-asserting the focus flag matters: the
	/// released tab's "focus off" would otherwise clobber the play widget's earlier
	/// "focus on", leaving host input dead until an OS focus cycle.
	/// </summary>
	internal static void RestoreEngineState()
	{
		if ( _inPlay is SceneRenderingWidget playWidget && playWidget.IsValid() && playWidget._widget.IsValid )
		{
			g_pEngineServiceMgr.SetEngineState( playWidget._widget.winId(), playWidget.SwapChain );
			NativeEngine.InputSystem.OnEditorGameFocusChange( playWidget._widget.winId(), true );
		}
	}

	/// <summary>
	/// Given a widget, register it for SDL input, and tell the engine this is the swapchain we have
	/// </summary>
	/// <param name="widget"></param>
	public static void SetPlayWidget( SceneRenderingWidget widget )
	{
		if ( _inPlay == widget ) return;

		// Blur before registering so SDL's fresh wrapper can't snapshot this widget as its
		// keyboard focus window - relative mouse mode is driven from the main editor window
		widget.Blur();

		widget.Focused += WidgetFocused;
		widget.Blurred += WidgetBlurred;
		widget.MouseTracking = true;
		widget.MouseMove += OnPlayWidgetMouseMove;

		NativeEngine.InputSystem.RegisterWindowWithSDL( widget._widget.winId() );
		g_pEngineServiceMgr.SetEngineState( widget._widget.winId(), widget.SwapChain );

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

		_inPlay.Blur();

		_inPlay.Focused -= WidgetFocused;
		_inPlay.Blurred -= WidgetBlurred;
		_inPlay.MouseMove -= OnPlayWidgetMouseMove;
		_inPlay.MouseTracking = false;

		NativeEngine.InputSystem.UnregisterWindowFromSDL( _inPlay._widget.winId() );

		if ( _inPlay is SceneRenderingWidget playWidget )
			g_pRenderDevice.SetSwapChainIsMainWindow( playWidget.SwapChain, false );

		_inPlay = null;
	}

	/// <summary>
	/// When the editor gains focus of the game widget, tell the input system so it'll mouse capture (if it wants to)
	/// </summary>
	private static void WidgetFocused( FocusChangeReason reason )
	{
		if ( _inPlay is null )
			return;

		NativeEngine.InputSystem.OnEditorGameFocusChange( _inPlay._widget.winId(), true );
	}

	/// <summary>
	/// When the editor loses focus of the game widget, tell the input system so it stops trying to do mouse capture.
	/// </summary>
	private static void WidgetBlurred( FocusChangeReason reason )
	{
		if ( _inPlay is null )
			return;

		NativeEngine.InputSystem.OnEditorGameFocusChange( _inPlay._widget.winId(), false );
	}

	private static void OnPlayWidgetMouseMove( Vector2 local )
	{
		// SDL handles position when the widget is focused; only fill in the gap when unfocused.
		if ( _inPlay is null || _inPlay.IsFocused )
			return;

		// While a docked client holds input focus the router's cursor is in THAT client's
		// window space - injecting play-widget coordinates would hover its UI from here.
		if ( Sandbox.InProcessClientSession.Focused is not null )
			return;

		var pos = new Vector2( (int)local.x, (int)local.y );
		var delta = pos - InputRouter.MouseCursorPosition;

		InputRouter.OnMousePositionChange( pos.x, pos.y, delta.x, delta.y );
	}
}
