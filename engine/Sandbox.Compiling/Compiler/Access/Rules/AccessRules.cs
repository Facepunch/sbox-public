using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Sandbox;

// Matches Roslyn symbols against the single rule sequence in Rules.
sealed class AccessRules
{
	static readonly AccessRules _instance = new();
	public static AccessRules Instance => _instance;

	readonly HashSet<string> _wholesale = new( StringComparer.OrdinalIgnoreCase );
	readonly List<Regex> _allow = [];
	readonly List<Regex> _deny = [];
	readonly List<Regex> _block = [];
	readonly List<Regex> _unblock = [];

	AccessRules() : this( Rules.All )
	{
	}

	internal AccessRules( IEnumerable<string> rules )
	{
		foreach ( var line in rules )
			Add( line );
	}

	void Add( string raw )
	{
		var line = raw.Trim();
		if ( line.Length == 0 )
			return;

		switch ( line[0] )
		{
			case '-': _block.Add( Compile( Operand( line, raw ) ) ); return;
			case '+': _unblock.Add( Compile( Operand( line, raw ) ) ); return;
			case '!': AddGated( Operand( line, raw ), deny: true ); return;
			default: AddGated( line, deny: false ); return;
		}
	}

	static string Operand( string line, string raw )
	{
		if ( line.Length == 1 )
			throw new InvalidOperationException( $"Code-access rule has no pattern: '{raw}'" );

		return line[1..];
	}

	// Assembly is retained only for whole-assembly rules; member matching is assembly-agnostic
	// so facade and implementation symbols resolve to the same rule.
	void AddGated( string line, bool deny )
	{
		var slash = line.IndexOf( '/' );
		if ( slash <= 0 || slash == line.Length - 1 )
			throw new InvalidOperationException( $"Gated code-access rule must be '<Assembly>/<signature>': '{line}'" );

		var rest = line[(slash + 1)..];

		if ( !deny && rest == "*" )
		{
			_wholesale.Add( line[..slash] );
			return;
		}

		(deny ? _deny : _allow).Add( Compile( rest ) );
	}

	static Regex Compile( string wildcard ) =>
		new( "^" + Regex.Escape( wildcard ).Replace( "\\*", ".*" ) + "$", RegexOptions.Compiled );

	static bool Matches( List<Regex> rules, string signature )
	{
		foreach ( var rule in rules )
		{
			if ( rule.IsMatch( signature ) )
				return true;
		}

		return false;
	}

	public bool WholesaleAssembly( string assembly ) => assembly is not null && _wholesale.Contains( assembly );

	public bool IsAllowed( string signature )
	{
		if ( Matches( _deny, signature ) )
			return false;

		return Matches( _allow, signature );
	}

	public bool IsBlocked( string signature )
	{
		if ( !Matches( _block, signature ) )
			return false;

		return !Matches( _unblock, signature );
	}
}
