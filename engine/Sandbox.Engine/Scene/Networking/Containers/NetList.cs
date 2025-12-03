using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Sandbox;

/// <summary>
/// A networkable list for use with the <see cref="SyncAttribute"/> and <see cref="HostSyncAttribute"/>. Only changes will be
/// networked instead of sending the whole list every time, so it's more efficient.
/// <br/>
/// <para>
/// <b>Example usage:</b>
/// <code>
/// public class MyComponent : Component
/// {
///		[Sync] public NetList&lt;int&gt; MyIntegerList { get; set; } = new();
///		<br/>
///		public void AddNumber( int number )
///		{
///			if ( IsProxy ) return;
///			MyIntegerList.Add( number );
///		}
/// }
/// </code>
/// </para>
/// </summary>
public sealed class NetList<T> : INetworkSerializer, INetworkReliable, INetworkProperty, IDisposable, IList, IList<T>, IReadOnlyList<T>
{
	/// <summary>
	/// Represents a change in the list.
	/// </summary>
	private struct Change
	{
		public NotifyCollectionChangedAction Type { get; set; }
		public int Index { get; set; }
		public int MovedIndex { get; set; }
		public T Value { get; set; }
	}

	private readonly ObservableCollection<T> list = new();
	private readonly List<Change> changes = new();

	public NetList()
	{
		list.CollectionChanged += OnCollectionChanged;
		AddResetChange();
	}

	public void Dispose()
	{
		changes.Clear();
	}

	bool ICollection<T>.IsReadOnly => false;
	bool IList.IsReadOnly => false;
	bool IList.IsFixedSize => false;
	bool ICollection.IsSynchronized => false;
	object ICollection.SyncRoot => this;

	object IList.this[int index]
	{
		get => this[index];
		set => this[index] = (T)value;
	}

