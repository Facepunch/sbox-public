// terrain.shader written as a surface that owns its vertex stage (IVertexStageSurface): the clipmap's instanced meshlets
// placed on the heightmap (with the LOD morph, and the layers' displacement near the camera), the heightmap's edge and the
// painted holes cut by a clip distance in every pass, and up to four layers splatted per pixel from the control map,
// sampled with the terrain-local position's derivatives. The engine lights and fogs the result, as terrain.shader's
// ShadingModelStandard::Shade does. Everything comes from the terrain's render attributes, as for the .shader: the
// material has no values of its own.
import sbox.surface;

// The engine's samplers (common_samplers.fxc), named for this module
SamplerState TerrainBilinearClamp < Filter( BILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
SamplerState TerrainPointClamp < Filter( POINT ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;

/// system.fxc's TextureDimensions2D.
int2 TerrainTextureSize( Texture2D texture, int level )
{
	int2 size;
	int levels;
	texture.GetDimensions( level, size.x, size.y, levels );
	return size;
}

// The control map's packing and the clipmap's meshlet placement, shared with terrain.shader
#include "terrain/TerrainSplatFormat.hlsl"
#define g_sBilinearClamp TerrainBilinearClamp
#define TextureDimensions2D TerrainTextureSize
#include "terrain/TerrainClipmap.hlsl"
#undef g_sBilinearClamp
#undef TextureDimensions2D

// TerrainCommon.hlsl's buffers: the terrain (its transform, maps and settings) and its layers
struct TerrainData
{
	float4x4 Transform;
	float4x4 TransformInv;
	int HeightMapTexture;
	int ControlMapTexture;
	float UnitsPerTexel;
	float HeightScale;
	bool HeightBlending;
	float HeightBlendSharpness;
	int SamplerIndex;
	int NormalMapTexture;
};

struct TerrainLayerData
{
	int ColorTexture;       // bcr: colour, roughness
	int NormalTexture;      // nho: normal, height, occlusion
	float UvScale;
	uint Flags;             // 1: no tiling
	float Metalness;
	float HeightStrength;
	float NormalStrength;
	float DisplacementScale;

	bool NoTile() { return ( Flags & 1u ) != 0; }
};

StructuredBuffer<TerrainData> TerrainBuffer < Attribute( "Terrain" ); >;
StructuredBuffer<TerrainLayerData> TerrainLayers < Attribute( "TerrainMaterials" ); >;
bool TerrainVertexDisplacement < Attribute( "VertexDisplacement" ); Default( 0 ); >;
float TerrainDisplacementFadeDist < Attribute( "DisplacementFadeDist" ); >;

// Set per draw: shadow passes skip vertex displacement (small relief isn't worth the extra taps there)
bool TerrainShadowPass < Attribute( "TerrainShadowPass" ); Default( 0 ); >;

// Hole-free terrains skip the control-map gather in the vertex stage
bool TerrainHasHoles < Attribute( "TerrainHasHoles" ); Default( 1 ); >;

// No layers yet: a checkerboard (terrain.shader's D_GRID combo, a specialization constant from the draw's combo)
int TerrainGrid < Attribute( "D_GRID" ); SpecializedConstant(); Default( 0 ); >;

static const float TerrainTileUnits = 32.0f;

TerrainData TerrainInfo() { return TerrainBuffer[0]; }
Texture2D TerrainTexture( int slot ) { return BindlessTexture2D( uint( slot ) ).Get(); }
SamplerState TerrainLayerSampler() { return BindlessSampler( uint( TerrainInfo().SamplerIndex ) ); }

/// The 0-1 heightmap and control map uv of a terrain-local position.
float2 TerrainLocalToUv( float2 local )
{
	TerrainData terrain = TerrainInfo();
	return local / ( TerrainTextureSize( TerrainTexture( terrain.HeightMapTexture ), 0 ) * terrain.UnitsPerTexel );
}

/// The baked terrain-local geometric normal: flat up where it isn't built.
float3 TerrainSampleNormal( float2 uv )
{
	TerrainData terrain = TerrainInfo();
	if ( terrain.NormalMapTexture == 0 ) return float3( 0, 0, 1 );
	float3 n;
	n.xy = TerrainTexture( terrain.NormalMapTexture ).SampleLevel( TerrainBilinearClamp, uv, 0 ).xy * 2.0 - 1.0;
	n.z = sqrt( saturate( 1.0 - dot( n.xy, n.xy ) ) );
	return n;
}

/// The geometric normal and tangents in world space at a uv (Terrain::ApplyGeometricNormals).
float3 TerrainWorldNormal( float2 uv, out float3 tangentU, out float3 tangentV )
{
	TerrainData terrain = TerrainInfo();
	float3 local = TerrainSampleNormal( uv );
	float3 u = normalize( cross( local, float3( 0, -1, 0 ) ) );

	// Row-vector multiplication by the inverse applies its transpose to the normal
	float3 normal = normalize( mul( local, (float3x3)terrain.TransformInv ) );
	u = mul( terrain.Transform, float4( u, 0.0 ) ).xyz;
	tangentU = normalize( u - normal * dot( u, normal ) );
	float sign = determinant( (float3x3)terrain.Transform ) < 0.0f ? -1.0f : 1.0f;
	tangentV = normalize( cross( normal, tangentU ) ) * sign;
	return normal;
}

/// The 2x2 control quad bilinear filtering would use at uv, with its weights, in (0,0) (1,0) (0,1) (1,1) order, as raw
/// bits (Terrain::GatherControlQuad).
uint4 TerrainGatherControlQuad( float2 uv, out float4 weights )
{
	Texture2D control = TerrainTexture( TerrainInfo().ControlMapTexture );
	float2 size = TerrainTextureSize( control, 0 );
	float2 pixel = uv * size - 0.5;
	float2 f = frac( pixel );
	weights = float4( ( 1.0 - f.x ) * ( 1.0 - f.y ), f.x * ( 1.0 - f.y ), ( 1.0 - f.x ) * f.y, f.x * f.y );

	// Gather returns w=(0,0) z=(1,0) x=(0,1) y=(1,1); reordered to match the weights
	float2 gatherUv = ( floor( pixel ) + 1.0 ) / size;
	return asuint( control.GatherRed( TerrainPointClamp, gatherUv ).wzxy );
}

/// Terrain_HeightBlendWeight: the overlay's share of a height-aware blend.
float TerrainHeightBlendWeight( float t, float baseHeight, float overlayHeight, float sharpness )
{
	float hmax = max( max( baseHeight, overlayHeight ), 1e-4 );
	baseHeight /= hmax;
	overlayHeight /= hmax;

	float depth = lerp( 0.3, 0.02, saturate( sharpness ) );
	float a = baseHeight + ( 1.0 - t );
	float b = overlayHeight + t;
	float ma = max( a, b ) - depth;
	float wb = max( a - ma, 0.0 );
	float wo = max( b - ma, 0.0 );
	return wo / ( wb + wo + 1e-6 );
}

/// Terrain_SampleSeamlessUV: a per-tile rotation and offset against visible tiling.
float2 TerrainSeamlessUv( float2 uv, out float2x2 rotation )
{
	float2 tile = floor( uv );
	float2 inner = frac( uv );
	float2 hash = frac( tile * float2( 443.897f, 441.423f ) );
	hash += dot( hash, hash.yx + 19.19f );
	hash = frac( ( hash.xx + hash.yx ) * hash.xy );

	float angle = hash.x * 6.28318530718;
	float c = cos( angle );
	float s = sin( angle );
	rotation = float2x2( c, -s, s, c );
	inner = mul( rotation, inner - 0.5 ) + 0.5;
	return tile + frac( inner + hash );
}

float2 TerrainSeamlessUv( float2 uv )
{
	float2x2 rotation;
	return TerrainSeamlessUv( uv, rotation );
}

/// One layer's or a blend's properties, the normal in tangent space.
struct TerrainSample
{
	float3 Albedo;
	float3 Normal;
	float Roughness;
	float AmbientOcclusion;
	float Metalness;

	/// common/material.hlsl's Material::Init() for these properties.
	static TerrainSample Init()
	{
		TerrainSample o;
		o.Albedo = 1;
		o.Normal = float3( 0, 0, 1 );
		o.Roughness = 1;
		o.AmbientOcclusion = 1;
		o.Metalness = 0;
		return o;
	}

	static TerrainSample Zero()
	{
		TerrainSample o;
		o.Albedo = 0;
		o.Normal = 0;
		o.Roughness = 0;
		o.AmbientOcclusion = 0;
		o.Metalness = 0;
		return o;
	}

	[mutating]
	void Add( TerrainSample s, float weight )
	{
		Albedo += s.Albedo * weight;
		Normal += s.Normal * weight;
		Roughness += s.Roughness * weight;
		AmbientOcclusion += s.AmbientOcclusion * weight;
		Metalness += s.Metalness * weight;
	}
};

/// Terrain_SampleLayer: one layer at the tile uv, with its gradients.
TerrainSample TerrainSampleLayer( TerrainLayerData layer, float2 tileUv, float2 tileDdx, float2 tileDdy, out float height )
{
	float2 layerUv = tileUv * layer.UvScale;
	float2x2 rotation = float2x2( 1, 0, 0, 1 );
	if ( layer.NoTile() ) layerUv = TerrainSeamlessUv( layerUv, rotation );

	SamplerState filtering = TerrainLayerSampler();
	float2 layerDdx = mul( rotation, tileDdx * layer.UvScale );
	float2 layerDdy = mul( rotation, tileDdy * layer.UvScale );

	float4 bcr = TerrainTexture( layer.ColorTexture ).SampleGrad( filtering, layerUv, layerDdx, layerDdy );
	float4 nho = TerrainTexture( layer.NormalTexture ).SampleGrad( filtering, layerUv, layerDdx, layerDdy );

	float3 normal = ComputeNormalFromRGTexture( nho.rg );
	normal.xy = mul( rotation, normal.xy );
	normal.xz *= layer.NormalStrength;
	normal = normalize( normal );

	TerrainSample o = TerrainSample::Init();
	o.Albedo = SrgbGammaToLinear( bcr.rgb );
	o.Normal = normal;
	o.Roughness = bcr.a;
	o.AmbientOcclusion = nho.a;
	o.Metalness = layer.Metalness;
	height = nho.b * layer.HeightStrength;
	return o;
}

/// Terrain_SplatCompact: a control texel's base and overlay layers, blended.
TerrainSample TerrainSplatCompact( float2 local, float2 localDdx, float2 localDdy, CompactTerrainMaterial material )
{
	float2 tileUv = local / TerrainTileUnits;
	float2 tileDdx = localDdx / TerrainTileUnits;
	float2 tileDdy = localDdy / TerrainTileUnits;
	TerrainData terrain = TerrainInfo();

	float baseHeight;
	TerrainSample base = TerrainSampleLayer( TerrainLayers[material.BaseTextureId], tileUv, tileDdx, tileDdy, baseHeight );
	float blend = material.GetNormalizedBlend();
	if ( blend <= 0.0f && !terrain.HeightBlending ) return base;

	float overlayHeight;
	TerrainSample overlay = TerrainSampleLayer( TerrainLayers[material.OverlayTextureId], tileUv, tileDdx, tileDdy, overlayHeight );
	if ( terrain.HeightBlending ) blend = TerrainHeightBlendWeight( blend, baseHeight, overlayHeight, terrain.HeightBlendSharpness );

	TerrainSample o = base;
	o.Albedo = lerp( base.Albedo, overlay.Albedo, blend );
	o.Normal = lerp( base.Normal, overlay.Normal, blend );
	o.Roughness = lerp( base.Roughness, overlay.Roughness, blend );
	o.AmbientOcclusion = lerp( base.AmbientOcclusion, overlay.AmbientOcclusion, blend );
	o.Metalness = lerp( base.Metalness, overlay.Metalness, blend );
	return o;
}

/// Terrain_AddToStack.
void TerrainAddToStack( uint index, float weight, inout uint indices[4], inout float weights[4], inout int count )
{
	for ( int i = 0; i < count; i++ )
	{
		if ( indices[i] == index )
		{
			weights[i] += weight;
			return;
		}
	}

	if ( weight <= 0.001 ) return;
	if ( count < 4 )
	{
		indices[count] = index;
		weights[count] = weight;
		count++;
	}
}

/// Terrain_SplatStack: up to four layers by index and weight, height-blended when the terrain asks.
TerrainSample TerrainSplatStack( float2 local, float2 localDdx, float2 localDdy, uint indices[4], float weights[4] )
{
	float2 tileUv = local / TerrainTileUnits;
	float2 tileDdx = localDdx / TerrainTileUnits;
	float2 tileDdy = localDdy / TerrainTileUnits;
	TerrainData terrain = TerrainInfo();

	TerrainSample layers[4];
	float heights[4];
	for ( int i = 0; i < 4; i++ )
	{
		if ( weights[i] <= 0.0f )
		{
			layers[i] = TerrainSample::Init();
			layers[i].Albedo = 0;
			layers[i].Roughness = 0;
			layers[i].AmbientOcclusion = 0;
			layers[i].Metalness = 0;
			heights[i] = 0;
			continue;
		}

		layers[i] = TerrainSampleLayer( TerrainLayers[indices[i]], tileUv, tileDdx, tileDdy, heights[i] );
	}

	float baseWeights[4] = { weights[0], weights[1], weights[2], weights[3] };
	float sum = baseWeights[0] + baseWeights[1] + baseWeights[2] + baseWeights[3];
	if ( sum > 0 && sum != 1.0 )
	{
		float scale = 1.0 / sum;
		baseWeights[0] *= scale;
		baseWeights[1] *= scale;
		baseWeights[2] *= scale;
		baseWeights[3] *= scale;
	}

	float blendWeights[4];
	if ( terrain.HeightBlending )
	{
		float average = heights[0] * baseWeights[0] + heights[1] * baseWeights[1] + heights[2] * baseWeights[2] + heights[3] * baseWeights[3];
		float sharpness = terrain.HeightBlendSharpness * 10.0;
		for ( int b = 0; b < 4; b++ ) blendWeights[b] = baseWeights[b] > 0.0 ? baseWeights[b] * pow( 2.0, ( heights[b] - average ) * sharpness ) : 0.0;

		float total = blendWeights[0] + blendWeights[1] + blendWeights[2] + blendWeights[3];
		if ( total > 0.0 )
		{
			blendWeights[0] /= total;
			blendWeights[1] /= total;
			blendWeights[2] /= total;
			blendWeights[3] /= total;
		}
	}
	else
	{
		blendWeights = baseWeights;
	}

	TerrainSample o = TerrainSample::Zero();
	[unroll]
	for ( int b = 0; b < 4; b++ ) o.Add( layers[b], blendWeights[b] );
	return o;
}

/// Terrain_SplatControlQuad: the layers of the four control texels around a pixel, blended bilinearly.
TerrainSample TerrainSplatControlQuad( float2 local, float2 localDdx, float2 localDdy, uint4 bits, float4 quadWeights )
{
	CompactTerrainMaterial mat00 = CompactTerrainMaterial::Decode( bits.x );
	CompactTerrainMaterial mat10 = CompactTerrainMaterial::Decode( bits.y );
	CompactTerrainMaterial mat01 = CompactTerrainMaterial::Decode( bits.z );
	CompactTerrainMaterial mat11 = CompactTerrainMaterial::Decode( bits.w );

	if ( bits.x == bits.y && bits.x == bits.z && bits.x == bits.w ) return TerrainSplatCompact( local, localDdx, localDdy, mat00 );

	if ( TerrainInfo().HeightBlending )
	{
		CompactTerrainMaterial corners[4];
		corners[0] = mat00;
		corners[1] = mat10;
		corners[2] = mat01;
		corners[3] = mat11;

		TerrainSample o = TerrainSample::Zero();
		[unroll]
		for ( int c = 0; c < 4; c++ ) o.Add( TerrainSplatCompact( local, localDdx, localDdy, corners[c] ), quadWeights[c] );
		return o;
	}

	uint indices00[4], indices10[4], indices01[4], indices11[4];
	float weights00[4], weights10[4], weights01[4], weights11[4];
	mat00.GetMaterialStack( indices00, weights00 );
	mat10.GetMaterialStack( indices10, weights10 );
	mat01.GetMaterialStack( indices01, weights01 );
	mat11.GetMaterialStack( indices11, weights11 );

	// The four texels' stacks merged into the four heaviest layers (Terrain_MergeBilinearStacks)
	uint indices[4] = { 0, 0, 0, 0 };
	float weights[4] = { 0, 0, 0, 0 };
	int count = 0;
	for ( int k = 0; k < 4; k++ ) TerrainAddToStack( indices00[k], weights00[k] * quadWeights.x, indices, weights, count );
	for ( int k = 0; k < 4; k++ ) TerrainAddToStack( indices10[k], weights10[k] * quadWeights.y, indices, weights, count );
	for ( int k = 0; k < 4; k++ ) TerrainAddToStack( indices01[k], weights01[k] * quadWeights.z, indices, weights, count );
	for ( int k = 0; k < 4; k++ ) TerrainAddToStack( indices11[k], weights11[k] * quadWeights.w, indices, weights, count );

	// Sorted by layer index, for a consistent blend order
	for ( int pass = 0; pass < 3; pass++ )
	{
		for ( int i = 0; i < 3 - pass; i++ )
		{
			if ( indices[i] > indices[i + 1] && weights[i + 1] > 0 )
			{
				uint index = indices[i];
				indices[i] = indices[i + 1];
				indices[i + 1] = index;
				float weight = weights[i];
				weights[i] = weights[i + 1];
				weights[i + 1] = weight;
			}
		}
	}

	return TerrainSplatStack( local, localDdx, localDdy, indices, weights );
}

/// Terrain_ProcGrid: a box-filtered checkerboard for a terrain without layers, from the position's derivatives.
void TerrainProcGrid( float2 local, float2 localDdx, float2 localDdy, out float3 albedo, out float roughness )
{
	float2 p = local / 64;
	float2 w = ( abs( localDdx ) + abs( localDdy ) ) / 64 + 0.001;
	float2 i = 2.0 * ( abs( frac( ( p - 0.5 * w ) * 0.5 ) - 0.5 ) - abs( frac( ( p + 0.5 * w ) * 0.5 ) - 0.5 ) ) / w;
	float v = 0.5 - 0.5 * i.x * i.y;
	albedo = 0.7f + v * 0.3f;
	roughness = 0.8f + ( 1 - v ) * 0.2f;
}

/// The layers' displacement at a terrain-local position, along the baked normal (terrain.shader's vertex stage).
float3 TerrainDisplacement( float3 local, TerrainData terrain, Texture2D heightMap )
{
	// Faded to zero by the region's edge, measured from a snapped centre so the amount is fixed per vertex and the surface
	// doesn't breathe as the camera moves
	float2 centre = roundToIncrement( g_vClipCameraLocal, terrain.UnitsPerTexel * 2.0f );
	float distance = max( abs( local.x - centre.x ), abs( local.y - centre.y ) );
	float t = saturate( distance / TerrainDisplacementFadeDist );
	float fade = 1.0 - t * t;
	if ( fade <= 0 ) return 0;

	float2 size = TerrainTextureSize( heightMap, 0 );
	float2 uv = local.xy / ( size * terrain.UnitsPerTexel );
	CompactTerrainMaterial control = CompactTerrainMaterial::DecodeFromFloat( TerrainTexture( terrain.ControlMapTexture ).SampleLevel( TerrainPointClamp, uv, 0 ).r );

	TerrainLayerData base = TerrainLayers[control.BaseTextureId];
	SamplerState filtering = TerrainLayerSampler();
	float2 baseUv = ( local.xy / 32.0f ) * base.UvScale;
	if ( base.NoTile() ) baseUv = TerrainSeamlessUv( baseUv );
	float4 baseNho = TerrainTexture( base.NormalTexture ).SampleLevel( filtering, baseUv, 0 );
	float baseDisplacement = ( baseNho.b - 0.5f ) * 2.0f * base.DisplacementScale;

	float blend = control.GetNormalizedBlend();
	float total = baseDisplacement;
	if ( blend > 0.0f || terrain.HeightBlending )
	{
		TerrainLayerData overlay = TerrainLayers[control.OverlayTextureId];
		float2 overlayUv = ( local.xy / 32.0f ) * overlay.UvScale;
		if ( overlay.NoTile() ) overlayUv = TerrainSeamlessUv( overlayUv );
		float4 overlayNho = TerrainTexture( overlay.NormalTexture ).SampleLevel( filtering, overlayUv, 0 );
		float overlayDisplacement = ( overlayNho.b - 0.5f ) * 2.0f * overlay.DisplacementScale;

		// Height-aware, as the colour and normal blend
		if ( terrain.HeightBlending && base.NormalTexture > 0 && overlay.NormalTexture > 0 )
			blend = TerrainHeightBlendWeight( blend, baseNho.b * base.HeightStrength, overlayNho.b * overlay.HeightStrength, terrain.HeightBlendSharpness );

		total = lerp( baseDisplacement, overlayDisplacement, blend );
	}

	return TerrainSampleNormal( uv ) * total * fade;
}

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
