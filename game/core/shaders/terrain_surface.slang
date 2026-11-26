// terrain.shader written as a surface that owns its vertex stage (IVertexStageSurface): the clipmap's instanced meshlets
// placed on the heightmap (with the LOD morph, and the layers' displacement near the camera), the heightmap's edge and the
// painted holes cut by a clip distance in every pass, and up to four layers splatted per pixel from the control map,
// sampled with the terrain-local position's derivatives. The engine lights and fogs the result, as terrain.shader's
// ShadingModelStandard::Shade does. Everything comes from the terrain's render attributes, as for the .shader: the
// material has no values of its own.
import sbox.surface;
import terrain_surface_layers;

/// What the terrain's vertex stage reads of the meshlet mesh: its block-local grid position.
public struct TerrainVertex
{
	float3 PositionAndLod : POSITION;
}

/// What it hands the pixel stage: the terrain-local position, whose derivatives filter the layers.
public struct TerrainVaryings
{
	float3 LocalPosition;
}

[Material, ClipDistances( 1 )]
public struct Terrain : IVertexStageSurface
{
	// The clipmap's grid view (D_GRID), a specialization constant
	[Engine( "D_GRID" ), Specialized, Default( 0 )] int TerrainGrid;

	typealias VertexInput = TerrainVertex;
	typealias Varyings = TerrainVaryings;

	public SurfaceVertex Vertex( TerrainVertex v, VertexStageInput s, out TerrainVaryings varyings )
	{
		TerrainData terrain = TerrainInfo();
		Texture2D heightMap = TerrainTexture( terrain.HeightMapTexture );

		// The instance is the meshlet: placed on its clipmap level and morphed towards the next
		float lod;
		float3 local = Terrain_ClipmapMeshlet( v.PositionAndLod.xy, g_TerrainMeshlets[s.InstanceId], heightMap, terrain.UnitsPerTexel, lod );
		local.z *= terrain.HeightScale;

		// Vertex displacement, skipped in shadow passes: they should be as cheap as possible
		if ( TerrainGrid == 0 && TerrainVertexDisplacement && !TerrainShadowPass && terrain.ControlMapTexture != 0 )
			local += TerrainDisplacement( local, terrain, heightMap );

		// Outside the heightmap, and painted holes: a signed distance, so the rasterizer cuts the triangle
		float2 uv = TerrainLocalToUv( local.xy );
		float keep = min( min( uv.x, 1.0 - uv.x ), min( uv.y, 1.0 - uv.y ) );
		if ( TerrainGrid == 0 && TerrainHasHoles && terrain.ControlMapTexture != 0 && keep >= 0.0 )
		{
			float4 holeWeights;
			uint4 holeBits = TerrainGatherControlQuad( uv, holeWeights );
			float holes = 0.0;
			if ( CompactTerrainMaterial::Decode( holeBits.x ).IsHole ) holes += holeWeights.x;
			if ( CompactTerrainMaterial::Decode( holeBits.y ).IsHole ) holes += holeWeights.y;
			if ( CompactTerrainMaterial::Decode( holeBits.z ).IsHole ) holes += holeWeights.z;
			if ( CompactTerrainMaterial::Decode( holeBits.w ).IsHole ) holes += holeWeights.w;
			keep = min( keep, 0.5 - holes );
		}

		SurfaceVertex o = {};
		o.Position = mul( terrain.Transform, float4( local, 1.0 ) ).xyz;
		o.Normal = TerrainWorldNormal( uv, o.TangentU, o.TangentV );
		o.Uv = uv;
		o.ClipDistance.x = keep;
		varyings.LocalPosition = local;
		return o;
	}

	public Material Evaluate( SurfaceInput i, TerrainVaryings v, Derivatives<TerrainVaryings> d )
	{
		float2 local = v.LocalPosition.xy;
		float2 uv = TerrainLocalToUv( local );
		float2 localDdx = d.Ddx.LocalPosition.xy;
		float2 localDdy = d.Ddy.LocalPosition.xy;

		TerrainSample p = TerrainSample::Init();
		if ( TerrainGrid != 0 )
		{
			TerrainProcGrid( local, localDdx, localDdy, p.Albedo, p.Roughness );
		}
		else if ( TerrainInfo().ControlMapTexture != 0 )
		{
			float4 weights;
			uint4 bits = TerrainGatherControlQuad( uv, weights );
			p = TerrainSplatControlQuad( local, localDdx, localDdy, bits, weights );
		}

		// The layers' tangent-space normal over the baked geometric normal at the pixel
		float3 tangentU, tangentV;
		float3 normal = TerrainWorldNormal( uv, tangentU, tangentV );

		Material m = Material.Init( i );
		m.Albedo = p.Albedo;
		m.Roughness = p.Roughness;
		m.Metalness = p.Metalness;
		m.AmbientOcclusion = p.AmbientOcclusion;
		m.Normal = TransformNormal( p.Normal, normal, tangentU, tangentV );
		return m;
	}
}
