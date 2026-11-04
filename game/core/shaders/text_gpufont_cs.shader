//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// text_gpufont_cs.shader
//
// Rasterizes text instances into a texture for the few consumers that need one: Bitmap text, scene text scopes
// and background-clip: text masks. The UI draws the same instances live through ui_cssbox_batched.shader, text
// particles through sprite_ps.shader and command list text through ui_text.shader; all evaluate them with
// ui/text.hlsl. Driven by GpuFontText.cs.
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
HEADER
{
	DevShader = true;
	Description = "GPU text rasterization from glyph outlines";
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
	#include "system.fxc"
}

CS
{
	#include "ui/text.hlsl"

	RWTexture2D<float4> TextOutput < Attribute( "TextOutput" ); >;

	int MipLevel < Attribute( "MipLevel" ); >;
	float4 BaseColor < Attribute( "BaseColor" ); >;			// what fully transparent texels hold, so filtering never drags in black

	[numthreads( 8, 8, 1 )]
	void MainCs( uint3 id : SV_DispatchThreadID )
	{
		int2 size = int2( TextWidth, TextHeight );
		if ( any( int2( id.xy ) >= max( size >> MipLevel, 1 ) ) )
			return;

		float ps = (float)( 1 << MipLevel );
		float2 p = ( float2( id.xy ) + 0.5 ) * ps;

		TextOutput[id.xy] = TextComposite( p, ps, TextInstanceOffset, TextTileOffset, TextTilesX, size, BaseColor.rgb );
	}
}
