using System.Collections;

namespace Sandbox.Network;

internal class NetworkTable : IDisposable
{
	/// <summary>
	/// Internal flag set while reading changes. Useful when you want to force
	/// something to be set when we otherwise wouldn't have permission to.
	/// </summary>
	internal static bool IsReadingChanges { get; private set; }

	public class Entry : INetworkProxy
	{
		public Type TargetType { get; init; }
		public string DebugName { get; init; }
		public bool NeedsQuery { get; set; }
		public Func<Connection, bool> ControlCondition { get; init; } = c => true;
		public Func<object> GetValue { get; init; }
		public Action<object> SetValue { get; init; }
		public Action<Entry> OnDirty { get; set; }
		public ulong SnapshotHash { get; set; }
		public int HashCodeValue { get; set; }
		public bool IsSerializerType { get; private set; }
		public bool IsDeltaSnapshotType { get; private set; }
		public bool IsReliableType { get; set; }
		public byte[] Serialized { get; set; }
		public bool Initialized { get; set; }
		public int Slot { get; private set; }

		private bool InternalIsDirty { get; set; }

		public bool IsDirty
		{
			get => InternalIsDirty;
			set
			{
				if ( InternalIsDirty == value )
					return;

				InternalIsDirty = value;
				OnDirty?.Invoke( this );
			}
		}

		bool INetworkProxy.IsProxy => !HasControl( Connection.Local );

		/// <summary>
		/// Whether the specified <see cref="Connection"/> has control of this entry.
		/// </summary>
		public bool HasControl( Connection c )
		{
			return ControlCondition?.Invoke( c ) ?? true;
		}

		/// <summary>
		/// Whether we (our local <see cref="Connection"/>) have control of this entry.
		/// </summary>
		/// <returns></returns>
		public bool HasControl()
		{
			return HasControl( Connection.Local );
		}

		internal void Init( int slot )
		{
			if ( TargetType is null ) return;

			var isListType = TargetType.IsAssignableTo( typeof( IList ) );
			var isDictionaryType = TargetType.IsAssignableTo( typeof( IDictionary ) );

			IsDeltaSnapshotType = TargetType.IsAssignableTo( typeof( INetworkDeltaSnapshot ) );
			IsReliableType = !IsDeltaSnapshotType && TargetType.IsAssignableTo( typeof( INetworkReliable ) );
			IsSerializerType = TargetType.IsAssignableTo( typeof( INetworkSerializer ) );
			NeedsQuery |= (isListType || isDictionaryType || IsSerializerType);
			Slot = slot;
		}
	}

	private readonly Dictionary<int, Entry> _entries = new();
	private readonly List<Entry> _reliableEntries = [];
	private readonly List<Entry> _snapshotEntries = [];
	private readonly List<Entry> _queryEntries = [];

	/// <summary>
	/// Do we have any pending changes for entries we control?
	/// </summary>
	public bool HasAnyChanges => _entries.Values.Any( entry => entry.HasControl() && entry.IsDirty );

	/// <summary>
	/// Do we have any pending reliable changes for entries we control?
	/// </summary>
	public bool HasReliableChanges()
	{
		for ( var i = 0; i < _reliableEntries.Count; i++ )
		{
			var entry = _reliableEntries[i];

			if ( !entry.IsDirty )
				continue;

			if ( entry.HasControl() )
				return true;
		}

		return false;
	}

	public void Dispose()
	{
		_reliableEntries.Clear();
		_snapshotEntries.Clear();
		_queryEntries.Clear();
		_entries.Clear();
	}

	/// <summary>
	/// Unregister a variable assigned to a slot id.
	/// </summary>
	/// <param name="slot"></param>
	public void Unregister( int slot )
	{
		_snapshotEntries.RemoveAll( e => e.Slot == slot );
		_reliableEntries.RemoveAll( e => e.Slot == slot );
		_queryEntries.RemoveAll( e => e.Slot == slot );
		_entries.Remove( slot );
	}

	/// <summary>
	/// Register a variable assigned to a slot id.
	/// </summary>
	public void Register( int slot, Entry entry )
	{
		// Do nothing if we already have an entry in this slot.
		if ( !_entries.TryAdd( slot, entry ) )
			return;

		var value = GetValue( slot );
		UpdateSlotHash( slot, value );

		entry.Init( slot );
		entry.IsDirty = true;

		if ( entry.IsReliableType )
			_reliableEntries.Add( entry );
		else
			_snapshotEntries.Add( entry );

		if ( entry.NeedsQuery )
			_queryEntries.Add( entry );
	}

