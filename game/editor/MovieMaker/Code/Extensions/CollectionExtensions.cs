using System.Collections;
using Sandbox.MovieMaker;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable
public static class CollectionExtensions
{
	public static T? FirstOrNull<T>( this IEnumerable<T> enumerable, Func<T, bool> predicate )
		where T : struct
	{
		foreach ( var item in enumerable )
		{
			if ( predicate( item ) )
			{
				return item;
			}
		}

		return null;
	}

	public static IReadOnlyList<T> Slice<T>( this IReadOnlyList<T> list, int offset, int count )
	{
		if ( offset < 0 )
		{
			throw new ArgumentException( "Offset must be >= 0.", nameof( offset ) );
		}

		if ( list.Count < offset + count )
		{
			throw new ArgumentException( "Slice exceeds list element count.", nameof( count ) );
		}

		// Fast paths

		if ( count == 0 ) return Array.Empty<T>();
		if ( offset == 0 && count == list.Count ) return list;

		switch ( list )
		{
			case T[] array:
				return new ArraySegment<T>( array, offset, count );
			case ImmutableArray<T> immutableArray:
				return immutableArray.Slice( offset, count );
			case ArraySegment<T> segment:
				return segment.Slice( offset, count );
		}

		// Slow copy

		return list.Skip( offset ).Take( count ).ToArray();
	}

	/// <summary>
	/// Given two ascending lists of time ranges, union any overlapping pairs between the two lists and return them.
	/// </summary>
	public static IEnumerable<MovieTimeRange> Union( this IEnumerable<MovieTimeRange> first, IEnumerable<MovieTimeRange> second )
	{
		using var enumeratorA = first.GetEnumerator();
		using var enumeratorB = second.GetEnumerator();

		var hasItemA = enumeratorA.MoveNext();
		var hasItemB = enumeratorB.MoveNext();

		while ( hasItemA || hasItemB )
		{
			var next = !hasItemB || hasItemA && enumeratorA.Current.Start <= enumeratorB.Current.Start
				? enumeratorA.Current
				: enumeratorB.Current;

			while ( true )
			{
				if ( hasItemA && next.Intersect( enumeratorA.Current ) is not null )
				{
					next = next.Union( enumeratorA.Current );
					hasItemA = enumeratorA.MoveNext();
					continue;
				}

				if ( hasItemB && next.Intersect( enumeratorB.Current ) is not null )
				{
					next = next.Union( enumeratorB.Current );
					hasItemB = enumeratorB.MoveNext();
					continue;
				}

				break;
			}

			yield return next;
		}
	}

	public static T Pop<T>( this List<T> list )
	{
		var item = list[^1];
		list.RemoveAt( list.Count - 1 );

		return item;
	}
}

public interface ISynchronizedList<TSrc, TItem> : IReadOnlyList<TItem>
{
	public void Clear() => Update( [] );
	bool Update( IEnumerable<TSrc> source );
	int IndexOf( TSrc src );
}

