using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// Turns a <c>.shader</c> written the <c>common/pixel.hlsl</c> way (a <c>Material</c> from <c>Material::From</c> or
/// <c>Init</c>, edited, then <c>ShadingModelStandard::Shade</c>) into a surface, mechanically: its pixel globals become
/// <c>[Material]</c> fields, its features fields its branches read, its <c>MainPs</c> body <c>Evaluate</c>, through a
/// fixed vocabulary (docs/managed/shader-format-replacement.md, "Converting today's shaders"). What it can't take is
/// listed in <see cref="Result.Notes"/>; the <c>.shader</c> stays as it is.
/// </summary>
public static partial class ShaderConverter
{
	/// <summary>A conversion: the surface's source (null when the shader has no <c>MainPs</c>) and what didn't convert.</summary>
	public sealed record Result( string Source, string Type, IReadOnlyList<string> Notes );

	[GeneratedRegex( @"^\s*Feature\(\s*(F_\w+)\s*,\s*(\d+)\s*\.\.\s*(\d+)", RegexOptions.Multiline )]
	private static partial Regex FeatureDecl();

	[GeneratedRegex( @"^\s*StaticCombo\(\s*(S_\w+)\s*,\s*(F_\w+)", RegexOptions.Multiline )]
	private static partial Regex StaticComboDecl();

	[GeneratedRegex( @"^[ \t]*Texture2D\s+g_t(\w+)\s*<([^>]*)>\s*;[^\n]*\n?", RegexOptions.Multiline )]
	private static partial Regex TextureDecl();

	[GeneratedRegex( @"^[ \t]*(float[234]?|int|uint|bool)\s+(\w+)\s*<([^>]*)>\s*;[^\n]*\n?", RegexOptions.Multiline )]
	private static partial Regex GlobalDecl();

	[GeneratedRegex( @"^[ \t]*CreateTexture2D\(\s*(\w+)\s*\)\s*<([^>]*)>\s*;[^\n]*\n?", RegexOptions.Multiline )]
	private static partial Regex CreateTextureDecl();

	[GeneratedRegex( @"^[ \t]*SamplerState\s+\w+\s*<[^>]*>\s*;[^\n]*\n?", RegexOptions.Multiline )]
	private static partial Regex SamplerDecl();

	[GeneratedRegex( @"(\w+)\s*\(\s*([^()]*)\)" )]
	private static partial Regex Annotation();

