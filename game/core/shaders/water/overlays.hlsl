#ifndef WATER_OVERLAYS_HLSL
#define WATER_OVERLAYS_HLSL

// Matches the four aligned vectors in WaterOverlay.Data.
struct WaterOverlayData
{
	float4 U;
	float4 V;
	float4 Tint;
	float4 Settings; // Texture index, roughness, normal distortion in inches, transmission.
};
StructuredBuffer<WaterOverlayData> WaterOverlays < Attribute( "WaterOverlays" ); >;
int WaterOverlayCount < Attribute( "WaterOverlayCount" ); Default( 0 ); >;
SamplerState OverlayFiltering < Filter( ANISOTROPIC ); MaxAniso( 8 ); AddressU( CLAMP ); AddressV( CLAMP ); >;

// These projections exist only in this water draw, so they cannot mark other geometry.
// Accumulate premultiplied colour and coverage in authored layer order.
float4 SampleWaterOverlays( float2 point, float3 normal, inout float roughness )
{
	float4 result = 0;
	float2 normalOffset = float2( dot( normal, AxisX ), dot( normal, AxisY ) );
	for ( int index = 0; index < WaterOverlayCount; index++ )
	{
		WaterOverlayData overlay = WaterOverlays[index];
		float2 uv = float2( dot( overlay.U.xy, point ) + overlay.U.z, dot( overlay.V.xy, point ) + overlay.V.z );
		float2 offset = normalOffset * overlay.Settings.z;
		float2 warped = uv + float2( dot( overlay.U.xy, offset ), dot( overlay.V.xy, offset ) );
		// Derivatives must precede the divergent footprint rejection.
		float2 dx = ddx( warped );
		float2 dy = ddy( warped );
		if ( any( uv < 0 ) || any( uv > 1 ) || any( warped < 0 ) || any( warped > 1 ) )
			continue;

		float4 texel = Bindless::GetTexture2DSrgb( (int)overlay.Settings.x ).SampleGrad( OverlayFiltering, warped, dx, dy );
		float coverage = saturate( texel.a * overlay.Tint.a );
		float opacity = coverage * (1 - overlay.Settings.w);
		result.rgb = lerp( result.rgb, texel.rgb * overlay.Tint.rgb, opacity );
		result.a = opacity + result.a * (1 - opacity);
		roughness = lerp( roughness, overlay.Settings.y, coverage );
	}
	return result;
}

#endif
