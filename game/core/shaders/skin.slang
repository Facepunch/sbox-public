// skin.shader written as a surface: colour with the object tint, tint mask and the per-object skin tint and age (the
// tint lookup, the age textures), the hemi-octahedral normal map with roughness and a blurred copy for subsurface
// scattering, bent normals, the combined masks (inside of the mouth, occlusion, opacity or tint or scattering mask), face
// occlusion, transmission, two-lobe specular and occlusion colour bleed. It lights through the final combiner with
// skin.shader's lighting features ([Combiner]), as skin.shader does.
//
// Wrinkles draw as skin.shader draws them today: its vertex stage never passes the wrinkle on, so they're always off.
// Per-vertex curvature (F_USE_PER_VERTEX_CURVATURE) isn't done: such materials stay on skin.shader.
import sbox.surface;

// What the material compiler makes of a .vmat's images (SlangMaterialCompiler): skin.shader's texture inputs,
// the textures made from them and its attributes, read with the material's features
#if MATERIAL_INPUTS
// The material editor's FEATURES lines, as the .shader declared them
Feature( F_DO_NOT_CAST_SHADOWS, 0..1, "Shadows" );
Feature( F_ALPHA_TEST, 0..1, "Translucent" );
Feature( F_TRANSLUCENT, 0..1( 0 = "Opaque", 1 = "Translucent" ), "Translucent" );
Feature( F_TINT_MASK, 0..1, "Per-Instance Tint Mask" );
Feature( F_USE_PER_VERTEX_CURVATURE, 0..1, "Skin" );
Feature( F_USE_BENT_NORMALS, 0..1, "Skin" );
Feature( F_USE_FACE_OCCLUSION_TEXTURE, 0..1, "Skin" );
Feature( F_SSS_MASK, 0..1, "Skin" );
Feature( F_AGE_TEXTURE, 0..1, "Skin" );
FeatureRule( Allow1( F_ALPHA_TEST, F_TRANSLUCENT, F_TINT_MASK, F_SSS_MASK ), "Translucent, Alpha Test, Tint Mask, and Sss Mask are not compatible" );
Feature( F_WRINKLE, 0..1, "Animation" );
// The material's parameters as the material editor shows them, with the feature states that use them
float3 g_vColorTint < UiType( Color ); Default3( 1, 1, 1 ); UiGroup( "Color" ); >;
float g_flModelTintAmount < Default( 1 ); Range( 0, 1 ); UiGroup( "Color" ); >;
float3 g_vTransmissionColor < UiType( Color ); Default3( 0.75, 0.23, 0.012 ); UiGroup( "Transmission" ); >;
float g_flAmbientOcclusionDirectDiffuse < Default( 1 ); Range( 0, 1 ); UiGroup( "Ambient Occlusion/20" ); >;
float g_flAmbientOcclusionDirectSpecular < Default( 1 ); Range( 0, 1 ); UiGroup( "Ambient Occlusion/30" ); >;
float g_flTransmissionFalloff < Default( 0.8 ); Range( 0, 4 ); UiGroup( "Transmission" ); >;
float g_flSecondSpecularLobeRatio < Default( 0.15 ); Range( 0, 1 ); UiGroup( "Specular" ); >;
float g_flSecondSpecularLobeGlossiness < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Specular" ); >;
float g_flCurvatureScale < Default( 1 ); Range( 0, 3 ); UiGroup( "Curvature" ); >;
float g_flRoughnessScale < Default( 1 ); Range( 0, 3 ); UiGroup( "Roughness,23/9" ); >;
float g_flDiffuseNormalBlur < Default( 0.5 ); Range( 0, 4 ); UiGroup( "Normal/30" ); >;
float g_flMouthInteriorBrightnessScale < Default( 1 ); Range( 0, 1 ); UiGroup( "Face Occlusion" ); >;
float g_flSkinAge < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Tinting/2" ); >;
float3 g_vAmbientNormalSoftness < UiType( Color ); Default3( 1, 0.3, 0 ); UiGroup( "Lookup Textures" ); >;
bool g_bOverrideTinting < Default( 0 ); UiGroup( "Tinting/0" ); >;
bool g_bFogEnabled < Default( 1 ); UiGroup( "Fog,300" ); >;
float g_flAlphaTestReference < Default( 0.5 ); Range( 0.01, 0.99 ); UiGroup( "Translucent" ); ActiveIf( F_ALPHA_TEST != 0 ); >;
float g_flAntiAliasedEdgeStrength < Default( 1 ); Range( 0, 1 ); UiGroup( "Translucent" ); ActiveIf( F_ALPHA_TEST != 0 ); >;
float g_flOpacityScale < Default( 1 ); Range( 0, 1 ); UiGroup( "Translucent" ); ActiveIf( F_TRANSLUCENT != 0 ); >;
float g_flSkinTint < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Tinting/1" ); ActiveIf( F_TINT_MASK != 0 ); >;
float g_flOcclusionWidth < Default( 3 ); Range( 0.1, 5 ); UiGroup( "Face Occlusion/20" ); ActiveIf( F_USE_FACE_OCCLUSION_TEXTURE != 0 ); >;
float g_flOcclusionStrength < Default( 0.75 ); Range( 0, 1 ); UiGroup( "Face Occlusion/30" ); ActiveIf( F_USE_FACE_OCCLUSION_TEXTURE != 0 ); >;
BoolAttribute( NeedsLightProbe, true );
// End of the material editor's lines
#define DISABLE_STATIC_LIGHTING 1
#define S_SUBSURFACE_SCATTERING SUBSURFACE_SCATTERING_PREINTEGRATED
#define S_TRANSMISSIVE 1
#define S_AMBIENT_OCCLUSION_COLOR_BLEED 1
#define S_POWER_SPECULAR_AMBIENT_OCCLUSION 1
#define S_TWO_LOBE_SPECULAR 1
#define S_USE_FACE_OCCLUSION_TEXTURE F_USE_FACE_OCCLUSION_TEXTURE
#include "vr_common_vs_code.fxc"
#define S_USE_PER_VERTEX_CURVATURE F_USE_PER_VERTEX_CURVATURE
#define S_WRINKLE F_WRINKLE
#define MINIMAL_MATERIAL
#define DEPTH_STATE_ALREADY_SET
#define BLEND_MODE_ALREADY_SET
#define S_ALPHA_TEST F_ALPHA_TEST
#define S_TRANSLUCENT F_TRANSLUCENT
#define S_TINT_MASK F_TINT_MASK
#define S_USE_PER_VERTEX_CURVATURE F_USE_PER_VERTEX_CURVATURE
#define S_USE_BENT_NORMALS F_USE_BENT_NORMALS
#define S_WRINKLE F_WRINKLE
#define S_WRINKLE F_WRINKLE
#define S_SSS_MASK F_SSS_MASK
#define S_AGE_TEXTURE F_AGE_TEXTURE
CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color", Default3( 0.86, 0.67, 0.62 ) );
CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal/10", Default3( 0.5, 0.5, 1.0 ) );
CreateInputTexture2D( TextureBentNormal, Linear, 8, "NormalizeNormals", "_bentnormal", "Normal/20", Default3( 0.5, 0.5, 1.0 ) );
CreateInputTexture2D( TextureRoughness, Linear, 8, "Inverse", "_rough", "Roughness,23/10", Default3( 0.5, 0.5, 0.5 ) );
CreateInputTexture2D( TextureAmbientOcclusion, Linear, 8, "", "_ao", "Ambient Occlusion,40/10", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureInsideMouth, Linear, 8, "", "_mask", "Face Occlusion", Default3AndFile( 0.0, 0.0, 0.0, "materials/default/default_black_mask.tga" ) );
CreateInputTexture2D( TextureTransmissiveMask, Linear, 8, "", "_trans", "Transmission", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureTranslucency, Linear, 8, "", "_trans", "Translucent,15", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureSelfIllumMask, Srgb, 8, "", "_selfillum", "Self Illum,60", Default3( 0.0, 0.0, 0.0 ) );
CreateInputTexture2D( TextureTintMask, Linear, 8, "", "_mask", "Color", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureSssMask, Linear, 8, "", "_mask", "Color", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureSquishColor, Srgb, 8, "", "_color", "Color/11", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureStretchColor, Srgb, 8, "", "_color", "Color/12", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureSquishNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal/21", Default3( 0.5, 0.5, 1.0 ) );
CreateInputTexture2D( TextureStretchNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal/22", Default3( 0.5, 0.5, 1.0 ) );
CreateInputTexture2D( TextureSquishAmbientOcclusion, Linear, 8, "", "_ao", "Ambient Occlusion/11", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureStretchAmbientOcclusion, Linear, 8, "", "_ao", "Ambient Occlusion/12", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureTintLookup, Linear, 8, "", "_color", "Tinting/3", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureAgeColor, Srgb, 8, "", "_color", "Color", Default3( 0.86, 0.67, 0.62 ) );
CreateInputTexture2D( TextureAgeNormal, Linear, 8, "NormalizeNormals", "_normal", "Age/10", Default3( 0.5, 0.5, 1.0 ) );
Texture2D g_tAgeNormal < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureAgeNormal, TextureRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); ActiveIf( S_AGE_TEXTURE != 0 ); >;
Texture2D g_tAgeColor < Channel( RGB, Box( TextureAgeColor ), Srgb ); OutputFormat( DXT1 ); SrgbRead( true ); ActiveIf( S_AGE_TEXTURE != 0 ); >;
#if ( S_TRANSMISSIVE )
Texture2D g_tColor <	Channel( RGB, Box( TextureColor ), Srgb );
Channel( A, Box( TextureTransmissiveMask ), Linear );
OutputFormat( BC7 );
SrgbRead( true ); >;
#else
Texture2D g_tColor <	Channel( RGB, Box( TextureColor ), Srgb );
OutputFormat( DXT1 );
SrgbRead( true ); >;
#endif
#include "vr_common_ps_code.fxc"
Texture2D g_tTintLookup < Channel( RGB, Box( TextureTintLookup ), Srgb );
OutputFormat( DXT1 );
SrgbRead( true ); >;
TextureAttribute( g_tTintLookup, g_tTintLookup );
Texture2D g_tNormal < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureNormal, TextureRoughness ), Linear );
OutputFormat( BC7 );
SrgbRead( false ); >;
Texture2D g_tBentNormal < Channel( RG, HemiOctIsoRoughness_RG_B( TextureBentNormal ), Linear );
OutputFormat( ATI2N );
SrgbRead( false ); ActiveIf( S_USE_BENT_NORMALS != 0 ); >;
Texture2D g_tCombinedMasks <
Channel( R, Box3( TextureInsideMouth ), Linear );
Channel( G, Box3( TextureAmbientOcclusion ), Linear );
#if ( S_ALPHA_TEST )
Channel( A, PreserveCoverage( TextureTranslucency ), Linear );
#elif(  S_TRANSLUCENT )
Channel( A, Box( TextureTranslucency ), Linear );
#elif( S_TINT_MASK )
Channel( A, Box( TextureTintMask ), Linear );
#elif ( S_SSS_MASK )
Channel( A, Box( TextureSssMask ), Linear );
#endif
OutputFormat( DXT5 );
SrgbRead( false );
Filter( TRILINEAR ); >;
#if ( ( S_TRANSLUCENT || S_ALPHA_TEST ) )
TextureAttribute( LightSim_Opacity_A, g_tColor );
#endif
#if ( S_USE_FACE_OCCLUSION_TEXTURE )
CreateInputTexture2D( TextureOcclusion, Linear, 8, "", "_color", "Face Occlusion/10", Default3( 1.0, 1.0, 1.0 ) );
Texture2D g_tOcclusion < Channel( RGB, Box( TextureOcclusion ), Linear ); OutputFormat( DXT1 ); SrgbRead( false ); >;
#endif
Texture2D g_tSquishColor < Channel( RGB, Box( TextureSquishColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); ActiveIf( F_WRINKLE != 0 ); >;
Texture2D g_tStretchColor < Channel( RGB, Box( TextureStretchColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); ActiveIf( F_WRINKLE != 0 ); >;
Texture2D g_tSquishNormal < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureSquishNormal, TextureRoughness ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); ActiveIf( F_WRINKLE != 0 ); >;
Texture2D g_tStretchNormal < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureStretchNormal, TextureRoughness ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); ActiveIf( F_WRINKLE != 0 ); >;
Texture2D g_tSquishAmbientOcclusion < Channel( R, Box3( TextureSquishAmbientOcclusion ), Linear ); OutputFormat( ATI1N ); SrgbRead( false ); ActiveIf( F_WRINKLE != 0 ); >;
Texture2D g_tStretchAmbientOcclusion < Channel( R, Box3( TextureStretchAmbientOcclusion ), Linear ); OutputFormat( ATI1N ); SrgbRead( false ); ActiveIf( F_WRINKLE != 0 ); >;
TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColor );
TextureAttribute( RepresentativeTexture, g_tColor );
BoolAttribute( SupportsMappingDimensions, true );
#if ( S_TRANSLUCENT )
BoolAttribute( translucent, true );
#endif
#if ( S_ALPHA_TEST )
BoolAttribute( alphatest, true );
#endif
BoolAttribute( transmissive, true );
BoolAttribute( DoNotCastShadows, F_DO_NOT_CAST_SHADOWS ? true : false );
BoolAttribute( ShadowFastPath, true );
#endif

