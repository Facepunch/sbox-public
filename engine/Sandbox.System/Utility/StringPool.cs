using System.Collections.Concurrent;

namespace Sandbox;

/// <summary>
/// One shared copy of strings that would otherwise be duplicated many times over. Attributes are created fresh for
/// every type a member is reflected through, so an inherited member's source path and summary exist once per derived
/// type: hundreds of thousands of copies across the type library, all surviving into the old generation.
/// </summary>
internal static class StringPool
{
	static readonly ConcurrentDictionary<string, string> _pool = new( StringComparer.Ordinal );

	public static string Get( string value ) => value is null ? null : _pool.GetOrAdd( value, value );
}
