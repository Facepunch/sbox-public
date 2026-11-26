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
[Material, Combiner( "S_SUBSURFACE_SCATTERING = SUBSURFACE_SCATTERING_PREINTEGRATED, S_TRANSMISSIVE, S_AMBIENT_OCCLUSION_COLOR_BLEED, S_TWO_LOBE_SPECULAR" ), Vertex( "Sbox" ),
	Attribute( "NeedsLightProbe" ), Attribute( "CanBatchWithDynamicShaderConstants" ), Attribute( "SupportsMappingDimensions" ), AttributeIf( "translucent", "UseTranslucent" ),
	AttributeIf( "alphatest", "UseAlphaTest" ), Attribute( "transmissive" ), AttributeExpression( "DoNotCastShadows", "DoNotCastShadows" ), Attribute( "ShadowFastPath" )]
public struct Skin : ICombinerSurface
{
	// Features
	[Group( "Shadows" ), Feature( "F_DO_NOT_CAST_SHADOWS" )] bool DoNotCastShadows;
	[Group( "Translucent" ), Feature( "F_ALPHA_TEST" ), RenderState( "AlphaTest" )] bool UseAlphaTest;
	[Group( "Translucent" ), Feature( "F_TRANSLUCENT" ), Options( "Opaque | Translucent" ), RenderState( "Blend: Alpha" )] bool UseTranslucent;
	[Group( "Per-Instance Tint Mask" ), Feature( "F_TINT_MASK" )] bool UseTintMask;
	[Group( "Skin" ), Feature( "F_USE_PER_VERTEX_CURVATURE" )] bool PerVertexCurvature;   // not done: materials that set it draw without it
	[Group( "Skin" ), Feature( "F_USE_BENT_NORMALS" )] bool UseBentNormals;
	[Group( "Skin" ), Feature( "F_USE_FACE_OCCLUSION_TEXTURE" )] bool UseFaceOcclusion;
	[Group( "Skin" ), Feature( "F_SSS_MASK" )] bool UseSssMask;
	[Group( "Skin" ), Feature( "F_AGE_TEXTURE" ), Rule( "Allow1( UseAlphaTest, UseTranslucent, UseTintMask, UseSssMask )", "Translucent, Alpha Test, Tint Mask, and Sss Mask are not compatible" )] bool UseAgeTexture;
	[Group( "Animation" ), Feature( "F_WRINKLE" )] bool Wrinkle;   // not done: skin.shader never passed wrinkles on either

	// The preintegrated scattering's lookup (the material's diffuse falloff)
	[InputWith( "TextureDiffuseFalloff", "Lookup Textures", "Srgb; _diffusewarp; Default3AndFile( 0.5, 0.5, 0.5, \"materials/default/default_skin_diffusewarp.tga\" )" ), Pack( "RGB: DiffuseFalloff", "RGBA8888" )] Texture2D DiffuseFalloff;

	// Age
	[ShowIf( "UseAgeTexture != 0" ), InputWith( "TextureAgeNormal", "Age/10", "NormalizeNormals; _normal; Default3( 0.5, 0.5, 1.0 )" ), Pack( "RGBA: HemiOctIsoRoughness_RG_B( AgeNormal, Roughness )", "BC7" )] Texture2D AgeNormal;
	[Srgb, ShowIf( "UseAgeTexture != 0" ), InputWith( "TextureAgeColor", "Color", "Default3( 0.86, 0.67, 0.62 )" ), Pack( "RGB: AgeColor", "DXT1" )] Texture2D AgeColor;

	// Colour (its alpha is the transmissive mask)
	[Srgb, Opacity, InputWith( "TextureColor", "Color", "Default3( 0.86, 0.67, 0.62 )" ), InputWith( "TextureTransmissiveMask", "Transmission", "_trans; Default3( 1.0, 1.0, 1.0 )" ),
		Pack( "RGB: Color; A: TransmissiveMask", "BC7" ), TextureAttribute( "RepresentativeTexture" )] Texture2D Color;
	[Srgb, InputWith( "TextureTintLookup", "Tinting/3", "Linear; Default3( 1.0, 1.0, 1.0 )" ), Pack( "RGB: TintLookup Srgb", "DXT1" )] Texture2D TintLookup;
	[Group( "Color" ), Color, Default3( 1, 1, 1 )] float3 ColorTint;
	[Group( "Color" ), Default( 1 ), Range( 0, 1 )] float ModelTintAmount;
	[Group( "Tinting/0" )] bool OverrideTinting;
	[Group( "Tinting/1" ), Default( 0.5 ), Range( 0, 1 ), ShowIf( "UseTintMask != 0" )] float SkinTint;
	[Group( "Tinting/2" ), Default( 0.5 ), Range( 0, 1 )] float SkinAge;

