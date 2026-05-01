///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (C) 2016-2021, Intel Corporation 
// 
// SPDX-License-Identifier: MIT
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// XeGTAO is based on GTAO/GTSO "Jimenez et al. / Practical Real-Time Strategies for Accurate Indirect Occlusion", 
// https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion_NEW%20VERSION_COLOR.pdf
// 
// Implementation:  Filip Strugar (filip.strugar@intel.com), Steve Mccalla <stephen.mccalla@intel.com>         (\_/)
// Version:         (see XeGTAO.h)                                                                            (='.'=)
// Details:         https://github.com/GameTechDev/XeGTAO                                                     (")_(")
//
// Version history: see XeGTAO.h
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


#if defined( XE_GTAO_SHOW_NORMALS ) || defined( XE_GTAO_SHOW_EDGES ) || defined( XE_GTAO_SHOW_BENT_NORMALS )
RWTexture2D<float4>         g_outputDbgImage    : register( u2 );
#endif

#define XE_GTAO_PI               	(3.1415926535897932384626433832795)
#define XE_GTAO_PI_HALF             (1.5707963267948966192313216916398)

#ifndef XE_GTAO_USE_HALF_FLOAT_PRECISION
#define XE_GTAO_USE_HALF_FLOAT_PRECISION 1
#endif

#if defined(XE_GTAO_FP32_DEPTHS) && XE_GTAO_USE_HALF_FLOAT_PRECISION
#error Using XE_GTAO_USE_HALF_FLOAT_PRECISION with 32bit depths is not supported yet unfortunately (it is possible to apply fp16 on parts not related to depth but this has not been done yet)
#endif 


#if (XE_GTAO_USE_HALF_FLOAT_PRECISION != 0)
#if 1 // old fp16 approach (<SM6.2)
    typedef min16float      lpfloat; 
    typedef min16float2     lpfloat2;
    typedef min16float3     lpfloat3;
    typedef min16float4     lpfloat4;
    typedef min16float3x3   lpfloat3x3;
#else // new fp16 approach (requires SM6.2 and -enable-16bit-types) - WARNING: perf degradation noticed on some HW, while the old (min16float) path is mostly at least a minor perf gain so this is more useful for quality testing
    typedef float16_t       lpfloat; 
    typedef float16_t2      lpfloat2;
    typedef float16_t3      lpfloat3;
    typedef float16_t4      lpfloat4;
    typedef float16_t3x3    lpfloat3x3;
#endif
#else
    typedef float           lpfloat;
    typedef float2          lpfloat2;
    typedef float3          lpfloat3;
    typedef float4          lpfloat4;
    typedef float3x3        lpfloat3x3;
#endif


//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

