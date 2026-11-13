// static_overlay.shader written as a surface: a map's overlays (decals baked into the world), complex.shader's standard
// processing without specular, tint mask, metalness or detail textures. Its blend modes are the .shader's: a converted
// material draws with that .shader's render state (blending, no depth write), and mod2x, multiply and mod-then-add take
// the fog as overlays (Fog::ApplyOverlay) and fade towards their neutral colour by opacity after lighting
// (Material.OverlayMode). Unlit unless F_LIT. It lights through the final combiner (ICombinerSurface).
import sbox.surface;

// static_overlay.shader's g_sTrilinearWrap, for occlusion and self-illumination (the rest use g_sAniso)
SamplerState g_OverlayTrilinear < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;

[Material]
public struct StaticOverlay : ICombinerSurface
{
	// Features
	[Group( "Features" ), Source( "F_LIT" )] bool Lit;
	[Group( "Features" ), Source( "F_BLEND_MODE" ), Range( 0, 6 )] int BlendMode;   // 0 opaque, 1 translucent, 2 alpha test, 3 mod2x, 4 additive, 5 multiply, 6 mod then add

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
		m.Albedo = albedo;
		m.Unlit = !Lit;
		m.Additive = BlendMode == 4 || BlendMode == 6;
		m.Fog = FogEnabled;
		m.OverlayMode = BlendMode == 3 || BlendMode == 5 || BlendMode == 6 ? BlendMode : 0;
		return m;
	}
}
