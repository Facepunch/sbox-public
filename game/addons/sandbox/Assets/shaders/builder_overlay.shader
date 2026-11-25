HEADER
{
	Description = "Builder snap points and alignment guides";
	DevShader = true;
}

MODES
{
	Forward();
}

FEATURES
{
}

COMMON
{
	#define CUSTOM_MATERIAL_INPUTS
	#include "common/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionWs : POSITION < Semantic( None ); >;
	float4 vColor : COLOR0 < Semantic( None ); >;
	float3 vShape : NORMAL < Semantic( None ); >;
	float4 vStart : TEXCOORD0 < Semantic( None ); >;
	float4 vPattern : TEXCOORD1 < Semantic( None ); >;
	float4 vEnd : TANGENT0 < Semantic( None ); >;
};

struct PixelInput
{
	float4 vColor : TEXCOORD0;
	float4 vPattern : TEXCOORD1;
	float3 vShape : TEXCOORD2;
	noperspective float2 vLocal : TEXCOORD3;
	nointerpolation float4 vDash : TEXCOORD4;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif

	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	static const float NearW = 0.01;

	float2 ClipToPixels( float4 clip )
	{
		return ( clip.xy / clip.w * float2( 0.5, -0.5 ) + 0.5 ) * g_vViewportSize;
	}

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vColor = float4( SrgbGammaToLinear( i.vColor.rgb ), i.vColor.a );
		o.vPattern = i.vPattern;
		o.vShape = i.vShape;
		o.vDash = 0;

		float2 pixelToClip = 2.0 * g_vInvViewportSize;

		if ( i.vShape.x > 0.5 )
		{
			float4 center = Position3WsToPs( i.vPositionWs );
			center.xy += i.vStart.xy * i.vShape.y * 0.5 * pixelToClip * center.w;
			o.vPositionPs = center;
			o.vLocal = i.vStart.xy;
			return o;
		}

		float4 start = Position3WsToPs( i.vStart.xyz );
		float4 end = Position3WsToPs( i.vEnd.xyz );
		bool atStart = distance( i.vPositionWs, i.vStart.xyz ) <= distance( i.vPositionWs, i.vEnd.xyz );

		if ( ( start.w < NearW ) != ( end.w < NearW ) )
		{
			float t = ( NearW - start.w ) / ( end.w - start.w );
			float4 crossing = lerp( start, end, t );
			bool clipsStart = start.w < NearW;
			if ( clipsStart ) start = crossing;
			else end = crossing;

			if ( clipsStart == atStart ) o.vPattern.x = t * i.vPattern.z;
		}

		float2 startPx = ClipToPixels( start );
		float2 endPx = ClipToPixels( end );
		float2 direction = endPx - startPx;
		direction = length( direction ) > 0.0001 ? normalize( direction ) : float2( 1, 0 );
		float2 normal = float2( -direction.y, direction.x );

		float4 position = atStart ? start : end;
		position.xy += normal * float2( 1, -1 ) * i.vStart.w * i.vShape.y * 0.5 * pixelToClip * position.w;

		o.vPositionPs = position;
		o.vLocal = float2( i.vStart.w, 0 );
		o.vDash = float4( startPx, direction );
		return o;
	}
}

PS
{
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, false );
	RenderState( CullMode, NONE );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float4 color = i.vColor;
		float width = i.vShape.y;

		if ( i.vShape.x > 0.5 )
		{
			float2 pixels = abs( i.vLocal ) * width * 0.5;
			float bar = i.vShape.z * 0.5;
			float arm = saturate( width * 0.5 - max( pixels.x, pixels.y ) + 0.5 );
			color.a *= saturate( bar - min( pixels.x, pixels.y ) + 0.5 ) * arm;
		}
		else
		{
			float dash = i.vShape.z;
			if ( dash > 0 )
			{
				float alongPx = dot( i.vPositionSs.xy - g_vViewportOffset.xy - i.vDash.xy, i.vDash.zw );
				if ( frac( alongPx / ( dash * 1.5 ) ) > 2.0 / 3.0 ) discard;
			}

			float along = i.vPattern.x;
			float solid = i.vPattern.y;
			float total = i.vPattern.z;
			if ( along > solid ) color.a *= 1.0 - saturate( ( along - solid ) / max( total - solid, 0.001 ) );

			color.a *= saturate( width * 0.5 * ( 1.0 - abs( i.vLocal.x ) ) + 0.5 );
		}

		if ( color.a < 0.002 ) discard;
		return color;
	}
}
