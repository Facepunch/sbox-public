using System.Collections;

namespace Editor;

public class HistoryStack<T> : IEnumerable<T>
{
	private LinkedList<T> items = new LinkedList<T>();
	private LinkedListNode<T> current;
	private int currentIndex;

	public T Current => current.Value ?? throw new InvalidOperationException( "No current item" );
	public int CurrentIndex => currentIndex;
	public int Count => items.Count;

	string _cookie;
	public string StateCookie
	{
		get => _cookie;

		set
		{
			if ( _cookie == value ) return;
			_cookie = value;
			Restore();
		}
	}

	public void Add( T item )
	{
		while ( current != null && current != items.Last )
		{
			items.RemoveLast();
		}

		items.AddLast( item );
		current = items.Last;
		currentIndex = items.Count - 1;
		Save();
	}

	public bool CanGoBack()
	{
		return currentIndex > 0;
	}

	public bool CanGoForward()
	{
		return currentIndex < items.Count - 1;
	}

	public T GoBack()
	{
		if ( current != null && current.Previous != null )
		{
			current = current.Previous;
			currentIndex--;
			Save();
			return current.Value;
		}

		throw new InvalidOperationException( "Can't go back further" );
	}

	public T GoForward()
	{
		if ( current != null && current.Next != null )
		{
			current = current.Next;
			currentIndex++;
			Save();
			return current.Value;
		}

		throw new InvalidOperationException( "Can't go forward" );
	}

	public T GoTo( int index )
	{
		if ( index < 0 || index >= items.Count )
		{
			throw new ArgumentOutOfRangeException( nameof( index ), "Index is out of range" );
		}

		int i = 0;
		foreach ( var node in items )
		{
			if ( i == index )
			{
				current = items.Find( node );
				currentIndex = index;
				Save();
				return current.Value;
			}
			i++;
		}

		throw new InvalidOperationException( "Unexpected error occurred" );
	}

	private void Save()
	{
		if ( string.IsNullOrEmpty( StateCookie ) ) return;

		ProjectCookie.Set( $"{StateCookie}.History", items );
		ProjectCookie.Set( $"{StateCookie}.HistoryIdx", currentIndex );
	}

	private void Restore()
	{
		if ( string.IsNullOrEmpty( StateCookie ) ) return;

		items = ProjectCookie.Get( $"{StateCookie}.History", items );
		items ??= new LinkedList<T>();

		int idx = ProjectCookie.Get( $"{StateCookie}.HistoryIdx", -1 );

		// A saved index can outlive the history it points into - the clamp alone does not
		// cover that, because an empty list clamps to -1 and GoTo rejects it.
		if ( idx != -1 && items.Count > 0 )
		{
			GoTo( idx.Clamp( 0, items.Count - 1 ) );
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		return items.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return items.GetEnumerator();
	}
}
