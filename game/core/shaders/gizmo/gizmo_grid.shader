//
// An anti-aliased grid shader with major & axis lines.
// Based on https://bgolus.medium.com/the-best-darn-grid-shader-yet-727f9278b9d8#3e73
//

HEADER
{
	DevShader = true;
	CompileTargets = ( IS_SM_50 && ( PC || VULKAN ) );
	Description = "Grid";
}

MODES
{
	Default();
	Forward();
	
	Depth();
}

FEATURES
{
}

COMMON
{
    // Opt out of stupid shit
    #define CUSTOM_MATERIAL_INPUTS

    #include "common/shared.hlsl"
	
	// axis
	#define AXIS_XY 0
	#define AXIS_YZ 1
	#define AXIS_XZ 2

	#define GRID_SIZE 16384

	//
	// Variables you can adjust with code
	//

	float3 GridOrigin < Attribute("GridOrigin"); Default3( 0, 0, 0); >;
	int GridAxis < Attribute( "GridAxis" ); Default( AXIS_XY ); >;

	// Size of each grid square
	float2 GridScale < Attribute( "GridScale" ); Default2( 32, 32 ); >;

	// Number of grid squares per major
	float MajorGridDivisions < Attribute( "MajorGridDivisions" ); Default( 16 ); >;

	float AxisLineWidth < Attribute( "AxisLineWidth" ); Default( 0.03 ); >;
	float MajorLineWidth < Attribute( "MajorLineWidth" ); Default( 0.02 ); >;
	float MinorLineWidth < Attribute( "MinorLineWidth" ); Default( 0.01 ); >;

	float4 MinorLineColor < Attribute( "MinorLineColor" ); Default4( 1, 1, 1, 0.5 ); >;
	float4 MajorLineColor < Attribute( "MajorLineColor" ); Default4( 1, 1, 1, 0.8 ); >;

	float4 XAxisColor < Attribute( "XAxisColor" ); Default4( 1.0, 0, 0, 1 ); >;
	float4 YAxisColor < Attribute( "YAxisColor" ); Default4( 0, 1.0, 0, 1 ); >;
	float4 ZAxisColor < Attribute( "ZAxisColor" ); Default4( 0, 0, 1.0, 1 ); >;
	float4 CenterColor < Attribute( "CenterColor" ); Default4( 1, 1, 1, 1 ); >;
}

struct VertexInput
{
	float3 Position	: POSITION < Semantic( PosXyz ); >;

	uint nInstanceTransformID : TEXCOORD13 < Semantic( InstanceTransformUv ); >;
};

struct PixelInput
{
	float2 UV : TEXCOORD0;

    #if ( PROGRAM == VFX_PROGRAM_VS )
        float4 PixelPosition : SV_Position;
    #endif

    #if ( PROGRAM == VFX_PROGRAM_PS )
        float4 ScreenPosition : SV_Position;
    #endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;

		// TODO: Get worldpos from uniform
		float3 worldPos = i.Position;

		if ( GridAxis == AXIS_XY )
		{
			worldPos.xy -= float2( 0.5, 0.5 );
			worldPos.xyz *= GRID_SIZE;

			o.UV.xy = worldPos.xy;
		}
		if ( GridAxis == AXIS_YZ )
		{
			worldPos.yz -= float2( 0.5, 0.5 );
			worldPos.xyz *= GRID_SIZE;

			o.UV.xy = worldPos.yz;
		}
		if ( GridAxis == AXIS_XZ )
		{
			worldPos.xz -= float2( 0.5, 0.5 );
			worldPos.xyz *= GRID_SIZE;

			o.UV.xy = worldPos.xz;
		}

		o.PixelPosition = Position3WsToPs( worldPos.xyz );

		// Simple relative depth bias 
		float flProjDepth = saturate( o.PixelPosition.z / o.PixelPosition.w );
		float flBiasAmount = flProjDepth * 0.0001f;
		o.PixelPosition.z += flBiasAmount * o.PixelPosition.w;

		// Offset by camera position to keep higher precision
		o.UV.xy -= g_vCameraPositionWs.xy;

		return o;
	}
}

