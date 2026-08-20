HEADER
{
	DevShader = true;
	Version = 1;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Default();
	Forward();
	
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
	#include "ui/features.hlsl"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "ui/common.hlsl"
	#include "common/Bindless.hlsl"
}
  
//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	#include "ui/vertex.hlsl"  
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{ 
	#include "ui/pixel.hlsl"
	#include "ui/rounded_rect.hlsl"

	DynamicCombo( D_BORDER_IMAGE, 0..2, Sys( PC ) ); // None = 0, Rounded = 1, Stretch = 2
	DynamicCombo( D_BACKGROUND_IMAGE, 0..1, Sys( PC ) ); // Use Background Image = 1

	bool HasBorder <Default( 0 ); Attribute( "HasBorder" );>;
	bool HasBorderImageFill <Default(  0 ); Attribute( "HasBorderImageFill" );>;
	float4 CornerRadius < Attribute( "BorderRadius" ); >;	// horizontal radii ( top-left, top-right, bottom-left, bottom-right )
	float4 CornerRadiusV < Attribute( "BorderRadiusV" ); >;	// vertical radii, same order
	float BoxBloat < Default( 0.0 ); Attribute( "BoxBloat" ); >;	// pixels the quad is grown by on each side, so the edge antialiasing has room
	float4 BorderWidth < UiGroup( "Border" ); Attribute( "BorderSize" ); >;	// left, top, right, bottom
	float4 BorderImageSlice < UiGroup( "Border" ); Attribute( "BorderImageSlice"); >;
	float4 BorderColorL < UiType( Color ); Default4( 0.0, 0.0, 0.0, 1.0 ); UiGroup( "Border,10/Colors,10/1" ); Attribute( "BorderColorL" ); >;
	float4 BorderColorT < UiType( Color ); Default4( 0.0, 0.0, 0.0, 1.0 ); UiGroup( "Border,10/Colors,10/2" ); Attribute( "BorderColorT" ); >;
	float4 BorderColorR < UiType( Color ); Default4( 0.0, 0.0, 0.0, 1.0 ); UiGroup( "Border,10/Colors,10/3" ); Attribute( "BorderColorR" ); >;
	float4 BorderColorB < UiType( Color ); Default4( 0.0, 0.0, 0.0, 1.0 ); UiGroup( "Border,10/Colors,10/4" ); Attribute( "BorderColorB" ); >;

	float4 BgPos < Default4( 0.0, 0.0, 500.0, 100.0 ); Attribute( "BgPos" ); >;
	float4 BgTint < Default4( 1.0, 1.0, 1.0, 1.0 ); Attribute( "BgTint" ); >;

	int BgRepeat <Attribute( "BgRepeat" );>;
	float BgAngle < Default( 0.0 ); Attribute( "BgAngle" ); >;
	
	Texture2D g_tBorderImage 	< Attribute( "BorderImageTexture" ); Default( 1.0 ); >;

	int SamplerIndex < Attribute( "SamplerIndex" ); >;
	int ClampSamplerIndex < Attribute( "ClampSamplerIndex" ); >;

	Texture2D g_tColor 	< Attribute( "Texture" ); SrgbRead( false ); >;

	float4 BorderImageTint < Default4( 1.0, 1.0, 1.0, 1.0 ); Attribute( "BorderImageTint" ); >;

	float4 g_vTextureDim < Source( TextureDim ); SourceArg( g_tColor ); >;
	float4 g_vInvTextureDim < Source( InvTextureDim ); SourceArg( g_tColor ); >;
	float4 g_vViewport < Source( Viewport ); >;

	// Render State -------------------------------------------------------------------------------------------------------------------------------------------

	// Always write rgba
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );

	// Never cull
	RenderState( CullMode, NONE );

	// No depth
	RenderState( DepthWriteEnable, false );

	float3 TonemapBasic( float3 vColor, float flWeight )
	{
		return vColor * ( flWeight * rcp( max( vColor.r, max( vColor.g, vColor.b ) ) + 1.0f ) );
	}

	float2 RotateTexCoord( float2 vTexCoord, float angle, float2 offset = 0.5 )
	{
		float2x2 m = float2x2( cos(angle), -sin(angle), sin(angle), cos(angle) );
		return mul( m, vTexCoord - offset ) + offset;
	}

	// Distance to the padding box edge: the box inset by its borders, each corner's radii less the border on
	// its two sides. A corner that loses either radius goes square, like CSS.
	float PaddingBoxSdf( float2 p )
	{
		float2 innerSize = max( BoxSize - float2( BorderWidth.x + BorderWidth.z, BorderWidth.y + BorderWidth.w ), 0.0 );
		float2 innerCentre = float2( BorderWidth.x - BorderWidth.z, BorderWidth.y - BorderWidth.w ) * 0.5;

		float4 innerH = CornerRadius - BorderWidth.xzxz;
		float4 innerV = CornerRadiusV - BorderWidth.yyww;
		float4 keep = step( 0.0001, innerH ) * step( 0.0001, innerV );

		return RoundedRectSdf( p - innerCentre, innerSize * 0.5, innerH * keep, innerV * keep );
	}

	// Which side's colour a border pixel takes. CSS splits each corner along the line from the box corner
	// to the padding box corner, which is the same as picking the side the pixel is proportionally least
	// deep into. Sides with no border never win. The join between the two nearest sides is antialiased
	// over a pixel, like the web strokes it.
	float4 BorderSideColor( float2 pos )
	{
		float4 has = step( 0.0001, BorderWidth );
		float4 depth = float4( pos.x, pos.y, BoxSize.x - pos.x, BoxSize.y - pos.y ) / max( BorderWidth, 0.0001 );
		depth = depth * has + ( 1.0 - has ) * 1e9;

		// Nearest side and runner up
		float4 c1 = BorderColorL, c2 = BorderColorT;
		float d1 = depth.x, d2 = depth.y;
		if ( d2 < d1 ) { c1 = BorderColorT; c2 = BorderColorL; d1 = depth.y; d2 = depth.x; }
		if ( depth.z < d1 ) { c2 = c1; d2 = d1; c1 = BorderColorR; d1 = depth.z; } else if ( depth.z < d2 ) { c2 = BorderColorR; d2 = depth.z; }
		if ( depth.w < d1 ) { c2 = c1; d2 = d1; c1 = BorderColorB; d1 = depth.w; } else if ( depth.w < d2 ) { c2 = BorderColorB; d2 = depth.w; }

		// How far this pixel is from the join line, in pixels
		float diff = d2 - d1;
		float t = saturate( 0.5 - diff / max( fwidth( diff ), 0.0001 ) );
		return lerp( c1, c2, t );
	}

	float4 AlphaBlend( float4 src, float4 dest )
	{
		float4 result;
		result.a = src.a + (1 - src.a) * dest.a;
		result.rgb = ( src.a * src.rgb + (1 - src.a) * dest.a * dest.rgb ) / max( result.a, 0.0001 );
		return result;
	}

	float4 AddImageBorder( float2 texCoord )
	{
		const float4 BorderImageWidth = BorderWidth; //Pixel width of the border, Left, Top, Right, Down

		const float2 vBorderImageSize = TextureDimensions2D( g_tBorderImage, 0 );
		const float4 vBorderPixelSize = BorderImageSlice; // Left, Top, Right, Down
		const float4 vBorderPixelRatio = vBorderPixelSize / float4(vBorderImageSize.x,vBorderImageSize.y,vBorderImageSize.x,vBorderImageSize.y);

		const float2 vBoxTexCoord = texCoord * BoxSize; //Texcoord mapped to pixel size
		
		float2 uv = 0.0;

		// If we aren't filling the middle, make it transparent
		if(  !HasBorderImageFill && 
			vBoxTexCoord.x > BorderImageWidth.x &&
			vBoxTexCoord.x < BoxSize.x - BorderImageWidth.z &&
			vBoxTexCoord.y > BorderImageWidth.y &&
			vBoxTexCoord.y < BoxSize.y - BorderImageWidth.w )
			return 0;

		//If PixelSize > ImageSize/2, it doesn't draw the side borders
		if( vBorderPixelSize.x < vBorderImageSize.x * 0.5)
		{
			if ( D_BORDER_IMAGE == 1 )
			{
				float2 vMiddleSize = 1.0 - (vBorderPixelRatio.xy + vBorderPixelRatio.zw);
				float2 vRepeatAmount = floor( ( BoxSize * vMiddleSize ) / BorderImageWidth.xy );
				// Horizontal Middle Repeat
				uv.x = ( vBoxTexCoord.x - BorderImageWidth.x ) / ( BoxSize.x - ( BorderImageWidth.x + BorderImageWidth.z ) ) * vRepeatAmount.x;
				uv.x = fmod( uv.x, vMiddleSize.x );
				uv.x += vBorderPixelRatio.x; //Get the offset of the middle one

				//Vertical Middle Repeat
				uv.y = ( vBoxTexCoord.y - BorderImageWidth.y ) / ( BoxSize.y - ( BorderImageWidth.y + BorderImageWidth.w ) ) * vRepeatAmount.y;
				uv.y = fmod( uv.y, vMiddleSize.y );
				uv.y += vBorderPixelRatio.y; //Get the offset of the middle one
			}
			else
			{
				// Horizontal Middle, stretch 
				uv.x = ( vBoxTexCoord.x - BorderImageWidth.x ) / ( BoxSize.x - ( BorderImageWidth.x + BorderImageWidth.z ) );
				uv.x *= 1.0 - (vBorderPixelRatio.x + vBorderPixelRatio.z); //Get the size of the middle one
				uv.x += vBorderPixelRatio.x; //Get the offset of the middle one

				//Vertical Middle, stretch
				uv.y = ( vBoxTexCoord.y - BorderImageWidth.y ) / ( BoxSize.y - ( BorderImageWidth.y + BorderImageWidth.w ) );
				uv.y *= 1.0 - (vBorderPixelRatio.y + vBorderPixelRatio.w); //Get the size of the middle one
				uv.y += vBorderPixelRatio.y; //Get the offset of the middle one
			}
		}
		
		//Horizontal Left
		if( vBoxTexCoord.x < BorderImageWidth.x )
			uv.x = (vBoxTexCoord.x / BorderImageWidth.x) * vBorderPixelRatio.x; 

		// Horizontal Right
		else if( vBoxTexCoord.x > BoxSize.x - BorderImageWidth.z )
			uv.x = ( ( (vBoxTexCoord.x - ( BoxSize.x - BorderImageWidth.z) ) / BorderImageWidth.z) * vBorderPixelRatio.z ) + ( 1.0 - vBorderPixelRatio.z );

		// Vertical Top
		if( vBoxTexCoord.y < BorderImageWidth.y )
			uv.y = (vBoxTexCoord.y / BorderImageWidth.y) * vBorderPixelRatio.y;
		
		// Vertical Bottom
		else if( vBoxTexCoord.y > BoxSize.y - BorderImageWidth.w )
			uv.y = ( ( (vBoxTexCoord.y - ( BoxSize.y - BorderImageWidth.w) ) / BorderImageWidth.w) * vBorderPixelRatio.w ) + ( 1.0 - vBorderPixelRatio.w );

		float4 r = g_tBorderImage.Sample( Bindless::GetSampler( ClampSamplerIndex ), uv );
		r.xyz = SrgbGammaToLinear( r.xyz );
		return r;
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;

		float2 bgSize = BgPos.zw;
		float4 bgTint = BgTint.rgba;
		bgTint.rgb = SrgbGammaToLinear(bgTint.rgb);

		float4 borderImageTint = BorderImageTint.rgba;
		borderImageTint.rgb = SrgbGammaToLinear(borderImageTint.rgb);

		// The quad may be the box grown by BoxBloat; make texcoords 0..1 across the box itself
		i.vTexCoord.xy = ( i.vTexCoord.xy - 0.5 ) * ( BoxSize + BoxBloat * 2.0 ) / max( BoxSize, 0.0001 ) + 0.5;

		// Pixels from the box centre
		float2 pos = ( i.vTexCoord.xy - 0.5 ) * BoxSize;
		float dOuter = RoundedRectSdf( pos, BoxSize * 0.5, CornerRadius, CornerRadiusV );

		float4 vBox = i.vColor.rgba;
		float4 vBoxBorder;

		UI_CommonProcessing_Pre( i );

		if ( D_BORDER_IMAGE )
		{
			vBoxBorder = AddImageBorder( i.vTexCoord.xy ) * borderImageTint;
		}
		else
		{
			if ( HasBorder )
			{
				// The border is everything inside the box that isn't inside the padding box
				vBoxBorder = BorderSideColor( i.vTexCoord.xy * BoxSize );
				vBoxBorder.xyz = SrgbGammaToLinear( vBoxBorder.xyz );
				vBoxBorder.a = saturate( vBoxBorder.a ) * ( 1.0 - SdfCoverage( PaddingBoxSdf( pos ) ) );
			}
			else
			{
				vBoxBorder = 0;
			}
		}

		if ( D_BACKGROUND_IMAGE == 1 )
		{
			float2 vOffset = BgPos.xy / bgSize;
			
			float2 vUV = -vOffset + ( ( i.vTexCoord.xy ) * ( BoxSize / bgSize ) );

			vUV = RotateTexCoord( vUV, BgAngle );

			float4 vImage;

			vImage = g_tColor.Sample( Bindless::GetSampler( SamplerIndex ), vUV );

			// Clamping UV? NoRepeat (3) will clamp both
			if ( BgRepeat != 0 && BgRepeat != 4 )
			{
				// Clamp U
				if ( BgRepeat != 1 )
				{
					if( vUV.x < 0 || vUV.x > 1 ) vImage = 0;
				}

				// Clamp V
				if ( BgRepeat != 2 )
				{
					if( vUV.y < 0 || vUV.y > 1 ) vImage = 0;
				}
			}
			
			#if ( D_BLENDMODE == 3 )
				// Premultiplied texture, premultiplied in sRGB space
				vImage = UIPremultipliedTexel( vImage );
				vImage.rgb *= bgTint.rgb;
				vImage *= bgTint.a;
			#else
				vImage.xyz = SrgbGammaToLinear( vImage.xyz );
				vImage *= bgTint;
			#endif

			vBox.rgb = lerp( vBox.rgb, vImage.rgb, saturate( vImage.a + ( 1 - vBox.a ) ) );
			vBox.a = max( vBox.a, vImage.a );
		}
		
		o.vColor = vBox;

		if ( D_BORDER_IMAGE == 1 || HasBorder == 1 )
		{
			o.vColor = AlphaBlend( vBoxBorder, o.vColor );
		}

		float edge = SdfCoverage( dOuter );
		o.vColor.a *= edge;

		// Premultiplied content already sits in the target's space, see ui/gamma.hlsl
		#if ( D_BLENDMODE == 3 )
			o.vColor = UI_ApplyClip( o.vColor, true );
			return o;
		#else
			return UI_CommonProcessing_Post( i, o, edge );
		#endif
	}
}
