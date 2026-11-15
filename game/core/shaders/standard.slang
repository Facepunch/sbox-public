// The standard lit material: complex.shader, simple.shader and generic.shader as one surface. Materials of all three
// load into it with their own values; Packing says which shader's textures they were compiled for (the normal map's
// encoding, what the colour's alpha holds) and picks that shader's shading where the three differ.
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
// pipelines need far fewer registers, is a specialization constant too ([Specialize]). A material using a feature this surface doesn't do
// (a field below with Range( 0, 0 )) isn't converted and stays on its .shader: complex's parallax occlusion, anisotropic
// gloss, retro-reflection, cloth, bent normals, normal self shadowing, diffuse wrap, the secondary uv set and wrinkles,
// and generic's vertex paint.
import sbox.surface;

// complex.shader's g_sTrilinearWrap (occlusion, metalness, self-illumination and transmission) and generic.shader's
// g_sDefault (every texture), the same state; the rest of complex's use g_sAniso, the surfaces' standard filtering
SamplerState g_StandardTrilinear < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;

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

	// Features this surface doesn't do: materials that set them stay on their .shader
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
		float4 color = i.Sample( Color, i.Uv, g_StandardTrilinear );
		float3 albedo = color.rgb;

		// The tint and the object's colour where the tint mask says (green)
		float tintMask = UseTintMask ? i.Sample( TintMask, i.Uv, g_StandardTrilinear ).g : 1.0;
		albedo = lerp( albedo, albedo * ColorTint * i.Color.rgb, tintMask );

		// The normal: DXT5 (alpha and green), y flipped as the shader does before the combiner's own flip, bump strength
		float3 normalTs = ComputeNormalFromRGTexture( i.Sample( Normal, i.Uv, g_StandardTrilinear ).ag );
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
			float4 rough = i.Sample( Roughness, i.Uv, g_StandardTrilinear );
			float2 scaleBias = float2( max( 1.0 - GlossinessRange.x, 1.0 - GlossinessRange.y ) - ( 1.0 - GlossinessRange.y ), 1.0 - GlossinessRange.y );
			m.Roughness = min( 1.0, ( rough.b + rough.a ) * 0.5 * scaleBias.x + scaleBias.y + ( rough.r + rough.g ) * 0.5 );
			float metalness = i.Sample( MetalnessReflectanceFresnel, i.Uv, g_StandardTrilinear ).r;
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
			float mask = i.Sample( SelfIllumMaskLinear, i.Uv, g_StandardTrilinear ).g;
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