PS
{
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	float4 PristineGridWithMajor( float2 uv )
	{
		float4 uvDDXY = float4( ddx( uv.xy ), ddy( uv.xy ) );
		float2 uvDeriv = float2( length( uvDDXY.xz ), length( uvDDXY.yw ) );

		//
		// axis lines
		//
		float axisLineWidth = max( MajorLineWidth, AxisLineWidth );
		float2 axisDrawWidth = max( axisLineWidth, uvDeriv );
		float2 axisLineAA = uvDeriv * 1.5;
		float2 axisLines2 = smoothstep( axisDrawWidth + axisLineAA, axisDrawWidth - axisLineAA, abs( uv.xy * 2.0 ) );
		axisLines2 *= saturate( axisLineWidth / axisDrawWidth );

		//
		// major grid lines
		//
		float div = max( 2.0, round( MajorGridDivisions ) );
		float2 majorUVDeriv = uvDeriv / div;
		float majorLineWidth = MajorLineWidth / div;
		float2 majorDrawWidth = clamp( majorLineWidth, majorUVDeriv, 0.5 );
		float2 majorLineAA = majorUVDeriv * 1.5;
		float2 majorGridUV = 1.0 - abs( frac( uv.xy / div ) * 2.0 - 1.0 );
		float2 majorAxisOffset = ( 1.0 - saturate( abs( uv.xy / div * 2.0 ) ) ) * 2.0;
		majorGridUV += majorAxisOffset; // adjust UVs so center axis line is skipped
		float2 majorGrid2 = smoothstep( majorDrawWidth + majorLineAA, majorDrawWidth - majorLineAA, majorGridUV );
		majorGrid2 *= saturate( majorLineWidth / majorDrawWidth );
		majorGrid2 = saturate( majorGrid2 - axisLines2 ); // hack
		majorGrid2 = lerp( majorGrid2, majorLineWidth, saturate( majorUVDeriv * 2.0 - 1.0 ) );

		//
		// minor grid lines
		//
		float minorLineWidth = min( MinorLineWidth, MajorLineWidth );
		bool minorInvertLine = minorLineWidth > 0.5;
		float minorTargetWidth = minorInvertLine ? 1.0 - minorLineWidth : minorLineWidth;
		float2 minorDrawWidth = clamp( minorTargetWidth, uvDeriv, 0.5 );
		float2 minorLineAA = uvDeriv * 1.5;
		float2 minorGridUV = abs( frac( uv.xy ) * 2.0 - 1.0 );
		minorGridUV = minorInvertLine ? minorGridUV : 1.0 - minorGridUV;
		float2 minorMajorOffset = ( 1.0 - saturate( ( 1.0 - abs( frac( uv.xy / div ) * 2.0 - 1.0 ) ) * div ) ) * 2.0;
		minorGridUV += minorMajorOffset; // adjust UVs so major division lines are skipped
		float2 minorGrid2 = smoothstep( minorDrawWidth + minorLineAA, minorDrawWidth - minorLineAA, minorGridUV );
		minorGrid2 *= saturate( minorTargetWidth / minorDrawWidth );
		minorGrid2 = saturate( minorGrid2 - axisLines2 ); // hack
		minorGrid2 = lerp( minorGrid2, minorTargetWidth, saturate( uvDeriv * 2.0 - 1.0 ) );
		minorGrid2 = minorInvertLine ? 1.0 - minorGrid2 : minorGrid2;
		minorGrid2 = abs( uv.xy ) > 0.5 ? minorGrid2 : 0.0;

		float minorGrid = lerp( minorGrid2.x, 1.0, minorGrid2.y );
		float majorGrid = lerp( majorGrid2.x, 1.0, majorGrid2.y );

		float4 aAxisColor = YAxisColor;
		float4 bAxisColor = XAxisColor;
		if ( GridAxis == AXIS_YZ )
		{
			aAxisColor = ZAxisColor;
			bAxisColor = YAxisColor;
		}
		if ( GridAxis == AXIS_XZ )
		{
			aAxisColor = ZAxisColor;
			bAxisColor = XAxisColor;
		}


		aAxisColor = lerp( aAxisColor, CenterColor, axisLines2.y );

		float4 axisLines = lerp( bAxisColor * axisLines2.y, aAxisColor, axisLines2.x );

		float4 col = MinorLineColor;
		col.a *= minorGrid;
		col = lerp( col, MajorLineColor, majorGrid * MajorLineColor.a );
		col = col * ( 1.0 - axisLines.a ) + axisLines;

		return col;
	}	

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 invGridScale = ( 1 / GridScale );

		// Restore camera offset
		i.UV.xy += g_vCameraPositionWs.xy - GridOrigin.xy;

		// Scale by grid size
		i.UV.xy *= invGridScale;
		
		float4 col = PristineGridWithMajor( i.UV.xy );
		return float4( SrgbGammaToLinear( col.rgb ), col.a );
	}
}
