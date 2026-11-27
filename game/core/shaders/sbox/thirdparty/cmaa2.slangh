///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Copyright (c) 2018, Intel Corporation
//
// Licensed under the Apache License, Version 2.0 ( the "License" );
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// Conservative Morphological Anti-Aliasing, version: 2.3
//
// Author(s):       Filip Strugar (filip.strugar@intel.com)
//
// More info:       https://github.com/GameTechDev/CMAA2
//
// Please see https://github.com/GameTechDev/CMAA2/README.md for additional information and a basic integration guide.
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// ENGINE MODIFICATIONS: engine-owned shader attributes replace fixed registers; control counters and
// dispatch arguments use structured word buffers; bounds and work budgets are enforced; output alpha
// is preserved. HDR input is compressed (c / (1 + max(c))) for detection and blending, then expanded on store.
// The Intel shape detection, line search, and deferred blend algorithm remain intact.

#ifndef __CMAA2_HLSL__
#define __CMAA2_HLSL__



// Constants that C++/API side needs to know!
#ifndef CMAA_PACK_SINGLE_SAMPLE_EDGE_TO_HALF_WIDTH
#define CMAA_PACK_SINGLE_SAMPLE_EDGE_TO_HALF_WIDTH 1   // packs two four-bit edges into one texel
#endif
#define CMAA2_CS_INPUT_KERNEL_SIZE_X                16
#define CMAA2_CS_INPUT_KERNEL_SIZE_Y                16

// The rest below is shader only code
#ifndef __cplusplus

// If the color buffer range is bigger than [0, 1] then use this, otherwise don't (and gain some precision - see https://bartwronski.com/2017/04/02/small-float-formats-r11g11b10f-precision/)
#ifndef CMAA2_SUPPORT_HDR_COLOR_RANGE
#define CMAA2_SUPPORT_HDR_COLOR_RANGE 0
#endif

// 0 is full color-based edge detection, 1 and 2 are idential log luma based, with the difference bing that 1 loads color and computes log luma in-place (less efficient) while 2 loads precomputed log luma from a separate R8_UNORM texture (more efficient).
// Luma-based edge detection has a slightly lower quality but better performance so use it as a default; providing luma as a separate texture (or .a channel of the main one) will improve performance.
// See RGBToLumaForEdges for luma conversions in non-HDR and HDR versions.
#ifndef CMAA2_EDGE_DETECTION_LUMA_PATH
#define CMAA2_EDGE_DETECTION_LUMA_PATH 1
#endif

// for CMAA2+MSAA support
#ifndef CMAA_MSAA_SAMPLE_COUNT
#define CMAA_MSAA_SAMPLE_COUNT 1
#endif

#define CMAA2_CS_OUTPUT_KERNEL_SIZE_X               (CMAA2_CS_INPUT_KERNEL_SIZE_X-2)
#define CMAA2_CS_OUTPUT_KERNEL_SIZE_Y               (CMAA2_CS_INPUT_KERNEL_SIZE_Y-2)
#define CMAA2_PROCESS_CANDIDATES_NUM_THREADS        128
#define CMAA2_DEFERRED_APPLY_NUM_THREADS            32

// Optimization paths
#define CMAA2_DEFERRED_APPLY_THREADGROUP_SWAP       1   // 1 seems to be better or same on all HW
#define CMAA2_COLLECT_EXPAND_BLEND_ITEMS            1   // this reschedules final part of work in the ProcessCandidatesCS (where the sampling and blending takes place) from few to all threads to increase hardware thread occupancy
#ifndef CMAA2_USE_HALF_FLOAT_PRECISION
#define CMAA2_USE_HALF_FLOAT_PRECISION              0   // use half precision by default? (not on by default due to driver issues on various different hardware, but let external code decide to define if needed)
#endif

#ifndef CMAA2_UAV_STORE_TYPED
#error Warning - make sure correct value is set according to D3D11_FORMAT_SUPPORT_TYPED_UNORDERED_ACCESS_VIEW & D3D11_FORMAT_SUPPORT2_UAV_TYPED_STORE caps for the color UAV format used in g_inoutColorWriteonly
#define CMAA2_UAV_STORE_TYPED                       1   // use defaults that match the most common scenario: DXGI_FORMAT_R8G8B8A8_UNORM as UAV on a DXGI_FORMAT_R8G8B8A8_UNORM_SRGB resource (no typed stores for sRGB so we have to manually convert)
#endif

#ifndef CMAA2_UAV_STORE_CONVERT_TO_SRGB
#error Warning - make sure correct value is set according to whether manual linear->sRGB color conversion is needed when writing color output to g_inoutColorWriteonly
#define CMAA2_UAV_STORE_CONVERT_TO_SRGB             1   // use defaults that match the most common scenario: DXGI_FORMAT_R8G8B8A8_UNORM as UAV on a DXGI_FORMAT_R8G8B8A8_UNORM_SRGB resource (no typed stores for sRGB so we have to manually convert)
#endif

#ifndef CMAA2_UAV_STORE_TYPED_UNORM_FLOAT
#error Warning - make sure correct value is set according to the color UAV format used in g_inoutColorWriteonly
#define CMAA2_UAV_STORE_TYPED_UNORM_FLOAT           1   // for typed UAV stores: set to 1 for all _UNORM formats and to 0 for _FLOAT formats
#endif

#if CMAA2_UAV_STORE_TYPED
    #ifndef CMAA2_UAV_STORE_TYPED_UNORM_FLOAT
        #error When CMAA2_UAV_STORE_TYPED is set to 1, CMAA2_UAV_STORE_TYPED_UNORM_FLOAT must be set 1 if the color UAV is not a _FLOAT format or 0 if it is.
    #endif
#else
    #ifndef CMAA2_UAV_STORE_UNTYPED_FORMAT
        #error Error - untyped format required (see FinalUAVStore function for the list)
    #endif
#endif

#if (CMAA2_USE_HALF_FLOAT_PRECISION != 0)
#error this codepath needs testing - it's likely not valid anymore
typedef min16float      lpfloat;
typedef min16float2     lpfloat2;
typedef min16float3     lpfloat3;
typedef min16float4     lpfloat4;
#else
typedef float           lpfloat;
typedef float2          lpfloat2;
typedef float3          lpfloat3;
typedef float4          lpfloat4;
#endif

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// VARIOUS QUALITY SETTINGS
//
// Longest line search distance; must be even number; for high perf low quality start from ~32 - the bigger the number,
// the nicer the gradients but more costly. Max supported is 128!
static const uint c_maxLineLength = 86;
//
#ifndef CMAA2_EXTRA_SHARPNESS
    #define CMAA2_EXTRA_SHARPNESS                   0     // Set to 1 to preserve even more text and shape clarity at the expense of less AA
#endif
//
// It makes sense to slightly drop edge detection thresholds with increase in MSAA sample count, as with the higher
// MSAA level the overall impact of CMAA2 alone is reduced but the cost increases.
#define CMAA2_SCALE_QUALITY_WITH_MSAA               0
//
//
#ifndef CMAA2_STATIC_QUALITY_PRESET
    #define CMAA2_STATIC_QUALITY_PRESET 2  // 0 - LOW, 1 - MEDIUM, 2 - HIGH, 3 - ULTRA
#endif
// presets (for HDR color buffer maybe use higher values)
#if CMAA2_STATIC_QUALITY_PRESET == 0   // LOW
    #define g_CMAA2_EdgeThreshold                   lpfloat(0.15)
#elif CMAA2_STATIC_QUALITY_PRESET == 1 // MEDIUM
    #define g_CMAA2_EdgeThreshold                   lpfloat(0.10)
#elif CMAA2_STATIC_QUALITY_PRESET == 2 // HIGH (default)
    #define g_CMAA2_EdgeThreshold                   lpfloat(0.07)
#elif CMAA2_STATIC_QUALITY_PRESET == 3 // ULTRA
    #define g_CMAA2_EdgeThreshold                   lpfloat(0.05)
#else
    #error CMAA2_STATIC_QUALITY_PRESET not set?
#endif
//
#if CMAA2_EXTRA_SHARPNESS
#define g_CMAA2_LocalContrastAdaptationAmount       lpfloat(0.15)
#define g_CMAA2_SimpleShapeBlurinessAmount          lpfloat(0.07)
#else
#define g_CMAA2_LocalContrastAdaptationAmount       lpfloat(0.05)
#define g_CMAA2_SimpleShapeBlurinessAmount          lpfloat(0.10)
#endif
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


#if CMAA_MSAA_SAMPLE_COUNT > 1
#define CMAA_MSAA_USE_COMPLEXITY_MASK 1
#endif

// Engine shader wrapper declares all resources with named Attribute bindings and maps them to these
// upstream names before including this file. No fixed register slots are used here.


///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// encoding/decoding of various data such as edges
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// how .rgba channels from the edge texture maps to pixel edges:
//
//                   A - 0x08               (A - there's an edge between us and a pixel above us)
//              |---------|                 (R - there's an edge between us and a pixel to the right)
//              |         |                 (G - there's an edge between us and a pixel at the bottom)
//     0x04 - B |  pixel  | R - 0x01        (B - there's an edge between us and a pixel to the left)
//              |         |
//              |_________|
//                   G - 0x02
uint PackEdges( lpfloat4 edges )   // input edges are binary 0 or 1
{
    return (uint)dot( edges, lpfloat4( 1, 2, 4, 8 ) );
}
uint4 UnpackEdges( uint value )
{
    int4 ret;
    ret.x = ( value & 0x01 ) != 0;
    ret.y = ( value & 0x02 ) != 0;
    ret.z = ( value & 0x04 ) != 0;
    ret.w = ( value & 0x08 ) != 0;
    return ret;
}
lpfloat4 UnpackEdgesFlt( uint value )
{
    lpfloat4 ret;
    ret.x = ( value & 0x01 ) != 0;
    ret.y = ( value & 0x02 ) != 0;
    ret.z = ( value & 0x04 ) != 0;
    ret.w = ( value & 0x08 ) != 0;
    return ret;
}
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// source color & color conversion helpers
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


