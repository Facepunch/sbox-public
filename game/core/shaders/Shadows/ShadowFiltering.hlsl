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
    float2 vBase = floor( vGridPosition ) - ( flTaps - 1.0 );
    float2 vPhase = frac( vGridPosition );

    float flVisibility = 0.0;
    for ( uint s = 0; s < nAxisTaps * nAxisTaps; s++ )
    {
        float2 k = 2.0 * float2( s % nAxisTaps, s / nAxisTaps );
        float2 w0 = flTaps - abs( k - ( flTaps - 1.0 ) - vPhase );
        float2 w1 = flTaps - abs( k - ( flTaps - 2.0 ) - vPhase );
        float2 vUV = ( vBase + k + w1 / ( w0 + w1 ) + 0.5 ) * flGrid;
        flVisibility += ( w0.x + w1.x ) * ( w0.y + w1.y ) * i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, vUV, flCompareDepth );
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

