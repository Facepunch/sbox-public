HEADER
{
	DevShader = true;
	Description = "A";
}

MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	#include "system.fxc" // This should always be the first include in COMMON
}

CS
{
    #include "common.fxc"
    #include "procedural.hlsl"
    
    #include "terrain/TerrainSplatFormat.hlsl"

    DynamicCombo( D_SCULPT_MODE, 0..4, Sys( ALL ) );
        #define MODE_RAISE_LOWER    0
        #define MODE_FLATTEN        1
        #define MODE_SMOOTH         2
        #define MODE_HOLE           3
        #define MODE_NOISE          4

    RWTexture2D<float> Heightmap < Attribute( "Heightmap" ); >;
    RWTexture2D<float> ControlMap < Attribute( "ControlMap" ); >;
    
    struct BrushData
    {
        float2 UV;
        float Strength;
        int Size;
        float Rotation;
        float FlattenHeight;
        int SplatChannel;
    };
    StructuredBuffer<BrushData> BrushSettings < Attribute( "BrushSettings" ); >;

	Texture2D<float> Brush < Attribute( "Brush" ); >;

    SamplerState g_sBilinearBorder < Filter( BILINEAR ); AddressU( BORDER ); AddressV( BORDER ); >;

    float2 RotateBrushUV( float2 uv )
    {
        float2 centered = uv - 0.5;
        float sinA, cosA;
        sincos( BrushSettings[0].Rotation, sinA, cosA );
        return float2( centered.x * cosA - centered.y * sinA,
                       centered.x * sinA + centered.y * cosA ) + 0.5;
    }

	[numthreads( 16, 16, 1 )]
	void MainCs( uint nGroupIndex : SV_GroupIndex, uint3 vThreadId : SV_DispatchThreadID )
	{
        float w, h;
        Heightmap.GetDimensions( w, h );

        int2 texelCenter = int2( float2( w, h ) * BrushSettings[0].UV );
        int2 texelOffset = int2( vThreadId.xy ) - int( BrushSettings[0].Size / 2 );

        int2 texel = texelCenter + texelOffset;
        if ( texel.x < 0 || texel.y < 0 || texel.x >= w || texel.y >= h ) return;

        float2 brushUV = RotateBrushUV( float2( vThreadId.xy ) / BrushSettings[0].Size );

        if ( D_SCULPT_MODE == MODE_RAISE_LOWER )
        {
            float brush = Brush.SampleLevel( g_sBilinearBorder, brushUV, 0 );
            float height = Heightmap.Load( texel ).x;

            Heightmap[texel] = height + brush * 0.001f * BrushSettings[0].Strength;
        }
        if ( D_SCULPT_MODE == MODE_FLATTEN )
        {
            float brush = Brush.SampleLevel( g_sBilinearBorder, brushUV, 0 ) * BrushSettings[0].Strength;
            float height = Heightmap.Load( texel ).x;

            // TODO: I think we're gonna need the delta of the last hit UV, so we can flatten everything
            // between those two points, oherwise there'll be gaps
            Heightmap[texel] = lerp( height, BrushSettings[0].FlattenHeight, brush );
        }
        if ( D_SCULPT_MODE == MODE_SMOOTH )
        {
            float brush = Brush.SampleLevel( g_sBilinearBorder, brushUV, 0 ) * BrushSettings[0].Strength;
            if ( brush <= 0.0f ) return;
            
            // Sample surroundings (3x3)
            float sum = 0.0f;
            int sampleCount = 0;
            
            for ( int y = -1; y <= 1; y++ )
            {
                for ( int x = -1; x <= 1; x++ )
                {
                    int2 samplePos = texel + int2( x, y );
                    
                    if ( samplePos.x >= 0 && samplePos.y >= 0 && samplePos.x < w && samplePos.y < h )
                    {
                        sum += Heightmap.Load( samplePos ).x;
                        sampleCount++;
                    }
                }
            }
            
            float average = sum / sampleCount;
            float height = Heightmap.Load( texel ).x;
            
            Heightmap[texel] = lerp( height, average, brush );
        }
        if ( D_SCULPT_MODE == MODE_HOLE )
        {
            float brush = Brush.SampleLevel( g_sBilinearBorder, brushUV, 0 ) * BrushSettings[0].Strength;

            bool setHole = brush > 0.1f;
            bool clearHole = brush < -0.1f;
            
            if ( setHole || clearHole )
            {
                // Decode compact format, modify hole bit, encode
                float rawPixel = ControlMap.Load( texel );
                CompactTerrainMaterial material = CompactTerrainMaterial::DecodeFromFloat( rawPixel );
                material.IsHole = setHole;
                ControlMap[texel] = material.EncodeToFloat();
            }
        }
        if ( D_SCULPT_MODE == MODE_NOISE )
        {
            float brush = Brush.SampleLevel( g_sBilinearBorder, brushUV, 0 );
            if ( brush <= 0.0f ) return;
            
            float noise = FuzzyNoise( float2( texel ) );
            float noiseStrength = 0.00005f;
            
            // Remap noise from [0,1] to [-1,1] for both additive and subtractive
            noise = (noise * 2.0f - 1.0f);
            
            float height = Heightmap.Load( texel ).x;
            Heightmap[texel] = height + noise * noiseStrength * BrushSettings[0].Strength * brush;
        }
    }
}