#if CMAA2_SUPPORT_HDR_COLOR_RANGE
// Edge detection and blending run in a compressed space so bright edges are weighted the way the later tonemap
// will show them. Blending in linear HDR leaves a bright pixel next to a dark one bright after tonemapping, so the
// edge stays aliased. Reversible and bounded to [0, 1) per channel: c / (1 + max(c)).
lpfloat3 CompressHdrColor( lpfloat3 color )
{
    color = max( color, 0 );
    return color / ( 1 + max( color.r, max( color.g, color.b ) ) );
}

lpfloat3 ExpandHdrColor( lpfloat3 color )
{
    float peak = min( max( color.r, max( color.g, color.b ) ), 0.999 );
    return color / ( 1 - peak );
}
#endif

lpfloat3 LoadSourceColor( int2 pixelPos, int2 offset, int sampleIndex )
{
#if CMAA_MSAA_SAMPLE_COUNT > 1
    lpfloat3 color = g_inColorMSReadonly.Load( int4( pixelPos, sampleIndex, 0 ), offset ).rgb;
#else
    int2 sourcePos = clamp( pixelPos + offset, int2( 0, 0 ), int2( Cmaa2Dimensions ) - 1 );
    lpfloat3 color = g_inoutColorReadonly.Load( int3( sourcePos, 0 ) ).rgb;
#endif
#if CMAA2_SUPPORT_HDR_COLOR_RANGE
    color = CompressHdrColor( color );
#endif
    return color;
}

bool IsPixelInBounds( int2 pixelPos )
{
    return all( pixelPos >= 0 ) && all( pixelPos < int2( Cmaa2Dimensions ) );
}
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// (R11G11B10 conversion code below taken from Miniengine's PixelPacking_R11G11B10.hlsli,
// Copyright (c) Microsoft, MIT license, Developed by Minigraph, Author:  James Stanard; original file link:
// https://github.com/Microsoft/DirectX-Graphics-Samples/blob/master/MiniEngine/Core/Shaders/PixelPacking_R11G11B10.hlsli )
//
// The standard 32-bit HDR color format.  Each float has a 5-bit exponent and no sign bit.
uint Pack_R11G11B10_FLOAT( float3 rgb )
{
    // Clamp upper bound so that it doesn't accidentally round up to INF
    // Exponent=15, Mantissa=1.11111
    rgb = min(rgb, asfloat(0x477C0000));
    uint r = ((f32tof16(rgb.x) + 8) >> 4) & 0x000007FF;
    uint g = ((f32tof16(rgb.y) + 8) << 7) & 0x003FF800;
    uint b = ((f32tof16(rgb.z) + 16) << 17) & 0xFFC00000;
    return r | g | b;
}

float3 Unpack_R11G11B10_FLOAT( uint rgb )
{
    float r = f16tof32((rgb << 4 ) & 0x7FF0);
    float g = f16tof32((rgb >> 7 ) & 0x7FF0);
    float b = f16tof32((rgb >> 17) & 0x7FE0);
    return float3(r, g, b);
}
//
// These next two encodings are great for LDR data.  By knowing that our values are [0.0, 1.0]
// (or [0.0, 2.0), incidentally), we can reduce how many bits we need in the exponent.  We can
// immediately eliminate all postive exponents.  By giving more bits to the mantissa, we can
// improve precision at the expense of range.  The 8E3 format goes one bit further, quadrupling
// mantissa precision but increasing smallest exponent from -14 to -6.  The smallest value of 8E3
// is 2^-14, while the smallest value of 7E4 is 2^-21.  Both are smaller than the smallest 8-bit
// sRGB value, which is close to 2^-12.
//
// This is like R11G11B10_FLOAT except that it moves one bit from each exponent to each mantissa.
uint Pack_R11G11B10_E4_FLOAT( float3 rgb )
{
    // Clamp to [0.0, 2.0).  The magic number is 1.FFFFF x 2^0.  (We can't represent hex floats in HLSL.)
    // This trick works because clamping your exponent to 0 reduces the number of bits needed by 1.
    rgb = clamp( rgb, 0.0, asfloat(0x3FFFFFFF) );
    uint r = ((f32tof16(rgb.r) + 4) >> 3 ) & 0x000007FF;
    uint g = ((f32tof16(rgb.g) + 4) << 8 ) & 0x003FF800;
    uint b = ((f32tof16(rgb.b) + 8) << 18) & 0xFFC00000;
    return r | g | b;
}
//
float3 Unpack_R11G11B10_E4_FLOAT( uint rgb )
{
    float r = f16tof32((rgb << 3 ) & 0x3FF8);
    float g = f16tof32((rgb >> 8 ) & 0x3FF8);
    float b = f16tof32((rgb >> 18) & 0x3FF0);
    return float3(r, g, b);
}
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// This is for temporary storage - R11G11B10_E4 covers 8bit per channel sRGB well enough;
// Colors are always [0, 1) here (HDR input is compressed by CompressHdrColor on load), so the E4 packing
// covers both LDR and HDR sources and has more mantissa precision than the standard float packing.
// It's worth noting that since CMAA2 works on high contrast edges, the lack of precision will not be nearly as
// noticeable as it would be on gradients (which always remain unaffected).
lpfloat3 InternalUnpackColor( uint packedColor )
{
    return Unpack_R11G11B10_E4_FLOAT( packedColor );
}
//
uint InternalPackColor( lpfloat3 color )
{
    return Pack_R11G11B10_E4_FLOAT( color );
}
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
static const uint CMAA2_HEAD_EMPTY = 0xFFFFFFFFu;

bool TryReserveControlIndex( uint wordIndex, uint capacity, out uint index )
{
    // Work limits prevent counter overflow; writers and indirect dispatches remain capacity-clamped.
    InterlockedAdd( Cmaa2Control[wordIndex], 1, index );
    return index < capacity;
}

