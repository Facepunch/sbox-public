#ifndef DDGI_HLSL
#define DDGI_HLSL

#include "common/classes/Bindless.hlsl"

struct DDGIVolume
{
    float4x4 WorldToProbeTransform;
    float3 BBoxMin;
    float3 BBoxMax;
    float NormalBias;
    float3 ProbeSpacing;
    float ProbeCellDiagonal;
    float3 ReciprocalSpacing;
    int IrradianceTextureIndex;
    float3 ReciprocalCountsMinusOne;
    int DistanceTextureIndex;
    int3 ProbeCounts;
    int RelocationTextureIndex;
    int BakeVersion;
    float4x4 ProbeToWorldTransform;
    float ViewBias;
    float RuntimeContrast;
    float RuntimeSaturation;
    float RuntimeIntensity;
    float3 RuntimeTint;
    float BlendDistance;

    bool IsValid()
    {
        return IrradianceTextureIndex > 0;
    }

    float3 ToProbeSpace( float3 positionWs )
    {
        return mul( WorldToProbeTransform, float4( positionWs, 1.0f ) ).xyz;
    }

    float GetBlendWeight( float3 positionWs )
    {
        float3 probeSpace = ToProbeSpace( positionWs );
        float3 faceDistance = min( probeSpace - BBoxMin, BBoxMax - probeSpace );
        if ( all( faceDistance >= 0.0f ) )
            return 1.0f;

        if ( BlendDistance <= 0.0f )
            return 0.0f;

        // Inverse-transform row lengths convert local plane distances to world units,
        // including rotated, mirrored and nonuniformly scaled volumes.
        float3 inverseScale = float3( length( WorldToProbeTransform[0].xyz ),
                                     length( WorldToProbeTransform[1].xyz ),
                                     length( WorldToProbeTransform[2].xyz ) );
        faceDistance /= max( inverseScale, 1e-6f );
        return saturate( 1.0f - length( max( -faceDistance, 0.0f ) ) / BlendDistance );
    }
};

StructuredBuffer<DDGIVolume> DDGIVolumes < Attribute( "DDGI_Volumes" ); >;
uint DDGIVolumeCount < Attribute( "DDGI_VolumeCount" ); >;

#define DDGISampler g_sBilinearClamp
#define DDGI_IRRADIANCE_OCT_RESOLUTION 8
#define DDGI_DISTANCE_OCT_RESOLUTION 16

struct DDGI
{
    // Tile size equals the octahedral resolution (no border texels)
    static uint TileSize(uint resolution)
    {
        return resolution;
    }

    // Base coordinate of a probe's tile in the 2D atlas slice
    static uint2 BaseCoordinate(uint2 probeXY, uint resolution)
    {
        return probeXY * TileSize(resolution);
    }

    // Convert a texel index [0, octResolution-1] to its normalized octahedral coordinate [0, 1].
    // Standard center mapping (Cigolle et al. 2014): texel i samples the direction at its centre,
    // oct = (i + 0.5) / N. Integration writes this direction into every texel of the tile.
    static float2 TexelToOctahedralCoord(uint2 texelIdx, uint octResolution)
    {
        return (float2(texelIdx) + 0.5f) / float(octResolution);
    }

    // Inverse of TexelToOctahedralCoord: map a normalized octahedral coord [0,1] to a continuous
    // texel coordinate. oct = (i + 0.5)/N inverts to texelCoord = oct * N. We clamp into
    // [0.5, octResolution-0.5] so the bilinear taps stay on this probe's texels: the boundary
    // half-texel wraps onto the edge texel by itself instead of bleeding into the neighbour tile.
    static float2 OctahedralCoordToTexel(float2 octNormalized, uint octResolution)
    {
        float n = float(octResolution);
        return clamp( octNormalized * n, 0.5f, n - 0.5f );
    }

    // Computes the surfaceBias parameter used by DDGI evaluation
    // The surfaceNormal and cameraDirection arguments are expected to be normalized
    static float3 GetSurfaceBias(float3 surfaceNormal, float3 cameraDirection, DDGIVolume volume)
    {
        float viewBias = volume.BakeVersion == 1 ? volume.ViewBias : 0.0f;
        return (surfaceNormal * volume.NormalBias) + (cameraDirection * viewBias);
    }

    static float3 OctahedralDecode(float2 octCoord)
    {
        // Convert from [0,1] to [-1,1] range
        float2 oct = (octCoord * 2.0f) - 1.0f;
        
        float3 direction = float3(oct.xy, 1.0f - abs(oct.x) - abs(oct.y));
        if (direction.z < 0.0f)
        {
            float2 signNotZero = float2((direction.x >= 0.0f) ? 1.0f : -1.0f, (direction.y >= 0.0f) ? 1.0f : -1.0f);
            direction.xy = (1.0f - abs(direction.yx)) * signNotZero;
        }
        return normalize(direction);
    }

