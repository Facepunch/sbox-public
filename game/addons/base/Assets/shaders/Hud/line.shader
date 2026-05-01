MODES
{ 
	Forward();
}

COMMON
{
	#include "Hud/sdf.hlsl"

	struct VS_INPUT
	{
		float2 NormalizedPosition : POSITION < Semantic( PosXyz ); >;
	};

	struct PixelInput
	{ 
		float4 PositionClipSpace	: SV_Position;
		float4 TexCoord				: TEXCOORD0;
		float4 Color				: TEXCOORD1;
		float4 Thickness			: TEXCOORD2;	
	};

	// For instancing all of this goes into 1 struct
	float4x4 TransformMatrix < Attribute( "TransformMat" ); >; 
	float2 LineStart < Attribute( "LineStart" ); >;
	float2 LineEnd < Attribute( "LineEnd" ); >;
	float LineThickness < Attribute( "LineThickness" ); >;
	int EndCaps < Attribute( "EndCaps" ); >;
	float4 ColorStart < Attribute( "ColorStart" ); >;
	float4 ColorEnd < Attribute( "ColorEnd" ); >;	
}

VS
{
	// Is this not a global somewhere
	float4 Viewport < Source( Viewport ); >;

	// How much to bloat lines in VS by to apply AA
	static const float AAPadding = 2;

	float4 ScreenToClipPos( float2 positionSs )
	{
		const float4 vMatrix = mul( TransformMatrix, float4( positionSs.xy, 0, 1 ) );
		positionSs.xy = vMatrix.xy / vMatrix.w;

		float2 positionClipSpace = 2.0 * ( positionSs.xy - Viewport.xy ) / ( Viewport.zw ) - float2( 1.0, 1.0 );
		positionClipSpace.y *= -1.0;
		return float4( positionClipSpace, 1.0f, 1.0f );
	}

	PixelInput MainVs( VS_INPUT i )
	{
		PixelInput o;

		// Remap our normalized -1, -1, 1, 1 verticies to where the line begins and ends
		const float2 vertOrigin = i.NormalizedPosition.y < 0 ? LineStart : LineEnd;

		// Get the normal, tangent and length of our line
		const float2 v = LineEnd - LineStart;
		const float lineLength = length( v );
		const float2 lineTangent = v / lineLength;
		const float2 lineNormal = float2( -lineTangent.y, lineTangent.x );

		const float radius = LineThickness * 0.5f;
		const float radiusAA = (LineThickness + AAPadding) * 0.5f;

		// Figure out vertex position
		float2 vertPos;
		if ( EndCaps > 0 )
		{
			// Rounded caps
			vertPos = vertOrigin + (lineNormal * i.NormalizedPosition.x + lineTangent * i.NormalizedPosition.y) * radiusAA;
		}
		else
		{
			vertPos = vertOrigin + lineNormal * (i.NormalizedPosition.x * radiusAA) + lineTangent * (i.NormalizedPosition.y * AAPadding);
		}

		o.Thickness.x = LineThickness;
		o.Thickness.y = LineThickness / ( lineLength + LineThickness );

		o.PositionClipSpace = ScreenToClipPos( vertPos.xy );

		o.TexCoord = float4( i.NormalizedPosition.xy, 0, 0 );
		o.TexCoord.x *= ( LineThickness + AAPadding ) / LineThickness;
		o.TexCoord.y *= ( lineLength + AAPadding * 2 ) / lineLength;

		o.Color = float4( i.NormalizedPosition.y / 2 + 0.5, 0, 0, 0 );

		return o;
	}
}

PS
{
	#include "common/blendmode.hlsl"

	RenderState( DepthEnable, true );
	RenderState( DepthWriteEnable, false );

	// Described in https://blog.frost.kiwi/analytical-anti-aliasing/
	float LineAAA( float v, float thickness )
	{
		float offset = saturate( thickness * 0.9 ) * 0.5;
		return 1.0 - saturate( ( abs( v ) - 1 ) / max( 0.00001, fwidth( v ) ) + offset );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float shapeMask = 1;

		// SDF + AAA mask the main line
		shapeMask = min( shapeMask, LineAAA( i.TexCoord.x, i.Thickness.x ) );
		shapeMask = min( shapeMask, LineAAA( i.TexCoord.y, i.Thickness.x ) );

		// Mask endcaps if we're using them
		if ( EndCaps > 0 )
		{
			float2 uv = abs( i.TexCoord.xy );
			uv.y = 1 + ( uv.y - 1 ) / i.Thickness.y;

			const float v = 1 - length( max( 0, uv ) );
			const float maskRound = saturate( v / max( 0.00001, fwidth( v ) ) + 0.5 );
			const float useMaskRound = ( i.TexCoord.y < 0 && ( EndCaps & 1 ) == 0 ) || ( i.TexCoord.y > 0 && ( EndCaps & 2 ) == 0 ) ? 0 : saturate( i.Thickness.x / 2 );

			shapeMask = min( shapeMask, lerp( 1, maskRound, useMaskRound ) );		
		}

		float4 color = lerp( ColorStart, ColorEnd, i.Color.x );
		return float4( color.rgb, shapeMask * color.a );
	}
}
