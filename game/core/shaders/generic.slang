// generic.shader written as a surface: colour with tint mask, a DXT5 normal map with bump strength, the anisotropic
// roughness texture and metalness when specular is on, alpha test and self-illumination. It lights through the final
// combiner (ICombinerSurface) as generic.shader does, which edits the combiner's input directly: with specular off the
// specular colour is zero (SpecularScale), and the self-illumination mask takes its share off the diffuse and specular
// colours (DiffuseScale, SpecularScale).
//
// The shader's features are fields, [Source] naming the feature a .vmat sets them with. A material using a feature this
// surface doesn't do (a field below with Range( 0, 0 )) isn't converted and stays on generic.shader: vertex paint.
// Translucent materials blend their opacity (the colour's alpha) over what's behind, additive ones add it; both write no
// depth, as generic.shader's render state says.
import sbox.surface;

// g_sDefault: the default state (trilinear, wrap), which every texture of generic.shader samples with
SamplerState g_GenericDefault < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); >;

[Material]
public struct Generic : ICombinerSurface
{
	// Features
	[Group( "Features" ), Source( "F_SPECULAR" )] bool UseSpecular;
	[Group( "Features" ), Source( "F_ALPHA_TEST" ), RenderState( "AlphaTest" )] bool UseAlphaTest;
	[Group( "Features" ), Source( "F_SELF_ILLUM" )] bool UseSelfIllum;
	[Group( "Features" ), Source( "F_TINT_MASK" )] bool UseTintMask;
	[Group( "Features" ), Source( "F_UNLIT" )] bool UseUnlit;
	[Group( "Features" ), Source( "F_TRANSLUCENT" ), RenderState( "Blend: Alpha" )] bool UseTranslucent;
	[Group( "Features" ), Source( "F_ADDITIVE_BLEND" ), RenderState( "Blend: Additive" )] bool UseAdditiveBlend;
	[Group( "Features" ), Source( "F_RENDER_BACKFACES" ), RenderState( "TwoSided" )] bool RenderBackfaces;

	// Features this surface doesn't do: materials that set them stay on generic.shader
	[Group( "Not converted" ), Source( "F_PAINT_VERTEX_COLORS" ), Range( 0, 0 )] int PaintVertexColors;

	// Colour
	[Group( "Color" ), Srgb, Opacity] Texture2D Color;
	[Group( "Color" )] Texture2D TintMask;
	[Group( "Color" ), Default3( 1, 1, 1 )] float3 ColorTint;

	// Normal
	[Group( "Normal Map" )] Texture2D Normal;
	[Group( "Normal Map" ), Default( 1 ), Range( 0, 10 )] float BumpStrength;

	// Specular: roughness from the anisotropic texture, metalness from the metalness/reflectance texture's red
	[Group( "Lighting" )] Texture2D Roughness;
	[Group( "Lighting" ), Default2( 0, 1 )] float2 GlossinessRange;
	[Group( "Lighting" )] Texture2D MetalnessReflectanceFresnel;
	[Group( "Lighting" ), Default2( 0, 1 )] float2 MetalnessRange;

	// Alpha test and translucency
	[Group( "Translucent" ), Default( 0.5 ), Range( 0.01, 0.99 )] float AlphaTestReference;
	[Group( "Translucent" ), Default( 1 ), Range( 0, 1 )] float OpacityScale;

	// Fog (g_bFogEnabled)
	[Group( "Fog" ), Default( 1 )] bool FogEnabled;

	// Self-illumination
	[Group( "Self Illum" )] Texture2D SelfIllumMask;
	[Group( "Self Illum" ), Default3( 1, 1, 1 )] float3 SelfIllumTint;
	[Group( "Self Illum" ), Default( 1 ), Range( 0, 16 )] float SelfIllumScale;

	public Material Evaluate( SurfaceInput i )
	{
		Material m = Material.Init( i );
		float4 color = i.Sample( Color, i.Uv, g_GenericDefault );
		float3 albedo = color.rgb;

		// The tint and the object's colour where the tint mask says (green)
		float tintMask = UseTintMask ? i.Sample( TintMask, i.Uv, g_GenericDefault ).g : 1.0;
		albedo = lerp( albedo, albedo * ColorTint * i.Color.rgb, tintMask );

		// The normal: DXT5 (alpha and green), y flipped as the shader does before the combiner's own flip, bump strength
		float3 normalTs = ComputeNormalFromRGTexture( i.Sample( Normal, i.Uv, g_GenericDefault ).ag );
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
			float4 rough = i.Sample( Roughness, i.Uv, g_GenericDefault );
			float2 scaleBias = float2( max( 1.0 - GlossinessRange.x, 1.0 - GlossinessRange.y ) - ( 1.0 - GlossinessRange.y ), 1.0 - GlossinessRange.y );
			m.Roughness = min( 1.0, ( rough.b + rough.a ) * 0.5 * scaleBias.x + scaleBias.y + ( rough.r + rough.g ) * 0.5 );
			float metalness = i.Sample( MetalnessReflectanceFresnel, i.Uv, g_GenericDefault ).r;
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
			float mask = i.Sample( SelfIllumMask, i.Uv, g_GenericDefault ).g;
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