void StoreColorSample( int2 pixelPos, lpfloat3 color, bool isComplexShape, uint msaaSampleIndex )
{
    if( !IsPixelInBounds( pixelPos ) )
        return;

    uint2 pixel = uint2( pixelPos );
    uint2 quadPos = pixel / 2;
    uint offsetXY = ( pixel.y % 2 ) * 2 + ( pixel.x % 2 );
    uint itemIndex;
    uint itemCapacity = min( Cmaa2BlendItemCapacity, 0x03FFFFFFu );
    if( !TryReserveControlIndex( 12, itemCapacity, itemIndex ) )
        return;

    uint header = ( offsetXY << 30 ) | ( msaaSampleIndex << 27 ) | ( uint( isComplexShape ) << 26 ) | itemIndex;
    uint previousHead;
    InterlockedExchange( Cmaa2BlendHeads[quadPos], header, previousHead );
    Cmaa2BlendItems[itemIndex] = uint2( previousHead, InternalPackColor( color ) );

    if( previousHead == CMAA2_HEAD_EMPTY )
    {
        uint locationIndex;
        if( TryReserveControlIndex( 8, Cmaa2BlendLocationCapacity, locationIndex ) )
            Cmaa2BlendLocations[locationIndex] = ( quadPos.x << 16 ) | quadPos.y;
    }
}
//
#if CMAA2_COLLECT_EXPAND_BLEND_ITEMS
#define CMAA2_BLEND_ITEM_SLM_SIZE           768         // there's a fallback for extreme cases (observed with this value set to 256 or below) in which case image will remain correct but performance will suffer
groupshared uint        g_groupSharedBlendItemCount;
groupshared uint2       g_groupSharedBlendItems[ CMAA2_BLEND_ITEM_SLM_SIZE ];
#endif
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Untyped UAV store packing & sRGB conversion helpers
//
lpfloat LINEAR_to_SRGB( lpfloat val )
{
    if( val < 0.0031308 )
        val *= lpfloat( 12.92 );
    else
        val = lpfloat( 1.055 ) * pow( abs( val ), lpfloat( 1.0 ) / lpfloat( 2.4 ) ) - lpfloat( 0.055 );
    return val;
}
lpfloat3 LINEAR_to_SRGB( lpfloat3 val )
{
    return lpfloat3( LINEAR_to_SRGB( val.x ), LINEAR_to_SRGB( val.y ), LINEAR_to_SRGB( val.z ) );
}
//
uint FLOAT4_to_R8G8B8A8_UNORM( lpfloat4 unpackedInput )
{
    return (( uint( saturate( unpackedInput.x ) * 255 + 0.5 ) ) |
            ( uint( saturate( unpackedInput.y ) * 255 + 0.5 ) << 8 ) |
            ( uint( saturate( unpackedInput.z ) * 255 + 0.5 ) << 16 ) |
            ( uint( saturate( unpackedInput.w ) * 255 + 0.5 ) << 24 ) );
}
//
uint FLOAT4_to_R10G10B10A2_UNORM( lpfloat4 unpackedInput )
{
    return (( uint( saturate( unpackedInput.x ) * 1023 + 0.5    ) ) |
            ( uint( saturate( unpackedInput.y ) * 1023 + 0.5    ) << 10 ) |
            ( uint( saturate( unpackedInput.z ) * 1023 + 0.5    ) << 20 ) |
            ( uint( saturate( unpackedInput.w ) * 3 + 0.5       ) << 30 ) );
}
//
// This handles various permutations for various formats with no/partial/full typed UAV store support
void FinalUAVStore( int2 pixelPos, lpfloat3 color )
{
    if( !IsPixelInBounds( pixelPos ) )
        return;

    uint2 coordinate = uint2( pixelPos );
    lpfloat alpha = g_inoutColorWriteonly[ coordinate ].a;
#if CMAA2_SUPPORT_HDR_COLOR_RANGE
    color = ExpandHdrColor( color );
#endif
#if CMAA2_UAV_STORE_CONVERT_TO_SRGB
    color = LINEAR_to_SRGB( color );
#endif

#if CMAA2_UAV_STORE_TYPED
    g_inoutColorWriteonly[ coordinate ] = lpfloat4( color.rgb, alpha );
#else
    #if CMAA2_UAV_STORE_UNTYPED_FORMAT == 1
        g_inoutColorWriteonly[ coordinate ] = FLOAT4_to_R8G8B8A8_UNORM( lpfloat4( color, alpha ) );
    #elif CMAA2_UAV_STORE_UNTYPED_FORMAT == 2
        g_inoutColorWriteonly[ coordinate ] = FLOAT4_to_R10G10B10A2_UNORM( lpfloat4( color, alpha ) );
    #else
        #error CMAA color packing format not defined - add it here!
    #endif
#endif
}
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Edge detection and local contrast adaptation helpers
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
lpfloat GetActualEdgeThreshold( )
{
    lpfloat retVal = g_CMAA2_EdgeThreshold;
#if CMAA2_SCALE_QUALITY_WITH_MSAA
    retVal *= 1.0 + (CMAA_MSAA_SAMPLE_COUNT-1) * 0.06;
#endif
    return retVal;
}
//
lpfloat EdgeDetectColorCalcDiff( lpfloat3 colorA, lpfloat3 colorB )
{
    const lpfloat3 LumWeights = lpfloat3( 0.299, 0.587, 0.114 );
    lpfloat3 diff = abs( (colorA.rgb - colorB.rgb) );
    return dot( diff.rgb, LumWeights.rgb );
}
//
// apply custom curve / processing to put input color (linear) in the format required by ComputeEdge
lpfloat3 ProcessColorForEdgeDetect( lpfloat3 color )
{
    //pixelColors[i] = LINEAR_to_SRGB( pixelColors[i] );            // correct reference
    //pixelColors[i] = pow( max( 0, pixelColors[i], 1.0 / 2.4 ) );  // approximate sRGB curve
    return sqrt( color ); // just very roughly approximate RGB curve
}
//
lpfloat2 ComputeEdge( int x, int y, lpfloat3 pixelColors[3 * 3 - 1] )
{
    lpfloat2 temp;
    temp.x = EdgeDetectColorCalcDiff( pixelColors[x + y * 3].rgb, pixelColors[x + 1 + y * 3].rgb );
    temp.y = EdgeDetectColorCalcDiff( pixelColors[x + y * 3].rgb, pixelColors[x + ( y + 1 ) * 3].rgb );
    return temp;    // for HDR edge detection it might be good to premultiply both of these by some factor - otherwise clamping to 1 might prevent some local contrast adaptation. It's a very minor nitpick though, unlikely to significantly affect things.
}
// color -> log luma-for-edges conversion
float RGBToLumaForEdges( float3 linearRGB )
{
#if 0
    // this matches Miniengine luma path
    float Luma = dot( linearRGB, float3(0.212671, 0.715160, 0.072169) );
    return log2(1 + Luma * 15) / 4;
#else
    // this is what original FXAA (and consequently CMAA2) use by default - these coefficients correspond to Rec. 601 and those should be
    // used on gamma-compressed components (see https://en.wikipedia.org/wiki/Luma_(video)#Rec._601_luma_versus_Rec._709_luma_coefficients),
    float luma = dot( sqrt( linearRGB.rgb ), float3( 0.299, 0.587, 0.114 ) );  // http://en.wikipedia.org/wiki/CCIR_601
    // using sqrt luma for now but log luma like in miniengine provides a nicer curve on the low-end
    return luma;
#endif
}
lpfloat2 ComputeEdgeLuma( int x, int y, lpfloat pixelLumas[3 * 3 - 1] )
{
    lpfloat2 temp;
    temp.x = abs( pixelLumas[x + y * 3] - pixelLumas[x + 1 + y * 3] );
    temp.y = abs( pixelLumas[x + y * 3] - pixelLumas[x + ( y + 1 ) * 3] );
    return temp;    // for HDR edge detection it might be good to premultiply both of these by some factor - otherwise clamping to 1 might prevent some local contrast adaptation. It's a very minor nitpick though, unlikely to significantly affect things.
}
//
lpfloat ComputeLocalContrastV( int x, int y, in lpfloat2 neighbourhood[4][4] )
{
    // new, small kernel 4-connecting-edges-only local contrast adaptation
    return max( max( neighbourhood[x + 1][y + 0].y, neighbourhood[x + 1][y + 1].y ), max( neighbourhood[x + 2][y + 0].y, neighbourhood[x + 2][y + 1].y ) ) * lpfloat( g_CMAA2_LocalContrastAdaptationAmount );

//    // slightly bigger kernel that enhances edges in-line (not worth the cost)
//  return ( max( max( neighbourhood[x + 1][y + 0].y, neighbourhood[x + 1][y + 1].y ), max( neighbourhood[x + 2][y + 0].y, neighbourhood[x + 2][y + 1].y ) )
//        - ( neighbourhood[x + 1][y + 0].x + neighbourhood[x + 1][y + 2].x ) * 0.3 ) * lpfloat( g_CMAA2_LocalContrastAdaptationAmount );
}
//
lpfloat ComputeLocalContrastH( int x, int y, in lpfloat2 neighbourhood[4][4] )
{
    // new, small kernel 4-connecting-edges-only local contrast adaptation
    return max( max( neighbourhood[x + 0][y + 1].x, neighbourhood[x + 1][y + 1].x ), max( neighbourhood[x + 0][y + 2].x, neighbourhood[x + 1][y + 2].x ) ) * lpfloat( g_CMAA2_LocalContrastAdaptationAmount );

//    // slightly bigger kernel that enhances edges in-line (not worth the cost)
//    return ( max( max( neighbourhood[x + 0][y + 1].x, neighbourhood[x + 1][y + 1].x ), max( neighbourhood[x + 0][y + 2].x, neighbourhood[x + 1][y + 2].x ) )
//        - ( neighbourhood[x + 0][y + 1].y + neighbourhood[x + 2][y + 1].y ) * 0.3 ) * lpfloat( g_CMAA2_LocalContrastAdaptationAmount );
}
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

lpfloat4 ComputeSimpleShapeBlendValues( lpfloat4 edges, lpfloat4 edgesLeft, lpfloat4 edgesRight, lpfloat4 edgesTop, lpfloat4 edgesBottom, uniform bool dontTestShapeValidity )
{
    // a 3x3 kernel for higher quality handling of L-based shapes (still rather basic and conservative)

    lpfloat fromRight   = edges.r;
    lpfloat fromBelow   = edges.g;
    lpfloat fromLeft    = edges.b;
    lpfloat fromAbove   = edges.a;

    lpfloat blurCoeff = lpfloat( g_CMAA2_SimpleShapeBlurinessAmount );

    lpfloat numberOfEdges = dot( edges, lpfloat4( 1, 1, 1, 1 ) );

    lpfloat numberOfEdgesAllAround = dot(edgesLeft.bga + edgesRight.rga + edgesTop.rba + edgesBottom.rgb, lpfloat3( 1, 1, 1 ) );

    // skip if already tested for before calling this function
    if( !dontTestShapeValidity )
    {
        // No blur for straight edge
        if( numberOfEdges == 1 )
            blurCoeff = 0;

        // L-like step shape ( only blur if it's a corner, not if it's two parallel edges)
        if( numberOfEdges == 2 )
            blurCoeff *= ( ( lpfloat(1.0) - fromBelow * fromAbove ) * ( lpfloat(1.0) - fromRight * fromLeft ) );
    }

    // L-like step shape
    //[branch]
    if( numberOfEdges == 2 )
    {
        blurCoeff *= 0.75;

#if 1
        float k = 0.9f;
#if 0
        fromRight   += k * (edges.g * edgesTop.r +      edges.a * edgesBottom.r );
        fromBelow   += k * (edges.r * edgesLeft.g +     edges.b * edgesRight.g );
        fromLeft    += k * (edges.g * edgesTop.b +      edges.a * edgesBottom.b );
        fromAbove   += k * (edges.b * edgesRight.a +    edges.r * edgesLeft.a );
#else
        fromRight   += k * (edges.g * edgesTop.r     * (1.0-edgesLeft.g)   +     edges.a * edgesBottom.r   * (1.0-edgesLeft.a)      );
        fromBelow   += k * (edges.b * edgesRight.g   * (1.0-edgesTop.b)    +     edges.r * edgesLeft.g     * (1.0-edgesTop.r)       );
        fromLeft    += k * (edges.a * edgesBottom.b  * (1.0-edgesRight.a)  +     edges.g * edgesTop.b      * (1.0-edgesRight.g)     );
        fromAbove   += k * (edges.r * edgesLeft.a    * (1.0-edgesBottom.r) +     edges.b * edgesRight.a   *  (1.0-edgesBottom.b)    );
#endif
#endif
    }

    // if( numberOfEdges == 3 )
    //     blurCoeff *= 0.95;

    // Dampen the blurring effect when lots of neighbouring edges - additionally preserves text and texture detail
#if CMAA2_EXTRA_SHARPNESS
    blurCoeff *= saturate( 1.15 - numberOfEdgesAllAround / 8.0 );
#else
    blurCoeff *= saturate( 1.30 - numberOfEdgesAllAround / 10.0 );
#endif

    return lpfloat4( fromLeft, fromAbove, fromRight, fromBelow ) * blurCoeff;
}

