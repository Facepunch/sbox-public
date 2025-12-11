using Sandbox.Hashing;

namespace Sandbox.Network;

/// <summary>
/// Represents the current snapshot state for an object based on delta snapshots received
/// from another client.
/// </summary>
internal class RemoteSnapshotState
{
	/// <summary>
	/// How many seconds we'll allow to pass for a snapshot acknowledgement packet
	/// to be received from a client.
	/// </summary>
	private const float MaximumAckResponseTime = 0.25f;

	private record struct PredictedEntry( byte[] Value, float ExpireTime, ulong Hash );
	public record struct Entry( ushort SnapshotId, byte[] Data, ulong Hash );

	private readonly Dictionary<int, PredictedEntry> _predictedData = new( 128 );
	public ushort SnapshotId { get; set; }
	public Guid ObjectId { get; init; }

	public readonly Dictionary<int, Entry> Data = new( 128 );

	/// <summary>
	/// Whether the incoming snapshot id is newer than our last processed one. This
	/// automatically handles wrapping of the ushort.
	/// </summary>
	internal static bool IsNewer( ushort newId, ushort lastId )
	{
		var difference = (short)(newId - lastId);

		switch ( difference )
		{
			case 0: // The snapshot is older if there's no difference.
				return false;
			case > 0: // The snapshot is newer if the id is larger.
				return true;
			case > -100: // The snapshot is older if it's less than 100 behind.
				return false;
			default: // Otherwise the snapshot is newer.
				return true;
		}
	}

	/// <summary>
	/// Add a predicted entry to the snapshot from a <see cref="DeltaSnapshot.SnapshotDataEntry"/>.
	/// </summary>
	/// <param name="input"></param>
	/// <param name="timeNow"></param>
	public void AddPredicted( in DeltaSnapshot.SnapshotDataEntry input, float timeNow )
	{
		_predictedData[input.Slot] = new PredictedEntry( input.Value, timeNow + MaximumAckResponseTime, input.Hash );
	}

	/// <summary>
	/// Update the value in the stored snapshot from a <see cref="DeltaSnapshot.SnapshotDataEntry"/>.
	/// </summary>
	public void Update( in DeltaSnapshot.SnapshotDataEntry input, ushort snapshotId )
	{
		if ( Data.TryGetValue( input.Slot, out var entry ) && !IsNewer( snapshotId, entry.SnapshotId ) )
			return;

		_predictedData.Remove( input.Slot );
		Data[input.Slot] = new Entry( snapshotId, input.Value, input.Hash );
	}

	/// <summary>
	/// Try to get the hash of the value from the specified slot without reading from
	/// the predicted data. It will only return true if the entry is from the same or
	/// older snapshot.
	/// </summary>
	public bool IsValueHashEqual( int slot, ulong hash, ushort snapshotId )
	{
		if ( Data.TryGetValue( slot, out var e )
			 && ((snapshotId == e.SnapshotId) || IsNewer( snapshotId, e.SnapshotId )) )
		{
			return e.Hash == hash;
		}

		return false;
	}

	/// <summary>
	/// Try to get the hash of the value from the specified slot.
	/// </summary>
	public bool TryGetHash( int slot, out ulong hash, float timeNow )
	{
		if ( _predictedData.TryGetValue( slot, out var predicted ) && timeNow <= predicted.ExpireTime )
		{
			hash = predicted.Hash;
			return true;
		}

		if ( Data.TryGetValue( slot, out var e ) )
		{
			hash = e.Hash;
			return true;
		}

		hash = 0;
		return false;
	}

	/// <summary>
	/// Try to get the serialized byte array value from the specified slot.
	/// </summary>
	public bool TryGetValue( int slot, out byte[] value, float timeNow )
	{
		if ( _predictedData.TryGetValue( slot, out var predicted ) && timeNow <= predicted.ExpireTime )
		{
			value = predicted.Value;
			return true;
		}

		if ( Data.TryGetValue( slot, out var e ) )
		{
			value = e.Data;
			return true;
		}

		value = null;
		return false;
	}

	/// <summary>
	/// Create a new delta snapshot using the values of this snapshot state but only with the slots
	/// from the provided <see cref="DeltaSnapshot"/>.
	/// </summary>
	public DeltaSnapshot ToDeltaSnapshot( ushort snapshotId, ushort version, IEnumerable<int> slots, float timeNow )
	{
		var result = new DeltaSnapshot
		{
			SnapshotId = snapshotId,
			ObjectId = ObjectId,
			Version = version
		};

		foreach ( var slot in slots )
		{
			if ( TryGetValue( slot, out var value, timeNow ) )
			{
				result.AddSerialized( slot, value );
			}
		}

		return result;
	}

	/// <summary>
	/// Build a new <see cref="RemoteSnapshotState"/> from the specified <see cref="DeltaSnapshot"/>.
	/// </summary>
	public static RemoteSnapshotState From( Guid sourceId, DeltaSnapshot delta )
	{
		var snapshot = new RemoteSnapshotState
		{
			ObjectId = delta.ObjectId
		};

		foreach ( var entry in delta.Entries )
		{
			if ( (!entry.Connections?.Contains( sourceId ) ?? false) )
				continue;

			snapshot.Update( entry, delta.SnapshotId );
		}

		return snapshot;
	}
}
