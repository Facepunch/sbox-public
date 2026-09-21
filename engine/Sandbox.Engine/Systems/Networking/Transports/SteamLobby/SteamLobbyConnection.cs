using NativeEngine;

namespace Sandbox.Network;

/// <summary>
/// A direct connection to a peer in a lobby.
/// </summary>
internal unsafe class SteamLobbyConnection : Connection, IValid
{
	private readonly SteamLobbySocket Lobby;
	internal Steamworks.Result? LastSendError { get; set; }
	internal SteamSessionStatus? SessionFailure { get; set; }
	bool _disposed;
	public Friend Friend { get; private set; }
	public bool IsValid => !_disposed;

	public SteamLobbyConnection( SteamLobbySocket lobby, Friend steamId )
	{
		Lobby = lobby;
		Friend = steamId;
	}

	public void Dispose()
	{
		Close( 0, "Left lobby" );
		State = ChannelState.Unconnected;
	}

	public string Description => "Steam Lobby Connection";

	private RealTimeUntil timeUntilFetchStats;
	private ConnectionStats cachedStats;

	public override ConnectionStats Stats
	{
		get
		{
			if ( !timeUntilFetchStats )
				return cachedStats;

			var net = Steam.SteamNetworkingMessages();
			var info = net.GetConnectionInfo( Friend.Id );

			cachedStats = new( DisplayName )
			{
				Ping = info.m_nPing,
				InBytesPerSecond = info.m_flInBytesPerSec,
				InPacketsPerSecond = info.m_flInPacketsPerSec,
				OutBytesPerSecond = info.m_flOutBytesPerSec,
				OutPacketsPerSecond = info.m_flOutPacketsPerSec,
				SendRateBytesPerSecond = info.m_nSendRateBytesPerSecond,
				ConnectionQuality = info.m_flConnectionQualityLocal
			};

			timeUntilFetchStats = 0.5f;
			return cachedStats;
		}
	}

	internal override void InternalSend( byte[] data, NetFlags flags )
	{
		var steamFlags = flags.ToSteamFlags();
		steamFlags |= 32; // k_nSteamNetworkingSend_AutoRestartBrokenSession
		Lobby.SendMessage( this, data, steamFlags );
	}

	internal override void InternalRecv( NetworkSystem.MessageHandler handler )
	{

	}

	internal override void InternalClose( int closeCode, string closeReason )
	{
		lock ( Networking.NetworkThreadLock )
		{
			if ( _disposed ) return;
			_disposed = true;
			var net = Steam.SteamNetworkingMessages();
			if ( net.IsValid ) net.CloseChannelWithUser( Friend.Id, Lobby.NetworkChannel );
		}
	}

	internal void UpdateFromInfo( ConnectionInfo info )
	{
		Id = info.ConnectionId;
	}
}
