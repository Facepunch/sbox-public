HEADER
{
	DevShader = true;
	Version = 1;
}

MODES
{
	Default();
	Forward();
}

FEATURES
{
	#include "ui/features.hlsl"
}

COMMON
{
	#include "system.fxc"
	#include "common.fxc"
	#include "vr_common.fxc"
	#include "common/Bindless.hlsl"

	DynamicCombo( D_WORLDPANEL, 0..1, Sys( ALL ) );
	DynamicCombo( D_NO_ZTEST, 0..1, Sys( ALL ) );
	DynamicCombo( D_PANEL_OPACITY, 0..1, Sys( ALL ) );
	float g_flUIPanelOpacity < Attribute( "UIPanelOpacity" ); Default( 1 ); >;

	#define BoxInstanceData TextInstanceData
	#include "ui/text.hlsl"
	#undef BoxInstanceData
	StructuredBuffer<TextInstanceData> PainterTextInstances < Attribute( "PainterTextInstances" ); >;

	struct BoxInstanceData
	{
		float4 Rect;
		float4 Color;
		float4 BorderRadius;	// horizontal radii ( top-left, top-right, bottom-left, bottom-right )
		float4 BorderRadiusV;	// vertical radii, same order
		float4 BorderSize;		// left, top, right, bottom
		float4 BorderColorL;
		float4 BorderColorT;
		float4 BorderColorR;
		float4 BorderColorB;
		int TextureIndex;
		int SamplerIndex;
		float BackgroundAngle;
		float4 BackgroundRect;
		float4 BackgroundTint;
		int BorderImageIndex;
		int BorderImageSamplerIndex;
		float4 BorderImageSlice;
		float4 BorderImageTint;
		uint Flags;
		int ScissorIndex;
		int TransformIndex;
		int InverseScissorIndex;
		int TextMaskIndex;
		int TextMaskSamplerIndex;
		float4 BackgroundClipRect;	// box clip: the inset. text clip: where the mask sits.
		int ShapeIndex;				// into BorderShapeBuffer, or -1 for a plain rounded rect

		// Packed settings, matching GPUBoxInstance. Bits 14..31 are reserved.
		int GetMode() { return int( ( Flags >> 0 ) & 0x3u ); }
		int GetBorderImageMode() { return int( ( Flags >> 2 ) & 0x3u ); }
		int GetBorderStyle() { return int( ( Flags >> 4 ) & 0xFu ); }
		int GetBorderImageFill() { return int( ( Flags >> 8 ) & 0x1u ); }
		int GetBackgroundRepeat() { return int( ( Flags >> 9 ) & 0x7u ); }
		int GetBackgroundClip() { return int( ( Flags >> 12 ) & 0x3u ); }
	};

	// Must match ShapeKind and PathPrimitiveKind in UICssBoxBatched.cs.
	#define UI_SHAPE_NONE 0
	#define UI_SHAPE_POLYGON 1
	#define UI_SHAPE_CIRCLE 2
	#define UI_SHAPE_POLYGON_PATH 3
	#define UI_SHAPE_STROKE_PATH 4
	#define UI_SHAPE_CAPSULE 5
	#define UI_SHAPE_CRESCENT 6
	#define UI_SHAPE_HEART 7
	// Kinds from here up are analytic SDF shapes with no path nodes.
	#define UI_SHAPE_FIRST_ANALYTIC UI_SHAPE_CAPSULE

	#define UI_PATH_SEGMENT 0
	#define UI_PATH_DISC 1
	#define UI_PATH_JOIN 2
	#define UI_PATH_ARC 3
	#define UI_PATH_ROUND_JOIN 4

	// Must match Stroke.LineCap and UICssBoxBatched.PathCap.
	#define UI_CAP_BUTT 0
	#define UI_CAP_SQUARE 1
	#define UI_CAP_ROUND 2
	#define UI_CAP_TRIANGLE 3
	#define UI_CAP_ARROW 4
	#define UI_CAP_RING -1
	#define UI_CAP_SQUARE_START 1
	#define UI_CAP_SQUARE_END 2

	// Must match GPUBorderShape in GPUBoxInstance.cs
	struct BorderShapeData
	{
		float4 Polygon01;
		float4 Polygon23;
		float4 Polygon45;
		float4 Polygon67;
		int PolygonCount;
		float4 Circle;
		int PathOffset;
		int PathCount;
		int PathNodeOffset;
		int PathNodeCount;
		int Kind;
	};

	// Must match GPUPathPrimitive. All fields use four-byte structured-buffer alignment.
	struct PathPrimitiveData
	{
		float4 A;
		float4 B;
		float4 C;
		int Kind;
		int Count;
	};

	struct PathNodeData
	{
		float4 Bounds;
		int Next;
		int Primitive;
	};

	struct TransformData
	{
		float4x4 Mat;
	};

	// One rounded rect of a clip stack. Rect is left, top, right, bottom in the clipping panel's layout space,
	// TransformMat takes screen space there.
	// Must match GPUGradientInstance in GPUBoxInstance.cs. Stop colors are straight
	// alpha in sRGB space; Angle is radians - 0 points down the panel for a linear
	// gradient, straight up for a conic one.

	StructuredBuffer<BoxInstanceData> BoxInstances < Attribute( "BoxInstances" ); >;
	StructuredBuffer<TransformData> TransformBuffer < Attribute( "TransformBuffer" ); >;
	StructuredBuffer<GradientData> GradientBuffer < Attribute( "GradientBuffer" ); >;
	StructuredBuffer<BorderShapeData> BorderShapeBuffer < Attribute( "BorderShapeBuffer" ); >;
	StructuredBuffer<PathPrimitiveData> PathBuffer < Attribute( "PathBuffer" ); >;
	StructuredBuffer<PathNodeData> PathNodeBuffer < Attribute( "PathNodeBuffer" ); >;

}

