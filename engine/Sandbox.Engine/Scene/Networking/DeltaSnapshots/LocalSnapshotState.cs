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
		public int Slot;
		public byte[] Value;
		public ulong Hash;

		/// <summary>
		/// Stable position of this entry, connections keep what they have of it at this index.
		/// </summary>
		public int Index;

		/// <summary>
		/// When the value last changed, see <see cref="NextChange"/>.
		/// </summary>
		public long Change;

		/// <summary>
		/// Until a connection has it, it goes with every send of this object, for values that can't be read without it.
		/// </summary>
		public bool SendWithChanges;
	}

	// Not pre-sized: one of these exists per networked object
	public readonly List<Entry> Entries = new();
	public readonly HashSet<int> RemovedSlots = new();
	public readonly Dictionary<int, Entry> Lookup = new();

	/// <summary>
	/// Value changes are numbered from one counter, so whether a connection has a value is a comparison.
	/// </summary>
	static long NextChange;

	/// <summary>
	/// The latest <see cref="Entry.Change"/> of any entry, or of a removal.
	/// </summary>
	public long LastChange { get; private set; }

	/// <summary>
	/// One more than the highest <see cref="Entry.Index"/> in use.
	/// </summary>
	public int IndexCount { get; private set; }

	readonly Stack<int> _freeIndices = new();

	/// <summary>
	/// <see cref="Entry.Change"/> by <see cref="Entry.Index"/>, 0 for unused indices. Connections compare against this
	/// in a tight loop, without touching the entries.
	/// </summary>
	public long[] ChangeByIndex => _changeByIndex;
	long[] _changeByIndex = [];

	/// <summary>
	/// The entry at each <see cref="Entry.Index"/>, null for unused indices.
	/// </summary>
	public Entry[] EntryByIndex => _entryByIndex;
	Entry[] _entryByIndex = [];

	/// <summary>
	/// Bumped to make every connection forget what it has of this object, see <see cref="ClearConnections"/>.
	/// </summary>
	public int ClearGeneration { get; private set; }

	/// <summary>
	/// Set by the snapshot system to its connection set once every connection has every value, -1 otherwise.
	/// </summary>
	public int AckedByConnections { get; set; } = -1;

	/// <summary>
	/// The snapshot system's index for this object, -1 until it first sends it.
	/// </summary>
	internal int NetIndex { get; set; } = -1;

	public uint SnapshotId { get; set; }
	public ushort Version { get; set; }
	public Guid ObjectId { get; set; }
	public int Size { get; private set; }

	private bool _isHashInvalid { get; set; }

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
			_isHashInvalid = true;

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
			_isHashInvalid = true;

			field = value;
		}
	}

	private byte[] _parentIdBytes;
	private byte[] _flagsBytes;

	private static readonly XxHash3 Hasher = new();

	/// <summary>
	/// Call this every time you begin updating the snapshot state.
	/// </summary>
	public void Begin()
	{
		_isHashInvalid = false;
	}

	/// <summary>
	/// Forget what every connection has of this object, so everything is sent to them again.
	/// </summary>
	public void ClearConnections()
	{
		ClearGeneration++;
		AckedByConnections = -1;
	}

	/// <summary>
	/// Remove an existing entry from the specified slot.
	/// </summary>
	public void Remove( int slot )
	{
		if ( !Lookup.TryGetValue( slot, out var entry ) )
			return;

		RemovedSlots.Add( slot );

		Entries.Remove( entry );
		Lookup.Remove( slot );
		_freeIndices.Push( entry.Index );
		ChangeByIndex[entry.Index] = 0;
		EntryByIndex[entry.Index] = null;
		LastChange = ++NextChange;
		AckedByConnections = -1;

		Size -= entry.Value.Length;
	}

	/// <summary>
	/// Compute a hash from the specified byte array.
	/// </summary>
	public ulong Hash( byte[] value )
	{
		Hasher.Reset();
		Hasher.Append( value );
		return Hasher.GetCurrentHashAsUInt64();
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
				Hash = hash,
				Index = _freeIndices.Count > 0 ? _freeIndices.Pop() : IndexCount++
			};

			if ( IndexCount > ChangeByIndex.Length )
			{
				var size = Math.Max( 8, ChangeByIndex.Length * 2 );
				Array.Resize( ref _changeByIndex, size );
				Array.Resize( ref _entryByIndex, size );
			}

			EntryByIndex[entry.Index] = entry;
			Entries.Add( entry );
			Lookup[slot] = entry;
		}

		entry.Change = LastChange = ++NextChange;
		ChangeByIndex[entry.Index] = entry.Change;
		AckedByConnections = -1;

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
		Hasher.Reset();
		Hasher.Append( value );

		if ( (hashFlags & HashFlags.WithParentId) != 0 && _parentIdBytes is not null )
			Hasher.Append( _parentIdBytes );

		if ( (hashFlags & HashFlags.WithNetworkFlags) != 0 && _flagsBytes is not null )
			Hasher.Append( _flagsBytes );

		var hash = Hasher.GetCurrentHashAsUInt64();
		AddSerialized( slot, value, hash );
	}

	/// <summary>
	/// Add from a <see cref="SnapshotValueCache"/> cache. Can optionally choose to add the
	/// parent <see cref="Guid"/> as a salt when hashing the value.
	/// </summary>
	public void AddCached<T>( SnapshotValueCache cache, int slot, in T value, HashFlags hashFlags = HashFlags.Default )
	{
		var cached = cache.GetCached( slot, value, out var isEqual );

		if ( isEqual && !_isHashInvalid )
			return;

		AddSerialized( slot, cached, hashFlags );
	}
}
