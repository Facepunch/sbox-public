namespace Sandbox;

/// <summary>
/// Controls network message delivery and RPC caller permissions.
/// </summary>
[Flags, Expose]
public enum NetFlags : uint
{
	/// <summary>
	/// Message will be sent unreliably. It may not arrive and it may be received out of order. But chances
	/// are that it will arrive on time and everything will be fine. This is good for sending position updates,
	/// or spawning effects. This is the fastest way to send a message. It is also the cheapest.
	/// </summary>
	Unreliable = 1,

	/// <summary>
	/// Message will be sent reliably. Multiple attempts will be made until the recipient has received it. Use this for things
	/// like chat messages, or important events. This is the slowest way to send a message. It is also the most expensive.
	/// </summary>
	Reliable = 2,

	/// <summary>
	/// Message will not be grouped up with other messages, and will be sent immediately. This is most useful for things like 
	/// streaming voice data, where packets need to stream in real-time, rather than arriving with a bunch of other packets.
	/// </summary>
	SendImmediate = 4,

	/// <summary>
	/// Message will be dropped if it can't be sent quickly. Only applicable to unreliable messages.
	/// </summary>
	DiscardOnDelay = 8,

	/// <summary>
	/// Only the host may call this action
	/// </summary>
	HostOnly = 16,

	/// <summary>
	/// Only the owner may call this action
	/// </summary>
	OwnerOnly = 32,

	/// <summary>
	/// Message will be sent unreliably, not grouped up with other messages and will be dropped if it can't be sent quickly.
	/// </summary>
	UnreliableNoDelay = Unreliable | SendImmediate | DiscardOnDelay
}

/// <summary>
/// Specifies who can invoke an action over the network.
/// </summary>
[Expose]
internal enum RpcMode
{
	/// <summary>
	/// Send to everyone
	/// </summary>
	Broadcast,

	/// <summary>
	/// Send to the owner of this
	/// </summary>
	Owner,

	/// <summary>
	/// Only send to the host.
	/// </summary>
	Host
}
