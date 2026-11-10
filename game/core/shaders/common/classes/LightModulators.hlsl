#ifndef LIGHT_MODULATORS_HLSL
#define LIGHT_MODULATORS_HLSL

#include "common/Bindless.hlsl"
#include "common_samplers.fxc"
#include "common.fxc"

// Scalar-layout mirror of LightModulatorData. Rectangles belong to the scene, not the camera.
struct LightModulatorRectangle
{
    float3 Origin;
    int TextureIndex;
    float3 U;
    float HalfWidth;
    float3 V;
    float HalfHeight;
    float3 Normal;
    int TwoSided;
    float3 Tint;
    float MipLevel;
    float DistanceFeather;
    float DistanceMipBias;
    float2 Tiling;
    float2 Offset;
    float2 ScrollSpeed;
    float2 Rotation; // cosine and sine, computed only when packing component settings
    float Contrast;
    float Brightness;
    int BlendMode;
    float Strength;
    float EdgeFeather;
    int Effect;
    float4 EffectParameters;
    int WorldSizedUVs;
    int Infinite;
};

StructuredBuffer<LightModulatorRectangle> LightModulatorRectangles < Attribute( "LightModulatorRectangles" ); >;
StructuredBuffer<uint> LightModulatorCandidates < Attribute( "LightModulatorCandidates" ); >;
uint LightModulatorRectangleCount < Attribute( "LightModulatorRectangleCount" ); Default( 0 ); >;
uint LightModulatorCandidateCount < Attribute( "LightModulatorCandidateCount" ); Default( 0 ); >;
bool LightModulatorsDisabled < Attribute( "LightModulatorsDisabled" ); Default( 0 ); >;

// Scene-wide light transmission queries shared by surfaces and volumetric fog.
struct LightModulators
{
    // Explicit LOD keeps samples valid inside divergent light and rectangle loops.
    static float4 SampleTexture( LightModulatorRectangle rect, float2 uv, float mip )
    {
        // The native sRGB view decodes sRGB formats and aliases the linear view for linear/HDR formats.
        uv += rect.Offset;
        if ( rect.Effect == 8 ) // Scroll
            uv -= frac( g_flTime * rect.ScrollSpeed );
        return Bindless::GetTexture2DSrgb( rect.TextureIndex ).SampleLevel( g_sTrilinearWrap, uv, mip );
    }

    // Effect animation stays on the GPU; physical aperture and feathering remain unchanged.
    static float4 SampleEffect( LightModulatorRectangle rect, float2 uv, float mip )
    {
        // Effect values match LightModulator.ModulationEffect. Default parameters: (1, 1, 1, 0).
        float4 p = rect.EffectParameters;
        float time = g_flTime * p.x + p.w;
        if ( rect.Effect == 1 ) // Caustics
        {
            float2 phase = frac( time * float2( 0.8f, -0.63f ) / 6.2831853f ) * 6.2831853f;
            float2 wobble = sin( uv.yx * 6.2831853f + phase ) * (0.025f * p.y);
            float2 uvA = uv + wobble + frac( time * float2( 0.035f, 0.017f ) );
            float2 uvB = float2( -uv.y, uv.x ) * (1.13f * max( abs( p.z ), 0.001f )) + float2( 0.37f, 0.61f )
                - wobble.yx - frac( time * float2( 0.025f, 0.031f ) );
            return min( SampleTexture( rect, uvA, mip ), SampleTexture( rect, uvB, mip ) );
        }

        if ( rect.Effect == 2 ) // Cloud Shadows
        {
            float4 a = SampleTexture( rect, uv + frac( time * float2( 0.012f, 0.008f ) ), mip );
            float4 b = SampleTexture( rect, uv * (1.7f * max( abs( p.z ), 0.001f ))
                - frac( time * float2( 0.006f, 0.009f ) ) + 0.37f, mip );
            return float4( lerp( 1.0f, a.rgb * b.rgb, saturate( p.y ) ), min( a.a, b.a ) );
        }

        if ( rect.Effect == 3 ) // Rotate
        {
            float s, c;
            sincos( frac( time * 0.1f ) * 6.2831853f, s, c );
            float2 centered = uv - 0.5f;
            uv = float2( c * centered.x + s * centered.y, -s * centered.x + c * centered.y ) + 0.5f;
        }
        else if ( rect.Effect == 4 ) // Kaleidoscope
        {
            float2 centered = uv - 0.5f;
            float radius = length( centered );
            float sector = 6.2831853f / clamp( round( 6.0f * abs( p.z ) ), 1.0f, 64.0f );
            float angle = (radius > 0.0f ? atan2( centered.y, centered.x ) : 0.0f) + frac( time * 0.1f ) * 6.2831853f;
            angle = abs( frac( angle / sector + 0.5f ) - 0.5f ) * sector;
            float2 folded = float2( cos( angle ), sin( angle ) ) * radius + 0.5f;
            uv = lerp( uv, folded, saturate( p.y ) );
        }
        else if ( rect.Effect == 5 ) // Chromatic Split
        {
            float phase = frac( time * p.z / 6.2831853f ) * 6.2831853f;
            float2 separation = float2( cos( phase ), sin( phase ) ) * (0.01f * p.y);
            float4 center = SampleTexture( rect, uv, mip );
            return float4( SampleTexture( rect, uv + separation, mip ).r, center.g,
                SampleTexture( rect, uv - separation, mip ).b, center.a );
        }
        else if ( rect.Effect == 6 ) // Ripple
        {
            float2 fromCenter = uv - 0.5f;
            float radius = length( fromCenter );
            uv += fromCenter / max( radius, 0.0001f ) * sin( radius * 12.0f * p.z - frac( time / 3.14159265f ) * 6.2831853f ) * (0.025f * p.y);
        }

        float4 sampleValue = SampleTexture( rect, uv, mip );
        if ( rect.Effect == 7 ) // Pulse
        {
            float wave = pow( saturate( 0.5f + 0.5f * sin( frac( time ) * 6.2831853f ) ), max( abs( p.z ), 0.001f ) );
            sampleValue.rgb *= lerp( 1.0f, wave, saturate( p.y ) );
        }
        return sampleValue;
    }

