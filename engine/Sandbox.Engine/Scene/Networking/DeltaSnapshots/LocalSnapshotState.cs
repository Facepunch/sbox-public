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
	public readonly Dictionary<int, Entry> Lookup = new( 128 );
	public readonly HashSet<Guid> UpdatedConnections = new( 128 );

	public ushort SnapshotId { get; set; }
	public ushort Version { get; set; }
	public Guid ObjectId { get; set; }
	public int Size { get; private set; }

	/// <summary>
	/// The unique <see cref="Guid"/> of the networked object's parent. Some values in the snapshot state
	/// may be specific to the parent object - such as the local transform. In these cases,
	/// this can be used as a salt when hashing those values, so if the parent changes, the
	/// values will be re-transmitted to connections.
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

	private byte[] _parentIdBytes;

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
	/// Add a serialized byte array value to the specified slot. Can optionally choose to add the
	/// parent <see cref="Guid"/> as a salt when hashing the value, if the value is related to the parent.
	/// </summary>
	/// <param name="slot"></param>
	/// <param name="value"></param>
	/// <param name="hashWithParentId"></param>
	public void AddSerialized( int slot, byte[] value, bool hashWithParentId = false )
	{
		_hasher.Reset();
		_hasher.Append( value );

		if ( hashWithParentId && _parentIdBytes is not null )
			_hasher.Append( _parentIdBytes );

		var hash = _hasher.GetCurrentHashAsUInt64();

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
	/// Add from a <see cref="SnapshotValueCache"/> cache. Can optionally choose to add the
	/// parent <see cref="Guid"/> as a salt when hashing the value, if the value is related to the parent.
	/// </summary>
	public void AddCached<T>( SnapshotValueCache cache, int slot, T value, bool hashWithParentId = false )
	{
		AddSerialized( slot, cache.GetCached( slot, value ), hashWithParentId );
	}
}
