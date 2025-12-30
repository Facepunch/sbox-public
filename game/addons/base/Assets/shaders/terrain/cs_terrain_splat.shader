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
	int PaintLayer < Attribute( "PaintLayer" ); >;

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

		// Skip if brush has no effect at this pixel
		if ( abs( brushValue ) < 0.05 ) return;

		// Decode existing material
		CompactTerrainMaterial material = CompactTerrainMaterial::DecodeFromFloat( ControlMap.Load( texel ) );

		if ( PaintLayer == 0 ) // Painting the Base layer
		{
			material.BaseTextureId = PaintMaterialIndex;
		}
		else // Painting the Overlay layer
		{
			material.OverlayTextureId = PaintMaterialIndex;
			
			// Increase blend to make overlay material more visible
			float newBlend = saturate( material.GetNormalizedBlend() + brushValue );
			material.BlendFactor = uint( newBlend * 255.0 );
		}

		// Write back
		ControlMap[texel] = material.EncodeToFloat();
    }
}

