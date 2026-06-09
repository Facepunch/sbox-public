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
    
    #include "terrain/TerrainSplatFormat.hlsl"

	RWTexture2D<float> ControlMap < Attribute( "ControlMap" ); >;
	
    float2 ControlUV < Attribute( "ControlUV" ); >;
    int BrushSize < Attribute( "BrushSize" ); >;
    float BrushStrength < Attribute( "BrushStrength" ); >;
	Texture2D<float> Brush < Attribute( "Brush" ); >;
    int PaintMaterialIndex < Attribute( "SplatChannel" ); >;

    SamplerState g_sBilinearBorder < Filter( BILINEAR ); AddressU( BORDER ); AddressV( BORDER ); >;

	[numthreads( 16, 16, 1 )]
	void MainCs( uint nGroupIndex : SV_GroupIndex, uint3 vThreadId : SV_DispatchThreadID )
	{
		float w, h;
		ControlMap.GetDimensions( w, h );

		int2 texelCenter = int2( float2( w, h ) * ControlUV );
		int2 texelOffset = int2( vThreadId.xy ) - int( BrushSize / 2 );

		int2 texel = texelCenter + texelOffset;
		if ( texel.x < 0 || texel.y < 0 || texel.x >= w || texel.y >= h ) return;

		float2 brushUV = float2( vThreadId.xy ) / BrushSize;
		float brushValue = Brush.SampleLevel( g_sBilinearBorder, brushUV, 0 ) * BrushStrength;
		float brushAmount = saturate( abs( brushValue ) );

		// Skip if brush has no effect at this pixel
		if ( brushAmount < 0.001 ) return;

		// Decode existing material
		CompactTerrainMaterial material = CompactTerrainMaterial::DecodeFromFloat( ControlMap.Load( texel ) );

		uint paintMaterialId = (uint)PaintMaterialIndex;
		float baseWeight = 1.0 - material.GetNormalizedBlend();
		float overlayWeight = material.GetNormalizedBlend();
		float targetWeight = brushValue > 0.0 ? 1.0 : 0.0;

		if ( material.BaseTextureId == paintMaterialId )
		{
			baseWeight = lerp( baseWeight, targetWeight, brushAmount );
			overlayWeight = 1.0 - baseWeight;
		}
		else if ( material.OverlayTextureId == paintMaterialId )
		{
			overlayWeight = lerp( overlayWeight, targetWeight, brushAmount );
			baseWeight = 1.0 - overlayWeight;
		}
		else if ( brushValue > 0.0 )
		{
			if ( baseWeight <= overlayWeight )
			{
				material.BaseTextureId = paintMaterialId;
				baseWeight = lerp( 0.0, 1.0, brushAmount );
				overlayWeight = 1.0 - baseWeight;
			}
			else
			{
				material.OverlayTextureId = paintMaterialId;
				overlayWeight = lerp( 0.0, 1.0, brushAmount );
				baseWeight = 1.0 - overlayWeight;
			}
		}
		else
		{
			return;
		}

		if ( overlayWeight > baseWeight )
		{
			uint oldBaseTextureId = material.BaseTextureId;
			material.BaseTextureId = material.OverlayTextureId;
			material.OverlayTextureId = oldBaseTextureId;

			float oldBaseWeight = baseWeight;
			baseWeight = overlayWeight;
			overlayWeight = oldBaseWeight;
		}

		material.BlendFactor = uint( saturate( overlayWeight ) * 255.0 + 0.5 );

		// Write back
		ControlMap[texel] = material.EncodeToFloat();
    }
}
