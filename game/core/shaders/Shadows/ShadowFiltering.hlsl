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

// Face normal of the shadow receiver, from the screen-space derivatives of its world position.
// Derivatives are only defined in uniform control flow, so compute this once per pixel *before* any
// per-light loop or branch and pass it down: the clustered light loop diverges between the lanes of a
// quad wherever neighbouring pixels land in different clusters, and a ddx taken in there is garbage.
// Non-pixel programs have no derivatives and get no offset.
float3 ComputeShadowReceiverNormal( float3 vPositionWs )
{
#if ( PROGRAM == VFX_PROGRAM_PS )
    return normalize( cross( ddy( vPositionWs ), ddx( vPositionWs ) ) );
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
    float flCompareDepth = saturate( i.ShadowPos.z + i.Bias );

    [unroll]
    for ( int s = 0; s < 5; s++ )
    {
        float2 vOffset = mul( mRotation, g_vPoissonDisk5[s] ) * vFilterScale;
        float2 vSampleUV = i.ShadowPos.xy + vOffset;
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
    float flCompareDepth = saturate( i.ShadowPos.z + i.Bias );

    [unroll]
    for ( int s = 0; s < 12; s++ )
    {
        float2 vOffset = mul( mRotation, g_vPoissonDisk12[s] ) * vFilterScale;
        float2 vSampleUV = i.ShadowPos.xy + vOffset;
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
    float flCompareDepth = saturate( i.ShadowPos.z + i.Bias );

    [unroll]
    for ( int s = 0; s < 16; s++ )
    {
        float2 vOffset = mul( mRotation, g_vPoissonDisk16[s] ) * vFilterScale;
        float2 vSampleUV = i.ShadowPos.xy + vOffset;
        flShadow += i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, vSampleUV, flCompareDepth );
    }

    return flShadow / 16.0;
}

// Morph the original soft Poisson kernel into an elongated tent as hardness increases.
// Interpolate sample positions and weights instead of evaluating both filters (16 taps total).
float SampleDirectionalShadowTent16( ShadowPCFInput i )
{
    if ( i.Hardness <= 1.0 )
    {
        i.Hardness = rcp( i.Hardness );
        return SampleShadowPCF_Poisson16( i );
    }

    float hardness = saturate( ( i.Hardness - 1.0 ) / 3.5 );
    float transition = smoothstep( 0.0, 1.0, hardness );
    float sine, cosine;
    sincos( ShadowNoise( i.ScreenPos ) * 6.283185307, sine, cosine );
    float2x2 rotation = float2x2( cosine, -sine, sine, cosine );
    float poissonRadius = 4.5 * rcp( i.Hardness ) * i.InvShadowMapRes;
    float2 texelPosition = i.ShadowPos.xy / i.InvShadowMapRes - 0.5;
    float2 baseTexel = floor( texelPosition ) - float2( 1.0, 7.0 );
    float2 phase = frac( texelPosition );
    float visibility = 0.0;
    float totalWeight = 0.0;
    [unroll]
    for ( int y = 0; y < 8; ++y )
    {
        float y0 = max( 0.0, 8.0 - abs( 2.0 * y - 7.0 - phase.y ) );
        float y1 = max( 0.0, 8.0 - abs( 2.0 * y - 6.0 - phase.y ) );
        float wy = y0 + y1;
        [unroll]
        for ( int x = 0; x < 2; ++x )
        {
            float x0 = max( 0.0, 2.0 - abs( 2.0 * x - 1.0 - phase.x ) );
            float x1 = max( 0.0, 2.0 - abs( 2.0 * x - 0.0 - phase.x ) );
            float wx = x0 + x1;
            float2 uv = ( baseTexel + float2( 2.0 * x + x1 / wx, 2.0 * y + y1 / wy ) + 0.5 ) * i.InvShadowMapRes;
            float2 poissonUv = i.ShadowPos.xy + mul( rotation, g_vPoissonDisk16[y * 2 + x] ) * poissonRadius;
            uv = lerp( poissonUv, uv, transition );
            // The tent's axis sums are 4 and 64, giving a total weight of 256.
            float weight = lerp( 1.0 / 16.0, wx * wy / 256.0, transition );
            visibility += weight * i.ShadowMap.SampleCmpLevelZero( ShadowDepthPCFSampler, uv, saturate( i.ShadowPos.z + i.Bias ) );
            totalWeight += weight;
        }
    }
    visibility /= totalWeight;
    return lerp( visibility, smoothstep( 0.40, 0.60, visibility ), hardness );
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

