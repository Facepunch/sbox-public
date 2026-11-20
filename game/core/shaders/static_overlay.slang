// static_overlay.shader written as a surface: a map's overlays (decals baked into the world), complex.shader's standard
// processing without specular, tint mask, metalness or detail textures. It writes no depth and is pulled towards the
// camera; F_BLEND_MODE picks how it blends (render state, as the .shader's modes), and mod2x, multiply and mod-then-add take
// the fog as overlays (Fog::ApplyOverlay) and fade towards their neutral colour by opacity after lighting
// (Material.OverlayMode). Unlit unless F_LIT. It lights through the final combiner (ICombinerSurface).
import sbox.surface;

// What the material compiler makes of a .vmat's images (SlangMaterialCompiler): static_overlay.shader's texture inputs,
// the textures made from them and its attributes, read with the material's features
#if MATERIAL_INPUTS
// The material editor's FEATURES lines, as the .shader declared them
#include "vr_common_features.fxc"
Feature( F_LIT, 0..1, "Lit" );
Feature( F_BLEND_MODE, 0..6(0="Opaque", 1="Translucent", 2="Alpha Test", 3="Mod2x", 4="Additive", 5="Multiply", 6="ModThenAdd" ), "Blend Mode" );
// The material's parameters as the material editor shows them, with the feature states that use them
float2 g_vTexCoordScale < Default2( 1, 1 ); Range2( 0, 0, 100, 100 ); UiGroup( "Texture Coordinates,80/10" ); >;
float2 g_vTexCoordOffset < Default2( 0, 0 ); Range2( -1, -1, 1, 1 ); UiGroup( "Texture Coordinates/20" ); >;
float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); Range2( -10, -10, 10, 10 ); UiGroup( "Texture Coordinates/30" ); >;
float3 g_vColorTint < UiType( Color ); Default3( 1, 1, 1 ); UiGroup( "Color,10/20" ); >;
float g_flModelTintAmount < Default( 1 ); Range( 0, 1 ); UiGroup( "Color,10/30" ); >;
float g_flFadeExponent < Default( 1 ); Range( 1, 16 ); UiGroup( "Fade,400/10" ); >;
float g_flRoughnessScaleFactor < Default( 1 ); Range( 0, 2 ); UiGroup( "Roughness" ); >;
float g_flMetalness < Default( 0 ); Range( 0, 1 ); UiGroup( "Metalness,27/20" ); >;
bool g_bFogEnabled < Default( 1 ); UiGroup( "Fog,300" ); >;
float g_flOpacityScale < Default( 1 ); Range( 0, 1 ); UiGroup( "Translucent" ); ActiveIf( F_BLEND_MODE != 0 && F_BLEND_MODE != 2 ); >;
float g_flAlphaTestReference < Default( 0.5 ); Range( 0.01, 0.99 ); UiGroup( "Translucent" ); ActiveIf( F_BLEND_MODE == 2 ); >;
float g_flAntiAliasedEdgeStrength < Default( 1 ); Range( 0, 1 ); UiGroup( "Translucent" ); ActiveIf( F_BLEND_MODE == 2 ); >;
float3 g_vSelfIllumTint < UiType( Color ); Default3( 1, 1, 1 ); UiGroup( "Self Illum/20" ); ActiveIf( F_BLEND_MODE == 6 ); >;
float g_flSelfIllumBrightness < Default( 0 ); Range( -10, 10 ); UiGroup( "Self Illum" ); ActiveIf( F_BLEND_MODE == 6 ); >;
float g_flSelfIllumScale < Default( 1 ); Range( 0, 16 ); UiGroup( "Self Illum/30" ); ActiveIf( F_BLEND_MODE == 6 ); >;
float2 g_vSelfIllumOffset < Default2( 0, 0 ); UiGroup( "Self Illum/35" ); ActiveIf( F_BLEND_MODE == 6 ); >;
float2 g_vSelfIllumScrollSpeed < Default2( 0, 0 ); Range2( -10, -10, 10, 10 ); UiGroup( "Self Illum/40" ); ActiveIf( F_BLEND_MODE == 6 ); >;
float g_flSelfIllumAlbedoFactor < Default( 1 ); Range( 0, 1 ); UiGroup( "Self Illum/50" ); ActiveIf( F_BLEND_MODE == 6 ); >;
#if ( F_LIT )
BoolAttribute( NeedsLightProbe, true );
#endif
// End of the material editor's lines
#define ENABLE_NORMAL_MAPS
#define S_SPECULAR 0
#define S_METALNESS_TEXTURE 0
#define S_SPECULAR_CUBE_MAP 0
#define S_RENDER_BACKFACES 0
#define S_DO_NOT_CAST_SHADOWS 1
#include "vr_shared_standard_vs_code.fxc"
#define S_LIT F_LIT
#define S_BLEND_MODE F_BLEND_MODE
BoolAttribute( SupportsLightmapping, true );
BoolAttribute( PerVertexLighting, true );
BoolAttribute( AllowPerDrawCulling, true );
BoolAttribute( overlay, true );
#define BLEND_MODE_ALREADY_SET
#define DEPTH_STATE_ALREADY_SET
#define S_ALPHA_TEST ( S_BLEND_MODE == 2 )
#if ( S_LIT == 0 )
#define S_UNLIT 1
#endif
#if ( S_BLEND_MODE == 1 )
#define S_TRANSLUCENT 1
#elif ( S_ALPHA_TEST )
BoolAttribute( NoZPrepass, true );
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
#if ( S_BLEND_MODE == 3 )
#elif ( S_BLEND_MODE == 5 )
#elif( S_BLEND_MODE == 6 )
#endif
#endif

