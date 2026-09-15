#ifndef UI_BATCHED_SCISSOR_HLSL
#define UI_BATCHED_SCISSOR_HLSL
#include "ui/rounded_rect.hlsl"
	struct ClipShape
	{
		float4 Rect;
		float4 RadiiH;
		float4 RadiiV;
		float4x4 TransformMat;
	};

	// Must match UICssBoxBatched.ScissorInstance
	#define MAX_CLIPS 4
	struct ScissorData
	{
		int Count;
		int Invert;
		int Next;
		int Pad1;
		ClipShape Clips[MAX_CLIPS];
	};


StructuredBuffer<ScissorData> ScissorBuffer < Attribute( "ScissorBuffer" ); >;
	// Carry the pixel footprint through each inverse homography. Derivatives stay outside
	// the variable-length clip loops; the quotient rule accounts for perspective within them.
	float ScissorCoverage( int index, float2 vPanelPos )
	{
		float2 vPixelX = ddx( vPanelPos );
		float2 vPixelY = ddy( vPanelPos );

		float coverage = 1.0;
		[loop]
		while ( index >= 0 )
		{
			ScissorData scissor = ScissorBuffer[index];
			float localCoverage = 1.0;
			[loop]
			for ( int k = 0; k < scissor.Count; k++ )
			{
				ClipShape c = scissor.Clips[k];
				if ( any( c.Rect.zw <= c.Rect.xy ) ) { localCoverage = 0; break; }
				float4 q = mul( c.TransformMat, float4( vPanelPos, 0, 1 ) );
				if ( q.w <= 0.0 ) { localCoverage = 0; break; }
				float2 p = q.xy / q.w;
				float2 vCentre = ( c.Rect.xy + c.Rect.zw ) * 0.5;
				float2 vHalf = ( c.Rect.zw - c.Rect.xy ) * 0.5;
				float d = RoundedRectSdf( p - vCentre, vHalf, c.RadiiH, c.RadiiV );
				float4 qDx = mul( c.TransformMat, float4( vPixelX, 0, 0 ) );
				float4 qDy = mul( c.TransformMat, float4( vPixelY, 0, 0 ) );
				// Reject pixels outside the projected bounding box before the local footprint
				// can blow up near the inverse homography's horizon and leak SDF coverage.
				float4 edges = float4( q.xy - c.Rect.xy * q.w, c.Rect.zw * q.w - q.xy );
				float4 edgesDx = float4( qDx.xy - c.Rect.xy * qDx.w, c.Rect.zw * qDx.w - qDx.xy );
				float4 edgesDy = float4( qDy.xy - c.Rect.xy * qDy.w, c.Rect.zw * qDy.w - qDy.xy );
				if ( any( edges < -0.5 * ( abs( edgesDx ) + abs( edgesDy ) ) ) ) { localCoverage = 0; break; }
				float2 dpDx = ( qDx.xy - p * qDx.w ) / q.w;
				float2 dpDy = ( qDy.xy - p * qDy.w ) / q.w;
				float flPixel = 0.5 * ( length( dpDx ) + length( dpDy ) );
				localCoverage *= saturate( 0.5 - d / max( flPixel, 0.0001 ) );
			}
			coverage *= scissor.Invert ? 1.0 - localCoverage : localCoverage;
			index = scissor.Next;
		}
		return coverage;
	}

#endif
