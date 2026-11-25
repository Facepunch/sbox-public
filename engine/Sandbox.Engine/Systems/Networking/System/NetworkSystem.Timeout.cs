using Steamworks;

namespace Sandbox.Network;

internal partial class NetworkSystem
{
	bool _timedOut;

	/// <summary>
	/// Time since the host last sent us anything, while we're a client in the game.
	/// </summary>
	internal RealTimeSince? LastRecievedFromHost
	{
		get
		{
			if ( IsHost || IsDisconnected || HostConnection is null || Connection.Local.IsConnecting )
				return null;

			if ( _migrationPhase != MigrationPhase.None || _hostLost || _pendingHostId != Guid.Empty )
				return null;

			return HostConnection.LastPacketReceived;
		}
	}

	/// <summary>
	/// Seconds until the connection to the host times out, or null when we're not timing out.
	/// </summary>
	internal double? TimeoutCountdown => LastRecievedFromHost is { } since
		? Math.Max( 0, Networking.HostTimeout - (double)since )
		: null;

	void TickHostTimeout()
	{
		if ( _timedOut || TimeoutCountdown is not <= 0 )
			return;

		_timedOut = true;
		OnServerDisconnection( (int)NetConnectionEnd.Misc_Timeout, "Lost connection to server" );
	}
}
