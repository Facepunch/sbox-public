using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sandbox.ShaderCompiler;

// The final combiner shape: a .shader whose MainPs is vr_shared_standard_ps_code.fxc's PS_SharedStandardProcessing lit by
// the final combiner, and nothing else (simple.shader). What it draws is set by its defines and static combos, so it
// converts to a combiner surface (ICombinerSurface) doing that standard processing with those features: a define the
// shader fixes is a constant, a static combo a feature field the material sets, and a feature the surface doesn't do a
// field with Range( 0, 0 ) (materials setting it stay on the .shader), or no conversion when the shader fixes it on.
public static partial class ShaderConverter
{
	/// <summary>Standard processing the combiner surface doesn't do: fixed off, or a feature the material can't turn on.</summary>
	static readonly string[] StandardUnsupported =
	[
		"S_TRANSLUCENT", "S_ADDITIVE_BLEND", "S_WRINKLE", "S_ANISOTROPIC_GLOSS", "S_RETRO_REFLECTIVE", "S_USE_BENT_NORMALS",
		"S_UNLIT", "S_UV2_DEFAULT_FOR_AO", "S_UV2_CONTROLS", "S_SECONDARY_UV", "S_PARALLAX_OCCLUSION", "S_CLOTH_SHADING",
		"S_DIFFUSE_WRAP", "S_ENABLE_NORMAL_SELF_SHADOW", "S_TRANSMISSIVE_BACKFACE_NDOTL", "S_LIQUID_SHADING",
		"S_DO_NOT_CAST_SHADOWS", "S_SUBSURFACE_SCATTERING",
	];

	/// <summary>The standard-processing surface, or null when the shader isn't of the final combiner shape.</summary>
	static Result StandardCombiner( string shader, string name, string ps, string mainBody, string type )
	{
		if ( !Regex.IsMatch( mainBody, @"\bPS_SharedStandardProcessing\s*\(" ) ) return null;
		var notes = new List<string>();
		Result Refuse( string why ) => new( null, type, [why] );

		// MainPs: the standard processing, then only the final combiner
		var body = Regex.Replace( mainBody, @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline );
		var input = Regex.Match( body, @"FinalCombinerInput_t\s+(\w+)\s*=\s*PS_SharedStandardProcessing\(\s*i\s*\)\s*;" );
		if ( !input.Success ) return Refuse( "final combiner: PS_SharedStandardProcessing's result isn't kept as it is" );
		var f = input.Groups[1].Value;
		var rest = body.Remove( input.Index, input.Length );
		foreach ( var allowed in new[]
		{
			@"LightingTerms_t\s+\w+\s*=\s*InitLightingTerms\(\s*\)\s*;",
			@"PS_OUTPUT\s+\w+\s*;",
			$@"(?:PS_OUTPUT\s+)?\w+\s*=\s*PS_FinalCombinerDoLighting\(\s*{f}\s*,\s*\w+\s*\)\s*;",
			$@"(?:PS_OUTPUT\s+)?\w+\s*=\s*PS_FinalCombinerDoPostProcessing\(\s*{f}\s*,\s*\w+\s*,\s*\w+\s*\)\s*;",
			$@"return\s+PS_FinalCombiner\(\s*{f}\s*\)\s*;",
			@"return\s+\w+\s*;",
		} )
		{
			rest = Regex.Replace( rest, allowed, "" );
		}

		if ( !string.IsNullOrWhiteSpace( rest ) ) return Refuse( "final combiner: MainPs works on the combiner's input or output beyond lighting it" );

		// MainVs: the standard processing alone
		var vs = Block( shader, "VS" ) ?? "";
		var mainVs = Functions( vs ).FirstOrDefault( fn => Regex.IsMatch( fn.Signature, @"\bMainVs\s*\(" ) );
		var vsBody = Regex.Replace( mainVs.Body ?? "", @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline );
		vsBody = Regex.Replace( vsBody, @"PS_INPUT\s+(\w+)\s*=\s*VS_SharedStandardProcessing\(\s*i\s*\)\s*;\s*return\s+VS_CommonProcessing_Post\(\s*\1\s*\)\s*;", "" );
		if ( mainVs.Body is null || !string.IsNullOrWhiteSpace( vsBody ) ) return Refuse( "final combiner: the vertex shader does more than VS_SharedStandardProcessing" );

		// The defines and combos the standard processing reads
		var features = FeatureDecl().Matches( Block( shader, "FEATURES" ) ?? "" ).ToDictionary( m => m.Groups[1].Value, m => (int.Parse( m.Groups[2].Value, CultureInfo.InvariantCulture ), int.Parse( m.Groups[3].Value, CultureInfo.InvariantCulture )) );
		var pre = new StandardDefines( (Block( shader, "COMMON" ) ?? "") + "\n" + ps );
		var fields = new StringBuilder();
		var unsupported = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var s in StandardUnsupported )
		{
			var value = pre.Value( s );
			if ( value.Constant == 0 ) continue;
			if ( pre.Combos.TryGetValue( s, out var feature ) && value.Code == pre.FeatureField( feature ) )
			{
				unsupported.Add( feature );
				notes.Add( $"{feature} isn't converted: materials setting it stay on the .shader" );
				continue;
			}

			return Refuse( $"final combiner: {s} is {(value.Constant is not null ? "on" : "set by more than its own feature")}, which a surface doesn't do" );
		}