	/// <summary>
	/// Convert a <c>.shader</c>'s text; <paramref name="name"/> is its file name without extension. <paramref name="include"/>
	/// reads an include by its path (<c>common/trunk_bending.hlsl</c>), for helpers the vertex shader includes; null when there's none.
	/// </summary>
	public static Result Convert( string shader, string name, Func<string, string> include = null )
	{
		var notes = new List<string>();
		var type = Pascal( name );
		if ( type is "Material" or "Surface" ) type += "Shader";
		var ps = Block( shader, "PS" );
		if ( ps is null ) return new Result( null, type, ["no PS block"] );

		// A surface is a material: a MainPs that builds none (post-processing, UI, tools' colour passes) isn't one
		var main = Functions( ps ).FirstOrDefault( f => Regex.IsMatch( f.Signature, @"\bMainPs\s*\(" ) );
		if ( main.Body is null ) return new Result( null, type, ["no MainPs"] );
		if ( !Regex.IsMatch( main.Body, @"\bMaterial::(From|Init)\b|\bShadingModelStandard::Shade\b" ) )
		{
			return new Result( null, type, [ps.Contains( "FinalCombiner", StringComparison.Ordinal )
				? "lit through the legacy FinalCombiner path (complex's), not a Material and Shade"
				: "not a material shader: MainPs doesn't build a Material and Shade it"] );
		}

		var vs = Block( shader, "VS" ) ?? "";

		var fields = new StringBuilder();
		var renames = new Dictionary<string, string>( StringComparer.Ordinal );

		// Declarations can sit in COMMON as well as PS
		var declarations = (Block( shader, "COMMON" ) ?? "") + "\n" + vs + "\n" + ps;

		// Material.CommonInputs.hlsl's textures and tint
		if ( ps.Contains( "Material.CommonInputs.hlsl", StringComparison.Ordinal ) )
		{
			fields.Append( "\t[Group( \"Material\" ), Srgb, Opacity] Texture2D Color;\n\t[Group( \"Material\" )] Texture2D Normal;\n\t[Group( \"Material\" )] Texture2D Rma;\n\t[Group( \"Material\" ), Default3( 1, 1, 1 )] float3 TintColor;\n" );
			renames["g_tColor"] = "Color";
			renames["g_tNormal"] = "Normal";
			renames["g_tRma"] = "Rma";
			renames["g_flTintColor"] = "TintColor";
		}

		// Features read through static combos: fields, their .vmat values by [Source]
		var features = FeatureDecl().Matches( Block( shader, "FEATURES" ) ?? "" ).ToDictionary( m => m.Groups[1].Value, m => (int.Parse( m.Groups[2].Value, CultureInfo.InvariantCulture ), int.Parse( m.Groups[3].Value, CultureInfo.InvariantCulture )) );
		foreach ( Match combo in StaticComboDecl().Matches( vs + "\n" + ps ).DistinctBy( m => m.Groups[1].Value ) )
		{
			var (comboName, feature) = (combo.Groups[1].Value, combo.Groups[2].Value);
			if ( feature == "F_ALPHA_TEST" )
			{
				renames[comboName] = "true";
				notes.Add( "F_ALPHA_TEST is the material's AlphaTest" );
				continue;
			}

			var field = Pascal( feature[2..] );
			var (min, max) = features.TryGetValue( feature, out var range ) ? range : (0, 1);
			fields.Append( CultureInfo.InvariantCulture, $"\t[Group( \"Features\" ), Source( \"{feature}\" ), Range( {min}, {max} )] int {field};\n" );
			renames[comboName] = field;
		}

		// What the engine binds by attribute (reflection and refraction textures, an object's textures, their flags): module
		// globals as written, which the passes bind by their Attribute as they bind the core includes' (designs: engine inputs)
		var engine = new StringBuilder();

		// Textures: the material's are fields
		foreach ( Match texture in TextureDecl().Matches( declarations ) )
		{
			var field = texture.Groups[1].Value;
			if ( renames.ContainsKey( "g_t" + field ) ) continue;
			if ( Regex.IsMatch( texture.Groups[2].Value, @"\bAttribute\s*\(" ) )
			{
				engine.Append( texture.Value.Trim() ).Append( '\n' );
				renames["g_t" + field] = "g_t" + field;
				continue;
			}

			var srgb = Regex.IsMatch( texture.Groups[2].Value, @"SrgbRead\(\s*true\s*\)", RegexOptions.IgnoreCase );
			fields.Append( $"\t{(srgb ? "[Srgb] " : "")}Texture2D {field};\n" );
			renames["g_t" + field] = field;
		}

		foreach ( Match texture in CreateTextureDecl().Matches( declarations ) )
		{
			var (textureName, annotations) = (texture.Groups[1].Value, texture.Groups[2].Value.Trim());
			if ( !Regex.IsMatch( annotations, @"\bAttribute\s*\(" ) )
			{
				notes.Add( $"material texture declared with CreateTexture2D: {textureName}" );
				continue;
			}

			engine.Append( CultureInfo.InvariantCulture, $"Texture2D {textureName} < {annotations} >;\nSamplerState {textureName}_sampler < {annotations} >;\n" );
			renames[textureName] = textureName;
			renames[textureName + "_sampler"] = textureName + "_sampler";
		}

		// Pixel globals: the material sets those without Attribute or Source
		foreach ( Match global in GlobalDecl().Matches( declarations ) )
		{
			var annotations = global.Groups[3].Value;
			var original = global.Groups[2].Value;
			if ( Regex.IsMatch( annotations, @"\bAttribute\s*\(" ) && !Regex.IsMatch( annotations, @"\b(Source|Expression)\s*\(" ) )
			{
				engine.Append( global.Value.Trim() ).Append( '\n' );
				renames[original] = original;
				continue;
			}

			if ( Regex.IsMatch( annotations, @"\b(Attribute|Source|Expression)\s*\(" ) )
			{
				notes.Add( $"reads {original} from the engine by Source or Expression, not the material" );
				continue;
			}

			var field = Regex.Replace( original, "^g_(?:fl|v|n|b|f|i)?", "" );
			if ( renames.ContainsValue( field ) ) field += "Value";
			var attributes = new List<string>();
			foreach ( Match a in Annotation().Matches( annotations ) )
			{
				var (key, value) = (a.Groups[1].Value, a.Groups[2].Value.Trim());
				if ( key is "Default" or "Default2" or "Default3" or "Default4" ) attributes.Add( $"{key}( {value} )" );
				else if ( key == "Range" ) attributes.Add( $"Range( {value} )" );
				else if ( key == "UiGroup" ) attributes.Add( $"Group( \"{value.Trim( '"' ).Split( ',' )[0]}\" )" );
			}

			fields.Append( $"\t{(attributes.Count > 0 ? $"[{string.Join( ", ", attributes )}] " : "")}{global.Groups[1].Value} {field};\n" );
			renames[original] = field;
		}

		var samplers = engine + string.Concat( SamplerDecl().Matches( declarations ).Select( m => m.Value.Trim() + "\n" ) );

		// Constants the code reads, as module constants
		foreach ( Match constant in Regex.Matches( declarations, @"^[ \t]*static\s+const\s+[^;(]+;", RegexOptions.Multiline ) ) samplers += constant.Value.Trim() + "\n";
		var commonInputs = renames.ContainsKey( "g_tColor" ) && renames["g_tColor"] == "Color" && renames.ContainsKey( "g_tRma" );

		// Functions: MainPs becomes Evaluate, the rest methods beside it (so they see the fields), each taking the input
		// first so it can read it as Evaluate does; one that takes the PixelInput itself takes the SurfaceInput there
		var methods = new StringBuilder();
		string evaluate = null, composite = null;
		var functions = Functions( ps ).ToList();
		var helpers = functions.Where( f => !f.Signature.Contains( "PixelInput", StringComparison.Ordinal ) )
			.Select( f => Regex.Match( f.Signature, @"(\w+)\s*\(" ).Groups[1].Value ).Where( n => n != "MainPs" ).ToList();
		string PassInput( string code )
		{
			foreach ( var helper in helpers ) code = Regex.Replace( code, $@"\b{helper}\s*\(\s*(\)?)", m => m.Groups[1].Length > 0 ? $"{helper}( i )" : $"{helper}( i, " );
			return code;
		}

		foreach ( var (signature, body) in functions )
		{
			if ( Regex.IsMatch( signature, @"\bMainPs\s*\(" ) )
			{
				// Work after Shade becomes Composite, on the lit colour
				var (before, after) = SplitAtShade( body, notes );
				evaluate = PassInput( Rewrite( before, renames, notes, commonInputs, evaluate: true ) );
				if ( after is not null ) composite = PassInput( Rewrite( after, renames, notes, commonInputs, evaluate: true ) );
				continue;
			}

			var withInput = signature.Contains( "PixelInput", StringComparison.Ordinal )
				? Regex.Replace( signature.Trim(), @"\bPixelInput\b", "SurfaceInput" )
				: Regex.Replace( signature.Trim(), @"\(\s*(\)?)", m => m.Groups[1].Length > 0 ? "( SurfaceInput i )" : "( SurfaceInput i, ", RegexOptions.None, TimeSpan.FromSeconds( 1 ) );
			var helperBody = Regex.Replace( body, @"\bPixelInput\b", "SurfaceInput" );
			methods.Append( '\t' ).Append( Rewrite( withInput, renames, notes, commonInputs, evaluate: true ) ).Append( "\n\t{" ).Append( PassInput( Rewrite( helperBody, renames, notes, commonInputs, evaluate: true ) ) ).Append( "}\n\n" );
		}

		if ( evaluate is null ) return new Result( null, type, ["no MainPs"] );

		// A vertex shader that moves vertices in object space becomes Displace, with the helpers it includes
		var (displace, vertexHelpers) = Displace( vs, renames, notes, include );
		samplers += vertexHelpers;
		foreach ( var line in ps.Split( '\n' ) )
		{
			if ( Regex.IsMatch( line, @"^\s*RenderState\(" ) ) notes.Add( $"render state not converted: {line.Trim()}" );
		}

		var text = new StringBuilder();
		text.Append( "// " ).Append( name ).Append( ".shader as a surface, converted by Sandbox.ShaderCompiler's ShaderConverter; the .shader stays.\n" );
		foreach ( var note in notes.Distinct() ) text.Append( "// Not converted: " ).Append( note ).Append( '\n' );
		text.Append( "import sbox.surface;\n\n" ).Append( samplers ).Append( samplers.Length > 0 ? "\n" : "" );
		var interfaces = new List<string> { displace is null ? "ISurface" : "IVertexSurface" };
		if ( composite is not null ) interfaces = [.. interfaces.Where( n => n != "ISurface" ), "ICompositeSurface"];
		text.Append( "[Material]\npublic struct " ).Append( type ).Append( " : " ).Append( string.Join( ", ", interfaces ) ).Append( "\n{\n" ).Append( fields ).Append( '\n' );
		if ( displace is not null ) text.Append( "\tpublic float3 Displace( VertexSurfaceInput v )\n\t{\n\t\tfloat3 __position = v.Position;" ).Append( displace ).Append( "\t\treturn __position;\n\t}\n\n" );
		text.Append( methods );
		text.Append( "\tpublic Material Evaluate( SurfaceInput i )\n\t{" ).Append( evaluate ).Append( "}\n" );
		if ( composite is not null ) text.Append( '\n' ).Append( composite );
		text.Append( "}\n" );
		return new Result( text.ToString(), type, notes.Distinct().ToList() );
	}

	/// <summary>
	/// A <c>MainPs</c> body that shades into a local and works on it afterwards (<c>float4 c = ShadingModelStandard::Shade( i, m );</c>
	/// then refraction): Evaluate's body up to Shade, returning the material, and a <c>Composite</c> method with the rest. The
	/// top-level locals the rest reads are declared again in Composite from the final material and the input (water's
	/// fresnel); a local whose material reads change after it's declared is noted. Null for the rest when Shade is returned.
	/// </summary>
	static (string Before, string After) SplitAtShade( string body, List<string> notes )
	{
		var shade = Regex.Match( body, @"^[ \t]*float4\s+(\w+)\s*=\s*ShadingModelStandard::Shade\(\s*(?:i\s*,\s*)?(\w+)\s*\)\s*;[^\n]*\n?", RegexOptions.Multiline );
		if ( !shade.Success ) return (body, null);

		var (color, material) = (shade.Groups[1].Value, shade.Groups[2].Value);
		var before = body[..shade.Index];
		var after = body[(shade.Index + shade.Length)..];

		// Top-level single-line local declarations before Shade, by name
		var locals = new List<(string Name, string Line, int At)>();
		var depth = 0;
		var at = 0;
		foreach ( var line in before.Split( '\n' ) )
		{
			var declaration = Regex.Match( line, @"^\s*(?:const\s+)?(?:float[1234]?(?:x[1234])?|half[1234]?|int[1234]?|uint[1234]?|bool)\s+(\w+)\s*=[^;]*;" );
			if ( depth == 0 && declaration.Success ) locals.Add( (declaration.Groups[1].Value, line.TrimEnd( '\r' ), at) );
			depth += line.Count( c => c == '{' ) - line.Count( c => c == '}' );
			at += line.Length + 1;
		}

		// The ones the rest reads, and those they read
		var needed = new HashSet<string>( StringComparer.Ordinal );
		var reads = new Queue<string>( Regex.Matches( after, @"\b\w+\b" ).Select( m => m.Value ) );
		while ( reads.TryDequeue( out var name ) )
		{
			if ( !needed.Add( name ) ) continue;
			foreach ( var local in locals.Where( l => l.Name == name ) )
			{
				foreach ( Match read in Regex.Matches( local.Line[(local.Line.IndexOf( '=' ) + 1)..], @"\b\w+\b" ) ) reads.Enqueue( read.Value );
			}
		}

		var redeclared = new StringBuilder();
		foreach ( var local in locals.Where( l => needed.Contains( l.Name ) ).DistinctBy( l => l.Name ) )
		{
			redeclared.Append( local.Line ).Append( '\n' );
			foreach ( Match field in Regex.Matches( local.Line, $@"\b{material}\.(\w+)" ) )
			{
				if ( Regex.IsMatch( before[local.At..], $@"\b{material}\.{field.Groups[1].Value}\b[^;=]*[-+*/]?=[^=]" ) )
					notes.Add( $"Composite recomputes {local.Name} from the final {material}.{field.Groups[1].Value}, which changes after it" );
			}
		}

		var composite = $"\tpublic float4 Composite( float4 shaded, Material {material}, SurfaceInput i )\n\t{{\n{redeclared}\t\tfloat4 {color} = shaded;\n{after}}}\n";
		return (before + $"\t\treturn {material};\n\t", composite);
	}

	/// <summary>Includes the engine's passes provide, which a converted vertex shader doesn't copy.</summary>
	static readonly HashSet<string> EngineIncludes = new( StringComparer.OrdinalIgnoreCase ) { "common/vertex.hlsl", "common/shared.hlsl", "common/pixel.hlsl", "system.fxc", "common.fxc" };

	/// <summary>
	/// A <c>MainVs</c> that moves vertices in object space as <c>Displace</c>'s body (null when it only does the engine's
	/// work), and the functions of the includes it adds, copied: its code between <c>ProcessVertex</c> and
	/// <c>FinalizeVertex</c> with the object-space position from the input, the wind, time, colour, normal and origin from
	/// <c>VertexSurfaceInput</c>, and the world position it writes as the position Displace returns.
	/// </summary>
	static (string Body, string Helpers) Displace( string vs, Dictionary<string, string> renames, List<string> notes, Func<string, string> include )
	{
		var main = Functions( vs ).FirstOrDefault( f => Regex.IsMatch( f.Signature, @"\bMainVs\s*\(" ) );
		if ( main.Body is null || !Regex.IsMatch( main.Body, @"\bo\.vPositionWs\b|vPositionOs" ) ) return (null, "");

		var body = main.Body;
		body = Regex.Replace( body, @"^[ \t]*PixelInput\s+o\s*=\s*ProcessVertex\(\s*i\s*\)\s*;[^\n]*\n", "", RegexOptions.Multiline );
		body = Regex.Replace( body, @"^[ \t]*return\s+FinalizeVertex\(\s*o\s*\)\s*;[^\n]*\n?", "", RegexOptions.Multiline );
		body = Regex.Replace( body, @"^[ \t]*o\.vPositionPs[^;]*;[^\n]*\n", "", RegexOptions.Multiline );
		body = Regex.Replace( body, @"^[ \t]*o\.vColor\s*=[^;]*;[^\n]*\n", "", RegexOptions.Multiline );
		body = Regex.Replace( body, @"^[ \t]*float3x4\s+(\w+)\s*=\s*GetTransformMatrix\([^;]*\)\s*;[^\n]*\n", "", RegexOptions.Multiline );
		body = Regex.Replace( body, @"o\.vPositionWs(?:\.xyz)?\s*=\s*mul\(\s*\w+\s*,\s*float4\(\s*(.+?)\s*,\s*1(?:\.0)?f?\s*\)\s*\)\s*;", "__position = $1;" );
		body = Regex.Replace( body, @"VS_DecodeObjectSpaceNormalAndTangent\(\s*i\s*,\s*(\w+)\s*,\s*(\w+)\s*\)\s*;", "$1 = v.Normal; $2 = 0;" );
		body = Regex.Replace( body, @"mul\(\s*\w+\s*,\s*float4\(\s*0(?:\.0)?\s*,\s*0(?:\.0)?\s*,\s*0(?:\.0)?\s*,\s*1(?:\.0)?\s*\)\s*\)", "v.ObjectOrigin" );
		body = Regex.Replace( body, @"\bi\.vPositionOs(\.xyz)?\b", "v.Position" );
		body = Regex.Replace( body, @"g_vWindDirection\.xyz\s*\*\s*g_vWindStrengthFreqMulHighStrength\.x", "v.Wind" );
		body = Regex.Replace( body, @"\bg_flTime\b", "v.Time" );
		body = Regex.Replace( body, @"\bi\.vColor\b", "v.Color" );
		body = Rewrite( body, renames, notes, commonInputs: false, evaluate: false );

		var left = Regex.Matches( body, @"\b[io]\.\w+" ).Select( m => m.Value ).Distinct().ToList();
		if ( left.Count > 0 ) notes.Add( $"vertex shader reads or writes {string.Join( ", ", left )}: left as written" );

		// The includes' functions, copied, so the vertex code can call them
		var helpers = new StringBuilder();
		foreach ( Match m in Regex.Matches( vs, @"#include\s+""([^""]+)""" ) )
		{
			var path = m.Groups[1].Value;
			if ( EngineIncludes.Contains( path ) ) continue;
			if ( include?.Invoke( path ) is not { } text )
			{
				notes.Add( $"vertex shader includes {path}, not found" );
				continue;
			}

			helpers.Append( "// From " ).Append( path ).Append( '\n' );
			foreach ( var (signature, functionBody) in Functions( text ) ) helpers.Append( signature.Trim() ).Append( "\n{" ).Append( functionBody ).Append( "}\n\n" );
		}

		return (body, helpers.ToString());
	}

	/// <summary>The fixed vocabulary from common/pixel.hlsl shaders to surfaces.</summary>
	static string Rewrite( string code, Dictionary<string, string> renames, List<string> notes, bool commonInputs, bool evaluate )
	{
		// Static combos as branches
		code = Regex.Replace( code, @"^([ \t]*)#\s*if\s+(.+?)\s*$", m => $"{m.Groups[1].Value}if ( {m.Groups[2].Value.Trim().TrimStart( '(' ).TrimEnd( ')' ).Trim()} )\n{m.Groups[1].Value}{{", RegexOptions.Multiline );
		code = Regex.Replace( code, @"^([ \t]*)#\s*else\s*$", m => $"{m.Groups[1].Value}}}\n{m.Groups[1].Value}else\n{m.Groups[1].Value}{{", RegexOptions.Multiline );
		code = Regex.Replace( code, @"^([ \t]*)#\s*endif\b.*$", m => $"{m.Groups[1].Value}}}", RegexOptions.Multiline );

		code = Regex.Replace( code, @"Material::From\(\s*i\s*\)", commonInputs ? "Material.FromCommonInputs( i, Color, Normal, Rma, TintColor )" : "Material.Init( i )" );
		code = Regex.Replace( code, @"Material::Init\(\s*i?\s*\)", "Material.Init( i )" );
		code = code.Replace( "Material::lerp", "Material.Lerp", StringComparison.Ordinal );
		code = Regex.Replace( code, @"ShadingModelStandard::Shade\(\s*(?:i\s*,\s*)?(\w+)\s*\)", "$1" );

		// Material members a surface's input has instead: assignments dropped, reads from the input
		code = Regex.Replace( code, @"^[ \t]*\w+\.(TextureCoords|LightmapUV|WorldPosition|WorldPositionWithOffset|ScreenPosition|WorldTangentU|WorldTangentV|GeometricNormal|TangentNormal)\s*=[^;]*;[ \t]*\r?\n", "", RegexOptions.Multiline );
		code = Regex.Replace( code, @"\b\w+\.TextureCoords\b", "i.Uv" );
		code = Regex.Replace( code, @"\b\w+\.WorldPositionWithOffset\b", "( i.Position - i.Camera )" );
		code = Regex.Replace( code, @"\b\w+\.WorldPosition\b", "i.Position" );
		code = Regex.Replace( code, @"\b\w+\.WorldTangentU\b", "i.TangentU" );
		code = Regex.Replace( code, @"\b\w+\.WorldTangentV\b", "i.TangentV" );
		code = Regex.Replace( code, @"\b\w+\.GeometricNormal\b", "i.Normal" );
		code = Regex.Replace( code, @"\b(?!i\.)\w+\.ScreenPosition\b", "i.ScreenPosition" );

		// Depth passes are the engine's: their early return goes
		code = Regex.Replace( code, @"^[ \t]*if\s*\(\s*DepthNormals::WantsDepthNormals\(\s*\)\s*\)\s*(?:\{\s*)?return\s+DepthNormals::Output\([^;]*\)\s*;[ \t]*(?:\}[ \t]*)?\r?\n?", "", RegexOptions.Multiline );

		// The pixel input
		code = Regex.Replace( code, @"\bi\.vPositionSs(?:\.xy)?\s*\*\s*g_vInvViewportSize(?:\.xy)?\b", "i.ScreenUv" );
		code = Regex.Replace( code, @"\bi\.vTextureCoords\.xy\b", "i.Uv" );
		code = Regex.Replace( code, @"\bi\.vTextureCoords\.zw\b", "i.Uv2" );
		code = Regex.Replace( code, @"\bi\.vTextureCoords\b", "float4( i.Uv, i.Uv2 )" );
		code = Regex.Replace( code, @"\bi\.vNormalWs(\.xyz)?\b", "i.Normal" );
		code = Regex.Replace( code, @"\bi\.vTangentUWs(\.xyz)?\b", "i.TangentU" );
		code = Regex.Replace( code, @"\bi\.vTangentVWs(\.xyz)?\b", "i.TangentV" );
		code = Regex.Replace( code, @"\bi\.vVertexColor\b", "i.Color" );
		code = Regex.Replace( code, @"\bi\.vPositionSs\b", "i.ScreenPosition" );
		code = Regex.Replace( code, @"\bi\.vPositionWithOffsetWs(\.xyz)?\b", "( i.Position - i.Camera )" );
		code = Regex.Replace( code, @"\bg_vCameraPositionWs(\.xyz)?\b", "i.Camera" );
		code = Regex.Replace( code, @"\bg_vHighPrecisionLightingOffsetWs(\.xyz)?\b", "i.Camera" );
		code = Regex.Replace( code, @"\bg_flTime\b", "i.Time" );
		code = Calls( code, "CalculatePositionToCameraDirWs", args => args.Length == 1 ? $"normalize( i.Camera - ( {args[0].Trim()} ) )" : null );

		// Texture reads: through the input, so a resolve or the light loop can supply gradients; engine-bound textures (a
		// module global and its _sampler, renamed to themselves) as written
		bool Engine( string texture ) => renames.TryGetValue( texture.Trim(), out var renamed ) && renamed == texture.Trim();
		code = Calls( code, "Tex2DLevel", args => args.Length == 3 && Engine( args[0] ) ? $"{args[0].Trim()}.SampleLevel( {args[0].Trim()}_sampler, {args[1].Trim()}, {args[2].Trim()} )" : null );
		code = Calls( code, "Tex2D", args => args.Length == 2 && Engine( args[0] ) ? $"{args[0].Trim()}.Sample( {args[0].Trim()}_sampler, {args[1].Trim()} )" : null );
		code = Calls( code, "Tex2DS", args => args.Length == 3 ? Sample( args[0], args[2], args[1], renames, evaluate ) : null );
		code = MethodSamples( code, renames, evaluate );
		foreach ( var (from, to) in renames ) code = Regex.Replace( code, $@"\b{Regex.Escape( from )}\b", to );

		foreach ( var unsupported in new[] { "ddx", "ddy", "fwidth", "discard", "clip", "SV_" } )
		{
			if ( Regex.IsMatch( code, $@"\b{unsupported}\b" ) ) notes.Add( $"uses {unsupported}" );
		}

		var left = Regex.Matches( code, @"\b(i\.v\w+|g_\w+)" ).Select( m => m.Value ).Where( v => v != "g_MaterialFiltering" && !renames.ContainsValue( v ) ).Distinct().ToList();
		if ( left.Count > 0 ) notes.Add( $"left as written: {string.Join( ", ", left )}" );
		return code;
	}

	static string Sample( string texture, string uv, string sampler, Dictionary<string, string> renames, bool evaluate )
	{
		texture = texture.Trim();
		var field = renames.TryGetValue( texture, out var renamed ) ? renamed : texture.StartsWith( "g_t", StringComparison.Ordinal ) ? texture[3..] : texture;
		sampler = sampler.Trim();
		var standard = sampler is "TextureFiltering" or "g_sAniso" or "g_sDefault";

		// An engine-bound texture is a plain module global, sampled as written
		if ( renamed == texture ) return $"{texture}.Sample( {(standard ? "g_MaterialFiltering" : sampler)}, {uv.Trim()} )";
		// Helpers have no input: they sample with hardware derivatives
		if ( !evaluate ) return $"{field}.Sample( {(standard ? "g_MaterialFiltering" : sampler)}, {uv.Trim()} )";
		return standard ? $"i.Sample( {field}, {uv.Trim()} )" : $"i.Sample( {field}, {uv.Trim()}, {sampler} )";
	}

	/// <summary>Replace each call of <paramref name="name"/> (arguments split at the top level) with what <paramref name="replace"/> makes, when it makes something.</summary>
	static string Calls( string code, string name, Func<string[], string> replace )
	{
		var result = new StringBuilder();
		var at = 0;
		while ( Regex.Match( code[at..], $@"\b{name}\s*\(" ) is { Success: true } found )
		{
			var start = at + found.Index;
			var (args, end) = Arguments( code, start + found.Length - 1 );
			result.Append( code, at, start - at );
			result.Append( replace( args ) ?? code[start..end] );
			at = end;
		}

		result.Append( code, at, code.Length - at );
		return result.ToString();
	}

	/// <summary><c>g_tX.Sample( sampler, uv )</c> through the input, as <see cref="Sample"/> makes it.</summary>
	static string MethodSamples( string code, Dictionary<string, string> renames, bool evaluate )
	{
		var result = new StringBuilder();
		var at = 0;
		while ( Regex.Match( code[at..], @"\b(g_t\w+)\.Sample\s*\(" ) is { Success: true } found )
		{
			var start = at + found.Index;
			var (args, end) = Arguments( code, start + found.Length - 1 );
			result.Append( code, at, start - at );
			result.Append( args.Length == 2 ? Sample( found.Groups[1].Value, args[1], args[0], renames, evaluate ) : code[start..end] );
			at = end;
		}

		result.Append( code, at, code.Length - at );
		return result.ToString();
	}
	static (string[] Args, int End) Arguments( string code, int open )
	{
		var args = new List<string>();
		var depth = 0;
		var start = open + 1;
		for ( int i = open; i < code.Length; i++ )
		{
			var c = code[i];
			if ( c is '(' or '[' ) depth++;
			else if ( c is ')' or ']' )
			{
				depth--;
				if ( depth == 0 )
				{
					args.Add( code[start..i] );
					return (args.ToArray(), i + 1);
				}
			}
			else if ( c == ',' && depth == 1 )
			{
				args.Add( code[start..i] );
				start = i + 1;
			}
		}

		return (args.ToArray(), code.Length);
	}

	/// <summary>A top-level block's contents (<c>PS { ... }</c>), or null.</summary>
	static string Block( string text, string name )
	{
		var match = Regex.Match( text, $@"^\s*{name}\s*\r?\n?\s*\{{", RegexOptions.Multiline );
		if ( !match.Success ) return null;
		var open = match.Index + match.Length - 1;
		var close = Matching( text, open );
		return text[(open + 1)..close];
	}

	static int Matching( string text, int open )
	{
		var depth = 0;
		for ( int i = open; i < text.Length; i++ )
		{
			if ( text[i] == '{' ) depth++;
			else if ( text[i] == '}' && --depth == 0 ) return i;
		}

		return text.Length;
	}

	/// <summary>The block's function definitions: signature and body.</summary>
	static IEnumerable<(string Signature, string Body)> Functions( string block )
	{
		foreach ( Match m in Regex.Matches( block, @"^[ \t]*(?:\[[^\]]*\][ \t]*\r?\n?[ \t]*)*((?:static\s+)?[\w:<>]+\s+\w+\s*\([^;{]*\)(?:\s*:\s*\w+)?)\s*\{", RegexOptions.Multiline ) )
		{
			var open = m.Index + m.Length - 1;
			var close = Matching( block, open );
			var signature = Regex.Replace( m.Groups[1].Value, @"\s*:\s*SV_\w+$", "" );
			yield return (signature, block[(open + 1)..close]);
		}
	}

	static string Pascal( string name ) => string.Concat( name.Split( '_', StringSplitOptions.RemoveEmptyEntries ).Select( p => char.ToUpperInvariant( p[0] ) + p[1..].ToLowerInvariant() ) );
}
