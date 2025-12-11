using Sandbox.Engine;
using Sandbox.Hashing;

using Sandbox.Network;

internal class DeltaSnapshot : IObjectPoolEvent
{
	public static NetworkObjectPool<DeltaSnapshot> Pool { get; } = new();

	private int ReferenceCount { get; set; }

	/// <summary>
	/// Add to reference count for this object.
	/// </summary>
	public void AddReference()
	{
		ReferenceCount++;
	}

	/// <summary>
	/// Release a reference for this object, and return it to the pool
	/// if nothing else is referencing it.
	/// </summary>
	public void Release()
	{
		if ( ReferenceCount == 0 )
			throw new InvalidOperationException( "ReferenceCount is already zero" );

		ReferenceCount--;

		if ( ReferenceCount <= 0 )
		{
			Pool.Return( this );
		}
	}

	void IObjectPoolEvent.OnRented()
	{
		TimeSinceCreated = 0f;
		ReferenceCount = 1;
	}

	void IObjectPoolEvent.OnReturned()
	{
		foreach ( var entry in Entries )
		{
			if ( entry.Connections is null )
				continue;

			entry.Connections.Clear();
			ObjectPool<HashSet<Guid>>.Return( entry.Connections );
		}

		Lookup.Clear();
		Entries.Clear();
		LocalState = null;
		Source = null;
		Size = 0;
	}

	public struct SnapshotDataEntry : IEquatable<SnapshotDataEntry>
	{
		public LocalSnapshotState.Entry LocalState;
		public HashSet<Guid> Connections;
		public int Slot;
		public byte[] Value;
		public ulong Hash;

		public bool Equals( SnapshotDataEntry other )
		{
			return Slot == other.Slot && Hash == other.Hash;
		}

		public override bool Equals( object obj )
		{
			return obj is SnapshotDataEntry other && Equals( other );
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( Slot, Hash );
		}
	}

	public readonly List<SnapshotDataEntry> Entries = new( 128 );
	public readonly Dictionary<int, SnapshotDataEntry> Lookup = new( 128 );

	public LocalSnapshotState LocalState { get; set; }
	public RealTimeSince TimeSinceCreated { get; private set; } = 0f;
	public ushort Version { get; set; }
	public IDeltaSnapshot Source { get; set; }
	public ushort SnapshotId { get; set; }
	public Guid ObjectId { get; set; }
	public int Size { get; private set; }

	public IEnumerable<int> Keys => Lookup.Keys;

	/// <summary>
	/// Add a serialized byte array value to the specified slot.
	/// </summary>
	/// <param name="slot"></param>
	/// <param name="value"></param>
	public void AddSerialized( int slot, byte[] value )
	{
		var hash = XxHash3.HashToUInt64( value );
		var entry = new SnapshotDataEntry
		{
			Slot = slot,
			Value = value,
			Hash = hash
		};

		Entries.Add( entry );
		Lookup[slot] = entry;

		Size += value.Length;
	}

	/// <summary>
	/// Try to get a deserialized value from the specified slot.
	/// </summary>
	/// <param name="slot"></param>
	/// <param name="value"></param>
	public bool TryGetValue<T>( int slot, out T value )
	{
		if ( Lookup.TryGetValue( slot, out var entry ) )
		{
			value = GlobalContext.Current.TypeLibrary.FromBytes<T>( entry.Value );
			return true;
		}

		value = default;
		return false;
	}

	/// <summary>
	/// Copy the data from a <see cref="LocalSnapshotState"/>.
	/// </summary>
	public void CopyFrom( IDeltaSnapshot snapshotter, LocalSnapshotState state, int connectionCount )
	{
		LocalState = state;
		Source = snapshotter;
		SnapshotId = state.SnapshotId;
		ObjectId = state.ObjectId;
		Version = state.Version;
		Size = 0;

		foreach ( var entry in state.Entries )
		{
			// Conna: why bother processing this entry at all if all clients that we know
			// of have the latest value.
			if ( entry.Connections.Count == connectionCount )
				continue;

			var data = new SnapshotDataEntry
			{
				Connections = ObjectPool<HashSet<Guid>>.Get(),
				LocalState = entry,
				Slot = entry.Slot,
				Value = entry.Value,
				Hash = entry.Hash
			};

			Entries.Add( data );
			Lookup[entry.Slot] = data;

			Size += entry.Value.Length;
		}
	}

	/// <summary>
	/// Build a <see cref="DeltaSnapshot"/> from the specified dictionary of type and value.
	/// </summary>
	public static DeltaSnapshot From( Dictionary<int, byte[]> data )
	{
		var snapshot = new DeltaSnapshot();

		foreach ( var (slot, value) in data )
		{
			snapshot.AddSerialized( slot, value );
		}

		return snapshot;
	}
}
