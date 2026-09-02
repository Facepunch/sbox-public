using NativeEngine;

namespace Sandbox.UI;


/// <summary>
/// Queue input events on here to be processed by the UISystem.
/// </summary>
class InputEventQueue
{
	Queue<PanelEvent> PanelEvents = new();
	Queue<ButtonEvent> ButtonEvents = new();
	Queue<string> DoubleClicks = new();
	Queue<string> TripleClicks = new();
	Queue<ButtonEvent> ButtonTyped = new();
	Queue<char> KeyTyped = new();

	Vector2 MouseMovement;

	internal static string NormalizeButtonName( string button )
	{
		button = button.ToLowerInvariant();

		if ( button.StartsWith( "key_" ) )
			button = button[4..];

		return button;
	}

	internal void TickFocused( Panel focused )
	{
		if ( !focused.IsValid() )
		{
			ButtonEvents.Clear();
			KeyTyped.Clear();
			ButtonTyped.Clear();
			PanelEvents.Clear();
			return;
		}

		while ( ButtonEvents.TryDequeue( out var e ) )
		{
			focused.OnButtonEvent( e );
		}

		while ( KeyTyped.TryDequeue( out var e ) )
		{
			focused?.OnKeyTyped( e );
		}

		while ( ButtonTyped.TryDequeue( out var e ) )
		{
			focused?.OnButtonTyped( e );
		}

		while ( PanelEvents.TryDequeue( out var e ) )
		{
			e.Target = focused;
			focused.CreateEvent( e );
		}
	}

	internal void Tick( Panel hovered, Panel active )
	{
		if ( MouseMovement != 0 )
		{
			// If we're pressing down on a panel we send all the mouse move events to that
			var moveRecv = hovered;
			if ( active != null ) moveRecv = active;

			moveRecv?.CreateEvent( new MousePanelEvent( "onmousemove", moveRecv, "none" ) );

			MouseMovement = 0;
		}

		var listSize = DoubleClicks.Count;
		for ( int i = 0; i < listSize; i++ )
			if ( DoubleClicks.TryDequeue( out var e ) )
			{
				hovered?.CreateEvent( new MousePanelEvent( "ondoubleclick", hovered, e ) );
			}

		listSize = TripleClicks.Count;
		for ( int i = 0; i < listSize; i++ )
			if ( TripleClicks.TryDequeue( out var e ) )
			{
				hovered?.CreateEvent( new MousePanelEvent( "ontripleclick", hovered, e ) );
			}
	}

	internal void AddDoubleClick( string button )
	{
		button = NormalizeButtonName( button );
		DoubleClicks.Enqueue( button );
	}

	internal void AddTripleClick( string button )
	{
		button = NormalizeButtonName( button );
		TripleClicks.Enqueue( button );
	}

	internal void QueueInputEvent( PanelEvent e )
	{
		PanelEvents.Enqueue( e );
	}

	internal void AddButtonEvent( ButtonCode button, bool down, KeyboardModifiers modifiers )
	{
		var e = new ButtonEvent( button, down, modifiers );
		ButtonEvents.Enqueue( e );
	}

	internal void AddButtonEvent( string button, bool down, int virtualKey, KeyboardModifiers modifiers )
	{
		ButtonEvents.Enqueue( new ButtonEvent( button, down, virtualKey, modifiers ) );
	}

	internal void AddButtonTyped( string button, int virtualKey, KeyboardModifiers modifiers )
	{
		ButtonTyped.Enqueue( new ButtonEvent( button, true, virtualKey, modifiers ) );
	}

	internal void AddKeyTyped( char c )
	{
		KeyTyped.Enqueue( c );
	}

	internal void AddButtonTyped( ButtonCode button, KeyboardModifiers modifiers )
	{
		if ( AddClipboardShortcut( button, modifiers ) )
			return;

		var e = new ButtonEvent( button, true, modifiers );
		ButtonTyped.Enqueue( e );
	}

	/// <summary>
	/// Ctrl+C, Ctrl+V and Ctrl+X become clipboard events here, so every window gets them the
	/// same way. Equals on purpose - ctrl+shift+c and friends belong to whoever's focused.
	/// </summary>
	bool AddClipboardShortcut( ButtonCode button, KeyboardModifiers modifiers )
	{
		if ( modifiers != KeyboardModifiers.Ctrl )
			return false;

		if ( button == ButtonCode.KEY_C )
		{
			QueueInputEvent( new CopyEvent() );
			return true;
		}

		if ( button == ButtonCode.KEY_X )
		{
			QueueInputEvent( new CutEvent() );
			return true;
		}

		if ( button == ButtonCode.KEY_V )
		{
			if ( NativeEngine.EngineGlobal.SDL_HasClipboardText() )
			{
				var ptr = NativeEngine.EngineGlobal.SDL_GetClipboardText();
				var text = System.Runtime.InteropServices.Marshal.PtrToStringUTF8( ptr );
				NativeEngine.EngineGlobal.SDL_free( ptr );

				if ( !string.IsNullOrEmpty( text ) )
				{
					QueueInputEvent( new PasteEvent( text ) );
				}
			}

			return true;
		}

		return false;
	}

	internal void MouseMoved( Vector2 delta )
	{
		MouseMovement += delta;
	}
}
