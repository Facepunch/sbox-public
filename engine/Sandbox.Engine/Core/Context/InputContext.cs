using NativeEngine;
using Sandbox.Internal;
using Sandbox.UI;

namespace Sandbox.Engine;

internal sealed class InputContext
{
	/// <summary>
	/// The name of this context, for debugging purposes
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// What mouse cursor does this context want to show?
	/// </summary>
	public string MouseCursor { get; set; }

	/// <summary>
	/// What kind of mouse interaction is this context interested in right now
	/// </summary>
	public InputState MouseState { get; private set; }

	/// <summary>
	/// Mouse is UI mode but wants to use the mouse capture/delta mode
	/// </summary>
	public bool MouseCapture { get; private set; }

	/// <summary>
	/// What kind of keyboard interaction is this context interested in right now
	/// </summary>
	public InputState KeyboardState { get; private set; }

	HashSet<ButtonCode> _pressed = [];

	/// <summary>
	/// When input type changes, we capture which buttons are down to block text input until they're released
	/// This fixes problems where you hold a key, open a UI, and the held key generates text input
	/// </summary>
	List<ButtonCode> _blockingTextInput = [];

	public Action<Vector2> OnGameMouseWheel { get; set; }

	/// <summary>
	/// Mouse moved in game mode
	/// </summary>
	public Action<Vector2> OnMouseMotion { get; set; }

	/// <summary>
	/// A button event to be sent to the game
	/// </summary>
	public Action<ButtonCode, string, bool> OnGameButton { get; set; }

	public IPanel KeyboardFocusPanel { get; internal set; }
	public IPanel MouseFocusPanel { get; internal set; }

	/// <summary>
	/// Which system should we be sending our input to?
	/// </summary>
	public UISystem TargetUISystem { get; set; }


	static bool IsMouseButton( ButtonCode btton )
	{
		if ( btton < ButtonCode.MOUSE_FIRST ) return false;
		if ( btton > ButtonCode.MOUSE_LAST ) return false;
		return true;
	}

	/// <summary>
	/// When true we've called StartTrapping and are waiting for the user to release keys
	/// </summary>
	bool TrappingKeys { get; set; }
	HashSet<string> TrappedKeys { get; set; }

	static Action<string[]> TrapCallback;

	/// <summary>
	/// Start trapping keys. When the user releases all keys the callback will be called
	/// with a list of buttons that were pressed during the trap.
	/// </summary>
	public void StartTrapping( Action<string[]> callback )
	{
		TrappingKeys = true;
		TrappedKeys = new HashSet<string>();
		TrapCallback = callback;
	}

	/// <summary>
	/// Called when a key is released if we're trapping keys.
	/// </summary>
	void EndTrapping()
	{
		TrappingKeys = false;
		TrapCallback?.Invoke( TrappedKeys.ToArray() );
		TrapCallback = null;
	}

	internal void IN_Text( char input )
	{
		if ( TrappingKeys )
			return;

		// A button is being held that was down when we switched to UI mode
		// so we just block text input until it's released
		if ( _blockingTextInput.Any() )
		{
			//Log.Info( $"In_Text blocked [{input}] {string.Join( ",", _blockingTextInput )}" );
			return;
		}

		TargetUISystem.InputEventQueue.AddKeyTyped( (char)input );
	}

	internal void IN_MouseWheel( Vector2 value, KeyboardModifiers modifiers )
	{
		if ( MouseFocusPanel is not null && MouseFocusPanel.WantsPointerEvents )
		{
			TargetUISystem.Input.AddMouseWheel( value, modifiers );
		}
		else
		{
			OnGameMouseWheel?.Invoke( value );
		}
	}

	public void BlockTextInputUntilButtonsReleased()
	{
		_blockingTextInput.Clear();
		_blockingTextInput.AddRange( _pressed.Where( x => !IsMouseButton( x ) ) );

		// Log.Info( $"BLOCKING TEXT UNTIL UP {string.Join( ",", _blockingTextInput )}" );
	}

	internal void IN_ImeStart()
	{
		TargetUISystem.CurrentFocus?.CreateEvent( "onimestart" );
	}

	internal void IN_ImeEnd()
	{
		TargetUISystem.CurrentFocus?.CreateEvent( "onimeend" );
	}

	internal void IN_ImeComposition( string text, bool final )
	{
		TargetUISystem.CurrentFocus?.CreateEvent( "onime", text );
	}

