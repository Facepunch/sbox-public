HEADER
{
	Description = "Procedural blueprint preview with a transparent drafting grid and bright silhouette rims";
}

MODES
{
	Forward();
}

FEATURES
{
	#include "common/features.hlsl"
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"

	float3 vGridPosition : TEXCOORD8;
	float3 vGridNormal : TEXCOORD9;
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		// Keep the standard skinning and instancing path for ragdolls and props.
		PixelInput o = ProcessVertex( i );

		// Push vertices forward a little to avoid preview model z-fighting in some cases 
		o.vPositionWs += o.vNormalWs * 0.02;
		o.vPositionPs = Position3WsToPs( o.vPositionWs );

		// Use one instance reference frame per draw, including skinned meshes.
		float3x4 objectToWorld = GetTransformMatrix( i.nInstanceTransformID );
		float3 origin = float3( objectToWorld[0][3], objectToWorld[1][3], objectToWorld[2][3] );
		float3x3 worldToGrid = transpose( (float3x3)objectToWorld );

		// Strip scale from the axes, but keep it in the vertex positions. This
		// anchors the grid to the model while preserving spacing in world units.
		worldToGrid[0] /= max( length( worldToGrid[0] ), 0.0001 );
		worldToGrid[1] /= max( length( worldToGrid[1] ), 0.0001 );
		worldToGrid[2] /= max( length( worldToGrid[2] ), 0.0001 );

		o.vGridPosition = mul( worldToGrid, o.vPositionWs - origin );
		o.vGridNormal = mul( worldToGrid, o.vNormalWs );

		return FinalizeVertex( o );
	}
}

PS
{
	BoolAttribute( translucent, true );
	BoolAttribute( DoNotCastShadows, true );

	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, BACK );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	float3 g_vBlueprintColor < UiType( Color ); Default3( 0.06, 0.3, 0.75 ); UiGroup( "Blueprint,0/0" ); >;
	float3 g_vOutlineColor < UiType( Color ); Default3( 0.3, 0.82, 1.0 ); UiGroup( "Blueprint,0/1" ); >;
	float g_flBrightness < Default( 1.5 ); Range( 0.0, 4.0 ); UiGroup( "Blueprint,0/2" ); >;
	float g_flBodyOpacity < Default( 0.045 ); Range( 0.0, 1.0 ); UiGroup( "Blueprint,0/3" ); >;
	float g_flRimOpacity < Default( 0.85 ); Range( 0.0, 1.0 ); UiGroup( "Rim,1/0" ); >;
	float g_flRimPower < Default( 4.0 ); Range( 1.0, 12.0 ); UiGroup( "Rim,1/1" ); >;
	float g_flGridSpacing < Default( 12.0 ); Range( 1.0, 128.0 ); UiGroup( "Grid,2/0" ); >;
	float g_flGridOpacity < Default( 0.18 ); Range( 0.0, 1.0 ); UiGroup( "Grid,2/1" ); >;
	float g_flLineWidth < Default( 0.7 ); Range( 0.5, 3.0 ); UiGroup( "Grid,2/2" ); >;
	float3 g_vGridScrollSpeed < Default3( 1.0, 1.0, 2.0 ); UiGroup( "Grid,2/3" ); >;

	// Derivatives keep the drafting lines about one pixel wide at any distance.
	// Fade unresolved cells instead of letting a distant grid shimmer.
	float GridLines( float2 position, float spacing )
	{
		float2 grid = position / spacing;
		float2 footprint = max( fwidth( grid ), 0.0001 );
		float2 distance = abs( frac( grid + 0.5 ) - 0.5 ) / footprint;
		float2 lines = 1.0 - smoothstep( g_flLineWidth - 0.5, g_flLineWidth + 0.5, distance );
		lines *= 1.0 - smoothstep( 0.25, 0.5, footprint );

		return max( lines.x, lines.y );
	}

	float DraftingGrid( float3 position, float3 weights, float spacing )
	{
		return dot( weights, float3(
			GridLines( position.yz, spacing ),
			GridLines( position.zx, spacing ),
			GridLines( position.xy, spacing ) ) );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 position = i.vPositionWithOffsetWs + g_vHighPrecisionLightingOffsetWs.xyz;
		float3 normal = normalize( i.vNormalWs );
		float3 view = CalculatePositionToCameraDirWs( position );

		// Grazing surfaces form the bright rim; broad faces stay nearly transparent.
		float rim = pow( 1.0 - saturate( abs( dot( normal, view ) ) ), g_flRimPower );

		// Projection follows the object's axes instead of sliding as it moves.
		float3 weights = pow( abs( normalize( i.vGridNormal ) ), 4.0 );
		weights /= max( dot( weights, 1.0 ), 0.0001 );

		// Scroll in world units per second along the object's axes. Wrap at a
		// major cell to keep coordinates small; zero speed freezes the pattern.
		float spacing = max( g_flGridSpacing, 0.001 );
		float3 scroll = frac( g_vGridScrollSpeed * ( g_flTime / ( spacing * 4.0 ) ) ) * ( spacing * 4.0 );
		float3 gridPosition = i.vGridPosition - scroll;
		float minorGrid = DraftingGrid( gridPosition, weights, spacing );
		float majorGrid = DraftingGrid( gridPosition, weights, spacing * 4.0 );
		float grid = max( minorGrid * 0.45, majorGrid );

		float opacity = saturate( g_flBodyOpacity + rim * g_flRimOpacity + grid * g_flGridOpacity );
		float3 color = lerp( g_vBlueprintColor, g_vOutlineColor, max( rim, grid * 0.6 ) );

		float3 tint = i.vVertexColor.rgb;
		float tinted = step( min( tint.r, min( tint.g, tint.b ) ), 0.999 );
		color = lerp( color, dot( color, float3( 0.2126, 0.7152, 0.0722 ) ) * tint * 2.0, tinted );

		return float4( color * g_flBrightness, opacity );
	}
}