// skin.shader's g_sTrilinearWrap (the combined masks) and g_sTrilinearClamp (the tint lookup and face occlusion); the
// rest use g_sAniso, the surfaces' standard filtering
SamplerState g_SkinTrilinearWrap < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;
SamplerState g_SkinTrilinearClamp < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;

// The object's skin tint and age (a dresser sets them on every renderer of a citizen), and the face occlusion
// projection, from the object's attributes
float g_flSkinTintAttribute < Attribute( "skin_tint" ); Default( 0.5 ); >;
float g_flSkinAgeAttribute < Attribute( "skin_age" ); Default( 0.5 ); >;
float4x4 g_matSkinFaceOcclusionWorldToTexture < Attribute( "EyeOcclusionViewProj" ); >;

// skin.shader's COMMON defines these after vr_common.fxc, so S_POWER_SPECULAR_AMBIENT_OCCLUSION never reaches
// PowerSpecularOcclusion and DISABLE_STATIC_LIGHTING nothing at all: both are left out to light as it does
[Material, Combiner( "S_SUBSURFACE_SCATTERING = SUBSURFACE_SCATTERING_PREINTEGRATED, S_TRANSMISSIVE, S_AMBIENT_OCCLUSION_COLOR_BLEED, S_TWO_LOBE_SPECULAR" )]
public struct Skin : ICombinerSurface
{
	// Features
	[Group( "Features" ), Source( "F_ALPHA_TEST" ), RenderState( "AlphaTest" )] bool UseAlphaTest;
	[Group( "Features" ), Source( "F_TRANSLUCENT" ), RenderState( "Blend: Alpha" )] bool UseTranslucent;
	[Group( "Features" ), Source( "F_TINT_MASK" )] bool UseTintMask;
	[Group( "Features" ), Source( "F_SSS_MASK" )] bool UseSssMask;
	[Group( "Features" ), Source( "F_AGE_TEXTURE" )] bool UseAgeTexture;
	[Group( "Features" ), Source( "F_USE_BENT_NORMALS" )] bool UseBentNormals;
	[Group( "Features" ), Source( "F_USE_FACE_OCCLUSION_TEXTURE" )] bool UseFaceOcclusion;

