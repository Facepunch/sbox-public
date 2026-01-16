using Sandbox.Network;

namespace Sandbox;

/// <summary>
/// A network table that can be used with <see cref="SyncAttribute">[Sync]</see>. It will be serialized
/// fully for clients when they first need it, and then it will be sent as delta snapshots when individual
/// entries change.
/// </summary>
internal class NetworkTable<T> : INetworkDeltaSnapshot, INetworkProperty, INetworkSerializer
{
	protected Dictionary<int, byte[]> Serialized { get; set; } = new();
	protected Dictionary<int, T> Table { get; set; } = new();

	protected byte[] SerializedKeys { get; set; }
	protected HashSet<int> Keys { get; set; } = new();

	private bool CanWriteChanges() => !Parent?.IsProxy ?? true;
	private readonly Dictionary<int, int> _subSlotHashes = new();
	private HashSet<int> KeysToRemove { get; set; } = [];

	protected int _parentSlot;
	protected int _keysHash;

	/// <summary>
	/// Read-only access for all entries in the network table.
	/// </summary>
	public IReadOnlyDictionary<int, T> Entries => Table;

	/// <summary>
	/// Clear the network table.
	/// </summary>
	public void Clear()
	{
		if ( !CanWriteChanges() )
			return;

		InternalClear();
	}

	void InternalClear()
	{
		_subSlotHashes.Clear();
		SerializedKeys = [];
		Table.Clear();
		Serialized.Clear();
		Keys.Clear();

		OnCleared();
	}

	/// <summary>
	/// Set the value of an entry in the specified slot.
	/// </summary>
	public void Set( int slot, T value )
	{
		if ( EqualityComparer<T>.Default.Equals( Get( slot ), value ) )
			return;

		if ( !CanWriteChanges() )
			return;

		Serialized[slot] = Game.TypeLibrary.ToBytes( value );
		Table[slot] = value;

		OnValueChanged( slot, value );

		if ( !Keys.Add( slot ) )
			return;

		WriteSerializedKeys();
		UpdateSubSlotHash( slot );
	}

	/// <summary>
	/// Get the value of an entry in the specified slot.
	/// </summary>
	public T Get( int slot )
	{
		return Table.GetValueOrDefault( slot );
	}

	void WriteSerializedKeys()
	{
		using var bs = ByteStream.Create( 1024 );
		bs.Write( Keys.Count );

		foreach ( var key in Keys )
		{
			bs.Write( key );
		}

		SerializedKeys = bs.ToArray();
	}

	void UpdateSubSlotHash( int slot )
	{
		if ( _parentSlot == 0 )
			return;

		_subSlotHashes[slot] = $"{_parentSlot}.{slot}".FastHash();
	}

	void ReadSerializedKeys( byte[] data )
	{
		if ( data.Length == 0 )
		{
			SerializedKeys = data;
			Keys.Clear();
			return;
		}

		using var reader = ByteStream.CreateReader( data );
		var count = reader.Read<int>();

		Keys.Clear();

		for ( var i = 0; i < count; i++ )
		{
			Keys.Add( reader.Read<int>() );
		}

		SerializedKeys = data;
	}

	private INetworkProxy Parent { get; set; }

	void INetworkProperty.Init( int slot, INetworkProxy parent )
	{
		Parent = parent;

		_parentSlot = slot;
		_keysHash = $"{slot}.keys".FastHash();

		foreach ( var key in Keys )
		{
			_subSlotHashes[key] = $"{slot}.{key}".FastHash();
		}

		OnInit( slot );
	}

	protected virtual void WriteSnapshot( int slot, LocalSnapshotState snapshot )
	{
		foreach ( var entry in Serialized )
		{
			var subSlot = _subSlotHashes.GetValueOrDefault( entry.Key );

			if ( subSlot == 0 )
			{
				subSlot = $"{_parentSlot}.{entry.Key}".FastHash();
				_subSlotHashes[entry.Key] = subSlot;
			}

			snapshot.AddSerialized( subSlot, entry.Value );
		}
	}

	void INetworkDeltaSnapshot.WriteSnapshotState( int slot, LocalSnapshotState snapshot )
	{
		if ( SerializedKeys is not null )
		{
			snapshot.AddSerialized( _keysHash, SerializedKeys );
		}

		WriteSnapshot( slot, snapshot );
	}

	protected virtual void ReadSnapshot( int slot, DeltaSnapshot snapshot )
	{
		foreach ( var key in Keys )
		{
			var subSlot = _subSlotHashes.GetValueOrDefault( key );

			if ( subSlot == 0 )
			{
				subSlot = $"{_parentSlot}.{key}".FastHash();
				_subSlotHashes[key] = subSlot;
			}

			if ( !snapshot.TryGetValue<T>( subSlot, out var value ) )
				continue;

			Serialized[key] = snapshot.Lookup[subSlot].Value;
			Table[key] = value;
		}
	}

	void INetworkDeltaSnapshot.ReadSnapshot( int slot, DeltaSnapshot snapshot )
	{
		if ( snapshot.Lookup.TryGetValue( _keysHash, out var keys ) )
		{
			ReadSerializedKeys( keys.Value );
		}

		ReadSnapshot( slot, snapshot );

		PruneRemovedEntries();
	}

	void PruneRemovedEntries()
	{
		foreach ( var entry in Table.Where( entry => !Keys.Contains( entry.Key ) ) )
		{
			KeysToRemove.Add( entry.Key );
		}

		if ( KeysToRemove.Count == 0 )
			return;

		foreach ( var key in KeysToRemove )
		{
			OnKeyRemoved( key );
			_subSlotHashes.Remove( key );
			Serialized.Remove( key );
			Table.Remove( key );
		}

		KeysToRemove.Clear();
	}

	void INetworkSerializer.WriteChanged( ref ByteStream data )
	{

	}

	void INetworkSerializer.WriteAll( ref ByteStream data )
	{
		data.Write( Serialized.Count );

		foreach ( var entry in Serialized )
		{
			data.Write( entry.Key );
			data.Write( entry.Value.Length );
			data.Write( entry.Value );
		}
	}

	void INetworkSerializer.Read( ref ByteStream data )
	{
		var count = data.Read<int>();

		if ( count <= 0 )
		{
			InternalClear();
			return;
		}

		for ( var i = 0; i < count; i++ )
		{
			var key = data.Read<int>();
			var byteCount = data.Read<int>();

			var bytes = new byte[byteCount];
			data.Read( bytes, 0, byteCount );

			var value = Game.TypeLibrary.FromBytes<T>( bytes );

			Serialized[key] = bytes;
			Table[key] = value;

			OnValueChanged( key, value );

			if ( !Keys.Add( key ) )
				continue;

			UpdateSubSlotHash( key );
		}

		WriteSerializedKeys();
	}

	/// <summary>
	/// Called when initialized from a parent network table.
	/// </summary>
	/// <param name="parentSlot"></param>
	protected virtual void OnInit( int parentSlot )
	{

	}

	/// <summary>
	/// Called whenever a key is removed from the table.
	/// </summary>
	protected virtual void OnKeyRemoved( int key )
	{

	}

	/// <summary>
	/// Called whenever a value changes in a specific slot.
	/// </summary>
	protected virtual void OnValueChanged( int slot, T value )
	{

	}

	/// <summary>
	/// Called when the network table is cleared.
	/// </summary>
	protected virtual void OnCleared()
	{

	}

	bool INetworkSerializer.HasChanges => false;
}
