using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Sandbox;

/// <summary>
/// Describes a change to a <see cref="NetDictionary{TKey,TValue}"/> which is passed to
/// <see cref="NetDictionary{TKey,TValue}.OnChanged"/> whenever its contents change.
/// </summary>
public struct NetDictionaryChangeEvent<TKey, TValue>
{
	public NotifyCollectionChangedAction Type { get; set; }
	public TKey Key { get; set; }
	public TValue NewValue { get; set; }
	public TValue OldValue { get; set; }
}

/// <summary>
/// A networkable dictionary for use with the <see cref="SyncAttribute"/> and <see cref="HostSyncAttribute"/>. Only changes will be
/// networked instead of sending the whole dictionary every time, so it's more efficient.
/// <br/>
/// <para>
/// <b>Example usage:</b>
/// <code>
/// public class MyComponent : Component
/// {
///		[Sync] public NetDictionary&lt;string,bool&gt; MyBoolTable { get; set; } = new();
///		<br/>
///		public void SetBoolState( string key, bool state )
///		{
///			if ( IsProxy ) return;
///			MyBoolTable[key] = state;
///		}
/// }
/// </code>
/// </para>
/// </summary>
public sealed class NetDictionary<TKey, TValue> : INetworkSerializer, INetworkReliable, INetworkProperty, IDisposable, IDictionary, IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
{
	/// <summary>
	/// Represents a change in the dictionary.
	/// </summary>
	private struct Change
	{
		public NotifyCollectionChangedAction Type { get; set; }
		public TKey Key { get; set; }
		public TValue Value { get; set; }
	}

	/// <summary>
	/// Get notified when the dictionary is changed.
	/// </summary>
	public Action<NetDictionaryChangeEvent<TKey, TValue>> OnChanged;

	private readonly ObservableDictionary<TKey, TValue> _dictionary = new();
	private readonly List<Change> _changes = new();

	bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
	bool IDictionary.IsReadOnly => false;
	bool IDictionary.IsFixedSize => false;
	bool ICollection.IsSynchronized => false;
	object ICollection.SyncRoot => this;
	ICollection IDictionary.Values => (ICollection)_dictionary.Values;
	ICollection IDictionary.Keys => (ICollection)_dictionary.Keys;

	IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _dictionary.Values;
	IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _dictionary.Keys;

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.Values"/>
	/// </summary>
	public ICollection<TValue> Values => _dictionary.Values;

	public NetDictionary()
	{
		_dictionary.CollectionChanged += OnCollectionChanged;
		AddResetChange();
	}

	public void Dispose()
	{
		_changes.Clear();
	}

	/// <summary>
	/// <inheritdoc cref="ICollection.CopyTo"/>
	/// </summary>
	void ICollection.CopyTo( Array array, int index )
	{
		(_dictionary as ICollection).CopyTo( array, index );
	}

	/// <summary>
	/// <inheritdoc cref="IDictionary.Add"/>
	/// </summary>
	void IDictionary.Add( object key, object value )
	{
		Add( (TKey)key, (TValue)value );
	}

	/// <summary>
	/// <inheritdoc cref="IDictionary.Contains"/>
	/// </summary>
	bool IDictionary.Contains( object key )
	{
		return ContainsKey( (TKey)key );
	}

