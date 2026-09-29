namespace Sandbox.Network;

internal sealed class NetworkWorld
{
	public NetworkSystem System { get; set; }
	public Connection LocalConnection { get; set; } = new LocalConnection( Guid.NewGuid() );
	public SceneNetworkSystem SceneSystem { get; set; }

	public string ServerName { get; set; }
	public string MapName { get; set; }
	public int MaxPlayers { get; set; }
	public Dictionary<string, string> ServerData { get; set; } = new();
	public ConnectionStats LocalStats { get; set; }
}
