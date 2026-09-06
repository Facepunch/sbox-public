namespace Sandbox.Layout;

internal static partial class LayoutAlgorithm
{
	[ThreadStatic] private static Stack<List<LayoutNode>> s_listPool;
	[ThreadStatic] private static int s_pooledListCapacity;
	private const int MaxPooledLists = 128;
	private const int MaxPooledListCapacity = 4096;
	private const int MaxTotalPooledListCapacity = 32768;

	internal static List<LayoutNode> RentList()
	{
		s_listPool ??= new Stack<List<LayoutNode>>();
		if ( s_listPool.Count == 0 ) return new List<LayoutNode>();
		var list = s_listPool.Pop();
		s_pooledListCapacity -= list.Capacity;
		return list;
	}

	internal static void ReturnList( List<LayoutNode> list )
	{
		var retain = list.Capacity <= MaxPooledListCapacity;
		list.Clear();
		// Grow with observed demand, but don't retain a pathological tree's entire working set.
		if ( retain && s_listPool.Count < MaxPooledLists && s_pooledListCapacity + list.Capacity <= MaxTotalPooledListCapacity )
		{
			s_listPool.Push( list );
			s_pooledListCapacity += list.Capacity;
		}
	}
}
