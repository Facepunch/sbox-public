#ifndef SHADOWS_SHADOWFILTERING
#define SHADOWS_SHADOWFILTERING

// A specialization constant: each pipeline has one filter quality, so the driver drops the others and fully
// unrolls the disk loop for its tap count. Changing the setting builds new pipelines.
int UserShadowFilterQuality < Attribute( "ShadowFilterQuality" ); Default( 0 ); SpecializedConstant(); >;

// HW PCF Sampler
SamplerComparisonState ShadowDepthPCFSampler < Filter( COMPARISON_MIN_MAG_MIP_LINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); ComparisonFunc( GREATER_EQUAL ); >;

// Non-comparison point sampler for raw depth fetching via Gather4 (DPCF blocker search)
SamplerState ShadowDepthGatherSampler < Filter( MIN_MAG_MIP_POINT ); AddressU( CLAMP ); AddressV( CLAMP ); >;



//--------------------------------------------------------------------------------------------------
// Input struct for shadow PCF sampling
//--------------------------------------------------------------------------------------------------
struct ShadowPCFInput
{
    Texture2D   ShadowMap;
    float3      ShadowPos;
    float       InvShadowMapRes;
    float       Bias;
    float       Hardness;
    float2      ScreenPos;
};

//--------------------------------------------------------------------------------------------------
// Filter disk taps, generated on a golden-angle (Vogel) spiral instead of read from tables: one loop
// serves every tap count, nothing is indexed at runtime, and the loop needn't be unrolled to stay out
// of local memory. Taps cover the unit disk evenly; their RMS radius is 1/sqrt(2).
//--------------------------------------------------------------------------------------------------
struct ShadowDisk
{
    float2 Direction;
    float InvCount;

    static ShadowDisk Start( float flAngle, uint nTaps )
    {
        ShadowDisk disk;
        sincos( flAngle, disk.Direction.y, disk.Direction.x );
        disk.InvCount = rcp( float( nTaps ) );
        return disk;
    }

    // Tap i in the unit disk, then turn the direction by the golden angle for the next one
    [mutating]
    float2 Next( uint i )
    {
        const float2 vGoldenAngle = float2( -0.7373688, 0.6754903 ); // cos and sin of pi * ( 3 - sqrt( 5 ) )
        float2 vTap = Direction * sqrt( ( float( i ) + 0.5 ) * InvCount );
        Direction = float2( Direction.x * vGoldenAngle.x - Direction.y * vGoldenAngle.y, Direction.x * vGoldenAngle.y + Direction.y * vGoldenAngle.x );
        return vTap;
    }
};

// The 16-tap Poisson disk the shadow filters used before their spiral taps. Nothing here reads it; it stays because shaders
// built against it (a package's own shadow filtering, Little Railways' floor) still name it, and recompile against these files
static const float2 g_vPoissonDisk16[16] =
{
    float2( -0.942016, -0.399062 ),
    float2( -0.094184, -0.938988 ),
    float2(  0.310720, -0.371712 ),
    float2( -0.545396, -0.589939 ),
    float2(  0.140388, -0.040836 ),
    float2(  0.667325,  0.174626 ),
    float2( -0.527440,  0.056346 ),
    float2(  0.346120, -0.935218 ),
    float2( -0.267592,  0.406868 ),
    float2( -0.850940,  0.424726 ),
    float2(  0.206578,  0.570748 ),
    float2( -0.413360,  0.855142 ),
    float2(  0.654718, -0.521624 ),
    float2(  0.954892,  0.016536 ),
    float2(  0.439138,  0.898128 ),
    float2(  0.878554, -0.397416 ),
};

// Face normal of the shadow receiver, from the screen-space derivatives of its world position.
// Prefer the camera-relative position: far from the origin the derivatives can round to 0 (no offset then).
// Derivatives are only defined in uniform control flow, so compute this once per pixel *before* any
// per-light loop or branch and pass it down: the clustered light loop diverges between the lanes of a
// quad wherever neighbouring pixels land in different clusters, and a ddx taken in there is garbage.
// Non-pixel programs have no derivatives and get no offset.
float3 ComputeShadowReceiverNormal( float3 vPositionWs )
{
#if ( PROGRAM == VFX_PROGRAM_PS )
    float3 vNormal = cross( ddy( vPositionWs ), ddx( vPositionWs ) );
    float flLengthSq = dot( vNormal, vNormal );
    return flLengthSq > 0.0f ? vNormal * rsqrt( flLengthSq ) : 0.0f;
#else
    return 0.0f;
#endif
}

