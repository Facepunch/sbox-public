namespace Sandbox.Network;

/// <summary>
/// Connection measurements returned by both Steam transports.
/// </summary>
internal struct SteamConnectionStats
{
	internal int Ping;
	internal float InBytesPerSecond;
	internal float InPacketsPerSecond;
	internal float OutBytesPerSecond;
	internal float OutPacketsPerSecond;
	internal int SendRateBytesPerSecond;
	internal float ConnectionQuality;

	internal readonly ConnectionStats ForConnection( string name ) => new( name )
	{
		Ping = Ping,
		InBytesPerSecond = InBytesPerSecond,
		InPacketsPerSecond = InPacketsPerSecond,
		OutBytesPerSecond = OutBytesPerSecond,
		OutPacketsPerSecond = OutPacketsPerSecond,
		SendRateBytesPerSecond = SendRateBytesPerSecond,
		ConnectionQuality = ConnectionQuality
	};
}
