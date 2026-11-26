// blendable.shader written as a surface: up to four layers, blended by the mesh's vertex paint. The paint (blend weights
// and tint) is the surface's Varyings, read from the mesh and interpolated to Evaluate; the shader's features are
// fields, [Source] naming the feature a .vmat sets them with: ExtraLayers and TintMasksInVertexPaint, read by uniform
// branches. Version is the material's ShaderVersion: 1 packs roughness into the normal map, beside a hemi-octahedral
// normal.
// HEADER: Version = 1;
import sbox.surface;


/// The mesh's vertex paint.
public struct BlendablePaint
{
	public float4 Blend : TEXCOORD4;   // VertexPaintBlendParams: layers B, C and D's amounts
	public float4 Paint : TEXCOORD5;   // VertexPaintTintColor
}

[Material, Features( "Common" ), Vertex( "VertexPaintBlend, VertexPaintTint, Common" ),
	Attribute( "NeedsLightProbe" ), Attribute( "CanBatchWithDynamicShaderConstants" ), AttributeExpression( "VertexPaintUI2Layer", "ExtraLayers == 1" ),
	AttributeExpression( "VertexPaintUI3Layer", "ExtraLayers == 2" ), AttributeExpression( "VertexPaintUI4Layer", "ExtraLayers == 3" ),
	AttributeExpression( "VertexPaintUI5Layer", "ExtraLayers == 4" ), Attribute( "VertexPaintUIPickColor" ), Attribute( "ShadowFastPath" ),
	Attribute( "SupportsMappingDimensions" )]
public struct Blendable : IInterpolatedSurface
{
	// blendable.shader samples with g_sDefault, which has no state of its own: native's default, anisotropic and wrapped
	[Sampler( "Anisotropic", "Wrap" )] SamplerState g_BlendableFiltering;

	typealias Varyings = BlendablePaint;

	[Group( "Number Of Blendable Layers" ), Feature( "F_MULTIBLEND" ), Options( "1 Layers | 2 Layers | 3 Layers | 4 Layers" ), Range( 0, 3 )] int ExtraLayers;   // layers past A
	[Group( "Use Tint Masks In Vertex Paint" ), Feature( "F_USE_TINT_MASKS_IN_VERTEX_PAINT" )] bool TintMasksInVertexPaint;
	[Source( "ShaderVersion" ), Default( 1 )] int Version;
	[Group( "Fog,300" ), Default( 1 )] bool FogEnabled;

	[Group( "Material A,10" ), Srgb, Pack( "RGB: ColorA; A: TintMaskA", "BC7" ), TextureAttribute( "RepresentativeTexture" )] Texture2D ColorA;
	[Group( "Material A,10" ), InputWith( "TextureRoughnessA", "Material A,10/30", "Inverse; Default3( 0.5, 0.5, 0.5 )" ), Pack( "RGBA: HemiOctIsoRoughness_RG_B( NormalA, RoughnessA )", "BC7" )] Texture2D NormalA;
	[Group( "Material A,10" ), Pack( "R: MetalnessA; G: AmbientOcclusionA; B: BlendMaskA", "BC7" )] Texture2D RmaA;
	[Group( "Material A,10/80" ), Source( "g_flTintColorA" ), Color, Default3( 1, 1, 1 )] float3 TintColorA;

	[Group( "Material B,10" ), Srgb, When( "ExtraLayers >= 1" ), Pack( "RGB: ColorB; A: TintMaskB", "BC7" )] Texture2D ColorB;
	[Group( "Material B,10" ), When( "ExtraLayers >= 1" ), InputWith( "TextureRoughnessB", "Material B,10/30", "Inverse; Default3( 0.5, 0.5, 0.5 )" ), Pack( "RGBA: HemiOctIsoRoughness_RG_B( NormalB, RoughnessB )", "BC7" )] Texture2D NormalB;
	[Group( "Material B,10" ), When( "ExtraLayers >= 1" ), Pack( "R: MetalnessB; G: AmbientOcclusionB; B: BlendMaskB", "BC7" )] Texture2D RmaB;
	[Group( "Material B,10/80" ), Source( "g_flTintColorB" ), Color, Default3( 1, 1, 1 ), ShowIf( "ExtraLayers != 0" )] float3 TintColorB;
	[Group( "Material B,10/90" ), Range( 0.1, 1 ), Default( 0.5 ), ShowIf( "ExtraLayers != 0" )] float BlendSoftnessB;
	[Group( "Material B,10/100" ), Default2( 1, 1 ), Range( 0, 10 ), ShowIf( "ExtraLayers != 0" )] float2 TexCoordScale2;

