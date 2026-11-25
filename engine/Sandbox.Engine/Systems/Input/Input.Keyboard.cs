using NativeEngine;

namespace Sandbox;

public static partial class Input
{
	public static partial class Keyboard
	{
		/// <summary>
		/// Finds project actions currently bound to this exact keyboard or mouse combination in either binding slot.
		/// Button names are the ones returned by <see cref="Capture"/>. Their order does not matter.
		/// </summary>
		public static IReadOnlyList<InputAction> GetBoundActions( IReadOnlyList<string> buttons )
		{
			if ( buttons is not { Count: > 0 } || InputActions is null ) return [];

			var game = string.IsNullOrEmpty( Application.GameIdent ) ? "common" : Application.GameIdent;
			var matches = new List<InputAction>();
			foreach ( var bind in Sandbox.Engine.InputBinds.FindCollection( game ).EnumerateWithButton( buttons[0] ) )
			{
				if ( !bind.Slots.Any( slot => slot.Matches( buttons ) ) ) continue;
				if ( FindInputActionByName( bind.Name ) is { } action )
					matches.Add( new InputAction( action ) );
			}

			return matches;
		}

		/// <summary>
		/// Keyboard key is held down
		/// </summary>
		public static bool Down( string keyName )
		{
			if ( Application.IsHeadless ) return false;
			if ( Suppressed ) return false;

			var code = Sandbox.Engine.KeyTranslation.StringToButtonCode( keyName );
			if ( code == ButtonCode.BUTTON_CODE_INVALID ) return false;

			return CurrentContext.KeysCurrent.Contains( code );
		}

		/// <summary>
		/// Keyboard key wasn't pressed but now it is
		/// </summary>
		public static bool Pressed( string keyName )
		{
			if ( Application.IsHeadless ) return false;
			if ( Suppressed ) return false;

			var code = Sandbox.Engine.KeyTranslation.StringToButtonCode( keyName );
			if ( code == ButtonCode.BUTTON_CODE_INVALID ) return false;

			return !CurrentContext.KeysPrevious.Contains( code ) && Down( keyName );
		}

		/// <summary>
		/// Keyboard key was pressed but now it isn't
		/// </summary>
		public static bool Released( string keyName )
		{
			if ( Application.IsHeadless ) return false;
			if ( Suppressed ) return false;

			var code = Sandbox.Engine.KeyTranslation.StringToButtonCode( keyName );
			if ( code == ButtonCode.BUTTON_CODE_INVALID ) return false;

			return CurrentContext.KeysPrevious.Contains( code ) && !Down( keyName );
		}

		internal static (bool Down, bool Pressed, bool Released) ReadKeys( ReadOnlySpan<string> keys )
		{
			if ( keys.IsEmpty || Application.IsHeadless || Suppressed || CurrentPlayerScope > 0 ) return default;

			bool down = true, wasDown = true;
			foreach ( var key in keys )
			{
				var code = Sandbox.Engine.KeyTranslation.StringToButtonCode( key );
				if ( code is <= ButtonCode.BUTTON_CODE_NONE or > ButtonCode.MOUSE_LAST ) return default;

				down &= CurrentContext.KeysCurrent.Contains( code );
				wasDown &= CurrentContext.KeysPrevious.Contains( code );
			}

			return (down, down && !wasDown, !down && wasDown);
		}

		/// <summary>
		/// Capture the next button combination the player presses, for rebinding controls. Keyboard and mouse
		/// buttons go to the capture instead of the game or UI until one of the captured buttons is released.
		/// Then <paramref name="onCaptured"/> is called with every button that went down, in the order they went
		/// down, by the names <see cref="Down"/> takes - so holding W, A, S and D then letting go of any of them
		/// gives <c>w, a, s, d</c>.
		/// <para>
		/// Escape backs out of the capture, as do <see cref="CancelCapture"/>, starting another capture and the
		/// window losing focus. <paramref name="onCaptured"/> is called with no buttons when that happens. Escape
		/// still does what it always does - opening the pause menu, or leaving when held. Nothing is captured while
		/// the pause menu or an overlay has the keyboard. Escape and the function keys can't be captured.
		/// </para>
		/// </summary>
		/// <example>
		/// <code>
		/// Input.Keyboard.Capture( buttons =>
		/// {
		/// 	if ( buttons.Count == 0 ) return; // cancelled
		/// 	Binding = string.Join( "+", buttons );
		/// } );
		/// </code>
		/// </example>
		public static void Capture( Action<IReadOnlyList<string>> onCaptured )
		{
			ArgumentNullException.ThrowIfNull( onCaptured );

			// No game, no input - the router only feeds the game's context while there's a game
			if ( Application.IsHeadless || IGameInstance.Current is null || GameContext is not { } context )
			{
				onCaptured( [] );
				return;
			}

			context.StartTrapping( buttons => onCaptured( buttons ) );
		}

		/// <summary>
		/// Back out of the capture started by <see cref="Capture"/>, if there is one. Its callback is called with no buttons.
		/// </summary>
		public static void CancelCapture()
		{
			GameContext?.CancelTrapping();
		}

		/// <summary>
		/// Always the game's, never whichever context we're in. Game delegates can be called from the menu's scope
		/// (a package selector's filter callback, say), and capturing there would hand the game every key and click
		/// meant for the platform UI - search boxes, chat - and lock the player out of it.
		/// </summary>
		static Sandbox.Engine.InputContext GameContext => Sandbox.Engine.IGameInstanceDll.Current?.InputContext;
	}
}
