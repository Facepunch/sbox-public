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
	/// Given a widget, register it for SDL input, and tell the engine this is the swapchain we have
	/// </summary>
	/// <param name="widget"></param>
	public static void SetPlayWidget( SceneRenderingWidget widget )
	{
		if ( _inPlay == widget ) return;

		widget.Focused += WidgetFocused;
		widget.Blurred += WidgetBlurred;
		widget.MouseTracking = true;
		widget.MouseMove += OnPlayWidgetMouseMove;

		NativeEngine.InputSystem.RegisterWindowWithSDL( widget._widget.winId() );
		g_pEngineServiceMgr.SetEngineState( widget._widget.winId(), widget.SwapChain );

		_inPlay = widget;

		// Force a full refocus by blurring first
		widget.Blur();
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

		var pos = new Vector2( (int)local.x, (int)local.y );
		var delta = pos - InputRouter.MouseCursorPosition;

		InputRouter.OnMousePositionChange( pos.x, pos.y, delta.x, delta.y );
	}
}
