using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// A surface's material inputs, generated from its <c>[Material]</c> struct's reflected attributes: the feature lines,
/// parameters, attributes, texture inputs and made textures, and vertex input signature the material compiler
/// (<see cref="MaterialInputs"/>), the material editor (<see cref="MaterialEditorDescription"/>) and the feature rules
/// (<see cref="FeatureRules"/>) read, in the text they read it as (vfx's material-input declarations). A surface file
/// declares none of it: <see cref="PassModule"/> appends this as the module's <c>MATERIAL_INPUTS</c> block.
/// Compatibility for materials compiled long ago is <see cref="ShaderCompatibility"/>'s.
/// </summary>
public static partial class SurfaceDeclarations
{
	/// <summary>The define the block is read with.</summary>
	public const string BlockDefine = "MATERIAL_INPUTS";

	/// <summary>
	/// Where <c>slang.dll</c> is, for the reflection a module's declarations are generated from; null to look for it above the
	/// module (<c>game/bin/win64</c>) and the process.
	/// </summary>
	public static string SlangDirectory { get; set; }

	static readonly ConcurrentDictionary<string, string> generated = new();
	static readonly ConcurrentDictionary<string, SlangCompiler> compilers = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// <paramref name="text"/> with its generated block appended, when it's a surface (a <c>[Material]</c> struct) without
	/// a block of its own; otherwise the text as it is. Generated once per text.
	/// </summary>
	public static string Append( string path, string text, IReadOnlyList<string> searchPaths )
	{
		if ( !MaterialInputs.SurfaceText( text ) || text.Contains( BlockDefine, StringComparison.Ordinal ) ) return text;

		var key = path + "\n" + string.Join( "\n", searchPaths ?? [] ) + "\n" + Hash( text );
		var block = generated.GetOrAdd( key, _ => Block( path, text, searchPaths ) );
		return block is null ? text : text + "\n#if " + BlockDefine + "\n" + block + "#endif\n";
	}