	// Features this surface doesn't do: materials that set them stay on skin.shader
	[Group( "Not converted" ), Source( "F_USE_PER_VERTEX_CURVATURE" ), Range( 0, 0 )] int PerVertexCurvature;

	// Colour (its alpha is the transmissive mask)
	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Color" ), Default3( 1, 1, 1 )] float3 ColorTint;
	[Group( "Color" ), Default( 1 ), Range( 0, 1 )] float ModelTintAmount;
	[Group( "Color" ), Srgb] Texture2D TintLookup;
	[Group( "Tinting" )] bool OverrideTinting;
	[Group( "Tinting" ), Default( 0.5 ), Range( 0, 1 )] float SkinTint;
	[Group( "Tinting" ), Default( 0.5 ), Range( 0, 1 )] float SkinAge;
	[Group( "Age" ), Srgb] Texture2D AgeColor;
	[Group( "Age" )] Texture2D AgeNormal;

	// Normal and roughness
	[Group( "Normal" )] Texture2D Normal;
	[Group( "Normal" )] Texture2D BentNormal;
	[Group( "Normal" ), Default( 0.5 ), Range( 0, 4 )] float DiffuseNormalBlur;
	[Group( "Roughness" ), Default( 1 ), Range( 0, 3 )] float RoughnessScale;
	[Group( "Specular" ), Default( 0.15 ), Range( 0, 1 )] float SecondSpecularLobeRatio;
	[Group( "Specular" ), Default( 0.5 ), Range( 0, 1 )] float SecondSpecularLobeGlossiness;
	[Group( "Curvature" ), Default( 1 ), Range( 0, 3 )] float CurvatureScale;
	[Group( "Lookup Textures" )] Texture2D DiffuseFalloff;