// R11G11B10_UNORM <-> float3
float3 XeGTAO_R11G11B10_UNORM_to_FLOAT3( uint packedInput )
{
    float3 unpackedOutput;
    unpackedOutput.x = (float)( ( packedInput       ) & 0x000007ff ) / 2047.0f;
    unpackedOutput.y = (float)( ( packedInput >> 11 ) & 0x000007ff ) / 2047.0f;
    unpackedOutput.z = (float)( ( packedInput >> 22 ) & 0x000003ff ) / 1023.0f;
    return unpackedOutput;
}
// 'unpackedInput' is float3 and not float3 on purpose as half float lacks precision for below!
uint XeGTAO_FLOAT3_to_R11G11B10_UNORM( float3 unpackedInput )
{
    uint packedOutput;
    packedOutput =( ( uint( saturate( unpackedInput.x ) * 2047 + 0.5f ) ) |
        ( uint( saturate( unpackedInput.y ) * 2047 + 0.5f ) << 11 ) |
        ( uint( saturate( unpackedInput.z ) * 1023 + 0.5f ) << 22 ) );
    return packedOutput;
}
//
lpfloat4 XeGTAO_R8G8B8A8_UNORM_to_FLOAT4( uint packedInput )
{
    lpfloat4 unpackedOutput;
    unpackedOutput.x = (lpfloat)( packedInput & 0x000000ff ) / (lpfloat)255;
    unpackedOutput.y = (lpfloat)( ( ( packedInput >> 8 ) & 0x000000ff ) ) / (lpfloat)255;
    unpackedOutput.z = (lpfloat)( ( ( packedInput >> 16 ) & 0x000000ff ) ) / (lpfloat)255;
    unpackedOutput.w = (lpfloat)( packedInput >> 24 ) / (lpfloat)255;
    return unpackedOutput;
}
//
uint XeGTAO_FLOAT4_to_R8G8B8A8_UNORM( lpfloat4 unpackedInput )
{
    return (( uint( saturate( unpackedInput.x ) * (lpfloat)255 + (lpfloat)0.5 ) ) |
            ( uint( saturate( unpackedInput.y ) * (lpfloat)255 + (lpfloat)0.5 ) << 8 ) |
            ( uint( saturate( unpackedInput.z ) * (lpfloat)255 + (lpfloat)0.5 ) << 16 ) |
            ( uint( saturate( unpackedInput.w ) * (lpfloat)255 + (lpfloat)0.5 ) << 24 ) );
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

// Inputs are screen XY and viewspace depth, output is viewspace position
float3 XeGTAO_ComputeViewspacePosition( const float2 screenPos, const float viewspaceDepth, const GTAOConstants consts )
{
    float3 ret;
    ret.xy = (consts.NDCToViewMul * screenPos.xy + consts.NDCToViewAdd) * viewspaceDepth;
    ret.z = viewspaceDepth;
    return ret;
}

float XeGTAO_ScreenSpaceToViewSpaceDepth( const float screenDepth, const GTAOConstants consts )
{
    float depthLinearizeMul = consts.DepthUnpackConsts.x;
    float depthLinearizeAdd = consts.DepthUnpackConsts.y;
    // Optimised version of "-cameraClipNear / (cameraClipFar - projDepth * (cameraClipFar - cameraClipNear)) * cameraClipFar"
    float normalizedDepth = 1.0f - Depth::Normalize( screenDepth );
    return depthLinearizeMul / (depthLinearizeAdd - normalizedDepth);
}

lpfloat4 XeGTAO_CalculateEdges( const lpfloat centerZ, const lpfloat leftZ, const lpfloat rightZ, const lpfloat topZ, const lpfloat bottomZ )
{
    lpfloat4 edgesLRTB = lpfloat4( leftZ, rightZ, topZ, bottomZ ) - (lpfloat)centerZ;

    lpfloat slopeLR = (edgesLRTB.y - edgesLRTB.x) * 0.5;
    lpfloat slopeTB = (edgesLRTB.w - edgesLRTB.z) * 0.5;
    lpfloat4 edgesLRTBSlopeAdjusted = edgesLRTB + lpfloat4( slopeLR, -slopeLR, slopeTB, -slopeTB );
    edgesLRTB = min( abs( edgesLRTB ), abs( edgesLRTBSlopeAdjusted ) );
    return lpfloat4(saturate( ( 1.25 - edgesLRTB / (centerZ * 0.011) ) ));
}

// packing/unpacking for edges; 2 bits per edge mean 4 gradient values (0, 0.33, 0.66, 1) for smoother transitions!
lpfloat XeGTAO_PackEdges( lpfloat4 edgesLRTB )
{
    // integer version:
    // edgesLRTB = saturate(edgesLRTB) * 2.9.xxxx + 0.5.xxxx;
    // return (((uint)edgesLRTB.x) << 6) + (((uint)edgesLRTB.y) << 4) + (((uint)edgesLRTB.z) << 2) + (((uint)edgesLRTB.w));
    // 
    // optimized, should be same as above
    edgesLRTB = round( saturate( edgesLRTB ) * 2.9 );
    return dot( edgesLRTB, lpfloat4( 64.0 / 255.0, 16.0 / 255.0, 4.0 / 255.0, 1.0 / 255.0 ) ) ;
}

float3 XeGTAO_CalculateNormal( const float4 edgesLRTB, float3 pixCenterPos, float3 pixLPos, float3 pixRPos, float3 pixTPos, float3 pixBPos )
{
    // Get this pixel's viewspace normal
    float4 acceptedNormals  = saturate( float4( edgesLRTB.x*edgesLRTB.z, edgesLRTB.z*edgesLRTB.y, edgesLRTB.y*edgesLRTB.w, edgesLRTB.w*edgesLRTB.x ) + 0.01 );

    pixLPos = normalize(pixLPos - pixCenterPos);
    pixRPos = normalize(pixRPos - pixCenterPos);
    pixTPos = normalize(pixTPos - pixCenterPos);
    pixBPos = normalize(pixBPos - pixCenterPos);

    float3 pixelNormal =  acceptedNormals.x * cross( pixLPos, pixTPos ) +
                        + acceptedNormals.y * cross( pixTPos, pixRPos ) +
                        + acceptedNormals.z * cross( pixRPos, pixBPos ) +
                        + acceptedNormals.w * cross( pixBPos, pixLPos );
    pixelNormal = normalize( pixelNormal );

    return pixelNormal;
}

#ifdef XE_GTAO_SHOW_DEBUG_VIZ
float4 DbgGetSliceColor(int slice, int sliceCount, bool mirror)
{
    float red = (float)slice / (float)sliceCount; float green = 0.01; float blue = 1.0 - (float)slice / (float)sliceCount;
    return (mirror)?(float4(blue, green, red, 0.9)):(float4(red, green, blue, 0.9));
}
#endif

// http://h14s.p5r.org/2012/09/0x5f3759df.html, [Drobot2014a] Low Level Optimizations for GCN, https://blog.selfshadow.com/publications/s2016-shading-course/activision/s2016_pbs_activision_occlusion.pdf slide 63
lpfloat XeGTAO_FastSqrt( float x )
{
    return (lpfloat)(asfloat( 0x1fbd1df5 + ( asint( x ) >> 1 ) ));
}
// input [-1, 1] and output [0, PI], from https://seblagarde.wordpress.com/2014/12/01/inverse-trigonometric-functions-gpu-optimization-for-amd-gcn-architecture/
lpfloat XeGTAO_FastACos( lpfloat inX )
{ 
    const lpfloat PI = 3.141593;
    const lpfloat HALF_PI = 1.570796;
    lpfloat x = abs(inX); 
    lpfloat res = -0.156583 * x + HALF_PI; 
    res *= XeGTAO_FastSqrt(1.0 - x); 
    return (inX >= 0) ? res : PI - res; 
}

uint XeGTAO_EncodeVisibilityBentNormal( lpfloat visibility, lpfloat3 bentNormal )
{
    return XeGTAO_FLOAT4_to_R8G8B8A8_UNORM( lpfloat4( bentNormal * 0.5 + 0.5, visibility ) );
}

void XeGTAO_DecodeVisibilityBentNormal( const uint packedValue, out lpfloat visibility, out lpfloat3 bentNormal )
{
    lpfloat4 decoded = XeGTAO_R8G8B8A8_UNORM_to_FLOAT4( packedValue );
    bentNormal = decoded.xyz * 2.0.xxx - 1.0.xxx;   // could normalize - don't want to since it's done so many times, better to do it at the final step only
    visibility = decoded.w;
}

void XeGTAO_OutputWorkingTerm( const uint2 pixCoord, lpfloat visibility, lpfloat3 bentNormal, RWTexture2D<float> outWorkingAOTerm )
{
    //visibility = saturate( visibility / lpfloat(XE_GTAO_OCCLUSION_TERM_SCALE) );
#ifdef XE_GTAO_COMPUTE_BENT_NORMALS
    outWorkingAOTerm[pixCoord] = XeGTAO_EncodeVisibilityBentNormal( visibility, bentNormal );
#else
    outWorkingAOTerm[pixCoord] = visibility;
#endif
}

// "Efficiently building a matrix to rotate one vector to another"
// http://cs.brown.edu/research/pubs/pdfs/1999/Moller-1999-EBA.pdf / https://dl.acm.org/doi/10.1080/10867651.1999.10487509
// (using https://github.com/assimp/assimp/blob/master/include/assimp/matrix3x3.inl#L275 as a code reference as it seems to be best)
lpfloat3x3 XeGTAO_RotFromToMatrix( lpfloat3 from, lpfloat3 to )
{
    const lpfloat e       = dot(from, to);
    const lpfloat f       = abs(e); //(e < 0)? -e:e;

    // WARNING: This has not been tested/worked through, especially not for 16bit floats; seems to work in our special use case (from is always {0, 0, -1}) but wouldn't use it in general
    if( f > lpfloat( 1.0 - 0.0003 ) )
        return lpfloat3x3( 1, 0, 0, 0, 1, 0, 0, 0, 1 );

    const lpfloat3 v      = cross( from, to );
    /* ... use this hand optimized version (9 mults less) */
    const lpfloat h       = (1.0)/(1.0 + e);      /* optimization by Gottfried Chen */
    const lpfloat hvx     = h * v.x;
    const lpfloat hvz     = h * v.z;
    const lpfloat hvxy    = hvx * v.y;
    const lpfloat hvxz    = hvx * v.z;
    const lpfloat hvyz    = hvz * v.y;

    lpfloat3x3 mtx;
    mtx[0][0] = e + hvx * v.x;
    mtx[0][1] = hvxy - v.z;
    mtx[0][2] = hvxz + v.y;

    mtx[1][0] = hvxy + v.z;
    mtx[1][1] = e + h * v.y * v.y;
    mtx[1][2] = hvyz - v.x;

    mtx[2][0] = hvxz - v.y;
    mtx[2][1] = hvyz + v.x;
    mtx[2][2] = e + hvz * v.z;

    return mtx;
}

void XeGTAO_MainPass( const uint2 pixCoord, lpfloat sliceCount, lpfloat stepsPerSlice, const lpfloat2 localNoise, lpfloat3 viewspaceNormal, const GTAOConstants consts, 
    Texture2D<float> sourceViewspaceDepth, SamplerState depthSampler, RWTexture2D<float> outWorkingAOTerm, RWTexture2D<float> outWorkingEdges )
{                                                                       
    float2 normalizedScreenPos = (pixCoord + 0.5.xx) * consts.ViewportPixelSize;

    lpfloat4 valuesUL   = (lpfloat4)sourceViewspaceDepth.GatherRed( depthSampler, float2( pixCoord * consts.ViewportPixelSize )               );
    lpfloat4 valuesBR   = (lpfloat4)sourceViewspaceDepth.GatherRed( depthSampler, float2( pixCoord * consts.ViewportPixelSize ), int2( 1, 1 ) );

    // viewspace Z at the center
    lpfloat viewspaceZ  = valuesUL.y; //sourceViewspaceDepth.SampleLevel( depthSampler, normalizedScreenPos, 0 ).x; 

    // viewspace Zs left top right bottom
    const lpfloat pixLZ = valuesUL.x;
    const lpfloat pixTZ = valuesUL.z;
    const lpfloat pixRZ = valuesBR.z;
    const lpfloat pixBZ = valuesBR.x;

    lpfloat4 edgesLRTB  = XeGTAO_CalculateEdges( (lpfloat)viewspaceZ, (lpfloat)pixLZ, (lpfloat)pixRZ, (lpfloat)pixTZ, (lpfloat)pixBZ );
    outWorkingEdges[pixCoord] = XeGTAO_PackEdges(edgesLRTB);

	// Generating screen space normals in-place is faster than generating normals in a separate pass but requires
	// use of 32bit depth buffer (16bit works but visibly degrades quality) which in turn slows everything down. So to
	// reduce complexity and allow for screen space normal reuse by other effects, we've pulled it out into a separate
	// pass.
	// However, we leave this code in, in case anyone has a use-case where it fits better.
#ifdef XE_GTAO_GENERATE_NORMALS_INPLACE
    float3 CENTER   = XeGTAO_ComputeViewspacePosition( normalizedScreenPos, viewspaceZ, consts );
    float3 LEFT     = XeGTAO_ComputeViewspacePosition( normalizedScreenPos + float2(-1,  0) * consts.ViewportPixelSize, pixLZ, consts );
    float3 RIGHT    = XeGTAO_ComputeViewspacePosition( normalizedScreenPos + float2( 1,  0) * consts.ViewportPixelSize, pixRZ, consts );
    float3 TOP      = XeGTAO_ComputeViewspacePosition( normalizedScreenPos + float2( 0, -1) * consts.ViewportPixelSize, pixTZ, consts );
    float3 BOTTOM   = XeGTAO_ComputeViewspacePosition( normalizedScreenPos + float2( 0,  1) * consts.ViewportPixelSize, pixBZ, consts );
    viewspaceNormal = (lpfloat3)XeGTAO_CalculateNormal( edgesLRTB, CENTER, LEFT, RIGHT, TOP, BOTTOM );
#endif

    // Move center pixel slightly towards camera to avoid imprecision artifacts due to depth buffer imprecision; offset depends on depth texture format used
#ifdef XE_GTAO_FP32_DEPTHS
    viewspaceZ *= 0.99999;     // this is good for FP32 depth buffer
#else
    viewspaceZ *= 0.99920;     // this is good for FP16 depth buffer
#endif

    const float3 pixCenterPos   = XeGTAO_ComputeViewspacePosition( normalizedScreenPos, viewspaceZ, consts );
    const lpfloat3 viewVec      = (lpfloat3)normalize(-pixCenterPos);
    
    // prevents normals that are facing away from the view vector - xeGTAO struggles with extreme cases, but in Vanilla it seems rare so it's disabled by default
    // viewspaceNormal = normalize( viewspaceNormal + max( 0, -dot( viewspaceNormal, viewVec ) ) * viewVec );

#ifdef XE_GTAO_SHOW_NORMALS
    g_outputDbgImage[pixCoord] = float4( DisplayNormalSRGB( viewspaceNormal.xyz ), 1 );
#endif

#ifdef XE_GTAO_SHOW_EDGES
    g_outputDbgImage[pixCoord] = 1.0 - float4( edgesLRTB.x, edgesLRTB.y * 0.5 + edgesLRTB.w * 0.5, edgesLRTB.z, 1.0 );
#endif

#if XE_GTAO_USE_DEFAULT_CONSTANTS != 0
    const lpfloat effectRadius              = (lpfloat)consts.EffectRadius * (lpfloat)XE_GTAO_DEFAULT_RADIUS_MULTIPLIER;
    const lpfloat sampleDistributionPower   = (lpfloat)XE_GTAO_DEFAULT_SAMPLE_DISTRIBUTION_POWER;
    const lpfloat thinOccluderCompensation  = (lpfloat)XE_GTAO_DEFAULT_THIN_OCCLUDER_COMPENSATION;
    const lpfloat falloffRange              = (lpfloat)XE_GTAO_DEFAULT_FALLOFF_RANGE * effectRadius;
#else
    const lpfloat effectRadius              = (lpfloat)consts.EffectRadius * (lpfloat)consts.RadiusMultiplier;
    const lpfloat sampleDistributionPower   = (lpfloat)consts.SampleDistributionPower;
    const lpfloat thinOccluderCompensation  = (lpfloat)consts.ThinOccluderCompensation;
    const lpfloat falloffRange              = (lpfloat)consts.EffectFalloffRange * effectRadius;
#endif

    const lpfloat falloffFrom       = effectRadius * ((lpfloat)1-(lpfloat)consts.EffectFalloffRange);

    // fadeout precompute optimisation
    const lpfloat falloffMul        = (lpfloat)-1.0 / ( falloffRange );
    const lpfloat falloffAdd        = falloffFrom / ( falloffRange ) + (lpfloat)1.0;

    lpfloat visibility = 0;
#ifdef XE_GTAO_COMPUTE_BENT_NORMALS
    lpfloat3 bentNormal = 0;
#else
    lpfloat3 bentNormal = viewspaceNormal;
#endif

#ifdef XE_GTAO_SHOW_DEBUG_VIZ
    float3 dbgWorldPos          = mul(g_globals.ViewInv, float4(pixCenterPos, 1)).xyz;
#endif

    // see "Algorithm 1" in https://www.activision.com/cdn/research/Practical_Real_Time_Strategies_for_Accurate_Indirect_Occlusion_NEW%20VERSION_COLOR.pdf
    {
        const lpfloat noiseSlice  = (lpfloat)localNoise.x;
        const lpfloat noiseSample = (lpfloat)localNoise.y;

        // quality settings / tweaks / hacks
        const lpfloat pixelTooCloseThreshold  = 1.0;      // if the offset is under approx pixel size (pixelTooCloseThreshold), push it out to the minimum distance

        // approx viewspace pixel size at pixCoord; approximation of NDCToViewspace( normalizedScreenPos.xy + consts.ViewportPixelSize.xy, pixCenterPos.z ).xy - pixCenterPos.xy;
        const float2 pixelDirRBViewspaceSizeAtCenterZ = viewspaceZ.xx * consts.NDCToViewMul_x_PixelSize;

        lpfloat screenspaceRadius   = effectRadius / (lpfloat)pixelDirRBViewspaceSizeAtCenterZ.x;

        // fade out for small screen radii 
        visibility += saturate((10 - screenspaceRadius)/100)*0.5;

#if 1   // sensible early-out for even more performance; disabled because not yet tested
        // Sam - looks fine
        [branch]
        if( screenspaceRadius < pixelTooCloseThreshold )
        {
            XeGTAO_OutputWorkingTerm( pixCoord, 1, viewspaceNormal, outWorkingAOTerm );
            return;
        }
#endif

#ifdef XE_GTAO_SHOW_DEBUG_VIZ
        [branch] if (IsUnderCursorRange(pixCoord, int2(1, 1)))
        {
            float3 dbgWorldNorm     = mul((float3x3)g_globals.ViewInv, viewspaceNormal).xyz;
            float3 dbgWorldViewVec  = mul((float3x3)g_globals.ViewInv, viewVec).xyz;
            //DebugDraw3DArrow(dbgWorldPos, dbgWorldPos + 0.5 * dbgWorldViewVec, 0.02, float4(0, 1, 0, 0.95));
            //DebugDraw2DCircle(pixCoord, screenspaceRadius, float4(1, 0, 0.2, 1));
            DebugDraw3DSphere(dbgWorldPos, effectRadius, float4(1, 0.2, 0, 0.1));
            //DebugDraw3DText(dbgWorldPos, float2(0, 0), float4(0.6, 0.3, 0.3, 1), float4( pixelDirRBViewspaceSizeAtCenterZ.xy, 0, screenspaceRadius) );
        }
#endif

        // this is the min distance to start sampling from to avoid sampling from the center pixel (no useful data obtained from sampling center pixel)
        const lpfloat minS = (lpfloat)pixelTooCloseThreshold / screenspaceRadius;

        //[unroll]
        for( lpfloat slice = 0; slice < sliceCount; slice++ )
        {
            lpfloat sliceK = (slice+noiseSlice) / sliceCount;
            // lines 5, 6 from the paper
            lpfloat phi = sliceK * XE_GTAO_PI;
            lpfloat cosPhi = cos(phi);
            lpfloat sinPhi = sin(phi);
            lpfloat2 omega = lpfloat2(cosPhi, -sinPhi);       //lpfloat2 on omega causes issues with big radii

            // convert to screen units (pixels) for later use
            omega *= screenspaceRadius;

            // line 8 from the paper
            const lpfloat3 directionVec = lpfloat3(cosPhi, sinPhi, 0);

            // line 9 from the paper
            const lpfloat3 orthoDirectionVec = directionVec - (dot(directionVec, viewVec) * viewVec);

            // line 10 from the paper
            //axisVec is orthogonal to directionVec and viewVec, used to define projectedNormal
            const lpfloat3 axisVec = normalize( cross(orthoDirectionVec, viewVec) );

            // alternative line 9 from the paper
            // float3 orthoDirectionVec = cross( viewVec, axisVec );

            // line 11 from the paper
            lpfloat3 projectedNormalVec = viewspaceNormal - axisVec * dot(viewspaceNormal, axisVec);

            // line 13 from the paper
            lpfloat signNorm = (lpfloat)sign( dot( orthoDirectionVec, projectedNormalVec ) );

            // line 14 from the paper
            lpfloat projectedNormalVecLength = length(projectedNormalVec);
            lpfloat cosNorm = (lpfloat)saturate(dot(projectedNormalVec, viewVec) / projectedNormalVecLength);

            // line 15 from the paper
            lpfloat n = signNorm * XeGTAO_FastACos(cosNorm);

            // this is a lower weight target; not using -1 as in the original paper because it is under horizon, so a 'weight' has different meaning based on the normal
            const lpfloat lowHorizonCos0  = cos(n+XE_GTAO_PI_HALF);
            const lpfloat lowHorizonCos1  = cos(n-XE_GTAO_PI_HALF);

            // lines 17, 18 from the paper, manually unrolled the 'side' loop
            lpfloat horizonCos0           = lowHorizonCos0; //-1;
            lpfloat horizonCos1           = lowHorizonCos1; //-1;

            [unroll]
            for( lpfloat step = 0; step < stepsPerSlice; step++ )
            {
                // R1 sequence (http://extremelearning.com.au/unreasonable-effectiveness-of-quasirandom-sequences/)
                const lpfloat stepBaseNoise = lpfloat(slice + step * stepsPerSlice) * 0.6180339887498948482; // <- this should unroll
                lpfloat stepNoise = frac(noiseSample + stepBaseNoise);

                // approx line 20 from the paper, with added noise
                lpfloat s = (step+stepNoise) / (stepsPerSlice); // + (lpfloat2)1e-6f);

                // additional distribution modifier
                s       = (lpfloat)pow( s, (lpfloat)sampleDistributionPower );

                // avoid sampling center pixel
                s       += minS;

                // approx lines 21-22 from the paper, unrolled
                lpfloat2 sampleOffset = s * omega;

                lpfloat sampleOffsetLength = length( sampleOffset );

                // note: when sampling, using point_point_point or point_point_linear sampler works, but linear_linear_linear will cause unwanted interpolation between neighbouring depth values on the same MIP level!
                const lpfloat mipLevel    = (lpfloat)clamp( log2( sampleOffsetLength ) - consts.DepthMIPSamplingOffset, 0, XE_GTAO_DEPTH_MIP_LEVELS );

                // Snap to pixel center (more correct direction math, avoids artifacts due to sampling pos not matching depth texel center - messes up slope - but adds other 
                // artifacts due to them being pushed off the slice). Also use full precision for high res cases.
                sampleOffset = round(sampleOffset) * (lpfloat2)consts.ViewportPixelSize;

#ifdef XE_GTAO_SHOW_DEBUG_VIZ
                int mipLevelU = (int)round(mipLevel);
                float4 mipColor = saturate( float4( mipLevelU>=3, mipLevelU>=1 && mipLevelU<=3, mipLevelU<=1, 1.0 ) );
                if( all( sampleOffset == 0 ) )
                    DebugDraw2DText( pixCoord, float4( 1, 0, 0, 1), pixelTooCloseThreshold );
                [branch] if (IsUnderCursorRange(pixCoord, int2(1, 1)))
                {
                    //DebugDraw2DText( (normalizedScreenPos + sampleOffset) * consts.ViewportSize, mipColor, mipLevelU );
                    //DebugDraw2DText( (normalizedScreenPos + sampleOffset) * consts.ViewportSize, mipColor, (uint)slice );
                    //DebugDraw2DText( (normalizedScreenPos - sampleOffset) * consts.ViewportSize, mipColor, (uint)slice );
                    //DebugDraw2DText( (normalizedScreenPos - sampleOffset) * consts.ViewportSize, saturate( float4( mipLevelU>=3, mipLevelU>=1 && mipLevelU<=3, mipLevelU<=1, 1.0 ) ), mipLevelU );
                }
#endif

                float2 sampleScreenPos0 = normalizedScreenPos + sampleOffset;
                float  SZ0 = sourceViewspaceDepth.SampleLevel( depthSampler, sampleScreenPos0, mipLevel ).x;
                float3 samplePos0 = XeGTAO_ComputeViewspacePosition( sampleScreenPos0, SZ0, consts );

                float2 sampleScreenPos1 = normalizedScreenPos - sampleOffset;
                float  SZ1 = sourceViewspaceDepth.SampleLevel( depthSampler, sampleScreenPos1, mipLevel ).x;
                float3 samplePos1 = XeGTAO_ComputeViewspacePosition( sampleScreenPos1, SZ1, consts );

                float3 sampleDelta0     = (samplePos0 - float3(pixCenterPos)); // using lpfloat for sampleDelta causes precision issues
                float3 sampleDelta1     = (samplePos1 - float3(pixCenterPos)); // using lpfloat for sampleDelta causes precision issues
                lpfloat sampleDist0     = (lpfloat)length( sampleDelta0 );
                lpfloat sampleDist1     = (lpfloat)length( sampleDelta1 );

                // approx lines 23, 24 from the paper, unrolled
                lpfloat3 sampleHorizonVec0 = (lpfloat3)(sampleDelta0 / sampleDist0);
                lpfloat3 sampleHorizonVec1 = (lpfloat3)(sampleDelta1 / sampleDist1);

                // any sample out of radius should be discarded - also use fallof range for smooth transitions; this is a modified idea from "4.3 Implementation details, Bounding the sampling area"
#if XE_GTAO_USE_DEFAULT_CONSTANTS != 0 && XE_GTAO_DEFAULT_THIN_OBJECT_HEURISTIC == 0
                lpfloat weight0         = saturate( sampleDist0 * falloffMul + falloffAdd );
                lpfloat weight1         = saturate( sampleDist1 * falloffMul + falloffAdd );
#else
                // this is our own thickness heuristic that relies on sooner discarding samples behind the center
                lpfloat falloffBase0    = length( lpfloat3(sampleDelta0.x, sampleDelta0.y, sampleDelta0.z * (1+thinOccluderCompensation) ) );
                lpfloat falloffBase1    = length( lpfloat3(sampleDelta1.x, sampleDelta1.y, sampleDelta1.z * (1+thinOccluderCompensation) ) );
                lpfloat weight0         = saturate( falloffBase0 * falloffMul + falloffAdd );
                lpfloat weight1         = saturate( falloffBase1 * falloffMul + falloffAdd );
#endif
                // sample horizon cos
                lpfloat shc0 = (lpfloat)dot(sampleHorizonVec0, viewVec);
                lpfloat shc1 = (lpfloat)dot(sampleHorizonVec1, viewVec);

                // discard unwanted samples
                shc0 = lerp( lowHorizonCos0, shc0, weight0 ); // this would be more correct but too expensive: cos(lerp( acos(lowHorizonCos0), acos(shc0), weight0 ));
                shc1 = lerp( lowHorizonCos1, shc1, weight1 ); // this would be more correct but too expensive: cos(lerp( acos(lowHorizonCos1), acos(shc1), weight1 ));

                // thickness heuristic - see "4.3 Implementation details, Height-field assumption considerations"
#if 0   // (disabled, not used) this should match the paper
                lpfloat newhorizonCos0 = max( horizonCos0, shc0 );
                lpfloat newhorizonCos1 = max( horizonCos1, shc1 );
                horizonCos0 = (horizonCos0 > shc0)?( lerp( newhorizonCos0, shc0, thinOccluderCompensation ) ):( newhorizonCos0 );
                horizonCos1 = (horizonCos1 > shc1)?( lerp( newhorizonCos1, shc1, thinOccluderCompensation ) ):( newhorizonCos1 );
#elif 0 // (disabled, not used) this is slightly different from the paper but cheaper and provides very similar results
                horizonCos0 = lerp( max( horizonCos0, shc0 ), shc0, thinOccluderCompensation );
                horizonCos1 = lerp( max( horizonCos1, shc1 ), shc1, thinOccluderCompensation );
#else   // this is a version where thicknessHeuristic is completely disabled
                horizonCos0 = max( horizonCos0, shc0 );
                horizonCos1 = max( horizonCos1, shc1 );
#endif


#ifdef XE_GTAO_SHOW_DEBUG_VIZ
                [branch] if (IsUnderCursorRange(pixCoord, int2(1, 1)))
                {
                    float3 WS_samplePos0 = mul(g_globals.ViewInv, float4(samplePos0, 1)).xyz;
                    float3 WS_samplePos1 = mul(g_globals.ViewInv, float4(samplePos1, 1)).xyz;
                    float3 WS_sampleHorizonVec0 = mul( (float3x3)g_globals.ViewInv, sampleHorizonVec0).xyz;
                    float3 WS_sampleHorizonVec1 = mul( (float3x3)g_globals.ViewInv, sampleHorizonVec1).xyz;
                    // DebugDraw3DSphere( WS_samplePos0, effectRadius * 0.02, DbgGetSliceColor(slice, sliceCount, false) );
                    // DebugDraw3DSphere( WS_samplePos1, effectRadius * 0.02, DbgGetSliceColor(slice, sliceCount, true) );
                    DebugDraw3DSphere( WS_samplePos0, effectRadius * 0.02, mipColor );
                    DebugDraw3DSphere( WS_samplePos1, effectRadius * 0.02, mipColor );
                    // DebugDraw3DArrow( WS_samplePos0, WS_samplePos0 - WS_sampleHorizonVec0, 0.002, float4(1, 0, 0, 1 ) );
                    // DebugDraw3DArrow( WS_samplePos1, WS_samplePos1 - WS_sampleHorizonVec1, 0.002, float4(1, 0, 0, 1 ) );
                    // DebugDraw3DText( WS_samplePos0, float2(0,  0), float4( 1, 0, 0, 1), weight0 );
                    // DebugDraw3DText( WS_samplePos1, float2(0,  0), float4( 1, 0, 0, 1), weight1 );

                    // DebugDraw2DText( float2( 500, 94+(step+slice*3)*12 ), float4( 0, 1, 0, 1 ), float4( projectedNormalVecLength, 0, horizonCos0, horizonCos1 ) );
                }
#endif
            }

#if 1       // I can't figure out the slight overdarkening on high slopes, so I'm adding this fudge - in the training set, 0.05 is close (PSNR 21.34) to disabled (PSNR 21.45)
            projectedNormalVecLength = lerp( projectedNormalVecLength, 1, 0.05 );
#endif

            // line ~27, unrolled
            lpfloat h0 = -XeGTAO_FastACos((lpfloat)horizonCos1);
            lpfloat h1 = XeGTAO_FastACos((lpfloat)horizonCos0);
#if 0       // we can skip clamping for a tiny little bit more performance
            h0 = n + clamp( h0-n, (lpfloat)-XE_GTAO_PI_HALF, (lpfloat)XE_GTAO_PI_HALF );
            h1 = n + clamp( h1-n, (lpfloat)-XE_GTAO_PI_HALF, (lpfloat)XE_GTAO_PI_HALF );
#endif
            lpfloat iarc0 = ((lpfloat)cosNorm + (lpfloat)2 * (lpfloat)h0 * (lpfloat)sin(n)-(lpfloat)cos((lpfloat)2 * (lpfloat)h0-n))/(lpfloat)4;
            lpfloat iarc1 = ((lpfloat)cosNorm + (lpfloat)2 * (lpfloat)h1 * (lpfloat)sin(n)-(lpfloat)cos((lpfloat)2 * (lpfloat)h1-n))/(lpfloat)4;
            lpfloat localVisibility = (lpfloat)projectedNormalVecLength * (lpfloat)(iarc0+iarc1);
            visibility += localVisibility;

#ifdef XE_GTAO_COMPUTE_BENT_NORMALS
            // see "Algorithm 2 Extension that computes bent normals b."
            lpfloat t0 = (6*sin(h0-n)-sin(3*h0-n)+6*sin(h1-n)-sin(3*h1-n)+16*sin(n)-3*(sin(h0+n)+sin(h1+n)))/12;
            lpfloat t1 = (-cos(3 * h0-n)-cos(3 * h1-n) +8 * cos(n)-3 * (cos(h0+n) +cos(h1+n)))/12;
            lpfloat3 localBentNormal = lpfloat3( directionVec.x * (lpfloat)t0, directionVec.y * (lpfloat)t0, -lpfloat(t1) );
            localBentNormal = (lpfloat3)mul( XeGTAO_RotFromToMatrix( lpfloat3(0,0,-1), viewVec ), localBentNormal ) * projectedNormalVecLength;
            bentNormal += localBentNormal;
#endif
        }
        visibility /= (lpfloat)sliceCount;
        visibility = pow( visibility, (lpfloat)consts.FinalValuePower );
        visibility = max( (lpfloat)0.03, visibility ); // disallow total occlusion (which wouldn't make any sense anyhow since pixel is visible but also helps with packing bent normals)

#ifdef XE_GTAO_COMPUTE_BENT_NORMALS
        bentNormal = normalize(bentNormal) ;
#endif
    }

#if defined(XE_GTAO_SHOW_DEBUG_VIZ) && defined(XE_GTAO_COMPUTE_BENT_NORMALS)
    [branch] if (IsUnderCursorRange(pixCoord, int2(1, 1)))
    {
        float3 dbgWorldViewNorm = mul((float3x3)g_globals.ViewInv, viewspaceNormal).xyz;
        float3 dbgWorldBentNorm = mul((float3x3)g_globals.ViewInv, bentNormal).xyz;
        DebugDraw3DSphereCone( dbgWorldPos, dbgWorldViewNorm, 0.3, VA_PI*0.5 - acos(saturate(visibility)), float4( 0.2, 0.2, 0.2, 0.5 ) );
        DebugDraw3DSphereCone( dbgWorldPos, dbgWorldBentNorm, 0.3, VA_PI*0.5 - acos(saturate(visibility)), float4( 0.0, 1.0, 0.0, 0.7 ) );
    }
#endif
    //visibility = RemapValClamped(visibility, 0.0f, 0.8, 0.0, 1.0f);
    XeGTAO_OutputWorkingTerm( pixCoord, visibility, bentNormal, outWorkingAOTerm );
}

// weighted average depth filter
lpfloat XeGTAO_DepthMIPFilter( lpfloat depth0, lpfloat depth1, lpfloat depth2, lpfloat depth3, const GTAOConstants consts )
{
    lpfloat maxDepth = max( max( depth0, depth1 ), max( depth2, depth3 ) );

    const lpfloat depthRangeScaleFactor = 0.75; // found empirically :)
#if XE_GTAO_USE_DEFAULT_CONSTANTS != 0
    const lpfloat effectRadius              = depthRangeScaleFactor * (lpfloat)consts.EffectRadius * (lpfloat)XE_GTAO_DEFAULT_RADIUS_MULTIPLIER;
    const lpfloat falloffRange              = (lpfloat)XE_GTAO_DEFAULT_FALLOFF_RANGE * effectRadius;
#else
    const lpfloat effectRadius              = depthRangeScaleFactor * (lpfloat)consts.EffectRadius * (lpfloat)consts.RadiusMultiplier;
    const lpfloat falloffRange              = (lpfloat)consts.EffectFalloffRange * effectRadius;
#endif
    const lpfloat falloffFrom       = effectRadius * ((lpfloat)1-(lpfloat)consts.EffectFalloffRange);
    // fadeout precompute optimisation
    const lpfloat falloffMul        = (lpfloat)-1.0 / ( falloffRange );
    const lpfloat falloffAdd        = falloffFrom / ( falloffRange ) + (lpfloat)1.0;

    lpfloat weight0 = saturate( (maxDepth-depth0) * falloffMul + falloffAdd );
    lpfloat weight1 = saturate( (maxDepth-depth1) * falloffMul + falloffAdd );
    lpfloat weight2 = saturate( (maxDepth-depth2) * falloffMul + falloffAdd );
    lpfloat weight3 = saturate( (maxDepth-depth3) * falloffMul + falloffAdd );

    lpfloat weightSum = weight0 + weight1 + weight2 + weight3;
    return (weight0 * depth0 + weight1 * depth1 + weight2 * depth2 + weight3 * depth3) / weightSum;
}

// This is also a good place to do non-linear depth conversion for cases where one wants the 'radius' (effectively the threshold between near-field and far-field GI), 
// is required to be non-linear (i.e. very large outdoors environments).
lpfloat XeGTAO_ClampDepth( float depth )
{
#ifdef XE_GTAO_USE_HALF_FLOAT_PRECISION
    return (lpfloat)clamp( depth, 0.0, 65504.0 );
#else
    return clamp( depth, 0.0, 3.402823466e+38 );
#endif
}

groupshared lpfloat g_scratchDepths[8][8];
void XeGTAO_PrefilterDepths16x16( uint2 dispatchThreadID /*: SV_DispatchThreadID*/, uint2 groupThreadID /*: SV_GroupThreadID*/, const GTAOConstants consts, Texture2D sourceNDCDepth, SamplerState depthSampler, RWTexture2D<float> outDepth0, RWTexture2D<float> outDepth1, RWTexture2D<float> outDepth2, RWTexture2D<float> outDepth3, RWTexture2D<float> outDepth4 )
{
    // MIP 0
    const uint2 baseCoord = dispatchThreadID;
    const uint2 pixCoord = baseCoord * 2;
    float4 depths4 = sourceNDCDepth.GatherRed( depthSampler, float2( pixCoord * consts.ViewportPixelSize ), int2(1,1) );
    lpfloat depth0 = XeGTAO_ClampDepth( XeGTAO_ScreenSpaceToViewSpaceDepth( depths4.w, consts ) );
    lpfloat depth1 = XeGTAO_ClampDepth( XeGTAO_ScreenSpaceToViewSpaceDepth( depths4.z, consts ) );
    lpfloat depth2 = XeGTAO_ClampDepth( XeGTAO_ScreenSpaceToViewSpaceDepth( depths4.x, consts ) );
    lpfloat depth3 = XeGTAO_ClampDepth( XeGTAO_ScreenSpaceToViewSpaceDepth( depths4.y, consts ) );
    outDepth0[ pixCoord + uint2(0, 0) ] = (lpfloat)depth0;
    outDepth0[ pixCoord + uint2(1, 0) ] = (lpfloat)depth1;
    outDepth0[ pixCoord + uint2(0, 1) ] = (lpfloat)depth2;
    outDepth0[ pixCoord + uint2(1, 1) ] = (lpfloat)depth3;

    // MIP 1
    lpfloat dm1 = XeGTAO_DepthMIPFilter( depth0, depth1, depth2, depth3, consts );
    outDepth1[ baseCoord ] = (lpfloat)dm1;
    g_scratchDepths[ groupThreadID.x ][ groupThreadID.y ] = dm1;

    GroupMemoryBarrierWithGroupSync( );

    // MIP 2
    [branch]
    if( all( ( groupThreadID.xy % 2.xx ) == 0 ) )
    {
        lpfloat inTL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+0];
        lpfloat inTR = g_scratchDepths[groupThreadID.x+1][groupThreadID.y+0];
        lpfloat inBL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+1];
        lpfloat inBR = g_scratchDepths[groupThreadID.x+1][groupThreadID.y+1];

        lpfloat dm2 = XeGTAO_DepthMIPFilter( inTL, inTR, inBL, inBR, consts );
        outDepth2[ baseCoord / 2 ] = (lpfloat)dm2;
        g_scratchDepths[ groupThreadID.x ][ groupThreadID.y ] = dm2;
    }

    GroupMemoryBarrierWithGroupSync( );

    // MIP 3
    [branch]
    if( all( ( groupThreadID.xy % 4.xx ) == 0 ) )
    {
        lpfloat inTL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+0];
        lpfloat inTR = g_scratchDepths[groupThreadID.x+2][groupThreadID.y+0];
        lpfloat inBL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+2];
        lpfloat inBR = g_scratchDepths[groupThreadID.x+2][groupThreadID.y+2];

        lpfloat dm3 = XeGTAO_DepthMIPFilter( inTL, inTR, inBL, inBR, consts );
        outDepth3[ baseCoord / 4 ] = (lpfloat)dm3;
        g_scratchDepths[ groupThreadID.x ][ groupThreadID.y ] = dm3;
    }

    GroupMemoryBarrierWithGroupSync( );

    // MIP 4
    [branch]
    if( all( ( groupThreadID.xy % 8.xx ) == 0 ) )
    {
        lpfloat inTL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+0];
        lpfloat inTR = g_scratchDepths[groupThreadID.x+4][groupThreadID.y+0];
        lpfloat inBL = g_scratchDepths[groupThreadID.x+0][groupThreadID.y+4];
        lpfloat inBR = g_scratchDepths[groupThreadID.x+4][groupThreadID.y+4];

        lpfloat dm4 = XeGTAO_DepthMIPFilter( inTL, inTR, inBL, inBR, consts );
        outDepth4[ baseCoord / 8 ] = (lpfloat)dm4;
        //g_scratchDepths[ groupThreadID.x ][ groupThreadID.y ] = dm4;
    }
}

