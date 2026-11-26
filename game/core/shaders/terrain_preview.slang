// terrain_preview: a surface, the whole shader for Borealis.Gpu (docs/reference/borealis-slang-materials.md).
import sbox.surface;

Texture2D g_tBCR < Attribute( "BCR" ); SrgbRead( false ); >;
Texture2D g_tNHO < Attribute( "NHO" ); SrgbRead( false ); >;
float g_flUVScale < Attribute( "UVScale" ); Default( 1.0 ); >;
float g_flNormalStrength < Attribute( "NormalStrength" ); Default( 1.0 ); >;
float g_flMetalness < Attribute( "Metalness" ); Default( 0.0 ); >;

[Material, Features( "Common" ), Vertex( "Common" ), Attribute( "NeedsLightProbe" ), Attribute( "CanBatchWithDynamicShaderConstants" )]
public struct TerrainPreview : ISurface
{
	[Group( "Fog,300" ), Default( 1 )] bool FogEnabled;

	public Material Evaluate( SurfaceInput i )
	{

        float2 uv = i.Uv * g_flUVScale;



        float4 bcr = g_tBCR.Sample( g_MaterialFiltering, uv );

        float4 nho = g_tNHO.Sample( g_MaterialFiltering, uv );



        // Matches how terrain.shader unpacks a terrain material

        float3 normal = ComputeNormalFromRGTexture( nho.rg );

        normal.xz *= g_flNormalStrength;

        normal = normalize( normal );



        Material m = Material.Init( i );



        m.Albedo = SrgbGammaToLinear( bcr.rgb );

        m.Normal = TransformNormal( normal, i.Normal, i.TangentU, i.TangentV );

        m.Roughness = bcr.a;

        m.Metalness = g_flMetalness;

        m.AmbientOcclusion = nho.a;



        // The lighting model derives tangent space from these, terrain.shader fills them too -

        // left at zero the basis is degenerate



        return m;

	}
}
