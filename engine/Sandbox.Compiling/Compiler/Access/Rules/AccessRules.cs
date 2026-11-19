using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Sandbox;

// Matches Roslyn symbols against the single rule sequence in Rules.
sealed class AccessRules
{
	static readonly AccessRules _instance = new();
	public static AccessRules Instance => _instance;

	/// <summary>
	/// Hard blocks with no member allow-list, for tests that assert hard-block behaviour against a
	/// minimal reference set. Never use this to compile untrusted code.
	/// </summary>
	internal static AccessRules HardBlocksOnly { get; } = new( Rules.All, skipAllowList: true );

	readonly HashSet<string> _wholesale = new( StringComparer.OrdinalIgnoreCase );
	readonly List<Regex> _allow = [];
	readonly List<Regex> _deny = [];
	readonly List<Regex> _block = [];
	readonly List<Regex> _unblock = [];
	readonly bool _skipAllowList;
	readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _allowedCache = new();
	readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _blockedCache = new();

	AccessRules() : this( Rules.All )
	{
	}

	internal AccessRules( IEnumerable<string> rules, bool skipAllowList = false )
	{
		_skipAllowList = skipAllowList;

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

	// Gated rules match the full "<Assembly>/<signature>", with the assembly being the runtime
	// assembly that defines the type (see ImplementationAssembly).
	void AddGated( string line, bool deny )
	{
		var slash = line.IndexOf( '/' );
		if ( slash <= 0 || slash == line.Length - 1 )
			throw new InvalidOperationException( $"Gated code-access rule must be '<Assembly>/<signature>': '{line}'" );

		if ( !deny && line[(slash + 1)..] == "*" )
		{
			_wholesale.Add( line[..slash] );
			return;
		}

		(deny ? _deny : _allow).Add( Compile( line ) );
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

	// Every symbol reference asks, and there are about a thousand patterns to try
	public bool IsAllowed( string signature )
	{
		if ( _skipAllowList )
			return true;

		return _allowedCache.GetOrAdd( signature, s => !Matches( _deny, s ) && Matches( _allow, s ) );
	}

	public bool IsBlocked( string signature ) =>
		_blockedCache.GetOrAdd( signature, s => Matches( _block, s ) && !Matches( _unblock, s ) );
}
