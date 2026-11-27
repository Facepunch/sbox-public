// vertex_tint: standard PBR (colour, normal, roughness, metalness, ambient occlusion, tint mask) tinted by the mesh's
// vertex colours where the tint mask is white, so trimsheet props can colour their parts per vertex while sharing one
// material, with a detail normal blended over the normal map. A surface: the vertex colour is its Varyings, made linear
// per vertex and interpolated to Evaluate.
import sbox.surface;

public struct VertexTintPaint
{
	public float4 Color : COLOR0;
}

[Material, Features( "Common" ), Vertex( "Common, Color" )]
public struct VertexTint : IInterpolatedSurface
{
	typealias Varyings = VertexTintPaint;

	[Srgb, InputWith( "TextureColor", "Material, 10/10", "Srgb; _color; Default3( 1.0, 1.0, 1.0 )" ), Pack( "RGB: Color", "BC7" )] Texture2D Color;
	[InputWith( "TextureNormal", "Material, 10/20", "NormalizeNormals; _normal; Default3( 0.5, 0.5, 1.0 )" ),
		InputWith( "TextureTintMask", "Material, 10/70", "Linear; _tint; Default( 1.0 )" ), Pack( "RGB: Normal; A: TintMask", "BC7" )] Texture2D Normal;
	[InputWith( "TextureNormalDetail", "Material, 10/25", "NormalizeNormals; _detail; Default3( 0, 0, 1 )" ), Pack( "RGB: NormalDetail", "BC7" )] Texture2D NormalDetail;
	[InputWith( "TextureRoughness", "Material, 10/30", "Linear; _rough; Default( 0.5 )" ), InputWith( "TextureMetalness", "Material, 10/40", "Linear; _metal; Default( 1.0 )" ),
		InputWith( "TextureAmbientOcclusion", "Material, 10/50", "Linear; _ao; Default( 1.0 )" ), Pack( "R: Roughness; G: Metalness; B: AmbientOcclusion", "BC7" )] Texture2D Rma;

	[Group( "Material, 10/26" ), Default( 1 ), Range( 0, 1 ), Source( "DetailIntensity" )] float DetailIntensity;
	[Group( "Material, 10/27" ), Default( 2.5 ), Range( 0.25, 8 ), Source( "DetailScale" )] float DetailScale;
	[Group( "Material, 10/31" ), Default( 1 ), Range( 0, 1 ), Source( "RoughnessScale" )] float RoughnessScale;
	[Group( "Material, 10/41" ), Default( 1 ), Range( 0, 1 ), Source( "MetalnessScale" )] float MetalnessScale;

	/// The mesh's colour, linear, as the .shader's vertex stage made it.
	public VertexTintPaint Vertex( VertexTintPaint v )
	{
		v.Color.rgb = SrgbGammaToLinear( v.Color.rgb );
		return v;
	}

	/// Reoriented normal mapping of two tangent-space normals, both still encoded 0..1.
	static float3 BlendNormals( float3 n1, float3 n2 )
	{
		float3 t = n1 * float3( 2, 2, 2 ) + float3( -1, -1, 0 );
		float3 u = n2 * float3( -2, -2, 2 ) + float3( 1, 1, -1 );
		return normalize( t * dot( t, u ) - u * t.z );
	}

	/// An encoded normal's xy scaled by intensity, still encoded.
	static float3 AdjustNormalIntensity( float3 normal, float intensity )
	{
		normal = normal * 2 - 1;
		normal.xy *= intensity;
		return normal * 0.5 + 0.5;
	}

	public Material Evaluate( SurfaceInput i, VertexTintPaint v )
	{
		float3 color = i.Sample( Color, i.Uv ).rgb;
		float4 normal = i.Sample( Normal, i.Uv );
		float3 rma = i.Sample( Rma, i.Uv ).rgb;
		float3 detail = AdjustNormalIntensity( i.Sample( NormalDetail, i.Uv * DetailScale ).rgb, DetailIntensity );

		// The .shader's vertex colour: the object's tint by the mesh's
		float3 tint = i.Color.rgb * v.Color.rgb;

		Material m = Material.Init( i );
		m.Albedo = lerp( color, color * tint, normal.a );
		m.Normal = TransformNormal( BlendNormals( normal.xyz, detail ), i.Normal, i.TangentU, i.TangentV );
		m.Roughness = rma.r * RoughnessScale;
		m.Metalness = rma.g * MetalnessScale;
		m.AmbientOcclusion = rma.b;
		m.TintMask = normal.a;
		return m;
	}
}
