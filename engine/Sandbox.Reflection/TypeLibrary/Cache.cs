using System.Collections.Concurrent;

namespace Sandbox.Internal;

public partial class TypeLibrary
{
	private readonly ConcurrentDictionary<int, object> _cacheDictionary = new();

	// ConcurrentDictionary does not allow null values, so we box null results with this sentinel.
	private static readonly object NullSentinel = new();

	/// <summary>
	/// Get or cache the result
	/// </summary>
	T Cached<T>( int key, Func<T> factory )
	{
		// GetOrAdd is thread-safe; the factory may be called more than once under contention,
		// but only one result is stored and all callers receive the same stored value.
		var result = _cacheDictionary.GetOrAdd( key, _ =>
		{
			var val = factory();
			return val is null ? NullSentinel : (object)val;
		} );

		return result == NullSentinel ? default! : (T)result;
	}

	/// <summary>
	/// Clear the cache. Should be called when types are added or removed.
	/// </summary>
	void InvalidateCache()
	{
		_cacheDictionary.Clear();
	}
}