		// Specular off is a define the combiner reads at compile time (vr_common_ps_code.fxc): undefined or on only
		if ( pre.Defined( "S_SPECULAR" ).Constant != 0 && pre.Value( "S_SPECULAR" ).Constant is null or 0 ) return Refuse( "final combiner: S_SPECULAR isn't fixed on" );
		if ( pre.Defined( "ENABLE_NORMAL_MAPS" ).Constant != 1 ) return Refuse( "final combiner: no normal map (ENABLE_NORMAL_MAPS)" );

		var gates = new Dictionary<string, Expression>
		{
			["alpha"] = pre.Value( "S_ALPHA_TEST" ),
			["tint"] = pre.Value( "S_TINT_MASK" ),
			["selfillum"] = pre.Value( "S_SELF_ILLUM" ),
			["metalness"] = pre.Value( "S_METALNESS_TEXTURE" ),
			["metalnessincolor"] = pre.Defined( "METALNESS_IN_COLOR" ),
			["aoincolor"] = pre.Defined( "AMBIENT_OCCLUSION_IN_COLOR" ),
			["scalenormal"] = pre.Value( "S_SCALE_NORMAL_MAP" ),
			["detail"] = pre.Value( "S_DETAIL_TEXTURE" ),
		};

		foreach ( var (gate, value) in gates )
		{
			if ( value.Unknown ) return Refuse( $"final combiner: can't tell when {gate} is on" );
		}

		// Feature fields: every static combo's feature, the ones the surface doesn't do limited to off
		foreach ( var (combo, feature) in pre.Combos.OrderBy( c => c.Value, StringComparer.Ordinal ) )
		{
			var (min, max) = features.TryGetValue( feature, out var range ) ? range : (0, 1);
			var off = unsupported.Contains( feature );
			fields.Append( CultureInfo.InvariantCulture, $"\t[Group( \"{(off ? "Not converted" : "Features")}\" ), Source( \"{feature}\" ), Range( {min}, {(off ? 0 : max)} )] int {pre.FeatureField( feature )};\n" );
		}

		bool On( string gate ) => gates[gate].Constant != 0;
		bool Always( string gate ) => gates[gate].Constant is not null and not 0;
		string Bool( string gate ) => gates[gate].Bool;

