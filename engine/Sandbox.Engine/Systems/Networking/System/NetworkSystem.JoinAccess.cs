namespace Sandbox.Network;

internal partial class NetworkSystem
{
	// Invites the host hasn't confirmed yet. It can drop them - mid handoff, or before it knows we're
	// in - so we keep sending them until it does.
	readonly HashSet<ulong> _pendingInvites = new();

	void InstallJoinAccessMessages()
	{
		AddHandler<AllowJoinMsg>( OnAllowJoin );
		AddHandler<AllowJoinAckMsg>( OnAllowJoinAck );
	}

	/// <summary>
	/// Let this player into the game, even if it's private. Anyone in the game can invite someone,
	/// so clients ask the host.
	/// </summary>
	internal void AllowJoin( ulong steamId )
	{
		if ( steamId == 0 )
			return;

		if ( IsHost )
		{
			Access.Invite( steamId );
			return;
		}

		_pendingInvites.Add( steamId );
		HostConnection?.SendMessage( new AllowJoinMsg { SteamId = steamId } );
	}

	void OnAllowJoin( AllowJoinMsg msg, Connection source, Guid msgId )
	{
		// Not acknowledged, so they'll send it again to whoever ends up host
		if ( !IsHost )
			return;

		// Only someone the host let in can invite people. They're at least Welcome from then on,
		// even while re-syncing after a host change.
		if ( source.State < Connection.ChannelState.Welcome )
			return;

		if ( Access.Invite( msg.SteamId ) )
		{
			log.Trace( $"{source.Name} [{source.SteamId}] invited {msg.SteamId}" );
		}

		source.SendMessage( new AllowJoinAckMsg { SteamId = msg.SteamId } );
	}

	void OnAllowJoinAck( AllowJoinAckMsg msg, Connection source, Guid msgId )
	{
		if ( !source.IsHost )
			return;

		_pendingInvites.Remove( msg.SteamId );
	}

	void ResendPendingInvites()
	{
		if ( _pendingInvites.Count == 0 )
			return;

		if ( IsHost )
		{
			foreach ( var steamId in _pendingInvites )
			{
				Access.Invite( steamId );
			}

			_pendingInvites.Clear();
			return;
		}

		if ( HostConnection is null )
			return;

		foreach ( var steamId in _pendingInvites )
		{
			HostConnection.SendMessage( new AllowJoinMsg { SteamId = steamId } );
		}
	}

	/// <summary>
	/// Can our party follow us in yet? Not until we're in and the host has their invites, or
	/// they'd get there first and be kicked.
	/// </summary>
	internal bool IsReadyForParty => IsHost || (Connection.Local?.State == Connection.ChannelState.Connected && _pendingInvites.Count == 0);

	/// <summary>
	/// A party follows its owner into games, so the owner lets in whoever's in it as they go in.
	/// Only then - parties are Steam lobbies too, and anyone with the id can join one, so later
	/// arrivals need a real invite.
	/// </summary>
	void InviteParty()
	{
		if ( PartyRoom.Current is not { } party || !party.Owner.IsMe )
			return;

		foreach ( var member in party.Members )
		{
			if ( !member.IsMe )
				AllowJoin( member.Id );
		}
	}
}