uint LoadEdge( int2 pixelPos, int2 offset, uint msaaSampleIndex )
{
    int2 samplePos = pixelPos + offset;
    if( !IsPixelInBounds( samplePos ) )
        return 0;

#if CMAA_MSAA_SAMPLE_COUNT > 1
    uint edge = (uint)g_workingEdges[ samplePos ];
    edge = ( edge >> ( msaaSampleIndex * 4 ) ) & 0xF;
#else
#if CMAA_PACK_SINGLE_SAMPLE_EDGE_TO_HALF_WIDTH
#if CMAA2_EDGE_UNORM
    uint packed = (uint)( g_workingEdges[ uint2( samplePos.x / 2, samplePos.y ) ] * 255.0 + 0.5 );
#else
    uint packed = g_workingEdges[ uint2( samplePos.x / 2, samplePos.y ) ];
#endif
    uint edge = ( packed >> ( ( samplePos.x % 2 ) * 4 ) ) & 0xF;
#else
    uint edge = (uint)g_workingEdges[ uint2( samplePos ) ];
#endif
#endif
    return edge;
}

groupshared lpfloat4 g_groupShared2x2FracEdgesH[CMAA2_CS_INPUT_KERNEL_SIZE_X * CMAA2_CS_INPUT_KERNEL_SIZE_Y];
groupshared lpfloat4 g_groupShared2x2FracEdgesV[CMAA2_CS_INPUT_KERNEL_SIZE_X * CMAA2_CS_INPUT_KERNEL_SIZE_Y];
// void GroupsharedLoadQuadH( uint addr, out lpfloat e00, out lpfloat e10, out lpfloat e01, out lpfloat e11 ) { lpfloat4 val = g_groupShared2x2FracEdgesH[addr]; e00 = val.x; e10 = val.y; e01 = val.z; e11 = val.w; }
// void GroupsharedLoadQuadV( uint addr, out lpfloat e00, out lpfloat e10, out lpfloat e01, out lpfloat e11 ) { lpfloat4 val = g_groupShared2x2FracEdgesV[addr]; e00 = val.x; e10 = val.y; e01 = val.z; e11 = val.w; }
void GroupsharedLoadQuadHV( uint addr, out lpfloat2 e00, out lpfloat2 e10, out lpfloat2 e01, out lpfloat2 e11 )
{
    lpfloat4 valH = g_groupShared2x2FracEdgesH[addr]; e00.y = valH.x; e10.y = valH.y; e01.y = valH.z; e11.y = valH.w;
    lpfloat4 valV = g_groupShared2x2FracEdgesV[addr]; e00.x = valV.x; e10.x = valV.y; e01.x = valV.z; e11.x = valV.w;
}

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Edge detection compute shader
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//groupshared uint g_groupShared2x2ProcColors[(CMAA2_CS_INPUT_KERNEL_SIZE_X * 2 + 1) * (CMAA2_CS_INPUT_KERNEL_SIZE_Y * 2 + 1)];
//groupshared float3 g_groupSharedResolvedMSColors[(CMAA2_CS_INPUT_KERNEL_SIZE_X * 2 + 1) * (CMAA2_CS_INPUT_KERNEL_SIZE_Y * 2 + 1)];
//
void EdgesColor2x2CS( uint3 groupID : SV_GroupID, uint3 groupThreadID : SV_GroupThreadID )
{
    // Each 16x16 group produces fourteen 2x2 quads in each dimension.
    int2 pixelPos = ( int2( groupID.xy ) * int2( CMAA2_CS_OUTPUT_KERNEL_SIZE_X, CMAA2_CS_OUTPUT_KERNEL_SIZE_Y ) + int2( groupThreadID.xy ) - 1 ) * 2;

    const int2 qeOffsets[4]          = { {0, 0}, {1, 0}, {0, 1}, {1, 1} };
    const uint rowStride2x2         = CMAA2_CS_INPUT_KERNEL_SIZE_X;
    const uint centerAddr2x2        = groupThreadID.x + groupThreadID.y * rowStride2x2;
    // const uint msaaSliceStride2x2   = CMAA2_CS_INPUT_KERNEL_SIZE_X * CMAA2_CS_INPUT_KERNEL_SIZE_Y;
    const bool inOutputKernel       = !any( bool4( groupThreadID.x == ( CMAA2_CS_INPUT_KERNEL_SIZE_X - 1 ), groupThreadID.x == 0, groupThreadID.y == ( CMAA2_CS_INPUT_KERNEL_SIZE_Y - 1 ), groupThreadID.y == 0 ) );

    uint i;
    lpfloat2 qe0, qe1, qe2, qe3;
    uint4 outEdges = { 0, 0, 0, 0 };

#if CMAA_MSAA_SAMPLE_COUNT > 1
    bool firstLoopIsEnough = false;

    #if CMAA_MSAA_USE_COMPLEXITY_MASK
    {
        float2 texSize;
        g_inColorMSComplexityMaskReadonly.GetDimensions( texSize.x, texSize.y );
        float2 gatherUV = float2(pixelPos) / texSize;
        float4 TL = g_inColorMSComplexityMaskReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 0, 0 ) );
        float4 TR = g_inColorMSComplexityMaskReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 2, 0 ) );
        float4 BL = g_inColorMSComplexityMaskReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 0, 2 ) );
        float4 BR = g_inColorMSComplexityMaskReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 2, 2 ) );
        float4 sumAll = TL+TR+BL+BR;
        firstLoopIsEnough = !any(sumAll);
    }
    #endif
#endif


    // not optimal - to be optimized
#if CMAA_MSAA_SAMPLE_COUNT > 1
    // clear this here to reduce complexity below - turns out it's quicker as well this way
    g_workingDeferredBlendItemListHeads[ uint2( pixelPos ) / 2 ] = 0xFFFFFFFF;
    [loop]
    for( uint msaaSampleIndex = 0; msaaSampleIndex < CMAA_MSAA_SAMPLE_COUNT; msaaSampleIndex++ )
    {
        bool msaaSampleIsRelevant = !firstLoopIsEnough || msaaSampleIndex == 0;
        [branch]
        if( msaaSampleIsRelevant )
        {
#else
        {
            uint msaaSampleIndex = 0;
#endif


            // edge detection
#if CMAA2_EDGE_DETECTION_LUMA_PATH == 0
            lpfloat3 pixelColors[3 * 3 - 1];
            [unroll]
            for( i = 0; i < 3 * 3 - 1; i++ )
                pixelColors[i] = LoadSourceColor( pixelPos, int2( i % 3, i / 3 ), msaaSampleIndex ).rgb;

            [unroll]
            for( i = 0; i < 3 * 3 - 1; i++ )
                pixelColors[i] = ProcessColorForEdgeDetect( pixelColors[i] );

            qe0 = ComputeEdge( 0, 0, pixelColors );
            qe1 = ComputeEdge( 1, 0, pixelColors );
            qe2 = ComputeEdge( 0, 1, pixelColors );
            qe3 = ComputeEdge( 1, 1, pixelColors );
#else // CMAA2_EDGE_DETECTION_LUMA_PATH != 0
            lpfloat pixelLumas[3 * 3 - 1];
    #if CMAA2_EDGE_DETECTION_LUMA_PATH == 1 // compute in-place
            [unroll]
            for( i = 0; i < 3 * 3 - 1; i++ )
            {
                lpfloat3 color = LoadSourceColor( pixelPos, int2( i % 3, i / 3 ), msaaSampleIndex ).rgb;
                pixelLumas[i] = RGBToLumaForEdges( color );
            }
    #elif CMAA2_EDGE_DETECTION_LUMA_PATH == 2 // source from outside
    #if 0 // same as below, just without Gather
            [unroll]
            for( i = 0; i < 3 * 3 - 1; i++ )
                 pixelLumas[i] = g_inLumaReadonly.Load( int3( pixelPos, 0 ), int2( i % 3, i / 3 ) ).r;
    #else
            float2 texSize;
            g_inLumaReadonly.GetDimensions( texSize.x, texSize.y );
            float2 gatherUV = (float2(pixelPos) + float2( 0.5, 0.5 )) / texSize;
            float4 TL = g_inLumaReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV );
            float4 TR = g_inLumaReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 1, 0 ) );
            float4 BL = g_inLumaReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 0, 1 ) );
            pixelLumas[0] = TL.w; pixelLumas[1] = TL.z; pixelLumas[2] = TR.z; pixelLumas[3] = TL.x;
            pixelLumas[4] = TL.y; pixelLumas[5] = TR.y; pixelLumas[6] = BL.x; pixelLumas[7] = BL.y;
    #endif
    #elif CMAA2_EDGE_DETECTION_LUMA_PATH == 3 // source in alpha channel of input color
            float2 texSize;
            g_inoutColorReadonly.GetDimensions( texSize.x, texSize.y );
            float2 gatherUV = (float2(pixelPos) + float2( 0.5, 0.5 )) / texSize;
            float4 TL = g_inoutColorReadonly.GatherAlpha( g_gather_point_clamp_Sampler, gatherUV );
            float4 TR = g_inoutColorReadonly.GatherAlpha( g_gather_point_clamp_Sampler, gatherUV, int2( 1, 0 ) );
            float4 BL = g_inoutColorReadonly.GatherAlpha( g_gather_point_clamp_Sampler, gatherUV, int2( 0, 1 ) );
            pixelLumas[0] = (lpfloat)TL.w; pixelLumas[1] = (lpfloat)TL.z; pixelLumas[2] = (lpfloat)TR.z; pixelLumas[3] = (lpfloat)TL.x;
            pixelLumas[4] = (lpfloat)TL.y; pixelLumas[5] = (lpfloat)TR.y; pixelLumas[6] = (lpfloat)BL.x; pixelLumas[7] = (lpfloat)BL.y;
    #endif
            qe0 = ComputeEdgeLuma( 0, 0, pixelLumas );
            qe1 = ComputeEdgeLuma( 1, 0, pixelLumas );
            qe2 = ComputeEdgeLuma( 0, 1, pixelLumas );
            qe3 = ComputeEdgeLuma( 1, 1, pixelLumas );