	/// <summary>
	/// Get a variable from a slot id.
	/// </summary>
	public object GetValue( int slot )
	{
		return !_entries.TryGetValue( slot, out var v ) ? default : v.GetValue();
	}

	/// <summary>
	/// Does a variable with the specified slot exist?
	/// </summary>
	public bool IsRegistered( int slot )
	{
		return _entries.ContainsKey( slot );
	}

	/// <summary>
	/// Do we have control over the value for a specific slot id?
	/// </summary>
	public bool HasControl( int slot )
	{
		return _entries.TryGetValue( slot, out var v ) && v.HasControl();
	}

	/// <summary>
	/// Update the hash for a specific entry.
	/// </summary>
	private void UpdateSlotHash( Entry entry, object value )
	{
		if ( value is INetworkProperty property && !entry.Initialized )
		{
			property.Init( entry.Slot, entry );
			entry.Initialized = true;
		}

		if ( value is INetworkSerializer serializer )
		{
			if ( !serializer.HasChanges )
				return;

			entry.Serialized = null;
			entry.IsDirty = true;
			return;
		}

		var hashValue = ToHashCode( value );

		if ( entry.HashCodeValue == hashValue )
			return;

		entry.HashCodeValue = hashValue;
		entry.Serialized = null;
		entry.IsDirty = true;
	}

	/// <summary>
	/// Update the hash for a specific slot id.
	/// </summary>
	public void UpdateSlotHash( int slot, object value )
	{
		if ( !_entries.TryGetValue( slot, out var v ) )
			return;

		UpdateSlotHash( v, value );
	}

	private static int ToHashCode( object value )
	{
		if ( value is IList list )
		{
			HashCode hc = default;

			hc.Add( list.Count );
			for ( var i = 0; i < list.Count; i++ )
			{
				hc.Add( list[i] );
			}

			return hc.ToHashCode();
		}

		if ( value is IDictionary dictionary )
		{
			HashCode hc = default;

			hc.Add( dictionary.Count );
			foreach ( DictionaryEntry item in dictionary )
			{
				hc.Add( HashCode.Combine( item.Key, item.Value ) );
			}

			return hc.ToHashCode();
		}

		return HashCode.Combine( value );
	}

	/// <summary>
	/// Set a variable from a slot id.
	/// </summary>
	public void SetValue( int slot, object value )
	{
		if ( !_entries.TryGetValue( slot, out var entry ) )
			return;

		try
		{
			var oldValue = entry.GetValue();

			if ( Equals( oldValue, value ) )
				return;

			entry.Initialized = false;
			UpdateSlotHash( slot, value );
			entry.SetValue( value );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when setting value {entry.DebugName} - {e.Message}" );
		}
	}

	/// <summary>
	/// Write supported snapshot variables serialized to the specified dictionary.
	/// </summary>
	/// <param name="snapshot"></param>
	internal void WriteSnapshotState( LocalSnapshotState snapshot )
	{
		for ( var i = 0; i < _snapshotEntries.Count; i++ )
		{
			var entry = _snapshotEntries[i];

			if ( !entry.HasControl() )
				continue;

			if ( entry.IsDeltaSnapshotType )
			{
				var value = entry.GetValue() as INetworkDeltaSnapshot;
				value?.WriteSnapshotState( entry.Slot, snapshot );
				continue;
			}

			try
			{
				if ( entry.Serialized is null )
				{
					var bs = ByteStream.Create( 4096 );
					WriteEntryToStream( entry, ref bs );
					entry.Serialized = bs.ToArray();
					entry.SnapshotHash = snapshot.Hash( entry.Serialized );
					bs.Dispose();
				}

				snapshot.AddSerialized( entry.Slot, entry.Serialized, entry.SnapshotHash );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when getting value {entry.DebugName} - {e.Message}" );
			}
		}
	}