lpfloat4 XeGTAO_UnpackEdges( lpfloat _packedVal )
{
    uint packedVal = (uint)(_packedVal * 255.5);
    lpfloat4 edgesLRTB;
    edgesLRTB.x = lpfloat((packedVal >> 6) & 0x03) / 3.0;          // there's really no need for mask (as it's an 8 bit input) but I'll leave it in so it doesn't cause any trouble in the future
    edgesLRTB.y = lpfloat((packedVal >> 4) & 0x03) / 3.0;
    edgesLRTB.z = lpfloat((packedVal >> 2) & 0x03) / 3.0;
    edgesLRTB.w = lpfloat((packedVal >> 0) & 0x03) / 3.0;

    return saturate( edgesLRTB );
}

#ifdef XE_GTAO_COMPUTE_BENT_NORMALS
typedef lpfloat4 AOTermType;            // .xyz is bent normal, .w is visibility term
#else
typedef lpfloat AOTermType;             // .x is visibility term
#endif

void XeGTAO_AddSample( AOTermType ssaoValue, lpfloat edgeValue, inout AOTermType sum, inout lpfloat sumWeight )
{
    lpfloat weight = edgeValue;    

    sum += (weight * ssaoValue);
    sumWeight += weight;
}

void XeGTAO_Output( uint2 pixCoord, RWTexture2D<uint> outputTexture, AOTermType outputValue, const uniform bool finalApply )
{
#ifdef XE_GTAO_COMPUTE_BENT_NORMALS
    lpfloat     visibility = outputValue.w * ((finalApply)?((lpfloat)XE_GTAO_OCCLUSION_TERM_SCALE):(1));
    lpfloat3    bentNormal = normalize(outputValue.xyz);
    outputTexture[pixCoord.xy] = XeGTAO_EncodeVisibilityBentNormal( visibility, bentNormal );
#else
    outputValue *=  (finalApply)?((lpfloat)XE_GTAO_OCCLUSION_TERM_SCALE):(1);
    outputTexture[pixCoord.xy] = uint(outputValue * 255.0 + 0.5);
#endif
}

