namespace Sandbox.Network;

/// <summary>
/// A network system is a bunch of connections that people can send messages 
/// over. Right now it can be a dedicated server, a listen server, a pure client,
/// or a p2p system.
/// </summary>
internal enum InternalMessageType : byte
{
	Unknown,

	/// <summary>
	/// A small message sent from the host to the client, and then returned to measure latency and keep
	/// everything in sync.
	/// </summary>
	HeartbeatPing,
	HeartbeatPong,

	/// <summary>
	/// Is a struct packed using TypeLibrary
	/// </summary>
	Packed,

	/// <summary>
	/// A delta-based tick sent by the client
	/// </summary>
	ClientTick,

	/// <summary>
	/// Set the cull state of a networked object
	/// </summary>
	SetCullState,

	/// <summary>
	/// Is a delta snapshot message
	/// </summary>
	DeltaSnapshot,

	/// <summary>
	/// Is a delta snapshot cluster message
	/// </summary>
	DeltaSnapshotCluster,

	/// <summary>
	/// Is a delta snapshot acknowledgement
	/// </summary>
	DeltaSnapshotAck,

	/// <summary>
	/// Is a delta snapshot cluster acknowledgement
	/// </summary>
	DeltaSnapshotClusterAck,

	//
	// Data Tables
	//
	TableSnapshot,
	TableUpdated,

	/// <summary>
	/// A request, this is a guid, then another message
	/// </summary>
	Request,

	/// <summary>
	/// A response, this is a guid, then another message
	/// </summary>
	Response,
}