// Holbert 2011: offset receiver along face normal by ~PCF kernel radius in texels (kills slope acne).
float3 ApplyShadowNormalOffset( float3 vPositionWs, float3 vNormalWs, float flTexelWorldSize, float flHardness )
{
    // Half-width of the SampleShadowPCF tent below: 2, 3 or 4 texels
    const float flKernelTexels = UserShadowFilterQuality > 0 ? min( UserShadowFilterQuality, 3 ) + 1.0 : 0.0;
    const float flRadiusTexels = flKernelTexels * rcp( max( flHardness, 1.0 ) ) + 1.0;
    return vPositionWs + vNormalWs * ( flTexelWorldSize * flRadiusTexels );
}

//--------------------------------------------------------------------------------------------------
// R2 quasi-random sequence (Martin Roberts 2018)
// Uses the plastic constant (unique real root of x³ = x + 1, p ≈ 1.3247) to achieve
// optimal low-discrepancy distribution in 2D - smoother than blue noise with no texture
// fetch required. The α values are 1/p and 1/p² respectively.
//--------------------------------------------------------------------------------------------------
float ShadowNoise( float2 vScreenPos )
{
    const float2 vAlpha = float2( 0.7548776662466927, 0.5698402909980532 );
    return frac( 0.5 + dot( floor( vScreenPos ), vAlpha ) );
}

//--------------------------------------------------------------------------------------------------
// Tent PCF from weighted bilinear compare taps (Castano 2013, as in Bevy and Esoterica): N x N hardware
// 2x2 PCF taps, placed and weighted so together they filter a tent over 2N x 2N texels. Smooth and
// noise-free: 2, 3 and 4 taps per axis give 4, 9 and 16 fetches. Soft lights (hardness below 1) spread
// the tent over a coarser grid; hard ones (above 1) sharpen it around 0.5.
//--------------------------------------------------------------------------------------------------
float SampleShadowTent( ShadowPCFInput i, uint nAxisTaps )
{
    const float flSpread = max( rcp( i.Hardness ), 1.0 );
    const float flSharpen = max( i.Hardness, 1.0 );
    const float flGrid = i.InvShadowMapRes * flSpread;
    const float flCompareDepth = saturate( i.ShadowPos.z + i.Bias );
    const float flTaps = float( nAxisTaps );

    float2 vGridPosition = i.ShadowPos.xy / flGrid - 0.5;
    float2 vPhase = frac( vGridPosition );

    // Two taps of a row at a time, their fetches in flight together, in loops. Fully unrolled, the driver grouped every
    // fetch with its coordinates and weights, the peak of the lit shaders' registers. Same taps, same order, and the same
    // rounding as unrolled: a tap's whole-texel offsets come from integers, as the constants they were, so the compiler
    // can't regroup the sums that round
    float2 vFloor = floor( vGridPosition );
    float flVisibility = 0.0;
    [loop]
    for ( uint y = 0; y < nAxisTaps; y++ )
    {
        [loop]
        for ( uint x0 = 0; x0 < nAxisTaps; x0 += 2 )
        {
            [unroll]
            for ( uint dx = 0; dx < 2; dx++ )
            {
                uint x = x0 + dx;
                if ( x >= nAxisTaps )
                    break;

                // k - ( taps - 1 ) and k - ( taps - 2 ), k twice the tap's index
                int2 c0 = 2 * int2( x, y ) + 1 - int( nAxisTaps );
                float2 w0 = flTaps - abs( float2( c0 ) - vPhase );
                float2 w1 = flTaps - abs( float2( c0 + 1 ) - vPhase );
                float2 vUV = ( ( vFloor + float2( 2 * c0 + 1 ) * 0.5 ) + w1 / ( w0 + w1 ) ) * flGrid;
                flVisibility += ( w0.x + w1.x ) * ( w0.y + w1.y ) * i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, vUV, flCompareDepth );
            }
        }
    }

    // The weights sum to nAxisTaps^4
    flVisibility /= flTaps * flTaps * flTaps * flTaps;
    return saturate( ( flVisibility - 0.5 ) * flSharpen + 0.5 );
}

//--------------------------------------------------------------------------------------------------
// Spot light and cascade PCF. Quality 0 is one hardware 2x2 tap, then 4, 9 and 16-tap tents.
//--------------------------------------------------------------------------------------------------
float SampleShadowPCF( ShadowPCFInput i )
{
    // Compute effects don't need high quality shadows and can be very performance sensitive, so force low quality PCF for them
    #ifdef FORCE_BILINEAR_PCF_SHADOWS_ONLY
        return i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, i.ShadowPos.xy, saturate( i.ShadowPos.z + i.Bias ) );
    #endif

    uint filter = UserShadowFilterQuality;
    if ( filter == 0 )
        return i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, i.ShadowPos.xy, saturate( i.ShadowPos.z + i.Bias ) );

    return SampleShadowTent( i, filter == 1 ? 2 : filter == 2 ? 3 : 4 );
}

#endif