	// Masks: r inside of the mouth, g occlusion, a opacity, tint mask or scattering mask
	[Group( "Masks" )] Texture2D CombinedMasks;

	// Ambient occlusion
	[Group( "Ambient Occlusion" ), Default3( 0.4, 0.15, 0.13 )] float3 AmbientOcclusionColorBleed;
	[Group( "Ambient Occlusion" ), Default( 1 ), Range( 0, 1 )] float AmbientOcclusionDirectDiffuse;
	[Group( "Ambient Occlusion" ), Default( 1 ), Range( 0, 1 )] float AmbientOcclusionDirectSpecular;

	// Transmission
	[Group( "Transmission" ), Default3( 0.75, 0.23, 0.012 )] float3 TransmissionColor;
	[Group( "Transmission" ), Default( 0.8 ), Range( 0, 4 )] float TransmissionFalloff;

	// Face occlusion
	[Group( "Face Occlusion" )] Texture2D Occlusion;
	[Group( "Face Occlusion" ), Default( 1 ), Range( 0, 1 )] float MouthInteriorBrightnessScale;
	[Group( "Face Occlusion" ), Default( 3 ), Range( 0.1, 5 )] float OcclusionWidth;
	[Group( "Face Occlusion" ), Default( 0.75 ), Range( 0, 1 )] float OcclusionStrength;

