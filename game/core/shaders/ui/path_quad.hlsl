#ifndef UI_PATH_QUAD_HLSL
#define UI_PATH_QUAD_HLSL

// Project homogeneous panel coordinates, including directions (w = 0).
float4 PathToClip( float4 position, float4x4 transform )
{
	float4 panel = mul( LayerMat, mul( transform, position ) );
	#if D_WORLDPANEL
		panel.y = -panel.y;
		return Position4WsToPs( mul( g_matWorldPanel, panel ) );
	#else
		float2 clip = 2.0 * ( panel.xy - g_vViewport.xy * panel.w ) / g_vViewport.zw - panel.w;
		return float4( clip.x, -clip.y, panel.w, panel.w * ( 1.0 + EPSILON ) );
	#endif
}

// An immutable path has one coverage quad. Its bounds are projected for the current view here,
// then expanded by the one-pixel support of the edge filter. No CPU camera state is required.
PixelInput PathQuad( uint index, float2 corner, BoxInstanceData inst, float4x4 transform )
{
	PixelInput o = (PixelInput)0;
	o.iInstanceID = index;
	o.vColor = float4( UIDecodeColor( inst.Color.rgb ), saturate( inst.Color.a ) );
	#if D_PANEL_OPACITY
		o.vColor.a = saturate( inst.Color.a * g_flUIPanelOpacity );
	#endif
	o.vPositionPs = float4( 0, 0, -1, 1 );
	float4 x = PathToClip( float4( 1, 0, 0, 0 ), transform );
	float4 y = PathToClip( float4( 0, 1, 0, 0 ), transform );
	float4 origin = PathToClip( float4( 0, 0, 0, 1 ), transform );
	float3 rowX = float3( x.x, y.x, origin.x );
	float3 rowY = float3( x.y, y.y, origin.y );
	float3 rowW = float3( x.w, y.w, origin.w );
	float3 inverseX = cross( rowY, rowW );
	float3 inverseY = cross( rowW, rowX );
	float3 inverseW = cross( rowX, rowY );
	float determinant = dot( rowX, inverseX );
	// A singular homography projects the panel to a line or a point: it has no covered area.
	if ( determinant == 0.0 ) return o;

	float2 low = float2( 1e30, 1e30 ), high = -low;
	bool inFront = false, behind = false;
	[unroll] for ( int i = 0; i < 4; i++ )
	{
		float2 local = inst.Rect.xy + QuadPositions[i] * inst.Rect.zw;
		float4 projected = origin + x * local.x + y * local.y;
		if ( projected.w > 0.0 )
		{
			inFront = true;
			low = min( low, projected.xy / projected.w );
			high = max( high, projected.xy / projected.w );
		}
		else behind = true;
	}
	if ( !inFront ) return o;
	// Crossing the projective horizon can cover the viewport. Reconstruct the actual plane below;
	// ordinary depth clipping and the homogeneous sign test reject its invisible portion.
	if ( behind ) { low = -1.0; high = 1.0; }
	float2 filterSupport = 2.0 / g_vViewport.zw;
	low = max( low - filterSupport, -1.0 );
	high = min( high + filterSupport, 1.0 );
	if ( any( low >= high ) ) return o;
	float2 ndc = lerp( low, high, corner );
	float3 localH = ( inverseX * ndc.x + inverseY * ndc.y + inverseW ) / determinant;
	// Homogeneous panel coordinates and projected depth are affine in screen space on this plane.
	// Keeping localH until the pixel shader also handles a quad that crosses the horizon.
	o.vPathPosition = localH;
	o.vPositionPs = float4( ndc, dot( float3( x.z, y.z, origin.z ), localH ), 1.0 );
	return o;
}

#endif
