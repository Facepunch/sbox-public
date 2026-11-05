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
		CloseDirectRoute();
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

	internal override void InternalSend( NetPacket packet, NetFlags flags )
	{
		var steamFlags = flags.ToSteamFlags();
		steamFlags |= 32; // k_nSteamNetworkingSend_AutoRestartBrokenSession
		Lobby.SendMessage( this, packet, steamFlags );
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

	/// <summary>
	/// Steam couldn't keep or set up a session with this peer, which happens between some pairs of peers.
	/// </summary>
	internal void OnSessionFailed( SteamSessionStatus failure )
	{
		SessionFailure = failure;
		CloseDirectRoute();
	}

	RealTimeUntil timeUntilDirectProbe;
	const float DirectProbeInterval = 5;

	/// <summary>
	/// Clients only talk to each other directly once Steam has a session up. Until then Steam would queue everything
	/// as reliable, and drop it all if the session fails, so it goes through the host instead.
	/// </summary>
	internal override void Tick( NetworkSystem parent )
	{
		if ( _disposed || parent.IsHost )
			return;

		var state = SteamSessionStatus.Read( Friend.Id ).State;

		if ( state == Steamworks.ConnectionState.Connected )
		{
			OpenDirectRoute();
			return;
		}

		CloseDirectRoute();

		if ( state is Steamworks.ConnectionState.Connecting or Steamworks.ConnectionState.FindingRoute )
			return;

		if ( timeUntilDirectProbe > 0 )
			return;

		timeUntilDirectProbe = DirectProbeInterval;

		// Empty, so receivers drop it. It only gets Steam to set up a session.
		var probe = NetPacket.Rent( 0 );
		InternalSend( probe, NetFlags.Unreliable );
		probe.Release();
	}
}
