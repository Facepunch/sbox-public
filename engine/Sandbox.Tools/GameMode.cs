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
	/// Given a widget, register it for SDL input, and tell the engine this is the swapchain we have
	/// </summary>
	/// <param name="widget"></param>
	public static void SetPlayWidget( SceneRenderingWidget widget )
	{
		if ( _inPlay == widget ) return;

		widget.Focused += WidgetFocused;
		widget.Blurred += WidgetBlurred;

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

	internal static void Tick()
	{
		// Keep game mouse position updated while the play widget is unfocused.
		var playWidget = _inPlay;
		if ( playWidget is null || playWidget.IsFocused )
			return;

		var local = playWidget.FromScreen( Application.CursorPosition );

		// Don't snap to borders if cursor isn't over the widget.
		if ( local.x < 0 || local.y < 0 || local.x >= playWidget.Size.x || local.y >= playWidget.Size.y )
			return;

		var pos = new Vector2( (int)local.x, (int)local.y );
		var delta = pos - InputRouter.MouseCursorPosition;

		InputRouter.ApplyAbsoluteMousePosition( pos, delta );
	}
}
