/// <summary>
/// Holds a banlist, can ban users
/// </summary>
internal sealed class BanSystem : GameObjectSystem<BanSystem>, Component.INetworkListener
{
	public record struct BanEntry( string DisplayName, string Reason );

	private Dictionary<long, BanEntry> _bans = new();

	public BanSystem( Scene scene ) : base( scene )
	{
		_bans = LocalData.Get<Dictionary<long, BanEntry>>( "bans", new() ) ?? new();
	}

	bool Component.INetworkListener.AcceptConnection( Connection connection, ref string reason )
	{
		if ( !_bans.TryGetValue( connection.SteamId, out var entry ) )
			return true;

		reason = $"You're banned from this server: {entry.Reason}";
		return false;
	}

	/// <summary>
	/// Bans a connected player and kicks them immediately
	/// </summary>
	public void Ban( Connection connection, string reason )
	{
		Assert.True( Networking.IsHost, "Only the host may ban players." );

		var e = new AdminActionEvent { Action = AdminAction.Ban, SteamId = connection.SteamId, Value = reason };
		if ( !e.Run() ) return;
		reason = e.Value;

		_bans[connection.SteamId] = new BanEntry( connection.DisplayName, reason );
		Save();

		GameManager.Current.NotifyLocalized( "#server.player_banned", new()
		{
			{ "name", LocalizedText.Literal( connection.DisplayName ) },
			{ "reason", LocalizedText.Literal( reason ) }
		} );

		connection.Kick( $"Banned: {reason}" );
		e.Done();
	}

	/// <summary>
	/// Bans a Steam ID by value. Use for pre-banning or banning players who are not currently connected.
	/// Display name falls back to the Steam ID string.
	/// </summary>
	public void Ban( SteamId steamId, string reason )
	{
		Assert.True( Networking.IsHost, "Only the host may ban players." );

		var e = new AdminActionEvent { Action = AdminAction.Ban, SteamId = steamId, Value = reason };
		if ( !e.Run() ) return;

		_bans[steamId] = new BanEntry( steamId.ToString(), e.Value );
		Save();
		e.Done();
	}

	/// <summary>
	/// Removes the ban for the given Steam ID.
	/// </summary>
	public void Unban( SteamId steamId )
	{
		Assert.True( Networking.IsHost, "Only the host may unban players." );

		var e = new AdminActionEvent { Action = AdminAction.Unban, SteamId = steamId };
		if ( !e.Run() ) return;

		if ( _bans.Remove( steamId ) )
			Save();

		e.Done();
	}

	/// <summary>
	/// Returns true if the given Steam ID is currently banned
	/// </summary>
	public bool IsBanned( SteamId steamId ) => _bans.ContainsKey( steamId );

	/// <summary>
	/// Returns a read-only view of all active bans
	/// </summary>
	public IReadOnlyDictionary<SteamId, BanEntry> GetBannedList() => _bans.ToDictionary( x => (SteamId)x.Key, x => x.Value );

	private void Save() => LocalData.Set( "bans", _bans );

	/// <summary>
	/// RPC to ban a connected player. Caller must be host or have admin permission.
	/// </summary>
	[Rpc.Host]
	internal static void RpcBanPlayer( Connection target, string reason = "Banned" )
	{
		if ( !Permissions.Has( Rpc.Caller, Permissions.Admin ) ) return;

		Current.Ban( target, reason );
	}

	/// <summary>
	/// Bans a player by name or Steam ID. Optionally provide a reason.
	/// Usage: ban [name|steamid] [reason]
	/// </summary>
	[ConCmd( "ban" )]
	internal static void BanCommand( string target, string reason = "Banned" )
	{
		if ( !Networking.IsHost ) return;

		// Try parsing as a Steam ID (64-bit integer) first
		if ( ulong.TryParse( target, out var steamIdValue ) )
		{
			var steamId = steamIdValue;
			var connection = Connection.All.FirstOrDefault( c => c.SteamId == steamId );

			if ( connection is not null )
				Current.Ban( connection, reason );
			else
				Current.Ban( steamId, reason );

			Log.Info( $"Banned {steamId}: {reason}" );
			return;
		}

		// Fall back to partial name match
		var conn = GameManager.FindPlayerWithName( target );
		if ( conn is not null )
		{
			Current.Ban( conn, reason );
			Log.Info( $"Banned {conn.DisplayName}: {reason}" );
		}
		else
		{
			Log.Warning( $"Could not find player '{target}'" );
		}
	}
}