	/// <summary>
	/// <inheritdoc cref="IList.Add"/>
	/// </summary>
	int IList.Add( object value )
	{
		Add( (T)value );
		return Count - 1;
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.Remove"/>
	/// </summary>
	bool ICollection<T>.Remove( T item )
	{
		return Remove( item );
	}

	/// <summary>
	/// <inheritdoc cref="ICollection.CopyTo"/>
	/// </summary>
	void ICollection.CopyTo( Array array, int index )
	{
		(list as ICollection).CopyTo( array, index );
	}

	/// <summary>
	/// <inheritdoc cref="IList.Contains"/>
	/// </summary>
	bool IList.Contains( object value )
	{
		return list.Contains( (T)value );
	}

	/// <summary>
	/// <inheritdoc cref="IList.IndexOf( object )"/>
	/// </summary>
	int IList.IndexOf( object value )
	{
		return list.IndexOf( (T)value );
	}

	/// <summary>
	/// <inheritdoc cref="IList.Insert"/>
	/// </summary>
	void IList.Insert( int index, object value )
	{
		Insert( index, (T)value );
	}

	/// <summary>
	/// <inheritdoc cref="IList.Remove"/>
	/// </summary>
	void IList.Remove( object value )
	{
		Remove( (T)value );
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.Clear"/>
	/// </summary>
	public void Clear()
	{
		if ( !CanWriteChanges() )
			return;

		list.Clear();
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.Contains"/>
	/// </summary>
	public bool Contains( T item )
	{
		return list.Contains( item );
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.CopyTo( T[], int )"/>
	/// </summary>
	public void CopyTo( T[] array, int arrayIndex )
	{
		list.CopyTo( array, arrayIndex );
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.Add"/>
	/// </summary>
	public void Add( T value )
	{
		if ( !CanWriteChanges() )
			return;

		list.Add( value );
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.AddRange"/>
	/// </summary>
	public void AddRange( IEnumerable<T> collection )
	{
		if ( !CanWriteChanges() )
			return;

		foreach ( var value in collection )
		{
			list.Add( value );
		}
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.Remove"/>
	/// </summary>
	public bool Remove( T value )
	{
		return CanWriteChanges() && list.Remove( value );
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.IndexOf( T )"/>
	/// </summary>
	public int IndexOf( T item )
	{
		return list.IndexOf( item );
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.Insert"/>
	/// </summary>
	public void Insert( int index, T value )
	{
		if ( !CanWriteChanges() )
			return;

		list.Insert( index, value );
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.RemoveAt"/>
	/// </summary>
	public void RemoveAt( int index )
	{
		if ( !CanWriteChanges() )
			return;

		list.RemoveAt( index );
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.Count"/>
	/// </summary>
	public int Count => list.Count;

	public T this[int key]
	{
		get
		{
			return list[key];
		}
		set
		{
			if ( !CanWriteChanges() )
				return;

			list[key] = value;
		}
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.GetEnumerator"/>
	/// </summary>
	public IEnumerator<T> GetEnumerator()
	{
		return list.GetEnumerator();
	}

	/// <summary>
	/// <inheritdoc cref="List{T}.GetEnumerator"/>
	/// </summary>
	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable)list).GetEnumerator();
	}

	private INetworkProxy Parent { get; set; }

	void INetworkProperty.Init( int slot, INetworkProxy parent )
	{
		Parent = parent;
	}

	/// <summary>
	/// Do we have any pending changes?
	/// </summary>
	bool INetworkSerializer.HasChanges => changes.Count > 0;

	/// <summary>
	/// Write any changed items to a <see cref="ByteStream"/>.
	/// </summary>
	void INetworkSerializer.WriteChanged( ref ByteStream data )
	{
		try
		{
			// We are sending changes, not a full update. This flag indicates that.
			data.Write( false );
			data.Write( changes.Count );

			foreach ( var change in changes )
			{
				data.Write( change.Type );
				data.Write( change.Index );
				data.Write( change.MovedIndex );
				WriteValue( change.Value, ref data );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when writing NetList changes - {e.Message}" );
		}

		changes.Clear();
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
			Log.Warning( e, $"Error when reading NetList - {e.Message}" );
		}

		// Clear changes whenever we read data. We don't want to keep local changes.
		changes.Clear();
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
			data.Write( list.Count );

			foreach ( var item in list )
			{
				WriteValue( item, ref data );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when writing NetList - {e.Message}" );
		}
	}

	/// <summary>
	/// Read all changes in the list as if we're building it for the first time.
	/// </summary>
	/// <param name="data"></param>
	private void ReadAll( ref ByteStream data )
	{
		list.Clear();

		var count = data.Read<int>();

		for ( var i = 0; i < count; i++ )
		{
			var value = ReadValue( ref data );
			list.Add( value );
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
			var index = data.Read<int>();
			var movedIndex = data.Read<int>();
			var value = ReadValue( ref data );

			if ( type == NotifyCollectionChangedAction.Add )
			{
				if ( index >= 0 && index <= list.Count )
					list.Insert( index, value );
				else
					list.Add( value );
			}
			else if ( type == NotifyCollectionChangedAction.Remove )
			{
				if ( index >= 0 && index < list.Count )
					list.RemoveAt( index );
			}
			else if ( type == NotifyCollectionChangedAction.Reset )
			{
				list.Clear();
			}
			else if ( type == NotifyCollectionChangedAction.Replace )
			{
				list[index] = value;
			}
			else if ( type == NotifyCollectionChangedAction.Move )
			{
				list.Move( index, movedIndex );
			}
		}
	}

	private void OnCollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
	{
		if ( !CanWriteChanges() )
			return;

		if ( e.Action == NotifyCollectionChangedAction.Add )
		{
			var change = new Change { Index = e.NewStartingIndex, Value = (T)e.NewItems[0], Type = e.Action };
			changes.Add( change );
		}
		else if ( e.Action == NotifyCollectionChangedAction.Remove )
		{
			var change = new Change { Index = e.OldStartingIndex, Type = e.Action };
			changes.Add( change );
		}
		else if ( e.Action == NotifyCollectionChangedAction.Reset )
		{
			AddResetChange();
		}
		else if ( e.Action == NotifyCollectionChangedAction.Replace )
		{
			var change = new Change { Index = e.OldStartingIndex, Type = e.Action, Value = (T)e.NewItems[0] };
			changes.Add( change );
		}
		else if ( e.Action == NotifyCollectionChangedAction.Move )
		{
			var change = new Change { Index = e.OldStartingIndex, MovedIndex = e.NewStartingIndex, Type = e.Action };
			changes.Add( change );
		}
	}

	private T ReadValue( ref ByteStream data )
	{
		var value = Game.TypeLibrary.FromBytes<object>( ref data );
		return (T)value;
	}

	private void WriteValue( T value, ref ByteStream data )
	{
		Game.TypeLibrary.ToBytes( value, ref data );
	}

	private bool CanWriteChanges() => !Parent?.IsProxy ?? true;

	private void AddResetChange()
	{
		var change = new Change { Type = NotifyCollectionChangedAction.Reset };
		changes.Add( change );

		for ( var i = 0; i < list.Count; i++ )
		{
			var item = list[i];
			change = new() { Index = -1, Value = item, Type = NotifyCollectionChangedAction.Add };
			changes.Add( change );
		}
	}
}
