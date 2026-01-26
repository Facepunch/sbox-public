using NativeEngine;

namespace Sandbox.Engine;

internal static partial class InputRouter
{
	static RealTimeSince timeSinceWindowActive;

	internal static void OnMouseButton( ButtonCode button, bool down, int ikeymods )
	{
		SetButtonState( button, down );

		var mouse = Contexts.FirstOrDefault( x => x.MouseState != InputContext.InputState.Ignore );

		// if this was likely the click that made the window active - and we're not in UI mode
		// then ignore it.. because we don't want people shooting guns every time they re-activate
		// the window
		if ( down && timeSinceWindowActive < 0.1f )
		{
			if ( mouse is null || mouse.MouseState != InputContext.InputState.UI )
				return;
		}

		var modifiers = GetCurrentModifiers();

		if ( mouse is not null )
		{
			mouse.IN_Button( down, button, false, modifiers );
		}

		//
		// When a button is released, we send it to each context. These contexts may have
		// the button as pressed. The release should be sent to them so they can clear it.
		//
		if ( !down )
		{
			foreach ( var context in Contexts )
			{
				if ( context == mouse ) continue;

				context.IN_ButtonReleased( button, modifiers );
			}
		}
	}

	internal static void OnWindowActive( bool active )
	{
		if ( active )
		{
			timeSinceWindowActive = 0;
		}
		else
		{
			//
			// Reset state and do button release events
			//
			PressedButtons.Clear();

			foreach ( var context in Contexts )
			{
				context.ReleaseAllButtons();
			}
		}
	}

	/// <summary>
	/// Cursor is hidden and restricted to window (game mode) but the mouse has been moved
	/// </summary>
	internal static void OnMouseMotion( float dx, float dy )
	{
		var delta = new Vector2( dx, dy );

		MouseCursorDelta += delta;
		MouseCursorPosition += delta;

		// game or mouse capture
		var mouse = Contexts.FirstOrDefault( x => x.MouseState != InputContext.InputState.Ignore );
		if ( mouse is not null )
		{
			mouse.In_MousePosition( MouseCursorPosition, delta );
			return;
		}
	}

	/// <summary>
	/// Cursor is visible and mouse is being moved
	/// </summary>
	internal static void OnMousePositionChange( float x, float y, float dx, float dy )
	{
		var delta = new Vector2( 0, 0 );

		// if we're not in relative mode - take the delta from this
		if ( !NativeEngine.InputSystem.GetRelativeMouseMode() )
		{
			delta = new Vector2( dx, dy );
			MouseCursorDelta += delta;
		}

		MouseCursorPosition = new Vector2( x, y );

		// if this is set, we're in capture mode - so just update the position
		// which will update the position of the cursor when we come out of it
		if ( mouseCapturePosition is not null )
		{
			mouseCapturePosition = MouseCursorPosition;
			return;
		}

		var mouse = Contexts.FirstOrDefault( x => x.MouseState != InputContext.InputState.Ignore );
		if ( mouse is not null )
		{
			mouse.In_MousePosition( MouseCursorPosition, delta );
		}
	}

	internal static void OnGameControllerButton( int deviceId, GameControllerCode button, bool down )
	{
		if ( button == GameControllerCode.Start )
		{
			OnEscapePressed( down );
			return;
		}

		OnGamepadCode( deviceId, button.ToGamepadCode(), down );
	}

	/// <summary>
	/// The escape, or start button has been pressed
	/// </summary>
	private static void OnEscapePressed( bool down )
	{
		EscapeIsDown = down;

		if ( down )
		{
			EscapeWasPressed = true;
			TimeSinceEscapePressed = 0;

			// Any focused UI? Let that swallow the escape button. This is for things like escaping from text areas
			var escapeTarget = Contexts.FirstOrDefault( x => x.KeyboardState != InputContext.InputState.Ignore && x.KeyboardFocusPanel is not null );
			if ( escapeTarget is not null && escapeTarget.In_Escape() )
			{
				return;
			}

			// Let the game input get first dibs
			if ( IGameInstance.Current is not null && IGameInstanceDll.Current.InputContext.In_Escape() )
			{
				return;
			}
		}
	}

