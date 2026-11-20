public sealed partial class Player
{
	/// <summary>
	/// Kill yourself
	/// </summary>
	[ConCmd( "kill" )]
	public static void KillSelf( Connection source )
	{
		var player = Player.FindForConnection( source );
		if ( player is null ) return;

		player.KillSelf();
	}

	[Rpc.Host]
	internal void KillSelf()
	{
		if ( Rpc.Caller != Network.Owner ) return;

		this.OnDamage( new DamageInfo( float.MaxValue, GameObject, null ) );
	}

	[ConCmd( "god", ConVarFlags.Server, Help = "Toggle invulnerability" )]
	public static void God( Connection source )
	{
		var player = PlayerData.For( source );
		if ( !player.IsValid() )
			return;

		// turning it off is always allowed
		if ( !player.IsGodMode && !ServerSettings.CanUseGodMode( source ) )
		{
			source.SendLog( LogLevel.Info, "Godmode is disabled on this server" );
			return;
		}

		player.IsGodMode = !player.IsGodMode;
		source.SendLog( LogLevel.Info, player.IsGodMode ? "Godmode enabled" : "Godmode disabled" );
	}

	[ConCmd( "noclip", Help = "Toggle noclip" )]
	public static void Noclip()
	{
		FindLocalPlayer()?.ToggleNoclip();
	}

	/// <summary>
	/// Switch to another map
	/// </summary>
	[ConCmd( "map", ConVarFlags.Admin )]
	public static void ChangeMap( string mapName )
	{
		var e = new AdminActionEvent { Action = AdminAction.ChangeMap, Value = mapName };
		if ( !e.Run() ) return;

		LaunchArguments.Map = e.Value;
		e.Done();
		Game.Load( Game.Ident, true );
	}

	/// <summary>
	/// Undo the last action for the calling player
	/// </summary>
	[ConCmd( "undo", ConVarFlags.Server )]
	public static void RunUndo( Connection source )
	{
		var player = Player.FindForConnection( source );
		if ( !player.IsValid() )
			return;

		player.Undo.Undo();
	}
}
