using System.Text.RegularExpressions;

namespace Sandbox;

static partial class CompilerRules
{
	public static List<Regex> Blacklist = new();

	// Carve-outs: a member matching one of these is allowed even if it matches a Blacklist pattern.
	// Lets us keep a broad default-deny ban (e.g. "Unsafe.*") while permitting a known-safe member.
	public static List<Regex> Allowlist = new();

	static CompilerRules()
	{
		AddRules( Methods );
		AddRules( Attributes );
		AddRules( Types );
	}

	static void AddRules( IEnumerable<string> rules )
	{
		foreach ( var rule in rules )
		{
			var line = rule.Trim();

			bool exception = line.StartsWith( '!' );
			if ( exception )
				line = line[1..];

			var wildcard = Regex.Escape( line ).Replace( "\\*", ".*" );
			wildcard = $"^{wildcard}$";

			var regex = new Regex( wildcard, RegexOptions.Compiled );

			if ( exception )
				Allowlist.Add( regex );
			else
				Blacklist.Add( regex );
		}
	}

	/// <summary>
	/// True if this fully-qualified symbol is prohibited - i.e. it matches a blacklist pattern and
	/// isn't carved back out by an allowlist (!) exception.
	/// </summary>
	public static bool IsBlocked( string name )
	{
		if ( !Blacklist.Any( x => x.IsMatch( name ) ) )
			return false;

		return !Allowlist.Any( x => x.IsMatch( name ) );
	}
}
