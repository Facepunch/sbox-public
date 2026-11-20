// vertex_color.shader written as a surface: the colour texture by the mesh's vertex colour (sRGB, by the object's tint),
// metalness as a value and ambient occlusion, lit through the final combiner as vertex_color.shader is
// (vr_shared_standard_ps_code.fxc's standard processing without normal maps: the geometric normal, roughness 0). The
// vertex colour is the surface's Varyings, read from the mesh and interpolated to Evaluate.
import sbox.surface;

// What the material compiler makes of a .vmat's images (SlangMaterialCompiler): vertex_color.shader's texture inputs,
// the textures made from them and its attributes, read with the material's features
#if MATERIAL_INPUTS
// The material editor's FEATURES lines, as the .shader declared them
#include "vr_common_features.fxc"
// The material's parameters as the material editor shows them, with the feature states that use them
float2 g_vTexCoordScale < Default2( 1, 1 ); Range2( 0, 0, 100, 100 ); UiGroup( "Texture Coordinates,80/10" ); >;
float2 g_vTexCoordOffset < Default2( 0, 0 ); Range2( -1, -1, 1, 1 ); UiGroup( "Texture Coordinates/20" ); >;
float2 g_vTexCoordScrollSpeed < Default2( 0, 0 ); Range2( -10, -10, 10, 10 ); UiGroup( "Texture Coordinates/30" ); >;
float3 g_vColorTint < UiType( Color ); Default3( 1, 1, 1 ); UiGroup( "Color,10/20" ); >;
float g_flModelTintAmount < Default( 1 ); Range( 0, 1 ); UiGroup( "Color,10/30" ); >;
float g_flFadeExponent < Default( 1 ); Range( 1, 16 ); UiGroup( "Fade,400/10" ); >;
float g_flMetalness < Default( 0 ); Range( 0, 1 ); UiGroup( "Metalness,27/20" ); >;
bool g_bFogEnabled < Default( 1 ); UiGroup( "Fog,300" ); >;
BoolAttribute( NeedsLightProbe, true );
// End of the material editor's lines
#define BLEND_MODE_ALREADY_SET
#include "vr_shared_standard_vs_code.fxc"
// What vr_shared_standard_ps_code.fxc (Valve's, which Slang shaders don't include) declared for vertex_color's features
#include "vr_common_ps_code.fxc"
CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color/10", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureTranslucency, Linear, 8, "", "_trans", "Translucent,15", Default3( 1.0, 1.0, 1.0 ) );
Texture2D g_tColor2 < Channel( RGB, Box( TextureColor ), Srgb ); OutputFormat( DXT1 ); SrgbRead( true ); >;
CreateInputTexture2D( TextureAmbientOcclusion, Linear, 8, "", "_ao", "Ambient Occlusion,40/10", Default3( 1.0, 1.0, 1.0 ) );
Texture2D g_tAmbientOcclusion < Channel( R, Box3( TextureAmbientOcclusion ), Linear ); OutputFormat( ATI1N ); SrgbRead( false ); >;
BoolAttribute( DoNotCastShadows, F_DO_NOT_CAST_SHADOWS ? true : false );
BoolAttribute( SupportsMappingDimensions, true );
TextureAttribute( RepresentativeTexture, g_tColor2 );
BoolAttribute( renderbackfaces, F_RENDER_BACKFACES ? true : false );
BoolAttribute( ShadowFastPath, true );
#endif

// g_sTrilinearWrap, for occlusion
SamplerState g_VertexColorTrilinear < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;

public struct VertexColorPaint
{
	public float4 Color : COLOR0;
}

[Material]
public struct VertexColor : IInterpolatedCombinerSurface
{
	typealias Varyings = VertexColorPaint;

	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Color" ), Default( 1 ), Range( 1, 16 )] float FadeExponent;
	[Group( "Texture Coordinates" ), Default2( 1, 1 )] float2 TexCoordScale;
	[Group( "Texture Coordinates" )] float2 TexCoordOffset;
	[Group( "Texture Coordinates" )] float2 TexCoordScrollSpeed;
	[Group( "Metalness" ), Range( 0, 1 )] float Metalness;
	[Group( "Ambient Occlusion" )] Texture2D AmbientOcclusion;

	/// The vertex stage's colour: linear, as the shader converts it per vertex.
	public VertexColorPaint Vertex( VertexColorPaint v )
	{
		v.Color.rgb = SrgbGammaToLinear( v.Color.rgb );
		return v;
	}

	public Material Evaluate( SurfaceInput i, VertexColorPaint v )
	{
		float2 uv = i.Uv * TexCoordScale + TexCoordOffset + TexCoordScrollSpeed * i.Time;
		Material m = Material.Init( i );
		m.Albedo = i.Sample( Color, uv ).rgb * v.Color.rgb * i.Color.rgb;
		m.Opacity = pow( i.Color.a, FadeExponent );
		m.Roughness = 0;
		m.Metalness = Metalness;
		m.AmbientOcclusion = i.Sample( AmbientOcclusion, uv, g_VertexColorTrilinear ).r;
		return m;
	}
}