void XeGTAO_DecodeGatherPartial( const uint4 packedValue, out AOTermType outDecoded[4] )
{
    for( int i = 0; i < 4; i++ )
#ifdef XE_GTAO_COMPUTE_BENT_NORMALS
        XeGTAO_DecodeVisibilityBentNormal( packedValue[i], outDecoded[i].w, outDecoded[i].xyz );
#else
        outDecoded[i] = lpfloat(packedValue[i]) / lpfloat(255.0);
#endif
}

// Returns two AO values: .x for pixCoordBase, .y for pixCoordBase + int2(1,0)
// Each thread processes 2 horizontal pixels as a performance optimization (shared gather reads)
lpfloat2 XeGTAO_Denoise( const uint2 pixCoordBase, const GTAOConstants consts, Texture2D sourceAOTerm, Texture2D<float> sourceEdges, SamplerState texSampler )
{
    const lpfloat blurAmount = (lpfloat)consts.DenoiseBlurBeta;
    const lpfloat diagWeight = 0.85 * 0.5;

    AOTermType aoTerm[2];   // pixel pixCoordBase and pixel pixCoordBase + int2( 1, 0 )
    lpfloat4 edgesC_LRTB[2];
    lpfloat weightTL[2];
    lpfloat weightTR[2];
    lpfloat weightBL[2];
    lpfloat weightBR[2];

    // gather edge and visibility quads, used later
    const float2 gatherCenter = float2( pixCoordBase.x, pixCoordBase.y ) * consts.ViewportPixelSize;
    lpfloat4 edgesQ0        = (lpfloat4)sourceEdges.GatherRed( texSampler, gatherCenter, int2( 0, 0 ) );
    lpfloat4 edgesQ1        = (lpfloat4)sourceEdges.GatherRed( texSampler, gatherCenter, int2( 2, 0 ) );
    lpfloat4 edgesQ2        = (lpfloat4)sourceEdges.GatherRed( texSampler, gatherCenter, int2( 1, 2 ) );

    lpfloat4 visQ0 = (lpfloat4)sourceAOTerm.GatherRed( texSampler, gatherCenter, int2( 0, 0 ) );
    lpfloat4 visQ1 = (lpfloat4)sourceAOTerm.GatherRed( texSampler, gatherCenter, int2( 2, 0 ) );
    lpfloat4 visQ2 = (lpfloat4)sourceAOTerm.GatherRed( texSampler, gatherCenter, int2( 0, 2 ) );
    lpfloat4 visQ3 = (lpfloat4)sourceAOTerm.GatherRed( texSampler, gatherCenter, int2( 2, 2 ) );

    for( int side = 0; side < 2; side++ )
    {
        const int2 pixCoord = int2( pixCoordBase.x + side, pixCoordBase.y );

        lpfloat4 edgesL_LRTB  = XeGTAO_UnpackEdges( (side==0)?(edgesQ0.x):(edgesQ0.y) );
        lpfloat4 edgesT_LRTB  = XeGTAO_UnpackEdges( (side==0)?(edgesQ0.z):(edgesQ1.w) );
        lpfloat4 edgesR_LRTB  = XeGTAO_UnpackEdges( (side==0)?(edgesQ1.x):(edgesQ1.y) );
        lpfloat4 edgesB_LRTB  = XeGTAO_UnpackEdges( (side==0)?(edgesQ2.w):(edgesQ2.z) );

        edgesC_LRTB[side]     = XeGTAO_UnpackEdges( (side==0)?(edgesQ0.y):(edgesQ1.x) );

        // Edges aren't perfectly symmetrical: edge detection algorithm does not guarantee that a left edge on the right pixel will match the right edge on the left pixel (although
        // they will match in majority of cases). This line further enforces the symmetricity, creating a slightly sharper blur. Works real nice with TAA.
        edgesC_LRTB[side] *= lpfloat4( edgesL_LRTB.y, edgesR_LRTB.x, edgesT_LRTB.w, edgesB_LRTB.z );

#if 1   // this allows some small amount of AO leaking from neighbours if there are 3 or 4 edges; this reduces both spatial and temporal aliasing
        const lpfloat leak_threshold = 2.5; const lpfloat leak_strength = 0.5;
        lpfloat edginess = (saturate(4.0 - leak_threshold - dot( edgesC_LRTB[side], 1.xxxx )) / (4-leak_threshold)) * leak_strength;
        edgesC_LRTB[side] = saturate( edgesC_LRTB[side] + edginess );
#endif

#ifdef XE_GTAO_SHOW_EDGES
        g_outputDbgImage[pixCoord] = 1.0 - lpfloat4( edgesC_LRTB[side].x, edgesC_LRTB[side].y * 0.5 + edgesC_LRTB[side].w * 0.5, edgesC_LRTB[side].z, 1.0 );
        //g_outputDbgImage[pixCoord] = 1 - float4( edgesC_LRTB[side].z, edgesC_LRTB[side].w , 1, 0 );
        //g_outputDbgImage[pixCoord] = edginess.xxxx;
#endif

        // for diagonals; used by first and second pass
        weightTL[side] = diagWeight * (edgesC_LRTB[side].x * edgesL_LRTB.z + edgesC_LRTB[side].z * edgesT_LRTB.x);
        weightTR[side] = diagWeight * (edgesC_LRTB[side].z * edgesT_LRTB.y + edgesC_LRTB[side].y * edgesR_LRTB.z);
        weightBL[side] = diagWeight * (edgesC_LRTB[side].w * edgesB_LRTB.x + edgesC_LRTB[side].x * edgesL_LRTB.w);
        weightBR[side] = diagWeight * (edgesC_LRTB[side].y * edgesR_LRTB.w + edgesC_LRTB[side].w * edgesB_LRTB.y);

        // first pass
        AOTermType ssaoValue     = (side==0)?(visQ0[1]):(visQ1[0]);
        AOTermType ssaoValueL    = (side==0)?(visQ0[0]):(visQ0[1]);
        AOTermType ssaoValueT    = (side==0)?(visQ0[2]):(visQ1[3]);
        AOTermType ssaoValueR    = (side==0)?(visQ1[0]):(visQ1[1]);
        AOTermType ssaoValueB    = (side==0)?(visQ2[2]):(visQ3[3]);
        AOTermType ssaoValueTL   = (side==0)?(visQ0[3]):(visQ0[2]);
        AOTermType ssaoValueBR   = (side==0)?(visQ3[3]):(visQ3[2]);
        AOTermType ssaoValueTR   = (side==0)?(visQ1[3]):(visQ1[2]);
        AOTermType ssaoValueBL   = (side==0)?(visQ2[3]):(visQ2[2]);

        lpfloat sumWeight = blurAmount;
        AOTermType sum = ssaoValue * sumWeight;

        XeGTAO_AddSample( ssaoValueL, edgesC_LRTB[side].x, sum, sumWeight );
        XeGTAO_AddSample( ssaoValueR, edgesC_LRTB[side].y, sum, sumWeight );
        XeGTAO_AddSample( ssaoValueT, edgesC_LRTB[side].z, sum, sumWeight );
        XeGTAO_AddSample( ssaoValueB, edgesC_LRTB[side].w, sum, sumWeight );

        XeGTAO_AddSample( ssaoValueTL, weightTL[side], sum, sumWeight );
        XeGTAO_AddSample( ssaoValueTR, weightTR[side], sum, sumWeight );
        XeGTAO_AddSample( ssaoValueBL, weightBL[side], sum, sumWeight );
        XeGTAO_AddSample( ssaoValueBR, weightBR[side], sum, sumWeight );

        aoTerm[side] = sum / sumWeight;
        
    }
    return lpfloat2(aoTerm[0], aoTerm[1]);
}


