// skin.shader written as a surface: colour with the object tint, tint mask and the per-object skin tint and age (the
// tint lookup, the age textures), the hemi-octahedral normal map with roughness and a blurred copy for subsurface
// scattering, bent normals, the combined masks (inside of the mouth, occlusion, opacity or tint or scattering mask), face
// occlusion, transmission, two-lobe specular and occlusion colour bleed. It lights through the final combiner with
// skin.shader's lighting features ([Combiner]), as skin.shader does.
//
// Wrinkles draw as skin.shader draws them today: its vertex stage never passes the wrinkle on, so they're always off.
// Per-vertex curvature (F_USE_PER_VERTEX_CURVATURE) isn't done: such materials stay on skin.shader.
import sbox.surface;

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
