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

	float4 CornerRadius < Attribute( "BorderRadius" ); >;
	float2 PanelSize < Attribute( "PanelSize" ); >;
	float OutlineWidth < Attribute( "OutlineWidth" ); >;
	float OutlineOffset < Attribute( "OutlineOffset" ); >;
	float Bloat < Attribute( "Bloat" ); >;

	// Render State -------------------------------------------------------------------------------------------------------------------------------------------
	RenderState( SrgbWriteEnable0, true );
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

	float RoundedRectangle( float2 pos, float2 center, float2 box, float size )
	{
		return size - length( pos - center );
	}

	//
	// Returns positive distance inside the rounded rect, negative outside.
	//
	float DrawRoundedRect( float2 pos, float2 size )
	{
		float f = 1;

		f = min( pos.x, size.x - pos.x );
		f = min( f, pos.y );
		f = min( f, size.y - pos.y );

		//
		// Top Left Radius
		//
		float r = min( size.y * 0.5, CornerRadius[0] );
		if ( pos.x < r && pos.y < r )
		{
			f = min( f, RoundedRectangle( pos, r, size, r ) );
		}

		//
		// Top Right Radius
		//
		r = min( size.y * 0.5, CornerRadius[1] );
		if ( pos.x > size.x - r && pos.y < r )
		{
			f = min( f, RoundedRectangle( pos, float2( size.x - r, r ), size, r ) );
		}

		//
		// Bottom Left Radius
		//
		r = min( size.y * 0.5, CornerRadius[2] );
		if ( pos.x < r && pos.y > size.y - r )
		{
			f = min( f, RoundedRectangle( pos, float2( r, size.y - r ), size, r ) );
		}

		//
		// Bottom Right Radius
		//
		r = min( size.y * 0.5, CornerRadius[3] );
		if ( pos.x > size.x - r && pos.y > size.y - r )
		{
			f = min( f, RoundedRectangle( pos, float2( size.x - r, size.y - r ), size, r ) );
		}

		return f;
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;
		UI_CommonProcessing_Pre( i );

		float2 pos = ( PanelSize + float2( Bloat, Bloat ) * 2.0 ) * i.vTexCoord.xy - float2( Bloat, Bloat );
		float d = DrawRoundedRect( pos, PanelSize );
		float dist_outside = -d;

		float inner_aa = smoothstep( OutlineOffset - 0.25, OutlineOffset + 0.25, dist_outside );
		float outer_aa = 1.0 - smoothstep( OutlineOffset + OutlineWidth - 0.5, OutlineOffset + OutlineWidth + 0.5, dist_outside );

		o.vColor = i.vColor;
		o.vColor.a *= inner_aa * outer_aa;

		return UI_CommonProcessing_Post( i, o );
	}
}
