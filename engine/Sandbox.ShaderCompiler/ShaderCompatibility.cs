namespace Sandbox.ShaderCompiler;

/// <summary>
/// What a surface keeps for materials compiled long ago, by module: the feature lines of the shaders it stands for
/// (standard's complex, simple and generic, beside <c>ShaderAliases</c>, which names them), their rules over features
/// the surface doesn't do, old texture packings, and the defines the engine's shared vfx headers need. Spliced into the
/// generated declarations (<see cref="SurfaceDeclarations"/>); nothing here is a surface author's.
/// </summary>
public sealed partial class ShaderCompatibility
{
	/// <summary>Lines ahead of the feature lines (a feature only old materials set, and its upgrade).</summary>
	public string Prefix { get; init; }

	/// <summary>
	/// The feature lines of another shader the surface stands for, by its place in the surface's
	/// <c>[Source( "Shader: ..." )]</c> list (<see cref="FeatureRules.ShaderDefine"/>); the surface's own are place 0's.
	/// </summary>
	public IReadOnlyDictionary<int, string> ShaderFeatures { get; init; }

	/// <summary>Rules over features the surface doesn't do, each after the feature line it followed in the shader's own.</summary>
	public IReadOnlyDictionary<string, string[]> RulesAfter { get; init; }

	/// <summary>Defines the shared headers read, after the static combos.</summary>
	public string Defines { get; init; }

	/// <summary>
	/// The shared inputs' declarations (the standard processing's header and the defines it's configured by: the static
	/// combos, and the features the surface doesn't do as 0), where the first of <see cref="TakenBySet"/> is. The
	/// surface's own static combos are these defines' alone.
	/// </summary>
	public string Inputs { get; init; }

	/// <summary>The texture fields <see cref="Inputs"/> makes.</summary>
	public IReadOnlySet<string> TakenBySet { get; init; }

	/// <summary>Old packings' textures, after the fields'.</summary>
	public string Textures { get; init; }

	/// <summary>Made textures named as an older shader named them (vertex_color's <c>g_tColor2</c>), by field.</summary>
	public IReadOnlyDictionary<string, string> TextureNames { get; init; }

	/// <summary>The module's, or null.</summary>
	public static ShaderCompatibility For( string module ) => module is not null && Table.TryGetValue( module, out var entry ) ? entry : null;

