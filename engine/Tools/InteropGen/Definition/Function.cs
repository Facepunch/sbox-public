using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Facepunch.InteropGen;

/// <summary>
/// A method exposed across the interop boundary, with its return type, parameters and any [special]
/// markers (new/delete). May carry an inline native body or a hand-written call replacement.
/// </summary>
public class Function
{
	private static readonly Regex FunctionRegex = new(
		@"^(static)?[\s+]?(.+?)\s+([a-zA-Z0-9_]+?)\(((.+?))?\)( const)?;(.+)?",
		RegexOptions.IgnoreCase
	);

	private static readonly Regex InlineFunctionRegex = new(
		@"^inline\s(static)?[\s+]?(.+?)\s+([a-zA-Z0-9_]+?)\(((.+?))?\)( const)?(.+)?",
		RegexOptions.IgnoreCase
	);

	public bool Native { get; set; }
	public bool Static { get; set; }
	public string Name { get; set; }
	public Arg Return { get; set; } = new ArgVoid();
	public Arg[] Parameters { get; set; } = [];
	public Class Class { get; set; }
	public string MangledName { get; set; }
	public List<string> Special { get; set; } = [];
	public StringBuilder Body { get; set; }

	public bool HasReturn => !Return.IsVoid;

	/// <summary>
	/// If set, the native side emits this hand-written body instead of generating the call.
	/// </summary>
	public string NativeCallReplacement { get; set; }

	internal static Function Parse( string line )
	{
		Match match = FunctionRegex.Match( line.Trim() );
		return match.Success ? FromMatch( match ) : null;
	}

	/// <summary>
	/// An inline function carries its native body inline in the .def file (parsed separately by BodyParser).
	/// </summary>
	internal static Function ParseInline( string line )
	{
		Match match = InlineFunctionRegex.Match( line.Trim() );
		return match.Success ? FromMatch( match ) : null;
	}

	private static Function FromMatch( Match match )
	{
		Function f = new()
		{
			Native = true,
			Static = match.Groups[1].Success,
			Name = match.Groups[3].Value.Trim(),
			Return = Arg.Parse( match.Groups[2].Value + " returnvalue" ),
			Parameters = Arg.ParseMany( match.Groups[4].Value )
		};

		f.AddSpecial( match.Groups[7].Value );
		return f;
	}

	/// <summary>
	/// Parse the "[delete] [new]" style specials that can follow a function declaration.
	/// Each token has at most one surrounding pair of brackets stripped.
	/// </summary>
	internal void AddSpecial( string value )
	{
		if ( string.IsNullOrEmpty( value ) )
		{
			return;
		}

		foreach ( string token in value.Split( ' ', StringSplitOptions.RemoveEmptyEntries ) )
		{
			string part = token;

			if ( part.Length > 0 && part[0] == '[' )
			{
				part = part[1..];
			}

			if ( part.Length > 0 && part[^1] == ']' )
			{
				part = part[..^1];
			}

			if ( part.Length > 0 )
			{
				Special.Add( part );
			}
		}
	}

	internal string GetManagedName()
	{
		return Name == "GetType" ? "GetType_Native" : Name;
	}

	internal List<string> Attributes = [];

	internal void TakeAttributes( List<string> attributes )
	{
		Attributes.AddRange( attributes );
		attributes.Clear();
	}

	internal bool HasAttribute( string name )
	{
		return Attributes.Contains( name, StringComparer.OrdinalIgnoreCase ) || Class.HasAttribute( name );
	}

	/// <summary>
	/// [nogc] adds [SuppressGCTransition] to the function
	/// </summary>
	public bool IsNoGC
	{
		get
		{
			// we or our class are marked with nogc
			bool wantsNoGc = HasAttribute( "nogc" ) || Class.HasAttribute( "nogc" );
			if ( !wantsNoGc )
			{
				return false;
			}

			// can't do it if it's calling back to managed
			return !HasAttribute( "callback" );
		}
	}

	public Function Copy()
	{
		return new Function
		{
			Native = Native,
			Static = Static,
			Name = Name,
			Return = Return,
			Parameters = Parameters,
			Class = Class,
			MangledName = MangledName,
			Special = Special,
			Attributes = Attributes,
			NativeCallReplacement = NativeCallReplacement,
			Body = Body
		};
	}
}