struct PixelInput
{
	float4 vColor : COLOR0;
	float4 vTexCoord : TEXCOORD0;
	float4 vPositionPanelSpace : TEXCOORD2;
	float3 vPathPosition : TEXCOORD4;
	nointerpolation uint iInstanceID : TEXCOORD3;
	float4 vPositionPs : SV_Position;
};

struct VS_INPUT
{
	float3 pos : POSITION < Semantic( None ); >;
};

VS
{
	#include "math_general.fxc"
	#include "instancing.fxc"
	#include "ui/gamma.hlsl"

	#define EPSILON 0.000001

	float4 g_vViewport < Source( Viewport ); >;
	float4x4 g_matTransform < Attribute( "TransformMat" ); >;
	float4x4 LayerMat < Attribute( "LayerMat" ); >;
	float4x4 g_matWorldPanel < Attribute( "WorldMat" ); >;
	int InstanceOffset < Attribute( "InstanceOffset" ); Default( 0 ); >;

	BoolAttribute( ui, true );
	BoolAttribute( ScreenSpaceVertices, true );

	static const float2 QuadPositions[4] =
	{
		float2( 0, 0 ),
		float2( 1, 0 ),
		float2( 1, 1 ),
		float2( 0, 1 ),
	};

	#include "ui/path_quad.hlsl"

	// Quads are the box grown by a pixel so the outer half of the edge antialiasing has somewhere to land
	#define BOX_BLOAT 1.0

	PixelInput MainVs( uint nVertexID : SV_VertexID, uint nInstanceID : SV_InstanceID )
	{
		PixelInput o;

		uint instanceIndex = nInstanceID + InstanceOffset;
		float2 corner = QuadPositions[nVertexID];
		BoxInstanceData inst = BoxInstances[instanceIndex];
		float4x4 instTransform = TransformBuffer[inst.TransformIndex].Mat;
		if ( inst.ShapeIndex >= 0 )
		{
			int kind = BorderShapeBuffer[inst.ShapeIndex].Kind;
			if ( kind == UI_SHAPE_POLYGON_PATH || kind == UI_SHAPE_STROKE_PATH )
				return PathQuad( instanceIndex, corner, inst, instTransform );
		}
		o.vPathPosition = float3( 0, 0, 1 );

		float2 vLocal = inst.Rect.xy - BOX_BLOAT + corner * ( inst.Rect.zw + BOX_BLOAT * 2.0 );
		float2 vPositionSs = vLocal;

		float4 vViewport = g_vViewport;
		float4 vMatrix = mul( LayerMat, mul( instTransform, float4( vPositionSs, 0, 1 ) ) );

		#if !( D_WORLDPANEL )
		{
			float safeW = abs( vMatrix.w ) > EPSILON ? vMatrix.w : ( vMatrix.w < 0.0 ? -EPSILON : EPSILON );
			vPositionSs = vMatrix.xy / safeW;

			// Keep homogeneous W so local coordinates interpolate identically on adjacent perspective tiles.
			// Negative W stays negative: the rasterizer clips geometry behind the projective plane.
			o.vPositionPs.xy = 2.0 * ( vMatrix.xy - vViewport.xy * vMatrix.w ) / vViewport.zw - vMatrix.w;
			o.vPositionPs.y *= -1.0;
			o.vPositionPs.z = vMatrix.w;
			o.vPositionPs.w = vMatrix.w * ( 1.0 + EPSILON );
		}
		#else
		{
			float3 vPositionLocal = vMatrix.xyz / vMatrix.w;
			vPositionSs = vPositionLocal.xy;

			o.vPositionPs = float4( vPositionLocal, 1 );
			o.vPositionPs.y *= -1.0;

			float3 vPositionWs = mul( g_matWorldPanel, float4( o.vPositionPs.xyz, 1.0 ) ).xyz;
			o.vPositionPs = Position3WsToPs( vPositionWs.xyz );
		}
		#endif

		o.vPositionPanelSpace = mul( instTransform, float4( vLocal, 0, 1 ) );

		// 0..1 across the box itself, so a little outside that in the bloat
		o.vTexCoord.xy = ( vLocal - inst.Rect.xy ) / max( inst.Rect.zw, 0.0001 );
		o.vTexCoord.zw = vPositionSs / vViewport.zw;

		// rgb can be HDR, alpha over 1 breaks alpha blending
		o.vColor.rgb = UIDecodeColor( inst.Color.rgb );
		#if D_PANEL_OPACITY
			o.vColor.a = saturate( inst.Color.a * g_flUIPanelOpacity );
		#else
			o.vColor.a = saturate( inst.Color.a );
		#endif

		o.iInstanceID = instanceIndex;

		return o;
	}
}