	/// <summary>
	/// <inheritdoc cref="IDictionary.Remove"/>
	/// </summary>
	void IDictionary.Remove( object key )
	{
		Remove( (TKey)key );
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.Add( TKey, TValue )"/>
	/// </summary>
	public void Add( TKey key, TValue value )
	{
		if ( !CanWriteChanges() )
			return;

		_dictionary.Add( key, value );
	}

	/// <summary>
	/// <inheritdoc cref="IDictionary{TKey,TValue}.Add( TKey, TValue )"/>
	/// </summary>
	public void Add( KeyValuePair<TKey, TValue> item )
	{
		if ( !CanWriteChanges() )
			return;

		_dictionary.Add( item );
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.Clear"/>
	/// </summary>
	public void Clear()
	{
		if ( !CanWriteChanges() )
			return;

		_dictionary.Clear();
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.ContainsKey"/>
	/// </summary>
	public bool ContainsKey( TKey key )
	{
		return _dictionary.ContainsKey( key );
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.Contains"/>
	/// </summary>
	public bool Contains( KeyValuePair<TKey, TValue> item )
	{
		return _dictionary.Contains( item );
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.CopyTo"/>
	/// </summary>
	public void CopyTo( KeyValuePair<TKey, TValue>[] array, int arrayIndex )
	{
		_dictionary.CopyTo( array, arrayIndex );
	}

	public bool Remove( KeyValuePair<TKey, TValue> item )
	{
		return CanWriteChanges() && _dictionary.Remove( item );
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.Keys"/>
	/// </summary>
	public ICollection<TKey> Keys
	{
		get { return _dictionary.Keys; }
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.Remove( TKey )"/>
	/// </summary>
	public bool Remove( TKey key )
	{
		if ( !CanWriteChanges() )
			return false;

		return _dictionary.Remove( key );
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.TryGetValue"/>
	/// </summary>
	public bool TryGetValue( TKey key, out TValue value ) => _dictionary.TryGetValue( key, out value );

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.Count"/>
	/// </summary>
	public int Count => _dictionary.Count;

	public TValue this[TKey key]
	{
		get
		{
			return _dictionary[key];
		}
		set
		{
			if ( !CanWriteChanges() )
				return;

			_dictionary[key] = value;
		}
	}

	object IDictionary.this[object key]
	{
		get => this[(TKey)key];
		set => this[(TKey)key] = (TValue)value;
	}

	/// <summary>
	/// <inheritdoc cref="IDictionary.GetEnumerator"/>
	/// </summary>
	IDictionaryEnumerator IDictionary.GetEnumerator()
	{
		return ((IDictionary)_dictionary).GetEnumerator();
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.GetEnumerator"/>
	/// </summary>
	public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
	{
		return _dictionary.GetEnumerator();
	}

	/// <summary>
	/// <inheritdoc cref="ObservableDictionary{TKey,TValue}.GetEnumerator"/>
	/// </summary>
	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable)_dictionary).GetEnumerator();
	}

	private INetworkProxy Parent { get; set; }

	void INetworkProperty.Init( int slot, INetworkProxy parent )
	{
		Parent = parent;
	}

	/// <summary>
	/// Do we have any pending changes?
	/// </summary>
	bool INetworkSerializer.HasChanges => _changes.Count > 0;

	/// <summary>
	/// Write any changed items to a <see cref="ByteStream"/>.
	/// </summary>
	void INetworkSerializer.WriteChanged( ref ByteStream data )
	{
		try
		{
			// We are sending changes, not a full update. This flag indicates that.
			data.Write( false );
			data.Write( _changes.Count );

			foreach ( var change in _changes )
			{
				data.Write( change.Type );
				WriteValue( change.Key, ref data );
				WriteValue( change.Value, ref data );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when writing NetDictionary changes - {e.Message}" );
		}

		_changes.Clear();
	}

	/// <summary>
	/// Read a network update from a <see cref="ByteStream"/>.
	/// </summary>
	void INetworkSerializer.Read( ref ByteStream data )
	{
		try
		{
			var isFullUpdate = data.Read<bool>();

			if ( isFullUpdate )
				ReadAll( ref data );
			else
				ReadChanged( ref data );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when reading NetDictionary - {e.Message}" );
		}

		// Clear changes whenever we read data. We don't want to keep local changes.
		_changes.Clear();
	}

	/// <summary>
	/// Write all items to a <see cref="ByteStream"/>.
	/// </summary>
	void INetworkSerializer.WriteAll( ref ByteStream data )
	{
		try
		{
			// We are sending a full update. This flag indicates that.
			data.Write( true );
			data.Write( _dictionary.Count );

			foreach ( var (k, v) in _dictionary )
			{
				WriteValue( k, ref data );
				WriteValue( v, ref data );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when writing NetDictionary - {e.Message}" );
		}
	}

	/// <summary>
	/// Read all changes in the dictionary as if we're building it for the first time.
	/// </summary>
	private void ReadAll( ref ByteStream data )
	{
		_dictionary.Clear();

		var count = data.Read<int>();

		for ( var i = 0; i < count; i++ )
		{
			var key = ReadValue<TKey>( ref data );
			var value = ReadValue<TValue>( ref data );

			if ( key is null ) continue;

			_dictionary[key] = value;
		}
	}

	/// <summary>
	/// Read any changed items from a <see cref="ByteStream"/>.
	/// </summary>
	private void ReadChanged( ref ByteStream data )
	{
		var count = data.Read<int>();

		for ( var i = 0; i < count; i++ )
		{
			var type = data.Read<NotifyCollectionChangedAction>();
			var key = ReadValue<TKey>( ref data );
			var value = ReadValue<TValue>( ref data );

			if ( type == NotifyCollectionChangedAction.Reset )
			{
				_dictionary.Clear();
			}
			else if ( key is null )
			{
				continue;
			}
			else if ( type == NotifyCollectionChangedAction.Add )
			{
				_dictionary.Add( key, value );
			}
			else if ( type == NotifyCollectionChangedAction.Remove )
			{
				_dictionary.Remove( key );
			}
			else if ( type == NotifyCollectionChangedAction.Replace )
			{
				_dictionary[key] = value;
			}
		}
	}

	private void OnCollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
	{
		var changeEvent = new NetDictionaryChangeEvent<TKey, TValue>
		{
			Type = e.Action
		};

		if ( e.OldItems is not null && e.OldItems.Count > 0 )
		{
			var (k, oldValue) = (KeyValuePair<TKey, TValue>)e.OldItems[0];
			changeEvent.OldValue = oldValue;
			changeEvent.Key = k;
		}

		if ( e.NewItems is not null && e.NewItems.Count > 0 )
		{
			var (k, newValue) = (KeyValuePair<TKey, TValue>)e.NewItems[0];
			changeEvent.NewValue = newValue;
			changeEvent.Key = k;
		}

		OnChanged?.InvokeWithWarning( changeEvent );

		if ( !CanWriteChanges() )
			return;

		if ( e.Action == NotifyCollectionChangedAction.Add )
		{
			var (k, v) = (KeyValuePair<TKey, TValue>)e.NewItems[0];
			var change = new Change { Key = k, Value = v, Type = e.Action };
			_changes.Add( change );
		}
		else if ( e.Action == NotifyCollectionChangedAction.Remove )
		{
			var (k, v) = (KeyValuePair<TKey, TValue>)e.OldItems[0];
			var change = new Change { Key = k, Type = e.Action };
			_changes.Add( change );
		}
		else if ( e.Action == NotifyCollectionChangedAction.Reset )
		{
			AddResetChange();
		}
		else if ( e.Action == NotifyCollectionChangedAction.Replace )
		{
			var (k, newValue) = (KeyValuePair<TKey, TValue>)e.NewItems[0];
			var change = new Change { Key = k, Type = e.Action, Value = newValue };
			_changes.Add( change );
		}
	}

	private static T ReadValue<T>( ref ByteStream data )
	{
		var value = Game.TypeLibrary.FromBytes<object>( ref data );
		return (T)value;
	}

	private static void WriteValue( object value, ref ByteStream data )
	{
		Game.TypeLibrary.ToBytes( value, ref data );
	}

	private bool CanWriteChanges() => !Parent?.IsProxy ?? true;

	private void AddResetChange()
	{
		var change = new Change { Type = NotifyCollectionChangedAction.Reset };
		_changes.Add( change );

		foreach ( var (k, v) in _dictionary )
		{
			// If a key is no longer valid, don't send it as a change, it'll be a null key on read.
			if ( k is IValid valid && !valid.IsValid() )
				continue;

			change = new Change { Key = k, Value = v, Type = NotifyCollectionChangedAction.Add };
			_changes.Add( change );
		}
	}
}
