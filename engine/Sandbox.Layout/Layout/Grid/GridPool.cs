namespace Sandbox.Layout;

/// <summary>Demand-grown per-thread reuse, bounded by object count and retained buffer capacity.</summary>
internal static class GridPool<T> where T : class, new()
{
	private const int MaxRetained = 4096;
	private const int MaxTotalCapacity = 32768;
	private const int MaxBufferCapacity = 4096;

	[ThreadStatic] private static Stack<(T Item, int Capacity)> s_items;
	[ThreadStatic] private static int s_capacity;

	public static T Rent()
	{
		var items = s_items ??= new();
		if ( items.Count == 0 ) return new T();
		var entry = items.Pop();
		s_capacity -= entry.Capacity;
		return entry.Item;
	}

	public static void Return( T item, int capacity = 0 )
	{
		var items = s_items ??= new();
		if ( items.Count < MaxRetained
			&& capacity <= MaxBufferCapacity && s_capacity + capacity <= MaxTotalCapacity )
		{
			items.Push( (item, capacity) );
			s_capacity += capacity;
		}
	}
}
