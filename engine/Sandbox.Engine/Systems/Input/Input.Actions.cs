using NativeEngine;
using Sandbox.Engine;

namespace Sandbox;

public static partial class Input
{
	/// <summary>
	/// Was the last button pressed a game controller button?
	/// </summary>
	public static bool UsingController { get; internal set; } = false;

	internal static ulong Actions
	{
		get => CurrentPlayerScope switch
		{
			0 => CurrentContext.ActionsCurrent,
			> 0 => CurrentController?.InputContext?.ActionsCurrent ?? 0,
			_ => CurrentContext.ActionsCurrent | (Controller.First?.InputContext?.ActionsCurrent ?? 0)
		};
		set => CurrentContext.ActionsCurrent = value;
	}

	static ulong LastActions
	{
		get => CurrentPlayerScope switch
		{
			0 => CurrentContext.ActionsPrevious,
			> 0 => CurrentController?.InputContext?.ActionsPrevious ?? 0,
			_ => CurrentContext.ActionsPrevious | (Controller.First?.InputContext?.ActionsPrevious ?? 0)
		};
		set => CurrentContext.ActionsPrevious = value;
	}

	/// <summary>
	/// Missing action names that we've warned about already.
	/// This gets cleared when actions are initialized again.
	/// </summary>
	private static HashSet<string> MissingActions { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// We pack actions bit-by-bit in CUserCmd, using the index (which is shared between realms) to map it to an action.
	/// This is an accessor to grab that index easily from its action.
	/// </summary>
	/// <param name="actionName"></param>
	/// <returns></returns>
	internal static int GetActionIndex( string actionName )
	{
		return InputActions?.FindIndex( x => string.Equals( x.Name, actionName, StringComparison.OrdinalIgnoreCase ) ) ?? -1;
	}

	/// <inheritdoc cref="GetActionIndex(string)"/>
	internal static int GetActionIndex( InputAction action )
	{
		return InputActions?.IndexOf( action ) ?? -1;
	}

	/// <summary>
	/// True if escape was pressed
	/// </summary>
	public static bool EscapePressed
	{
		get => InputRouter.EscapeWasPressed;
		set => InputRouter.EscapeWasPressed = value;
	}

	/// <summary>
	/// Action is currently pressed down
	/// </summary>
	[ActionGraphNode( "input.down" ), Pure, Category( "Input" ), Icon( "gamepad" )]
	public static bool Down( [InputAction] string action, bool complainOnMissing = true )
	{
		if ( Application.IsHeadless ) return false;
		if ( Suppressed ) return false;
		if ( string.IsNullOrWhiteSpace( action ) ) return false;

		var index = GetActionIndex( action );
		if ( index == -1 )
		{
			if ( complainOnMissing && MissingActions.Add( action ) )
				Log.Warning( $"Couldn't find Input Action called \"{action}\"" );

			return false;
		}

		return (Actions & 1UL << index) != 0;
	}

	internal static bool WasDownLastCommand( string action )
	{
		if ( Suppressed ) return false;
		if ( string.IsNullOrWhiteSpace( action ) ) return false;

		var index = GetActionIndex( action );
		if ( index == -1 && MissingActions.Add( action ) )
		{
			Log.Warning( $"Couldn't find Input Action called \"{action}\"" );
			return false;
		}

		return (LastActions & (1UL << index)) != 0;
	}

	/// <summary>
	/// Action wasn't pressed but now it is
	/// </summary>
	[ActionGraphNode( "input.pressed" ), Pure, Category( "Input" ), Icon( "gamepad" )]
	public static bool Pressed( [InputAction] string action )
	{
		if ( Application.IsHeadless ) return false;
		if ( Suppressed ) return false;
		return !WasDownLastCommand( action ) && Down( action );
	}

	/// <summary>
	/// Action was pressed but now it isn't
	/// </summary>
	[ActionGraphNode( "input.released" ), Pure, Category( "Input" ), Icon( "gamepad" )]
	public static bool Released( [InputAction] string action )
	{
		if ( Application.IsHeadless ) return false;
		if ( Suppressed ) return false;
		return WasDownLastCommand( action ) && !Down( action );
	}

	/// <inheritdoc cref="SetAction(int, bool)"/>
	internal static void SetAction( InputAction action, bool down ) => SetAction( GetActionIndex( action ), down );

	/// <inheritdoc cref="SetAction(int, bool)"/>
	public static void SetAction( string action, bool down ) => SetAction( GetActionIndex( action ), down );

	/// <inheritdoc cref="SetLastAction(int, bool)"/>
	public static void SetLastAction( string action, bool down ) => SetLastAction( GetActionIndex( action ), down );

	/// <summary>
	/// Remove this action, so it's no longer being pressed.
	/// </summary>
	/// <param name="action"></param>
	public static void Clear( string action ) => SetAction( action, false );

	/// <summary>
	/// Clears the current input actions, so that none of them are active.
	/// </summary>
	public static void ClearActions()
	{
		Actions = default;
		LastActions = default;
	}

	/// <summary>
	/// Clears the current input actions, so that none of them are active. Unlike ClearActions
	/// this will unpress the buttons, so they won't be active again until they're pressed again.
	/// </summary>
	public static void ReleaseActions()
	{
		foreach ( var e in Contexts )
		{
			e.ActionsCurrent = default;
			e.ActionsPrevious = default;
			e.AccumActionsPressed = default;
		}
	}

	/// <summary>
	/// Releases the action, and it won't be active again until it's pressed again.
	/// </summary>
	public static void ReleaseAction( string name )
	{
		var index = GetActionIndex( name );

		foreach ( var e in Contexts )
		{
			e.ActionsCurrent &= ~(1UL << index);
			e.ActionsPrevious &= ~(1UL << index);
			e.AccumActionsPressed &= ~(1UL << index);
		}
	}

	/// <summary>
	/// Activates / Deactivates an action when building input.
	/// </summary>
	/// <param name="index"></param>
	/// <param name="down"></param>
	internal static void SetAction( int index, bool down )
	{
		if ( down ) Actions |= 1UL << index;
		else Actions &= ~(1UL << index);
	}

	/// <summary>
	/// Activates / Deactivates the previous action when building input.
	/// </summary>
	/// <param name="index"></param>
	/// <param name="down"></param>
	internal static void SetLastAction( int index, bool down )
	{
		if ( down ) LastActions |= 1UL << index;
		else LastActions &= ~(1UL << index);
	}

	static InputAction FindInputActionByName( string action )
	{
		return InputActions?.FirstOrDefault( x => string.Equals( x.Name, action, StringComparison.OrdinalIgnoreCase ) );
	}

	static HashSet<string> activeButtons = new HashSet<string>();

	/// <summary>
	/// Called when a compatible button is pressed.
	/// </summary>
	internal static void OnButton( ButtonCode code, string button, bool down )
	{
		if ( InputActions == null )
		{
			activeButtons.Clear();
			return;
		}

		string loadedGame = Application.GameIdent;

		if ( string.IsNullOrEmpty( loadedGame ) ) loadedGame = "common";

		var collection = InputBinds.FindCollection( loadedGame );

		if ( down )
		{
			activeButtons.Add( button );

			foreach ( var e in Contexts )
			{
				e.AccumKeysPressed.Add( code );
			}
		}
		else
		{
			foreach ( var e in Contexts )
			{
				e.AccumKeysReleased.Add( code );
			}

			// remove it but if it wasn't even active, ignore it
			if ( !activeButtons.Remove( button ) )
				return;
		}

		UsingController = false;

		bool handled = false;

		//
		// Find any actions that contain this button
		//
		foreach ( var bind in collection.EnumerateWithButton( button ) )
		{
			var action = FindInputActionByName( bind.Name );

			if ( action == null ) continue;

			// For the action to be active we need a bind with this button and all the other buttons in the bind
			if ( down && !bind.Test( button, activeButtons ) )
				continue;

			// One of the binds for this action passed - so don't do anything
			if ( !down && bind.Test( button, activeButtons ) )
				continue;

			var i = GetActionIndex( action );

			if ( down )
			{
				foreach ( var e in Contexts )
				{
					if ( IsControllerContext( e ) )
						continue;

					e.AccumActionsPressed |= 1UL << i;
				}
			}
			else
			{
				foreach ( var e in Contexts )
				{
					if ( IsControllerContext( e ) )
						continue;

					e.AccumActionsReleased |= 1UL << i;
				}
			}

			handled = true;
		}

		if ( !handled )
		{
			OnUnhandledButton( code, button, down );
		}
	}

	static void OnUnhandledButton( ButtonCode code, string button, bool down )
	{
		if ( !down ) return;

		var binding = g_pInputService.GetBinding( code );
		if ( string.IsNullOrEmpty( binding ) ) return;

		ConVarSystem.Run( $"{binding}\n" );
	}

	/// <summary>
	/// Returns true if the given context belongs to a controller
	/// </summary>
	private static bool IsControllerContext( Context context )
	{
		return Controller.All.Any( c => c.InputContext == context );
	}

	internal static InputSettings InputSettings { get; set; }

	internal static List<InputAction> InputActions => InputSettings?.Actions;

	/// <summary>
	/// Copies all input actions to be used publicly
	/// </summary>
	/// <returns></returns>
	public static IEnumerable<InputAction> GetActions() => InputActions.Select( x => new InputAction( x.Name, x.KeyboardCode, x.GamepadCode, x.GroupName, x.Title ) );

	/// <summary>
	/// Names of all actions from the current game's input settings.
	/// </summary>
	public static IEnumerable<string> ActionNames => InputActions?.Select( x => x.Name ) ?? Array.Empty<string>();

	/// <summary>
	/// Finds the <see cref="InputAction.GroupName"/> of the given action.
	/// </summary>
	/// <param name="action">Action name to find the group name of.</param>
	public static string GetGroupName( string action ) => FindInputActionByName( action )?.GroupName;

	/// <summary>
	/// The bind collection that belongs to the currently loaded game.
	/// </summary>
	static BindCollection CurrentBinds
	{
		get
		{
			var ident = Application.GameIdent;
			if ( string.IsNullOrEmpty( ident ) ) ident = "common";

			return InputBinds.FindCollection( ident );
		}
	}

	/// <summary>
	/// Read the button(s) currently bound to an action, as a compact combo string
	/// such as <c>"ctrl + a"</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Resolution order: the loaded game's bind <see cref="SetBind"/> first, then the shared
	/// "common" collection (if <paramref name="fallbackToCommon"/> is set), then the action's
	/// declared default key. This mirrors the lookup the input router performs when it resolves
	/// an action, so the string returned here is always what the player has to physically press.
	/// </para>
	/// <para>
	/// Returns <see langword="null"/> when nothing is bound to that action.
	/// </para>
	/// </remarks>
	/// <param name="actionName">The action to look up, as it appears in <see cref="ActionNames"/>.</param>
	/// <param name="slot">The bind slot to read. Each action has up to two slots.</param>
	/// <param name="fallbackToCommon">If <see langword="true"/>, fall back to the shared game-agnostic binds.</param>
	public static string GetBind( string actionName, int slot = 0, bool fallbackToCommon = true )
	{
		var collection = CurrentBinds;
		var value = collection.Get( actionName, slot );

		if ( !string.IsNullOrWhiteSpace( value ) )
			return value;

		if ( fallbackToCommon )
		{
			var commonValue = InputBinds.FindCollection( "common" ).Get( actionName, slot );
			if ( !string.IsNullOrWhiteSpace( commonValue ) )
				return commonValue;
		}

		var action = FindInputActionByName( actionName );
		return slot == 0 ? action?.KeyboardCode : null;
	}

	/// <summary>
	/// Read every bind defined for the loaded game, as an action name mapped to its slot values.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The returned arrays always have two entries, one per bind slot. An empty or <see langword="null"/>
	/// entry means that slot is unbound. Values are the raw stored combo strings - the action name used
	/// as a key matches <see cref="ActionNames"/>, and each value can be passed straight back to
	/// <see cref="SetBind"/>.
	/// </para>
	/// </remarks>
	public static Dictionary<string, string[]> GetAllBinds()
	{
		var collection = CurrentBinds;
		var result = new Dictionary<string, string[]>( StringComparer.OrdinalIgnoreCase );

		foreach ( var (name, bind) in collection.Actions )
		{
			var slots = new string[bind.Slots.Length];
			for ( int i = 0; i < bind.Slots.Length; i++ )
			{
				slots[i] = bind.Get( i )?.FullString;
			}

			result[name] = slots;
		}

		return result;
	}

	/// <summary>
	/// (Re)bind the button(s) for an action to <paramref name="buttonName"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The value is a compact combo string such as <c>"ctrl + a"</c>, exactly as you'd expect to
	/// see from <see cref="CaptureBind"/>. It takes effect immediately - the input router re-reads
	/// the bind collection on the next physical button press. Use <see cref="SaveBinds"/> to persist
	/// it to disk so it survives a restart.
	/// </para>
	/// <para>
	/// This is reversible - <see cref="ResetBinds"/> restores every action to its declared default.
	/// </para>
	/// </remarks>
	/// <param name="actionName">The action to bind, as it appears in <see cref="ActionNames"/>.</param>
	/// <param name="buttonName">The button combo to assign, or <see langword="null"/> to clear the slot.</param>
	/// <param name="slot">The bind slot to write. Each action has up to two slots.</param>
	public static void SetBind( string actionName, string buttonName, int slot = 0 )
	{
		CurrentBinds.Set( actionName, slot, buttonName );
	}

	/// <summary>
	/// Restore every bind in the loaded game back to its declared default, and persist it.
	/// </summary>
	/// <remarks>
	/// This is equivalent to the bind reset button found in the engine's settings - it clears
	/// any user overrides and rewrites the collection to disk.
	/// </remarks>
	public static void ResetBinds()
	{
		var collection = CurrentBinds;
		collection.ResetToDefaults();
		collection.SaveToDisk();
	}

	/// <summary>
	/// Write the loaded game's binds out to disk as JSON, so they persist across restarts.
	/// </summary>
	/// <remarks>
	/// You don't need to call this after every <see cref="SetBind"/> - only when you consider the
	/// player's configuration "committed". The settings UI calls this when the panel is closed.
	/// </remarks>
	public static void SaveBinds()
	{
		CurrentBinds.SaveToDisk();
	}

	/// <summary>
	/// Begin capturing the next key combo the player presses.
	/// </summary>
	/// <remarks>
	/// <para>
	/// While capturing, the engine swallows input and collects every button pressed until the
	/// player releases them all, then invokes <paramref name="onCaptured"/> with the resulting combo
	/// (for example <c>["ctrl", "a"]</c>). Pressing <c>escape</c> interrupts the capture sooner - the
	/// callback fires immediately so callers can treat that as a cancel.
	/// </para>
	/// <para>
	/// Only a single capture can be active at a time; the most recent caller wins.
	/// </para>
	/// </remarks>
	/// <param name="onCaptured">Callback invoked once the player releases the captured keys.</param>
	public static void CaptureBind( Action<string[]> onCaptured )
	{
		if ( Game.InputContext is null )
		{
			onCaptured?.Invoke( Array.Empty<string>() );
			return;
		}

		Game.InputContext.StartTrapping( onCaptured );
	}

	/// <summary>
	/// Read the config from this source
	/// </summary>
	internal static void ReadConfig( InputSettings inputConfig )
	{
		InputSettings = inputConfig;

		InputSettings ??= new InputSettings();

		// if there's nothing in the list, we want to init to the defaults
		if ( InputSettings?.Actions?.Count == 0 )
		{
			InputSettings.InitDefault();
		}

		MissingActions.Clear();

		if ( string.IsNullOrEmpty( Application.GameIdent ) || InputActions is null )
			return;

		// Tell the binding system about the new binds so it can set defaults properly
		var collection = InputBinds.FindCollection( Application.GameIdent );
		collection.UpdateActions( InputActions );
	}
}
