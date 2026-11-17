using System.Globalization;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// What vfx's HLSL parser does to today's includes before Slang sees them, for new-tier shaders that include them:
/// <c>ExternalDescriptorSet X Slot N;</c> becomes <c>#define X (3 + N)</c> (the static sets after the render system's
/// three), and each global's <c>&lt; Attribute( ... ); Default( ... ); Filter( ... ); &gt;</c> annotations are recorded
/// so reflection can say what a binding or a <c>$Globals</c> member is bound to. Lines are kept, so diagnostics point at
/// the real file.
/// </summary>
internal static partial class VfxIncludes
{
	[GeneratedRegex( @"^[ \t]*ExternalDescriptorSet[ \t]+(\w+)[ \t]+Slot[ \t]+(\d+)[ \t]*;", RegexOptions.Multiline )]
	private static partial Regex ExternalSet();

	// A global's name before its annotations: "Type name [: register( ... )] < ... >;" or "CreateTexture2D( name ) < ... >;"
	[GeneratedRegex( @"(?:\bCreate\w+\(\s*(?<call>\w+)\s*\)|\b(?<name>\w+))\s*(?:\[[^\]]*\]\s*)?(?::\s*register\s*\([^)]*\)\s*)?<(?<annotations>[^<>]*)>\s*;" )]
	private static partial Regex Annotated();

	[GeneratedRegex( @"(\w+)\s*\(([^()]*(?:\([^()]*\)[^()]*)*)\)" )]
	private static partial Regex Entry();

	// "int X < Attribute( "Y" ); Default( 0 ); SpecializedConstant(); >;"
	[GeneratedRegex( @"^(?<lead>[ \t]*)(?<type>int|uint|bool|float)[ \t]+(?<name>\w+)[ \t]*<(?<annotations>[^<>]*SpecializedConstant\(\s*\)[^<>]*)>[ \t]*;", RegexOptions.Multiline )]
	private static partial Regex Specialized();

	static readonly Dictionary<string, int> specializationIds = new( StringComparer.Ordinal );

	/// <summary>First specialization constant id given to <c>SpecializedConstant()</c> globals; lower ids are the shaders' own.</summary>
	public const int FirstSpecializationId = 64;

	/// <summary>The specialization constant id of a <c>SpecializedConstant()</c> global, the same for every compile in this process.</summary>
	public static int SpecializationId( string name )
	{
		lock ( specializationIds )
		{
			if ( !specializationIds.TryGetValue( name, out var id ) ) specializationIds[name] = id = FirstSpecializationId + specializationIds.Count;
			return id;
		}
	}

	/// <summary>The global a specialization constant id was given to, or null.</summary>
	public static string SpecializationName( int id )
	{
		lock ( specializationIds ) return specializationIds.FirstOrDefault( p => p.Value == id ).Key;
	}

	/// <summary>Rewrite one included file's text and add its annotations to <paramref name="annotations"/>.</summary>
	public static string Rewrite( string text, Dictionary<string, Annotations> annotations )
	{
		text = ExternalSet().Replace( text, m => $"#define {m.Groups[1].Value} {3 + int.Parse( m.Groups[2].Value, CultureInfo.InvariantCulture )}" );

		// SpecializedConstant() globals are specialization constants, as vfx compiles them: the pipeline is made per value
		text = Specialized().Replace( text, m =>
		{
			var name = m.Groups["name"].Value;
			var type = m.Groups["type"].Value;
			var parsed = Parse( m.Groups["annotations"].Value ) ?? new Annotations();
			parsed.Specialized = true;
			annotations[name] = parsed;
			var value = parsed.Default is { Length: > 0 } d ? d[0] : 0;
			var literal = type switch
			{
				"bool" => value != 0 ? "true" : "false",
				"float" => value.ToString( "R", CultureInfo.InvariantCulture ),
				"uint" => ((uint)Math.Max( value, 0 )).ToString( CultureInfo.InvariantCulture ),
				_ => ((int)value).ToString( CultureInfo.InvariantCulture ),
			};
			return $"{m.Groups["lead"].Value}[vk::constant_id( {SpecializationId( name )} )] const {type} {name} = {literal};";
		} );

		var macros = AnnotationMacros( text );
		foreach ( Match m in Annotated().Matches( text ) )
		{
			var name = m.Groups["call"].Success ? m.Groups["call"].Value : m.Groups["name"].Value;
			var parsed = Parse( ExpandMacros( m.Groups["annotations"].Value, macros ) );
			if ( parsed is not null ) annotations[name] = parsed;
		}

		return text;
	}

	// "#define SRGB SrgbRead( true )", "#define Address( a ) AddressU( a ); AddressV( a ); AddressW( a )"
	[GeneratedRegex( @"^[ \t]*#[ \t]*define[ \t]+(?<name>\w+)(?:\((?<arg>[ \t\w]*)\))?[ \t]+(?<body>[^\n]*?)[ \t]*$", RegexOptions.Multiline )]
	private static partial Regex Define();

	static readonly HashSet<string> annotationKeys = ["Attribute", "Default", "Default2", "Default3", "Default4", "SrgbRead", "Filter", "AddressU", "AddressV", "AddressW", "MaxAniso", "ComparisonFunc"];

