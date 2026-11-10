using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Facepunch.InteropGen;

/// <summary>
/// A value type shared across the boundary: a struct, an enum, or a pointer-handle (DECLARE_POINTER_HANDLE).
/// Both sides must agree on its size, which is checked at startup.
/// </summary>
public class Struct
{
	private static readonly Regex StructParseRegex = new(
		@"([\w.:\(\)]+)( [\s+]?(?:as|is) [\s+]?([\w.:]+))?(.+)?",
		RegexOptions.IgnoreCase
	);

	public string NativeName { get; set; }
	public string NativeNamespace { get; set; }

	public string ManagedName { get; set; }
	public string ManagedNamespace { get; set; }

	public bool IsEnum { get; set; }

	/// <summary>
	/// A native type that is a pointer but is wrapped in a way where it pretends it isn't one
	/// Usually wrapped using DECLARE_POINTER_HANDLE etc
	/// </summary>
	public bool IsPointer { get; set; }

	public string NativeNameWithNamespace => string.IsNullOrEmpty( NativeNamespace ) ? NativeName : $"{NativeNamespace}::{NativeName}";
	public string ManagedNameWithNamespace => string.IsNullOrEmpty( ManagedNamespace ) ? ManagedName : $"{ManagedNamespace}.{ManagedName}";

	internal static Struct Parse( bool isNative, string type, string line )
	{
		Match match = StructParseRegex.Match( line );
		if ( !match.Success )
		{
			Log.WriteLine( $"Couldn't parse {type} definition: {line}" );
			return null;
		}

		string name = match.Groups[1].Value;
		string alias = match.Groups[3].Value;

		if ( string.IsNullOrWhiteSpace( alias ) )
		{
			alias = name;
		}

		if ( !isNative )
		{
			(name, alias) = (alias, name);
		}

		Struct s = new()
		{
			NativeName = name,
			ManagedName = alias,
			IsEnum = type == "enum",
			IsPointer = type == "pointer"
		};

		if ( name.Contains( '.' ) )
		{
			int last = name.LastIndexOf( '.' );
			s.NativeName = name[(last + 1)..];
			s.NativeNamespace = name[..last].Replace( ".", "::" );
		}

		if ( alias.Contains( '.' ) )
		{
			int last = alias.LastIndexOf( '.' );
			s.ManagedName = alias[(last + 1)..];
			s.ManagedNamespace = alias[..last];
		}

		return s;
	}

	private readonly List<string> attr = [];

	internal void TakeAttributes( List<string> attributes )
	{
		attr.AddRange( attributes );
		attributes.Clear();
	}

	internal bool HasAttribute( string name )
	{
		return attr.Contains( name );
	}
}
