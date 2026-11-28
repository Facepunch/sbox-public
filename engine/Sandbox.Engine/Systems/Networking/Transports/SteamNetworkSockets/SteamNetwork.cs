namespace Sandbox.Network;

internal static partial class SteamNetwork
{
	internal static void Initialize()
	{
		SteamNative.NetworkingUtils.SetDebugOutputEnabled( Networking.Debug );
	}
}
