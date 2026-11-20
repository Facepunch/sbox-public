using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// What a material of a module needs compiled, for its feature values: the artist's input textures
/// (<c>CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color", Default3( 1, 1, 1 ) )</c>), the textures made from
/// them (<c>Texture2D g_tColor &lt; Channel( RGB, Box( TextureColor ), Srgb ); OutputFormat( DXT1 ); SrgbRead( true ); &gt;</c>)
/// and the shader's attributes (<c>BoolAttribute( sky, true )</c>). This is what the material compiler reads from a
/// <c>.shader</c>'s compiled features, read from a module's text instead, as vfx reads it: every stage's lines with the
/// features' values and the static combos that follow them.
/// </summary>
public sealed partial class MaterialInputs
{
	/// <summary>An artist's input texture: its name (<c>TextureColor</c>), as the material names its file.</summary>
	public sealed record Input( string Name, string Type, bool Srgb, int MinBits, string Processor, string FileEnding, string Group, float[] Default, string DefaultFile );

	/// <summary>One channel group of a made texture: which channels, the mip algorithm, its inputs and the output's colour space.</summary>
	public sealed record Channel( string Channels, string Algorithm, IReadOnlyList<string> Inputs, bool Srgb );

	/// <summary>A texture made from inputs: the variable it binds (<c>g_tColor</c>), its type, format and channels.</summary>
	public sealed record Texture( string Name, string Type, string Format, bool SrgbRead, IReadOnlyList<Channel> Channels );

	/// <summary>The input textures, by name.</summary>
	public IReadOnlyDictionary<string, Input> Inputs { get; init; }

	/// <summary>The textures made from them, in the order declared.</summary>
	public IReadOnlyList<Texture> Textures { get; init; }

	/// <summary>The shader's attributes with a literal value (<c>BoolAttribute( sky, true )</c> as 1); ones that read a variable are left out.</summary>
	public IReadOnlyList<(string Name, string Kind, float[] Value)> Attributes { get; init; }

	/// <summary>
	/// The material inputs of <paramref name="module"/> for a material with these feature values (<c>F_TEXTURED</c> = 1;
	/// a feature it doesn't set is its lowest value).
	/// </summary>
	public static MaterialInputs Read( PassModule module, IReadOnlyDictionary<string, int> features )
	{
		var inputs = new Dictionary<string, Input>( StringComparer.Ordinal );
		var textures = new List<Texture>();
		var attributes = new List<(string, string, float[])>();
		var seenTextures = new HashSet<string>( StringComparer.Ordinal );
		var used = new HashSet<string>( StringComparer.Ordinal );

		foreach ( var stage in new[] { StageKind.Vertex, StageKind.Pixel, StageKind.Compute } )
		{
			if ( !module.Has( stage ) ) continue;

			var values = new Dictionary<string, int>( StringComparer.Ordinal );
			foreach ( var feature in module.Features ) values[feature.Name] = Math.Clamp( features.TryGetValue( feature.Name, out var v ) ? v : feature.Min, feature.Min, feature.Max );
			foreach ( var combo in module.Combos( stage ).Where( c => c.Static ) )
				values[combo.Name] = combo.Feature is not null && values.TryGetValue( combo.Feature, out var f ) ? Math.Clamp( f, combo.Min, combo.Max ) : combo.Min;
			foreach ( var combo in module.Combos( stage ).Where( c => !c.Static ) ) values.TryAdd( combo.Name, combo.Min );

			var text = Code( module.CompiledLines( stage, values ) );

			foreach ( Match m in InputDeclaration().Matches( text ) )
			{
				var args = Arguments( m.Groups["args"].Value );
				if ( args.Count < 6 || inputs.ContainsKey( args[0] ) ) continue;
				var (defaults, file) = Defaults( args.Count > 6 ? string.Join( ",", args.Skip( 6 ) ) : "" );
				inputs[args[0]] = new Input( args[0], m.Groups["type"].Value switch { "Cube" => "CUBE", "3D" => "3D", _ => "2D" },
					args[1].Equals( "Srgb", StringComparison.OrdinalIgnoreCase ), int.TryParse( args[2], CultureInfo.InvariantCulture, out var bits ) ? bits : 8,
					Unquote( args[3] ), Unquote( args[4] ).TrimStart( '_' ), Unquote( args[5] ), defaults, file );
			}

			foreach ( Match m in TextureDeclaration().Matches( text ) )
			{
				var name = m.Groups["name"].Value;
				var annotations = m.Groups["annotations"].Value;
				var format = OutputFormat().Match( annotations );
				var channels = ChannelAnnotation().Matches( annotations )
					.Select( c => new Channel( c.Groups["channels"].Value.ToLowerInvariant(), c.Groups["algorithm"].Value, Arguments( c.Groups["inputs"].Value ), c.Groups["space"].Value.Equals( "Srgb", StringComparison.OrdinalIgnoreCase ) ) )
					.ToList();
				// One the engine binds (Attribute or Source) isn't the material's, whatever else it declares
				if ( !format.Success || channels.Count == 0 || BoundByEngine().IsMatch( annotations ) ) continue;

				// Only textures the code reads are made, as vfx keeps only the variables a combo uses: the name once more
				// than its declaration
				if ( Regex.Matches( text, $@"\b{Regex.Escape( name )}\b" ).Count > 1 ) used.Add( name );
				if ( !seenTextures.Add( name ) ) continue;

				var type = (m.Groups["type"].Value + m.Groups["create"].Value) switch { var t when t.Contains( "Cube", StringComparison.Ordinal ) => "CUBE", var t when t.Contains( "3D", StringComparison.Ordinal ) => "3D", _ => "2D" };
				var srgbRead = SrgbRead().Match( annotations ) is { Success: true } s && s.Groups["value"].Value.Equals( "true", StringComparison.OrdinalIgnoreCase );
				textures.Add( new Texture( name, type, format.Groups["format"].Value, srgbRead, channels ) );
			}

			foreach ( Match m in AttributeDeclaration().Matches( text ) )
			{
				var name = m.Groups["name"].Value;
				if ( attributes.Exists( a => a.Item1 == name ) ) continue;
				var value = m.Groups["value"].Value.Trim();
				float[] parsed = value.ToLowerInvariant() switch
				{
					"true" => [1],
					"false" => [0],
					_ => float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) ? [number] : null,
				};
				if ( parsed is not null ) attributes.Add( (name, m.Groups["kind"].Value, parsed) );
			}
		}