	/// <summary>
	/// Read and apply any variables from the provided snapshot.
	/// </summary>
	/// <param name="source"></param>
	/// <param name="snapshot"></param>
	internal void ReadSnapshot( Connection source, DeltaSnapshot snapshot )
	{
		foreach ( var entry in _snapshotEntries )
		{
			if ( !entry.IsDeltaSnapshotType )
				continue;

			// The connection sending us this can't modify it!
			if ( !entry.HasControl( source ) )
				continue;

			var value = entry.GetValue() as INetworkDeltaSnapshot;
			value?.ReadSnapshot( entry.Slot, snapshot );
		}

		foreach ( var kv in snapshot.Entries )
		{
			var slot = kv.Slot;
			var serialized = kv.Value;

			if ( !_entries.TryGetValue( slot, out var entry ) )
				continue;

			// The connection sending us this can't modify it!
			if ( !entry.HasControl( source ) )
				continue;

			if ( entry.IsReliableType || entry.IsDeltaSnapshotType )
				continue;

			var bs = ByteStream.CreateReader( serialized );

			try
			{
				IsReadingChanges = true;
				ReadEntryFromStream( slot, entry, ref bs );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when reading value {entry.DebugName} - {e.Message}" );
			}
			finally
			{
				IsReadingChanges = false;

				// We're never dirty if we just had our value read.
				entry.IsDirty = false;
				bs.Dispose();
			}
		}
	}

	/// <summary>
	/// Write all reliable variables to the provided <see cref="ByteStream"/>.
	/// </summary>
	/// <param name="data"></param>
	public void WriteAllReliable( ref ByteStream data )
	{
		var container = ByteStream.Create( 32 );
		var count = 0;

		foreach ( var entry in _reliableEntries )
		{
			var bs = ByteStream.Create( 32 );

			try
			{
				WriteEntryToStream( entry, ref bs );

				container.Write( entry.Slot );
				container.Write( bs.Length );

				if ( bs.Length > 0 )
					container.Write( bs );

				count++;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when getting value {entry.DebugName} - {e.Message}" );
			}
			finally
			{
				bs.Dispose();
			}
		}

		data.Write( count );

		if ( count > 0 )
		{
			data.Write( container.Length );
			data.Write( container );
		}

		container.Dispose();
	}

	/// <summary>
	/// Write all variables to the provided <see cref="ByteStream"/>.
	/// </summary>
	/// <param name="data"></param>
	public void WriteAll( ref ByteStream data )
	{
		var container = ByteStream.Create( 32 );
		var count = 0;

		foreach ( var (slot, entry) in _entries )
		{
			var bs = ByteStream.Create( 32 );

			try
			{
				WriteEntryToStream( entry, ref bs );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when getting value {entry.DebugName} - {e.Message}" );
			}
			finally
			{
				container.Write( slot );
				container.Write( bs.Length );

				if ( bs.Length > 0 )
					container.Write( bs );

				bs.Dispose();

				count++;
			}
		}

		data.Write( count );

		if ( count > 0 )
		{
			data.Write( container.Length );
			data.Write( container );
		}

		container.Dispose();
	}

	/// <summary>
	/// Write an entry to the specified <see cref="ByteStream"/>.
	/// </summary>
	/// <param name="entry"></param>
	/// <param name="bs"></param>
	/// <param name="onlyWriteChanges"></param>
	private void WriteEntryToStream( Entry entry, ref ByteStream bs, bool onlyWriteChanges = false )
	{
		var value = entry.GetValue();
		if ( entry.IsSerializerType )
		{
			if ( value is INetworkSerializer custom )
			{
				bs.Write( true );

				if ( onlyWriteChanges )
					custom.WriteChanged( ref bs );
				else
					custom.WriteAll( ref bs );
			}
			else
			{
				bs.Write( false );
			}
		}
		else
		{
			Game.TypeLibrary.ToBytes( entry.GetValue(), ref bs );
		}
	}