PS
{
	#include "common/blendmode.hlsl"
	#include "ui/gamma.hlsl"
	#include "ui/rounded_rect.hlsl"
	#include "ui/batched_scissor.hlsl"
	#include "ui/shape_path.hlsl"

	// Scissor is now per-instance via ScissorIndex into ScissorBuffer (defined in COMMON)

	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );

	#if ( D_NO_ZTEST )
		RenderState( DepthEnable, false );
	#else
		RenderState( DepthEnable, true );
	#endif

	// Distance to an inset box edge - the box pulled in by inset, each corner's radii less the inset on
	// its two sides. A corner that loses either radius goes square, like CSS. Inset is left, top, right, bottom.
	// The padding box is this with the border widths; the content box adds the padding on top.
	float InsetBoxSdf( float2 p, float2 boxSize, float4 radiiH, float4 radiiV, float4 inset )
	{
		float2 innerSize = max( boxSize - float2( inset.x + inset.z, inset.y + inset.w ), 0.0 );
		if ( any( innerSize <= 0.0 ) ) return 1e20;
		float2 innerCentre = float2( inset.x - inset.z, inset.y - inset.w ) * 0.5;

		float4 innerH = radiiH - inset.xzxz;
		float4 innerV = radiiV - inset.yyww;
		float4 keep = step( 0.0001, innerH ) * step( 0.0001, innerV );

		return RoundedRectSdf( p - innerCentre, innerSize * 0.5, innerH * keep, innerV * keep );
	}

	// How much of this pixel the background is allowed to paint. border-box paints everywhere, the padding
	// and content boxes stop at their edge, and text keeps only what the panel's glyphs cover.
	float BackgroundClipCoverage( BoxInstanceData inst, float2 pos, float2 boxSize, float2 texCoord )
	{
		if ( inst.GetBackgroundClip() == 0 )
			return 1.0;

		if ( inst.GetBackgroundClip() != 3 )
			return SdfCoverage( InsetBoxSdf( pos, boxSize, inst.BorderRadius, inst.BorderRadiusV, inst.BackgroundClipRect ) );

		// No text under the panel means nothing to paint into
		if ( inst.TextMaskIndex == 0 )
			return 0.0;

		float2 uv = ( texCoord * boxSize - inst.BackgroundClipRect.xy ) / inst.BackgroundClipRect.zw;
		if ( any( uv < 0.0 ) || any( uv > 1.0 ) )
			return 0.0;

		Texture2D maskTex = Bindless::GetTexture2D( inst.TextMaskIndex );
		return saturate( maskTex.Sample( Bindless::GetSampler( NonUniform( inst.TextMaskSamplerIndex ) ), uv ).a );
	}

	// Which side's colour a border pixel takes. CSS splits each corner along the line from the box corner
	// to the padding box corner, which is the same as picking the side the pixel is proportionally least
	// deep into. Sides with no border never win. The join between the two nearest sides is antialiased
	// over a pixel, like the web strokes it.
	float4 BorderSideColor( float2 pos, float2 boxSize, float4 borderWidth, float4 cL, float4 cT, float4 cR, float4 cB )
	{
		float4 has = step( 0.0001, borderWidth );
		float4 depth = float4( pos.x, pos.y, boxSize.x - pos.x, boxSize.y - pos.y ) / max( borderWidth, 0.0001 );
		depth = depth * has + ( 1.0 - has ) * 1e9;

		// Nearest side and runner up
		float4 c1 = cL, c2 = cT;
		float d1 = depth.x, d2 = depth.y;
		if ( d2 < d1 ) { c1 = cT; c2 = cL; d1 = depth.y; d2 = depth.x; }
		if ( depth.z < d1 ) { c2 = c1; d2 = d1; c1 = cR; d1 = depth.z; } else if ( depth.z < d2 ) { c2 = cR; d2 = depth.z; }
		if ( depth.w < d1 ) { c2 = c1; d2 = d1; c1 = cB; d1 = depth.w; } else if ( depth.w < d2 ) { c2 = cB; d2 = depth.w; }

		// How far this pixel is from the join line, in pixels
		float diff = d2 - d1;
		float t = saturate( 0.5 - diff / max( fwidth( diff ), 0.0001 ) );
		return lerp( c1, c2, t );
	}

	#include "ui/border_style.hlsl"

	float4 AddImageBorder( float2 texCoord, float2 boxSize, float4 borderWidth, int borderImageIndex, NonUniform borderImageSamplerIndex, int borderImageMode, int borderImageFill, float4 borderImageSlice )
	{
		float4 BorderImageWidth = borderWidth;
		Texture2D borderTex = Bindless::GetTexture2D( borderImageIndex );
		float2 vBorderImageSize = TextureDimensions2D( borderTex, 0 );
		float4 vBorderPixelSize = borderImageSlice;
		float4 vBorderPixelRatio = vBorderPixelSize / float4( vBorderImageSize.x, vBorderImageSize.y, vBorderImageSize.x, vBorderImageSize.y );
		float2 vBoxTexCoord = texCoord * boxSize;
		float2 uv = 0.0;

		if ( !borderImageFill &&
			vBoxTexCoord.x > BorderImageWidth.x && vBoxTexCoord.x < boxSize.x - BorderImageWidth.z &&
			vBoxTexCoord.y > BorderImageWidth.y && vBoxTexCoord.y < boxSize.y - BorderImageWidth.w )
			return 0;

		if ( vBorderPixelSize.x < vBorderImageSize.x * 0.5 )
		{
			if ( borderImageMode == 1 )
			{
				float2 vMiddleSize = 1.0 - ( vBorderPixelRatio.xy + vBorderPixelRatio.zw );
				float2 vRepeatAmount = floor( ( boxSize * vMiddleSize ) / BorderImageWidth.xy );
				uv.x = ( vBoxTexCoord.x - BorderImageWidth.x ) / ( boxSize.x - ( BorderImageWidth.x + BorderImageWidth.z ) ) * vRepeatAmount.x;
				uv.x = fmod( uv.x, vMiddleSize.x ) + vBorderPixelRatio.x;
				uv.y = ( vBoxTexCoord.y - BorderImageWidth.y ) / ( boxSize.y - ( BorderImageWidth.y + BorderImageWidth.w ) ) * vRepeatAmount.y;
				uv.y = fmod( uv.y, vMiddleSize.y ) + vBorderPixelRatio.y;
			}
			else
			{
				uv.x = ( vBoxTexCoord.x - BorderImageWidth.x ) / ( boxSize.x - ( BorderImageWidth.x + BorderImageWidth.z ) );
				uv.x = uv.x * ( 1.0 - ( vBorderPixelRatio.x + vBorderPixelRatio.z ) ) + vBorderPixelRatio.x;
				uv.y = ( vBoxTexCoord.y - BorderImageWidth.y ) / ( boxSize.y - ( BorderImageWidth.y + BorderImageWidth.w ) );
				uv.y = uv.y * ( 1.0 - ( vBorderPixelRatio.y + vBorderPixelRatio.w ) ) + vBorderPixelRatio.y;
			}
		}

		if ( vBoxTexCoord.x < BorderImageWidth.x )
			uv.x = ( vBoxTexCoord.x / BorderImageWidth.x ) * vBorderPixelRatio.x;
		else if ( vBoxTexCoord.x > boxSize.x - BorderImageWidth.z )
			uv.x = ( ( vBoxTexCoord.x - ( boxSize.x - BorderImageWidth.z ) ) / BorderImageWidth.z ) * vBorderPixelRatio.z + ( 1.0 - vBorderPixelRatio.z );

		if ( vBoxTexCoord.y < BorderImageWidth.y )
			uv.y = ( vBoxTexCoord.y / BorderImageWidth.y ) * vBorderPixelRatio.y;
		else if ( vBoxTexCoord.y > boxSize.y - BorderImageWidth.w )
			uv.y = ( ( vBoxTexCoord.y - ( boxSize.y - BorderImageWidth.w ) ) / BorderImageWidth.w ) * vBorderPixelRatio.w + ( 1.0 - vBorderPixelRatio.w );

		float4 r = borderTex.Sample( Bindless::GetSampler( borderImageSamplerIndex ), uv );
		r.xyz = UIDecodeColor( r.xyz );
		return r;
	}

	float4 AlphaBlend( float4 src, float4 dest )
	{
		float4 result;
		result.a = src.a + ( 1 - src.a ) * dest.a;
		result.rgb = ( src.a * src.rgb + ( 1 - src.a ) * dest.a * dest.rgb ) / max( result.a, 0.0001 );
		return result;
	}

	float2 RotateTexCoord( float2 vTexCoord, float angle, float2 offset = 0.5 )
	{
		float2x2 m = float2x2( cos( angle ), -sin( angle ), sin( angle ), cos( angle ) );
		return mul( m, vTexCoord - offset ) + offset;
	}

	float2 ShapePoint( float4 p01, float4 p23, float4 p45, float4 p67, int i )
	{
		if ( i < 2 ) return i == 0 ? p01.xy : p01.zw;
		if ( i < 4 ) return i == 2 ? p23.xy : p23.zw;
		if ( i < 6 ) return i == 4 ? p45.xy : p45.zw;
		return i == 6 ? p67.xy : p67.zw;
	}

	float ShapeSdf( float2 p, float4 p01, float4 p23, float4 p45, float4 p67, int count, float4 circle, int kind )
	{
		if ( kind == UI_SHAPE_CIRCLE ) return length( p - circle.xy ) - circle.z;
		if ( kind == UI_SHAPE_CAPSULE )
		{
			float2 delta = p01.xy - circle.xy;
			float len = length( delta );
			float dr = circle.w - circle.z;
			float2 axis = delta / max( len, 0.000001 );
			float2 q = p - circle.xy;
			float x = dot( q, axis ), y = abs( dot( q, float2( -axis.y, axis.x ) ) );
			float t = saturate( (x + dr * y / sqrt( max( len * len - dr * dr, 0.000001 ) )) / max( len, 0.000001 ) );
			return length( q - delta * t ) - lerp( circle.z, circle.w, t );
		}
		if ( kind == UI_SHAPE_CRESCENT )
			return max( length( p - circle.xy ) - circle.z, circle.w - length( p - p01.xy ) );
		if ( kind == UI_SHAPE_HEART )
		{
			// circle: origin.xy, scale, lobe radius. p01: lobe offset.xy, tip. All in unit-heart space.
			float2 q = (p - circle.xy) / circle.z;
			float diamond = (abs( q.x ) + abs( q.y ) - p01.z) * 0.70710678;
			float lobes = length( float2( abs( q.x ) - p01.x, q.y + p01.y ) ) - circle.w;
			return min( diamond, lobes ) * circle.z;
		}
		float2 first = ShapePoint( p01, p23, p45, p67, 0 );
		float d = dot( p - first, p - first ), s = 1.0; int j = count - 1;
		for ( int i = 0; i < 8; i++ )
		{
			if ( i >= count ) break;
			float2 vi = ShapePoint( p01, p23, p45, p67, i ), vj = ShapePoint( p01, p23, p45, p67, j );
			float2 e = vj - vi, w = p - vi;
			float2 b = w - e * clamp( dot( w, e ) / max( dot( e, e ), 0.000001 ), 0.0, 1.0 );
			d = min( d, dot( b, b ) );
			bool3 c = bool3( p.y >= vi.y, p.y < vj.y, e.x * w.y > e.y * w.x );
			if ( all( c ) || all( !c ) ) s *= -1.0; j = i;
		}
		return s * sqrt( d );
	}


	// Modes 1 and 2. BackgroundRect is the shape as (x, y, w, h) relative to the quad, BackgroundAngle the CSS blur
	// radius. Outset draws the blurred shape, inset draws what's outside it; the extra scissor keeps each on its side
	// of the box.
	float4 RenderShadow( BoxInstanceData inst, PixelInput i, bool inset, out float flCoverage )
	{
		float2 boxSize = inst.Rect.zw;
		float2 p = i.vTexCoord.xy * boxSize;

		float4 shape = inst.BackgroundRect;
		float2 half = shape.zw * 0.5;
		float2 q = p - ( shape.xy + half );
		float sigma = inst.BackgroundAngle * 0.5;

		float a;
		if ( sigma > 0.01 )
			a = RoundedRectShadow( q, half, inst.BorderRadius, inst.BorderRadiusV, sigma );
		else
			a = SdfCoverage( RoundedRectSdf( q, half, inst.BorderRadius, inst.BorderRadiusV ) );

		if ( inset ) a = 1.0 - a;

		flCoverage = saturate( a );

		float4 col = i.vColor;
		col.a *= flCoverage;
		return col;
	}

	// Mode 3. BackgroundRect is (panel w, panel h, width, offset), BackgroundAngle how far the quad reaches past the
	// panel. The outline is the band between the panel shape pushed out by offset and by offset + width.
	float4 RenderOutline( BoxInstanceData inst, PixelInput i, out float flCoverage )
	{
		float2 panelSize = inst.BackgroundRect.xy;
		float outlineWidth = inst.BackgroundRect.z;
		float outlineOffset = inst.BackgroundRect.w;
		float bloat = inst.BackgroundAngle;

		float2 p = ( panelSize + bloat * 2.0 ) * i.vTexCoord.xy - bloat;
		float d = RoundedRectSdf( p - panelSize * 0.5, panelSize * 0.5, inst.BorderRadius, inst.BorderRadiusV );

		flCoverage = SdfBandCoverage( d - ( outlineOffset + outlineWidth ), d - outlineOffset );

		float4 col = i.vColor;
		col.a *= flCoverage;
		return col;
	}

	// Position along the gradient, 0 at its start and 1 at its end. Linear runs along a line through the box
	// centre, long enough that its ends touch the corners; radial and conic measure out from their centre, all
	// matching the web. gradLength comes back with it: how many pixels that 0..1 spans, which is what a stop
	// position written in pixels is measured in.
	float GradientPosition( GradientData g, float2 texCoord, float2 boxSize, out float gradLength )
	{
		gradLength = 1.0;

		if ( g.Type == 0 ) // linear
		{
			float2 dir = float2( sin( g.Angle ), cos( g.Angle ) ); // 0 = down the panel, 90deg = right

			// A corner keyword isn't 45 degrees: the gradient line is perpendicular to the diagonal
			// joining the other two corners, so it leans with the box.
			if ( g.Corner != 0 )
			{
				float2 towards = float2( ( g.Corner == 2 || g.Corner == 4 ) ? 1 : -1,
										 ( g.Corner == 3 || g.Corner == 4 ) ? 1 : -1 );

				dir = normalize( float2( towards.x * boxSize.y, towards.y * boxSize.x ) );
			}

			float2 rel = ( texCoord - 0.5 ) * boxSize;
			gradLength = abs( boxSize.x * dir.x ) + abs( boxSize.y * dir.y );
			return dot( rel, dir ) / max( gradLength, 0.0001 ) + 0.5;
		}

		float2 p = texCoord * boxSize;

		float2 c;
		c.x = ( g.CenterUnits & 1 ) ? g.Center.x * boxSize.x : g.Center.x;
		c.y = ( g.CenterUnits & 2 ) ? g.Center.y * boxSize.y : g.Center.y;

		float2 d = p - c;

		if ( g.Type == 2 ) // conic
		{
			// Zero points straight up and the sweep runs clockwise, like the web. Angle
			// rotates where it starts. frac wraps the negative half back into 0..1.
			float a = atan2( d.x, -d.y );
			return frac( ( a - g.Angle ) / 6.28318530718 );
		}

		// radial - the ending shape's radii, per the four CSS sizes
		float2 nearSide = float2( min( c.x, boxSize.x - c.x ), min( c.y, boxSize.y - c.y ) );
		float2 farSide = float2( max( c.x, boxSize.x - c.x ), max( c.y, boxSize.y - c.y ) );

		float2 radius;

		if ( g.Circle )
		{
			// One radius: the nearest/farthest side, or the distance to that corner.
			float r =	( g.SizeMode == 0 ) ? max( farSide.x, farSide.y ) :
						( g.SizeMode == 1 ) ? length( farSide ) :
						( g.SizeMode == 2 ) ? min( nearSide.x, nearSide.y ) :
						length( nearSide );

			radius = float2( r, r );
		}
		else
		{
			// A corner ellipse keeps the matching side ellipse's aspect and is scaled to
			// touch the corner, which always works out to sqrt(2) larger.
			radius =	( g.SizeMode == 0 ) ? farSide :
						( g.SizeMode == 1 ) ? farSide * 1.41421356 :
						( g.SizeMode == 2 ) ? nearSide :
						nearSide * 1.41421356;
		}

		// The gradient ray runs to the ending shape, so that's what a pixel position is along
		gradLength = radius.x;

		return length( d / max( radius, 0.0001 ) );
	}

	// A stop's position as a fraction of the gradient, resolving the ones written as pixels
	float GradientStop( GradientData g, int i, float gradLength )
	{
		if ( g.StopUnits & ( 1 << i ) )
			return g.StopOffsets[i] / max( gradLength, 0.0001 );

		return g.StopOffsets[i];
	}

	// Stops interpolate premultiplied with linear alpha, in sRGB space, like the web.
	float4 EvaluateGradient( GradientData g, float2 texCoord, float2 boxSize )
	{
		float gradLength;
		float t = GradientPosition( g, texCoord, boxSize, gradLength );

		int last = g.Count - 1;

		if ( t <= GradientStop( g, 0, gradLength ) )
			return g.StopColors[0];

		if ( t >= GradientStop( g, last, gradLength ) )
			return g.StopColors[last];

		float4 col = g.StopColors[last];

		[loop]
		for ( int s = 0; s < last; s++ )
		{
			float o1 = GradientStop( g, s + 1, gradLength );
			if ( t <= o1 )
			{
				float o0 = GradientStop( g, s, gradLength );
				float f = saturate( ( t - o0 ) / max( o1 - o0, 0.0001 ) );

				float4 A = g.StopColors[s];
				float4 B = g.StopColors[s + 1];

				float a = lerp( A.a, B.a, f );
				float3 rgb = lerp( A.rgb * A.a, B.rgb * B.a, f );
				if ( a > 0.0001 )
					rgb /= a;

				col = float4( rgb, a );
				break;
			}
		}

		return col;
	}

	// The instance's own clip stack, and for outset box-shadows the second one that keeps them out of their panel
	float InstanceClipCoverage( BoxInstanceData inst, PixelInput i )
	{
		float flCoverage = 1.0;

		if ( inst.ScissorIndex >= 0 )
			flCoverage *= ScissorCoverage( inst.ScissorIndex, i.vPositionPanelSpace.xy / i.vPositionPanelSpace.w );

		if ( inst.InverseScissorIndex >= 0 )
			flCoverage *= ScissorCoverage( inst.InverseScissorIndex, i.vPositionPanelSpace.xy / i.vPositionPanelSpace.w );

		return flCoverage;
	}

	// flCoverage is how much of the pixel the shape covers, before opacity, see UISoftenHdrEdges
	float4 RenderInstance( PixelInput i, out float flCoverage )
	{
		BoxInstanceData inst = BoxInstances[i.iInstanceID];
		if ( inst.Flags & (1u << 14) )
		{
			TextInstanceData text = PainterTextInstances[inst.TextureIndex];
			float2 p = text.Rect.xy + i.vTexCoord.xy * text.Rect.zw;
			float4 color = i.vColor;
			if ( text.TextureIndex < 0 )
			{
				float4 gradient = EvaluateTextGradient( GradientBuffer[-text.TextureIndex - 1], text.BorderImageSlice, p );
				color.rgb = UIDecodeColor( gradient.rgb );
				color.a *= saturate( gradient.a );
			}
			flCoverage = text.Mode == 0 ? 1.0 : TextCoverage( text, p, TextFootprint( p ) );
			color.a *= flCoverage;
			return color;
		}

		#if D_PANEL_OPACITY
		inst.BorderColorL.a *= g_flUIPanelOpacity;
		inst.BorderColorT.a *= g_flUIPanelOpacity;
		inst.BorderColorR.a *= g_flUIPanelOpacity;
		inst.BorderColorB.a *= g_flUIPanelOpacity;
		inst.BorderImageTint.a *= g_flUIPanelOpacity;
		inst.BackgroundTint.a *= g_flUIPanelOpacity;
		#endif

		if ( inst.GetMode() == 1 ) return RenderShadow( inst, i, false, flCoverage );
		if ( inst.GetMode() == 2 ) return RenderShadow( inst, i, true, flCoverage );
		if ( inst.GetMode() == 3 ) return RenderOutline( inst, i, flCoverage );

		// Mode 0: standard box rendering
		float2 boxSize = inst.Rect.zw;
		float4 borderWidth = inst.BorderSize;

		float2 pos = ( i.vTexCoord.xy - 0.5 ) * boxSize;
		float dOuter;
		float pathCoverage = -1.0;
		bool isPath = false;
		[branch] if ( inst.ShapeIndex >= 0 )
		{
			// Shape coordinates are relative to the box, so the local position is all it takes
			BorderShapeData shape = BorderShapeBuffer[inst.ShapeIndex];
			isPath = shape.Kind == UI_SHAPE_POLYGON_PATH || shape.Kind == UI_SHAPE_STROKE_PATH;
			if ( shape.Kind == UI_SHAPE_STROKE_PATH )
			{
				float2 local = i.vTexCoord.xy * boxSize;
				pathCoverage = PathStrokeCoverage( shape, local + shape.Circle.xy, ddx( local ), ddy( local ) );
				if ( shape.PolygonCount > 0 )
				{
					BorderShapeData mask = BorderShapeBuffer[shape.PolygonCount - 1];
					float2 p = local + shape.Circle.xy;
					float distance;
					if ( mask.Kind == UI_SHAPE_CIRCLE )
					{
						float radius = length( p - mask.Circle.xy );
						distance = radius - mask.Circle.z;
						if ( mask.Circle.w > 0.0 ) distance = max( distance, mask.Circle.w - radius );
					}
					else if ( mask.Kind >= UI_SHAPE_FIRST_ANALYTIC ) distance = ShapeSdf( p, mask.Polygon01, mask.Polygon23, mask.Polygon45, mask.Polygon67, mask.PolygonCount, mask.Circle, mask.Kind );
					else distance = PathPolygonSdf( p, mask );
					pathCoverage = min( pathCoverage, SdfCoverage( distance * shape.Circle.w ) );
				}
				dOuter = -1.0;
			}
			else if ( shape.Kind == UI_SHAPE_POLYGON_PATH )
				dOuter = PathPolygonSdf( i.vTexCoord.xy * boxSize, shape );
			else
				dOuter = ShapeSdf( i.vTexCoord.xy * boxSize, shape.Polygon01, shape.Polygon23, shape.Polygon45, shape.Polygon67, shape.PolygonCount, shape.Circle, shape.Kind );
		}
		else
		{
			dOuter = RoundedRectSdf( pos, boxSize * 0.5, inst.BorderRadius, inst.BorderRadiusV );
		}

		float4 col = i.vColor;

		float flMask = 1.0;

		// Background image or gradient (negative TextureIndex = gradient table index).
		// Both are tiles sized and placed by background-size/position, like the web.
		if ( inst.TextureIndex != 0 )
		{
			float2 bgSize = inst.BackgroundRect.zw;
			float4 bgTint = inst.BackgroundTint;
			bgTint.rgb = UIDecodeColor( bgTint.rgb );
			bgTint.a = saturate( bgTint.a );

			float2 vOffset = inst.BackgroundRect.xy / bgSize;
			float2 vUV = -vOffset + ( i.vTexCoord.xy * ( boxSize / bgSize ) );

			float4 vImage;
			int bgRepeat = inst.GetBackgroundRepeat();

			vUV = RotateTexCoord( vUV, inst.BackgroundAngle );
			if ( inst.TextureIndex > 0 )
			{

				Texture2D tex = Bindless::GetTexture2D( inst.TextureIndex );
				vImage = tex.SampleBias( Bindless::GetSampler( NonUniform( inst.SamplerIndex ) ), vUV, -1.5 ); // negative = sharper, positive = blurrier
			}
			else
			{
				// The sampler wraps textures; wrap the tile coordinate ourselves.
				// Clamping falls out of the stop walk, which pins to the end stops.
				float2 tileUV = vUV;
				if ( bgRepeat == 0 || bgRepeat == 1 ) tileUV.x = frac( tileUV.x );
				if ( bgRepeat == 0 || bgRepeat == 2 ) tileUV.y = frac( tileUV.y );

				GradientData gradient = GradientBuffer[ -inst.TextureIndex - 1 ];
				vImage = EvaluateGradient( gradient, tileUV, bgSize );
			}

			if ( bgRepeat != 0 && bgRepeat != 4 )
			{
				if ( bgRepeat != 1 )
					if ( vUV.x < 0 || vUV.x > 1 ) vImage = 0;
				if ( bgRepeat != 2 )
					if ( vUV.y < 0 || vUV.y > 1 ) vImage = 0;
			}

			float imageCoverage = saturate( vImage.a );
			#if ( D_BLENDMODE == 3 )
				// Premultiplied texture, premultiplied in sRGB space
				vImage = UIPremultipliedTexel( vImage );
				vImage.rgb *= bgTint.rgb;
				vImage *= bgTint.a;
			#else
				vImage.xyz = UIDecodeColor( vImage.xyz );
				vImage *= bgTint;
			#endif

			col.rgb = lerp( col.rgb, vImage.rgb, saturate( vImage.a + ( 1 - col.a ) ) );
			col.a = max( col.a, vImage.a );

			// A texture's alpha is a mask - a glyph's edge lives there - so it's coverage. A gradient's isn't.
			if ( inst.TextureIndex > 0 )
				flMask = isPath ? imageCoverage : saturate( vImage.a );
		}

		// The border isn't clipped, so the background is cut back before the border goes on
		float bgClip = BackgroundClipCoverage( inst, pos, boxSize, i.vTexCoord.xy );
		#if ( D_BLENDMODE == 3 )
			col *= bgClip;
		#else
			col.a *= bgClip;
		#endif
		flMask *= bgClip;

		// Border image or solid border
		if ( inst.GetBorderImageMode() > 0 )
		{
			float4 biTint = inst.BorderImageTint;
			biTint.rgb = UIDecodeColor( biTint.rgb );
			biTint.a = saturate( biTint.a );
			float4 vBoxBorder = AddImageBorder( i.vTexCoord.xy, boxSize, borderWidth, inst.BorderImageIndex,
				NonUniform( inst.BorderImageSamplerIndex ), inst.GetBorderImageMode(), inst.GetBorderImageFill(), inst.BorderImageSlice ) * biTint;
			col = AlphaBlend( vBoxBorder, col );
			flMask = max( flMask, saturate( vBoxBorder.a ) );
		}
		else if ( inst.ShapeIndex < 0 )
		{
			bool hasBorder = borderWidth.x != 0 || borderWidth.y != 0 || borderWidth.z != 0 || borderWidth.w != 0;
			if ( hasBorder )
			{
				// The border is everything inside the box that isn't inside the padding box
				float dInner = InsetBoxSdf( pos, boxSize, inst.BorderRadius, inst.BorderRadiusV, borderWidth );

				float4 vBoxBorder = inst.GetBorderStyle() != 0 ? StyledBoxBorder( i.vTexCoord.xy * boxSize, inst )
					: BorderSideColor( i.vTexCoord.xy * boxSize, boxSize, borderWidth,
					inst.BorderColorL, inst.BorderColorT,
					inst.BorderColorR, inst.BorderColorB );
				vBoxBorder.xyz = UIDecodeColor( vBoxBorder.xyz );
				vBoxBorder.a = saturate( vBoxBorder.a ) * ( 1.0 - SdfCoverage( dInner ) );
				col = AlphaBlend( vBoxBorder, col );
				flMask = max( flMask, vBoxBorder.a );
			}
		}

		if ( inst.ShapeIndex >= 0 )
		{
			float width = max( max( borderWidth.x, borderWidth.y ), max( borderWidth.z, borderWidth.w ) );
			if ( width > 0 )
			{
				float4 border = inst.GetBorderStyle() != 0
					? StyledShapeBorder( i.vTexCoord.xy * boxSize, inst, BorderShapeBuffer[inst.ShapeIndex], dOuter ) : inst.BorderColorL;
				border.rgb = UIDecodeColor( border.rgb );
				border.a = saturate( border.a ) * ( 1.0 - SdfCoverage( dOuter + width ) );
				col = AlphaBlend( border, col ); flMask = max( flMask, border.a );
			}
		}

		float edge = pathCoverage >= 0.0 ? pathCoverage : SdfCoverage( dOuter );
		flCoverage = edge * flMask;

		// Premultiplied colour scales as a whole, straight alpha by alpha
		#if ( D_BLENDMODE == 3 )
			col *= edge;
		#else
			col.a *= edge;
		#endif

		return col;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		BoxInstanceData inst = BoxInstances[i.iInstanceID];
		bool path = false;
		if ( inst.ShapeIndex >= 0 )
		{
			int kind = BorderShapeBuffer[inst.ShapeIndex].Kind;
			path = kind == UI_SHAPE_POLYGON_PATH || kind == UI_SHAPE_STROKE_PATH;
			if ( path )
			{
				// The horizon itself is discarded below; keep its derivative helpers finite.
				float2 local = i.vPathPosition.xy / ( i.vPathPosition.z != 0.0 ? i.vPathPosition.z : 1.0 );
				i.vTexCoord.xy = ( local - inst.Rect.xy ) / inst.Rect.zw;
				i.vPositionPanelSpace = mul( TransformBuffer[inst.TransformIndex].Mat, float4( local, 0, 1 ) );
			}
		}

		float flCoverage;
		float4 col = RenderInstance( i, flCoverage );

		// Clip last, so nothing taking screen derivatives runs after the discard
		float flClip = InstanceClipCoverage( inst, i );
		if ( path && i.vPathPosition.z <= 0.0 ) clip( -1 );
		if ( flClip <= 0.0 )
			clip( -1 );

		// Premultiplied content already sits in the target's space, see ui/gamma.hlsl
		#if ( D_BLENDMODE == 3 )
			col *= flClip;
		#else
			col.a *= flClip;
			col = UISoftenHdrEdges( col, flCoverage * flClip );
		#endif

		return col;
	}
}
