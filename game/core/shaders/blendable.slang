// blendable.shader written as a surface: up to four layers, blended by the mesh's vertex paint. The paint (blend weights
// and tint) is the surface's Varyings, read from the mesh and interpolated to Evaluate; the shader's features are
// fields, [Source] naming the feature a .vmat sets them with: ExtraLayers and TintMasksInVertexPaint, read by uniform
// branches. Version is the material's ShaderVersion: 1 packs roughness into the normal map, beside a hemi-octahedral
// normal.
import sbox.surface;

// What the material compiler makes of a .vmat's images (SlangMaterialCompiler): blendable.shader's texture inputs,
// the textures made from them and its attributes, read with the material's features
#if MATERIAL_INPUTS
#define S_MULTIBLEND F_MULTIBLEND
#include "common/vertex.hlsl"
BoolAttribute( VertexPaintUI2Layer, F_MULTIBLEND == 1 );
BoolAttribute( VertexPaintUI3Layer, F_MULTIBLEND == 2 );
BoolAttribute( VertexPaintUI4Layer, F_MULTIBLEND == 3 );
BoolAttribute( VertexPaintUI5Layer, F_MULTIBLEND == 4 );
BoolAttribute( VertexPaintUIPickColor, true );
BoolAttribute( ShadowFastPath, true );
#define S_MULTIBLEND F_MULTIBLEND
#define S_USE_TINT_MASKS_IN_VERTEX_PAINT F_USE_TINT_MASKS_IN_VERTEX_PAINT
BoolAttribute( SupportsMappingDimensions, true );
#include "common/pixel.hlsl"
CreateInputTexture2D( TextureColorA,            Srgb,   8, "",                 "_color",  "Material A,10/10", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureNormalA,           Linear, 8, "NormalizeNormals", "_normal", "Material A,10/20", Default3( 0.5, 0.5, 1.0 ) );
CreateInputTexture2D( TextureRoughnessA,        Linear, 8, "Inverse",          "_rough",  "Material A,10/30", Default3( 0.5, 0.5, 0.5 ) );
CreateInputTexture2D( TextureMetalnessA,        Linear, 8, "",                 "_metal",  "Material A,10/40", Default( 1.0 ) );
CreateInputTexture2D( TextureAmbientOcclusionA, Linear, 8, "",                 "_ao",     "Material A,10/50", Default( 1.0 ) );
CreateInputTexture2D( TextureBlendMaskA,        Linear, 8, "",                 "_blend",  "Material A,10/60", Default( 1.0 ) );
CreateInputTexture2D( TextureTintMaskA,         Linear, 8, "",                 "_tint",   "Material A,10/70", Default( 1.0 ) );
Texture2D g_tColorA  < Channel( RGB,  Box( TextureColorA ), Srgb ); Channel( A, Box( TextureTintMaskA ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
Texture2D g_tNormalA < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureNormalA, TextureRoughnessA ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); > ;
Texture2D g_tRmaA < Channel( R, Box( TextureMetalnessA ), Linear ); Channel( G, Box( TextureAmbientOcclusionA ), Linear ); Channel( B, Box( TextureBlendMaskA ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
TextureAttribute( LightSim_DiffuseAlbedoTexture, g_tColorA );
TextureAttribute( RepresentativeTexture, g_tColorA );
#if S_MULTIBLEND >= 1
CreateInputTexture2D( TextureColorB,            Srgb,   8, "",                 "_color",  "Material B,10/10", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureNormalB,           Linear, 8, "NormalizeNormals", "_normal", "Material B,10/20", Default3( 0.5, 0.5, 1.0 ) );
CreateInputTexture2D( TextureRoughnessB,        Linear, 8, "Inverse",          "_rough",  "Material B,10/30", Default3( 0.5, 0.5, 0.5 ) );
CreateInputTexture2D( TextureMetalnessB,        Linear, 8, "",                 "_metal",  "Material B,10/40", Default( 1.0 ) );
CreateInputTexture2D( TextureAmbientOcclusionB, Linear, 8, "",                 "_ao",     "Material B,10/50", Default( 1.0 ) );
CreateInputTexture2D( TextureBlendMaskB,        Linear, 8, "",                 "_blend",  "Material B,10/60", Default( 1.0 ) );
CreateInputTexture2D( TextureTintMaskB,         Linear, 8, "",                 "_tint",   "Material B,10/70", Default( 1.0 ) );
Texture2D g_tColorB  < Channel( RGB,  Box( TextureColorB ), Srgb ); Channel( A, Box( TextureTintMaskB ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
Texture2D g_tNormalB < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureNormalB, TextureRoughnessB ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); > ;
Texture2D g_tRmaB < Channel( R, Box( TextureMetalnessB ), Linear ); Channel( G, Box( TextureAmbientOcclusionB ), Linear ); Channel( B, Box( TextureBlendMaskB ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
#if S_MULTIBLEND >= 2
CreateInputTexture2D( TextureColorC,            Srgb,   8, "",                 "_color",  "Material C,10/10", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureNormalC,           Linear, 8, "NormalizeNormals", "_normal", "Material C,10/20", Default3( 0.5, 0.5, 1.0 ) );
CreateInputTexture2D( TextureRoughnessC,        Linear, 8, "Inverse",          "_rough",  "Material C,10/30", Default3( 0.5, 0.5, 0.5 ) );
CreateInputTexture2D( TextureMetalnessC,        Linear, 8, "",                 "_metal",  "Material C,10/40", Default( 1.0 ) );
CreateInputTexture2D( TextureAmbientOcclusionC, Linear, 8, "",                 "_ao",     "Material C,10/50", Default( 1.0 ) );
CreateInputTexture2D( TextureBlendMaskC,        Linear, 8, "",                 "_blend",  "Material C,10/60", Default( 1.0 ) );
CreateInputTexture2D( TextureTintMaskC,         Linear, 8, "",                 "_tint",   "Material C,10/70", Default( 1.0 ) );
Texture2D g_tColorC  < Channel( RGB,  Box( TextureColorC ), Srgb ); Channel( A, Box( TextureTintMaskC ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
Texture2D g_tNormalC < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureNormalC, TextureRoughnessC ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); > ;
Texture2D g_tRmaC < Channel( R, Box( TextureMetalnessC ), Linear ); Channel( G, Box( TextureAmbientOcclusionC ), Linear ); Channel( B, Box( TextureBlendMaskC ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
#if S_MULTIBLEND >= 3
CreateInputTexture2D( TextureColorD,            Srgb,   8, "",                 "_color",  "Material D,10/10", Default3( 1.0, 1.0, 1.0 ) );
CreateInputTexture2D( TextureNormalD,           Linear, 8, "NormalizeNormals", "_normal", "Material D,10/20", Default3( 0.5, 0.5, 1.0 ) );
CreateInputTexture2D( TextureRoughnessD,        Linear, 8, "Inverse",          "_rough",  "Material D,10/30", Default3( 0.5, 0.5, 0.5 ) );
CreateInputTexture2D( TextureMetalnessD,        Linear, 8, "",                 "_metal",  "Material D,10/40", Default( 1.0 ) );
CreateInputTexture2D( TextureAmbientOcclusionD, Linear, 8, "",                 "_ao",     "Material D,10/50", Default( 1.0 ) );
CreateInputTexture2D( TextureBlendMaskD,        Linear, 8, "",                 "_blend",  "Material D,10/60", Default( 1.0 ) );
CreateInputTexture2D( TextureTintMaskD,         Linear, 8, "",                 "_tint",   "Material D,10/70", Default( 1.0 ) );
Texture2D g_tColorD  < Channel( RGB,  Box( TextureColorD ), Srgb ); Channel( A, Box( TextureTintMaskD ), Linear ); OutputFormat( BC7 ); SrgbRead( true ); >;
Texture2D g_tNormalD < Channel( RGBA, HemiOctIsoRoughness_RG_B( TextureNormalD, TextureRoughnessD ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); > ;
Texture2D g_tRmaD < Channel( R, Box( TextureMetalnessD ), Linear ); Channel( G, Box( TextureAmbientOcclusionD ), Linear ); Channel( B, Box( TextureBlendMaskD ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
#endif
#endif
#endif
#endif

// blendable.shader samples with g_sDefault, which has no state of its own: native's default, anisotropic and wrapped
SamplerState g_BlendableFiltering < Filter( ANISOTROPIC ); >;

/// The mesh's vertex paint.
public struct BlendablePaint
{
	public float4 Blend : TEXCOORD4;   // VertexPaintBlendParams: layers B, C and D's amounts
	public float4 Paint : TEXCOORD5;   // VertexPaintTintColor
}

[Material]
public struct Blendable : IInterpolatedSurface
{
	typealias Varyings = BlendablePaint;

	[Group( "Layers" ), Source( "F_MULTIBLEND" ), Range( 0, 3 )] int ExtraLayers;   // layers past A
	[Group( "Layers" ), Source( "F_USE_TINT_MASKS_IN_VERTEX_PAINT" )] bool TintMasksInVertexPaint;
	[Group( "Layers" ), Source( "ShaderVersion" ), Default( 1 )] int Version;

	[Group( "Material A" ), Srgb] Texture2D ColorA;
	[Group( "Material A" )] Texture2D NormalA;
	[Group( "Material A" )] Texture2D RmaA;
	[Group( "Material A" ), Default3( 1, 1, 1 )] float3 TintColorA;

	[Group( "Material B" ), Srgb] Texture2D ColorB;
	[Group( "Material B" )] Texture2D NormalB;
	[Group( "Material B" )] Texture2D RmaB;
	[Group( "Material B" ), Default3( 1, 1, 1 )] float3 TintColorB;
	[Group( "Material B" ), Range( 0.1, 1 ), Default( 0.5 )] float BlendSoftnessB;
	[Group( "Material B" ), Default2( 1, 1 )] float2 TexCoordScale2;

	[Group( "Material C" ), Srgb] Texture2D ColorC;
	[Group( "Material C" )] Texture2D NormalC;
	[Group( "Material C" )] Texture2D RmaC;
	[Group( "Material C" ), Default3( 1, 1, 1 )] float3 TintColorC;
	[Group( "Material C" ), Range( 0.1, 1 ), Default( 0.5 )] float BlendSoftnessC;
	[Group( "Material C" ), Default2( 1, 1 )] float2 TexCoordScale3;

	[Group( "Material D" ), Srgb] Texture2D ColorD;
	[Group( "Material D" )] Texture2D NormalD;
	[Group( "Material D" )] Texture2D RmaD;
	[Group( "Material D" ), Default3( 1, 1, 1 )] float3 TintColorD;
	[Group( "Material D" ), Range( 0.1, 1 ), Default( 0.5 )] float BlendSoftnessD;
	[Group( "Material D" ), Default2( 1, 1 )] float2 TexCoordScale4;

	// Models have no vertex paint: don't paint them black
	public BlendablePaint Vertex( BlendablePaint v )
	{
		if ( v.Paint.w == 0 ) v.Paint = 1;
		return v;
	}

	// One layer, as blendable.shader's MaterialMultiblend::From reads it; mask is the layer's blend mask
	Material Layer( SurfaceInput i, BlendablePaint v, MaterialTexture2D color, MaterialTexture2D normal, MaterialTexture2D rma, float3 tint, float2 scale, out float mask )
	{
		float2 uv = i.Uv * scale;
		float4 c = i.Sample( color, uv, g_BlendableFiltering );
		float4 n = i.Sample( normal, uv, g_BlendableFiltering );
		float4 r = i.Sample( rma, uv, g_BlendableFiltering );

		Material m = {};
		m.Albedo = c.rgb;
		if ( Version >= 1 )
		{
			m.Normal = i.NormalFromHemiOctahedron( n.rg );
			m.Roughness = n.b;
			m.Metalness = r.r;
			m.AmbientOcclusion = r.g;
			mask = r.b;
		}
		else
		{
			m.Normal = i.NormalFromMap( n.rgb );
			m.Roughness = r.r;
			m.Metalness = r.g;
			m.AmbientOcclusion = r.b;
			mask = r.a;
		}

		m.TintMask = c.a;
		tint *= i.Color.rgb;
		if ( TintMasksInVertexPaint ) tint *= v.Paint.rgb;
		m.Albedo = lerp( m.Albedo, m.Albedo * tint, m.TintMask );
		return m;
	}

	// blendable.shader's ComputeBlendWeight
	static float Weight( float amount, float softness, float reveal )
	{
		float scaled = amount * ( 1.0 + softness * 2.0 ) - softness;
		return 1.0 - saturate( ( reveal - ( scaled - softness ) ) / ( softness * 2.0 ) );
	}

	public Material Evaluate( SurfaceInput i, BlendablePaint v )
	{
		float mask;
		Material m = Layer( i, v, ColorA, NormalA, RmaA, TintColorA, 1, mask );

		if ( ExtraLayers >= 1 && v.Blend.r > 0.0 )
		{
			Material b = Layer( i, v, ColorB, NormalB, RmaB, TintColorB, TexCoordScale2, mask );
			m = Material.Lerp( m, b, Weight( v.Blend.r, BlendSoftnessB, mask ) );
		}

		if ( ExtraLayers >= 2 && v.Blend.g > 0.0 )
		{
			Material c = Layer( i, v, ColorC, NormalC, RmaC, TintColorC, TexCoordScale3, mask );
			m = Material.Lerp( m, c, Weight( v.Blend.g, BlendSoftnessC, mask ) );
		}

		if ( ExtraLayers >= 3 && v.Blend.b > 0.0 )
		{
			Material d = Layer( i, v, ColorD, NormalD, RmaD, TintColorD, TexCoordScale4, mask );
			m = Material.Lerp( m, d, Weight( v.Blend.b, BlendSoftnessD, mask ) );
		}

		if ( !TintMasksInVertexPaint ) m.Albedo *= v.Paint.rgb;
		m.Opacity = 1;
		return m;
	}
}