	/// <summary>
	/// Write any changes to the provided <see cref="ByteStream"/> for entries that must be sent reliably. Calling this will clear the changes.
	/// </summary>
	/// <param name="data"></param>
	public void WriteReliableChanged( ref ByteStream data )
	{
		var container = ByteStream.Create( 2048 );
		var count = 0;

		foreach ( var entry in _reliableEntries )
		{
			if ( !entry.IsDirty || !entry.HasControl() )
				continue;

			var bs = ByteStream.Create( 2048 );

			try
			{
				WriteEntryToStream( entry, ref bs, true );

				container.Write( entry.Slot );
				container.Write( bs.Length );

				if ( bs.Length > 0 )
					container.Write( bs );

				count++;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when getting value {entry.DebugName} - {e.Message}" );
			}
			finally
			{
				entry.IsDirty = false;
				bs.Dispose();
			}
		}

		data.Write( count );

		if ( count > 0 )
		{
			data.Write( container.Length );
			data.Write( container );
		}

		container.Dispose();
	}

	/// <summary>
	/// Write any changes to the provided <see cref="ByteStream"/>. Calling this will clear the changes.
	/// </summary>
	/// <param name="data"></param>
	public void WriteChanged( ref ByteStream data )
	{
		var container = ByteStream.Create( 32 );
		var count = 0;

		foreach ( var (slot, entry) in _entries )
		{
			if ( !entry.IsDirty || !entry.HasControl() )
				continue;

			var bs = ByteStream.Create( 32 );

			try
			{
				WriteEntryToStream( entry, ref bs, true );

				container.Write( slot );
				container.Write( bs.Length );

				if ( bs.Length > 0 )
					container.Write( bs );

				count++;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when getting value {entry.DebugName} - {e.Message}" );
			}
			finally
			{
				entry.IsDirty = false;
				bs.Dispose();
			}
		}

		data.Write( count );

		if ( count > 0 )
		{
			data.Write( container.Length );
			data.Write( container );
		}

		container.Dispose();
	}

	public delegate bool ReadFilter( int slot, Entry entry );

	/// <summary>
	/// Read and apply any variables from the provided <see cref="ByteStream"/>.
	/// </summary>
	public void Read( ref ByteStream reader, ReadFilter filter = null )
	{
		var count = reader.Read<int>();
		if ( count <= 0 ) return;

		var containerCount = reader.Read<int>();
		if ( containerCount <= 0 ) return;

		var container = reader.ReadByteStream( containerCount );

		for ( var i = 0; i < count; i++ )
		{
			var slot = container.Read<int>();
			var length = container.Read<int>();

			if ( length <= 0 )
				continue;

			var bs = container.ReadByteStream( length );

			if ( !_entries.TryGetValue( slot, out var entry ) )
			{
				// It might be valid that we don't have a variable in this slot (we haven't had a network refresh yet.)
				bs.Dispose();
				continue;
			}

			if ( filter is not null && !filter.Invoke( slot, entry ) )
			{
				// We aren't allowed to make changes to the value in this slot right now.
				bs.Dispose();
				continue;
			}

			try
			{
				IsReadingChanges = true;
				ReadEntryFromStream( slot, entry, ref bs );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when reading value {entry.DebugName} - {e.Message}" );
			}
			finally
			{
				IsReadingChanges = false;

				// We're never dirty if we just had our value read.
				entry.IsDirty = false;
				bs.Dispose();
			}
		}

		container.Dispose();
	}

	private void ReadEntryFromStream( int slot, Entry entry, ref ByteStream bs )
	{
		if ( entry.IsSerializerType )
		{
			var isValid = bs.Read<bool>();

			if ( isValid )
			{
				var value = entry.GetValue();

				if ( value is not INetworkSerializer custom )
				{
					custom = Activator.CreateInstance( entry.TargetType ) as INetworkSerializer;
					SetValue( slot, custom );
				}

				custom?.Read( ref bs );
			}
			else
			{
				SetValue( slot, null );
			}
		}
		else
		{
			var value = Game.TypeLibrary.FromBytes<object>( ref bs );
			SetValue( slot, value );
		}
	}

	/// <summary>
	/// If any properties are "query" types, we'll copy the new values to ourselves
	/// and mark as changed, if changed.
	/// </summary>
	public void QueryValues( bool onlyReliableEntries = false )
	{
		for ( var i = 0; i < _queryEntries.Count; i++ )
		{
			var entry = _queryEntries[i];

			if ( onlyReliableEntries && !entry.IsReliableType )
				continue;

			if ( !entry.HasControl() )
				continue;

			try
			{
				UpdateSlotHash( entry, entry.GetValue() );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when getting value {entry.DebugName} - {e.Message}" );
			}
		}
	}
}
