// tools_view: the pixel stage of the editor's geometric debug views on Borealis.Gpu (ToolsViews, ToolsPass). A draw in a
// tools pass keeps its own vertex stage, whatever made it (a .shader, a Slang surface, a pass module, a GPU-driven bucket),
// and draws with this pixel stage instead of its material's: it reads no varying and binds nothing, so it fits every
// vertex stage and pipeline layout. What it writes is picked by specialization constants the pipeline sets:
//   kToolsPass 1, wireframe: kToolsColor as the line colour (polygon mode lines, depth tested)
//   kToolsPass 2, overdraw: a constant added per fragment (additive, no depth test)
//   kToolsPass 3, quad overdraw: added per fragment, redder the fewer of its 2x2 quad's pixels the triangle covers
//   kToolsPass 4, shader IDs: kToolsColor, shaded by the triangle's slope on screen
// A standalone module: Borealis.Gpu draws it from here alone. See docs/managed/borealis.md (Debug views).
#include "sbox/pass.slangh"

Mode( Default );

[vk::constant_id( 0 )] const uint kToolsPass = 0;
[vk::constant_id( 1 )] const uint kToolsColor = 0xffffffu;

float3 ToolsColor()
{
	return float3( ( kToolsColor >> 16 ) & 0xff, ( kToolsColor >> 8 ) & 0xff, kToolsColor & 0xff ) / 255.0;
}

#if ( PROGRAM == VFX_PROGRAM_VS )
// Never drawn with: a draw's own vertex stage feeds the pixel stage. A fullscreen triangle, so the module has both stages
[shader( "vertex" )] float4 MainVs( uint nVertex : SV_VertexID ) : SV_Position
{
	float2 uv = float2( ( nVertex << 1 ) & 2, nVertex & 2 );
	return float4( uv * 2.0 - 1.0, 0.0, 1.0 );
}
#endif

#if ( PROGRAM == VFX_PROGRAM_PS )
[shader( "pixel" )] float4 MainPs( float4 vPositionSs : SV_Position, uint nCoverage : SV_Coverage ) : SV_Target0
{
	if ( kToolsPass == 2 )
		return float4( 0.12, 0.05, 0.015, 1.0 );

	if ( kToolsPass == 3 )
	{
		// Helper lanes cover no sample: the quad's live pixels are those with coverage
		uint live = nCoverage != 0 ? 1u : 0u;
		live += QuadReadAcrossX( live ) + QuadReadAcrossY( live ) + QuadReadAcrossDiagonal( live );
		float waste = saturate( ( 4.0 - float( live ) ) / 3.0 );
		return float4( lerp( float3( 0.02, 0.08, 0.03 ), float3( 0.2, 0.03, 0.01 ), waste ), 1.0 );
	}

	if ( kToolsPass == 4 )
	{
		// The triangle's facing from its depth's slope on screen, so neighbouring faces of one shader tell apart
		float slope = length( float2( ddx( vPositionSs.z ), ddy( vPositionSs.z ) ) ) / max( vPositionSs.z, 1e-6 );
		float shade = lerp( 1.0, 0.55, saturate( slope * 400.0 ) );
		return float4( ToolsColor() * shade, 1.0 );
	}

	return float4( ToolsColor(), 1.0 );
}
#endif