	// Alpha test and translucency
	[Group( "Translucent" ), Default( 0.5 ), Range( 0.01, 0.99 )] float AlphaTestReference;
	[Group( "Translucent" ), Default( 1 ), Range( 0, 1 )] float AntiAliasedEdgeStrength;
	[Group( "Translucent" ), Default( 1 ), Range( 0, 1 )] float OpacityScale;

	/// vr_common.fxc's DecodeHemiOctahedronNormal, with the tools' y flip skin.shader makes.
	static float3 HemiOctahedron( float2 rg )
	{
		float2 d = float2( rg.x + rg.y - 1.00392163, rg.x - rg.y );
		float3 n = normalize( float3( d, 1.0 - abs( d.x ) - abs( d.y ) ) );
		n.y = -n.y;
		return n;
	}

	public Material Evaluate( SurfaceInput i )
	{
		// The vertex stage's tint
		float3 tint = lerp( float3( 1, 1, 1 ), i.Color.rgb, ModelTintAmount ) * SrgbGammaToLinear( ColorTint );
		float age = OverrideTinting ? SkinAge : g_flSkinAgeAttribute;

		float4 masks = i.Sample( CombinedMasks, i.Uv, g_SkinTrilinearWrap );
		float3 tintLookup = i.Sample( TintLookup, float2( OverrideTinting ? SkinTint : g_flSkinTintAttribute, age ), g_SkinTrilinearClamp ).rgb;
		float4 color = i.Sample( Color, i.Uv );

		// The normal, and a blurred one for scattering and diffuse bent normals
		float diffuseMip = max( i.MipLevel( Normal ), DiffuseNormalBlur );
		float4 normal = i.Sample( Normal, i.Uv );
		float4 diffuseNormal = i.SampleLevel( Normal, i.Uv, diffuseMip );
		if ( UseAgeTexture )
		{
			normal.rg = lerp( normal.rg, i.Sample( AgeNormal, i.Uv ).rg, age );
			color.rgb = lerp( color.rgb, i.Sample( AgeColor, i.Uv ).rgb, age );
		}

		Material m = Material.Init( i );
		m.Normal = i.ToWorld( HemiOctahedron( normal.rg ) );
		m.SssNormal = i.ToWorld( HemiOctahedron( diffuseNormal.rg ) );
		if ( UseBentNormals )
		{
			m.UseBentNormals = true;
			m.BentNormal = i.ToWorld( HemiOctahedron( i.Sample( BentNormal, i.Uv ).rg ) );
			m.BentDiffuseNormal = m.SssNormal;
		}

		m.SssCurvatureScale = CurvatureScale;
		m.SssFalloff = DiffuseFalloff;
		if ( UseSssMask )
		{
			m.SssMask = masks.a;
			m.SssNormal = normalize( lerp( m.Normal, m.SssNormal, masks.a ) );
		}

		// Opacity: the vertex alpha, by the mask when translucent or alpha-tested
		m.Opacity = i.Color.a;
		if ( UseTranslucent ) m.Opacity *= OpacityScale * masks.a;
		if ( UseAlphaTest ) m.Opacity *= masks.a;
		m.AlphaTestReference = AlphaTestReference;
		m.AntiAliasedEdgeStrength = AntiAliasedEdgeStrength;

		m.Albedo = UseTintMask ? lerp( color.rgb, color.rgb * tint * tintLookup, masks.a ) : color.rgb * tint;
		m.Metalness = 0;

		// Roughness, and the second, glossier lobe
		m.Roughness = saturate( normal.b * RoughnessScale );
		m.SecondLobeRoughness = 1.0 - SecondSpecularLobeGlossiness;
		m.TwoLobeSpecularRatio = SecondSpecularLobeRatio;

		// Occlusion, bleeding colour where the skin scatters
		m.AmbientOcclusion = masks.g;
		m.AmbientOcclusionColorBleed = lerp( float3( 1, 1, 1 ), float3( 1, 1, 1 ) - SrgbGammaToLinear( AmbientOcclusionColorBleed ), m.SssMask );
		m.AmbientOcclusionDirectDiffuse = AmbientOcclusionDirectDiffuse;
		m.AmbientOcclusionDirectSpecular = AmbientOcclusionDirectSpecular;

		// Transmission through thin parts, by the colour's alpha
		m.Transmission = color.aaa * SrgbGammaToLinear( TransmissionColor );
		m.TransmissiveFalloff = TransmissionFalloff.xxx;

		// Inside the mouth: dimmed, and by the face occlusion texture projected from the object's attribute (an affine
		// projection: the same per pixel as interpolated from the vertices)
		float lighting = MouthInteriorBrightnessScale;
		if ( UseFaceOcclusion )
		{
			float2 occlusionUv = mul( g_matSkinFaceOcclusionWorldToTexture, float4( i.Position, 1.0 ) ).xy * float2( 0.5, -0.5 ) + float2( 0.5, 0.5 );
			float occlusion = i.Sample( Occlusion, occlusionUv, g_SkinTrilinearClamp ).r;
			lighting *= pow( occlusion, OcclusionWidth ) * OcclusionStrength + ( 1.0 - OcclusionStrength );
		}

		lighting = lerp( 1.0, lighting, masks.r );
		m.Transmission *= lighting;
		m.AmbientOcclusion *= lighting;
		return m;
	}
}
