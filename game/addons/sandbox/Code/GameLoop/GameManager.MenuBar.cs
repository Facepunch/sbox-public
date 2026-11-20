using Sandbox.Npcs;

public sealed partial class GameManager : IMenuBarEvent
{
	/// <summary>
	/// The built-in menus on the inspect menu's settings bar: Client, NPCs and Server.
	/// Addons add their own by implementing <see cref="IMenuBarEvent"/> the same way.
	/// </summary>
	void IMenuBarEvent.PopulateMenuBar( IMenuBarEvent.Event e )
	{
		var client = e.GetOrAddMenu( Game.Language.GetPhrase( "spawnmenu.menubar.client" ), 100 );

		// Write through the console system so Saved and UserInfo convar behavior is preserved.
		IMenuBarEvent.Event.AddToggle( client, Game.Language.GetPhrase( "spawnmenu.utility.view_bobbing" ), () => GamePreferences.ViewBobbing, v => ConsoleSystem.SetValue( "sandbox.viewbob", v ) );
		IMenuBarEvent.Event.AddToggle( client, Game.Language.GetPhrase( "spawnmenu.utility.auto_switch" ), () => GamePreferences.AutoSwitch, v => ConsoleSystem.SetValue( "sandbox.autoswitch", v ) );
		client.AddSeparator();
		IMenuBarEvent.Event.AddToggle( client, Game.Language.GetPhrase( "spawnmenu.utility.show_hints" ), () => Sandbox.UI.Hints.ShowHints, v => ConsoleSystem.SetValue( "sandbox.showhints", v ) );

		// Write through the host so the replicated convars change for everyone.
		var npcs = e.GetOrAddMenu( Game.Language.GetPhrase( "spawnmenu.menubar.npcs" ), 200 );

		IMenuBarEvent.Event.AddServerToggle( npcs, Game.Language.GetPhrase( "spawnmenu.utility.ai_enabled" ), () => NpcConVars.Enabled, v => SetConVar( "sandbox.ai.enabled", v ) );
		IMenuBarEvent.Event.AddServerToggle( npcs, Game.Language.GetPhrase( "spawnmenu.utility.no_target" ), () => NpcConVars.NoTarget, v => SetConVar( "sandbox.ai.notarget", v ) );

		var server = e.GetOrAddMenu( Game.Language.GetPhrase( "spawnmenu.menubar.server" ), 300 );

		IMenuBarEvent.Event.AddServerToggle( server, Game.Language.GetPhrase( "spawnmenu.menubar.reequip_last" ), () => ServerSettings.ReEquipLast, v => SetConVar( "sandbox.reequiplast", v ) );
		server.AddSeparator();
		IMenuBarEvent.Event.AddServerToggle( server, Game.Language.GetPhrase( "server.developer.entities" ), () => ServerSettings.DeveloperEntities, v => SetConVar( "sandbox.developer.entities", v ) );
	}
}
