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

	/// <summary>
	/// A material parameter the editor shows (<c>float g_flTint &lt; Default( 1.0 ); Range( 0, 1 ); UiGroup( "Color" ); &gt;</c>):
	/// its name, HLSL type and annotations.
	/// </summary>
	public sealed record Parameter( string Name, string Type, string Annotations );

	/// <summary>
	/// One input of the vertex shader the material draws with (<c>float3 vPositionOs : POSITION &lt; Semantic( PosXyz ); &gt;</c>):
	/// its name, vertex semantic and HLSL semantic. What the model compiler lays a mesh's vertices out by (the material's
	/// <c>VSInputSignature</c>).
	/// </summary>
	public sealed record VertexInput( string Name, string Semantic, string D3DSemanticName, int D3DSemanticIndex );

	/// <summary>
	/// The vertex shader's inputs, as vfx records a <c>.shader</c>'s for a material (<c>m_vsInputSignatureArray</c>): the
	/// members of the struct its <c>MainVs</c> takes (a surface's: the <c>VS_INPUT</c> its MATERIAL_INPUTS block declares,
	/// the inputs of the <c>.shader</c> it replaced), with every dynamic combo's (off and on), ordered by HLSL semantic and
	/// index. Empty for a module with no vertex stage.
	/// </summary>
	public IReadOnlyList<VertexInput> VertexInputs { get; init; } = [];

	/// <summary>The input textures, by name.</summary>
	public IReadOnlyDictionary<string, Input> Inputs { get; init; }

	/// <summary>The textures made from them, in the order declared.</summary>
	public IReadOnlyList<Texture> Textures { get; init; }

	/// <summary>Every made texture declared, read or not: what the representative texture is looked for in.</summary>
	public IReadOnlyList<Texture> Declared { get; init; } = [];

	/// <summary>
	/// The material's parameters for these feature values: declared where the preprocessor reaches, not bound by the engine
	/// (<c>Attribute</c>, <c>Source</c>, <c>Expression</c>), and with an <c>ActiveIf</c> that holds when they have one.
	/// </summary>
	public IReadOnlyList<Parameter> Parameters { get; init; } = [];

	/// <summary>
	/// The made textures the material editor shows the inputs of: the ones the code reads (a surface's: its fields take),
	/// and ones with an <c>ActiveIf</c> that holds, which the editor shows whether or not the code reads them yet.
	/// </summary>
	public IReadOnlyList<Texture> ShownTextures { get; init; } = [];

	/// <summary>The attributes whose value is an expression of the features (<c>F_RENDER_BACKFACES ? true : false</c>), evaluated here.</summary>
	public IReadOnlySet<string> ExpressionAttributes { get; init; } = new HashSet<string>();

	/// <summary>
	/// The block's FEATURES lines (<c>Feature</c>, <c>FeatureRule</c>, <c>FeatureUpgrade</c>), as the material editor reads a
	/// <c>.shader</c>'s FEATURES block; empty when it has none.
	/// </summary>
	public string FeatureLines { get; init; } = "";

	/// <summary>The shader's attributes with a literal value (<c>BoolAttribute( sky, true )</c> as 1); ones that read a variable are left out.</summary>
	public IReadOnlyList<(string Name, string Kind, float[] Value)> Attributes { get; init; }

	/// <summary>
	/// The shader's version, which its materials record (<c>m_shaderVersion</c>) for code that reads one layout or another
	/// by it (<c>Source( "ShaderVersion" )</c>): a module's <c>// HEADER: Version = 1;</c>, as a <c>.shader</c>'s HEADER
	/// <c>Version</c>. 0 when it declares none.
	/// </summary>
	public int Version { get; init; }

	/// <summary>
	/// The made texture whose first input's size the material records (<c>TextureAttribute( RepresentativeTexture, g_tColor )</c>):
	/// what tools map a face's uvs by. Null for none.
	/// </summary>
	public string RepresentativeTexture { get; init; }

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
		var parameters = new List<Parameter>();
		var expressionAttributes = new HashSet<string>( StringComparer.Ordinal );
		var conditioned = new HashSet<string>( StringComparer.Ordinal );
		var hidden = new HashSet<string>( StringComparer.Ordinal );
		var featureLines = new List<string>();
		var used = new HashSet<string>( StringComparer.Ordinal );
		IReadOnlyList<VertexInput> vertexInputs = [];
		string representative = null;

		// The block's feature rules first, as vfx applies a shader's before it reads anything for a material's values
		features = FeatureRules.Read( module ).Apply( features );

		var surface = IsSurface( module );
		foreach ( var stage in Stages( module, surface ) )
		{
			var values = StageValues( module, stage, features );
			var text = StageText( module, stage, values );

			// A surface declares the vertex inputs in its block, read with the pixel stage's lines; a module in its vertex stage
			if ( stage == (surface ? StageKind.Pixel : StageKind.Vertex) ) vertexInputs = ReadVertexInputs( module, stage, values, text );

			var conditions = Conditions( module, values );
			bool Active( string annotations ) => ActiveIf().Match( annotations ) is not { Success: true } condition || PassPreprocessor.Holds( condition.Groups["expression"].Value, conditions ) != false;

			foreach ( Match m in FeatureLine().Matches( text ) )
				if ( !featureLines.Contains( m.Value.Trim() ) ) featureLines.Add( m.Value.Trim() );

			foreach ( Match m in ParameterDeclaration().Matches( text ) )
			{
				var annotations = m.Groups["annotations"].Value;
				if ( BoundByEngine().IsMatch( annotations ) || ExpressionAnnotation().IsMatch( annotations ) || !Active( annotations ) ) continue;
				// The module's own: an include's parameters are its code's, which the module doesn't run
				var name = m.Groups["name"].Value;
				if ( parameters.Exists( p => p.Name == name ) || !Regex.IsMatch( module.Text, $@"\b{Regex.Escape( name )}\s*<" ) ) continue;
				parameters.Add( new Parameter( m.Groups["name"].Value, m.Groups["type"].Value, annotations ) );
			}

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
				// A surface's are the ones its fields take: g_tColor for Color, or one a field names as its [Source]
				if ( surface ? SurfaceTakes( module.Text, name ) : Regex.Matches( text, $@"\b{Regex.Escape( name )}\b" ).Count > 1 ) used.Add( name );

				// An ActiveIf is the editor's alone (the material compiler makes the texture whatever it says): where it holds the
				// editor shows the texture's inputs even if the code doesn't read it yet, where it doesn't it hides them
				if ( ActiveIf().IsMatch( annotations ) ) (Active( annotations ) ? conditioned : hidden).Add( name );
				if ( !seenTextures.Add( name ) ) continue;

				var type = (m.Groups["type"].Value + m.Groups["create"].Value) switch { var t when t.Contains( "Cube", StringComparison.Ordinal ) => "CUBE", var t when t.Contains( "3D", StringComparison.Ordinal ) => "3D", _ => "2D" };
				var srgbRead = SrgbRead().Match( annotations ) is { Success: true } s && s.Groups["value"].Value.Equals( "true", StringComparison.OrdinalIgnoreCase );
				textures.Add( new Texture( name, type, format.Groups["format"].Value, srgbRead, channels ) );
			}

			ReadAttributes( text, values, conditions, attributes, expressionAttributes, ref representative );
		}

		// A representative texture a macro names (g_tColor for vr_shared_standard_ps_code.fxc's g_tColor2): the numbered one declared
		if ( representative is not null && !textures.Exists( t => t.Name == representative ) && textures.Find( t => t.Name.TrimEnd( '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' ) == representative ) is { } numbered )
			representative = numbered.Name;

		return new MaterialInputs { Version = HeaderVersion().Match( module.Text ) is { Success: true } version ? int.Parse( version.Groups[1].Value, CultureInfo.InvariantCulture ) : 0, Inputs = inputs, Textures = [.. textures.Where( t => used.Contains( t.Name ) )], Declared = textures, Attributes = attributes, RepresentativeTexture = representative, Parameters = parameters, VertexInputs = vertexInputs, ExpressionAttributes = expressionAttributes, ShownTextures = [.. textures.Where( t => (used.Contains( t.Name ) || conditioned.Contains( t.Name )) && !hidden.Contains( t.Name ) )], FeatureLines = EditorLines( featureLines ) };
	}

	/// <summary>
	/// Only the <see cref="Attributes"/> of <see cref="Read"/>, read as it reads them: what a material's renderer asks of a
	/// surface (<c>BoolAttribute( overlay, true )</c>) for each set of feature values a load meets, without the textures,
	/// parameters and vertex inputs it doesn't use (those read every stage's lines again with every dynamic combo on).
	/// </summary>
	public static IReadOnlyList<(string Name, string Kind, float[] Value)> ReadAttributes( PassModule module, IReadOnlyDictionary<string, int> features )
	{
		var attributes = new List<(string, string, float[])>();
		var expressionAttributes = new HashSet<string>( StringComparer.Ordinal );
		string representative = null;

		features = FeatureRules.Read( module ).Apply( features );
		foreach ( var stage in Stages( module, IsSurface( module ) ) )
		{
			var values = StageValues( module, stage, features );
			var text = StageText( module, stage, values );
			ReadAttributes( text, values, Conditions( module, values ), attributes, expressionAttributes, ref representative );
		}

		return attributes;
	}

	/// <summary>
	/// The stages a material's inputs are read from. A surface has no entry points or features of its own: its inputs are in
	/// its MATERIAL_INPUTS block (the textures its fields take, by name), read with the material's features as the pixel
	/// stage would see them.
	/// </summary>
	static IEnumerable<StageKind> Stages( PassModule module, bool surface )
	{
		if ( surface )
		{
			yield return StageKind.Pixel;
			yield break;
		}

		if ( module.Has( StageKind.Vertex ) ) yield return StageKind.Vertex;
		if ( module.Has( StageKind.Pixel ) ) yield return StageKind.Pixel;
		if ( module.Has( StageKind.Compute ) ) yield return StageKind.Compute;
	}

	/// <summary>A stage's values for these feature values: the module's features in their ranges, and its combos following them.</summary>
	static Dictionary<string, int> StageValues( PassModule module, StageKind stage, IReadOnlyDictionary<string, int> features )
	{
		var values = new Dictionary<string, int>( StringComparer.Ordinal ) { ["MATERIAL_INPUTS"] = 1 };
		foreach ( var (name, value) in features ) values[name] = value;
		foreach ( var feature in module.Features ) values[feature.Name] = Math.Clamp( features.TryGetValue( feature.Name, out var v ) ? v : feature.Min, feature.Min, feature.Max );
		foreach ( var combo in module.Combos( stage ).Where( c => c.Static ) )
			values[combo.Name] = combo.Feature is not null && values.TryGetValue( combo.Feature, out var f ) ? Math.Clamp( f, combo.Min, combo.Max ) : combo.Min;
		foreach ( var combo in module.Combos( stage ).Where( c => !c.Static ) ) values.TryAdd( combo.Name, combo.Min );
		return values;
	}

	/// <summary>
	/// A stage's code for <paramref name="values"/>. Static combos the lines declare (an include's <c>StaticCombo( S_X, F_X )</c>)
	/// follow their features: added to the values and read again until no new one shows, as vfx's preprocessor sees them from the start.
	/// </summary>
	static string StageText( PassModule module, StageKind stage, Dictionary<string, int> values )
	{
		var text = Code( module.CompiledLines( stage, values ) );
		for ( int pass = 0; pass < 4; pass++ )
		{
			var added = false;
			foreach ( Match combo in StaticComboLine().Matches( text ) )
			{
				if ( values.ContainsKey( combo.Groups["name"].Value ) ) continue;
				values[combo.Groups["name"].Value] = values.GetValueOrDefault( combo.Groups["feature"].Value );
				added = true;
			}

			if ( !added ) break;
			text = Code( module.CompiledLines( stage, values ) );
		}

		return text;
	}

	/// <summary>What an ActiveIf reads: these values, and the block's own defines (<c>#define S_X F_X</c>).</summary>
	static Dictionary<string, string> Conditions( PassModule module, Dictionary<string, int> values )
	{
		var conditions = new Dictionary<string, string>( StringComparer.Ordinal );
		foreach ( Match define in DefineLine().Matches( module.Text ) ) conditions[define.Groups["name"].Value] = define.Groups["value"].Value.Trim();
		foreach ( var (name, value) in values ) conditions[name] = value.ToString( CultureInfo.InvariantCulture );
		return conditions;
	}

	/// <summary>
	/// The attributes a stage's code declares that aren't in <paramref name="attributes"/> yet, with a literal value or one
	/// these values evaluate; the representative texture the first names.
	/// </summary>
	static void ReadAttributes( string text, Dictionary<string, int> values, Dictionary<string, string> conditions, List<(string, string, float[])> attributes, HashSet<string> expressionAttributes, ref string representative )
	{
		foreach ( Match m in AttributeDeclaration().Matches( text ) )
		{
			var name = m.Groups["name"].Value;
			var value = m.Groups["value"].Value.Trim();
			if ( m.Groups["kind"].Value == "Texture" && name == "RepresentativeTexture" ) representative ??= value;
			if ( attributes.Exists( a => a.Item1 == name ) ) continue;
			// A literal, or an expression of the features (F_RENDER_BACKFACES ? true : false); one that reads a variable isn't kept
			// A feature or combo the material doesn't set is 0, as vfx reads it, not a name
			var known = new Dictionary<string, int>( values, StringComparer.Ordinal );
			foreach ( Match feature in FeatureName().Matches( value ) ) known.TryAdd( feature.Value, 0 );
			var evaluated = PassPreprocessor.Evaluate( value, known ) ?? value;
			float[] parsed = evaluated.ToLowerInvariant() switch
			{
				"true" => [1],
				"false" => [0],
				_ => float.TryParse( evaluated, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) ? [number] : null,
			};

			// A bool of the block's own defines (S_GLASS_QUALITY != GLASS_CHEAP), when it names nothing else (an include's
			// NEEDS_LIGHT_PROBE is defined where this can't see)
			var named = Identifier().Matches( value ).Select( i => i.Value ).Where( i => i is not "true" and not "false" );
			if ( m.Groups["kind"].Value == "Bool" && named.All( i => conditions.ContainsKey( i ) || FeatureName().IsMatch( i ) ) && PassPreprocessor.Holds( value, conditions ) is bool holds )
				parsed = [holds ? 1 : 0];
			if ( parsed is not null ) attributes.Add( (name, m.Groups["kind"].Value, parsed) );
			// One of the material's features stays an expression in vfx; one of static combos is evaluated per combo
			if ( parsed is not null && MaterialFeatureName().IsMatch( value ) ) expressionAttributes.Add( name );
		}
	}

	/// <summary>
	/// The vertex inputs in a stage's lines: read with its dynamic combos as they are (off) and again with every one on
	/// (each <c>D_</c> name the module or an include mentions, at its highest value), as vfx's signature holds the inputs of
	/// all of a static combo's dynamic combos (the lightmap uv, compute skinning's streams).
	/// </summary>
	static List<VertexInput> ReadVertexInputs( PassModule module, StageKind stage, Dictionary<string, int> values, string text )
	{
		var on = new Dictionary<string, int>( values, StringComparer.Ordinal );
		var dynamic = module.Combos( stage ).Where( c => !c.Static ).ToDictionary( c => c.Name, c => c.Max, StringComparer.Ordinal );
		foreach ( var name in module.BindingNames().Where( n => n.StartsWith( "D_", StringComparison.Ordinal ) ) )
			on[name] = dynamic.TryGetValue( name, out var max ) ? max : 1;

		var inputs = new List<VertexInput>();
		foreach ( var lines in new[] { text, Code( module.CompiledLines( stage, on ) ) } )
		{
			foreach ( var input in VertexStruct( lines ) )
				if ( !inputs.Exists( i => i.Name == input.Name ) ) inputs.Add( input );
		}

		return [.. inputs.OrderBy( i => i.D3DSemanticName, StringComparer.Ordinal ).ThenBy( i => i.D3DSemanticIndex )];
	}

	/// <summary>
	/// The members of the struct the vertex entry takes (<c>MainVs( VS_INPUT i )</c>), or of a <c>VS_INPUT</c> or
	/// <c>VertexInput</c> where there's no entry (a surface's block); members with an HLSL and a vertex semantic only.
	/// </summary>
	static IEnumerable<VertexInput> VertexStruct( string text )
	{
		var names = VertexEntry().Matches( text ).Select( m => m.Groups["type"].Value ).Append( "VS_INPUT" ).Append( "VertexInput" );
		foreach ( var name in names )
		{
			if ( Regex.Match( text, $@"\bstruct\s+{Regex.Escape( name )}\s*\{{(?<body>[^{{}}]*)\}}" ) is not { Success: true } body ) continue;
			foreach ( Match m in VertexMember().Matches( body.Groups["body"].Value ) )
			{
				// A member with no vertex semantic names no mesh stream (a module's own extra input): no model is laid out by it
				if ( SemanticAnnotation().Match( m.Groups["annotations"].Value ) is not { Success: true } semantic ) continue;
				yield return new VertexInput( m.Groups["name"].Value, semantic.Groups["semantic"].Value, m.Groups["d3d"].Value,
					m.Groups["index"].Length > 0 ? int.Parse( m.Groups["index"].Value, CultureInfo.InvariantCulture ) : 0 );
			}

			yield break;
		}
	}

	/// <summary>
	/// The lines as one text without comments, so declarations spanning lines read as one: joined by newlines, each
	/// <c>/* */</c> made a space, then each <c>//</c> to the end of its line taken out, made in one string. A material's
	/// inputs are read for every set of feature values a load meets, and these texts are a module's lines with every include.
	/// </summary>
	internal static string Code( IReadOnlyList<string> lines )
	{
		var length = Math.Max( 0, lines.Count - 1 );
		foreach ( var line in lines ) length += line.Length;
		if ( length == 0 ) return "";

		var rented = System.Buffers.ArrayPool<char>.Shared.Rent( length );
		try
		{
			var text = rented.AsSpan( 0, length );
			var at = 0;
			for ( int i = 0; i < lines.Count; i++ )
			{
				if ( i > 0 ) text[at++] = '\n';
				lines[i].CopyTo( text[at..] );
				at += lines[i].Length;
			}

			// In place: neither pass writes past where it reads
			length = WithoutBlockComments( text );
			length = WithoutLineComments( text[..length] );
			return new string( text[..length] );
		}
		finally
		{
			System.Buffers.ArrayPool<char>.Shared.Return( rented );
		}
	}

	/// <summary>Each <c>/*</c> up to the first <c>*/</c> after it made a space, as <c>/\*.*?\*/</c> replaces them; an unclosed one stays.</summary>
	static int WithoutBlockComments( Span<char> text )
	{
		int read = 0, write = 0;
		while ( read < text.Length )
		{
			if ( text[read] == '/' && read + 1 < text.Length && text[read + 1] == '*' )
			{
				var close = text[(read + 2)..].IndexOf( "*/" );
				if ( close < 0 )
				{
					// Nothing closes it, nor any opened after it: the rest stays as it is
					text[read..].CopyTo( text[write..] );
					return write + text.Length - read;
				}

				text[write++] = ' ';
				read += 2 + close + 2;
				continue;
			}

			text[write++] = text[read++];
		}

		return write;
	}

	/// <summary>Each <c>//</c> up to the end of its line taken out, as <c>//[^\n]*</c> replaces them.</summary>
	static int WithoutLineComments( Span<char> text )
	{
		int read = 0, write = 0;
		while ( read < text.Length )
		{
			if ( text[read] == '/' && read + 1 < text.Length && text[read + 1] == '/' )
			{
				var end = text[read..].IndexOf( '\n' );
				read = end < 0 ? text.Length : read + end;
				continue;
			}

			text[write++] = text[read++];
		}

		return write;
	}

	/// <summary>Whether a module is a surface (a <c>[Material]</c> struct), whose block is read with its pixel stage's lines alone.</summary>
	internal static bool IsSurface( PassModule module ) => SurfaceStruct().IsMatch( module.Text );

	/// <summary>Whether a module's text is a surface's (a <c>[Material]</c> struct).</summary>
	public static bool SurfaceText( string text ) => SurfaceStruct().IsMatch( text );

	/// <summary>
	/// The FEATURES lines the material editor reads: every one but the rules naming a feature the lines don't declare (the
	/// features a surface doesn't do, whose rules still apply to materials' values: <see cref="FeatureRules"/>).
	/// </summary>
	static string EditorLines( List<string> lines )
	{
		var declared = new HashSet<string>( lines.Select( l => DeclaredFeature().Match( l ) ).Where( m => m.Success ).Select( m => m.Groups[1].Value ), StringComparer.Ordinal );
		return string.Join( "\n", lines.Where( l => !l.StartsWith( "FeatureRule", StringComparison.Ordinal ) || FeatureRules.Parse( l ).Features.All( declared.Contains ) ) );
	}

	[GeneratedRegex( @"^Feature\s*\(\s*(\w+)" )]
	private static partial Regex DeclaredFeature();

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

	[GeneratedRegex( @"\bMainVs\s*\(\s*(?:const\s+)?(?<type>\w+)\s+\w+" )]
	private static partial Regex VertexEntry();

	[GeneratedRegex( @"\b(?<type>\w+)\s+(?<name>\w+)\s*:\s*(?<d3d>[A-Za-z_]\w*?)(?<index>\d*)\s*(?:<(?<annotations>[^<>]*)>)?\s*;" )]
	private static partial Regex VertexMember();

	[GeneratedRegex( @"\bSemantic\s*\(\s*(?<semantic>\w+)\s*\)" )]
	private static partial Regex SemanticAnnotation();

	[GeneratedRegex( @"^[ \t]*//[ \t]*HEADER:[ \t]*Version[ \t]*=[ \t]*(\d+)[ \t]*;", RegexOptions.Multiline )]
	private static partial Regex HeaderVersion();

	[GeneratedRegex( @"\bCreateInputTexture(?<type>2D|Cube|3D)\s*\((?<args>(?:[^()]|\((?:[^()]|\([^()]*\))*\))*)\)" )]
	private static partial Regex InputDeclaration();

	[GeneratedRegex( @"(?:\bCreateTexture(?<create>2D|Cube|3D|2DArray|2DWithoutSampler)\s*\(\s*(?<name>\w+)\s*\)|\b(?<type>Texture2D|TextureCube|Texture3D|Texture2DArray)\b(?:\s*<\s*\w+\s*>)?\s+(?<name>\w+))\s*<(?<annotations>(?:[^<>]|[<>]=)*)>\s*;" )]
	private static partial Regex TextureDeclaration();

	[GeneratedRegex( @"\bChannel\s*\(\s*(?<channels>\w+)\s*,\s*(?<algorithm>\w+)\s*\((?<inputs>[^()]*)\)\s*,\s*(?<space>\w+)\s*\)" )]
	private static partial Regex ChannelAnnotation();

	/// <summary>
	/// Whether a surface has a field a made texture fills: named as it without vfx's <c>g_t</c>, or naming it as its source.
	/// A numbered variant is the same texture made another way (vr_shared_standard_ps_code.fxc's <c>g_tColor2</c>, which
	/// <c>g_tColor</c> names where it's the one declared).
	/// </summary>
	static bool SurfaceTakes( string surface, string texture )
	{
		var stem = texture.StartsWith( "g_t", StringComparison.Ordinal ) ? texture[3..] : texture;
		var unnumbered = stem.TrimEnd( '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' );
		const string types = @"\b(?:Texture2D|Rma)\s+";
		return Regex.IsMatch( surface, $@"{types}{Regex.Escape( stem )}\s*;" ) || Regex.IsMatch( surface, $@"\bSource\(\s*""{Regex.Escape( texture )}""\s*\)" )
			|| (unnumbered.Length > 0 && unnumbered != stem && Regex.IsMatch( surface, $@"{types}{Regex.Escape( unnumbered )}\s*;" ));
	}

	[GeneratedRegex( @"\bStaticCombo\s*\(\s*(?<name>S_\w+)\s*,\s*(?<feature>F_\w+)" )]
	private static partial Regex StaticComboLine();

	[GeneratedRegex( @"\b[FSD]_\w+" )]
	private static partial Regex FeatureName();

	[GeneratedRegex( @"\bF_\w+" )]
	private static partial Regex MaterialFeatureName();

	[GeneratedRegex( @"\[\s*Material\b" )]
	private static partial Regex SurfaceStruct();

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

	[GeneratedRegex( @"\bActiveIf\s*\((?<expression>(?:[^()]|\((?:[^()]|\([^()]*\))*\))*)\)" )]
	private static partial Regex ActiveIf();

	[GeneratedRegex( @"\bExpression\s*\(" )]
	private static partial Regex ExpressionAnnotation();

	[GeneratedRegex( @"\b[A-Za-z_]\w*\b" )]
	private static partial Regex Identifier();

	[GeneratedRegex( @"^[ \t]*#[ \t]*define[ \t]+(?<name>\w+)[ \t]+(?<value>[^\r\n/]+)", RegexOptions.Multiline )]
	private static partial Regex DefineLine();

	[GeneratedRegex( @"\b(?:FeatureRule|FeatureUpgrade|Feature)\s*\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\)\s*;" )]
	private static partial Regex FeatureLine();

	[GeneratedRegex( @"(?<![\w.])(?<type>float[234]?|int[234]?|uint|bool)\s+(?<name>\w+)\s*<(?<annotations>(?:[^<>]|[<>]=)*)>\s*;" )]
	private static partial Regex ParameterDeclaration();
}
