// Copyright (c) Facepunch. All Rights Reserved.

//
// Terrain Splat Map Format
// Contains only the encoding/decoding logic for the splat map format.
// Can be included independently by compute shaders and other utilities.
//

#ifndef TERRAIN_SPLAT_FORMAT_H
#define TERRAIN_SPLAT_FORMAT_H

// SplatMap Format: Each RGBA32F channel stores packed uint32 as float

struct SplatChannel
{
    uint MaterialIndex;
    float Weight;       // Normalized 0-1
    uint ExtraData;     // 16-bit extra data
    
    static SplatChannel Decode( uint raw )
    {
        SplatChannel result;
        result.MaterialIndex = (raw >> 24) & 0xFF;
        result.Weight = float((raw >> 16) & 0xFF) / 255.0;
        result.ExtraData = raw & 0xFFFF;
        return result;
    }
    
    float Encode()
    {
        uint weightByte = uint(saturate(Weight) * 255.0 + 0.5);
        uint packed = (MaterialIndex << 24) | (weightByte << 16) | ExtraData;
        return asfloat(packed);
    }
    
    bool IsHole()
    {
        return (ExtraData & 0x01) != 0;
    }
    
    void SetHole( bool isHole )
    {
        ExtraData = isHole ? (ExtraData | 0x01) : (ExtraData & 0xFFFE);
    }
};

struct SplatPixel
{
    SplatChannel Channels[4];
    
    static SplatPixel Decode( float4 pixel )
    {
        uint4 raw = asuint(pixel);
        SplatPixel result;
        result.Channels[0] = SplatChannel::Decode(raw.r);
        result.Channels[1] = SplatChannel::Decode(raw.g);
        result.Channels[2] = SplatChannel::Decode(raw.b);
        result.Channels[3] = SplatChannel::Decode(raw.a);
        return result;
    }
    
    float4 Encode()
    {
        return float4(
            Channels[0].Encode(),
            Channels[1].Encode(),
            Channels[2].Encode(),
            Channels[3].Encode()
        );
    }
    
    bool IsHole()
    {
        return Channels[0].IsHole() || Channels[1].IsHole() || 
               Channels[2].IsHole() || Channels[3].IsHole();
    }
};

// Legacy compatibility functions
void DecodeSplatMap( float4 pixel, out uint indices[4], out float weights[4] )
{
    SplatPixel splat = SplatPixel::Decode(pixel);
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        indices[i] = splat.Channels[i].MaterialIndex;
        weights[i] = splat.Channels[i].Weight;
    }
}

bool IsHoleFromSplatMap( float4 pixel )
{
    return SplatPixel::Decode(pixel).IsHole();
}

// Witcher Format: Compact base/overlay texture blending
// Packed format (32-bit uint):
// - Bits 0-4:   Base texture ID (0-31)
// - Bits 5-9:   Overlay texture ID (0-31)
// - Bits 10-17: Blend factor between base and overlay (0-255)
// - Bit 18:     Hole flag (1=hole, 0=solid)
// - Bits 19-31: Reserved for future use (13 bits)
struct CompactTerrainMaterial
{
    uint BaseTextureId;      // 0-31
    uint OverlayTextureId;   // 0-31
    uint BlendFactor;        // 0-255 (0=full base, 255=full overlay)
    bool IsHole;
    uint Reserved;           // 13 bits for future use
    
    // Decode from packed uint32
    static CompactTerrainMaterial Decode( uint packed )
    {
        CompactTerrainMaterial result;
        result.BaseTextureId = (packed >> 0) & 0x1F;        // 5 bits
        result.OverlayTextureId = (packed >> 5) & 0x1F;     // 5 bits
        result.BlendFactor = (packed >> 10) & 0xFF;         // 8 bits
        result.IsHole = ((packed >> 18) & 0x1) != 0;        // 1 bit
        result.Reserved = (packed >> 19) & 0x1FFF;          // 13 bits
        return result;
    }
    
    // Decode from float (GPU storage)
    static CompactTerrainMaterial DecodeFromFloat( float value )
    {
        return Decode( asuint(value) );
    }
    
    // Encode to packed uint32
    uint Encode()
    {
        uint packed = 0;
        packed |= (BaseTextureId & 0x1F) << 0;
        packed |= (OverlayTextureId & 0x1F) << 5;
        packed |= (BlendFactor & 0xFF) << 10;
        packed |= (IsHole ? 1u : 0u) << 18;
        packed |= (Reserved & 0x1FFF) << 19;
        return packed;
    }
    
    // Encode to float (GPU storage)
    float EncodeToFloat()
    {
        return asfloat( Encode() );
    }
    
    // Get normalized blend factor (0.0 - 1.0)
    float GetNormalizedBlend()
    {
        return float(BlendFactor) / 255.0;
    }
};

#endif // TERRAIN_SPLAT_FORMAT_H
