namespace Sandbox;

/// <summary>
/// A class that holds all configured networking settings for a game.
/// This is serialized as a config and shared from the server to the client.
/// </summary>
[Expose]
public class NetworkingSettings : ConfigData
{
	/// <summary>
	/// Whether to disband the game lobby when the host leaves.
	/// </summary>
	public bool DestroyLobbyWhenHostLeaves { get; set; }

	/// <summary>
	/// Whether to periodically switch to the best host candidate. Candidates are
	/// scored based on their average ping and connection quality to all other peers.
	/// </summary>
	public bool AutoSwitchToBestHost { get; set; } = true;

	/// <summary>
	/// By default, can clients create objects. This can be changed per connection after join.
	/// </summary>
	[Title( "Client Object Spawning" )]
	[Group( "Default Client Permissions" )]
	public bool ClientsCanSpawnObjects { get; set; } = true;

	/// <summary>
	/// By default, can clients refresh objects. This can be changed per connection after join.
	/// </summary>
	[Title( "Client Object Refreshing" )]
	[Group( "Default Client Permissions" )]
	public bool ClientsCanRefreshObjects { get; set; } = true;

	/// <summary>
	/// By default, can clients destroy objects. This can be changed per connection after join.
	/// </summary>
	[Title( "Client Object Destroying" )]
	[Group( "Default Client Permissions" )]
	public bool ClientsCanDestroyObjects { get; set; } = true;

	/// <summary>
	/// The frequency at which the network system will send updates to clients. Higher is better but
	/// you probably want to stay in the 10-60 range.
	/// </summary>
	public float UpdateRate { get; set; } = 30;

}