	static string Hash( string text ) => Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( Encoding.UTF8.GetBytes( text ) ) );

	static string Block( string path, string text, IReadOnlyList<string> searchPaths )
	{
		var directory = SlangDirectory ?? FindSlang( Path.GetDirectoryName( Path.GetFullPath( path ) ) ) ?? FindSlang( AppContext.BaseDirectory );
		if ( directory is null ) return null;

		// The engine's shaders (sbox.surface, the shared headers) are found beside the native folder: game/core/shaders
		var paths = (searchPaths ?? []).Append( Path.GetDirectoryName( Path.GetFullPath( path ) ) ).ToList();
		var core = Path.GetFullPath( Path.Combine( directory, "..", "..", "core", "shaders" ) );
		if ( Directory.Exists( core ) ) paths.Add( core );
		paths = [.. paths.Distinct( StringComparer.OrdinalIgnoreCase )];

		var compiler = compilers.GetOrAdd( directory + "|" + string.Join( "|", paths ), _ => new SlangCompiler( directory, paths ) );
		var layout = compiler.ReflectLayout( path, text );
		if ( layout is null ) return null;
		return Generate( layout, Path.GetFileNameWithoutExtension( path ), name => ReadInclude( name, paths ) );
	}

	/// <summary>The engine's native folder above <paramref name="start"/> with Slang in it (<c>game/bin/win64</c>).</summary>
	static string FindSlang( string start )
	{
		var library = OperatingSystem.IsWindows() ? "slang.dll" : OperatingSystem.IsMacOS() ? "libslang.dylib" : "libslang.so";
		for ( var folder = start; folder is not null; folder = Path.GetDirectoryName( folder ) )
		{
			if ( File.Exists( Path.Combine( folder, library ) ) ) return folder;
			foreach ( var native in new[] { "win64", "linuxsteamrt64", "osxarm64" } )
			{
				var candidate = Path.Combine( folder, "bin", native );
				if ( File.Exists( Path.Combine( candidate, library ) ) ) return candidate;
			}
		}

		return null;
	}

	static string ReadInclude( string name, IReadOnlyList<string> searchPaths )
	{
		foreach ( var folder in searchPaths )
		{
			var file = Path.Combine( folder, name );
			if ( File.Exists( file ) ) return File.ReadAllText( file );
		}

		return null;
	}

	/// <summary>
	/// The material-input declarations of <paramref name="layout"/>, the struct of the module <paramref name="module"/>
	/// (its file name, for <see cref="ShaderCompatibility"/>); <paramref name="readInclude"/> reads an engine header by
	/// name, for the features a shared set declares.
	/// </summary>
	public static string Generate( MaterialLayout layout, string module, Func<string, string> readInclude )
	{
		var context = new Context( layout, ShaderCompatibility.For( module ), readInclude );
		return context.Write();
	}

	sealed class Context( MaterialLayout layout, ShaderCompatibility compatibility, Func<string, string> readInclude )
	{
		readonly StringBuilder text = new();

		/// <summary>Each field a condition can name, as its feature (<c>F_X</c>).</summary>
		readonly Dictionary<string, string> features = layout.Fields.Where( f => f.Source is { } s && s.StartsWith( "F_", StringComparison.Ordinal ) ).ToDictionary( f => f.Name, f => f.Source, StringComparer.Ordinal );

		IEnumerable<SurfaceAttribute> Struct( string name ) => layout.Attributes.Where( a => a.Name == name );

		public string Write()
		{
			Features();
			Defines();
			Parameters();
			Attributes();
			// The shared inputs (the standard processing's) where the first field they make is, in field order
			var shared = compatibility?.Inputs;
			foreach ( var field in layout.Fields.Where( f => f.IsTexture ) )
			{
				if ( shared is not null && compatibility.TakenBySet?.Contains( field.Name ) == true )
				{
					Line( shared );
					shared = null;
				}

				Texture( field );
			}

			if ( shared is not null ) Line( shared );
			if ( compatibility?.Textures is { } textures ) Line( textures );
			Vertex();
			return text.ToString();
		}

		void Line( string line ) => text.Append( line.Replace( "\r\n", "\n" ).TrimEnd( '\n' ) ).Append( '\n' );

		/// <summary>A condition over the fields, each field named as its feature (<c>F_X</c>) or its static combo (<c>S_X</c>).</summary>
		string Condition( string condition, bool combo ) => FieldName().Replace( condition, m =>
			features.TryGetValue( m.Value, out var feature ) ? (combo ? "S_" + feature[2..] : feature) : m.Value );

		// Features: the shared set's include, then each feature field's line and rules, in field order; the shaders a surface
		// stands for each have their own lines (standard's complex, simple, generic), picked by FeatureRules.ShaderDefine
		void Features()
		{
			if ( compatibility?.Prefix is { } prefix ) Line( prefix );

			var shared = new HashSet<string>( StringComparer.Ordinal );
			var own = new StringBuilder();
			foreach ( var set in Struct( "Features" ) )
			{
				var include = set[0] switch
				{
					"Common" => "common/features.hlsl",
					"Sbox" => "vr_common_features.fxc",
					var other => throw new ShaderCompileException( null, $"{layout.Type}: [Features( \"{other}\" )]: the sets are Common and Sbox" ),
				};
				own.Append( "#include \"" ).Append( include ).Append( "\"\n" );
				DeclaredFeatures( include, shared, [] );
			}

			foreach ( var field in layout.Fields )
			{
				if ( field.Get( "Feature" ) is not { } feature ) continue;
				var name = feature[0];
				if ( !shared.Contains( name ) )
				{
					var (min, max) = field.Kind == MaterialFieldKind.Bool ? (0, 1) : ((int)(field.Min ?? 0), (int)(field.Max ?? 1));
					own.Append( "Feature( " ).Append( name ).Append( ", " ).Append( min ).Append( ".." ).Append( max );
					if ( field.Get( "Options" ) is { } options )
					{
						var names = options[0].Split( '|', StringSplitOptions.TrimEntries );
						own.Append( "( " ).AppendJoin( ", ", names.Select( ( n, i ) => $"{min + i}=\"{n}\"" ) ).Append( " )" );
					}

					if ( field.Group is { } group ) own.Append( ", \"" ).Append( group ).Append( '"' );
					own.Append( " );\n" );
				}

				foreach ( var rule in field.All( "Rule" ) ) own.Append( "FeatureRule( " ).Append( Condition( rule[0], false ) ).Append( ", \"" ).Append( rule[1] ).Append( "\" );\n" );
				if ( compatibility?.RulesAfter is { } after && after.TryGetValue( name, out var rules ) ) foreach ( var rule in rules ) own.Append( rule ).Append( '\n' );
			}

			if ( compatibility?.ShaderFeatures is { Count: > 0 } others )
			{
				var first = true;
				foreach ( var (index, lines) in others.OrderByDescending( o => o.Key ) )
				{
					text.Append( first ? "#if " : "#elif " ).Append( FeatureRules.ShaderDefine ).Append( " == " ).Append( index ).Append( '\n' );
					Line( lines );
					first = false;
				}

				text.Append( "#else\n" ).Append( own ).Append( "#endif\n" );
			}
			else
			{
				text.Append( own );
			}
		}

		/// <summary>The features an engine header declares, and those of the headers it includes.</summary>
		void DeclaredFeatures( string include, HashSet<string> declared, HashSet<string> seen )
		{
			if ( !seen.Add( include ) || readInclude( include ) is not { } header ) return;
			foreach ( Match m in FeatureDeclaration().Matches( header ) ) declared.Add( m.Groups[1].Value );
			foreach ( Match m in IncludeLine().Matches( header ) ) DeclaredFeatures( m.Groups[1].Value, declared, seen );
		}

		// Each feature field's static combo, so conditions read it as vfx's code did (S_X F_X); the compatibility's defines
		void Defines()
		{
			// A module on the shared vfx headers has its own static combos (the compatibility's defines), which the headers read
			foreach ( var feature in compatibility?.Inputs is null ? features.Values.Distinct() : [] )
			{
				text.Append( "#define S_" ).Append( feature[2..] ).Append( ' ' ).Append( feature ).Append( '\n' );
			}

			if ( compatibility?.Defines is { } defines ) Line( defines );
		}

		// Value fields with a [Group]: the parameters the material editor shows
		void Parameters()
		{
			foreach ( var field in layout.Fields )
			{
				if ( field.IsTexture || field.Group is null || field.Get( "Feature" ) is not null ) continue;
				if ( field.Source is { } s && (s.StartsWith( "F_", StringComparison.Ordinal ) || s.Contains( ':' ) || s == "ShaderVersion") ) continue;

				var type = (field.Kind, field.Components) switch
				{
					(MaterialFieldKind.Float, 1 ) => "float",
					(MaterialFieldKind.Float, var n ) => $"float{n}",
					(MaterialFieldKind.Bool, _ ) => "bool",
					(MaterialFieldKind.Int, 1 ) or (MaterialFieldKind.UInt, 1 ) => "int",
					(_, var n ) => $"int{n}",
				};

				var name = field.Source ?? field.Kind switch
				{
					MaterialFieldKind.Float when field.Components == 1 => "g_fl",
					MaterialFieldKind.Float => "g_v",
					MaterialFieldKind.Bool => "g_b",
					_ => "g_n",
				} + field.Name;

				text.Append( type ).Append( ' ' ).Append( name ).Append( " < " );
				if ( field.Get( "Color" ) is not null ) text.Append( "UiType( Color ); " );
				var defaults = Enumerable.Range( 0, field.Components ).Select( i => field.Default is { } d && i < d.Length ? d[i] : 0f );
				text.Append( field.Components == 1 ? "Default( " : $"Default{field.Components}( " ).AppendJoin( ", ", defaults.Select( Number ) ).Append( " ); " );
				if ( field.Min is { } min && field.Max is { } max )
				{
					text.Append( field.Components == 1 ? "Range( " : $"Range{field.Components}( " )
						.AppendJoin( ", ", Enumerable.Repeat( min, field.Components ).Concat( Enumerable.Repeat( max, field.Components ) ).Select( Number ) ).Append( " ); " );
				}

				text.Append( "UiGroup( \"" ).Append( field.Group ).Append( "\" ); " );
				if ( field.Get( "ShowIf" ) is { } show ) text.Append( "ActiveIf( " ).Append( Condition( show[0], false ) ).Append( " ); " );
				text.Append( ">;\n" );
			}
		}

		// The struct's attributes, in order: true, worked out per material, kept as an expression, or set where a condition holds
		void Attributes()
		{
			foreach ( var attribute in layout.Attributes )
			{
				switch ( attribute.Name )
				{
					case "Attribute":
						text.Append( "BoolAttribute( " ).Append( attribute[0] ).Append( ", true );\n" );
						break;
					case "AttributeOf":
						text.Append( "BoolAttribute( " ).Append( attribute[0] ).Append( ", " ).Append( Condition( attribute[1], true ) ).Append( " );\n" );
						break;
					case "AttributeExpression":
						text.Append( "BoolAttribute( " ).Append( attribute[0] ).Append( ", " ).Append( Condition( attribute[1], false ) ).Append( " );\n" );
						break;
					case "AttributeIf":
						text.Append( "#if ( " ).Append( Condition( attribute[1], false ) ).Append( " )\nBoolAttribute( " ).Append( attribute[0] ).Append( ", true );\n#endif\n" );
						break;
				}
			}
		}

		readonly HashSet<string> declaredInputs = new( StringComparer.Ordinal );

		// A texture field: its inputs and the made texture, by its packs or its name's convention (the common inputs:
		// Color, Normal, Rma and their suffixed sets)
		void Texture( MaterialField field )
		{
			if ( compatibility?.TakenBySet?.Contains( field.Name ) == true ) return;

			// The conditional packs first, in order; an unconditional one is what's left
			var packs = field.All( "PackIf" ).Select( p => (Channels: p[0], Format: p[1], Condition: p[2]) )
				.Concat( field.All( "Pack" ).Select( p => (Channels: p[0], Format: p[1], Condition: (string)null) ) ).ToList();
			if ( packs.Count == 0 && Convention( field ) is { } convention ) packs.Add( (convention.Channels, convention.Format, null) );
			if ( packs.Count == 0 ) return;

			var when = field.Get( "When" );
			if ( when is not null ) text.Append( "#if ( " ).Append( Condition( when[0], false ) ).Append( " )\n" );

			var inputs = new List<string>();
			foreach ( var pack in packs )
				foreach ( var channel in Channels( pack.Channels ) )
					foreach ( var input in channel.Inputs ) if ( !inputs.Contains( input ) ) inputs.Add( input );
			foreach ( var input in inputs ) Input( field, input );

			var name = compatibility?.TextureNames?.GetValueOrDefault( field.Name ) ?? (field.Source is { } source && source.StartsWith( "g_t", StringComparison.Ordinal ) ? source : "g_t" + field.Name);
			var conditional = packs.Exists( p => p.Condition is not null );
			for ( int i = 0; i < packs.Count; i++ )
			{
				var pack = packs[i];
				if ( conditional ) text.Append( i == 0 ? "#if ( " : pack.Condition is null ? "#else" : "#elif ( " ).Append( pack.Condition is null ? "" : Condition( pack.Condition, false ) + " )" ).Append( '\n' );
				text.Append( field.Kind == MaterialFieldKind.TextureCube ? "TextureCube " : "Texture2D " ).Append( name ).Append( " < " );
				foreach ( var channel in Channels( pack.Channels ) )
				{
					var inputNames = channel.Inputs.Select( InputName ).ToList();
					text.Append( "Channel( " ).Append( channel.Channels ).Append( ", " ).Append( channel.Algorithm ).Append( "( " ).AppendJoin( ", ", inputNames ).Append( " ), " )
						.Append( channel.Space ?? (InputSettings( field, channel.Inputs[0] ).Srgb ? "Srgb" : "Linear") ).Append( " ); " );
				}

				text.Append( "OutputFormat( " ).Append( pack.Format ).Append( " ); SrgbRead( " ).Append( field.Srgb ? "true" : "false" ).Append( " ); " );
				if ( field.Get( "ShowIf" ) is { } show ) text.Append( "ActiveIf( " ).Append( Condition( show[0], false ) ).Append( " ); " );
				text.Append( ">;\n" );
			}

			if ( conditional ) text.Append( "#endif\n" );
			foreach ( var attribute in field.All( "TextureAttribute" ) ) text.Append( "TextureAttribute( " ).Append( attribute[0] ).Append( ", " ).Append( name ).Append( " );\n" );
			if ( when is not null ) text.Append( "#endif\n" );
		}

		/// <summary>The common inputs' packing of a field named for one (<c>Color</c>, <c>NormalB</c>, <c>RmaB</c>).</summary>
		static (string Channels, string Format)? Convention( MaterialField field )
		{
			foreach ( var (stem, channels) in CommonPackings )
			{
				if ( !field.Name.StartsWith( stem, StringComparison.Ordinal ) ) continue;
				var suffix = field.Name[stem.Length..];
				return (channels.Replace( "{0}", suffix, StringComparison.Ordinal ), "BC7");
			}

			return null;
		}

		static readonly (string Stem, string Channels)[] CommonPackings =
		[
			("Color", "RGB: AlphaWeighted( Color{0}, Translucency{0} ); A: Translucency{0}"),
			("Normal", "RGB: Normal{0}; A: TintMask{0}"),
			("Rma", "R: Roughness{0}; G: Metalness{0}; B: AmbientOcclusion{0}; A: BlendMask{0}"),
		];

		/// <summary>
		/// An input's name as declared: a pack's short name (<c>Color</c>) is <c>TextureColor</c>, unless an <c>[Input]</c>
		/// declares an input of that very name (fur's <c>BaseColor</c>).
		/// </summary>
		string InputName( string input ) => Declared( input ) is not null || input.StartsWith( "Texture", StringComparison.Ordinal ) ? input : "Texture" + input;

		/// <summary>The <c>[Input]</c> or <c>[InputWith]</c> declaring the input of this full name, or null.</summary>
		SurfaceAttribute Declared( string name ) => layout.Fields.SelectMany( f => f.All( "Input" ).Concat( f.All( "InputWith" ) ) ).FirstOrDefault( a => a[0] == name );

		void Input( MaterialField field, string input )
		{
			var name = InputName( input );
			if ( !declaredInputs.Add( name ) ) return;
			var settings = InputSettings( field, input );
			text.Append( field.Kind == MaterialFieldKind.TextureCube ? "CreateInputTextureCube( " : "CreateInputTexture2D( " ).Append( name ).Append( ", " ).Append( settings.Srgb ? "Srgb" : "Linear" ).Append( ", 8, \"" ).Append( settings.Processor )
				.Append( "\", \"" ).Append( settings.Ending ).Append( "\", \"" ).Append( settings.Group ).Append( "\", " ).Append( settings.Default ).Append( " );\n" );
		}

		/// <summary>An input's colour space, processor, file ending, pane and default: its <c>[Input]</c>'s, else its name's convention.</summary>
		(bool Srgb, string Processor, string Ending, string Group, string Default) InputSettings( MaterialField field, string input )
		{
			var stem = input.StartsWith( "Texture", StringComparison.Ordinal ) ? input["Texture".Length..] : input;
			var convention = InputConventions.FirstOrDefault( c => stem == c.Stem || (stem.StartsWith( c.Stem, StringComparison.Ordinal ) && stem.Length == c.Stem.Length + 1 && char.IsUpper( stem[^1] )) );
			var srgb = convention.Stem is null ? stem.EndsWith( "Color", StringComparison.Ordinal ) : convention.Srgb;
			var processor = convention.Processor ?? "";
			var ending = convention.Ending ?? "_color";
			var defaults = convention.Default ?? "Default3( 1.0, 1.0, 1.0 )";
			var group = field.Group is { } g ? $"{g}/{convention.Order}" : "";

			var declared = Declared( InputName( input ) );
			if ( declared is not null )
			{
				group = declared[1];
				foreach ( var setting in (declared[2] ?? "").Split( ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
				{
					if ( setting == "Srgb" ) srgb = true;
					else if ( setting == "Linear" ) srgb = false;
					else if ( setting.StartsWith( '_' ) ) ending = setting;
					else if ( setting.StartsWith( "Default", StringComparison.Ordinal ) ) defaults = setting.Replace( "\\\"", "\"", StringComparison.Ordinal );
					else processor = setting == "\"\"" ? "" : setting;
				}
			}

			return (srgb, processor, ending, group, defaults);
		}

		static readonly (string Stem, bool Srgb, string Processor, string Ending, string Default, int Order)[] InputConventions =
		[
			("Color", true, "", "_color", "Default3( 1.0, 1.0, 1.0 )", 10),
			("Normal", false, "NormalizeNormals", "_normal", "Default3( 0.5, 0.5, 1.0 )", 20),
			("Roughness", false, "", "_rough", "Default( 0.5 )", 30),
			("Metalness", false, "", "_metal", "Default( 1.0 )", 40),
			("AmbientOcclusion", false, "", "_ao", "Default( 1.0 )", 50),
			("BlendMask", false, "", "_blend", "Default( 1.0 )", 60),
			("Translucency", false, "", "_trans", "Default3( 1.0, 1.0, 1.0 )", 70),
			("TintMask", false, "", "_tint", "Default( 1.0 )", 70),
		];

		/// <summary>
		/// A pack's channels: <c>"RGB: AlphaWeighted( Color, Translucency ); A: Translucency"</c>, each in its first input's
		/// colour space unless it ends with one (<c>"RGB: TintLookup Srgb"</c>).
		/// </summary>
		static List<(string Channels, string Algorithm, List<string> Inputs, string Space)> Channels( string pack )
		{
			var list = new List<(string, string, List<string>, string)>();
			foreach ( var part in pack.Split( ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
			{
				var colon = part.IndexOf( ':' );
				var channels = part[..colon].Trim();
				var source = part[(colon + 1)..].Trim();
				string space = null;
				foreach ( var named in new[] { "Srgb", "Linear" } )
				{
					if ( !source.EndsWith( " " + named, StringComparison.Ordinal ) ) continue;
					space = named;
					source = source[..^named.Length].Trim();
				}

				var open = source.IndexOf( '(' );
				var (algorithm, inputs) = open < 0
					? ("Box", new List<string> { source })
					: (source[..open].Trim(), source[(open + 1)..source.LastIndexOf( ')' )].Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ).ToList());
				list.Add( (channels, algorithm, inputs, space) );
			}

			return list;
		}

		// The vertex inputs: a base (the engine's vertex input struct) and extra streams, each with an optional condition
		void Vertex()
		{
			if ( Struct( "Vertex" ).FirstOrDefault() is not { } vertex ) return;
			var members = new StringBuilder();
			var before = new StringBuilder();
			foreach ( var part in vertex[0].Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
			{
				var at = part.IndexOf( " if ", StringComparison.Ordinal );
				var name = at < 0 ? part : part[..at].Trim();
				var condition = at < 0 ? null : Condition( part[(at + 4)..].Trim(), false );
				if ( name == "SecondaryUv" )
				{
					before.Append( "#undef S_UV2\n#define S_UV2 " ).Append( condition ?? "1" ).Append( '\n' );
					continue;
				}

				var member = name switch
				{
					"Common" => "\t#include \"common/vertexinput.hlsl\"\n",
					"Standard" => "\t#include \"vr_shared_standard_vs_input.fxc\"\n",
					"Sbox" => "\t#include \"vr_common_vs_input.fxc\"\n",
					"Color" => "\tfloat4 vColor : COLOR0 < Semantic( Color ); >;\n",
					"VertexPaintBlend" => "\tfloat4 vColorBlendValues : TEXCOORD4 < Semantic( VertexPaintBlendParams ); >;\n",
					"VertexPaintTint" => "\tfloat4 vColorPaintValues : TEXCOORD5 < Semantic( VertexPaintTintColor ); >;\n",
					_ => throw new ShaderCompileException( null, $"{layout.Type}: [Vertex]: no input {name}" ),
				};
				if ( condition is null ) members.Append( member );
				else members.Append( "\t#if ( " ).Append( condition ).Append( " )\n" ).Append( member ).Append( "\t#endif\n" );
			}

			text.Append( before ).Append( "#define VS_INPUT_HAS_TANGENT_BASIS 1\nstruct VS_INPUT\n{\n" ).Append( members ).Append( "};\n" );
		}
	}

	static string Number( float value ) => value.ToString( "R", CultureInfo.InvariantCulture );

	[GeneratedRegex( @"\b[A-Za-z_]\w*\b" )]
	private static partial Regex FieldName();

	[GeneratedRegex( @"\bFeature\s*\(\s*(F_\w+)" )]
	private static partial Regex FeatureDeclaration();

	[GeneratedRegex( @"^[ \t]*#[ \t]*include[ \t]*""([^""]+)""", RegexOptions.Multiline )]
	private static partial Regex IncludeLine();
}