	/// <summary>
	/// The file's macros that stand for annotations (a shader's own shorthand, such as <c>SRGB</c> or <c>Address( CLAMP )</c>,
	/// which vfx's preprocessor expands before it reads them): bodies made of annotations alone, by name.
	/// </summary>
	static Dictionary<string, (string Argument, string Body)> AnnotationMacros( string text )
	{
		var macros = new Dictionary<string, (string, string)>( StringComparer.Ordinal );
		if ( !text.Contains( "#define", StringComparison.Ordinal ) ) return macros;
		foreach ( Match m in Define().Matches( text ) )
		{
			var body = m.Groups["body"].Value;
			if ( body.Contains( '=' ) || body.Contains( '#' ) || annotationKeys.Contains( m.Groups["name"].Value ) ) continue;
			var entries = Entry().Matches( body );
			if ( entries.Count == 0 || entries.Any( e => !annotationKeys.Contains( e.Groups[1].Value ) ) ) continue;
			macros[m.Groups["name"].Value] = (m.Groups["arg"].Success ? m.Groups["arg"].Value.Trim() : null, body);
		}

		return macros;
	}

	/// <summary>An annotation block with the annotation macros in it expanded.</summary>
	static string ExpandMacros( string block, Dictionary<string, (string Argument, string Body)> macros )
	{
		if ( macros.Count == 0 ) return block;
		foreach ( var (name, (argument, body)) in macros )
		{
			if ( !block.Contains( name, StringComparison.Ordinal ) ) continue;
			block = argument is null
				? Regex.Replace( block, $@"\b{name}\b(?!\s*\()", body )
				: Regex.Replace( block, $@"\b{name}\s*\(([^()]*)\)", m => argument.Length == 0 ? body : Regex.Replace( body, $@"\b{argument}\b", m.Groups[1].Value.Trim() ) );
		}

		return block;
	}

	/// <summary>One annotation block; null when it says nothing reflection needs.</summary>
	internal static Annotations Parse( string block )
	{
		var result = new Annotations();
		var any = false;
		foreach ( Match entry in Entry().Matches( block ) )
		{
			var key = entry.Groups[1].Value;
			var argument = entry.Groups[2].Value.Trim();
			switch ( key )
			{
				case "Attribute":
					result.Attribute = argument.Trim( '"', ' ' );
					any = true;
					break;
				case "Default" or "Default2" or "Default3" or "Default4":
					result.Default = Floats( argument );
					any = true;
					break;
				case "SrgbRead":
					result.SrgbRead = argument.Equals( "true", StringComparison.OrdinalIgnoreCase ) || argument == "1";
					any = true;
					break;
				case "Filter":
					result.Filter = argument;
					any = true;
					break;
				case "AddressU":
					result.AddressU = argument;
					any = true;
					break;
				case "AddressV":
					result.AddressV = argument;
					any = true;
					break;
				case "AddressW":
					result.AddressW = argument;
					any = true;
					break;
				case "MaxAniso":
					result.MaxAniso = int.TryParse( argument, CultureInfo.InvariantCulture, out var aniso ) ? aniso : 0;
					any = true;
					break;
				case "ComparisonFunc":
					result.Comparison = argument;
					any = true;
					break;
				case "SpecializedConstant":
					result.Specialized = true;
					any = true;
					break;
				case "Source":
					result.Source = argument;
					any = true;
					break;
				case "SourceArg":
					result.SourceArg = argument;
					any = true;
					break;
			}
		}

		return any ? result : null;
	}

	static float[] Floats( string text )
	{
		var values = new List<float>();
		foreach ( var part in text.Split( ',' ) )
		{
			var s = part.Trim().TrimEnd( 'f', 'F' );
			if ( s.Equals( "true", StringComparison.OrdinalIgnoreCase ) ) values.Add( 1 );
			else if ( s.Equals( "false", StringComparison.OrdinalIgnoreCase ) ) values.Add( 0 );
			else if ( float.TryParse( s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v ) ) values.Add( v );
			else values.Add( 0 );
		}

		return [.. values];
	}
}

/// <summary>What a global's vfx annotations say: the attribute it's bound from, its default, and sampler state.</summary>
public sealed class Annotations
{
	/// <summary>The render attribute it's bound from (<c>Attribute( "X" )</c>); null for none.</summary>
	public string Attribute { get; set; }

	/// <summary>Its default value (<c>Default</c> to <c>Default4</c>); null for none.</summary>
	public float[] Default { get; set; }

	/// <summary><c>SrgbRead( true )</c>.</summary>
	public bool SrgbRead { get; set; }

	/// <summary>Sampler state (<c>Filter</c>, <c>AddressU/V/W</c>, <c>MaxAniso</c>, <c>ComparisonFunc</c>), as written.</summary>
	public string Filter { get; set; }

	/// <inheritdoc cref="Filter"/>
	public string AddressU { get; set; }

	/// <inheritdoc cref="Filter"/>
	public string AddressV { get; set; }

	/// <inheritdoc cref="Filter"/>
	public string AddressW { get; set; }

	/// <inheritdoc cref="Filter"/>
	public int MaxAniso { get; set; }

	/// <inheritdoc cref="Filter"/>
	public string Comparison { get; set; }

	/// <summary><c>SpecializedConstant()</c>: vfx makes it a specialization constant; here it stays a constant.</summary>
	public bool Specialized { get; set; }

	/// <summary>A value the engine computes per draw (<c>Source( Viewport )</c>, <c>Source( InvTextureDim )</c>), as written; null for none.</summary>
	public string Source { get; set; }

	/// <summary>The variable a texture-derived <see cref="Source"/> reads (<c>SourceArg( g_tColor )</c>); null for none.</summary>
	public string SourceArg { get; set; }
}
