// blendable.shader written as a surface: up to four layers, blended by the mesh's vertex paint. The paint (blend weights
// and tint) is the surface's Varyings, read from the mesh and interpolated to Evaluate; the shader's features are
// fields, [Source] naming the feature a .vmat sets them with: ExtraLayers and TintMasksInVertexPaint, read by uniform
// branches. Version is the material's ShaderVersion: 1 packs roughness into the normal map, beside a hemi-octahedral
// normal.
import sbox.surface;

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