		// Fields: the standard vertex processing's, then the pixel processing's for what can be on
		fields.Append( "\n\t// Colour\n\t[Group( \"Color\" ), Srgb, Opacity] Texture2D Color;\n" );
		if ( On( "tint" ) ) fields.Append( "\t[Group( \"Color\" )] Texture2D TintMask;\n" );
		fields.Append( "\t[Group( \"Color\" ), Default3( 1, 1, 1 )] float3 ColorTint;\n\t[Group( \"Color\" ), Default( 1 ), Range( 0, 1 )] float ModelTintAmount;\n\t[Group( \"Color\" ), Default( 1 ), Range( 1, 16 )] float FadeExponent;\n" );
		fields.Append( "\n\t// Texture coordinates\n\t[Group( \"Texture Coordinates\" ), Default2( 1, 1 )] float2 TexCoordScale;\n\t[Group( \"Texture Coordinates\" )] float2 TexCoordOffset;\n\t[Group( \"Texture Coordinates\" )] float2 TexCoordScrollSpeed;\n" );
		fields.Append( "\n\t// Normal and roughness\n\t[Group( \"Normal\" )] Texture2D Normal;\n" );
		if ( On( "scalenormal" ) ) fields.Append( "\t[Group( \"Normal\" ), Default( 1 ), Range( 0, 1 )] float NormalMapScaleFactor;\n" );
		fields.Append( "\t[Group( \"Roughness\" ), Default( 1 ), Range( 0, 2 )] float RoughnessScaleFactor;\n" );
		if ( !Always( "metalness" ) ) fields.Append( "\n\t// Metalness\n\t[Group( \"Metalness\" ), Range( 0, 1 )] float Metalness;\n" );
		if ( On( "metalness" ) && !Always( "metalnessincolor" ) ) fields.Append( "\t[Group( \"Metalness\" ), Source( \"g_tMetalness\" )] Texture2D MetalnessMap;\n" );
		if ( !Always( "aoincolor" ) ) fields.Append( "\n\t// Ambient occlusion\n\t[Group( \"Ambient Occlusion\" )] Texture2D AmbientOcclusion;\n" );
		if ( On( "alpha" ) ) fields.Append( "\n\t// Alpha test\n\t[Group( \"Translucent\" ), Default( 0.5 ), Range( 0.01, 0.99 )] float AlphaTestReference;\n\t[Group( \"Translucent\" ), Default( 1 ), Range( 0, 1 )] float AntiAliasedEdgeStrength;\n" );
		if ( On( "selfillum" ) )
		{
			fields.Append( "\n\t// Self-illumination\n\t[Group( \"Self Illum\" ), Srgb] Texture2D SelfIllumMask;\n\t[Group( \"Self Illum\" ), Default3( 1, 1, 1 )] float3 SelfIllumTint;\n\t[Group( \"Self Illum\" ), Range( -10, 10 )] float SelfIllumBrightness;\n" );
			fields.Append( "\t[Group( \"Self Illum\" ), Default( 1 ), Range( 0, 16 )] float SelfIllumScale;\n\t[Group( \"Self Illum\" )] float2 SelfIllumOffset;\n\t[Group( \"Self Illum\" )] float2 SelfIllumScrollSpeed;\n\t[Group( \"Self Illum\" ), Default( 1 ), Range( 0, 1 )] float SelfIllumAlbedoFactor;\n" );
		}

		if ( On( "detail" ) )
		{
			fields.Append( "\n\t// Detail texture\n\t[Group( \"Detail Texture\" )] Texture2D Detail;\n\t[Group( \"Detail Texture\" )] Texture2D NormalDetail;\n\t[Group( \"Detail Texture\" )] Texture2D DetailMask;\n" );
			fields.Append( "\t[Group( \"Detail Texture\" ), Default( 1 ), Range( 0, 1 )] float DetailBlendFactor;\n\t[Group( \"Detail Texture\" ), Range( 0, 1 )] float DetailBlendToFull;\n\t[Group( \"Detail Texture\" ), Default( 1 ), Range( 0, 1 )] float DetailNormalStrength;\n" );
			fields.Append( "\t[Group( \"Detail Texture\" ), Default2( 1, 1 )] float2 DetailTexCoordScale;\n\t[Group( \"Detail Texture\" ), Range( 0, 360 )] float DetailTexCoordRotation;\n\t[Group( \"Detail Texture\" )] float2 DetailTexCoordOffset;\n" );
		}

