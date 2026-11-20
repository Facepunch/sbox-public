public sealed partial class GameManager
{
	private readonly HashSet<Guid> _kickedPlayers = new();

	public static Connection FindPlayerWithName( string name, bool partial = true )
	{
		return Connection.All.FirstOrDefault( c =>
			partial
				? c.DisplayName.Contains( name, StringComparison.OrdinalIgnoreCase )
				: c.DisplayName.Equals( name, StringComparison.OrdinalIgnoreCase )
		);
	}

	/// <summary>
	/// Kicks a connected player with an optional reason.
	/// </summary>
	public void Kick( Connection connection, string reason = "Kicked" )
	{
		Assert.True( Networking.IsHost, "Only the host may kick players." );

		var e = new AdminActionEvent { Action = AdminAction.Kick, SteamId = connection.SteamId, Value = reason };
		if ( !e.Run() ) return;
		reason = e.Value;

		_kickedPlayers.Add( connection.Id );

		GameManager.Current.NotifyLocalized( "#server.player_kicked", new()
		{
			{ "name", LocalizedText.Literal( connection.DisplayName ) },
			{ "reason", reason == "Kicked" ? "#server.kicked" : LocalizedText.Literal( reason ) }
		} );

		connection.Kick( reason );
		e.Done();
	}

	/// <summary>
	/// RPC to kick a player. Caller must be host or have admin permission.
	/// </summary>
	[Rpc.Host]
	public static void RpcKickPlayer( Connection target, string reason = "Kicked" )
	{
		if ( !Permissions.Has( Rpc.Caller, Permissions.Admin ) ) return;

		Current.Kick( target, reason );
	}

	/// <summary>
	/// Kicks a player by name or Steam ID. Optionally provide a reason.
	/// Usage: kick [name|steamid] [reason]
	/// </summary>
	[ConCmd( "kick" )]
	public static void KickCommand( string target, string reason = "Kicked" )
	{
		if ( !Networking.IsHost ) return;

		if ( ulong.TryParse( target, out var steamIdValue ) )
		{
			var connection = Connection.All.FirstOrDefault( c => c.SteamId == steamIdValue );
			if ( connection is not null )
			{
				Current.Kick( connection, reason );
				Log.Info( $"Kicked {connection.DisplayName}: {reason}" );
			}
			else
			{
				Log.Warning( $"Could not find player with Steam ID '{target}'" );
			}
			return;
		}

		var conn = FindPlayerWithName( target );
		if ( conn is not null )
		{
			Current.Kick( conn, reason );
			Log.Info( $"Kicked {conn.DisplayName}: {reason}" );
		}
		else
		{
			Log.Warning( $"Could not find player '{target}'" );
		}
	}

	/// <summary>
	/// Sets a replicated convar through the host. Only works for admins.
	/// </summary>
	public static void SetConVar( string name, bool value ) => ServerSettings.Set( name, value );
}
