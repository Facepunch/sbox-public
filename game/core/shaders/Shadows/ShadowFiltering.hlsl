#ifndef SHADOWS_SHADOWFILTERING
#define SHADOWS_SHADOWFILTERING

int UserShadowFilterQuality < Attribute( "ShadowFilterQuality" ); >;

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
// 5-tap Poisson disk for low quality PCF
//--------------------------------------------------------------------------------------------------
static const float2 g_vPoissonDisk5[5] =
{
    float2(  0.000000,  0.000000 ),  // Center sample
    float2( -0.707107, -0.707107 ),
    float2(  0.707107, -0.707107 ),
    float2(  0.707107,  0.707107 ),
    float2( -0.707107,  0.707107 ),
};

//--------------------------------------------------------------------------------------------------
// 12-tap Poisson disk for medium quality PCF
// Pre-normalized samples in [-1, 1] range, good spatial distribution
//--------------------------------------------------------------------------------------------------
static const float2 g_vPoissonDisk12[12] =
{
    float2( -0.326212, -0.405805 ),
    float2( -0.840144, -0.073580 ),
    float2( -0.695914,  0.457137 ),
    float2( -0.203345,  0.620716 ),
    float2(  0.962340, -0.194983 ),
    float2(  0.473434, -0.480026 ),
    float2(  0.519456,  0.767022 ),
    float2(  0.185461, -0.893124 ),
    float2(  0.507431,  0.064425 ),
    float2(  0.896420,  0.412458 ),
    float2( -0.321940, -0.932615 ),
    float2( -0.791559, -0.597705 ),
};

//--------------------------------------------------------------------------------------------------
// 16-tap Poisson disk for high quality PCF
//--------------------------------------------------------------------------------------------------
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
// 5-tap rotated Poisson PCF - Low quality
//--------------------------------------------------------------------------------------------------
float SampleShadowPCF_Poisson5( ShadowPCFInput i )
{
    const float flFilterRadius = 1.5;
    float2 vTexelSize = float2( i.InvShadowMapRes, i.InvShadowMapRes );
    float flHardness = i.Hardness;
    
    float flNoise = ShadowNoise( i.ScreenPos );
    float flAngle = flNoise * 6.283185307;
    float flSin, flCos;
    sincos( flAngle, flSin, flCos );
    float2x2 mRotation = float2x2( flCos, -flSin, flSin, flCos );
    
    float2 vFilterScale = flFilterRadius * flHardness * vTexelSize;
    
    float flShadow = 0.0;
    
    [unroll]
    for ( int s = 0; s < 5; s++ )
    {
        float2 vOffset = mul( mRotation, g_vPoissonDisk5[s] ) * vFilterScale;
        float2 vSampleUV = i.ShadowPos.xy + vOffset;
        float flCompareDepth = saturate( i.ShadowPos.z + i.Bias );
        flShadow += i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, vSampleUV, flCompareDepth );
    }
    
    return flShadow / 5.0;
}

//--------------------------------------------------------------------------------------------------
// 12-tap rotated Poisson PCF - Medium quality
//--------------------------------------------------------------------------------------------------
float SampleShadowPCF_Poisson12( ShadowPCFInput i )
{
    const float flFilterRadius = 3.0;
    float2 vTexelSize = float2(i.InvShadowMapRes, i.InvShadowMapRes);
    float flHardness = i.Hardness;
    
    float flNoise = ShadowNoise( i.ScreenPos );
    float flAngle = flNoise * 6.283185307;
    float flSin, flCos;
    sincos( flAngle, flSin, flCos );
    float2x2 mRotation = float2x2( flCos, -flSin, flSin, flCos );
    
    float2 vFilterScale = flFilterRadius * flHardness * vTexelSize;
    
    float flShadow = 0.0;
    
    [unroll]
    for ( int s = 0; s < 12; s++ )
    {
        float2 vOffset = mul( mRotation, g_vPoissonDisk12[s] ) * vFilterScale;
        float2 vSampleUV = i.ShadowPos.xy + vOffset;
        float flCompareDepth = saturate( i.ShadowPos.z + i.Bias );
        flShadow += i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, vSampleUV, flCompareDepth );
    }
    
    return flShadow / 12.0;
}

//--------------------------------------------------------------------------------------------------
// 16-tap rotated Poisson PCF - High quality
//--------------------------------------------------------------------------------------------------
float SampleShadowPCF_Poisson16( ShadowPCFInput i )
{
    const float flFilterRadius = 4.5;
    float2 vTexelSize = float2( i.InvShadowMapRes, i.InvShadowMapRes );
    float flHardness = i.Hardness;
    
    float flNoise = ShadowNoise( i.ScreenPos );
    float flAngle = flNoise * 6.283185307;
    float flSin, flCos;
    sincos( flAngle, flSin, flCos );
    float2x2 mRotation = float2x2( flCos, -flSin, flSin, flCos );
    
    float2 vFilterScale = flFilterRadius * flHardness * vTexelSize;
    
    float flShadow = 0.0;
    
    [unroll]
    for ( int s = 0; s < 16; s++ )
    {
        float2 vOffset = mul( mRotation, g_vPoissonDisk16[s] ) * vFilterScale;
        float2 vSampleUV = i.ShadowPos.xy + vOffset;
        float flCompareDepth = saturate( i.ShadowPos.z + i.Bias );
        flShadow += i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, vSampleUV, flCompareDepth );
    }
    
    return flShadow / 16.0;
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
    else if ( filter == 1 )
    {
        // Low quality - 5 tap rotated Poisson
        return SampleShadowPCF_Poisson5( i );
    }
    else if ( filter == 2 )
    {
        // Medium quality - 12 tap rotated Poisson
        return SampleShadowPCF_Poisson12( i );
    }
    else
    {
        // High quality - 16 tap rotated Poisson
        return SampleShadowPCF_Poisson16( i );
    }
}

#endif
