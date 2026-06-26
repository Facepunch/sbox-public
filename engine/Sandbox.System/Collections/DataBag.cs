using System.Collections.Generic;

namespace Sandbox;

/// <summary>
/// A lightweight, lazily-allocated key/value store for stashing arbitrary typed data on an object.
/// The backing dictionary isn't created until you store something, so empty bags are essentially free.
/// Not thread-safe - use it from a single thread (usually the main thread).
/// </summary>
public sealed class DataBag
{
	Dictionary<string, object> _values;

	/// <summary>
	/// Get the value stored under <paramref name="key"/>, or <paramref name="fallback"/> if there's
	/// nothing there or it's a different type.
	/// </summary>
	public T Get<T>( string key, T fallback = default )
	{
		if ( _values is not null && _values.TryGetValue( key, out var value ) && value is T typed )
			return typed;

		return fallback;
	}

	/// <summary>
	/// Store a value under <paramref name="key"/>, replacing anything already there.
	/// </summary>
	public void Set<T>( string key, T value )
	{
		_values ??= new();
		_values[key] = value;
	}

	/// <summary>
	/// Whether anything is stored under <paramref name="key"/>.
	/// </summary>
	public bool Has( string key ) => _values is not null && _values.ContainsKey( key );

	/// <summary>
	/// Remove the value stored under <paramref name="key"/>. Returns true if something was removed.
	/// </summary>
	public bool Remove( string key ) => _values is not null && _values.Remove( key );

	/// <summary>
	/// Remove everything from the bag.
	/// </summary>
	public void Clear() => _values?.Clear();
}
