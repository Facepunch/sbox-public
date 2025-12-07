using System.Collections;
namespace Sandbox;

/// <summary>
/// An ordered collection of unique objects with add/remove callbacks.
/// Maintains insertion order and provides change notifications.
/// </summary>
public class SelectionSystem : IEnumerable<object>
{
	// Value is always false and unused - we only need the ordered key collection
	private readonly OrderedDictionary<object, bool> _list = new();

	// Random hash code that changes when the collection is modified
	int _hashCode = Random.Shared.Int( 0, 4096 );

	/// <summary>
	/// Invoked when an item is added to the selection.
	/// </summary>
	public Action<object> OnItemAdded { get; set; }

	/// <summary>
	/// Invoked when an item is removed from the selection.
	/// </summary>
	public Action<object> OnItemRemoved { get; set; }

	/// <summary>
	/// Gets a hash code that changes whenever the collection is modified.
	/// Useful for detecting selection changes.
	/// </summary>
	public override int GetHashCode() => _hashCode;

	/// <summary>
	/// Returns an enumerator that iterates through the selected objects in order.
	/// </summary>
	public IEnumerator<object> GetEnumerator()
	{
		return _list.Keys.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return _list.Keys.GetEnumerator();
	}

	/// <summary>
	/// Gets the number of selected objects.
	/// </summary>
	public int Count => _list.Count;

	/// <summary>
	/// Removes all objects from the selection, invoking OnItemRemoved for each.
	/// </summary>
	public virtual void Clear()
	{
		if ( !_list.Any() )
			return;

		foreach ( var kvp in _list )
		{
			OnItemRemoved?.Invoke( kvp.Key );
		}

		_list.Clear();
		_hashCode++;
	}

	/// <summary>
	/// Adds an object to the selection.
	/// </summary>
	/// <param name="obj">The object to add</param>
	/// <returns>True if the object was added, false if it was already selected</returns>
	public virtual bool Add( object obj )
	{
		if ( _list.TryAdd( obj, false ) )
		{
			_hashCode++;
			OnItemAdded?.Invoke( obj );
			return true;
		}

		return false;
	}

	/// <summary>
	/// Clears the selection and sets it to a single object.
	/// </summary>
	/// <param name="obj">The object to select</param>
	/// <returns>True if the selection changed, false if it was already the only selected object</returns>
	public virtual bool Set( object obj )
	{
		if ( _list.Count == 1 && _list.Keys.First() == obj )
			return false;

		Clear();
		Add( obj );
		return true;
	}

	/// <summary>
	/// Removes an object from the selection.
	/// </summary>
	/// <param name="obj">The object to remove</param>
	/// <returns>True if the object was removed, false if it wasn't selected</returns>
	public virtual bool Remove( object obj )
	{
		if ( _list.Remove( obj ) )
		{
			_hashCode++;
			OnItemRemoved?.Invoke( obj );
			return true;
		}

		return false;
	}

	/// <summary>
	/// Checks if an object is in the selection.
	/// </summary>
	/// <param name="obj">The object to check</param>
	/// <returns>True if the object is selected</returns>
	public virtual bool Contains( object obj )
	{
		if ( obj is null ) return false;
		return _list.ContainsKey( obj );
	}

	/// <summary>
	/// Checks if the selection contains any objects.
	/// </summary>
	/// <returns>True if there are any selected objects</returns>
	public virtual bool Any() => _list.Count != 0;
}