    static float2 OctahedralEncode(float3 direction)
    {
        float l1norm = abs(direction.x) + abs(direction.y) + abs(direction.z);
        float2 result = direction.xy * (1.0f / l1norm);
        if (direction.z < 0.0f)
        {
            float2 signNotZero = float2((result.x >= 0.0f) ? 1.0f : -1.0f, (result.y >= 0.0f) ? 1.0f : -1.0f);
            result = (1.0f - abs(result.yx)) * signNotZero;
        }
        
        // Convert from [-1,1] to [0,1] range
        return (result * 0.5f) + 0.5f;
    }

    // Load probe relocation offset and active state from the relocation texture
    // Returns offset in xyz, active state (0 or 1) is written to outActive
    static float3 GetProbeRelocationOffset( in DDGIVolume volume, int3 probeIndex, out bool outActive )
    {
        outActive = true;
        
        if ( volume.RelocationTextureIndex <= 0 )
            return 0;

        Texture3D relocationTex = Bindless::GetTexture3D( volume.RelocationTextureIndex );
        float4 data = relocationTex.Load( int4( probeIndex, 0 ) );
        
        // Alpha channel stores active state
        outActive = data.w > 0.5f;
        return data.xyz;
    }

    // Sample a probe's octahedral tile with a single hardware bilinear fetch.
    // OctahedralCoordToTexel clamps the local coordinate into [0.5, N-0.5], so both bilinear
    // taps always land on this probe's texels and never bleed into a neighbouring tile.
    static float4 SampleProbeOctahedral( in DDGIVolume volume, Texture3D tex, int3 probeIndex, float3 direction, uint octResolution )
    {
        float2 octNormalized = OctahedralEncode( direction );
        float2 tileLocal = OctahedralCoordToTexel( octNormalized, octResolution );

        float2 atlasSize = max( float2( volume.ProbeCounts.xy ) * float( octResolution ), 1.0f );
        float2 texelCoord = float2( probeIndex.xy ) * float( octResolution ) + tileLocal;

        float3 uvw;
        uvw.xy = texelCoord / atlasSize;
        uvw.z = ( float( probeIndex.z ) + 0.5f ) / max( float( volume.ProbeCounts.z ), 1.0f );

        return tex.SampleLevel( DDGISampler, uvw, 0.0f );
    }

    static float3 SampleProbeIrradiance( in DDGIVolume volume, Texture3D irradianceTex, int3 probeIndex, float3 direction )
    {
        return SampleProbeOctahedral( volume, irradianceTex, probeIndex, direction, DDGI_IRRADIANCE_OCT_RESOLUTION ).rgb;
    }

    static float2 SampleProbeDistance( in DDGIVolume volume, Texture3D distanceTex, int3 probeIndex, float3 direction, float maxDistance )
    {
        if ( volume.BakeVersion == 1 )
        {
            // Filter mean and standard deviation using the law of total variance.
            // Subtracting independently compressed first/second moments loses small variances.
            const int n = DDGI_DISTANCE_OCT_RESOLUTION;
            float2 texel = clamp( OctahedralEncode( direction ) * n - 0.5f, 0.0f, float(n - 1) );
            int2 p0 = int2( floor( texel ) );
            int2 p1 = min( p0 + 1, n - 1 );
            float2 alpha = frac( texel );
            int2 tileBase = probeIndex.xy * n;
            float2 a = distanceTex.Load( int4( tileBase + p0, probeIndex.z, 0 ) ).rg;
            float2 b = distanceTex.Load( int4( tileBase + int2( p1.x, p0.y ), probeIndex.z, 0 ) ).rg;
            float2 c = distanceTex.Load( int4( tileBase + int2( p0.x, p1.y ), probeIndex.z, 0 ) ).rg;
            float2 d = distanceTex.Load( int4( tileBase + p1, probeIndex.z, 0 ) ).rg;
            float4 weights = float4( (1 - alpha.x) * (1 - alpha.y), alpha.x * (1 - alpha.y), (1 - alpha.x) * alpha.y, alpha.x * alpha.y );
            float4 means = float4( a.x, b.x, c.x, d.x );
            float4 deviations = float4( a.y, b.y, c.y, d.y );
            float mean = dot( weights, means );
            float4 delta = means - mean;
            float variance = dot( weights, deviations * deviations + delta * delta );
            return float2( mean * maxDistance, variance * maxDistance * maxDistance );
        }

        // Saved legacy data retains its original sampling and visibility calculation.
        return SampleProbeOctahedral( volume, distanceTex, probeIndex, direction, DDGI_DISTANCE_OCT_RESOLUTION ).rg;
    }