		return new MaterialInputs { Inputs = inputs, Textures = [.. textures.Where( t => used.Contains( t.Name ) )], Attributes = attributes };
	}

	/// <summary>The lines as one text without comments, so declarations spanning lines read as one.</summary>
	static string Code( IReadOnlyList<string> lines )
	{
		var text = string.Join( "\n", lines );
		text = BlockComment().Replace( text, " " );
		return LineComment().Replace( text, "" );
	}

	/// <summary>The comma-separated arguments of a call, split only at its top level.</summary>
	static List<string> Arguments( string text )
	{
		var result = new List<string>();
		int depth = 0, start = 0;
		var quoted = false;
		for ( int i = 0; i < text.Length; i++ )
		{
			var c = text[i];
			if ( c == '"' ) quoted = !quoted;
			if ( quoted ) continue;
			if ( c == '(' ) depth++;
			else if ( c == ')' ) depth--;
			else if ( c == ',' && depth == 0 )
			{
				result.Add( text[start..i].Trim() );
				start = i + 1;
			}
		}

		if ( text[start..].Trim() is { Length: > 0 } last ) result.Add( last );
		return result;
	}

	static string Unquote( string text ) => text.Trim().Trim( '"' );

	/// <summary>An input's default value and file: <c>Default3( 1, 1, 1 )</c>, <c>Default4AndFile( 0, 0, 0, 1, "x.tga" )</c>, <c>DefaultFile( "x.tga" )</c>.</summary>
	static (float[] Value, string File) Defaults( string text )
	{
		var value = new float[4];
		string file = null;
		if ( DefaultCall().Match( text ) is not { Success: true } m ) return (value, file);

		var args = Arguments( m.Groups["args"].Value );
		var count = m.Groups["count"].Success ? int.Parse( m.Groups["count"].Value, CultureInfo.InvariantCulture ) : 1;
		if ( m.Groups["file"].Success && m.Groups["count"].Success && args.Count > count ) file = Unquote( args[count] );
		if ( m.Groups["onlyFile"].Success ) return (value, args.Count > 0 ? Unquote( args[0] ) : null);

		for ( int i = 0; i < Math.Min( count, args.Count ) && i < 4; i++ )
			value[i] = float.TryParse( args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var f ) ? f : 0;
		return (value, file);
	}

	[GeneratedRegex( @"/\*.*?\*/", RegexOptions.Singleline )]
	private static partial Regex BlockComment();

	[GeneratedRegex( @"//[^\n]*" )]
	private static partial Regex LineComment();

	[GeneratedRegex( @"\bCreateInputTexture(?<type>2D|Cube|3D)\s*\((?<args>(?:[^()]|\((?:[^()]|\([^()]*\))*\))*)\)" )]
	private static partial Regex InputDeclaration();

	[GeneratedRegex( @"(?:\bCreateTexture(?<create>2D|Cube|3D|2DArray|2DWithoutSampler)\s*\(\s*(?<name>\w+)\s*\)|\b(?<type>Texture2D|TextureCube|Texture3D|Texture2DArray)\b(?:\s*<\s*\w+\s*>)?\s+(?<name>\w+))\s*<(?<annotations>[^<>]*)>\s*;" )]
	private static partial Regex TextureDeclaration();

	[GeneratedRegex( @"\bChannel\s*\(\s*(?<channels>\w+)\s*,\s*(?<algorithm>\w+)\s*\((?<inputs>[^()]*)\)\s*,\s*(?<space>\w+)\s*\)" )]
	private static partial Regex ChannelAnnotation();

	[GeneratedRegex( @"\b(?:Attribute|Source)\s*\(" )]
	private static partial Regex BoundByEngine();

	[GeneratedRegex( @"\bOutputFormat\s*\(\s*(?<format>\w+)\s*\)" )]
	private static partial Regex OutputFormat();

	[GeneratedRegex( @"\bSrgbRead\s*\(\s*(?<value>\w+)\s*\)" )]
	private static partial Regex SrgbRead();

	[GeneratedRegex( @"\b(?<kind>Bool|Float|Float3|Texture)Attribute\s*\(\s*(?<name>\w+)\s*,\s*(?<value>[^)]*)\)" )]
	private static partial Regex AttributeDeclaration();

	[GeneratedRegex( @"\bDefault(?:(?<count>[1-4])(?<file>AndFile)?|(?<onlyFile>File))?\s*\((?<args>[^()]*)\)" )]
	private static partial Regex DefaultCall();
}