    // Intersect one rectangle with the receiver-to-source segment and sample its transmission.
    static float3 Sample( uint index, float3 position, float3 toLight, float distanceToLight )
    {
        LightModulatorRectangle rect = LightModulatorRectangles[index];
        if ( rect.Strength <= 0.0f )
            return 1.0f;

        float denominator = dot( rect.Normal, toLight );

        // Trace toward the source. A one-sided filter accepts light traveling along +normal.
        if ( abs( denominator ) < 0.00001f || (rect.TwoSided == 0 && denominator >= 0.0f) )
            return 1.0f;

        float distance = dot( rect.Origin - position, rect.Normal ) / denominator;
        if ( distance <= 0.01f || distance >= distanceToLight )
            return 1.0f;

        float3 relative = position + toLight * distance - rect.Origin;
        float2 projected = float2( dot( relative, rect.U ), dot( relative, rect.V ) ) * float2( 1.0f, -1.0f );
        float2 uv = projected + 0.5f;

        // Clip the physical aperture before transforming UVs: tiling never enlarges the rectangle.
        if ( rect.Infinite == 0 && (any( uv < 0.0f ) || any( uv > 1.0f )) )
            return 1.0f;

        float edgeDistance = min( min( uv.x, 1.0f - uv.x ), min( uv.y, 1.0f - uv.y ) );
        float coverage = rect.Strength;
        if ( rect.Infinite == 0 && rect.EdgeFeather > 0.0f )
            coverage *= smoothstep( 0.0f, rect.EdgeFeather, edgeDistance );

        // World-space distance normal to the plane keeps the fade width independent of the light angle.
        if ( rect.DistanceFeather > 0.0f )
            coverage *= smoothstep( 0.0f, rect.DistanceFeather, abs( dot( position - rect.Origin, rect.Normal ) ) );

        float2 centered = uv - 0.5f;
        if ( rect.WorldSizedUVs != 0 )
            centered = projected * float2( rect.HalfWidth, rect.HalfHeight ) / 64.0f;
        uv = float2( rect.Rotation.x * centered.x + rect.Rotation.y * centered.y,
            -rect.Rotation.y * centered.x + rect.Rotation.x * centered.y );
        uv = uv * rect.Tiling + 0.5f;

        // Explicit LOD is safe inside divergent light loops. Softening uses pane-to-receiver distance.
        float mip = min( 12.0f, rect.MipLevel + rect.DistanceMipBias * log2( 1.0f + distance / 128.0f ) );
        float4 sampleValue = SampleEffect( rect, uv, mip );

        float3 transmission = max( (sampleValue.rgb - 0.5f) * rect.Contrast + 0.5f, 0.0f );
        transmission *= rect.Brightness * rect.Tint;
        transmission = max( transmission, 0.0f );
        if ( rect.BlendMode == 1 )
            transmission = max( transmission, 1.0f );
        else if ( rect.BlendMode == 2 )
            transmission = min( transmission, 1.0f );

        return lerp( 1.0f, transmission, saturate( sampleValue.a ) * coverage );
    }

    // True when this view has any rectangles to evaluate. Uniform: test it once, outside light loops.
    static bool Active()
    {
        return !LightModulatorsDisabled && LightModulatorRectangleCount > 0;
    }

    // Evaluate only the candidates belonging to this local light.
    static float3 Evaluate( float3 position, float3 toLight, float distanceToLight, uint first, uint count )
    {
        if ( LightModulatorsDisabled || LightModulatorRectangleCount == 0 || first >= LightModulatorCandidateCount )
            return 1.0f;

        float3 transmission = 1.0f;
        count = min( count, LightModulatorCandidateCount - first );
        for ( uint i = 0; i < count; i++ )
        {
            uint index = LightModulatorCandidates[first + i];
            if ( index < LightModulatorRectangleCount )
                transmission *= Sample( index, position, toLight, distanceToLight );
        }

        return transmission;
    }

    // A directional light has unlimited reach, so every scene rectangle is a candidate.
    static float3 EvaluateSun( float3 position, float3 toLight )
    {
        if ( LightModulatorsDisabled )
            return 1.0f;

        float3 transmission = 1.0f;
        for ( uint i = 0; i < LightModulatorRectangleCount; i++ )
        {
            transmission *= Sample( i, position, toLight, 1.0e30f );
        }

        return transmission;
    }
};

#endif
