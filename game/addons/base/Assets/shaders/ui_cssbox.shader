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

	DynamicCombo( D_BORDER_IMAGE, 0..2, Sys( PC ) ); // None = 0, Rounded = 1, Stretch = 2
	DynamicCombo( D_BACKGROUND_IMAGE, 0..1, Sys( PC ) ); // Use Background Image = 1

	bool HasBorder <Default( 0 ); Attribute( "HasBorder" );>;
	bool HasBorderImageFill <Default(  0 ); Attribute( "HasBorderImageFill" );>;
	float4 CornerRadius < Attribute( "BorderRadius" ); >;
	float4 BorderWidth < UiGroup( "Border" ); Attribute( "BorderSize" ); >;	
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
	RenderState( SrgbWriteEnable0, true );

	// Always write rgba
	RenderState( ColorWriteEnable0, RGBA );
	RenderState( FillMode, SOLID );

	// Never cull
	RenderState( CullMode, NONE );

	// No depth
	RenderState( DepthWriteEnable, false );

	#define SUBPIXEL_AA_MAGIC 0.5

	float3 TonemapBasic( float3 vColor, float flWeight )
	{
		return vColor * ( flWeight * rcp( max( vColor.r, max( vColor.g, vColor.b ) ) + 1.0f ) );
	}

	float GetDistanceFromEdge( float2 pos, float2 size, float4 cornerRadius )
	{
		float minCorner = min(size.x, size.y);

		//Based off https://iquilezles.org/www/articles/distfunctions2d/distfunctions2d.htm

		float4 r = min( cornerRadius * 2.0 , minCorner );
		r.xy = (pos.x>0.0)?r.xy : r.zw;
		r.x  = (pos.y>0.0)?r.x  : r.y;
		float2 q = abs(pos)-(size)+r.x;
		return -0.5 + min(max(q.x,q.y),0.0) + length(max(q,0.0)) - r.x;
	}

	float2 RotateTexCoord( float2 vTexCoord, float angle, float2 offset = 0.5 )
	{
		float2x2 m = float2x2( cos(angle), -sin(angle), sin(angle), cos(angle) );
		return mul( m, vTexCoord - offset ) + offset;
	}

	float4 CalcBorderColor( float4 ruv )
	{
		float4 c = 	( BorderColorL * ruv.x ) +
					( BorderColorT * ruv.y ) +
					( BorderColorR * ruv.z ) +
					( BorderColorB * ruv.a );
		return c; 
	}

	float CalcBorderWidth( float4 ruv ) 
	{ 
		float w = 	( BorderWidth.x * ruv.x ) +
					( BorderWidth.y * ruv.y ) +
					( BorderWidth.z * ruv.z ) +
					( BorderWidth.a * ruv.a );
		return w * 2.0;
	}

	float2 DistanceNormal( float2 p, float2 c )
	{
		const float eps = 1;
		const float2 h = float2(eps,0);
		return normalize( float3( GetDistanceFromEdge(p-h.xy, c, CornerRadius) - GetDistanceFromEdge(p+h.xy, c, CornerRadius),
								GetDistanceFromEdge(p-h.yx, c, CornerRadius) - GetDistanceFromEdge(p+h.yx, c, CornerRadius),
								2.0*h.x
			) ).xy;
	}

	float4 AddBorder( float2 texCoord, float2 pos, float distanceFromCenter )
	{
		float2 vTransPos = texCoord * BoxSize;

		//Scale - Fixme: this is messing transitions
		float2 fScale = 1.0 / ( 1.0 - ( float2( BorderWidth.z + BorderWidth.x , BorderWidth.y + BorderWidth.w ) / BoxSize) );
		vTransPos = ( vTransPos - ( BoxSize * 0.5 ) ) * ( fScale ) + ( BoxSize * 0.5 );	
		
		//Offset
		vTransPos += float2( -BorderWidth.x + BorderWidth.z, -BorderWidth.y + BorderWidth.a ) * (fScale * 0.5);

		float2 vOffsetPos = ( BoxSize ) * ( ( vTransPos / BoxSize) * 2.0 - 1.0);
		
		float2 vNormal = DistanceNormal( vOffsetPos, BoxSize );

		float fDistance = GetDistanceFromEdge( vOffsetPos, BoxSize, CornerRadius );
		fDistance += 1.5;

		float4 vBorderL = BorderColorL;
		float4 vBorderT = BorderColorT;
		float4 vBorderR = BorderColorR;
		float4 vBorderB = BorderColorB;

		vBorderL.a = max( vNormal.x, 0 ) * fDistance / ( BorderWidth.x );
		vBorderT.a = max( vNormal.y, 0 ) * fDistance / ( BorderWidth.y );
		vBorderR.a = max(-vNormal.x, 0 ) * fDistance / ( BorderWidth.z );
		vBorderB.a = max(-vNormal.y, 0 ) * fDistance / ( BorderWidth.w );
		
		float4 vBorderColor = -100;
		float fBorderAlpha = 0;
		
		if( BorderWidth.x > 0.0f && vBorderL.a > vBorderColor.a ) { vBorderColor = vBorderL; fBorderAlpha = BorderColorL.a; }
		if( BorderWidth.y > 0.0f && vBorderT.a > vBorderColor.a ) { vBorderColor = vBorderT; fBorderAlpha = BorderColorT.a; }
		if( BorderWidth.z > 0.0f && vBorderR.a > vBorderColor.a ) { vBorderColor = vBorderR; fBorderAlpha = BorderColorR.a; }
		if( BorderWidth.a > 0.0f && vBorderB.a > vBorderColor.a ) { vBorderColor = vBorderB; fBorderAlpha = BorderColorB.a; }

		float fAntialiasAmount = max( 1.0f / SUBPIXEL_AA_MAGIC, 2.0f / SUBPIXEL_AA_MAGIC * abs( distanceFromCenter / ( min(BoxSize.x, BoxSize.y) ) ) );
		vBorderColor.a = saturate( smoothstep( 0, fAntialiasAmount, fDistance ) )  * fBorderAlpha;

		return vBorderColor;
	}

	float4 AlphaBlend( float4 src, float4 dest )
	{
		float4 result;
		result.a = src.a + (1 - src.a) * dest.a;
		result.rgb = (1 / result.a) * (src.a * src.rgb + (1 - src.a) * dest.a * dest.rgb);
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
				uv.y = ( vBoxTexCoord.y - BorderImageWidth.y ) / ( BoxSize.y - ( BorderImageWidth.y + BorderImageWidth.z ) ) * vRepeatAmount.y;
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

		float4 r = g_tBorderImage.Sample( Bindless::GetSampler( NonUniformResourceIndex( ClampSamplerIndex ) ), uv );
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

		float borderRadius = 0;
		float2 pos = ( BoxSize ) * (i.vTexCoord.xy * 2.0 - 1.0);  

		float dist = GetDistanceFromEdge( pos, BoxSize, CornerRadius );
		
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
				vBoxBorder = AddBorder( i.vTexCoord.xy, pos, dist );
				vBoxBorder.xyz = SrgbGammaToLinear( vBoxBorder.xyz );
			}
			else
			{
				vBoxBorder = 0;
			}
		}
  
		// this makes the corner radius borders uneven and weird
		//	dist -= D_BORDER_IMAGE ? 1.0 + SUBPIXEL_AA_MAGIC : 1.0; // Add one pixel to fill to specified size
		
		if ( D_BACKGROUND_IMAGE == 1 )
		{
			float2 vOffset = BgPos.xy / bgSize;
			
			float2 vUV = -vOffset + ( ( i.vTexCoord.xy ) * ( BoxSize / bgSize ) );

			vUV = RotateTexCoord( vUV, BgAngle );

			float4 vImage;

			float mipBias = -1.5; // negative = sharper, positive = blurrier
	
			vImage = g_tColor.SampleBias( Bindless::GetSampler( NonUniformResourceIndex( SamplerIndex ) ), vUV, mipBias );

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
			
			vImage.xyz = SrgbGammaToLinear( vImage.xyz );

			#if ( D_BLENDMODE == 3 ) // PREMULIPLIED
				vImage.rgb *= bgTint.rgb;
				vImage *= bgTint.a;
			#else
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

		// corner curves
		o.vColor.a *= saturate( -dist - 0.5 );
		
		return UI_CommonProcessing_Post( i, o );
	}
}
