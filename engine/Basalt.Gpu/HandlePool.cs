namespace Basalt.Gpu;

/// <summary>
/// Device-owned records named by an index and a generation. Records live in fixed pages that never move, so a
/// reference to one stays good while other threads add more. Index 0 is never used, so a default handle names nothing.
/// </summary>
internal sealed class HandlePool<T> where T : struct
{
	const int PageBits = 10;
	const int PageSize = 1 << PageBits;
	const int MaxPages = 4096;

	readonly T[][] pages = new T[MaxPages][];
	readonly uint[][] generations = new uint[MaxPages][];
	readonly Stack<uint> free = new();
	readonly string kind;
	uint next = 1;
	int live;

	public HandlePool( string kind )
	{
		this.kind = kind;
	}

	/// <summary>How many records are alive.</summary>
	public int Count => live;

	public (uint Index, uint Generation) Add( in T record )
	{
		lock ( free )
		{
			var index = free.Count > 0 ? free.Pop() : next++;
			var page = (int)(index >> PageBits);
			if ( page >= MaxPages ) throw new GpuException( $"Too many {kind}s" );

			if ( pages[page] is null )
			{
				generations[page] = new uint[PageSize];
				pages[page] = new T[PageSize];
			}

			var slot = (int)(index & (PageSize - 1));
			pages[page][slot] = record;
			var generation = generations[page][slot] + 1;
			if ( generation == 0 ) generation = 1;
			generations[page][slot] = generation;
			live++;
			return (index, generation);
		}
	}

	/// <summary>
	/// The record a handle names. Throws for a default, stale or foreign handle.
	/// </summary>
	public ref T Get( uint index, uint generation )
	{
		var page = (int)(index >> PageBits);
		var slot = (int)(index & (PageSize - 1));
		if ( generation == 0 || page >= MaxPages || generations[page] is not { } gens || gens[slot] != generation )
			throw new ArgumentException( generation == 0 ? $"No {kind} (a default handle)" : $"{kind} {index} was destroyed or never existed" );

		return ref pages[page][slot];
	}

	/// <summary>Whether a handle names a live record.</summary>
	public bool IsAlive( uint index, uint generation )
	{
		var page = (int)(index >> PageBits);
		var slot = (int)(index & (PageSize - 1));
		return generation != 0 && page < MaxPages && generations[page] is { } gens && gens[slot] == generation;
	}

	/// <summary>
	/// Forget a record and return it; its index is reused with a new generation.
	/// </summary>
	public T Remove( uint index, uint generation )
	{
		lock ( free )
		{
			ref var record = ref Get( index, generation );
			var removed = record;
			record = default;

			var page = (int)(index >> PageBits);
			var slot = (int)(index & (PageSize - 1));
			generations[page][slot]++;
			if ( generations[page][slot] == 0 ) generations[page][slot] = 1;

			free.Push( index );
			live--;
			return removed;
		}
	}

	/// <summary>Every live record, for shutdown.</summary>
	public List<T> All()
	{
		lock ( free )
		{
			var freeSet = new HashSet<uint>( free );
			var all = new List<T>();
			for ( uint i = 1; i < next; i++ )
			{
				if ( !freeSet.Contains( i ) ) all.Add( pages[i >> PageBits][i & (PageSize - 1)] );
			}

			return all;
		}
	}
}