		// Evaluate: PS_SharedStandardProcessing with the vertex stage's uv and tint, gated as the shader's defines say
		var e = new StringBuilder();
		void Gated( string gate, string code )
		{
			if ( !On( gate ) ) return;
			if ( Always( gate ) ) e.Append( code );
			else e.Append( CultureInfo.InvariantCulture, $"\t\tif ( {Bool( gate )} )\n\t\t{{\n{Regex.Replace( code, "^(?=.)", "\t", RegexOptions.Multiline )}\t\t}}\n" );
		}

		e.Append( "\t\t// The vertex stage's work (vr_shared_standard_vs_code.fxc): uv scale, offset and scroll; object tint and fade\n" );
		e.Append( "\t\tfloat2 uv = i.Uv * TexCoordScale + TexCoordOffset + TexCoordScrollSpeed * i.Time;\n" );
		e.Append( "\t\tfloat3 tint = lerp( float3( 1, 1, 1 ), i.Color.rgb, ModelTintAmount ) * SrgbGammaToLinear( ColorTint );\n" );
		e.Append( "\t\tfloat fade = pow( i.Color.a, FadeExponent );\n\n" );
		e.Append( "\t\tMaterial m = Material.Init( i );\n\t\tfloat4 color = i.Sample( Color, uv );\n" );
		e.Append( "\t\tm.Opacity = fade;\n" );
		Gated( "alpha", "\t\tm.Opacity = color.a * fade;\n\t\tm.AlphaTestReference = AlphaTestReference;\n\t\tm.AntiAliasedEdgeStrength = AntiAliasedEdgeStrength;\n\t\tm.OpacityUvScale = TexCoordScale;\n" );
		e.Append( "\n\t\tfloat3 albedo = color.rgb * tint;\n" );
		Gated( "tint", "\t\talbedo = lerp( color.rgb, color.rgb * tint, i.Sample( TintMask, uv ).r );\n" );
		Gated( "selfillum", "\t\tfloat2 selfIllumUv = SelfIllumOffset + uv + frac( i.Time * SelfIllumScrollSpeed );\n\t\tfloat3 scaled = pow( 2.0, SelfIllumBrightness ).xxx * saturate( SelfIllumScale ) * SrgbGammaToLinear( SelfIllumTint );\n\t\tfloat3 emission = i.Sample( SelfIllumMask, selfIllumUv, g_StandardTrilinear ).rgb * scaled;\n\t\tm.Emission = lerp( emission, emission * albedo, SelfIllumAlbedoFactor );\n" );

		// Occlusion: the colour's alpha, or its texture
		e.Append( '\n' ).Append( Always( "aoincolor" ) ? "\t\tm.AmbientOcclusion = color.a;\n"
			: On( "aoincolor" ) ? $"\t\tm.AmbientOcclusion = {Bool( "aoincolor" )} ? color.a : i.Sample( AmbientOcclusion, uv, g_StandardTrilinear ).r;\n"
			: "\t\tm.AmbientOcclusion = i.Sample( AmbientOcclusion, uv, g_StandardTrilinear ).r;\n" );

		// Metalness: a value, or with the feature the colour's alpha or the metalness texture's green
		if ( !Always( "metalness" ) ) e.Append( "\t\tm.Metalness = Metalness;\n" );
		Gated( "metalness", Always( "metalnessincolor" ) ? "\t\tm.Metalness = color.a;\n"
			: On( "metalnessincolor" ) ? $"\t\tm.Metalness = {Bool( "metalnessincolor" )} ? color.a : i.Sample( MetalnessMap, uv, g_StandardTrilinear ).g;\n"
			: "\t\tm.Metalness = i.Sample( MetalnessMap, uv, g_StandardTrilinear ).g;\n" );

