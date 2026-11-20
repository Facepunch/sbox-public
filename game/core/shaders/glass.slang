// glass.shader written as a surface: the common inputs' material, metal-free, its refraction read from the frame-buffer
// copy along the refracted ray (blurred by roughness and grazing angle) and mixed into the emission by the environment
// reflection's share, an optional second material over it (F_OVERLAY_LAYER), and three render states by quality:
// refractive (unblended, alpha 1: it replaces what's behind it), simple (blended by opacity, no frame-buffer copy) and
// layered (premultiplied, its transmission's common floor left to the blend).
//
// Every glass material draws in the translucent layer whatever its quality, as glass.shader defines S_TRANSLUCENT itself,
// and writes depth, as the .shader's default depth state does. Its shadows are its own (IShadowSurface): the depth dithered
// by an opacity from the view's angle, roughness, refraction and albedo, as glass.shader's depth mode; the glass of an
// object that is partly opaque casts no shadow, as on native. In the 3D skybox it doesn't refract, as the .shader's
// D_SKYBOX. Not done: the tools' refraction view.
import sbox.surface;

// What the material compiler makes of a .vmat's images (SlangMaterialCompiler): glass.shader's texture inputs,
// the textures made from them and its attributes, read with the material's features
#if MATERIAL_INPUTS
#define BLEND_MODE_ALREADY_SET 1
#include "common/vertex.hlsl"
#define GLASS_REFRACTIVE 0
#define GLASS_CHEAP 1
#define GLASS_LAYERED 2
#define S_GLASS_QUALITY F_GLASS_QUALITY
#define S_OVERLAY_LAYER F_OVERLAY_LAYER
#if (S_GLASS_QUALITY == GLASS_LAYERED)
#elif (S_GLASS_QUALITY == GLASS_CHEAP)
#endif
#define DEPTH_STATE_ALREADY_SET 1
#define BLEND_MODE_ALREADY_SET 1
#define S_TRANSLUCENT 1
#include "common/utils/Material.CommonInputs.hlsl"
#include "common/pixel.hlsl"
BoolAttribute(bWantsFBCopyTexture, S_GLASS_QUALITY != GLASS_CHEAP );
#if (S_OVERLAY_LAYER)
CreateInputTexture2D(TextureColorB, Srgb, 8, "", "_color", "MaterialB,10/10", Default3(1.0, 1.0, 1.0));
CreateInputTexture2D(TextureNormalB, Linear, 8, "NormalizeNormals", "_normal", "MaterialB,10/20", Default3(0.5, 0.5, 1.0));
CreateInputTexture2D(TextureRoughnessB, Linear, 8, "", "_rough", "MaterialB,10/30", Default(0.5));
CreateInputTexture2D(TextureMetalnessB, Linear, 8, "", "_metal", "MaterialB,10/40", Default(1.0));
CreateInputTexture2D(TextureAmbientOcclusionB, Linear, 8, "", "_ao", "MaterialB,10/50", Default(1.0));
CreateInputTexture2D(TextureBlendMaskB, Linear, 8, "", "_blend", "MaterialB,10/60", Default(1.0));
CreateInputTexture2D(TextureTranslucencyB, Linear, 8, "", "_trans", "MaterialB,10/70", Default3(1.0, 1.0, 1.0));
CreateInputTexture2D(TextureTintMaskB, Linear, 8, "", "_tint", "MaterialB,10/70", Default(1.0));
Texture2D g_tColorB < Channel(RGB, AlphaWeighted(TextureColorB, TextureTranslucencyB), Srgb); Channel(A, Box(TextureTranslucencyB), Linear); OutputFormat(BC7); SrgbRead(true); > ;
Texture2D g_tNormalB < Channel(RGB, Box(TextureNormalB), Linear); Channel(A, Box(TextureTintMaskB), Linear); OutputFormat(DXT5); SrgbRead(false); > ;
Texture2D g_tRmaB < Channel(R, Box(TextureRoughnessB), Linear); Channel(G, Box(TextureMetalnessB), Linear); Channel(B, Box(TextureAmbientOcclusionB), Linear); Channel(A, Box(TextureBlendMaskB), Linear); OutputFormat(BC7); SrgbRead(false); > ;
#endif
#endif

// The engine's, bound by attribute as glass.shader's are
Texture2D GlassFrameBufferCopy < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;
Texture2D GlassBrdfLookup < Attribute( "BRDFLookup" ); >;
SamplerState GlassTrilinearMirror < Filter( TRILINEAR ); AddressU( MIRROR ); AddressV( MIRROR ); AddressW( MIRROR ); >;
SamplerState GlassTrilinearClamp < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;

static const int GlassRefractive = 0;
static const int GlassSimple = 1;
static const int GlassLayered = 2;

/// common/BRDF.hlsl's CalcBRDFReflectionFactor: the environment's reflected share from the BRDF lookup.
float3 ReflectionFactor( float nDotV, float roughness, float3 specular )
{
	float2 terms = GlassBrdfLookup.SampleLevel( GlassTrilinearClamp, float2( nDotV, roughness ), 0.0 ).rg;
	terms *= terms;
	return specular * terms.x + terms.y;
}

[Material, DepthWrite, Translucent]
public struct Glass : ICompositeSurface, IShadowSurface
{
	// Features
	[Group( "Glass" ), Source( "F_GLASS_QUALITY" ), Range( 0, 2 ), RenderState( "Blend: Opaque, Alpha, Premultiplied" )] int Quality;
	[Group( "Glass" ), Source( "F_OVERLAY_LAYER" )] bool OverlayLayer;
	[Group( "Features" ), Source( "F_RENDER_BACKFACES" ), RenderState( "TwoSided" )] bool RenderBackfaces;