#endif

            g_groupShared2x2FracEdgesV[centerAddr2x2 + rowStride2x2 * 0] = lpfloat4( qe0.x, qe1.x, qe2.x, qe3.x );
            g_groupShared2x2FracEdgesH[centerAddr2x2 + rowStride2x2 * 0] = lpfloat4( qe0.y, qe1.y, qe2.y, qe3.y );

#if CMAA_MSAA_SAMPLE_COUNT > 1
         }  // if( msaaSampleIsRelevant )
#endif

        GroupMemoryBarrierWithGroupSync( );

        [branch]
        if( inOutputKernel )
        {
            // Every in-bounds quad head is cleared, including quads with no edges.
            if( IsPixelInBounds( pixelPos ) )
                g_workingDeferredBlendItemListHeads[ uint2( pixelPos ) / 2 ] = CMAA2_HEAD_EMPTY;

            lpfloat2 topRow         = g_groupShared2x2FracEdgesH[ centerAddr2x2 - rowStride2x2 ].zw;   // top row's bottom edge
            lpfloat2 leftColumn     = g_groupShared2x2FracEdgesV[ centerAddr2x2 - 1 ].yw;              // left column's right edge

            bool someNonZeroEdges = any( lpfloat4( qe0, qe1 ) + lpfloat4( qe2, qe3 ) + lpfloat4( topRow[0], topRow[1], leftColumn[0], leftColumn[1] ) );
            //bool someNonZeroEdges = packedCenterEdges.x | packedCenterEdges.y | (packedQuadP0M1.y & 0xFFFF0000) | (packedQuadM1P0.x & 0xFF00FF00);

            [branch]
            if( someNonZeroEdges )
            {
                lpfloat4 ce[4];


            #if 1 // local contrast adaptation
                lpfloat2 dummyd0, dummyd1, dummyd2;
                lpfloat2 neighbourhood[4][4];

                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                // load & unpack kernel data from SLM
                GroupsharedLoadQuadHV( centerAddr2x2 - rowStride2x2 - 1 , dummyd0, dummyd1, dummyd2, neighbourhood[0][0] );
                GroupsharedLoadQuadHV( centerAddr2x2 - rowStride2x2     , dummyd0, dummyd1, neighbourhood[1][0], neighbourhood[2][0] );
                GroupsharedLoadQuadHV( centerAddr2x2 - rowStride2x2 + 1 , dummyd0, dummyd1, neighbourhood[3][0], dummyd2 );
                GroupsharedLoadQuadHV( centerAddr2x2 - 1                , dummyd0, neighbourhood[0][1], dummyd1, neighbourhood[0][2] );
                GroupsharedLoadQuadHV( centerAddr2x2 + 1                , neighbourhood[3][1], dummyd0, neighbourhood[3][2], dummyd1 );
                GroupsharedLoadQuadHV( centerAddr2x2 - 1 + rowStride2x2 , dummyd0, neighbourhood[0][3], dummyd1, dummyd2 );
                GroupsharedLoadQuadHV( centerAddr2x2 + rowStride2x2     , neighbourhood[1][3], neighbourhood[2][3], dummyd0, dummyd1 );
                neighbourhood[1][0].y = topRow[0]; // already in registers
                neighbourhood[2][0].y = topRow[1]; // already in registers
                neighbourhood[0][1].x = leftColumn[0]; // already in registers
                neighbourhood[0][2].x = leftColumn[1]; // already in registers
                neighbourhood[1][1] = qe0; // already in registers
                neighbourhood[2][1] = qe1; // already in registers
                neighbourhood[1][2] = qe2; // already in registers
                neighbourhood[2][2] = qe3; // already in registers
                ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

                topRow[0]     = ( topRow[0]     - ComputeLocalContrastH( 0, -1, neighbourhood ) ) > GetActualEdgeThreshold();
                topRow[1]     = ( topRow[1]     - ComputeLocalContrastH( 1, -1, neighbourhood ) ) > GetActualEdgeThreshold();
                leftColumn[0] = ( leftColumn[0] - ComputeLocalContrastV( -1, 0, neighbourhood ) ) > GetActualEdgeThreshold();
                leftColumn[1] = ( leftColumn[1] - ComputeLocalContrastV( -1, 1, neighbourhood ) ) > GetActualEdgeThreshold();

                ce[0].x = ( qe0.x - ComputeLocalContrastV( 0, 0, neighbourhood ) ) > GetActualEdgeThreshold();
                ce[0].y = ( qe0.y - ComputeLocalContrastH( 0, 0, neighbourhood ) ) > GetActualEdgeThreshold();
                ce[1].x = ( qe1.x - ComputeLocalContrastV( 1, 0, neighbourhood ) ) > GetActualEdgeThreshold();
                ce[1].y = ( qe1.y - ComputeLocalContrastH( 1, 0, neighbourhood ) ) > GetActualEdgeThreshold();
                ce[2].x = ( qe2.x - ComputeLocalContrastV( 0, 1, neighbourhood ) ) > GetActualEdgeThreshold();
                ce[2].y = ( qe2.y - ComputeLocalContrastH( 0, 1, neighbourhood ) ) > GetActualEdgeThreshold();
                ce[3].x = ( qe3.x - ComputeLocalContrastV( 1, 1, neighbourhood ) ) > GetActualEdgeThreshold();
                ce[3].y = ( qe3.y - ComputeLocalContrastH( 1, 1, neighbourhood ) ) > GetActualEdgeThreshold();
            #else
                topRow[0]     = topRow[0]    > GetActualEdgeThreshold();
                topRow[1]     = topRow[1]    > GetActualEdgeThreshold();
                leftColumn[0] = leftColumn[0]> GetActualEdgeThreshold();
                leftColumn[1] = leftColumn[1]> GetActualEdgeThreshold();
                ce[0].x = qe0.x > GetActualEdgeThreshold();
                ce[0].y = qe0.y > GetActualEdgeThreshold();
                ce[1].x = qe1.x > GetActualEdgeThreshold();
                ce[1].y = qe1.y > GetActualEdgeThreshold();
                ce[2].x = qe2.x > GetActualEdgeThreshold();
                ce[2].y = qe2.y > GetActualEdgeThreshold();
                ce[3].x = qe3.x > GetActualEdgeThreshold();
                ce[3].y = qe3.y > GetActualEdgeThreshold();
            #endif

                //left
                ce[0].z = leftColumn[0];
                ce[1].z = ce[0].x;
                ce[2].z = leftColumn[1];
                ce[3].z = ce[2].x;

                // top
                ce[0].w = topRow[0];
                ce[1].w = topRow[1];
                ce[2].w = ce[0].y;
                ce[3].w = ce[1].y;

                [unroll]
                for( i = 0; i < 4; i++ )
                {
                    const int2 localPixelPos = pixelPos + qeOffsets[i];

                    const lpfloat4 edges = ce[i];
                    bool localPixelInBounds = IsPixelInBounds( localPixelPos );

                    // Preserve CMAA2's candidate detection while dropping work beyond the bounded candidate budget.
                    bool isCandidate = localPixelInBounds && ( edges.x * edges.y + edges.y * edges.z + edges.z * edges.w + edges.w * edges.x ) != 0;
                    if( isCandidate )
                    {
                        uint counterIndex;
                        if( TryReserveControlIndex( 4, Cmaa2CandidateCapacity, counterIndex ) )
                        {
                            uint2 candidatePixel = uint2( localPixelPos );
                            g_workingShapeCandidates[counterIndex] = (candidatePixel.x << 18) | (msaaSampleIndex << 14) | candidatePixel.y;
                        }
                    }

                    // Include zeroes for out-of-range pixels so partial edge quads remain well-defined.
                    uint packedEdge = localPixelInBounds ? PackEdges( edges ) : 0;
    #if CMAA_MSAA_SAMPLE_COUNT > 1
                    outEdges[i] |= packedEdge << (msaaSampleIndex * 4);
    #else
                    outEdges[i] = packedEdge;
    #endif
                }
            }
        }
    }

    [branch]
    if( inOutputKernel )
    {
#if CMAA_PACK_SINGLE_SAMPLE_EDGE_TO_HALF_WIDTH && CMAA_MSAA_SAMPLE_COUNT == 1
#if CMAA2_EDGE_UNORM
        if( pixelPos.x >= 0 && pixelPos.y >= 0 && pixelPos.x < int( Cmaa2Dimensions.x ) && pixelPos.y < int( Cmaa2Dimensions.y ) )
            g_workingEdges[ uint2( pixelPos.x / 2, pixelPos.y ) ] = ((outEdges[1] << 4) | outEdges[0]) / 255.0;
        if( pixelPos.x >= 0 && pixelPos.y + 1 >= 0 && pixelPos.x < int( Cmaa2Dimensions.x ) && pixelPos.y + 1 < int( Cmaa2Dimensions.y ) )
            g_workingEdges[ uint2( pixelPos.x / 2, pixelPos.y + 1 ) ] = ((outEdges[3] << 4) | outEdges[2]) / 255.0;
#else
        if( pixelPos.x >= 0 && pixelPos.y >= 0 && pixelPos.x < int( Cmaa2Dimensions.x ) && pixelPos.y < int( Cmaa2Dimensions.y ) )
            g_workingEdges[ uint2( pixelPos.x / 2, pixelPos.y ) ] = (outEdges[1] << 4) | outEdges[0];
        if( pixelPos.x >= 0 && pixelPos.y + 1 >= 0 && pixelPos.x < int( Cmaa2Dimensions.x ) && pixelPos.y + 1 < int( Cmaa2Dimensions.y ) )
            g_workingEdges[ uint2( pixelPos.x / 2, pixelPos.y + 1 ) ] = (outEdges[3] << 4) | outEdges[2];
#endif
#else
        [unroll]
        for( uint i = 0; i < 4; i++ )
        {
            int2 edgePos = pixelPos + qeOffsets[i];
            if( IsPixelInBounds( edgePos ) )
                g_workingEdges[ uint2( edgePos ) ] = outEdges[i];
        }
#endif
    }
}
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// Compute shaders used to generate DispatchIndirec() control buffer
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
//
// Compute dispatch arguments for the DispatchIndirect() that calls ProcessCandidatesCS and DeferredColorApply2x2CS
void ComputeDispatchArgsCS( uint3 groupID : SV_GroupID )
{
    // The first invocation group (1,0,0) builds candidates dispatch args for Dispatch(2,1,1).
    if( groupID.x == 1 )
    {
        uint candidateCount = min( Cmaa2Control[4], Cmaa2CandidateCapacity );
        Cmaa2DispatchArgs[0].x = ( candidateCount + CMAA2_PROCESS_CANDIDATES_NUM_THREADS - 1 ) / CMAA2_PROCESS_CANDIDATES_NUM_THREADS;
        Cmaa2DispatchArgs[0].y = 1;
        Cmaa2DispatchArgs[0].z = 1;
        Cmaa2Control[3] = candidateCount;
    }
    // The second invocation group (0,1,0) builds deferred args for Dispatch(1,2,1).
    else if( groupID.y == 1 )
    {
        uint locationCount = min( Cmaa2Control[8], Cmaa2BlendLocationCapacity );
        Cmaa2DispatchArgs[0].x = 1;
        Cmaa2DispatchArgs[0].y = ( locationCount + CMAA2_DEFERRED_APPLY_NUM_THREADS - 1 ) / CMAA2_DEFERRED_APPLY_NUM_THREADS;
        Cmaa2DispatchArgs[0].z = 1;
        Cmaa2Control[3] = locationCount;

        // Reset counters for the next frame's use of this private resource set.
        Cmaa2Control[4] = 0;
        Cmaa2Control[8] = 0;
        Cmaa2Control[12] = 0;
    }
}
//
///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