	private static void OnGamepadCode( int deviceId, GamepadCode code, bool down )
	{
		var controller = Controller.All.FirstOrDefault( x => x.DeviceId == deviceId );
		Log.Trace( $"OnGameControllerButton {controller} - {code}, {down}" );

		Sandbox.Input.UsingController = true;

		SetButtonState( code, down );

		foreach ( var action in Sandbox.Input.InputActions.Where( x => x.GamepadCode != GamepadCode.None && x.GamepadCode == code ) )
		{
			var i = Sandbox.Input.GetActionIndex( action );
			foreach ( var e in Sandbox.Input.Contexts )
			{
				if ( down )
				{
					e.AccumActionsPressed |= 1UL << i;
				}
				else
				{
					e.AccumActionsReleased |= 1UL << i;
				}
			}
		}
	}

	internal static void OnGameControllerAxis( int deviceId, GameControllerAxis axis, int value )
	{
		var controller = Controller.All.FirstOrDefault( x => x.DeviceId == deviceId );
		Log.Trace( $"OnGameControllerButton {controller} - {axis}, {value}" );

		if ( controller is null )
			return;

		controller.SetAxis( axis, value );

		//// We're going to check our triggers and convert them into a virtual button, for ease of use
		//// (and backwards compatibility with SteamInput)

		//// We don't care about non triggers
		if ( axis < GameControllerAxis.TriggerLeft )
			return;

		const float triggerDeadzone = 0.75f;

		// I hate this but okay
		GamepadCode code = axis switch
		{
			GameControllerAxis.TriggerLeft => GamepadCode.LeftTrigger,
			GameControllerAxis.TriggerRight => GamepadCode.RightTrigger,
			_ => GamepadCode.None,
		};

		OnGamepadCode( deviceId, code, value >= triggerDeadzone );
	}

	internal static void OnGameControllerConnected( int joystickId, int deviceId )
	{
		var controller = new Controller( joystickId, deviceId );
		Log.Info( $"New {controller} controller detected" );

		Controller.All.Add( controller );
	}

	internal static void OnGameControllerDisconnected( int joystickId )
	{
		var controller = Controller.All.FirstOrDefault( x => x.SDLHandle == joystickId );
		if ( controller is not null )
		{
			Log.Info( $"{controller} controller removed" );
			Controller.All.Remove( controller );
		}
		else
		{
			Log.Warning( $"Couldn't find Controller instance with {joystickId}" );
		}
	}

	internal static void OnKey( ButtonCode button, bool down, bool repeat, int ikeymods, int vkcode )
	{
		if ( !repeat )
		{
			SetButtonState( button, down );
		}

		var modifiers = GetCurrentModifiers();

		if ( button == ButtonCode.KEY_ESCAPE )
		{
			if ( repeat )
				return;

			OnEscapePressed( down );
			return;
		}

		//
		// Function keys
		//
		if ( button >= ButtonCode.KEY_F1 && button <= ButtonCode.KEY_F12 )
		{
			if ( !down || repeat ) return;

			IToolsDll.Current?.OnFunctionKey( button, modifiers );

			var bind = g_pInputService.GetBinding( button );
			if ( string.IsNullOrEmpty( bind ) ) return;

			ConVarSystem.Run( bind );
			return;
		}

		//
		// Console
		//
		if ( button == ButtonCode.KEY_BACKQUOTE || button == ButtonCode.KEY_TILDE )
		{
			if ( !down || repeat ) return;

			if ( Engine.IToolsDll.Current?.ConsoleFocus() ?? false )
				return;

			ConVarSystem.Run( "con_toggle" );
			return;
		}

		var keyboard = Contexts.FirstOrDefault( x => x.KeyboardState != InputContext.InputState.Ignore );
		if ( keyboard is not null )
		{
			keyboard.IN_Button( down, button, repeat, modifiers );
		}

		//
		// When a button is released, we send it to each context. These contexts may have
		// the button as pressed. The release should be sent to them so they can clear it.
		//
		if ( !down )
		{
			foreach ( var context in Contexts )
			{
				if ( context == keyboard ) continue;

				context.IN_ButtonReleased( button, modifiers );
			}
		}
	}

