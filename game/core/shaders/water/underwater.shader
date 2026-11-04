HEADER
{
	Description = "Underwater screen distortion and world-space sediment";
}

MODES
{
	Default();
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
};

struct PixelInput
{
	#if PROGRAM == VFX_PROGRAM_VS
		float4 vPositionPs : SV_Position;
	#endif

	#if PROGRAM == VFX_PROGRAM_PS
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xy, 0, 1 );
		return o;
	}
}

PS
{
	#include "postprocess/common.hlsl"
	#include "water/particles.hlsl"

	Texture2D ColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;
	float Strength < Attribute( "Strength" ); Default( 1 ); >;
	float DistortionTime < Attribute( "DistortionTime" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = CalculateViewportUv( i.vPositionSs.xy );
		// Sediment can remain enabled when distortion is disabled.
		if ( Strength <= 0 )
		{
			float4 scene = ColorBuffer.SampleLevel( g_sBilinearClamp, uv, 0 );
			return float4( UnderwaterSediment( scene.rgb, uv ), scene.a );
		}

		float aspect = g_vViewportSize.x / max( g_vViewportSize.y, 1 );
		float2 p = (uv - 0.5) * float2( aspect, 1 );
		float time = DistortionTime;

		// Broad crossing waves drift at different speeds so the image does not simply sway.
		float a = dot( p, float2( 9, 5 ) ) + time * 0.8;
		float b = dot( p, float2( -6, 11 ) ) - time * 0.63;
		float c = dot( p, float2( 15, -7 ) ) + time * 1.1;
		float2 flow = float2( sin( a ) + 0.5 * sin( b ), cos( b ) + 0.5 * cos( c ) ) / 1.5;

		// Keep the centre calmer and fade movement at the border to avoid stretched edge pixels.
		float peripheral = saturate( dot( uv - 0.5, uv - 0.5 ) * 4 );
		float border = min( min( uv.x, 1 - uv.x ), min( uv.y, 1 - uv.y ) );
		float envelope = smoothstep( 0, 0.06, border ) * (0.4 + 0.6 * peripheral);
		float2 offset = flow * float2( 1 / aspect, 1 ) * (0.012 * Strength * envelope);
		float2 warped = uv + offset;

		// Less than a pixel of colour separation at ordinary resolutions and default strength.
		float2 dispersion = p * float2( 1 / aspect, 1 ) * (0.0012 * Strength * peripheral * envelope);
		uint width, height;
		ColorBuffer.GetDimensions( width, height );
		float2 margin = 0.5 / float2( width, height );
		float4 green = ColorBuffer.SampleLevel( g_sBilinearClamp, clamp( warped, margin, 1 - margin ), 0 );
		float red = ColorBuffer.SampleLevel( g_sBilinearClamp, clamp( warped + dispersion, margin, 1 - margin ), 0 ).r;
		float blue = ColorBuffer.SampleLevel( g_sBilinearClamp, clamp( warped - dispersion, margin, 1 - margin ), 0 ).b;
		float3 color = UnderwaterSediment( float3( red, green.g, blue ), clamp( warped, margin, 1 - margin ) );
		return float4( color, green.a );
	}
}
