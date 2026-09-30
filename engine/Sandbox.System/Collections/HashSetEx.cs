namespace Sandbox.Utility;

// 1. Be as fast as possible to iterate
// 2. Queue modifications while iterating
// 3. Don't allow duplicate entries
// 4. Fast as possible removal
// 5. Thread concurrency doesn't matter

/// <summary>
/// Wrapper around a <see cref="HashSet{T}"/> that supports items being added / removed
/// during enumeration. Enumerate the set with <see cref="EnumerateLocked"/>.
/// </summary>
internal class HashSetEx<T> : IHotloadManaged
{
	[SuppressNullKeyWarning]
	private readonly HashSet<T> _hashset = new( 16 );
	private readonly List<T> _cachedList = new( 16 );

	private bool _listInvalid;
	private int _activeEnumerators;

	/// <summary>
	/// Current number of unique items in the set.
	/// </summary>
	public int Count => _hashset.Count;

	/// <summary>
	/// List view of the set. This is only updated when there are no
	/// active enumerators created by <see cref="EnumerateLocked"/>.
	/// </summary>
	public IReadOnlyList<T> List
	{
		get
		{
			UpdateList();
			return _cachedList;
		}
	}

	/// <summary>
	/// Adds an item to the set, returning true if it wasn't already present.
	/// If any enumerators are active, they won't see this new item yet.
	/// </summary>
	public bool Add( T obj )
	{
		if ( !_hashset.Add( obj ) ) return false;

		_listInvalid = true;
		return true;
	}

	/// <summary>
	/// Removes an item from the set, returning true if it was present.
	/// If any enumerators are active, they will still see the removed item.
	/// </summary>
	public bool Remove( T obj )
	{
		if ( !_hashset.Remove( obj ) ) return false;

		_listInvalid = true;
		return true;
	}

	/// <summary>
	/// Determines whether this set contains the given object.
	/// </summary>
	public bool Contains( T obj ) => _hashset.Contains( obj );

	/// <summary>
	/// Remove all items from the set. If any enumerators are active, they won't be affected.
	/// </summary>
	public void Clear()
	{
		if ( _hashset.Count <= 0 ) return;

		_hashset.Clear();
		_listInvalid = true;
	}

	/// <summary>
	/// If any items were added / removed, and there are no active enumerators, synchronize
	/// <see cref="_cachedList"/> with items from <see cref="_hashset"/>.
	/// </summary>
	private void UpdateList()
	{
		if ( !_listInvalid ) return;
		if ( _activeEnumerators > 0 ) return;

		_listInvalid = false;

		_cachedList.Clear();
		_cachedList.AddRange( _hashset );
	}

	/// <summary>
	/// Enumerates the list, incrementing <see cref="_activeEnumerators"/> for the duration so
	/// <see cref="_cachedList"/> can't be rebuilt underneath us.
	/// IMPORTANT: Don't expose this to users directly - because they might purposefully not dispose it?
	/// </summary>
	public LockedEnumerable EnumerateLocked( bool nullChecks = false ) => new( this, nullChecks );

	/// <summary>
	/// Struct enumerable so <c>foreach</c> over <see cref="EnumerateLocked"/> doesn't allocate.
	/// </summary>
	internal readonly struct LockedEnumerable
	{
		private readonly HashSetEx<T> _set;
		private readonly bool _nullChecks;

		internal LockedEnumerable( HashSetEx<T> set, bool nullChecks )
		{
			_set = set;
			_nullChecks = nullChecks;
		}

		// The foreach pattern requires these members be public, even though the type isn't
		public LockedEnumerator GetEnumerator() => new( _set, _nullChecks );
	}

	/// <summary>
	/// Holds the enumeration lock until disposed, which <c>foreach</c> does for us.
	/// </summary>
	internal struct LockedEnumerator : IDisposable
	{
		private readonly HashSetEx<T> _set;
		private readonly bool _nullChecks;
		private int _index;
		private bool _disposed;

		internal LockedEnumerator( HashSetEx<T> set, bool nullChecks )
		{
			_set = set;
			_nullChecks = nullChecks;
			_index = -1;
			_disposed = false;
			Current = default;

			set.UpdateList();
			set._activeEnumerators++;
		}

		public T Current { get; private set; }

		public bool MoveNext()
		{
			var list = _set._cachedList;

			// The list can't be rebuilt while we're active, so indexing it is safe
			while ( ++_index < list.Count )
			{
				var item = list[_index];

				if ( _nullChecks && item is IValid { IsValid: false } )
				{
					_set.Remove( item );
					continue;
				}

				Current = item;
				return true;
			}

			Current = default;
			return false;
		}

		public void Dispose()
		{
			if ( _disposed ) return;

			_disposed = true;
			_set._activeEnumerators--;
		}
	}

	// If types are removed during hotload, items of those types are
	// automatically removed from _hashset because it can't contain null items.
	// Therefore, we'll need to rebuild _cachedList too.

	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state ) => _listInvalid = true;
	void IHotloadManaged.Persisted() => _listInvalid = true;
}