// static_overlay.shader's g_sTrilinearWrap, for occlusion and self-illumination (the rest use g_sAniso)
SamplerState g_OverlayTrilinear < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;

[Material, NoDepthWrite, DepthBias( 250, 0.5 )]
public struct StaticOverlay : ICombinerSurface
{
	// Features
	[Group( "Features" ), Source( "F_LIT" )] bool Lit;
	[Group( "Features" ), Source( "F_BLEND_MODE" ), Range( 0, 6 ), RenderState( "Blend: Opaque, Alpha, Opaque, Mod2x, Additive, Multiply, ModulateAdd; AlphaTest: false, false, true" )]
	int BlendMode;   // 0 opaque, 1 translucent, 2 alpha test, 3 mod2x, 4 additive, 5 multiply, 6 mod then add

	// Colour: sRGB, but read linear in mod2x (the .shader's colour texture there)
	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Color" ), Source( "g_tColor" )] Texture2D ColorLinear;
	[Group( "Color" ), Default3( 1, 1, 1 )] float3 ColorTint;
	[Group( "Color" ), Default( 1 ), Range( 0, 1 )] float ModelTintAmount;
	[Group( "Color" ), Default( 1 ), Range( 1, 16 )] float FadeExponent;

	// Texture coordinates
	[Group( "Texture Coordinates" ), Default2( 1, 1 )] float2 TexCoordScale;
	[Group( "Texture Coordinates" )] float2 TexCoordOffset;
	[Group( "Texture Coordinates" )] float2 TexCoordScrollSpeed;

	// Normal and roughness
	[Group( "Normal" )] Texture2D Normal;
	[Group( "Roughness" ), Default( 1 ), Range( 0, 2 )] float RoughnessScaleFactor;

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

	// Self-illumination (mod then add)
	[Group( "Self Illum" ), Srgb] Texture2D SelfIllumMask;
	[Group( "Self Illum" ), Default3( 1, 1, 1 )] float3 SelfIllumTint;
	[Group( "Self Illum" ), Range( -10, 10 )] float SelfIllumBrightness;
	[Group( "Self Illum" ), Default( 1 ), Range( 0, 16 )] float SelfIllumScale;
	[Group( "Self Illum" )] float2 SelfIllumOffset;
	[Group( "Self Illum" )] float2 SelfIllumScrollSpeed;
	[Group( "Self Illum" ), Default( 1 ), Range( 0, 1 )] float SelfIllumAlbedoFactor;

	/// vr_common.fxc's DecodeHemiOctahedronNormal: a tangent-space normal from a hemi-octahedral texel's rg.
	static float3 HemiOctahedron( float2 rg )
	{
		float2 d = float2( rg.x + rg.y - 1.00392163, rg.x - rg.y );
		return normalize( float3( d, 1.0 - abs( d.x ) - abs( d.y ) ) );
	}

	public Material Evaluate( SurfaceInput i )
	{
		// The vertex stage's work (vr_shared_standard_vs_code.fxc): uv scale, offset and scroll; object tint and fade
		float2 uv = i.Uv * TexCoordScale + TexCoordOffset + TexCoordScrollSpeed * i.Time;
		float3 tint = lerp( float3( 1, 1, 1 ), i.Color.rgb, ModelTintAmount ) * SrgbGammaToLinear( ColorTint );
		float fade = pow( i.Color.a, FadeExponent );
		bool alphaTest = BlendMode == 2;
		bool translucent = BlendMode == 1 || BlendMode >= 3;

		Material m = Material.Init( i );
		float4 color = BlendMode == 3 ? i.Sample( ColorLinear, uv ) : i.Sample( Color, uv );

		// Opacity: the colour's alpha when alpha-tested or blended (scaled), else the vertex fade
		m.Opacity = alphaTest ? color.a * fade : translucent ? fade * color.a * OpacityScale : fade;
		m.AlphaTestReference = AlphaTestReference;
		m.AntiAliasedEdgeStrength = AntiAliasedEdgeStrength;
		m.OpacityUvScale = TexCoordScale;

		float3 albedo = color.rgb * tint;

		// Self-illumination: mod then add's
		if ( BlendMode == 6 )
		{
			float2 selfIllumUv = SelfIllumOffset + uv + frac( i.Time * SelfIllumScrollSpeed );
			float3 scaled = pow( 2.0, SelfIllumBrightness ).xxx * saturate( SelfIllumScale ) * SrgbGammaToLinear( SelfIllumTint );
			float3 emission = i.Sample( SelfIllumMask, selfIllumUv, g_OverlayTrilinear ).rgb * scaled;
			m.Emission = lerp( emission, emission * albedo, SelfIllumAlbedoFactor );
		}

		m.AmbientOcclusion = i.Sample( AmbientOcclusion, uv, g_OverlayTrilinear ).r;
		m.AmbientOcclusionDirectDiffuse = AmbientOcclusionDirectDiffuse;
		m.AmbientOcclusionDirectSpecular = AmbientOcclusionDirectSpecular;

		// Normal and roughness: hemi-octahedral normal in rg, roughness in b
		float4 normal = i.Sample( Normal, uv );
		m.Roughness = pow( normal.b, RoughnessScaleFactor );
		m.Normal = TransformNormal( HemiOctahedron( normal.rg ), i.Normal, i.TangentU, i.TangentV );

		// No specular (S_SPECULAR 0) or metalness
		m.Metalness = 0;
		m.SpecularScale = 0;
		m.Specular = false;
		m.Albedo = albedo;
		m.Unlit = !Lit;
		m.Additive = BlendMode == 4 || BlendMode == 6;
		m.Fog = FogEnabled;
		m.OverlayMode = BlendMode == 3 || BlendMode == 5 || BlendMode == 6 ? BlendMode : 0;
		return m;
	}
}
