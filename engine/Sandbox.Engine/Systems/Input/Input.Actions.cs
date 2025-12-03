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
		get => CurrentContext.ActionsCurrent;
		set => CurrentContext.ActionsCurrent = value;
	}

	static ulong LastActions
	{
		get => CurrentContext.ActionsPrevious;
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
		if ( Suppressed ) return false;
		return !WasDownLastCommand( action ) && Down( action );
	}

	/// <summary>
	/// Action was pressed but now it isn't
	/// </summary>
	[ActionGraphNode( "input.released" ), Pure, Category( "Input" ), Icon( "gamepad" )]
	public static bool Released( [InputAction] string action )
	{
		if ( Suppressed ) return false;
		return WasDownLastCommand( action ) && !Down( action );
	}

	/// <inheritdoc cref="SetAction(int, bool)"/>
	internal static void SetAction( InputAction action, bool down ) => SetAction( GetActionIndex( action ), down );

	/// <inheritdoc cref="SetAction(int, bool)"/>
	public static void SetAction( string action, bool down ) => SetAction( GetActionIndex( action ), down );

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
					e.AccumActionsPressed |= 1UL << i;
				}
			}
			else
			{
				foreach ( var e in Contexts )
				{
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