void FindZLineLengths( out lpfloat lineLengthLeft, out lpfloat lineLengthRight, uint2 screenPos, uniform bool horizontal, uniform bool invertedZShape, const float2 stepRight, uint msaaSampleIndex )
{
// this enables additional conservativeness test but is pretty detrimental to the final effect so left disabled by default even when CMAA2_EXTRA_SHARPNESS is enabled
#define CMAA2_EXTRA_CONSERVATIVENESS2 0
    /////////////////////////////////////////////////////////////////////////////////////////////////////////
    // TODO: a cleaner and faster way to get to these - a precalculated array indexing maybe?
    uint maskLeft, bitsContinueLeft, maskRight, bitsContinueRight;
    {
        // Horizontal (vertical is the same, just rotated 90- counter-clockwise)
        // Inverted Z case:              // Normal Z case:
        //   __                          // __
        //  X|                           //  X|
        // --                            //   --
        uint maskTraceLeft, maskTraceRight;
#if CMAA2_EXTRA_CONSERVATIVENESS2
        uint maskStopLeft, maskStopRight;
#endif
        if( horizontal )
        {
            maskTraceLeft = 0x08; // tracing top edge
            maskTraceRight = 0x02; // tracing bottom edge
#if CMAA2_EXTRA_CONSERVATIVENESS2
            maskStopLeft = 0x01; // stop on right edge
            maskStopRight = 0x04; // stop on left edge
#endif
        }
        else
        {
            maskTraceLeft = 0x04; // tracing left edge
            maskTraceRight = 0x01; // tracing right edge
#if CMAA2_EXTRA_CONSERVATIVENESS2
            maskStopLeft = 0x08; // stop on top edge
            maskStopRight = 0x02; // stop on bottom edge
#endif
        }
        if( invertedZShape )
        {
            uint temp = maskTraceLeft;
            maskTraceLeft = maskTraceRight;
            maskTraceRight = temp;
        }
        maskLeft = maskTraceLeft;
        bitsContinueLeft = maskTraceLeft;
        maskRight = maskTraceRight;
#if CMAA2_EXTRA_CONSERVATIVENESS2
        maskLeft |= maskStopLeft;
        maskRight |= maskStopRight;
#endif
        bitsContinueRight = maskTraceRight;
    }
    /////////////////////////////////////////////////////////////////////////////////////////////////////////

    bool continueLeft = true;
    bool continueRight = true;
    lineLengthLeft = 1;
    lineLengthRight = 1;
    [loop]
    for( ; ; )
    {
        uint edgeLeft = LoadEdge( int2( screenPos ) - int2( stepRight * float( lineLengthLeft ) ), int2( 0, 0 ), msaaSampleIndex );
        uint edgeRight = LoadEdge( int2( screenPos ) + int2( stepRight * ( float( lineLengthRight ) + 1 ) ), int2( 0, 0 ), msaaSampleIndex );

        // stop on encountering 'stopping' edge (as defined by masks)
        continueLeft    = continueLeft  && ( ( edgeLeft & maskLeft ) == bitsContinueLeft );
        continueRight   = continueRight && ( ( edgeRight & maskRight ) == bitsContinueRight );

        lineLengthLeft += continueLeft;
        lineLengthRight += continueRight;

        lpfloat maxLR = max( lineLengthRight, lineLengthLeft );

        // both stopped? cause the search end by setting maxLR to max length.
        if( !continueLeft && !continueRight )
            maxLR = (lpfloat)c_maxLineLength;

        // either the longer one is ahead of the smaller (already stopped) one by more than a factor of x, or both
        // are stopped - end the search.
#if CMAA2_EXTRA_SHARPNESS
        if( maxLR >= min( (lpfloat)c_maxLineLength, (1.20 * min( lineLengthRight, lineLengthLeft ) - 0.20) ) )
#else
        if( maxLR >= min( (lpfloat)c_maxLineLength, (1.25 * min( lineLengthRight, lineLengthLeft ) - 0.25) ) )
#endif
            break;
    }
}

// these are blendZ settings, determined empirically :)
static const float c_symmetryCorrectionOffset = 0.22;
#if CMAA2_EXTRA_SHARPNESS
static const float c_dampeningEffect          = 0.11;
#else
static const float c_dampeningEffect          = 0.15;
#endif

#if CMAA2_COLLECT_EXPAND_BLEND_ITEMS
bool CollectBlendZs( uint2 screenPos, bool horizontal, bool invertedZShape, lpfloat shapeQualityScore, lpfloat lineLengthLeft, lpfloat lineLengthRight, float2 stepRight, uint msaaSampleIndex )
{
    lpfloat leftOdd = c_symmetryCorrectionOffset * lpfloat( lineLengthLeft % 2 );
    lpfloat rightOdd = c_symmetryCorrectionOffset * lpfloat( lineLengthRight % 2 );

    lpfloat dampenEffect = saturate( lpfloat(lineLengthLeft + lineLengthRight - shapeQualityScore) * c_dampeningEffect ) ;

    lpfloat loopFrom = -floor( ( lineLengthLeft + 1 ) / 2 ) + 1.0;
    lpfloat loopTo = floor( ( lineLengthRight + 1 ) / 2 );

    uint itemIndex;
    const uint blendItemCount = (uint)( loopTo - loopFrom + 1 );
    uint currentCount = g_groupSharedBlendItemCount;
    [allow_uav_condition]
    while( true )
    {
        if( currentCount + blendItemCount > CMAA2_BLEND_ITEM_SLM_SIZE )
            return false;
        uint originalCount;
        InterlockedCompareExchange( g_groupSharedBlendItemCount, currentCount, currentCount + blendItemCount, originalCount );
        if( originalCount == currentCount )
        {
            itemIndex = currentCount;
            break;
        }
        currentCount = originalCount;
    }

    lpfloat totalLength = lpfloat(loopTo - loopFrom) + 1 - leftOdd - rightOdd;
    lpfloat lerpStep = lpfloat(1.0) / totalLength;

    lpfloat lerpFromK = (0.5 - leftOdd - loopFrom) * lerpStep;

    uint itemHeader     = (screenPos.x << 18) | (msaaSampleIndex << 14) | screenPos.y;
    uint itemValStatic  = (horizontal << 31) | (invertedZShape << 30);

    for( lpfloat i = loopFrom; i <= loopTo; i++ )
    {
        lpfloat lerpVal = lerpStep * i + lerpFromK;

        lpfloat secondPart = (i>0);
        lpfloat srcOffset = 1.0 - secondPart * 2.0;

        lpfloat lerpK = (lerpStep * i + lerpFromK) * srcOffset + secondPart;
        lerpK *= dampenEffect;

        int2 encodedItem;
        encodedItem.x = itemHeader;
        encodedItem.y = itemValStatic | ((uint(i+256) /*& 0x3FF*/) << 20) | ( (uint(srcOffset+256) /*& 0x3FF*/ ) << 10 ) | uint( saturate(lerpK) * 1023 + 0.5 );
        g_groupSharedBlendItems[itemIndex++] = encodedItem;
    }
    return true;
}
#endif

