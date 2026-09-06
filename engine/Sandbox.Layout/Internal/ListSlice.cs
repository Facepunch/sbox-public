namespace Sandbox.Layout;

/// <summary>
/// A non-allocating view over a range of a list, for hot loops that would otherwise copy sub-lists.
/// </summary>
internal readonly struct ListSlice<T>
{
	private readonly List<T> _list;
	private readonly int _start;
	private readonly int _end;

	public ListSlice( List<T> list, int start, int end )
	{
		_list = list;
		_start = start;
		_end = end;
	}

	public int Count => _end - _start;
	public T this[int index] => _list[_start + index];

	public Enumerator GetEnumerator() => new( _list, _start, _end );

	public struct Enumerator
	{
		private readonly List<T> _list;
		private readonly int _end;
		private int _index;

		public Enumerator( List<T> list, int start, int end )
		{
			_list = list;
			_end = end;
			_index = start - 1;
		}

		public readonly T Current => _list[_index];
		public bool MoveNext() => ++_index < _end;
	}
}