	// Material.CommonInputs.hlsl's textures and tint
	[Group( "Material" ), Srgb, Opacity] Texture2D Color;
	[Group( "Material" )] Texture2D Normal;
	[Group( "Material" )] Texture2D Rma;
	[Group( "Material" ), Default3( 1, 1, 1 )] float3 TintColor;

	// Blur and refraction
	[Group( "Glass" ), Default( 0 ), Range( 0, 1 )] float BlurAmount;
	[Group( "Glass" ), Default( 1.005 ), Range( 1, 1.1 )] float RefractionStrength;
	[Group( "Glass" ), Default( 0 ), Range( 0, 1 )] float AlbedoAbsorption;

	// The overlay layer's material (F_OVERLAY_LAYER)
	[Group( "MaterialB" ), Srgb] Texture2D ColorB;
	[Group( "MaterialB" )] Texture2D NormalB;
	[Group( "MaterialB" )] Texture2D RmaB;
	[Group( "MaterialB" ), Default3( 1, 1, 1 )] float3 TintColorB;

	public Material Evaluate( SurfaceInput i )
	{
		// Mapped from the face's own normal, then turned on back faces, as glass.shader does it
		Material m = Material.FromCommonInputs( i, Color, Normal, Rma, TintColor );
		if ( !i.FrontFace ) m.Normal = -m.Normal;

		m.Metalness = 0;

		// Orthographic: every view ray is the camera's forward; perspective: through the pixel
		float3 ray = i.Orthographic ? i.CameraDirection : normalize( i.PositionFromOrigin );
		float nDotV = saturate( dot( -m.Normal, ray ) );
		float3 environment = ReflectionFactor( nDotV, m.Roughness, 0.04 );
		float3 albedo = m.Albedo;

		if ( Quality != GlassSimple )
		{
			float3 behind = i.ScenePosition - i.Camera;
			float distanceVs = distance( i.PositionFromOrigin, behind );
			float3 refracted = i.PositionFromOrigin + refract( ray, m.Normal, 1.0 / RefractionStrength ) * distanceVs;

			// Where the refracted ray lands on screen; an orthographic view and the 3D skybox keep the pixel's own
			float2 screen = i.ScreenUv;
			if ( !i.Orthographic && !i.Skybox )
			{
				float4 projected = mul( i.WorldToProjection, float4( refracted, 0 ) );
				screen = projected.xy / projected.w * 0.5 + 0.5;
				screen.y = 1.0 - screen.y;
			}

			// Blurred by roughness and the grazing angle, through the copy's mips
			float amount = BlurAmount * m.Roughness * ( 1.0 - ( 1.0 / distanceVs ) );
			amount /= nDotV;
			float4 refraction = GlassFrameBufferCopy.SampleLevel( GlassTrilinearMirror, screen * i.FrameBufferUvScale, sqrt( amount ) * 7 );

			m.Emission = lerp( refraction.xyz, 0.0, environment );
			m.Emission *= m.Albedo * ( 1.0 - m.Roughness * AlbedoAbsorption );
			m.Albedo *= m.Roughness * AlbedoAbsorption;

			// Layered: premultiplied, the reflection additive in the colour and the transmission's common floor left to
			// the blend's weight on what's behind
			if ( Quality == GlassLayered )
			{
				float3 transmission = saturate( ( 1.0 - environment ) * albedo * ( 1.0 - m.Roughness * AlbedoAbsorption ) );
				float common = min( transmission.x, min( transmission.y, transmission.z ) );
				m.Emission = refraction.xyz * max( transmission - common.xxx, 0.0 );
				m.Opacity = saturate( 1.0 - common );
			}
		}

		if ( OverlayLayer )
		{
			float4 color = i.Sample( ColorB, i.Uv );
			float4 normal = i.Sample( NormalB, i.Uv );
			float4 rma = i.Sample( RmaB, i.Uv );

			Material b = {};
			b.Albedo = color.rgb * TintColorB;
			b.Opacity = color.a;
			b.Metalness = rma.g;
			b.Roughness = rma.r;
			b.AmbientOcclusion = rma.b;
			b.Transmission = rma.a;
			b.TintMask = normal.a;
			b.Normal = i.NormalFromMap( normal.rgb );
			m = Material.Lerp( m, b, b.Opacity );
		}

		return m;
	}

	// Depth and shadows, as glass.shader's depth mode: the base material's environment reflection at the view's angle (the
	// geometric normal's), made more opaque by roughness, refraction and albedo
	public float ShadowOpacity( SurfaceInput i )
	{
		Material m = Material.FromCommonInputs( i, Color, Normal, Rma, TintColor );
		float facing = dot( -i.Normal, i.CameraDirection );
		float opacity = sqrt( ReflectionFactor( facing, m.Roughness, 0.04 ).x );
		opacity = lerp( opacity, 0.75, sqrt( m.Roughness ) );
		opacity = lerp( opacity, 1.0 - facing, ( RefractionStrength - 1.0 ) * 5.0 );
		return lerp( 1.0, opacity, length( m.Albedo ) * 0.5 + 0.5 );
	}

	// Refractive glass writes no alpha of its own: what it read of the frame is its colour
	public float4 Composite( float4 shaded, Material m, SurfaceInput i )
	{
		if ( Quality == GlassRefractive ) shaded.a = 1.0;
		return shaded;
	}
}