    static float ComputeVisibility(float distanceToSample, float2 meanVariance, bool corrected)
    {
        float mean = meanVariance.x;           // Mean distance
        float variance = meanVariance.y;        // Variance of distance

        // Minimum variance threshold to prevent numerical instability
        // and overly harsh shadows from low-variance regions
        const float minVariance = 0.001f;
        variance = max(variance, minVariance);

        // If we're clearly in front of the mean surface, fully visible
        // Small epsilon to handle grazing angles
        if (distanceToSample <= mean * 1.01f)
            return 1.0f;

        // Use the same biased surface as the early-out, avoiding a discontinuity there.
        float delta = distanceToSample - mean * (corrected ? 1.01f : 1.0f);
        float chebyshev = variance / (variance + delta * delta);

        // Sharpen the curve to reduce light leaking
        chebyshev = chebyshev * chebyshev * chebyshev; // pow(chebyshev, 3)
        
        // Apply a smooth threshold to cut off very low visibility values
        // This helps eliminate residual light leak from the tail of the distribution
        const float visibilityThreshold = 0.05f;
        chebyshev = smoothstep(0.0f, visibilityThreshold * 2.0f, chebyshev) * chebyshev;

        return chebyshev;
    }

    static bool IsEnabled()
    {
        return DDGIVolumeCount > 0;
    }

