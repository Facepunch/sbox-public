using NativeEngine;
using Steamworks.Data;

namespace EngineTests;

[TestClass]
public class SteamServerTest
{
	[TestMethod]
	public void CreateServerAndShutdown()
	{
		Steam.SteamGameServer_Init( 27015, Defines.STEAMGAMESERVER_QUERY_PORT_SHARED, "1.0.0.0" );

		var sgs = Steam.SteamGameServer();
		Assert.IsTrue( sgs.IsValid, "SteamGameServer interface is null after init - Steam redistributables may not be present" );

		sgs.SetMapName( "gm_construct" );

		Steam.SteamGameServer_Shutdown();
	}
}
