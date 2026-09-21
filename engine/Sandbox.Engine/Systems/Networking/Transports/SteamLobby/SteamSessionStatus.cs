using NativeEngine;
using Steamworks;
using System.Text;

namespace Sandbox.Network;

internal readonly record struct SteamSessionStatus( ConnectionState State, int Reason, string Detail )
{
	internal static unsafe SteamSessionStatus Read( ulong peer )
	{
		var net = Steam.SteamNetworkingMessages();
		if ( !net.IsValid ) return new( ConnectionState.None, 0, "Steam networking is unavailable." );
		var buffer = new byte[256];
		int reason = 0;
		fixed ( byte* ptr = buffer )
		{
			var state = net.GetSessionState( peer, (IntPtr)(&reason), (IntPtr)ptr );
			var length = Array.IndexOf( buffer, (byte)0 );
			return new( (ConnectionState)state, reason, Encoding.UTF8.GetString( buffer, 0, length < 0 ? buffer.Length : length ) );
		}
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