    static float3 Evaluate( DDGIVolume volume, float3 positionWs, float3 normalWs, float3 cameraDirection = float3(0,0,0) )
    {
        // Extend the boundary lighting through the outward blend region.
        float3 positionPs = volume.ToProbeSpace( positionWs );
        if ( volume.BlendDistance > 0.0f && (any( positionPs < volume.BBoxMin ) || any( positionPs > volume.BBoxMax )) )
        {
            positionPs = clamp( positionPs, volume.BBoxMin, volume.BBoxMax );
            positionWs = mul( volume.ProbeToWorldTransform, float4( positionPs, 1.0f ) ).xyz;
        }

        Texture3D irradianceTex = Bindless::GetTexture3D( volume.IrradianceTextureIndex );
        Texture3D distanceTex = Bindless::GetTexture3D( volume.DistanceTextureIndex );

        // Apply surface bias to reduce light leaking
        float3 surfaceBias = GetSurfaceBias( normalWs, cameraDirection, volume );
        float3 biasedPosition = positionWs + surfaceBias;

        float3 probeSpacePosition = volume.ToProbeSpace( biasedPosition );

        if ( any( probeSpacePosition < volume.BBoxMin ) || any( probeSpacePosition > volume.BBoxMax ) )
        {
            if ( volume.BakeVersion != 1 && volume.BlendDistance <= 0.0f )
                return 0;

            // A sampling bias must not turn a point inside the volume into a black border.
            probeSpacePosition = clamp( probeSpacePosition, volume.BBoxMin, volume.BBoxMax );
            biasedPosition = mul( volume.ProbeToWorldTransform, float4( probeSpacePosition, 1.0f ) ).xyz;
        }

        int3 baseGridCoord = clamp( int3( (probeSpacePosition - volume.BBoxMin) * volume.ReciprocalSpacing ),
                                    int3( 0, 0, 0 ),
                                    volume.ProbeCounts - int3( 1, 1, 1 ) );

        float3 baseProbePos = volume.ProbeSpacing * float3(baseGridCoord) + volume.BBoxMin;

        // Alpha is how far from the floor(currentVertex) position. on [0, 1] for each axis.
        float3 alpha = clamp( (probeSpacePosition - baseProbePos) / volume.ProbeSpacing, 0.0f.xxx, 1.0f.xxx );

        float3 accumulatedIrradiance = 0.0f.xxx;
        float accumulatedWeight = 0.0f;

        [unroll]
        for ( int i = 0; i < 8; ++i )
        {
            int3 offset = int3( i, i >> 1, i >> 2 ) & int3( 1, 1, 1 );
            int3 probeGridCoord = clamp( baseGridCoord + offset, int3( 0, 0, 0 ), volume.ProbeCounts - int3( 1, 1, 1 ) );

            // Base probe position in probe space
            float3 probePos = volume.ProbeSpacing * float3( probeGridCoord ) + volume.BBoxMin;

            // Relocation is stored in world space. Legacy bakes retain their original interpretation.
            bool probeActive = true;
            float3 relocationOffset = GetProbeRelocationOffset( volume, probeGridCoord, probeActive );
            
            // Skip inactive probes (inside geometry)
            if ( !probeActive )
                continue;

            float3 relocatedProbePos = probePos + relocationOffset;
            float3 samplePosition = positionPs;
            float3 biasedSamplePosition = positionPs + normalWs * volume.NormalBias;

            if ( volume.BakeVersion == 1 )
            {
                relocatedProbePos = mul( volume.ProbeToWorldTransform, float4( probePos, 1.0f ) ).xyz + relocationOffset;
                samplePosition = positionWs;
                biasedSamplePosition = biasedPosition;
            }

            // Use relocated probe position for distance calculations
            float3 probeToPoint = volume.BakeVersion == 1
                ? biasedSamplePosition - relocatedProbePos
                : (positionPs - relocatedProbePos) + (normalWs * volume.NormalBias);
            float distanceToProbe = length( probeToPoint );
            if ( distanceToProbe < 1e-5f )
                continue;

            float3 direction = -probeToPoint / distanceToProbe;

            float3 trilinear = lerp( 1.0f - alpha, alpha, float3( offset ) );
            float weight = 1.0f;

            float3 trueDirectionToProbe = normalize( relocatedProbePos - samplePosition );
            

            float maxDistance = max( 1.0f, volume.ProbeCellDiagonal + length( relocationOffset ) );
            float2 distanceMoments = SampleProbeDistance( volume, distanceTex, probeGridCoord, direction, maxDistance );
            float visibility = ComputeVisibility( distanceToProbe, distanceMoments, volume.BakeVersion == 1 );
            weight *= visibility;

            float3 irradiance = SampleProbeIrradiance( volume, irradianceTex, probeGridCoord, -normalWs );

            // Aggressively crush low weights to reduce light leaking
            // Probes with very low visibility/backface weight are likely leaking
            const float crushThreshold = 0.25f;
            if ( weight < crushThreshold )
            {
                // Cubic falloff for smooth but aggressive crushing
                float t = weight / crushThreshold;
                weight = crushThreshold * t * t * t;
            }

            // Backface weight: aggressively reduce contribution from probes behind the surface
            // Using a tighter wrap that doesn't add a constant offset
            float backfaceDot = dot(trueDirectionToProbe, normalWs);

            // Smooth falloff that reaches zero at 90 degrees from normal
            // This prevents probes on the other side of walls from contributing
            weight *= saturate(backfaceDot) + (volume.BakeVersion == 1 ? 0.0f : 0.01f);

            weight *= trilinear.x * trilinear.y * trilinear.z;

            accumulatedIrradiance += weight * irradiance;
            accumulatedWeight += weight;
        }

        if ( accumulatedWeight <= (volume.BakeVersion == 1 ? 0.0f : 1e-5f) )
            return 0;

        // Integration already stores cosine-weighted radiance (irradiance / pi).
        // Preserve the historical gain only for existing saved bakes.
        float3 irradiance = volume.BakeVersion == 1
            ? accumulatedIrradiance / accumulatedWeight
            : (0.5f * 3.14159265f) * (accumulatedIrradiance / accumulatedWeight);

        // Grade this volume's blended lighting. Neutral controls preserve the
        // original result exactly; capture buffers always supply neutral values.
        if ( volume.RuntimeContrast != 1.0f || volume.RuntimeSaturation != 1.0f )
        {
            float luminance = dot( irradiance, float3( 0.2126f, 0.7152f, 0.0722f ) );
            irradiance = lerp( luminance.xxx, irradiance, volume.RuntimeSaturation );
            irradiance = max( 0.0f, (irradiance - 0.18f) * volume.RuntimeContrast + 0.18f );
        }

        return irradiance * volume.RuntimeIntensity * max( volume.RuntimeTint, 0.0f );
    }

    // RGB is weighted irradiance; alpha is coverage for callers with fallback lighting.
    // Preserve first-volume priority: a full-weight volume ends the search as before.
    static float4 EvaluateVolumes( float3 positionWs, float3 normalWs, float3 cameraDirection = float3(0,0,0) )
    {
        float4 result = 0.0f;
        [loop]
        for ( uint volumeIdx = 0u; volumeIdx < DDGIVolumeCount; ++volumeIdx )
        {
            DDGIVolume volume = DDGIVolumes[volumeIdx];
            if ( !volume.IsValid() )
                continue;

            float weight = volume.GetBlendWeight( positionWs ) * (1.0f - result.a);
            if ( weight <= 0.0f )
                continue;

            result.rgb += Evaluate( volume, positionWs, normalWs, cameraDirection ) * weight;
            result.a += weight;
            if ( result.a >= 1.0f )
                break;
        }

        return result;
    }
};

#endif // DDGI_HLSL






