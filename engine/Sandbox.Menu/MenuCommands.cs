using Sandbox.Modals;

namespace Sandbox;

/// <summary>
/// Console commands for the built-in menu.
/// </summary>
public static class MenuCommands
{
	/// <summary>
	/// F1 by default - the pause menu, the same one Escape opens, and closes it again. Unlike Escape
	/// the game can't take it for itself, so there's always a way to it. Still called gameinfo so
	/// the key bindings people already have keep working.
	/// </summary>
	[MenuConCmd( "gameinfo", ConVarFlags.Protected )]
	public static void OpenPauseMenu()
	{
		if ( string.IsNullOrEmpty( Application.GameIdent ) )
		{
			Log.Info( "Couldn't open gameinfo - not in a game." );
			return;
		}

		IModalSystem.Current?.PauseMenu();
	}
}
