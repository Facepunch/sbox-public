using Sandbox.SteamApi;

namespace Sandbox.Network;

internal readonly record struct SteamSessionStatus( ConnectionState State, int Reason, string Detail )
{
	internal static SteamSessionStatus Read( ulong peer )
	{
		var state = SteamNative.NetworkingMessages.GetSessionStatus( peer, out var reason, out var detail );
		return new( state, reason, detail );
	}

	internal string DescribeTimeout( double elapsed, string sendFailure = null )
	{
		var stage = State switch
		{
			ConnectionState.Connected => "Steam connected, but the host did not send the initial game handshake",
			ConnectionState.FindingRoute => "Steam was still finding a route to the host",
			ConnectionState.Connecting => "Steam was still establishing the connection to the host",
			ConnectionState.None => "No Steam connection to the host was established",
			_ => "The Steam connection to the host failed"
		};
		var message = $"Joined the lobby, but could not join the game. {stage} after {elapsed:0} seconds.\nSteam state: {State}.";
		if ( Reason != 0 ) message += $" Reason: {Reason}.";
		if ( !string.IsNullOrWhiteSpace( Detail ) ) message += $"\n{Detail}";
		if ( !string.IsNullOrWhiteSpace( sendFailure ) ) message += $"\n{sendFailure}";
		return message;
	}
}