	internal void In_MousePosition( Vector2 pos, Vector2 delta )
	{
		if ( delta.Length == 0 ) return;

		if ( MouseState == InputState.Game || MouseCapture )
		{
			OnMouseMotion?.Invoke( delta );
			return;
		}

		TargetUISystem.InputEventQueue.MouseMoved( delta );
	}

	/// <summary>
	/// Special handling for the escape button. Return false if we didn't use it.
	/// </summary>
	internal bool In_Escape()
	{
		if ( TrappingKeys )
		{
			EndTrapping();
			return true;
		}

		if ( KeyboardState == InputState.Game )
		{
			TargetUISystem.CurrentFocus?.CreateEvent( "onescape" );
			return true;
		}

		if ( KeyboardFocusPanel is null )
			return false;

		if ( KeyboardFocusPanel.Parent is null )
			return false;

		TargetUISystem.CurrentFocus?.CreateEvent( "onescape" );
		return true;
	}

	internal void ReleaseAllButtons()
	{
		foreach ( var scanButtonCode in _pressed.ToArray() )
		{
			IN_ButtonReleased( scanButtonCode, scanButtonCode, KeyboardModifiers.None );
		}
	}

	/// <summary>
	/// This is called even if the context doesn't have focus.
	/// It's just a place to unpress buttons, if they're down.
	/// </summary>
	internal void IN_ButtonReleased( ButtonCode scanButtonCode, ButtonCode keyButtonCode, KeyboardModifiers modifiers )
	{
		if ( TrappingKeys )
			return;

		_blockingTextInput.Remove( scanButtonCode );

		if ( !_pressed.Remove( scanButtonCode ) )
			return;

		TargetUISystem.InputEventQueue.AddButtonEvent( keyButtonCode, false, modifiers );

		var name = InputSystem.CodeToString( scanButtonCode );
		if ( !string.IsNullOrWhiteSpace( name ) )
		{
			OnGameButton?.Invoke( scanButtonCode, name, false );
		}
	}

	internal void IN_Button( bool pressed, ButtonCode scanButtonCode, ButtonCode keyButtonCode, bool repeat, KeyboardModifiers modifiers )
	{
		if ( TrappingKeys )
		{
			var name = InputSystem.CodeToString( scanButtonCode );
			if ( !string.IsNullOrWhiteSpace( name ) )
			{
				TrappedKeys.Add( name );
			}

			if ( !pressed )
			{
				EndTrapping();
			}

			return;
		}

		if ( IsMouseButton( scanButtonCode ) )
		{
			OnMouseButton( scanButtonCode, pressed, modifiers );
			return;
		}

		if ( pressed && KeyboardState == InputState.UI )
		{
			TargetUISystem.InputEventQueue.AddButtonTyped( keyButtonCode, modifiers );
		}

		// We reserve some buttons that we handle ourselves, like function keys and ESCAPE.
		if ( IsReservedButton( scanButtonCode ) )
			return;

		OnButton( scanButtonCode, keyButtonCode, pressed, repeat, modifiers );
	}

	// cleanme
	internal Vector2 lastClickPos;
	internal RealTimeSince timeSinceClick;
	internal int clickCounter;

	void OnMouseButton( ButtonCode button, bool pressed, KeyboardModifiers modifiers )
	{
		var gameToo = MouseFocusPanel?.ButtonInput == PanelInputType.Game;
		if ( MouseFocusPanel is null || !MouseFocusPanel.WantsPointerEvents ) gameToo = true;

		if ( MouseState == InputState.Ignore )
			return;

		if ( MouseState == InputState.Game || gameToo || !pressed )
		{
			var name = InputSystem.CodeToString( button );
			if ( !string.IsNullOrWhiteSpace( name ) )
			{
				OnGameButton?.Invoke( button, name, pressed );
			}
		}

		// Slightly dodgy double/triple click handling
		// lets hope no-one notices you can click with left and then right to double click
		if ( button != ButtonCode.MouseWheelDown && button != ButtonCode.MouseWheelUp )
		{
			var clickDelta = lastClickPos - InputRouter.MouseCursorPosition;
			lastClickPos = InputRouter.MouseCursorPosition;

			if ( timeSinceClick > 0.4f || clickDelta.Length > 5.0f )
			{
				clickCounter = 0;
			}

			if ( !pressed )
				clickCounter++;

			timeSinceClick = 0;

			if ( !pressed && clickCounter == 2 )
			{
				//Log.Info( "Double Click" );
				TargetUISystem.InputEventQueue.AddDoubleClick( button.ToString() );
			}

			if ( !pressed && clickCounter == 3 )
			{
				//Log.Info( "Triple Click" );
				//OnTripleClick?.Invoke( button.ToString() );
			}
		}

		if ( button == ButtonCode.MouseWheelDown || button == ButtonCode.MouseWheelUp )
		{
			//	OnMouseWheel?.Invoke( button == ButtonCode.MouseWheelDown ? 1 : -1 );
			return;
		}

		if ( _pressed.Contains( button ) == pressed )
			return;

		if ( pressed ) _pressed.Add( button );
		else _pressed.Remove( button );

		if ( MouseState == InputState.UI )
		{
			if ( pressed )
			{
				TargetUISystem.InputEventQueue.AddButtonTyped( button, modifiers );
			}

			TargetUISystem.Input.AddMouseButton( button, pressed, modifiers );
		}
	}

