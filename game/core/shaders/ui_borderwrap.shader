HEADER
{
	DevShader = true;
	Version = 1;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
	Forward();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
	#include "ui/features.hlsl"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "ui/common.hlsl"
}
  
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	#include "ui/vertex.hlsl"  
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	#include "ui/pixel.hlsl"  

	float4 g_vViewport < Source( Viewport ); >; 

	// Texture Samplers ---------------------------------------------------------------------------------------------------------------------------------------
	Texture2D g_tColor < Attribute( "Texture" ); SrgbRead( true ); Default( 1.0 ); >;
	float4 g_vInvTextureDim < Source( InvTextureDim ); SourceArg( g_tColor ); >;

	//
	// Filter
	//
	float4 FilterBorderWrapColor< UiType( Color ); Default4( 1.0f, 1.0f, 1.0f, 1.0f ); Attribute( "FilterBorderWrapColor" ); >;
	float2 FilterBorderWrapColorScale < UiType( Slider ); Default2( 1.0f, 1.0f ); Attribute( "FilterBorderWrapColorScale" ); >;
	float FilterBorderWrapWidth< UiType( Slider ); Default( 0.0f ); Attribute( "FilterBorderWrapWidth" ); >;

	// Always write rgba
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );

	// Never cull
	RenderState( CullMode, NONE );

	// No depth
	RenderState( DepthWriteEnable, false );

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

	float SampleAlpha( float2 uv )
	{
		return g_tColor.Sample( g_sTrilinearBorder, uv ).a;
	}

	float GetBorder( float2 uv )
	{
		float2 pixelSize = 1.0 / BoxSize;
		pixelSize *= FilterBorderWrapWidth;

		float Pi = M_PI * 2;
		float Directions = 32.0; // works well with larger border sizes

		float alpha = 0.0f;
	
		// Sample alpha at points along circle
		for ( float d = 0.0; d < Pi; d += Pi / Directions )
		{
			float2 off = float2( cos( d ), sin( d ) ) * pixelSize;
			alpha += SampleAlpha( uv + off );
		}

		alpha /= M_PI;

		return alpha;
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;

		UI_CommonProcessing_Pre( i );

		//
		// Calculate texcoords
		// 
		float2 texCoord = i.vTexCoord.xy;
		
		// Scale down UVs based on the blur
		if ( FilterBorderWrapWidth > 0 )
		{
			float2 scale = FilterBorderWrapColorScale;

			// Center texcoords
			texCoord = texCoord - ( 1.0f - scale ) * 0.5f;
			texCoord = texCoord / scale;
		}

		// Sample
		float edge = GetBorder( texCoord );

		// For smooth edges, we want to blend nothing with the border color
		float4 colorTransparent = FilterBorderWrapColor;
		colorTransparent.a = 0;
		o.vColor = lerp( colorTransparent, FilterBorderWrapColor, edge );
		
		// Sharpen edges by increasing alpha
		o.vColor.a = saturate( o.vColor.a * 2.0f - 1.0f );
		
		return UI_CommonProcessing_Post( i, o );
	}
}
