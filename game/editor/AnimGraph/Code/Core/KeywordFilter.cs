using System;

namespace Editor.AnimGraph;

internal sealed class KeywordFilter( string query )
{
	private readonly string[] _terms = query.Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

	public bool Matches( string name, string context = "" )
	{
		foreach ( var term in _terms )
		{
			var excluded = term.StartsWith( '-' );
			var search = excluded ? term[1..] : term;
			if ( search.Length == 0 ) continue;
			var wildcard = search.Contains( '*' ) || search.Contains( '?' );
			var matches = wildcard
				? name.WildcardMatch( search ) || context.WildcardMatch( search )
				: name.Contains( search, StringComparison.OrdinalIgnoreCase ) || context.Contains( search, StringComparison.OrdinalIgnoreCase );

			if ( matches == excluded )
				return false;
		}

		return true;
	}
}
