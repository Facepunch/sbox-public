using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Facepunch.InteropGen;

/// <summary>
/// Assigns each function and variable a unique, C-safe mangled name (namespace_class_member, with long
/// parts vowel-stripped and truncated), de-duplicating any collisions with a numeric suffix.
/// </summary>
public class Mangler
{
	private readonly HashSet<string> taken = [];

	/// <summary>
	/// Walk every class and stamp a unique MangledName onto each of its functions and variables.
	/// </summary>
	internal void Mangle( List<Class> classes )
	{
		foreach ( Class c in classes )
		{
			foreach ( Function f in c.Functions )
			{
				f.MangledName = GetMangledName( c.NativeNamespace, c.NativeName, f.Name );
			}

			foreach ( Variable v in c.Variables )
			{
				v.MangledName = GetMangledName( c.NativeNamespace, c.NativeName, v.Name );
			}
		}
	}

	/// <summary>
	/// Find a unique mangled name for this member
	/// </summary>
	private string GetMangledName( string ns, string c, string f )
	{
		string name = GenerateMangledName( ns, c, f );

		int i = 1;
		while ( taken.Contains( name ) )
		{
			name = GenerateMangledName( ns, c, $"{f}_{i++}" );
		}

		_ = taken.Add( name );
		return name;
	}

	/// <summary>
	/// Generate a mangled name from namespace, class and function
	/// </summary>
	private string GenerateMangledName( string ns, string c, string f )
	{
		ns = Shortify( ns );
		c = Shortify( c );

		string str = $"{ns}_{c}_{f}".Trim( '_' );

		str = Regex.Replace( str, @"[^a-z0-9\._]", "", RegexOptions.IgnoreCase );

		return str;
	}

	private string Shortify( string s )
	{
		if ( s.Length <= 16 )
		{
			return s;
		}

		s = Regex.Replace( s, @"[aeiou<>\.\:\s\-]", "", RegexOptions.IgnoreCase );

		return s.Length > 16 ? s[..16] : s;
	}
}