	internal static void OnText( uint key )
	{
		var keyboard = Contexts.FirstOrDefault( x => x.KeyboardState == InputContext.InputState.UI );
		if ( keyboard is not null )
		{
			keyboard.IN_Text( (char)key );
		}
	}

	internal static void OnMouseWheel( int x, int y, int ikeymods )
	{
		var value = new Vector2( x, y );
		var mouse = Contexts.FirstOrDefault( x => x.MouseState != InputContext.InputState.Ignore );
		var modifiers = GetCurrentModifiers();

		if ( mouse is not null )
		{
			if ( y < 0 )
			{
				mouse.IN_Button( true, ButtonCode.MouseWheelDown, false, default );
				mouse.IN_Button( false, ButtonCode.MouseWheelDown, false, default );
			}
			else
			{
				mouse.IN_Button( true, ButtonCode.MouseWheelUp, false, default );
				mouse.IN_Button( false, ButtonCode.MouseWheelUp, false, default );
			}

			mouse.IN_MouseWheel( value, modifiers );
		}
	}

	internal static void OnImeStart()
	{
		var keyboard = Contexts.FirstOrDefault( x => x.KeyboardState != InputContext.InputState.Ignore );
		if ( keyboard is not null )
		{
			keyboard.IN_ImeStart();
		}
	}

	internal static void OnImeComposition( string text, bool final )
	{
		var keyboard = Contexts.FirstOrDefault( x => x.KeyboardState != InputContext.InputState.Ignore );
		if ( keyboard is not null )
		{
			keyboard.IN_ImeComposition( text, final );
		}
	}

	internal static void OnImeEnd()
	{
		var keyboard = Contexts.FirstOrDefault( x => x.KeyboardState != InputContext.InputState.Ignore );
		if ( keyboard is not null )
		{
			keyboard.IN_ImeEnd();
		}
	}

	/// <summary>
	/// Convert engine (IE_ShiftPressed etc) to our KeyboardModifiers enum
	/// </summary>
	static KeyboardModifiers EngineToModifier( int engine )
	{
		KeyboardModifiers m = KeyboardModifiers.None;

		if ( (engine & 1) == 1 ) m |= KeyboardModifiers.Shift;
		if ( (engine & 2) == 2 ) m |= KeyboardModifiers.Ctrl;
		if ( (engine & 4) == 4 ) m |= KeyboardModifiers.Alt;
		//if ( (m_nData2 & 8) == 8 ) m |= KeyboardModifiers.Windows;
		//if ( (m_nData2 & 16) == 8 ) m |= KeyboardModifiers.Finger;

		return m;
	}

	/// <summary>
	/// Get the current modifier key state by checking the actual button state directly.
	/// </summary>
	static KeyboardModifiers GetCurrentModifiers()
	{
		KeyboardModifiers m = KeyboardModifiers.None;

		if ( IsButtonDown( ButtonCode.KEY_LSHIFT ) || IsButtonDown( ButtonCode.KEY_RSHIFT ) )
			m |= KeyboardModifiers.Shift;

		if ( IsButtonDown( ButtonCode.KEY_LCONTROL ) || IsButtonDown( ButtonCode.KEY_RCONTROL ) )
			m |= KeyboardModifiers.Ctrl;

		if ( IsButtonDown( ButtonCode.KEY_LALT ) || IsButtonDown( ButtonCode.KEY_RALT ) )
			m |= KeyboardModifiers.Alt;

		return m;
	}
}