	[Group( "Material C,10" ), Srgb, When( "ExtraLayers >= 2" ), Pack( "RGB: ColorC; A: TintMaskC", "BC7" )] Texture2D ColorC;
	[Group( "Material C,10" ), When( "ExtraLayers >= 2" ), InputWith( "TextureRoughnessC", "Material C,10/30", "Inverse; Default3( 0.5, 0.5, 0.5 )" ), Pack( "RGBA: HemiOctIsoRoughness_RG_B( NormalC, RoughnessC )", "BC7" )] Texture2D NormalC;
	[Group( "Material C,10" ), When( "ExtraLayers >= 2" ), Pack( "R: MetalnessC; G: AmbientOcclusionC; B: BlendMaskC", "BC7" )] Texture2D RmaC;
	[Group( "Material C,10/80" ), Source( "g_flTintColorC" ), Color, Default3( 1, 1, 1 ), ShowIf( "ExtraLayers >= 2" )] float3 TintColorC;
	[Group( "Material C,10/90" ), Range( 0.1, 1 ), Default( 0.5 ), ShowIf( "ExtraLayers >= 2" )] float BlendSoftnessC;
	[Group( "Material C,10/100" ), Default2( 1, 1 ), Range( 0, 10 ), ShowIf( "ExtraLayers >= 2" )] float2 TexCoordScale3;

	[Group( "Material D,10" ), Srgb, When( "ExtraLayers >= 3" ), Pack( "RGB: ColorD; A: TintMaskD", "BC7" )] Texture2D ColorD;
	[Group( "Material D,10" ), When( "ExtraLayers >= 3" ), InputWith( "TextureRoughnessD", "Material D,10/30", "Inverse; Default3( 0.5, 0.5, 0.5 )" ), Pack( "RGBA: HemiOctIsoRoughness_RG_B( NormalD, RoughnessD )", "BC7" )] Texture2D NormalD;
	[Group( "Material D,10" ), When( "ExtraLayers >= 3" ), Pack( "R: MetalnessD; G: AmbientOcclusionD; B: BlendMaskD", "BC7" )] Texture2D RmaD;
	[Group( "Material D,10/80" ), Source( "g_flTintColorD" ), Color, Default3( 1, 1, 1 ), ShowIf( "ExtraLayers == 3" )] float3 TintColorD;
	[Group( "Material D,10/90" ), Range( 0.1, 1 ), Default( 0.5 ), ShowIf( "ExtraLayers == 3" )] float BlendSoftnessD;
	[Group( "Material D,10/100" ), Default2( 1, 1 ), Range( 0, 10 ), ShowIf( "ExtraLayers == 3" )] float2 TexCoordScale4;

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

		// Version picks the channels and the normal's encoding by selects, with one transform to world space for both
		// encodings (NormalFromHemiOctahedron's and NormalFromMap's maths): a branch between the two whole decodes holds six
		// more registers through the shading's decal loop
		bool packed = Version >= 1;
		float2 d = float2( n.r + n.g - 1.00392163, n.r - n.g );
		float3 t = normalize( packed ? float3( d, 1.0 - abs( d.x ) - abs( d.y ) ) : 2.0 * n.rgb - 1.0 );
		t.y = -t.y;
		m.Normal = normalize( t.x * normalize( i.TangentU ) + t.y * normalize( i.TangentV ) + t.z * i.Normal );
		m.Roughness = packed ? n.b : r.r;
		m.Metalness = packed ? r.r : r.g;
		m.AmbientOcclusion = packed ? r.g : r.b;
		mask = packed ? r.b : r.a;

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