	static readonly Dictionary<string, ShaderCompatibility> Table = new( StringComparer.OrdinalIgnoreCase )
	{
		// fur.shader's materials from before F_NEW_TEXTURE_PACKING: one texture per input, the normal and masks read as sRGB
		["fur"] = new()
		{
			Prefix = """
				Feature( F_NEW_TEXTURE_PACKING, 0..1 );
				FeatureUpgrade( F_NEW_TEXTURE_PACKING, 1 );
				""",
			Textures = """
				#if !S_NEW_TEXTURE_PACKING
				CreateInputTexture2D( FurNoise, Linear, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
				CreateInputTexture2D( BaseColor, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
				CreateInputTexture2D( Normal, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
				CreateInputTexture2D( Roughness, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
				CreateInputTexture2D( Metalness, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
				CreateInputTexture2D( AO, Srgb, 8, "None", "_color", ",0/,0/0", Default4( 1.00, 1.00, 1.00, 1.00 ) );
				Texture2D g_tFurNoise < Channel( RGBA, Box( FurNoise ), Linear ); OutputFormat( DXT5 ); SrgbRead( False ); >;
				Texture2D g_tBaseColor < Channel( RGBA, Box( BaseColor ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
				Texture2D g_tNormal < Channel( RGBA, Box( Normal ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
				Texture2D g_tRoughness < Channel( RGBA, Box( Roughness ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
				Texture2D g_tMetalness < Channel( RGBA, Box( Metalness ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
				Texture2D g_tAO < Channel( RGBA, Box( AO ), Srgb ); OutputFormat( DXT5 ); SrgbRead( True ); >;
				#endif
				""",
		},

		// skin.shader's wrinkle textures, which the material editor shows where F_WRINKLE is set: skin.shader never passed
		// wrinkles on, and the surface doesn't do them, so nothing reads them
		["skin"] = new()
		{
			Textures = """
				CreateInputTexture2D( TextureSquishColor, Srgb, 8, "", "_color", "Color/11", Default3( 1.0, 1.0, 1.0 ) );
				CreateInputTexture2D( TextureStretchColor, Srgb, 8, "", "_color", "Color/12", Default3( 1.0, 1.0, 1.0 ) );
				CreateInputTexture2D( TextureSquishNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal/21", Default3( 0.5, 0.5, 1.0 ) );
				CreateInputTexture2D( TextureStretchNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal/22", Default3( 0.5, 0.5, 1.0 ) );
				CreateInputTexture2D( TextureSquishAmbientOcclusion, Linear, 8, "", "_ao", "Ambient Occlusion/11", Default3( 1.0, 1.0, 1.0 ) );
				CreateInputTexture2D( TextureStretchAmbientOcclusion, Linear, 8, "", "_ao", "Ambient Occlusion/12", Default3( 1.0, 1.0, 1.0 ) );
				Texture2D g_tSquishColor < Channel( RGB, Box( TextureSquishColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); ActiveIf( F_WRINKLE != 0 ); >;
				Texture2D g_tStretchColor < Channel( RGB, Box( TextureStretchColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); ActiveIf( F_WRINKLE != 0 ); >;
				Texture2D g_tSquishNormal < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureSquishNormal, TextureRoughness ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); ActiveIf( F_WRINKLE != 0 ); >;
				Texture2D g_tStretchNormal < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureStretchNormal, TextureRoughness ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); ActiveIf( F_WRINKLE != 0 ); >;
				Texture2D g_tSquishAmbientOcclusion < Channel( R, Box3( TextureSquishAmbientOcclusion ), Linear ); OutputFormat( ATI1N ); SrgbRead( false ); ActiveIf( F_WRINKLE != 0 ); >;
				Texture2D g_tStretchAmbientOcclusion < Channel( R, Box3( TextureStretchAmbientOcclusion ), Linear ); OutputFormat( ATI1N ); SrgbRead( false ); ActiveIf( F_WRINKLE != 0 ); >;
				""",
		},

		// standard stands for complex, simple and generic (ShaderAliases): materials an older engine compiled for simple or
		// generic keep their shader's feature lines and rules (FeatureRules.ShaderDefine, Packing's place), and complex's
		// rules over the features standard doesn't do still apply to a material's values; the standard processing's header
		// is configured as complex.shader configured it, those features off
		["standard"] = new()
		{
			ShaderFeatures = new Dictionary<int, string>
			{
				[1] = """
					#include "vr_common_features.fxc"
					Feature( F_METALNESS_TEXTURE, 0..1, "PBR" );
					Feature( F_AMBIENT_OCCLUSION_TEXTURE, 0..1, "PBR" );
					""",
				[2] = """
					Feature( F_SPECULAR, 0..1, "Specular" );
					Feature( F_UNLIT, 0..1, "Lighting" );
					FeatureRule( Allow1( F_UNLIT, F_SPECULAR ), "Specular and Unlit are not compatible" );
					Feature( F_ALPHA_TEST, 0..1, "Translucent" );
					Feature( F_TRANSLUCENT, 0..1, "Translucent" );
					Feature( F_ADDITIVE_BLEND, 0..1, "Translucent" );
					FeatureRule( Allow1( F_TRANSLUCENT, F_ALPHA_TEST ), "Translucent and Alpha Test are not compatible" );
					FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Requires translucency" );
					Feature( F_RENDER_BACKFACES, 0..1, "Rendering" );
					Feature( F_SELF_ILLUM, 0..1, "Self Illum" );
					FeatureRule( Allow1( F_SELF_ILLUM, F_UNLIT ), "Self Illum and Unlit are not compatible" );
					Feature( F_TINT_MASK, 0..1, "Per-Instance Tint Mask" );
					FeatureRule( Allow1( F_UNLIT, F_TINT_MASK ), "Tint Mask and Unlit are not compatible" );
					Feature( F_PAINT_VERTEX_COLORS, 0..1, "Vertex Paint" );
					""",
			},
			RulesAfter = new Dictionary<string, string[]>
			{
				["F_ADDITIVE_BLEND"] =
				[
					"FeatureRule( Allow1( F_ALPHA_TEST, F_TRANSLUCENT ), \"Translucent and Alpha Test are not compatible\" );",
					"FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), \"Requires translucency\" );",
					"FeatureRule( Allow1( F_TRANSLUCENT, F_DO_NOT_CAST_SHADOWS ), \"Translucent and Do Not Cast Shadows are not compatible\" );",
				],
				["F_TINT_MASK"] =
				[
					"FeatureRule( Allow1( F_ANISOTROPIC_GLOSS, F_ADDITIVE_BLEND ), \"Anisotropic Gloss and Additive Blend are incompatible\" );",
					"FeatureRule( Allow1( F_RETRO_REFLECTIVE, F_ANISOTROPIC_GLOSS ), \"Retro Reflective and Anisotropic Gloss are not compatible\" );",
					"FeatureRule( Allow1( F_RETRO_REFLECTIVE, F_ALPHA_TEST ), \"Retro Reflective and Alpha Test are not compatible\" );",
					"FeatureRule( Allow1( F_RETRO_REFLECTIVE, F_TRANSLUCENT ), \"Retro Reflective and Translucent are not compatible\" );",
					"FeatureRule( Allow1( F_RETRO_REFLECTIVE, F_TINT_MASK ), \"Retro Reflective and Tint Mask are not compatible\" );",
					"FeatureRule( Allow1( F_RETRO_REFLECTIVE, F_ADDITIVE_BLEND ), \"Retro Reflective and Additive Blend are incompatible\" );",
				],
				["F_SCALE_NORMAL_MAP"] =
				[
					"FeatureRule( Allow1( F_SCALE_NORMAL_MAP, F_ALPHA_TEST ), \"Scale normals and Alpha Test are not compatible\" );",
					"FeatureRule( Allow1( F_SCALE_NORMAL_MAP, F_TRANSLUCENT ), \"Scale normals and Translucent are not compatible\" );",
					"FeatureRule( Allow1( F_SCALE_NORMAL_MAP, F_ANISOTROPIC_GLOSS ), \"Scale normals and Anisotropic Gloss are not compatible\" );",
					"FeatureRule( Allow1( F_RETRO_REFLECTIVE, F_SCALE_NORMAL_MAP ), \"Retro Reflective and Scale normals are not compatible\" );",
					"FeatureRule( Allow1( F_SCALE_NORMAL_MAP, F_ADDITIVE_BLEND ), \"Scale Normal Map and Additive Blend are incompatible\" );",
					"FeatureRule( Allow1( F_ENABLE_NORMAL_SELF_SHADOW, F_ADDITIVE_BLEND ), \"Enable Normal Self Shadow and Additive Blend are incompatible\" );",
					"FeatureRule( Allow1( F_USE_BENT_NORMALS, F_TRANSLUCENT ), \"Bent Normals and Alpha Test are not compatible\" );",
					"FeatureRule( Allow1( F_USE_BENT_NORMALS, F_TINT_MASK ), \"Bent Normals and Tint Mask are not compatible\" );",
					"FeatureRule( Allow1( F_USE_BENT_NORMALS, F_RETRO_REFLECTIVE ), \"Bent Normals and Retro Reflective are not compatible\" );",
					"FeatureRule( Allow1( F_USE_BENT_NORMALS, F_ADDITIVE_BLEND ), \"Use Bent Normals and Additive Blend are incompatible\" );",
					"FeatureRule( Allow1( F_USE_BENT_NORMALS, F_SCALE_NORMAL_MAP ), \"Use Bent Normals and Scaled Normal Maps are Incompatible\" );",
				],
				["F_SELF_ILLUM"] =
				[
					"FeatureRule( Allow1( F_SELF_ILLUM, F_RETRO_REFLECTIVE ), \"Self Illum and Retro Reflective are not compatible\" );",
				],
				["F_DETAIL_TEXTURE"] =
				[
					"FeatureRule( Allow1( F_DETAIL_TEXTURE, F_SELF_ILLUM ), \"Detail Texture and Self Illum are not compatible\" );",
					"FeatureRule( Allow1( F_DETAIL_TEXTURE, F_ANISOTROPIC_GLOSS ), \"Detail Texture and Anisotropic Gloss are not compatible\" );",
					"FeatureRule( Allow1( F_DETAIL_TEXTURE, F_RETRO_REFLECTIVE ), \"Detail Texture and Retro Reflective are not compatible\" );",
					"FeatureRule( Allow1( F_DETAIL_TEXTURE, F_ADDITIVE_BLEND ), \"Detail Texture and Additive Blend are not compatible\" );",
				],
				["F_METALNESS_TEXTURE"] =
				[
					"FeatureRule( Allow1( F_METALNESS_TEXTURE, F_ADDITIVE_BLEND ), \"Metalness Texture and Additive Blend are not compatible\" );",
					"FeatureRule( Allow1( F_SECONDARY_UV, F_RETRO_REFLECTIVE ), \"Secondary UV and Retro Reflective are not compatible\" );",
					"FeatureRule( Allow1( F_SECONDARY_UV, F_ENABLE_NORMAL_SELF_SHADOW ), \"Secondary UV and Normal Self Shadow are not compatible\" );",
					"FeatureRule( Allow1( F_SECONDARY_UV, F_USE_BENT_NORMALS ), \"Secondary UV and Use Bent Normals are not compatible\" );",
					"FeatureRule( Allow1( F_SECONDARY_UV, F_SCALE_NORMAL_MAP ), \"Secondary UV and Scale normals are not compatible\" );",
					"FeatureRule( Allow1( F_DIFFUSE_WRAP, F_RETRO_REFLECTIVE ), \"Diffuse wrap and Retro Reflective are not compatible\" );",
					"FeatureRule( Allow1( F_DIFFUSE_WRAP, F_SCALE_NORMAL_MAP ), \"Diffuse wrap and Scale Normal Map are not compatible\" );",
					"FeatureRule( Allow1( F_DIFFUSE_WRAP, F_DETAIL_TEXTURE ), \"Diffuse wrap and Detail Texture are not compatible\" );",
					"FeatureRule( Allow1( F_DIFFUSE_WRAP, F_ADDITIVE_BLEND ), \"Diffuse wrap and Additive Blend are not compatible\" );",
				],
				["F_TRANSMISSIVE_BACKFACE_NDOTL"] =
				[
					"FeatureRule( Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_DIFFUSE_WRAP ), \"Transmissive Backface Ndotl and Unlit are not compatible\" );",
					"FeatureRule( Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_RETRO_REFLECTIVE ), \"Transmissive Backface Ndotl and Retro Reflective are not compatible\" );",
					"FeatureRule( Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_SCALE_NORMAL_MAP ), \"Transmissive Backface Ndotl and Scale Normal Map are not compatible\" );",
					"FeatureRule( Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_DETAIL_TEXTURE ), \"Transmissive Backface Ndotl and Detail Texture are not compatible\" );",
					"FeatureRule( Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_ADDITIVE_BLEND ), \"Transmissive Backface Ndotl and Additive Blend are not compatible\" );",
					"FeatureRule( Allow1( F_CLOTH_SHADING, F_ANISOTROPIC_GLOSS ), \"Cloth Shading and Anisotropic Gloss are not compatible\" );",
					"FeatureRule( Allow1( F_CLOTH_SHADING, F_RETRO_REFLECTIVE ), \"Cloth Shading and Retro Reflective are not compatible\" );",
					"FeatureRule( Allow1( F_CLOTH_SHADING, F_METALNESS_TEXTURE ), \"Cloth Shading and Metalness Texture are not compatible\" );",
					"FeatureRule( Allow1( F_CLOTH_SHADING, F_SCALE_NORMAL_MAP ), \"Cloth Shading and Scale Normal Map are not compatible\" );",
					"FeatureRule( Allow1( F_CLOTH_SHADING, F_DIFFUSE_WRAP ), \"Cloth Shading and Diffuse Wrap are not compatible\" );",
					"FeatureRule( Allow1( F_PARALLAX_OCCLUSION, F_TRANSLUCENT ), \"Parallax Occlusion and Translucent are not compatible\" );",
					"FeatureRule( Allow1( F_PARALLAX_OCCLUSION, F_ALPHA_TEST ), \"Parallax Occlusion and Alpha Test are not compatible\" );",
					"FeatureRule( Allow1( F_PARALLAX_OCCLUSION, F_CLOTH_SHADING ), \"Parallax Occlusion and Cloth Shading are not compatible\" );",
					"FeatureRule( Allow1( F_PARALLAX_OCCLUSION, F_USE_BENT_NORMALS ), \"Parallax Occlusion and Bent Normals are not compatible\" );",
					"FeatureRule( Allow1( F_PARALLAX_OCCLUSION, F_DIFFUSE_WRAP ), \"Parallax Occlusion and Diffuse Wrap are not compatible\" );",
					"FeatureRule( Allow1( F_PARALLAX_OCCLUSION, F_ADDITIVE_BLEND ), \"Parallax Occlusion and Additive Blend are not compatible\" );",
					"FeatureRule( Allow1( F_PARALLAX_OCCLUSION, F_DETAIL_TEXTURE ), \"Parallax Occlusion and Detail Texture are not compatible\" );",
					"FeatureRule( Allow1( F_PARALLAX_OCCLUSION, F_METALNESS_TEXTURE ), \"Parallax Occlusion and Metalness Texture are not compatible\" );",
					"FeatureRule( Allow1( F_PARALLAX_OCCLUSION, F_RETRO_REFLECTIVE ), \"Parallax Occlusion and Retro Reflective are not compatible\" );",
				],
			},
			Inputs = """
				#define DETAIL_TEXTURES_DECLARED
				#define VR_DETAIL_TEXTURE_FXC
				#define S_ANISOTROPIC_GLOSS 0
				#define S_RETRO_REFLECTIVE 0
				#define S_ENABLE_NORMAL_SELF_SHADOW 0
				#define S_USE_BENT_NORMALS 0
				#define S_SECONDARY_UV 0
				#define S_DIFFUSE_WRAP 0
				#define S_CLOTH_SHADING 0
				#define S_PARALLAX_OCCLUSION 0
				#define ENABLE_NORMAL_MAPS
				#define S_SPECULAR 1
				#define S_UV2 ( S_SECONDARY_UV )
				#define S_UV2_DEFAULT_FOR_AO ( S_SECONDARY_UV )
				#define S_UV2_CONTROLS ( S_SECONDARY_UV )
				#define S_SPECULAR_CUBE_MAP 1
				#define MATERIAL_CONTROLLED_BACKFACES 1
				#define BENT_NORMAL_ACTIVE_IF ActiveIf( S_USE_BENT_NORMALS != 0 );
				#define S_DETAIL_TEXTURE F_DETAIL_TEXTURE
				#include "vr_shared_standard_vs_code.fxc"
				#define S_ALPHA_TEST F_ALPHA_TEST
				#define S_TRANSLUCENT F_TRANSLUCENT
				#define S_ADDITIVE_BLEND F_ADDITIVE_BLEND
				#define S_SELF_ILLUM F_SELF_ILLUM
				#define S_TINT_MASK F_TINT_MASK
				#define S_METALNESS_TEXTURE F_METALNESS_TEXTURE
				#define S_SCALE_NORMAL_MAP F_SCALE_NORMAL_MAP
				#define S_TRANSMISSIVE_BACKFACE_NDOTL F_TRANSMISSIVE_BACKFACE_NDOTL
				#if ( S_TRANSLUCENT )
				Texture2D g_tColor < Channel( RGB, AlphaWeighted( TextureColor, TextureTranslucency ), Srgb );
				Channel( A, Box( TextureTranslucency ), Linear );
				OutputFormat( BC7 ); SrgbRead( true ); >;
				#elif ( S_ALPHA_TEST )
				Texture2D g_tColor < Channel( RGB, AlphaWeighted( TextureColor, TextureTranslucency ), Srgb );
				Channel( A, PreserveCoverage( TextureTranslucency ), Linear );
				OutputFormat( BC7 ); SrgbRead( true ); >;
				#elif ( S_METALNESS_TEXTURE && !S_RETRO_REFLECTIVE )
				#define METALNESS_IN_COLOR
				Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb );
				Channel( A, Box( TextureMetalness ), Linear );
				OutputFormat( BC7 ); SrgbRead( true ); >;
				#else
				Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb );
				OutputFormat( DXT1 ); SrgbRead( true ); >;
				#endif
				#if ( ( S_TRANSLUCENT || S_ALPHA_TEST ) )
				TextureAttribute( LightSim_Opacity_A, g_tColor );
				#endif
				#define COLOR_SAMPLER_DEFINED
				#include "sbox/standard_material_inputs.slangh"
				""",
			TakenBySet = new HashSet<string> { "Color", "TintMask", "Normal", "MetalnessMap", "AmbientOcclusion", "SelfIllumMask", "SelfIllumMaskLinear", "DetailMask" },
		},

		// static_overlay.shader configured the standard processing's header by blend mode
		["static_overlay"] = new()
		{
			Inputs = """
				#define ENABLE_NORMAL_MAPS
				#define S_SPECULAR 0
				#define S_METALNESS_TEXTURE 0
				#define S_SPECULAR_CUBE_MAP 0
				#define S_RENDER_BACKFACES 0
				#define S_DO_NOT_CAST_SHADOWS 1
				#include "vr_shared_standard_vs_code.fxc"
				#define S_LIT F_LIT
				#define S_BLEND_MODE F_BLEND_MODE
				#define BLEND_MODE_ALREADY_SET
				#define DEPTH_STATE_ALREADY_SET
				#define S_ALPHA_TEST ( S_BLEND_MODE == 2 )
				#if ( S_LIT == 0 )
				#define S_UNLIT 1
				#endif
				#if ( S_BLEND_MODE == 1 )
				#define S_TRANSLUCENT 1
				#elif ( S_ALPHA_TEST )
				#elif( S_BLEND_MODE == 3 )
				#define S_TRANSLUCENT 1
				#elif( S_BLEND_MODE == 4 )
				#define S_TRANSLUCENT 1
				#define S_ADDITIVE_BLEND 1
				#elif( S_BLEND_MODE == 5 )
				#define S_TRANSLUCENT 1
				#elif( S_BLEND_MODE == 6 )
				#define S_TRANSLUCENT 1
				#define S_ADDITIVE_BLEND 1
				#define S_SELF_ILLUM 1
				#endif
				#if ( S_BLEND_MODE == 3 )
				#define COLOR_SAMPLER_DEFINED
				Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb );
				Channel( A, Box( TextureTranslucency ), Linear );
				OutputFormat( BC7 ); SrgbRead( false ); >;
				#endif
				#include "sbox/standard_material_inputs.slangh"
				""",
			TakenBySet = new HashSet<string> { "Color", "ColorLinear", "Normal", "AmbientOcclusion", "SelfIllumMask" },
		},

		// vertex_color.shader made its colour as vr_shared_standard_ps_code.fxc's g_tColor2, which its materials name
		["vertex_color"] = new() { TextureNames = new Dictionary<string, string> { ["Color"] = "g_tColor2" } },
	};
}
