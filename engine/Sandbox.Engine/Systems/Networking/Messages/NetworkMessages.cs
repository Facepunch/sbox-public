namespace Sandbox.Network;

[Expose]
struct ChannelInfo
{
	public Guid Id { get; set; }
}

[Expose]
struct KickMsg
{
	public string Reason { get; set; }
}

[Expose]
struct RestartHandshakeMsg
{

}

[Expose]
public struct HostStats
{
	public float OutBytesPerSecond { get; set; }
	public float InBytesPerSecond { get; set; }
	public ushort Fps { get; set; }
}

[Expose]
struct ServerNameMsg
{
	public string Name { get; set; }
}

[Expose]
struct ServerDataMsg
{
	public string Name { get; set; }
	public string Value { get; set; }
}

[Expose]
struct MapNameMsg
{
	public string Name { get; set; }
}

[Expose]
struct ServerInfo
{
	public Dictionary<string, string> ServerData { get; set; }
	public string ServerName { get; set; }
	public string MapName { get; set; }
	public ChannelInfo Host { get; set; }
	public ChannelInfo Assigned { get; set; }
	public int MaxPlayers { get; set; }
	public int EngineVersion { get; set; }
	public string GamePackage { get; set; }
	public string MapPackage { get; set; }
	public Guid HandshakeId { get; set; }

	/// <summary>
	/// If true then this host is being run from an editor, as such the assemblies
	/// are sent via network tables and loading assemblies from the package is not required.
	/// </summary>
	public bool IsDeveloperHost { get; set; }
}

/// <summary>
/// A console command was run on a client but is being forwarded to the server. This is the message
/// that contains the details of that command.
/// </summary>
[Expose]
struct ServerCommand
{
	public string Command { get; set; }
	public string Args { get; set; }
}

/// <summary>
/// A UserInfo ConVar value was changed on a client. This message is intended for the host so that they
/// can propagate this change to all clients.
/// </summary>
[Expose]
struct UserInfoUpdate
{
	public string Command { get; set; }
	public string Value { get; set; }
}

/// <summary>
/// A simple log message packet. This is used by <see cref="Connection.SendLog"/> when logging
/// to another client's console.
/// </summary>
[Expose]
struct LogMsg
{
	public string Message { get; set; }
	public byte Level { get; set; }
}

[Expose]
struct Welcome
{
	public Guid HandshakeId { get; set; }
}

[Expose]
public record struct NetworkFile( string Name, byte[] Content )
{

}

[Expose]
struct RequestMountedVPKs
{
	public Guid HandshakeId { get; set; }
}

[Expose]
public struct MountedVPKsResponse
{
	public List<string> MountedVPKs { get; set; }
	public Guid HandshakeId { get; set; }
}

[Expose]
struct RequestInitialSnapshot
{
	public Dictionary<string, string> UserData { get; set; }
	public Guid HandshakeId { get; set; }
}

[Expose]
public struct InitialSnapshotResponse
{
	public SnapshotMsg Snapshot { get; set; }
	public Guid HandshakeId { get; set; }
}

[Expose]
public struct SnapshotMsg
{
	public double Time { get; set; }
	public string SceneData { get; set; }
	public List<object> NetworkObjects { get; init; }
	public List<GameObjectSystemData> GameObjectSystems { get; set; }

	[Expose]
	public struct GameObjectSystemData
	{
		public byte[] SnapshotData { get; set; }
		public byte[] TableData { get; set; }
		public int Type { get; set; }
		public Guid Id { get; set; }
	}
}

[Expose]
struct ClientReady
{
	public Guid HandshakeId { get; set; }
}

[Expose]
struct Activate
{
	public Guid HandshakeId { get; set; }
}

/// <summary>
/// Sent to the server to tell clients to reconnect. This is sent when
/// the server is changing games, or maps, and wants the current players
/// to follow them to the new game, or map.
/// We send the Game and Map to the best of our knowledge, so the client
/// can maybe preload them, while we are.
/// </summary>
[Expose]
public struct ReconnectMsg
{
	public string Game { get; set; }
	public string Map { get; set; }
}