		var detail = gates["detail"].Int;
		if ( On( "detail" ) ) e.Append( CultureInfo.InvariantCulture, $"\n\t\tint detailMode = {detail};\n\t\tfloat detailMask = 0;\n\t\tif ( detailMode != 0 ) detailMask = max( i.Sample( DetailMask, uv ).r, DetailBlendToFull ) * DetailBlendFactor;\n" );

		// Normal and roughness: hemi-octahedral normal in rg, roughness in b
		e.Append( "\n\t\tfloat4 normal = i.Sample( Normal, uv );\n\t\tfloat3 normalTs = HemiOctahedron( normal.rg );\n\t\tm.Roughness = pow( normal.b, RoughnessScaleFactor );\n" );
		Gated( "scalenormal", "\t\tnormalTs = normalize( lerp( float3( 0, 0, 1 ), normalTs, NormalMapScaleFactor ) );\n" );
		if ( On( "detail" ) )
		{
			e.Append( "\t\tfloat2 detailUv = DetailUv( i.Uv );\n\t\tif ( detailMode == 3 || detailMode == 4 )\n\t\t{\n" );
			e.Append( "\t\t\tfloat3 detailTs = HemiOctahedron( i.Sample( NormalDetail, detailUv ).rg );\n\t\t\tdetailTs = float3( 0, 0, 1 - DetailNormalStrength ) + DetailNormalStrength * detailTs;\n" );
			e.Append( "\t\t\tdetailTs = lerp( float3( 0, 0, 1 ), detailTs, detailMask );\n\t\t\tnormalTs *= detailTs.z;\n\t\t\tnormalTs.xy += normalTs.z * detailTs.xy;\n\t\t\tnormalTs = normalize( normalTs );\n\t\t}\n" );
		}

		e.Append( "\t\tm.Normal = TransformNormal( normalTs, i.Normal, i.TangentU, i.TangentV );\n" );
		if ( On( "detail" ) )
		{
			e.Append( "\n\t\tif ( detailMode == 1 || detailMode == 2 || detailMode == 4 )\n\t\t{\n\t\t\tfloat3 detail = i.Sample( Detail, detailUv ).rgb;\n" );
			e.Append( "\t\t\tif ( detailMode == 1 ) albedo *= lerp( float3( 1, 1, 1 ), 1.992156862745098 * detail, detailMask );\n\t\t\telse albedo = Overlay( albedo, detail, detailMask );\n\t\t}\n" );
		}

		e.Append( "\n\t\tm.Albedo = albedo;\n\t\treturn m;\n" );

		// Helpers
		var methods = new StringBuilder();
		methods.Append( "\t/// vr_common.fxc's DecodeHemiOctahedronNormal: a tangent-space normal from a hemi-octahedral texel's rg.\n\tstatic float3 HemiOctahedron( float2 rg )\n\t{\n\t\tfloat2 d = float2( rg.x + rg.y - 1.00392163, rg.x - rg.y );\n\t\treturn normalize( float3( d, 1.0 - abs( d.x ) - abs( d.y ) ) );\n\t}\n\n" );
		if ( On( "detail" ) )
		{
			methods.Append( "\t/// vr_detail_texture.fxc's CalculateDetailTextureCoordinates: the detail uv from the mesh's, scaled, rotated and offset.\n\tfloat2 DetailUv( float2 uv )\n\t{\n\t\tfloat a = DetailTexCoordRotation * 3.14159265359 / 180.0;\n\t\tfloat c = cos( a );\n\t\tfloat s = sin( a );\n" );
			methods.Append( "\t\tfloat4 xform = DetailTexCoordScale.xxyy * float4( c, -s, s, c );\n\t\tfloat2 offset = -0.5 * DetailTexCoordScale * float2( c - s, s + c ) + DetailTexCoordOffset + 0.5;\n\t\treturn float2( dot( xform.xy, uv ), dot( xform.zw, uv ) ) + offset;\n\t}\n\n" );
			methods.Append( "\t/// vr_detail_texture.fxc's PhotoshopOverlay.\n\tstatic float3 Overlay( float3 albedo, float3 detail, float mask )\n\t{\n\t\tdetail *= 1.992156862745098 * 0.5;\n\t\tfloat3 gamma = pow( albedo, 1.0 / 2.2 );\n" );
			methods.Append( "\t\tfloat3 screen = 1.0 - 2.0 * ( ( 1.0 - gamma ) * ( 1.0 - detail ) );\n\t\tfloat3 multiply = 2.0 * gamma * detail;\n\t\tfloat3 overlay = lerp( screen, multiply, step( gamma, float3( 0.5, 0.5, 0.5 ) ) );\n\t\treturn lerp( albedo, pow( overlay, 2.2 ), mask );\n\t}\n\n" );
		}

