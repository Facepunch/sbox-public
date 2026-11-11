using System.Collections.Concurrent;

namespace Sandbox.Internal;

public partial class TypeLibrary
{
	private readonly ConcurrentDictionary<int, object> _cacheDictionary = new();

	// ConcurrentDictionary does not allow null values, so we box null results with this sentinel.
	private static readonly object NullSentinel = new();

	/// <summary>
	/// Get or cache the result. Pass a static factory and its inputs as state, so a cache hit doesn't allocate.
	/// </summary>
	T Cached<T, TState>( int key, TState state, Func<TState, T> factory )
	{
		// GetOrAdd is thread-safe; the factory may be called more than once under contention,
		// but only one result is stored and all callers receive the same stored value.
		var result = _cacheDictionary.GetOrAdd( key, static ( _, s ) =>
		{
			var val = s.Factory( s.State );
			return val is null ? NullSentinel : (object)val;
		}, (State: state, Factory: factory) );

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
