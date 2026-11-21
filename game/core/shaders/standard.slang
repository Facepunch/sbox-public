// The standard lit material, which stands in for complex.shader, simple.shader and generic.shader: those names are
// aliases of standard (ShaderAliases), so materials naming them compile from this module, open in the material editor with
// its inputs (complex's) and draw here. Materials an older engine compiled for the three load into it with their own
// values; Packing says which shader's textures they were compiled for (the normal map's encoding, what the colour's alpha
// holds) and picks that shader's shading where the three differ. Materials compiled now are packed as complex's.
//
// complex's features: colour with tint mask and object tint, the hemi-octahedral normal map with roughness, ambient
// occlusion, metalness as a value or texture, alpha test (alpha to coverage), translucency and additive blending,
// self-illumination, detail textures (all four modes), scaled normal maps and back-face transmission. simple's: occlusion
// or metalness in the colour's alpha. generic's: the DXT5 normal map with bump strength, specular on or off (the
// anisotropic roughness texture and metalness when on), unlit, the self-illumination mask that takes its share off the
// diffuse and specular colours, and the alpha test's hard cut. It lights through the final combiner (ICombinerSurface), as
// the three shaders do.
//
// The features are fields, [Source] naming the feature a .vmat sets them with, read by uniform branches; unlit, whose
// pipelines need far fewer registers, is a specialization constant too ([Specialize]). Features this surface doesn't do
// (the fields below with Range( 0, 0 )) aren't offered, and a material setting them draws without them: complex's parallax
// occlusion, anisotropic gloss, retro-reflection, cloth, bent normals, normal self shadowing, diffuse wrap, the secondary
// uv set and wrinkles, and generic's vertex paint.
import sbox.surface;