// Generic viewspace normal generate pass
float3 XeGTAO_ComputeViewspaceNormal( const uint2 pixCoord, const GTAOConstants consts, Texture2D sourceNDCDepth, SamplerState depthSampler )
{
    float2 normalizedScreenPos = (pixCoord + 0.5.xx) * consts.ViewportPixelSize;

    float4 valuesUL   = sourceNDCDepth.GatherRed( depthSampler, float2( pixCoord * consts.ViewportPixelSize )               );
    float4 valuesBR   = sourceNDCDepth.GatherRed( depthSampler, float2( pixCoord * consts.ViewportPixelSize ), int2( 1, 1 ) );

    // viewspace Z at the center
    float viewspaceZ  = XeGTAO_ScreenSpaceToViewSpaceDepth( valuesUL.y, consts ); //sourceViewspaceDepth.SampleLevel( depthSampler, normalizedScreenPos, 0 ).x; 

    // viewspace Zs left top right bottom
    const float pixLZ = XeGTAO_ScreenSpaceToViewSpaceDepth( valuesUL.x, consts );
    const float pixTZ = XeGTAO_ScreenSpaceToViewSpaceDepth( valuesUL.z, consts );
    const float pixRZ = XeGTAO_ScreenSpaceToViewSpaceDepth( valuesBR.z, consts );
    const float pixBZ = XeGTAO_ScreenSpaceToViewSpaceDepth( valuesBR.x, consts );

    lpfloat4 edgesLRTB  = XeGTAO_CalculateEdges( (lpfloat)viewspaceZ, (lpfloat)pixLZ, (lpfloat)pixRZ, (lpfloat)pixTZ, (lpfloat)pixBZ );

    float3 CENTER   = XeGTAO_ComputeViewspacePosition( normalizedScreenPos, viewspaceZ, consts );
    float3 LEFT     = XeGTAO_ComputeViewspacePosition( normalizedScreenPos + float2(-1,  0) * consts.ViewportPixelSize, pixLZ, consts );
    float3 RIGHT    = XeGTAO_ComputeViewspacePosition( normalizedScreenPos + float2( 1,  0) * consts.ViewportPixelSize, pixRZ, consts );
    float3 TOP      = XeGTAO_ComputeViewspacePosition( normalizedScreenPos + float2( 0, -1) * consts.ViewportPixelSize, pixTZ, consts );
    float3 BOTTOM   = XeGTAO_ComputeViewspacePosition( normalizedScreenPos + float2( 0,  1) * consts.ViewportPixelSize, pixBZ, consts );
    return XeGTAO_CalculateNormal( edgesLRTB, CENTER, LEFT, RIGHT, TOP, BOTTOM );
}