	// Normal and roughness
	[Input( "TextureNormal", "Normal/10" ), InputWith( "TextureRoughness", "Roughness,23/10", "Inverse; Default3( 0.5, 0.5, 0.5 )" ), Pack( "RGBA: HemiOctIsoRoughness_RG_B( Normal, Roughness )", "BC7" )] Texture2D Normal;
	[ShowIf( "UseBentNormals != 0" ), InputWith( "TextureBentNormal", "Normal/20", "NormalizeNormals; _bentnormal; Default3( 0.5, 0.5, 1.0 )" ), Pack( "RG: HemiOctIsoRoughness_RG_B( BentNormal )", "ATI2N" )] Texture2D BentNormal;
	[Group( "Normal/30" ), Default( 0.5 ), Range( 0, 4 )] float DiffuseNormalBlur;
	[Group( "Roughness,23/9" ), Default( 1 ), Range( 0, 3 )] float RoughnessScale;
	[Group( "Specular" ), Default( 0.15 ), Range( 0, 1 )] float SecondSpecularLobeRatio;
	[Group( "Specular" ), Default( 0.5 ), Range( 0, 1 )] float SecondSpecularLobeGlossiness;
	[Group( "Curvature" ), Default( 1 ), Range( 0, 3 )] float CurvatureScale;
	[Group( "Lookup Textures" ), Color, Default3( 1, 0.3, 0 )] float3 AmbientNormalSoftness;   // the .shader's, which it never read

	// Masks: r inside of the mouth, g occlusion, a opacity, tint mask or scattering mask
	[InputWith( "TextureInsideMouth", "Face Occlusion", "_mask; Default3AndFile( 0.0, 0.0, 0.0, \"materials/default/default_black_mask.tga\" )" ),
		InputWith( "TextureAmbientOcclusion", "Ambient Occlusion,40/10", "Default3( 1.0, 1.0, 1.0 )" ), InputWith( "TextureTranslucency", "Translucent,15", "" ),
		InputWith( "TextureTintMask", "Color", "_mask; Default3( 1.0, 1.0, 1.0 )" ), InputWith( "TextureSssMask", "Color", "_mask; Default3( 1.0, 1.0, 1.0 )" ),
		PackIf( "R: Box3( InsideMouth ); G: Box3( AmbientOcclusion ); A: PreserveCoverage( Translucency )", "DXT5", "UseAlphaTest" ),
		PackIf( "R: Box3( InsideMouth ); G: Box3( AmbientOcclusion ); A: Translucency", "DXT5", "UseTranslucent" ),
		PackIf( "R: Box3( InsideMouth ); G: Box3( AmbientOcclusion ); A: TintMask", "DXT5", "UseTintMask" ),
		PackIf( "R: Box3( InsideMouth ); G: Box3( AmbientOcclusion ); A: SssMask", "DXT5", "UseSssMask" ),
		Pack( "R: Box3( InsideMouth ); G: Box3( AmbientOcclusion )", "DXT5" )] Texture2D CombinedMasks;

	// Ambient occlusion
	[Default3( 0.4, 0.15, 0.13 )] float3 AmbientOcclusionColorBleed;
	[Group( "Ambient Occlusion/20" ), Default( 1 ), Range( 0, 1 )] float AmbientOcclusionDirectDiffuse;
	[Group( "Ambient Occlusion/30" ), Default( 1 ), Range( 0, 1 )] float AmbientOcclusionDirectSpecular;

	// Transmission
	[Group( "Transmission" ), Color, Default3( 0.75, 0.23, 0.012 )] float3 TransmissionColor;
	[Group( "Transmission" ), Default( 0.8 ), Range( 0, 4 )] float TransmissionFalloff;

	// Face occlusion
	[When( "UseFaceOcclusion" ), InputWith( "TextureOcclusion", "Face Occlusion/10", "" ), Pack( "RGB: Occlusion", "DXT1" )] Texture2D Occlusion;
	[Group( "Face Occlusion" ), Default( 1 ), Range( 0, 1 )] float MouthInteriorBrightnessScale;
	[Group( "Face Occlusion/20" ), Default( 3 ), Range( 0.1, 5 ), ShowIf( "UseFaceOcclusion != 0" )] float OcclusionWidth;
	[Group( "Face Occlusion/30" ), Default( 0.75 ), Range( 0, 1 ), ShowIf( "UseFaceOcclusion != 0" )] float OcclusionStrength;

	// Alpha test and translucency
	[Group( "Translucent" ), Default( 0.5 ), Range( 0.01, 0.99 ), ShowIf( "UseAlphaTest != 0" )] float AlphaTestReference;
	[Group( "Translucent" ), Default( 1 ), Range( 0, 1 ), ShowIf( "UseAlphaTest != 0" )] float AntiAliasedEdgeStrength;
	[Group( "Translucent" ), Default( 1 ), Range( 0, 1 ), ShowIf( "UseTranslucent != 0" )] float OpacityScale;
	[Group( "Fog,300" ), Default( 1 )] bool FogEnabled;

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
