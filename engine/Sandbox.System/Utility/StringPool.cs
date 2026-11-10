using System.Collections.Concurrent;
using System.Threading;

namespace Sandbox;

/// <summary>
/// One shared copy of strings that would otherwise be duplicated many times over. Attributes are created fresh for
/// every type a member is reflected through, so an inherited member's source path and summary exist once per derived
/// type: hundreds of thousands of copies across the type library, all surviving into the old generation.
/// </summary>
internal static class StringPool
{
	// Far more than a game's type library needs; past it strings are returned as they are, so code making attributes in
	// a loop can't grow the pool without limit.
	const int MaxCount = 200_000;

	static readonly ConcurrentDictionary<string, string> _pool = new( StringComparer.Ordinal );

	// Counted alongside: the dictionary's own Count takes every bucket lock
	static int _count;

	public static string Get( string value )
	{
		if ( value is null ) return null;
		if ( _pool.TryGetValue( value, out var pooled ) ) return pooled;
		if ( Volatile.Read( ref _count ) >= MaxCount ) return value;

		if ( _pool.TryAdd( value, value ) )
		{
			Interlocked.Increment( ref _count );
			return value;
		}

		return _pool.TryGetValue( value, out pooled ) ? pooled : value;
	}

	/// <summary>
	/// Forget every pooled string, when a game's types go away. Strings already handed out stay valid.
	/// </summary>
	public static void Clear()
	{
		_pool.Clear();
		Interlocked.Exchange( ref _count, 0 );
	}
}
