namespace Sandbox.Network;

internal interface IDeltaSnapshot
{
	/// <summary>
	/// Unique identifier for this object.
	/// </summary>
	Guid Id { get; }

	/// <summary>
	/// Whether this object is a proxy (or we own it.)
	/// </summary>
	bool IsProxy { get; }

	/// <summary>
	/// Current snapshot version for this object.
	/// </summary>
	ushort SnapshotVersion { get; }

	/// <summary>
	/// Called when a snapshot is received by a <see cref="Connection"/>.
	/// </summary>
	/// <param name="source"></param>
	/// <param name="snapshot"></param>
	/// <returns></returns>
	bool OnSnapshot( Connection source, DeltaSnapshot snapshot );

	/// <summary>
	/// Update the transmit state for the target <see cref="Connection">connections</see>. This method
	/// should return true if we should transmit to ANY of these connections.
	/// </summary>
	bool UpdateTransmitState( Connection[] targets );

	/// <summary>
	/// Should this snapshot transmit to the target <see cref="Connection"/>?
	/// </summary>
	/// <param name="target"></param>
	/// <returns></returns>
	bool ShouldTransmit( Connection target );

	/// <summary>
	/// Write delta snapshot data and return the <see cref="DeltaSnapshot"/> object.
	/// </summary>
	internal LocalSnapshotState WriteSnapshotState();

	/// <summary>
	/// Try to send a network update or do nothing if no update is required. This is most
	/// likely called after WriteSnapshotState.
	/// </summary>
	internal void SendNetworkUpdate( bool queryValues = false );
}
