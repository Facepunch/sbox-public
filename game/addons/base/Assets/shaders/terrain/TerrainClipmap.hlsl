//
// Geometry clipmap - instanced meshlets
//
// The clipmap is decomposed (on the CPU) into uniform BlockSize x BlockSize quad blocks. A single
// canonical block mesh is instanced for every meshlet; this file places each instance in world
// space and applies the per-vertex LOD morph that keeps ring seams crack-free.
//
// References:
//   GPU Gems 2, ch. 2 - "Terrain Rendering Using GPU-Based Geometry Clipmaps"
//   http://casual-effects.blogspot.com/2014/04/fast-terrain-rendering-with-continuous.html
//

float2 roundToIncrement( float2 value, float increment )
{
    return round( value * ( 1.0f / increment ) ) * increment;
}

// Per-instance data. Must match TerrainClipmap.Meshlet (C#).
struct TerrainMeshlet
{
    float2 BlockOffset; // lower-corner offset of the block, in level-cells, from the level center
    int    Level;       // LOD level (0 = one vertex per texel, coarser outward)
    uint   Pad;
};

StructuredBuffer<TerrainMeshlet> g_TerrainMeshlets < Attribute( "TerrainMeshlets" ); >;

// LOD morph band, in level-cells. Precomputed and clamped on the CPU (see CreateClipmapSceneObject).
float g_flClipMorphStartCells < Attribute( "ClipMorphStartCells" ); >;
float g_flClipMorphEndCells   < Attribute( "ClipMorphEndCells" ); >;

// Unsnapped camera in terrain-local space. GPU geometry and CPU cull both snap this per-level with the
// same math, so they stay aligned.
float2 g_vClipCameraLocal < Attribute( "ClipCameraLocal" ); >;

// Reconstruct height on a coarser lattice by bilinearly interpolating the four surrounding
// lattice samples. Used to obtain the next-coarser level's height for the LOD morph.
float Terrain_SampleHeightLattice( Texture2D tHeightMap, float2 localXY, float latticeStep, float texSizeUnits )
{
    float2 c = floor( localXY / latticeStep ) * latticeStep;
    float2 f = saturate( ( localXY - c ) / latticeStep );

    float inv = 1.0f / texSizeUnits;
    float h00 = tHeightMap.SampleLevel( g_sBilinearClamp, ( c                                  ) * inv, 0 ).r;
    float h10 = tHeightMap.SampleLevel( g_sBilinearClamp, ( c + float2( latticeStep, 0 )       ) * inv, 0 ).r;
    float h01 = tHeightMap.SampleLevel( g_sBilinearClamp, ( c + float2( 0, latticeStep )       ) * inv, 0 ).r;
    float h11 = tHeightMap.SampleLevel( g_sBilinearClamp, ( c + float2( latticeStep, latticeStep ) ) * inv, 0 ).r;

    return lerp( lerp( h00, h10, f.x ), lerp( h01, h11, f.x ), f.y );
}

//
// Place an instanced meshlet vertex.
//   blockLocal : block-local grid coordinate in [0, BlockSize]
// Returns terrain-local position (xy, normalized height in z). Caller scales z by HeightScale
// and transforms by Terrain::Get().Transform to get world space.
//
float3 Terrain_ClipmapMeshlet(
    float2 blockLocal,
    TerrainMeshlet meshlet,
    Texture2D tHeightMap,
    float unitsPerTexel,
    out float outLod )
{
    int level = meshlet.Level;

    // World units per vertex step at this level.
    float vertexStep = unitsPerTexel * exp2( (float)level );

    // Per-level snap: each level snaps to its own 2-cell increment so its vertices stay on a fixed lattice
    // as the camera moves (a shared finer increment would make coarse levels jitter).
    float increment = vertexStep * 2.0f;
    float2 center = roundToIncrement( g_vClipCameraLocal, increment );

    float2 localXY = center + ( meshlet.BlockOffset + blockLocal ) * vertexStep;

    float texSizeUnits = TextureDimensions2D( tHeightMap, 0 ).x * unitsPerTexel;

    // Fine height (this level's lattice).
    float zFine = tHeightMap.SampleLevel( g_sBilinearClamp, localXY / texSizeUnits, 0 ).r;

    // LOD morph: the outer blocks collapse onto the next-coarser lattice so ring seams stay crack-free.
    float morphEnd = g_flClipMorphEndCells * vertexStep;
    float morphStart = g_flClipMorphStartCells * vertexStep;

    // Measure from the continuous camera, not the snapped center, so the morph band doesn't jump (swim)
    // when the level snaps.
    float dist = max( abs( localXY.x - g_vClipCameraLocal.x ), abs( localXY.y - g_vClipCameraLocal.y ) );
    float alpha = saturate( ( dist - morphStart ) / max( morphEnd - morphStart, 1e-5f ) );

    // Interior vertices (alpha == 0, the vast majority) skip the 4-tap coarse sample entirely.
    float z = zFine;
    if ( alpha > 0.0f )
        z = lerp( zFine, Terrain_SampleHeightLattice( tHeightMap, localXY, vertexStep * 2.0f, texSizeUnits ), alpha );

    outLod = (float)level;
    return float3( localXY, z );
}