		var text = new StringBuilder();
		text.Append( "// " ).Append( name ).Append( ".shader as a surface, converted by Sandbox.ShaderCompiler's ShaderConverter (the final combiner shape:\n" );
		text.Append( "// vr_shared_standard_ps_code.fxc's standard processing, lit by the final combiner); the .shader stays.\n" );
		foreach ( var note in notes.Distinct() ) text.Append( "// Not converted: " ).Append( note ).Append( '\n' );
		text.Append( "import sbox.surface;\n\n" );
		text.Append( "// g_sTrilinearWrap, for occlusion, metalness and self-illumination (the rest use g_sAniso, the surfaces' standard filtering)\n" );
		text.Append( "SamplerState g_StandardTrilinear < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;\n\n" );
		text.Append( "[Material]\npublic struct " ).Append( type ).Append( " : ICombinerSurface\n{\n" ).Append( fields ).Append( '\n' ).Append( methods );
		text.Append( "\tpublic Material Evaluate( SurfaceInput i )\n\t{\n" ).Append( e ).Append( "\t}\n}\n" );
		return new Result( text.ToString(), type, notes.Distinct().ToList() );
	}

	/// <summary>
	/// A preprocessor expression as Slang: <see cref="Code"/> (an int, or a bool when <see cref="IsBool"/>), its value
	/// when it's a constant, and whether it reads something the converter can't resolve.
	/// </summary>
	sealed record Expression( string Code, bool IsBool, int? Constant, bool Unknown = false )
	{
		public static Expression Of( int value ) => new( value.ToString( CultureInfo.InvariantCulture ), false, value );

		/// <summary>As a bool.</summary>
		public string Bool => Constant is { } c ? (c != 0 ? "true" : "false") : IsBool ? Code : $"( {Code} != 0 )";

		/// <summary>As an int.</summary>
		public string Int => Constant is { } c ? c.ToString( CultureInfo.InvariantCulture ) : IsBool ? $"( {Code} ? 1 : 0 )" : Code;
	}

	/// <summary>
	/// A shader's defines and static combos (COMMON and PS), each define with the condition its <c>#if</c>s put it under,
	/// read as Slang expressions over the material's feature fields.
	/// </summary>
	sealed class StandardDefines
	{
		readonly Dictionary<string, List<(Expression Condition, string Value)>> defines = new( StringComparer.Ordinal );
		readonly HashSet<string> resolving = new( StringComparer.Ordinal );

		/// <summary>Static combos: S_ name to the feature that sets it.</summary>
		public Dictionary<string, string> Combos { get; } = new( StringComparer.Ordinal );

		public StandardDefines( string text )
		{
			// Each open #if: the conditions of its branches so far, and the current branch's
			var stack = new List<(List<Expression> Previous, Expression Current)>();
			Expression Under() => stack.Aggregate( Expression.Of( 1 ), ( all, frame ) => And( all, frame.Current ) );

			foreach ( var raw in text.Split( '\n' ) )
			{
				var line = Regex.Replace( raw, @"//.*$", "" ).Trim();
				Match m;
				if ( (m = Regex.Match( line, @"^#\s*ifdef\s+(\w+)" )).Success ) stack.Add( ([], Defined( m.Groups[1].Value )) );
				else if ( (m = Regex.Match( line, @"^#\s*ifndef\s+(\w+)" )).Success ) stack.Add( ([], Not( Defined( m.Groups[1].Value ) )) );
				else if ( (m = Regex.Match( line, @"^#\s*if\s+(.+)$" )).Success ) stack.Add( ([], Parse( m.Groups[1].Value )) );
				else if ( (m = Regex.Match( line, @"^#\s*elif\s+(.+)$" )).Success && stack.Count > 0 )
				{
					var (previous, current) = stack[^1];
					previous.Add( current );
					stack[^1] = (previous, And( NoneOf( previous ), Parse( m.Groups[1].Value ) ));
				}
				else if ( Regex.IsMatch( line, @"^#\s*else\b" ) && stack.Count > 0 )
				{
					var (previous, current) = stack[^1];
					previous.Add( current );
					stack[^1] = (previous, NoneOf( previous ));
				}
				else if ( Regex.IsMatch( line, @"^#\s*endif\b" ) && stack.Count > 0 ) stack.RemoveAt( stack.Count - 1 );
				else if ( (m = Regex.Match( line, @"^#\s*define\s+(\w+)(?:\s+(.*))?$" )).Success )
				{
					var name = m.Groups[1].Value;
					if ( !defines.TryGetValue( name, out var list ) ) defines[name] = list = [];
					list.Add( (Under(), m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0 ? m.Groups[2].Value.Trim() : "1") );
				}
				else if ( (m = Regex.Match( line, @"^StaticCombo\(\s*(S_\w+)\s*,\s*(F_\w+)" )).Success ) Combos.TryAdd( m.Groups[1].Value, m.Groups[2].Value );
			}
		}

		/// <summary>A feature's field (<c>F_TINT_MASK</c> is <c>UseTintMask</c>).</summary>
		public string FeatureField( string feature ) => "Use" + Pascal( feature[2..] );

		/// <summary>Whether a name is defined, as an expression.</summary>
		public Expression Defined( string name )
		{
			if ( !defines.TryGetValue( name, out var list ) ) return Expression.Of( 0 );
			return list.Select( d => d.Condition ).Aggregate( Expression.Of( 0 ), Or );
		}

		/// <summary>A name's value: a combo's feature field, a define's value under its condition, or 0 when undefined.</summary>
		public Expression Value( string name )
		{
			if ( Combos.TryGetValue( name, out var feature ) ) return new Expression( FeatureField( feature ), false, null );
			if ( name.StartsWith( "F_", StringComparison.Ordinal ) )
				return Combos.ContainsValue( name ) ? new Expression( FeatureField( name ), false, null ) : Expression.Of( 0 );
			if ( !defines.TryGetValue( name, out var list ) )
				return name.StartsWith( "S_", StringComparison.Ordinal ) ? Expression.Of( 0 ) : new Expression( name, false, null, true );
			if ( !resolving.Add( name ) ) return new Expression( name, false, null, true );
			try
			{
				// The last definition whose condition holds
				var value = Expression.Of( 0 );
				foreach ( var (condition, text) in list ) value = Select( condition, Parse( text ), value );
				return value;
			}
			finally
			{
				resolving.Remove( name );
			}
		}

		Expression Parse( string text )
		{
			var tokens = Regex.Matches( text, @"\d+|\w+|&&|\|\||==|!=|<=|>=|[!()<>]" ).Select( t => t.Value ).ToList();
			var at = 0;
			string Peek() => at < tokens.Count ? tokens[at] : null;

			Expression Primary()
			{
				var token = Peek();
				at++;
				if ( token is null ) return new Expression( "?", false, null, true );
				if ( token == "(" )
				{
					var inner = OrExpr();
					if ( Peek() == ")" ) at++;
					return inner;
				}

				if ( token == "!" ) return Not( Primary() );
				if ( token == "defined" )
				{
					var paren = Peek() == "(";
					if ( paren ) at++;
					var name = Peek();
					at++;
					if ( paren && Peek() == ")" ) at++;
					return Defined( name );
				}

				if ( int.TryParse( token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number ) ) return Expression.Of( number );
				return token is "true" or "false" ? Expression.Of( token == "true" ? 1 : 0 ) : Value( token );
			}

			Expression Compare()
			{
				var left = Primary();
				while ( Peek() is "==" or "!=" or "<" or ">" or "<=" or ">=" )
				{
					var op = tokens[at++];
					var right = Primary();
					left = Binary( left, right, op, ( a, b ) => op switch { "==" => a == b, "!=" => a != b, "<" => a < b, ">" => a > b, "<=" => a <= b, _ => a >= b } ? 1 : 0, ints: true );
				}

				return left;
			}

			Expression AndExpr()
			{
				var left = Compare();
				while ( Peek() == "&&" ) { at++; left = And( left, Compare() ); }
				return left;
			}

			Expression OrExpr()
			{
				var left = AndExpr();
				while ( Peek() == "||" ) { at++; left = Or( left, AndExpr() ); }
				return left;
			}

			var result = OrExpr();
			return at < tokens.Count ? result with { Unknown = true } : result;
		}

		static Expression Binary( Expression a, Expression b, string op, Func<int, int, int> fold, bool ints )
		{
			var unknown = a.Unknown || b.Unknown;
			if ( a.Constant is { } x && b.Constant is { } y ) return Expression.Of( fold( x, y ) ) with { Unknown = unknown };
			return new Expression( ints ? $"( {a.Int} {op} {b.Int} )" : $"( {a.Bool} {op} {b.Bool} )", true, null, unknown );
		}

		static Expression And( Expression a, Expression b )
		{
			if ( a.Constant == 0 || b.Constant == 0 ) return Expression.Of( 0 ) with { Unknown = a.Unknown || b.Unknown };
			if ( a.Constant is not null ) return b with { Unknown = a.Unknown || b.Unknown };
			if ( b.Constant is not null ) return a with { Unknown = a.Unknown || b.Unknown };
			return Binary( a, b, "&&", ( x, y ) => x != 0 && y != 0 ? 1 : 0, ints: false );
		}

		static Expression Or( Expression a, Expression b )
		{
			if ( a.Constant is not null and not 0 || b.Constant is not null and not 0 ) return Expression.Of( 1 ) with { Unknown = a.Unknown || b.Unknown };
			if ( a.Constant == 0 ) return b with { Unknown = a.Unknown || b.Unknown };
			if ( b.Constant == 0 ) return a with { Unknown = a.Unknown || b.Unknown };
			return Binary( a, b, "||", ( x, y ) => x != 0 || y != 0 ? 1 : 0, ints: false );
		}

		static Expression Not( Expression a ) => a.Constant is { } c ? Expression.Of( c == 0 ? 1 : 0 ) with { Unknown = a.Unknown } : new Expression( $"!{a.Bool}", true, null, a.Unknown );

		static Expression NoneOf( List<Expression> branches ) => branches.Aggregate( Expression.Of( 1 ), ( all, b ) => And( all, Not( b ) ) );

		static Expression Select( Expression condition, Expression value, Expression otherwise )
		{
			if ( condition.Constant is { } c ) return (c != 0 ? value : otherwise) with { Unknown = condition.Unknown || (c != 0 ? value.Unknown : otherwise.Unknown) };
			return new Expression( $"( {condition.Bool} ? {value.Int} : {otherwise.Int} )", false, null, condition.Unknown || value.Unknown || otherwise.Unknown );
		}
	}
}