	/// <summary>
	/// Is this a reserved button? This means developers can not detect these keys as up or down.
	/// </summary>
	/// <param name="button"></param>
	/// <returns></returns>
	bool IsReservedButton( ButtonCode button )
	{
		return button switch
		{
			>= ButtonCode.KEY_F1 and <= ButtonCode.KEY_F12 => true,
			ButtonCode.KEY_ESCAPE => true,
			_ => false
		};
	}

	void OnButton( ButtonCode scanButtonCode, ButtonCode keyButtonCode, bool down, bool repeat, KeyboardModifiers modifiers )
	{
		// not right now
		if ( repeat ) return;

		// equals is on purpose -
		// we only want this if they don't have shift and alt etc
		if ( modifiers == KeyboardModifiers.Ctrl && KeyboardState == InputState.UI )
		{
			if ( keyButtonCode == ButtonCode.KEY_C )
			{
				if ( !down ) return;
				TargetUISystem.InputEventQueue.QueueInputEvent( new CopyEvent() );
				return;
			}

			if ( keyButtonCode == ButtonCode.KEY_V )
			{
				if ( !down ) return;

				if ( EngineGlobal.Plat_HasClipboardText() )
				{
					TargetUISystem.InputEventQueue.QueueInputEvent( new PasteEvent( EngineGlobal.Plat_GetClipboardText() ) );
				}

				return;
			}

			if ( keyButtonCode == ButtonCode.KEY_X )
			{
				if ( !down ) return;
				TargetUISystem.InputEventQueue.QueueInputEvent( new CutEvent() );
				return;
			}
		}

		// always allow the actions to "release" when UI pops up,
		// but don't allow new presses
		if ( KeyboardState == InputState.Game || !down )
		{
			var name = InputSystem.CodeToString( scanButtonCode );
			if ( !string.IsNullOrWhiteSpace( name ) )
			{
				OnGameButton?.Invoke( scanButtonCode, name, down );
			}
		}

		if ( _pressed.Contains( scanButtonCode ) == down )
			return;

		if ( down ) _pressed.Add( scanButtonCode );
		else
		{
			_pressed.Remove( scanButtonCode );
			_blockingTextInput.Remove( scanButtonCode );
		}

		if ( KeyboardState == InputState.UI || !down )
		{
			TargetUISystem.InputEventQueue.AddButtonEvent( keyButtonCode, down, modifiers );
		}
	}

	internal void UpdateInputFromUI( InputState mouseState, IPanel mouseFocus, bool mouseCapture, InputState keyboardState, IPanel keyboardFocus )
	{
		MouseFocusPanel = mouseFocus;
		MouseState = mouseState;
		MouseCapture = mouseCapture;

		if ( KeyboardFocusPanel != keyboardFocus || KeyboardState != keyboardState )
		{
			BlockTextInputUntilButtonsReleased();
			KeyboardFocusPanel = keyboardFocus;
			KeyboardState = keyboardState;
		}
	}

	internal void SetMousePosition( Vector2 vector2 )
	{
		InputRouter.SetCursorPosition( this, vector2 );
	}

	public enum InputState
	{
		/// <summary>
		/// Doesn't want it, pass down to next context
		/// </summary>
		Ignore,

		/// <summary>
		/// Interacting with UI
		/// </summary>
		UI,

		/// <summary>
		/// Interacting with the game
		/// </summary>
		Game
	}

}