/// <summary>
/// Maintains a set of <typeparamref name="TItem"/>, mapped from a collection of <typeparamref name="TSrc"/>.
/// </summary>
public sealed class SynchronizedSet<TSrc, TItem> : ISynchronizedList<TSrc, TItem>, IHotloadManaged
	where TSrc : notnull
{
	[SuppressNullKeyWarning]
	private readonly Dictionary<TSrc, TItem> _items;

	[SuppressNullKeyWarning]
	private readonly Dictionary<TSrc, int> _indices;

	private readonly List<KeyValuePair<TSrc, TItem>> _ordered = new();

	private readonly Func<TSrc, TItem> _addFunc;
	private readonly Action<TItem>? _removeAction;
	private readonly Func<TSrc, TItem, bool>? _updateAction;

	private bool _keysCanBeInvalid;
	private bool _keysWereInvalid;

	public SynchronizedSet( Func<TSrc, TItem> addFunc,
		Action<TItem>? removeAction = null,
		Func<TSrc, TItem, bool>? updateAction = null,
		IEqualityComparer<TSrc>? comparer = null )
	{
		_addFunc = addFunc;
		_removeAction = removeAction;
		_updateAction = updateAction;

		_items = new Dictionary<TSrc, TItem>( comparer );
		_indices = new Dictionary<TSrc, int>( comparer );
	}

	public TItem? Find( TSrc key )
	{
		RemoveInvalidKeys();
		return _items.GetValueOrDefault( key );
	}

	public void Clear() => Update( [] );

	public bool Update( IEnumerable<TSrc> source )
	{
		RemoveInvalidKeys();

		_indices.Clear();

		foreach ( var src in source )
		{
			_indices.Add( src, _indices.Count );
		}

		// We might have removed some items after a hotload, so
		// that should count as the collection being changed here

		var changed = _keysWereInvalid;

		_keysWereInvalid = false;

		// Remove items

		for ( var i = _ordered.Count - 1; i >= 0; --i )
		{
			var src = _ordered[i].Key;

			if ( _indices.ContainsKey( src ) ) continue;

			_ordered.RemoveAt( i );

			if ( !_items.Remove( src, out var item ) ) continue;

			_removeAction?.Invoke( item );

			changed = true;
		}

		// Add items

		foreach ( var src in _indices.Keys )
		{
			if ( _items.ContainsKey( src ) ) continue;

			var item = _addFunc( src );

			_items.Add( src, item );
			_ordered.Add( new KeyValuePair<TSrc, TItem>( src, item ) );

			changed = true;
		}

		// Sort and update items

		_ordered.Sort( ( a, b ) => _indices[a.Key] - _indices[b.Key] );

		if ( _updateAction is { } updateAction )
		{
			foreach ( var pair in _ordered )
			{
				changed |= updateAction( pair.Key, pair.Value );
			}
		}

		return changed;
	}

	public int IndexOf( TSrc src )
	{
		RemoveInvalidKeys();
		return _indices.TryGetValue( src, out var index ) ? index : -1;
	}

	public IEnumerator<TItem> GetEnumerator()
	{
		RemoveInvalidKeys();
		return _ordered.Select( x => x.Value ).GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		RemoveInvalidKeys();
		return _items.GetEnumerator();
	}

	public int Count
	{
		get
		{
			RemoveInvalidKeys();
			return _items.Count;
		}
	}

	public TItem this[int index]
	{
		get
		{
			RemoveInvalidKeys();
			return _ordered[index].Value;
		}
	}

	void IHotloadManaged.Persisted() => _keysCanBeInvalid = true;
	void IHotloadManaged.Created( IReadOnlyDictionary<string, object?> state ) => _keysCanBeInvalid = true;

	/// <summary>
	/// Called after hotload to remove any items that didn't survive because their types got removed.
	/// </summary>
	private void RemoveInvalidKeys()
	{
		if ( !_keysCanBeInvalid ) return;

		_keysCanBeInvalid = false;

		// First pass: remove invalid / null items

		for ( var i = _ordered.Count - 1; i >= 0; i-- )
		{
			var (src, dst) = _ordered[i];

			if ( (TSrc?)src is null )
			{
				// Null items will have been removed from _items dict during hotload,
				// because dictionaries can't have null keys

				_removeAction?.Invoke( dst );
				_ordered.RemoveAt( i );

				_keysWereInvalid = true;
			}
			else if ( src is IValid { IsValid: false } )
			{
				_removeAction?.Invoke( dst );
				_ordered.RemoveAt( i );
				_items.Remove( src );

				_keysWereInvalid = true;
			}
		}

		// Second pass: update indices dict

		_indices.Clear();

		for ( var i = 0; i < _ordered.Count; i++ )
		{
			_indices.Add( _ordered[i].Key, i );
		}
	}
}

/// <summary>
/// Maintains a list of <typeparamref name="TItem"/> with the same length as a list of <typeparamref name="TSrc"/>.
/// </summary>
public sealed class SynchronizedList<TSrc, TItem> : ISynchronizedList<TSrc, TItem>
{
	private readonly List<TSrc> _sources = new();
	private readonly List<TItem> _items = new();

	private readonly Func<TSrc, TItem> _addFunc;
	private readonly Action<TItem>? _removeAction;
	private readonly UpdateItemDelegate? _updateAction;

	public IEnumerable<TSrc> Sources => _sources;

	public delegate bool UpdateItemDelegate( TSrc source, ref TItem item );

	public SynchronizedList( Func<TSrc, TItem> addFunc,
		Action<TItem>? removeAction = null,
		UpdateItemDelegate? updateAction = null )
	{
		_addFunc = addFunc;
		_removeAction = removeAction;
		_updateAction = updateAction;
	}

	public void Clear() => Update( [] );

	public bool Update( IEnumerable<TSrc> source )
	{
		_sources.Clear();
		_sources.AddRange( source );

		var changed = false;

		// Remove items

		while ( _items.Count > _sources.Count )
		{
			var item = _items.Pop();
			_removeAction?.Invoke( item );

			changed = true;
		}

		// Add items

		while ( _items.Count < _sources.Count )
		{
			_items.Add( _addFunc( _sources[_items.Count] ) );

			changed = true;
		}

		if ( _updateAction is { } updateAction )
		{
			for ( var i = 0; i < _sources.Count; ++i )
			{
				var item = _items[i];

				if ( updateAction( _sources[i], ref item ) )
				{
					_items[i] = item;
					changed = true;
				}
			}
		}

		return changed;
	}

	public int IndexOf( TSrc src ) => _sources.IndexOf( src );

	public IEnumerator<TItem> GetEnumerator() => _items.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

	public int Count => _items.Count;

	public TItem this[int index] => _items[index];
}
