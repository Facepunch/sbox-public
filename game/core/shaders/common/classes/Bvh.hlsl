#ifndef BVH_HLSL
#define BVH_HLSL

// Compute ray tracing against a Sandbox.Rendering.Bvh, bound with Bvh.Bind. Works in any stage, without RayQuery.

#define BVH_TIE 0.01

struct BvhNode
{
    float3 Mins;
    int Skip;       // The node to visit when this one is missed or finished, the node count at the end
    float3 Maxs;
    int Leaf;       // Triangle count in the low three bits and the first triangle above them, -1 for an inner node
};

struct BvhTriangle
{
    float3 A;
    uint Id;
    float3 AB;
    uint Unused0;
    float3 AC;
    uint Unused1;
};

StructuredBuffer<BvhNode> g_BvhNodes < Attribute( "BvhNodes" ); >;
StructuredBuffer<BvhTriangle> g_BvhTriangles < Attribute( "BvhTriangles" ); >;

struct Bvh
{
    struct Hit
    {
        float Distance;
        uint Id;        // Its triangle's place in the list the Bvh was built from
        float3 Normal;  // Of the triangle's winding, not flipped to face the ray
        float2 Barycentrics; // Along its second and third corners from its first
    };

    // The nearest triangle a ray hits within a distance, or the first one found when bAnyHit
    static bool Trace( float3 vOrigin, float3 vDirection, float flMaxDistance, out Hit hit, bool bAnyHit = false )
    {
        hit.Distance = flMaxDistance;
        hit.Id = 0;
        hit.Normal = float3( 0.0, 0.0, 0.0 );
        hit.Barycentrics = float2( 0.0, 0.0 );

        const float3 vInverse = 1.0 / vDirection;
        bool bFound = false;
        bool bFacing = false;

        // Nodes are depth first, so the walk needs no stack: step into the next node or skip its subtree
        uint nNodes, nStride;
        g_BvhNodes.GetDimensions( nNodes, nStride );

        int i = 0;
        while ( i < (int)nNodes )
        {
            const BvhNode node = g_BvhNodes[i];

            const float3 t0 = ( node.Mins - vOrigin ) * vInverse;
            const float3 t1 = ( node.Maxs - vOrigin ) * vInverse;
            const float3 lo = min( t0, t1 );
            const float3 hi = max( t0, t1 );
            const float flNear = max( max( lo.x, lo.y ), lo.z );
            const float flFar = min( min( hi.x, hi.y ), hi.z );

            if ( flNear > flFar || flFar < 0.0 || flNear > hit.Distance + BVH_TIE )
            {
                i = node.Skip;
                continue;
            }

            if ( node.Leaf < 0 )
            {
                i++;
                continue;
            }

            const int nEnd = ( node.Leaf >> 3 ) + ( node.Leaf & 7 );
            for ( int t = node.Leaf >> 3; t < nEnd; t++ )
            {
                const BvhTriangle tri = g_BvhTriangles[t];

                // Moller-Trumbore
                const float3 p = cross( vDirection, tri.AC );
                const float flDeterminant = dot( tri.AB, p );
                if ( abs( flDeterminant ) < 1e-12 )
                    continue;

                const float3 s = vOrigin - tri.A;
                const float u = dot( s, p ) / flDeterminant;
                const float3 q = cross( s, tri.AB );
                const float v = dot( vDirection, q ) / flDeterminant;
                const float flDistance = dot( tri.AC, q ) / flDeterminant;

                // Of two surfaces in the same place, like rooms sharing a wall, the ray hits the one facing it
                const bool bFront = flDeterminant > 0.0;
                const float flLimit = ( !bFound || bFront == bFacing ) ? hit.Distance : hit.Distance + ( bFront ? BVH_TIE : -BVH_TIE );

                if ( u < 0.0 || v < 0.0 || u + v > 1.0 || flDistance <= 0.0 || flDistance >= flLimit )
                    continue;

                hit.Distance = flDistance;
                hit.Id = tri.Id;
                hit.Normal = normalize( cross( tri.AB, tri.AC ) );
                hit.Barycentrics = float2( u, v );
                bFound = true;
                bFacing = bFront;
            }

            if ( bFound && bAnyHit )
                return true;

            i = node.Skip;
        }

        return bFound;
    }

    // Whether anything blocks a ray within a distance
    static bool Occluded( float3 vOrigin, float3 vDirection, float flMaxDistance )
    {
        Hit hit;
        return Trace( vOrigin, vDirection, flMaxDistance, hit, true );
    }
};

#endif // BVH_HLSL
