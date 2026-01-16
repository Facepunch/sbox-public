using Sandbox.Hashing;

namespace Sandbox.Network;

/// <summary>
/// Represents the current local snapshot state for a networked object. This will contain entries that will
/// be sent to other clients.
/// </summary>
internal class LocalSnapshotState
{
	internal class Entry
	{
		public readonly HashSet<Guid> Connections = [];
		public int Slot;
		public byte[] Value;
		public ulong Hash;
	}

	public readonly List<Entry> Entries = new( 128 );
	public readonly HashSet<int> RemovedSlots = new( 128 );
	public readonly Dictionary<int, Entry> Lookup = new( 128 );
	public readonly HashSet<Guid> UpdatedConnections = new( 128 );

	public ushort SnapshotId { get; set; }
	public ushort Version { get; set; }
	public Guid ObjectId { get; set; }
	public int Size { get; private set; }

	[Flags]
	public enum HashFlags
	{
		Default = 0,

		/// <summary>
		/// Hash the value with the unique <see cref="Guid"/> of the parent.
		/// </summary>
		WithParentId = 1,

		/// <summary>
		/// Hash the value with the value of the network flags.
		/// </summary>
		WithNetworkFlags = 2,

		/// <summary>
		/// Hash the value with all other hashable values.
		/// </summary>
		All = WithParentId | WithNetworkFlags
	}

	/// <summary>
	/// The unique <see cref="Guid"/> of the networked object's parent.
	/// </summary>
	public Guid ParentId
	{
		get;
		set
		{
			if ( _parentIdBytes != null && field == value )
				return;

			_parentIdBytes ??= new byte[16];

			value.TryWriteBytes( _parentIdBytes );
			field = value;
		}
	}

	/// <summary>
	/// Network flags which describe the behavior of this network object.
	/// </summary>
	public NetworkFlags Flags
	{
		get;
		set
		{
			if ( _flagsBytes != null && field == value )
				return;

			_flagsBytes ??= new byte[1];
			_flagsBytes[0] = (byte)value;

			field = value;
		}
	}

	private byte[] _parentIdBytes;
	private byte[] _flagsBytes;

	private readonly XxHash3 _hasher = new();

	/// <summary>
	/// Remove a connection from stored state acknowledgements.
	/// </summary>
	/// <param name="id"></param>
	public void RemoveConnection( Guid id )
	{
		UpdatedConnections.Remove( id );

		foreach ( var entry in Entries )
		{
			entry.Connections.Remove( id );
		}
	}

	/// <summary>
	/// Clear all connections from stored state acknowledgements.
	/// </summary>
	public void ClearConnections()
	{
		UpdatedConnections.Clear();

		foreach ( var entry in Entries )
		{
			entry.Connections.Clear();
		}
	}

	/// <summary>
	/// Remove an existing entry from the specified slot.
	/// </summary>
	public void Remove( int slot )
	{
		if ( !Lookup.TryGetValue( slot, out var entry ) )
			return;

		UpdatedConnections.Clear();
		entry.Connections.Clear();
		RemovedSlots.Add( slot );

		Entries.Remove( entry );
		Lookup.Remove( slot );

		Size -= entry.Value.Length;
	}

	/// <summary>
	/// Compute a hash from the specified byte array.
	/// </summary>
	public ulong Hash( byte[] value )
	{
		_hasher.Reset();
		_hasher.Append( value );
		return _hasher.GetCurrentHashAsUInt64();
	}

	/// <summary>
	/// Add a serialized byte array value to the specified slot with the specified hash.
	/// </summary>
	/// <param name="slot"></param>
	/// <param name="value"></param>
	/// <param name="hash"></param>
	public void AddSerialized( int slot, byte[] value, ulong hash )
	{
		if ( Lookup.TryGetValue( slot, out var entry ) )
		{
			if ( entry.Hash == hash )
				return;

			Size -= entry.Value.Length;

			entry.Hash = hash;
			entry.Value = value;
		}
		else
		{
			entry = new Entry
			{
				Slot = slot,
				Value = value,
				Hash = hash
			};

			Entries.Add( entry );
			Lookup[slot] = entry;
		}

		entry.Connections.Clear();
		UpdatedConnections.Clear();

		Size += value.Length;
	}

	/// <summary>
	/// Add a serialized byte array value to the specified slot. Can optionally choose to add the
	/// parent <see cref="Guid"/> as a salt when hashing the value, if the value is related to the parent.
	/// </summary>
	/// <param name="slot"></param>
	/// <param name="value"></param>
	/// <param name="hashFlags"></param>
	public void AddSerialized( int slot, byte[] value, HashFlags hashFlags = HashFlags.Default )
	{
		_hasher.Reset();
		_hasher.Append( value );

		if ( (hashFlags & HashFlags.WithParentId) != 0 && _parentIdBytes is not null )
			_hasher.Append( _parentIdBytes );

		if ( (hashFlags & HashFlags.WithNetworkFlags) != 0 && _flagsBytes is not null )
			_hasher.Append( _flagsBytes );

		var hash = _hasher.GetCurrentHashAsUInt64();

		AddSerialized( slot, value, hash );
	}

	/// <summary>
	/// Add from a <see cref="SnapshotValueCache"/> cache. Can optionally choose to add the
	/// parent <see cref="Guid"/> as a salt when hashing the value.
	/// </summary>
	public void AddCached<T>( SnapshotValueCache cache, int slot, T value, HashFlags hashFlags = HashFlags.Default )
	{
		AddSerialized( slot, cache.GetCached( slot, value ), hashFlags );
	}
}
