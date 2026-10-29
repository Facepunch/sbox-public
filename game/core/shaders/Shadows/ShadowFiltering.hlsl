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
    const float flRadiusTexels = 1.5 * min( UserShadowFilterQuality, 3 ) * rcp( max( flHardness, 1.0 ) ) + 1.0; // matches the SampleShadowPCF_* kernels below
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
// Rotated disk PCF: nTaps taps over a disk of flRadius texels (RMS radius), turned per pixel by ShadowNoise
//--------------------------------------------------------------------------------------------------
float SampleShadowPCF_Disk( ShadowPCFInput i, uint nTaps, float flRadius )
{
    // ShadowDisk taps have an RMS radius of 1/sqrt(2): scale them up so flRadius is the RMS radius
    float2 vFilterScale = ( flRadius * 1.41421356 ) * i.Hardness * i.InvShadowMapRes;
    float flCompareDepth = saturate( i.ShadowPos.z + i.Bias );

    ShadowDisk disk = ShadowDisk::Start( ShadowNoise( i.ScreenPos ) * 6.283185307, nTaps );
    float flShadow = 0.0;
    for ( uint s = 0; s < nTaps; s++ )
    {
        float2 vSampleUV = i.ShadowPos.xy + disk.Next( s ) * vFilterScale;
        flShadow += i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, vSampleUV, flCompareDepth );
    }

    return flShadow / nTaps;
}

// Radii keep the RMS radius of the Poisson disks these replaced (1.5, 3 and 4.5 texels across), so
// the blur width matches
float SampleShadowPCF_Poisson5( ShadowPCFInput i ) { return SampleShadowPCF_Disk( i, 5, 1.5 * 0.8944 ); }
float SampleShadowPCF_Poisson12( ShadowPCFInput i ) { return SampleShadowPCF_Disk( i, 12, 3.0 * 0.8368 ); }
float SampleShadowPCF_Poisson16( ShadowPCFInput i ) { return SampleShadowPCF_Disk( i, 16, 4.5 * 0.8118 ); }

// Hard shadows: a smooth, noise-free 8x8 texel tent (16 bilinear taps), sharpened around 0.5 by hardness.
float SampleDirectionalShadowTent16( ShadowPCFInput i )
{
    if ( i.Hardness <= 1.0 )
    {
        i.Hardness = rcp( i.Hardness );
        return SampleShadowPCF_Poisson16( i );
    }

    float2 texelPosition = i.ShadowPos.xy / i.InvShadowMapRes - 0.5;
    float2 baseTexel = floor( texelPosition ) - 3.0;
    float2 phase = frac( texelPosition );
    float visibility = 0.0;
    [unroll]
    for ( int s = 0; s < 16; ++s )
    {
        float2 k = 2.0 * float2( s % 4, s / 4 );
        float2 w0 = 4.0 - abs( k - 3.0 - phase );
        float2 w1 = 4.0 - abs( k - 2.0 - phase );
        float2 uv = ( baseTexel + k + w1 / ( w0 + w1 ) + 0.5 ) * i.InvShadowMapRes;
        visibility += ( w0.x + w1.x ) * ( w0.y + w1.y ) * i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, uv, saturate( i.ShadowPos.z + i.Bias ) );
    }
    return saturate( ( visibility / 256.0 - 0.5 ) * i.Hardness + 0.5 );
}

//--------------------------------------------------------------------------------------------------
// Main PCF sampling function - selects quality based on UserShadowFilterQuality
//--------------------------------------------------------------------------------------------------
float SampleShadowPCF( ShadowPCFInput i )
{
    // Compute effects don't need high quality shadows and can be very performance sensitive, so force low quality PCF for them
    #ifdef FORCE_BILINEAR_PCF_SHADOWS_ONLY
        return i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, i.ShadowPos.xy, saturate( i.ShadowPos.z + i.Bias ) );
    #endif

    uint filter = UserShadowFilterQuality;

    // christ
    i.Hardness = rcp(i.Hardness);

    if ( filter == 0 )
    {
        // Off - single HW PCF sample
        return i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, i.ShadowPos.xy, saturate( i.ShadowPos.z + i.Bias ) );
    }

    // Low, medium and high share one loop: 5, 12 or 16 taps
    uint nTaps = filter == 1 ? 5 : filter == 2 ? 12 : 16;
    float flRadius = filter == 1 ? 1.5 * 0.8944 : filter == 2 ? 3.0 * 0.8368 : 4.5 * 0.8118;
    return SampleShadowPCF_Disk( i, nTaps, flRadius );
}

#endif

