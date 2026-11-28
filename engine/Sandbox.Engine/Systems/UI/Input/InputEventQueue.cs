using NativeEngine;
using System.Runtime.CompilerServices;

namespace Sandbox.UI;


/// <summary>
/// Queue input events on here to be processed by the UISystem.
/// </summary>
class InputEventQueue
{
	Queue<PanelEvent> PanelEvents = new();
	Queue<QueuedButton> ButtonEvents = new();
	Queue<string> DoubleClicks = new();
	Queue<string> TripleClicks = new();
	Queue<QueuedButton> ButtonTyped = new();
	Queue<(char Character, KeyboardModifiers Modifiers)> KeyTyped = new();
	Queue<Action> Callbacks = new();
	KeyboardModifiers _keyboardModifiers;

	Vector2 MouseMovement;

	/// <summary>
	/// A button event waiting for the focused panel: its <see cref="ButtonEvent"/> is made when a panel takes it, so key
	/// presses with nothing focused (playing) make none.
	/// </summary>
	readonly record struct QueuedButton( ButtonCode Code, string Name, bool Pressed, int VirtualKey, KeyboardModifiers Modifiers )
	{
		public ButtonEvent ToEvent() => Name is null ? new ButtonEvent( Code, Pressed, Modifiers ) : new ButtonEvent( Name, Pressed, VirtualKey, Modifiers );
	}

	/// <summary>
	/// A button's name as <see cref="NormalizeButtonName(string)"/> makes it, made once per button: every key press and
	/// release names its button.
	/// </summary>
	internal static string NormalizeButtonName( ButtonCode button )
	{
		var index = (int)button;
		if ( index < 0 || index >= MaxCachedButton ) return NormalizeButtonName( button.ToString() );

		var names = normalizedNames;
		return names[index] ??= NormalizeButtonName( button.ToString() );
	}

	const int MaxCachedButton = 1024;
	static readonly string[] normalizedNames = new string[MaxCachedButton];

	internal static string NormalizeButtonName( string button )
	{
		button = button.ToLowerInvariant();

		if ( button.StartsWith( "key_" ) )
			button = button[4..];

		return button;
	}

	/// <summary>
	/// Whether anything from <paramref name="focused"/> up overrides the handler the event goes to (the base handlers only
	/// pass it to the parent): when nothing does, as with keys going to a root with nothing focused while playing, no
	/// <see cref="ButtonEvent"/> is made. The root's own typed handler only takes Tab.
	/// </summary>
	static bool Handled( Panel focused, in QueuedButton e, bool typed )
	{
		for ( var panel = focused; panel is not null; panel = panel.Parent )
		{
			var declaring = Handler( panel.GetType(), typed );
			if ( declaring == typeof( Panel ) ) continue;
			if ( typed && declaring == typeof( RootPanel ) )
			{
				if ( e.Name is null ? e.Code == ButtonCode.KEY_TAB : NormalizeButtonName( e.Name ) == "tab" ) return true;
				continue;
			}

			return true;
		}

		return false;
	}

	// Per panel type: the types declaring the OnButtonEvent and OnButtonTyped it runs. Weak, so a closed game's types can unload
	static readonly ConditionalWeakTable<Type, Tuple<Type, Type>> handlers = new();

	static Type Handler( Type type, bool typed )
	{
		if ( !handlers.TryGetValue( type, out var found ) )
		{
			found = Tuple.Create( Declaring( type, nameof( Panel.OnButtonEvent ) ), Declaring( type, nameof( Panel.OnButtonTyped ) ) );
			handlers.AddOrUpdate( type, found );
		}

		return typed ? found.Item2 : found.Item1;

		static Type Declaring( Type type, string name ) => type.GetMethod( name, [typeof( ButtonEvent )] )?.DeclaringType ?? typeof( Panel );
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
			if ( Handled( focused, e, typed: false ) ) focused.OnButtonEvent( e.ToEvent() );
		}

		while ( KeyTyped.TryDequeue( out var e ) )
		{
			focused?.OnKeyTyped( e.Character, e.Modifiers );
		}

		while ( ButtonTyped.TryDequeue( out var e ) )
		{
			if ( Handled( focused, e, typed: true ) ) focused.OnButtonTyped( e.ToEvent() );
		}

		while ( PanelEvents.TryDequeue( out var e ) )
		{
			e.Target = focused;
			focused.CreateEvent( e );
		}
	}

	internal void Tick( Panel hovered, Panel active )
	{
		// Results of input that was taken before it reached the UI (button trapping)
		while ( Callbacks.TryDequeue( out var callback ) )
		{
			try
			{
				callback();
			}
			catch ( Exception e )
			{
				Log.Error( e );
			}
		}

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

	/// <summary>
	/// Run this on the next <see cref="Tick"/>, in the scope of whoever ticks this UI system.
	/// </summary>
	internal void QueueCallback( Action callback )
	{
		Callbacks.Enqueue( callback );
	}

	internal void AddButtonEvent( ButtonCode button, bool down, KeyboardModifiers modifiers )
	{
		_keyboardModifiers = modifiers;
		ButtonEvents.Enqueue( new QueuedButton( button, null, down, 0, modifiers ) );
	}

	internal void AddButtonEvent( string button, bool down, int virtualKey, KeyboardModifiers modifiers )
	{
		_keyboardModifiers = modifiers;
		ButtonEvents.Enqueue( new QueuedButton( default, button, down, virtualKey, modifiers ) );
	}

	internal void AddButtonTyped( string button, int virtualKey, KeyboardModifiers modifiers )
	{
		_keyboardModifiers = modifiers;
		ButtonTyped.Enqueue( new QueuedButton( default, button, true, virtualKey, modifiers ) );
	}

	internal void AddKeyTyped( char c )
	{
		KeyTyped.Enqueue( (c, _keyboardModifiers) );
	}

	internal void AddButtonTyped( ButtonCode button, KeyboardModifiers modifiers )
	{
		_keyboardModifiers = modifiers;
		if ( AddClipboardShortcut( button, modifiers ) )
			return;

		ButtonTyped.Enqueue( new QueuedButton( button, null, true, 0, modifiers ) );
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
			if ( NativeEngine.Sdl.HasClipboardText() )
			{
				var ptr = NativeEngine.Sdl.GetClipboardText();
				var text = System.Runtime.InteropServices.Marshal.PtrToStringUTF8( ptr );
				NativeEngine.Sdl.Free( ptr );

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