void BlendZs( uint2 screenPos, bool horizontal, bool invertedZShape, lpfloat shapeQualityScore, lpfloat lineLengthLeft, lpfloat lineLengthRight, float2 stepRight, uint msaaSampleIndex )
{
    float2 blendDir = ( horizontal ) ? ( float2( 0, -1 ) ) : ( float2( -1, 0 ) );

    if( invertedZShape )
        blendDir = -blendDir;

    lpfloat leftOdd = c_symmetryCorrectionOffset * lpfloat( lineLengthLeft % 2 );
    lpfloat rightOdd = c_symmetryCorrectionOffset * lpfloat( lineLengthRight % 2 );

    lpfloat dampenEffect = saturate( lpfloat(lineLengthLeft + lineLengthRight - shapeQualityScore) * c_dampeningEffect ) ;

    lpfloat loopFrom = -floor( ( lineLengthLeft + 1 ) / 2 ) + 1.0;
    lpfloat loopTo = floor( ( lineLengthRight + 1 ) / 2 );

    lpfloat totalLength = lpfloat(loopTo - loopFrom) + 1 - leftOdd - rightOdd;
    lpfloat lerpStep = lpfloat(1.0) / totalLength;

    lpfloat lerpFromK = (0.5 - leftOdd - loopFrom) * lerpStep;

    for( lpfloat i = loopFrom; i <= loopTo; i++ )
    {
        lpfloat lerpVal = lerpStep * i + lerpFromK;

        lpfloat secondPart = (i>0);
        lpfloat srcOffset = 1.0 - secondPart * 2.0;

        lpfloat lerpK = (lerpStep * i + lerpFromK) * srcOffset + secondPart;
        lerpK *= dampenEffect;

        int2 pixelPos = int2( screenPos ) + int2( stepRight * float( i ) );

        lpfloat3 colorCenter = LoadSourceColor( pixelPos, int2( 0, 0 ), msaaSampleIndex ).rgb;
        lpfloat3 colorFrom = LoadSourceColor( pixelPos + int2( blendDir * srcOffset ), int2( 0, 0 ), msaaSampleIndex ).rgb;
        lpfloat3 output = lerp( colorCenter.rgb, colorFrom.rgb, lerpK );

        StoreColorSample( pixelPos, output, true, msaaSampleIndex );
}
}

// TODO:
// There were issues with moving this (including the calling code) to half-float on some hardware (broke in certain cases on RX 480).
// Further investigation is required.
void DetectZsHorizontal( in lpfloat4 edges, in lpfloat4 edgesM1P0, in lpfloat4 edgesP1P0, in lpfloat4 edgesP2P0, out lpfloat invertedZScore, out lpfloat normalZScore )
{
    // Inverted Z case:
    //   __
    //  X|
    // --
    {
        invertedZScore  = edges.r * edges.g *                edgesP1P0.a;
        invertedZScore  *= 2.0 + ((edgesM1P0.g + edgesP2P0.a) ) - (edges.a + edgesP1P0.g) - 0.7 * (edgesP2P0.g + edgesM1P0.a + edges.b + edgesP1P0.r);
    }

    // Normal Z case:
    // __
    //  X|
    //   --
    {
        normalZScore    = edges.r * edges.a *                edgesP1P0.g;
        normalZScore    *= 2.0 + ((edgesM1P0.a + edgesP2P0.g) ) - (edges.g + edgesP1P0.a) - 0.7 * (edgesP2P0.a + edgesM1P0.g + edges.b + edgesP1P0.r);
    }
}

void ProcessCandidatesCS( uint3 dispatchThreadID : SV_DispatchThreadID, uint3 groupThreadID : SV_GroupThreadID )
{
#if CMAA2_COLLECT_EXPAND_BLEND_ITEMS
    if( groupThreadID.x == 0 )
        g_groupSharedBlendItemCount = 0;
    GroupMemoryBarrierWithGroupSync( );
#endif

    uint msaaSampleIndex = 0;
    const uint numCandidates = Cmaa2Control[3];
    if( dispatchThreadID.x < numCandidates )
    {

	uint pixelID = g_workingShapeCandidates[dispatchThreadID.x];

#if 0 // debug display
    uint2 screenSize;
    g_inoutColorReadonly.GetDimensions( screenSize.x, screenSize.y );
    StoreColorSample( uint2(dispatchThreadID.x % screenSize.x, dispatchThreadID.x / screenSize.x), lpfloat3( 1, 1, 0 ), false, msaaSampleIndex );
    return;
#endif

    uint2 pixelPos = uint2( (pixelID >> 18) /*& 0x3FFF*/, pixelID & 0x3FFF );
    if( all( pixelPos < Cmaa2Dimensions ) )
    {
#if CMAA_MSAA_SAMPLE_COUNT > 1
    msaaSampleIndex = (pixelID >> 14) & 0x07;
#endif

#if CMAA_MSAA_SAMPLE_COUNT > 1
    int4 loadPosCenter = int4( pixelPos, msaaSampleIndex, 0 );
#else
    int3 loadPosCenter = int3( pixelPos, 0 );
#endif

    uint edgesCenterPacked = LoadEdge( pixelPos, int2( 0, 0 ), msaaSampleIndex );
    lpfloat4 edges      = UnpackEdgesFlt( edgesCenterPacked );
    lpfloat4 edgesLeft  = UnpackEdgesFlt( LoadEdge( pixelPos, int2( -1, 0 ), msaaSampleIndex ) );
    lpfloat4 edgesRight = UnpackEdgesFlt( LoadEdge( pixelPos, int2(  1, 0 ), msaaSampleIndex ) );
    lpfloat4 edgesBottom= UnpackEdgesFlt( LoadEdge( pixelPos, int2( 0,  1 ), msaaSampleIndex ) );
    lpfloat4 edgesTop   = UnpackEdgesFlt( LoadEdge( pixelPos, int2( 0, -1 ), msaaSampleIndex ) );

    // simple shapes
    {
        lpfloat4 blendVal = ComputeSimpleShapeBlendValues( edges, edgesLeft, edgesRight, edgesTop, edgesBottom, true );

        const lpfloat fourWeightSum = dot( blendVal, lpfloat4( 1, 1, 1, 1 ) );
        const lpfloat centerWeight = 1.0 - fourWeightSum;

        lpfloat3 outColor = LoadSourceColor( pixelPos, int2( 0, 0 ), msaaSampleIndex ).rgb * centerWeight;
        [flatten]
        if( blendVal.x > 0.0 )   // from left
        {
            lpfloat3 pixelL = LoadSourceColor( pixelPos, int2( -1, 0 ), msaaSampleIndex ).rgb;
            outColor.rgb += blendVal.x * pixelL;
        }
        [flatten]
        if( blendVal.y > 0.0 )   // from above
        {
            lpfloat3 pixelT = LoadSourceColor( pixelPos, int2( 0, -1 ), msaaSampleIndex ).rgb;
            outColor.rgb += blendVal.y * pixelT;
        }
        [flatten]
        if( blendVal.z > 0.0 )   // from right
        {
            lpfloat3 pixelR = LoadSourceColor( pixelPos, int2( 1, 0 ), msaaSampleIndex ).rgb;
            outColor.rgb += blendVal.z * pixelR;
        }
        [flatten]
        if( blendVal.w > 0.0 )   // from below
        {
            lpfloat3 pixelB = LoadSourceColor( pixelPos, int2( 0, 1 ), msaaSampleIndex ).rgb;
            outColor.rgb += blendVal.w * pixelB;
        }

        StoreColorSample( pixelPos.xy, outColor, false, msaaSampleIndex );
    }

    // complex shapes - detect
    {
        lpfloat invertedZScore;
        lpfloat normalZScore;
        lpfloat maxScore;
        bool horizontal = true;
        bool invertedZ = false;
        // lpfloat shapeQualityScore;    // 0 - best quality, 1 - some edges missing but ok, 2 & 3 - dubious but better than nothing

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // horizontal
        {
            lpfloat4 edgesM1P0 = edgesLeft;
            lpfloat4 edgesP1P0 = edgesRight;
            lpfloat4 edgesP2P0 = UnpackEdgesFlt( LoadEdge( pixelPos, int2(  2, 0 ), msaaSampleIndex ) );

            DetectZsHorizontal( edges, edgesM1P0, edgesP1P0, edgesP2P0, invertedZScore, normalZScore );
            maxScore = max( invertedZScore, normalZScore );

            if( maxScore > 0 )
            {
                invertedZ = invertedZScore > normalZScore;
            }
        }
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // vertical
        {
            // Reuse the same code for vertical (used for horizontal above), but rotate input data 90 degrees counter-clockwise, so that:
            // left     becomes     bottom
            // top      becomes     left
            // right    becomes     top
            // bottom   becomes     right

            // we also have to rotate edges, thus .argb
            lpfloat4 edgesM1P0 = edgesBottom;
            lpfloat4 edgesP1P0 = edgesTop;
            lpfloat4 edgesP2P0 = UnpackEdgesFlt( LoadEdge( pixelPos, int2( 0, -2 ), msaaSampleIndex ) );

            DetectZsHorizontal( edges.argb, edgesM1P0.argb, edgesP1P0.argb, edgesP2P0.argb, invertedZScore, normalZScore );
            lpfloat vertScore = max( invertedZScore, normalZScore );

            if( vertScore > maxScore )
            {
                maxScore = vertScore;
                horizontal = false;
                invertedZ = invertedZScore > normalZScore;
                //shapeQualityScore = floor( clamp(4.0 - maxScore, 0.0, 3.0) );
            }
        }
        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        if( maxScore > 0 )
        {
#if CMAA2_EXTRA_SHARPNESS
            lpfloat shapeQualityScore = round( clamp(4.0 - maxScore, 0.0, 3.0) );    // 0 - best quality, 1 - some edges missing but ok, 2 & 3 - dubious but better than nothing
#else
            lpfloat shapeQualityScore = floor( clamp(4.0 - maxScore, 0.0, 3.0) );    // 0 - best quality, 1 - some edges missing but ok, 2 & 3 - dubious but better than nothing
#endif

            const float2 stepRight = ( horizontal ) ? ( float2( 1, 0 ) ) : ( float2( 0, -1 ) );
            lpfloat lineLengthLeft, lineLengthRight;
            FindZLineLengths( lineLengthLeft, lineLengthRight, pixelPos, horizontal, invertedZ, stepRight, msaaSampleIndex );

            lineLengthLeft  -= shapeQualityScore;
            lineLengthRight -= shapeQualityScore;

            if( ( lineLengthLeft + lineLengthRight ) >= (5.0) )
            {
#if CMAA2_COLLECT_EXPAND_BLEND_ITEMS
                // try adding to SLM but fall back to in-place processing if full (which only really happens in synthetic test cases)
                if( !CollectBlendZs( pixelPos, horizontal, invertedZ, shapeQualityScore, lineLengthLeft, lineLengthRight, stepRight, msaaSampleIndex ) )
#endif
                    BlendZs( pixelPos, horizontal, invertedZ, shapeQualityScore, lineLengthLeft, lineLengthRight, stepRight, msaaSampleIndex );
            }
        }
    }

    }

    }

#if CMAA2_COLLECT_EXPAND_BLEND_ITEMS
    GroupMemoryBarrierWithGroupSync( );

    uint totalItemCount = min( CMAA2_BLEND_ITEM_SLM_SIZE, g_groupSharedBlendItemCount );

    // spread items into waves
    uint loops = (totalItemCount+(CMAA2_PROCESS_CANDIDATES_NUM_THREADS-1)-groupThreadID.x)/CMAA2_PROCESS_CANDIDATES_NUM_THREADS;

    for( uint loop = 0; loop < loops; loop++ )
    {
        uint    index           = loop*CMAA2_PROCESS_CANDIDATES_NUM_THREADS + groupThreadID.x;

        uint2   itemVal         = g_groupSharedBlendItems[index];

        uint2   startingPos     = uint2( (itemVal.x >> 18) /*& 0x3FFF*/, itemVal.x & 0x3FFF );
        uint itemMSAASampleIndex= 0;
#if CMAA_MSAA_SAMPLE_COUNT > 1
        itemMSAASampleIndex     = (itemVal.x >> 14) & 0x07;
#endif

        bool    itemHorizontal  = (itemVal.y >> 31) & 1;
        bool    itemInvertedZ   = (itemVal.y >> 30) & 1;
        lpfloat itemStepIndex   = float((itemVal.y >> 20) & 0x3FF) - 256.0;
        lpfloat itemSrcOffset   = ((itemVal.y >> 10) & 0x3FF) - 256.0;
        lpfloat itemLerpK       = (itemVal.y & 0x3FF) / 1023.0;

        lpfloat2 itemStepRight    = ( itemHorizontal ) ? ( lpfloat2( 1, 0 ) ) : ( lpfloat2( 0, -1 ) );
        lpfloat2 itemBlendDir     = ( itemHorizontal ) ? ( lpfloat2( 0, -1 ) ) : ( lpfloat2( -1, 0 ) );
        if( itemInvertedZ )
            itemBlendDir = -itemBlendDir;

        int2 itemPixelPos = int2( startingPos ) + int2( itemStepRight * lpfloat( itemStepIndex ) );

        lpfloat3 colorCenter = LoadSourceColor( itemPixelPos, int2( 0, 0 ), itemMSAASampleIndex ).rgb;
        lpfloat3 colorFrom = LoadSourceColor( itemPixelPos + int2( itemBlendDir * lpfloat( itemSrcOffset ) ), int2( 0, 0 ), itemMSAASampleIndex ).rgb;
        lpfloat3 outputColor = lerp( colorCenter.rgb, colorFrom.rgb, itemLerpK );

        StoreColorSample( itemPixelPos, outputColor, true, itemMSAASampleIndex );
    }
#endif

}