// What the material compiler makes of a .vmat's images (SlangMaterialCompiler) and the material editor shows: complex.shader's
// texture inputs, the textures made from them, its parameters and attributes, read with the material's features. Materials
// naming complex, simple or generic compile and open as standard's (ShaderAliases)
#if MATERIAL_INPUTS
// The material editor's FEATURES lines: complex.shader's, less the features this surface doesn't do (above)
#include "vr_common_features.fxc"
Feature( F_ALPHA_TEST, 0..1, "Translucent" );
Feature( F_TRANSLUCENT, 0..1, "Translucent" );
Feature( F_ADDITIVE_BLEND, 0..1, "Translucent" );
FeatureRule( Allow1( F_ALPHA_TEST, F_TRANSLUCENT ), "Translucent and Alpha Test are not compatible" );
FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Requires translucency" );
FeatureRule( Allow1( F_TRANSLUCENT, F_DO_NOT_CAST_SHADOWS ), "Translucent and Do Not Cast Shadows are not compatible" );
Feature( F_TINT_MASK, 0..1, "Color" );
Feature( F_SCALE_NORMAL_MAP, 0..1, "Normal" );
FeatureRule( Allow1( F_SCALE_NORMAL_MAP, F_ALPHA_TEST ), "Scale normals and Alpha Test are not compatible" );
FeatureRule( Allow1( F_SCALE_NORMAL_MAP, F_TRANSLUCENT ), "Scale normals and Translucent are not compatible" );
FeatureRule( Allow1( F_SCALE_NORMAL_MAP, F_ADDITIVE_BLEND ), "Scale Normal Map and Additive Blend are incompatible" );
Feature( F_SELF_ILLUM, 0..1, "PBR" );
Feature( F_DETAIL_TEXTURE, 0..4( 0="None", 1="Mod2X", 2="Overlay", 3="Normals", 4="Overlay and Normals" ), "Detail Texture" );
FeatureRule( Allow1( F_DETAIL_TEXTURE, F_SELF_ILLUM ), "Detail Texture and Self Illum are not compatible" );
FeatureRule( Allow1( F_DETAIL_TEXTURE, F_ADDITIVE_BLEND ), "Detail Texture and Additive Blend are not compatible" );
Feature( F_METALNESS_TEXTURE, 0..1, "PBR" );
FeatureRule( Allow1( F_METALNESS_TEXTURE, F_ADDITIVE_BLEND ), "Metalness Texture and Additive Blend are not compatible" );
Feature( F_TRANSMISSIVE_BACKFACE_NDOTL, 0..1, "PBR" );
FeatureRule( Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_SCALE_NORMAL_MAP ), "Transmissive Backface Ndotl and Scale Normal Map are not compatible" );
FeatureRule( Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_DETAIL_TEXTURE ), "Transmissive Backface Ndotl and Detail Texture are not compatible" );
FeatureRule( Allow1( F_TRANSMISSIVE_BACKFACE_NDOTL, F_ADDITIVE_BLEND ), "Transmissive Backface Ndotl and Additive Blend are not compatible" );
// The material's parameters as the material editor shows them, with the feature states that use them
float2 g_vTexCoordScale < Default2( 1, 1 ); Range2( 0, 0, 100, 100 ); UiGroup( "Texture Coordinates,80/10" ); >;
float2 g_vTexCoordOffset < Default2( 0, 0 ); Range2( -1, -1, 1, 1 ); UiGroup( "Texture Coordinates/20" ); >;
float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); Range2( -10, -10, 10, 10 ); UiGroup( "Texture Coordinates/30" ); >;
float3 g_vColorTint < UiType( Color ); Default3( 1, 1, 1 ); UiGroup( "Color,10/20" ); >;
float g_flModelTintAmount < Default( 1 ); Range( 0, 1 ); UiGroup( "Color,10/30" ); >;
float g_flFadeExponent < Default( 1 ); Range( 1, 16 ); UiGroup( "Fade,400/10" ); >;
float g_flNormalMapScaleFactor < Default( 1 ); Range( 0, 1 ); UiGroup( "Normal" ); >;
float g_flRoughnessScaleFactor < Default( 1 ); Range( 0, 2 ); UiGroup( "Roughness" ); >;
float g_flMetalness < Default( 0 ); Range( 0, 1 ); UiGroup( "Metalness,27/20" ); ActiveIf( F_METALNESS_TEXTURE == 0 ); >;
float g_flAmbientOcclusionDirectDiffuse < Default( 0 ); Range( 0, 1 ); UiGroup( "Ambient Occlusion/20" ); >;
float g_flAmbientOcclusionDirectSpecular < Default( 0 ); Range( 0, 1 ); UiGroup( "Ambient Occlusion/30" ); >;
bool g_bFogEnabled < Default( 1 ); UiGroup( "Fog,300" ); >;
float g_flAlphaTestReference < Default( 0.5 ); Range( 0.01, 0.99 ); UiGroup( "Translucent" ); ActiveIf( F_ALPHA_TEST != 0 ); >;
float g_flAntiAliasedEdgeStrength < Default( 1 ); Range( 0, 1 ); UiGroup( "Translucent" ); ActiveIf( F_ALPHA_TEST != 0 ); >;
float g_flOpacityScale < Default( 1 ); Range( 0, 1 ); UiGroup( "Translucent" ); ActiveIf( F_TRANSLUCENT != 0 ); >;
float3 g_vSelfIllumTint < UiType( Color ); Default3( 1, 1, 1 ); UiGroup( "Self Illum/20" ); ActiveIf( F_SELF_ILLUM != 0 ); >;
float g_flSelfIllumBrightness < Default( 0 ); Range( -10, 10 ); UiGroup( "Self Illum" ); ActiveIf( F_SELF_ILLUM != 0 ); >;
float g_flSelfIllumScale < Default( 1 ); Range( 0, 16 ); UiGroup( "Self Illum/30" ); ActiveIf( F_SELF_ILLUM != 0 ); >;
float2 g_vSelfIllumOffset < Default2( 0, 0 ); UiGroup( "Self Illum/35" ); ActiveIf( F_SELF_ILLUM != 0 ); >;
float2 g_vSelfIllumScrollSpeed < Default2( 0, 0 ); Range2( -10, -10, 10, 10 ); UiGroup( "Self Illum/40" ); ActiveIf( F_SELF_ILLUM != 0 ); >;
float g_flSelfIllumAlbedoFactor < Default( 1 ); Range( 0, 1 ); UiGroup( "Self Illum/50" ); ActiveIf( F_SELF_ILLUM != 0 ); >;
float2 g_vDetailTexCoordScale < Default2( 1, 1 ); UiGroup( "Detail Texture,80/30" ); ActiveIf( F_DETAIL_TEXTURE != 0 ); >;
float g_flDetailTexCoordRotation < Default( 0 ); Range( 0, 360 ); UiGroup( "Detail Texture,80/35" ); ActiveIf( F_DETAIL_TEXTURE != 0 ); >;
float2 g_vDetailTexCoordOffset < Default2( 0, 0 ); Range2( -1, -1, 1, 1 ); UiGroup( "Detail Texture,80/40" ); ActiveIf( F_DETAIL_TEXTURE != 0 ); >;
float g_flDetailBlendFactor < Default( 1 ); Range( 0, 1 ); UiGroup( "Detail Texture,80/20" ); ActiveIf( F_DETAIL_TEXTURE != 0 ); >;
float g_flDetailBlendToFull < Default( 0 ); Range( 0, 1 ); UiGroup( "Detail Texture,80/50" ); ActiveIf( F_DETAIL_TEXTURE != 0 ); >;
float g_flDetailNormalStrength < Default( 1 ); Range( 0, 1 ); UiGroup( "Detail Texture,80/15" ); ActiveIf( F_DETAIL_TEXTURE >= 3 ); >;
BoolAttribute( NeedsLightProbe, true );
// End of the material editor's lines
// The detail textures with the modes that use each (the colour in mod2x and the overlays, the normal in the normal modes),
// and the detail parameters declared above with the states that show them: the standard inputs leave both to this
#if ( F_DETAIL_TEXTURE )
Texture2D g_tDetail < Channel( RGB, Box( TextureDetail ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); ActiveIf( F_DETAIL_TEXTURE == 1 || F_DETAIL_TEXTURE == 2 || F_DETAIL_TEXTURE == 4 ); >;
Texture2D g_tNormalDetail < Channel( RG, HemiOctIsoRoughness_RG_B( TextureNormalDetail ), Linear ); OutputFormat( ATI2N ); SrgbRead( false ); ActiveIf( F_DETAIL_TEXTURE >= 3 ); >;
#endif
#define DETAIL_TEXTURES_DECLARED
#define VR_DETAIL_TEXTURE_FXC
// The features this surface doesn't do, off
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
CreateInputTexture2D( TextureTransmissiveColor, Srgb, 8, "", "_color", "Transmission", Default3( 1.0, 1.0, 1.0 ) );
Texture2D g_tTransmissiveColor < Channel( RGB, Box( TextureTransmissiveColor ), Srgb );  OutputFormat( BC7 ); SrgbRead( true ); ActiveIf( S_TRANSMISSIVE_BACKFACE_NDOTL != 0 ); >;
#endif

// complex.shader's g_sTrilinearWrap (occlusion, metalness, self-illumination and transmission); the rest of complex's use
// g_sAniso, the surfaces' standard filtering
SamplerState g_StandardTrilinear < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;

// generic.shader's g_sDefault (every texture), which has no state of its own: native's default, anisotropic and wrapped
SamplerState g_StandardDefault < Filter( ANISOTROPIC ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;

[Material]
public struct Standard : ICombinerSurface
{
	// Which shader the material was made for: 0 complex, 1 simple, 2 generic (their textures are packed differently)
	[Group( "Features" ), Source( "Shader: complex simple generic" ), Range( 0, 2 )] int Packing;

	// Features
	[Group( "Features" ), Source( "F_TINT_MASK" )] bool UseTintMask;
	[Group( "Features" ), Source( "F_METALNESS_TEXTURE" )] bool UseMetalnessTexture;
	[Group( "Features" ), Source( "F_AMBIENT_OCCLUSION_TEXTURE" )] bool UseAmbientOcclusionTexture;
	[Group( "Features" ), Source( "F_ALPHA_TEST" ), RenderState( "AlphaTest" )] bool UseAlphaTest;
	[Group( "Features" ), Source( "F_SELF_ILLUM" )] bool UseSelfIllum;
	[Group( "Features" ), Source( "F_DETAIL_TEXTURE" ), Range( 0, 4 )] int DetailMode;   // 1 mod2x, 2 overlay, 3 normals, 4 overlay and normals
	[Group( "Features" ), Source( "F_SCALE_NORMAL_MAP" )] bool UseNormalMapScale;
	[Group( "Features" ), Source( "F_TRANSMISSIVE_BACKFACE_NDOTL" )] bool UseBackfaceTransmission;
	[Group( "Features" ), Source( "F_SPECULAR" )] bool UseSpecular;   // generic's; complex and simple are always specular
	[Group( "Features" ), Source( "F_UNLIT" ), Specialize] bool UseUnlit;
	[Group( "Features" ), Source( "F_TRANSLUCENT" ), RenderState( "Blend: Alpha" )] bool UseTranslucent;
	[Group( "Features" ), Source( "F_ADDITIVE_BLEND" ), RenderState( "Blend: Additive" )] bool UseAdditiveBlend;
	[Group( "Features" ), Source( "F_RENDER_BACKFACES" ), RenderState( "TwoSided" )] bool RenderBackfaces;

	// Features this surface doesn't do: materials that set them draw without them
	[Group( "Not converted" ), Source( "F_PARALLAX_OCCLUSION" ), Range( 0, 0 )] int ParallaxOcclusion;
	[Group( "Not converted" ), Source( "F_ANISOTROPIC_GLOSS" ), Range( 0, 0 )] int AnisotropicGloss;
	[Group( "Not converted" ), Source( "F_RETRO_REFLECTIVE" ), Range( 0, 0 )] int RetroReflective;
	[Group( "Not converted" ), Source( "F_CLOTH_SHADING" ), Range( 0, 0 )] int ClothShading;
	[Group( "Not converted" ), Source( "F_USE_BENT_NORMALS" ), Range( 0, 0 )] int BentNormals;
	[Group( "Not converted" ), Source( "F_ENABLE_NORMAL_SELF_SHADOW" ), Range( 0, 0 )] int NormalSelfShadow;
	[Group( "Not converted" ), Source( "F_DIFFUSE_WRAP" ), Range( 0, 0 )] int DiffuseWrap;
	[Group( "Not converted" ), Source( "F_SECONDARY_UV" ), Range( 0, 0 )] int SecondaryUv;
	[Group( "Not converted" ), Source( "F_WRINKLE" ), Range( 0, 0 )] int Wrinkle;
	[Group( "Not converted" ), Source( "F_PAINT_VERTEX_COLORS" ), Range( 0, 0 )] int PaintVertexColors;

	// Colour
	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Color" )] Texture2D TintMask;
	[Group( "Color" ), Default3( 1, 1, 1 )] float3 ColorTint;
	[Group( "Color" ), Default( 1 ), Range( 0, 1 )] float ModelTintAmount;
	[Group( "Color" ), Default( 1 ), Range( 1, 16 )] float FadeExponent;

	// Texture coordinates
	[Group( "Texture Coordinates" ), Default2( 1, 1 )] float2 TexCoordScale;
	[Group( "Texture Coordinates" )] float2 TexCoordOffset;
	[Group( "Texture Coordinates" )] float2 TexCoordScrollSpeed;

	// Normal and roughness: complex and simple pack a hemi-octahedral normal and roughness, generic a DXT5 normal
	[Group( "Normal" )] Texture2D Normal;
	[Group( "Normal" ), Default( 1 ), Range( 0, 1 )] float NormalMapScaleFactor;
	[Group( "Normal" ), Default( 1 ), Range( 0, 10 )] float BumpStrength;
	[Group( "Roughness" ), Default( 1 ), Range( 0, 2 )] float RoughnessScaleFactor;

	// Metalness
	[Group( "Metalness" ), Range( 0, 1 )] float Metalness;
	[Group( "Metalness" ), Source( "g_tMetalness" )] Texture2D MetalnessMap;

	// generic's specular: roughness from the anisotropic texture, metalness from the metalness/reflectance texture's red
	[Group( "Lighting" )] Texture2D Roughness;
	[Group( "Lighting" ), Default2( 0, 1 )] float2 GlossinessRange;
	[Group( "Lighting" )] Texture2D MetalnessReflectanceFresnel;
	[Group( "Lighting" ), Default2( 0, 1 )] float2 MetalnessRange;

	// Ambient occlusion
	[Group( "Ambient Occlusion" )] Texture2D AmbientOcclusion;
	[Group( "Ambient Occlusion" ), Range( 0, 1 )] float AmbientOcclusionDirectDiffuse;
	[Group( "Ambient Occlusion" ), Range( 0, 1 )] float AmbientOcclusionDirectSpecular;

	// Alpha test and translucency
	[Group( "Translucent" ), Default( 0.5 ), Range( 0.01, 0.99 )] float AlphaTestReference;
	[Group( "Translucent" ), Default( 1 ), Range( 0, 1 )] float AntiAliasedEdgeStrength;
	[Group( "Translucent" ), Default( 1 ), Range( 0, 1 )] float OpacityScale;

	// Fog (g_bFogEnabled)
	[Group( "Fog" ), Default( 1 )] bool FogEnabled;

	// Self-illumination: complex reads its mask as sRGB colour, generic as a linear mask
	[Group( "Self Illum" ), Srgb] Texture2D SelfIllumMask;
	[Group( "Self Illum" ), Source( "g_tSelfIllumMask" )] Texture2D SelfIllumMaskLinear;
	[Group( "Self Illum" ), Default3( 1, 1, 1 )] float3 SelfIllumTint;
	[Group( "Self Illum" ), Range( -10, 10 )] float SelfIllumBrightness;
	[Group( "Self Illum" ), Default( 1 ), Range( 0, 16 )] float SelfIllumScale;
	[Group( "Self Illum" )] float2 SelfIllumOffset;
	[Group( "Self Illum" )] float2 SelfIllumScrollSpeed;
	[Group( "Self Illum" ), Default( 1 ), Range( 0, 1 )] float SelfIllumAlbedoFactor;

	// Detail texture
	[Group( "Detail Texture" )] Texture2D Detail;
	[Group( "Detail Texture" )] Texture2D NormalDetail;
	[Group( "Detail Texture" )] Texture2D DetailMask;
	[Group( "Detail Texture" ), Default( 1 ), Range( 0, 1 )] float DetailBlendFactor;
	[Group( "Detail Texture" ), Range( 0, 1 )] float DetailBlendToFull;
	[Group( "Detail Texture" ), Default( 1 ), Range( 0, 1 )] float DetailNormalStrength;
	[Group( "Detail Texture" ), Default2( 1, 1 )] float2 DetailTexCoordScale;
	[Group( "Detail Texture" ), Range( 0, 360 )] float DetailTexCoordRotation;
	[Group( "Detail Texture" )] float2 DetailTexCoordOffset;

	// Transmission
	[Group( "Transmission" ), Srgb] Texture2D TransmissiveColor;

	/// vr_detail_texture.fxc's CalculateDetailTextureCoordinates: the detail uv from the mesh's, scaled, rotated and offset.
	float2 DetailUv( float2 uv )
	{
		float a = DetailTexCoordRotation * 3.14159265359 / 180.0;
		float c = cos( a );
		float s = sin( a );
		float4 xform = DetailTexCoordScale.xxyy * float4( c, -s, s, c );
		float2 offset = -0.5 * DetailTexCoordScale * float2( c - s, s + c ) + DetailTexCoordOffset + 0.5;
		return float2( dot( xform.xy, uv ), dot( xform.zw, uv ) ) + offset;
	}

	/// vr_common.fxc's DecodeHemiOctahedronNormal: a tangent-space normal from a hemi-octahedral texel's rg.
	static float3 HemiOctahedron( float2 rg )
	{
		float2 d = float2( rg.x + rg.y - 1.00392163, rg.x - rg.y );
		return normalize( float3( d, 1.0 - abs( d.x ) - abs( d.y ) ) );
	}

	/// vr_detail_texture.fxc's PhotoshopOverlay.
	static float3 Overlay( float3 albedo, float3 detail, float mask )
	{
		detail *= 1.992156862745098 * 0.5;
		float3 gamma = pow( albedo, 1.0 / 2.2 );
		float3 screen = 1.0 - 2.0 * ( ( 1.0 - gamma ) * ( 1.0 - detail ) );
		float3 multiply = 2.0 * gamma * detail;
		float3 overlay = lerp( screen, multiply, step( gamma, float3( 0.5, 0.5, 0.5 ) ) );
		return lerp( albedo, pow( overlay, 2.2 ), mask );
	}

	public Material Evaluate( SurfaceInput i )
	{
		if ( Packing == 2 ) return Generic( i );

		// The vertex stage's work (vr_shared_standard_vs_code.fxc): uv scale, offset and scroll; object tint and fade
		float2 uv = i.Uv * TexCoordScale + TexCoordOffset + TexCoordScrollSpeed * i.Time;
		float3 tint = lerp( float3( 1, 1, 1 ), i.Color.rgb, ModelTintAmount ) * SrgbGammaToLinear( ColorTint );
		float fade = pow( i.Color.a, FadeExponent );

		Material m = Material.Init( i );
		float4 color = i.Sample( Color, uv );

		// Opacity: the colour's alpha with alpha test, also scaled when translucent; else the vertex fade
		m.Opacity = UseAlphaTest ? color.a * fade : fade;
		if ( UseTranslucent ) m.Opacity = fade * color.a * OpacityScale;
		m.Additive = UseAdditiveBlend;
		m.Fog = FogEnabled;
		m.AlphaTestReference = AlphaTestReference;
		m.AntiAliasedEdgeStrength = AntiAliasedEdgeStrength;
		m.OpacityUvScale = TexCoordScale;

		// Albedo, tinted where the tint mask says
		float3 albedo = color.rgb * tint;
		if ( UseTintMask ) albedo = lerp( color.rgb, color.rgb * tint, i.Sample( TintMask, uv ).r );

		// Self-illumination
		if ( UseSelfIllum )
		{
			float2 selfIllumUv = SelfIllumOffset + uv + frac( i.Time * SelfIllumScrollSpeed );
			float3 scaled = pow( 2.0, SelfIllumBrightness ).xxx * saturate( SelfIllumScale ) * SrgbGammaToLinear( SelfIllumTint );
			float3 emission = i.Sample( SelfIllumMask, selfIllumUv, g_StandardTrilinear ).rgb * scaled;
			m.Emission = lerp( emission, emission * albedo, SelfIllumAlbedoFactor );
		}

		// Occlusion: its own texture; simple's is the colour's alpha unless that holds metalness
		if ( Packing == 1 && !UseMetalnessTexture ) m.AmbientOcclusion = color.a;
		else m.AmbientOcclusion = i.Sample( AmbientOcclusion, uv, g_StandardTrilinear ).r;

		// How much it darkens direct light: complex's values; simple leaves the combiner's defaults
		if ( Packing == 0 )
		{
			m.AmbientOcclusionDirectDiffuse = AmbientOcclusionDirectDiffuse;
			m.AmbientOcclusionDirectSpecular = AmbientOcclusionDirectSpecular;
		}

		// Metalness: a value, or with the feature the colour's alpha (the metalness texture's green when the alpha is opacity)
		m.Metalness = Metalness;
		if ( UseMetalnessTexture ) m.Metalness = UseAlphaTest || UseTranslucent ? i.Sample( MetalnessMap, uv, g_StandardTrilinear ).g : color.a;

		float detailMask = 0;
		if ( DetailMode != 0 ) detailMask = max( i.Sample( DetailMask, uv ).r, DetailBlendToFull ) * DetailBlendFactor;

		// Normal and roughness: hemi-octahedral normal in rg, roughness in b
		float4 normal = i.Sample( Normal, uv );
		float3 normalTs = HemiOctahedron( normal.rg );
		m.Roughness = pow( normal.b, RoughnessScaleFactor );
		if ( UseNormalMapScale ) normalTs = normalize( lerp( float3( 0, 0, 1 ), normalTs, NormalMapScaleFactor ) );

		float2 detailUv = DetailUv( i.Uv );
		if ( DetailMode == 3 || DetailMode == 4 )
		{
			// vr_detail_texture.fxc's ApplyDetailNormal
			float3 detailTs = HemiOctahedron( i.Sample( NormalDetail, detailUv ).rg );
			detailTs = float3( 0, 0, 1 - DetailNormalStrength ) + DetailNormalStrength * detailTs;
			detailTs = lerp( float3( 0, 0, 1 ), detailTs, detailMask );
			normalTs *= detailTs.z;
			normalTs.xy += normalTs.z * detailTs.xy;
			normalTs = normalize( normalTs );
		}

		m.Normal = TransformNormal( normalTs, i.Normal, i.TangentU, i.TangentV );

		if ( DetailMode == 1 || DetailMode == 2 || DetailMode == 4 )
		{
			float3 detail = i.Sample( Detail, detailUv ).rgb;
			if ( DetailMode == 1 ) albedo *= lerp( float3( 1, 1, 1 ), 1.992156862745098 * detail, detailMask );
			else albedo = Overlay( albedo, detail, detailMask );
		}

		m.Albedo = albedo;

		// Back-face transmission: the transmissive colour by the vertex tint, lit from behind
		if ( UseBackfaceTransmission )
		{
			m.Transmission = i.Sample( TransmissiveColor, uv, g_StandardTrilinear ).rgb * tint;
			m.BackfaceTransmission = true;
		}

		m.Unlit = UseUnlit;
		return m;
	}

	/// generic.shader's shading, which edits the combiner's input directly: with specular off the specular colour is
	/// zero (SpecularScale), and the self-illumination mask takes its share off the diffuse and specular colours
	/// (DiffuseScale, SpecularScale). Every texture is read at the mesh's uv with g_sDefault.
	Material Generic( SurfaceInput i )
	{
		Material m = Material.Init( i );
		float4 color = i.Sample( Color, i.Uv, g_StandardDefault );
		float3 albedo = color.rgb;

		// The tint and the object's colour where the tint mask says (green)
		float tintMask = UseTintMask ? i.Sample( TintMask, i.Uv, g_StandardDefault ).g : 1.0;
		albedo = lerp( albedo, albedo * ColorTint * i.Color.rgb, tintMask );

		// The normal: DXT5 (alpha and green), y flipped as the shader does before the combiner's own flip, bump strength
		float3 normalTs = ComputeNormalFromRGTexture( i.Sample( Normal, i.Uv, g_StandardDefault ).ag );
		normalTs.y = -normalTs.y;
		normalTs.xy *= BumpStrength;
		normalTs = normalize( normalTs );
		m.Normal = TransformNormal( normalTs, i.Normal, i.TangentU, i.TangentV );

		// Specular on: roughness and metalness from their textures in the material's ranges; off, no specular colour
		m.Roughness = 0;
		m.Metalness = 0;
		m.SpecularScale = 0;
		if ( UseSpecular )
		{
			float4 rough = i.Sample( Roughness, i.Uv, g_StandardDefault );
			float2 scaleBias = float2( max( 1.0 - GlossinessRange.x, 1.0 - GlossinessRange.y ) - ( 1.0 - GlossinessRange.y ), 1.0 - GlossinessRange.y );
			m.Roughness = min( 1.0, ( rough.b + rough.a ) * 0.5 * scaleBias.x + scaleBias.y + ( rough.r + rough.g ) * 0.5 );
			float metalness = i.Sample( MetalnessReflectanceFresnel, i.Uv, g_StandardDefault ).r;
			m.Metalness = metalness * ( max( MetalnessRange.x, MetalnessRange.y ) - MetalnessRange.x ) + MetalnessRange.x;
			m.SpecularScale = 1;
		}
		else
		{
			m.Specular = false;
		}

		// Opacity: the object's fade, translucent by the colour's alpha and the opacity scale. The alpha test is
		// generic.shader's hard cut of the colour's alpha at the material's cutoff (no alpha to coverage), as coverage
		m.Opacity = i.Color.a;
		if ( UseTranslucent ) m.Opacity *= color.a * OpacityScale;
		if ( UseAlphaTest ) m.Coverage = color.a < AlphaTestReference ? 0.0 : 1.0;
		m.Additive = UseAdditiveBlend;
		m.Fog = FogEnabled;

		// Self-illumination: the albedo lit by the mask (green), taken off the diffuse and specular colours there
		if ( UseSelfIllum )
		{
			float mask = i.Sample( SelfIllumMaskLinear, i.Uv, g_StandardDefault ).g;
			m.Emission = SelfIllumScale * SelfIllumTint * albedo * mask;
			m.DiffuseScale = 1.0 - mask;
			m.SpecularScale *= 1.0 - mask;
		}

		// Unlit: the albedo as it is (specular is off with it)
		m.Unlit = UseUnlit;
		m.Albedo = albedo;
		return m;
	}
}
