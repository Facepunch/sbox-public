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

	//
	// Variables you can adjust with code
	//

	float3 GridOrigin < Attribute("GridOrigin"); Default3( 0, 0, 0); >;
	int GridAxis < Attribute( "GridAxis" ); Default( AXIS_XY ); >;

	// Size of each grid square
	float2 GridScale < Attribute( "GridScale" ); Default2( 32, 32 ); >;

	// Width of the grid, centered on GridOrigin. 0 is unlimited.
	float GridSize < Attribute( "GridSize" ); Default( 0 ); >;

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

	//
	// Plane helpers - "uv" is the pair of in-plane axes, "height" is the axis along the plane normal
	//
	float2 ToPlane( float3 v )
	{
		if ( GridAxis == AXIS_YZ ) return v.yz;
		if ( GridAxis == AXIS_XZ ) return v.xz;
		return v.xy;
	}

	float PlaneHeight( float3 v )
	{
		if ( GridAxis == AXIS_YZ ) return v.x;
		if ( GridAxis == AXIS_XZ ) return v.y;
		return v.z;
	}

	float3 FromPlane( float2 uv, float height )
	{
		if ( GridAxis == AXIS_YZ ) return float3( height, uv.x, uv.y );
		if ( GridAxis == AXIS_XZ ) return float3( uv.x, height, uv.y );
		return float3( uv, height );
	}
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
	//
	// Bounds of the region where the grid plane crosses the view frustum, in camera-relative plane space.
	// Intersects the plane with the 12 frustum edges - the crossing is a convex polygon whose vertices all
	// lie on those edges, so their bounds are exact. Returns false when the plane misses the frustum.
	//
	bool GetVisiblePlaneBounds( float height, out float2 mins, out float2 maxs )
	{
		float3 corners[8];

		[unroll]
		for ( int c = 0; c < 8; c++ )
		{
			float4 ndc = float4( ( c & 1 ) ? 1.0f : -1.0f, ( c & 2 ) ? 1.0f : -1.0f, ( c & 4 ) ? 1.0f : 0.0f, 1.0f );
			float4 hom = mul( g_matProjectionToWorld, ndc );
			corners[c] = hom.xyz / max( hom.w, 1e-10 ); // camera-relative
		}

		mins = 1e30;
		maxs = -1e30;
		bool hit = false;

		[unroll]
		for ( int i = 0; i < 8; i++ )
		{
			[unroll]
			for ( int bit = 1; bit < 8; bit <<= 1 )
			{
				if ( i & bit ) continue;

				float3 a = corners[i];
				float3 b = corners[i | bit];
				float da = PlaneHeight( a ) - height;
				float db = PlaneHeight( b ) - height;
				if ( da * db > 0.0f ) continue;

				// Edges lying in the plane have da == db == 0; their endpoints are picked up by neighbouring edges
				float t = abs( da - db ) > 1e-6f ? da / ( da - db ) : 0.0f;
				float2 p = ToPlane( lerp( a, b, saturate( t ) ) );

				mins = min( mins, p );
				maxs = max( maxs, p );
				hit = true;
			}
		}

		return hit;
	}

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;

		float2 cameraUV = ToPlane( g_vCameraPositionWs );
		float gridHeight = PlaneHeight( GridOrigin );

		float2 mins, maxs;
		bool visible = GetVisiblePlaneBounds( gridHeight - PlaneHeight( g_vCameraPositionWs ), mins, maxs );

		// Snap outward to whole major cells so the vertices sit on a stable lattice as the camera moves
		float2 snap = GridScale * max( 2.0, round( MajorGridDivisions ) );
		mins = floor( ( cameraUV + mins ) / snap ) * snap;
		maxs = ceil( ( cameraUV + maxs ) / snap ) * snap;

		if ( GridSize > 0.0 )
		{
			float2 originUV = ToPlane( GridOrigin );
			mins = max( mins, originUV - GridSize * 0.5 );
			maxs = min( maxs, originUV + GridSize * 0.5 );
			visible = visible && all( mins < maxs );
		}

		if ( !visible )
		{
			// Nothing of the grid is in this view - collapse every vertex to one clipped point
			o.PixelPosition = float4( 0, 0, -1, 1 );
			o.UV = 0;
			return o;
		}

		// Vertex positions are a tessellated [0,1] quad
		float2 planeUV = lerp( mins, maxs, i.Position.xy );

		o.PixelPosition = Position3WsToPs( FromPlane( planeUV, gridHeight ) );

		// Simple relative depth bias
		float flProjDepth = saturate( o.PixelPosition.z / o.PixelPosition.w );
		float flBiasAmount = flProjDepth * 0.0001f;
		o.PixelPosition.z += flBiasAmount * o.PixelPosition.w;

		// Offset by camera position to keep higher precision
		o.UV.xy = planeUV - cameraUV;

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

	// Smallest on-screen size a grid cell may shrink to before that level fades into the next, coarser one.
	// Line widths are a fraction of a cell, so cells much smaller than this fade to invisible.
	static const float MinCellPixels = 16.0;

	// uv is in grid cells; uvDeriv is cells per pixel. Derivatives are passed in rather than taken from uv
	// so they stay continuous where neighbouring pixels pick different levels.
	float4 PristineGridWithMajor( float2 uv, float2 uvDeriv )
	{
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
		// World units per pixel, from the camera-relative UV so it keeps full precision
		float2 worldDeriv = float2( length( float2( ddx( i.UV.x ), ddy( i.UV.x ) ) ), length( float2( ddx( i.UV.y ), ddy( i.UV.y ) ) ) );

		// Restore camera offset
		float2 uv = i.UV.xy + ToPlane( g_vCameraPositionWs ) - ToPlane( GridOrigin );

		// Pick the level (GridScale * 2^n) whose cells are at least MinCellPixels on screen and blend
		// towards the next one. Every coarser line is also a line of the finer level, so this only
		// fades out in-between lines as they get too dense to read - it never adds off-grid lines.
		float2 cellPixels = GridScale / max( worldDeriv, 1e-8 );
		float level = max( 0.0, log2( MinCellPixels / min( cellPixels.x, cellPixels.y ) ) );
		float levelFloor = floor( level );
		float2 scale = GridScale * exp2( levelFloor );

		float4 fine = PristineGridWithMajor( uv / scale, worldDeriv / scale );
		float4 coarse = PristineGridWithMajor( uv / ( scale * 2.0 ), worldDeriv / ( scale * 2.0 ) );
		float4 col = lerp( fine, coarse, level - levelFloor );

		return float4( SrgbGammaToLinear( col.rgb ), col.a );
	}
}