void DeferredColorApply2x2CS( uint3 dispatchThreadID : SV_DispatchThreadID, uint3 groupThreadID : SV_GroupThreadID )
{
    const uint numCandidates = Cmaa2Control[3];
#if CMAA2_DEFERRED_APPLY_THREADGROUP_SWAP
    const uint currentCandidate     = dispatchThreadID.y;
    const uint currentQuadOffsetXY  = groupThreadID.x;
#else
    const uint currentCandidate     = dispatchThreadID.x;
    const uint currentQuadOffsetXY  = groupThreadID.y;
#endif

    if( currentCandidate >= numCandidates )
        return;

    uint pixelID    = g_workingDeferredBlendLocationList[currentCandidate];
    uint2 quadPos   = uint2( (pixelID >> 16), pixelID & 0xFFFF );
    const int2 qeOffsets[4] = { {0, 0}, {1, 0}, {0, 1}, {1, 1} };
    int2 pixelPos = int2( quadPos * 2 + uint2( qeOffsets[currentQuadOffsetXY] ) );
    if( !IsPixelInBounds( pixelPos ) )
        return;

    uint counterIndexWithHeader = g_workingDeferredBlendItemListHeads[quadPos];

    int counter = 0;

#if CMAA_MSAA_SAMPLE_COUNT > 1
    lpfloat4 outColors[CMAA_MSAA_SAMPLE_COUNT];
    [unroll]
    for( uint msaaSampleIndex = 0; msaaSampleIndex < CMAA_MSAA_SAMPLE_COUNT; msaaSampleIndex++ )
        outColors[msaaSampleIndex] = lpfloat4( 0, 0, 0, 0 );
    bool hasValue = false;
#else
    lpfloat4 outColors = lpfloat4( 0, 0, 0, 0 );
#endif

    const uint maxLoops = 32*CMAA_MSAA_SAMPLE_COUNT;   // do the loop to prevent bad data hanging the GPU <- probably not needed
    {
        for( uint i = 0; (counterIndexWithHeader != 0xFFFFFFFF) && ( i < maxLoops); i ++ )
        {
            // decode item-specific info: {2 bits for 2x2 quad location}, {3 bits for MSAA sample index}, {1 bit for isComplexShape flag}, {26 bits for address}
            uint offsetXY           = (counterIndexWithHeader >> 30) & 0x03;
            uint msaaSampleIndex    = (counterIndexWithHeader >> 27) & 0x07;
            bool isComplexShape     = (counterIndexWithHeader >> 26) & 0x01;

            uint2 val = g_workingDeferredBlendItemList[ counterIndexWithHeader & 0x03FFFFFFu ];

            counterIndexWithHeader  = val.x;

            if( offsetXY == currentQuadOffsetXY )
            {
                lpfloat3 color      = InternalUnpackColor(val.y);
                lpfloat weight      = 0.8 + 1.0 * lpfloat(isComplexShape);
#if CMAA_MSAA_SAMPLE_COUNT > 1
                outColors[msaaSampleIndex] += lpfloat4( color * weight, weight );
                hasValue = true;
#else
                outColors += lpfloat4( color * weight, weight );
#endif
            }
            //numberOfElements[offsetXY]++;
        }
    }

#if CMAA_MSAA_SAMPLE_COUNT > 1
    if( !hasValue )             return;
#else
    if( outColors.a == 0 )      return;
#endif

    {
#if CMAA_MSAA_SAMPLE_COUNT > 1
        lpfloat4 outColor = 0;
        for( uint msaaSampleIndex = 0; msaaSampleIndex < CMAA_MSAA_SAMPLE_COUNT; msaaSampleIndex++ )
        {
            if( outColors[msaaSampleIndex].a != 0 )
                outColor.xyz += outColors[msaaSampleIndex].rgb / (outColors[msaaSampleIndex].a);
            else
                outColor.xyz += LoadSourceColor( pixelPos, int2(0, 0), msaaSampleIndex );
        }
        outColor /= (lpfloat)CMAA_MSAA_SAMPLE_COUNT;
#else
        lpfloat4 outColor = outColors;
        outColor.rgb /= outColor.a;
#endif
        FinalUAVStore( pixelPos, lpfloat3(outColor.rgb) );
    }
}

void DebugDrawEdgesCS( uint2 dispatchThreadID : SV_DispatchThreadID )
{
    int msaaSampleIndex = 0;
    lpfloat4 edges = UnpackEdgesFlt( LoadEdge( dispatchThreadID, int2( 0, 0 ), msaaSampleIndex ) );

    // show MSAA control mask
    // uint v = g_inColorMSComplexityMaskReadonly.Load( int3( dispatchThreadID, 0 ) );
    // FinalUAVStore( dispatchThreadID, float3( v, v, v ) );
    // return;

#if 0
#if CMAA_MSAA_SAMPLE_COUNT > 1
    uint2 pixelPos = dispatchThreadID.xy / 2 * 2;
    /*
    uint all2x2MSSamplesDifferent = 0;

     [unroll] for( uint x = 0; x < 4; x++ )
         [unroll] for( uint y = 0; y < 4; y++ )
             all2x2MSSamplesDifferent |= g_inColorMSComplexityMaskReadonly.Load( int3( pixelPos, 0 ), int2( x-1, y-1 ) ) > 0;
    bool firstLoopIsEnough = all2x2MSSamplesDifferent == 0;
    */

#if CMAA_MSAA_USE_COMPLEXITY_MASK
    float2 texSize;
    g_inColorMSComplexityMaskReadonly.GetDimensions( texSize.x, texSize.y );
    float2 gatherUV = float2(pixelPos) / texSize;
    float4 TL = g_inColorMSComplexityMaskReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 0, 0 ) );
    float4 TR = g_inColorMSComplexityMaskReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 2, 0 ) );
    float4 BL = g_inColorMSComplexityMaskReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 0, 2 ) );
    float4 BR = g_inColorMSComplexityMaskReadonly.GatherRed( g_gather_point_clamp_Sampler, gatherUV, int2( 2, 2 ) );
    float4 sumAll = TL+TR+BL+BR;
    bool firstLoopIsEnough = !any(sumAll);

    //all2x2MSSamplesDifferent = (all2x2MSSamplesDifferent != 0)?(CMAA_MSAA_SAMPLE_COUNT):(1);
    FinalUAVStore( dispatchThreadID, (firstLoopIsEnough).xxx );
    return;
#endif
#endif
#endif


    //if( any(edges) )
    {
        lpfloat4 outputColor = lpfloat4( lerp( edges.xyz, 0.5.xxx, edges.a * 0.2 ), 1.0 );
        FinalUAVStore( dispatchThreadID, outputColor.rgb );
    }

//#if CMAA2_EDGE_DETECTION_LUMA_PATH == 2
//    FinalUAVStore( dispatchThreadID, g_inLumaReadonly.Load( int3( dispatchThreadID.xy, 0 ) ).r );
//#endif
}

#endif // #ifndef __cplusplus

#endif // #ifndef __CMAA2_HLSL__
