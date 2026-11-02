using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Sandbox;

public partial class AccessRules
{
	public List<Regex> Whitelist = new();
	public List<Regex> Blacklist = new();

	readonly ConcurrentDictionary<string, bool> whitelistCache = new();

	public AccessRules()
	{
		InitAssemblyList();

		// TODO add rules based on config
		AddRange( Rules.BaseAccess );
		AddRange( Rules.Types );
		AddRange( Rules.Numerics );
		AddRange( Rules.Reflection );
		AddRange( Rules.Exceptions );
		AddRange( Rules.Diagnostics );
		AddRange( Rules.Async );
		AddRange( Rules.CompilerGenerated );
	}

	void AddRange( IEnumerable<string> rules )
	{
		foreach ( var line in rules )
		{
			AddRule( line );
		}
	}

	void AddRule( string line )
	{
		var wildcard = line.Trim();

		bool blacklist = wildcard.StartsWith( '!' );
		if ( blacklist )
			wildcard = wildcard[1..];

		wildcard = Regex.Escape( wildcard ).Replace( "\\*", ".*" );
		wildcard = $"^{wildcard}$";

		var regex = new Regex( wildcard, RegexOptions.Compiled );

		if ( blacklist )
			Blacklist.Add( regex );
		else
			Whitelist.Add( regex );
	}

	/// <summary>
	/// Returns true if call is in the whitelist
	/// </summary>
	public bool IsInWhitelist( string test )
	{
		if ( whitelistCache.TryGetValue( test, out var cached ) )
			return cached;

		var allowed = !Blacklist.Any( x => x.IsMatch( test ) ) && Whitelist.Any( x => x.IsMatch( test ) );
		whitelistCache[test] = allowed;
		return allowed;
	}
}
