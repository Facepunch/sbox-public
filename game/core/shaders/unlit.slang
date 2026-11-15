// unlit.shader as a pass module: Borealis.Gpu draws this; the .shader stays for the other routes
// and as the fallback (docs/managed/shader-format-replacement.md, "Pass modules").
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================
//
// ########     ###    ##    ##  ######   ######## ########  ####          ########     ###    ##    ##  ######   ######## ########  ####
// ##     ##   ## ##   ###   ## ##    ##  ##       ##     ## ####          ##     ##   ## ##   ###   ## ##    ##  ##       ##     ## ####
// ##     ##  ##   ##  ####  ## ##        ##       ##     ## ####          ##     ##  ##   ##  ####  ## ##        ##       ##     ## ####
// ##     ## ##     ## ## ## ## ##   #### ######   ########   ##           ##     ## ##     ## ## ## ## ##   #### ######   ########   ##
// ##     ## ######### ##  #### ##    ##  ##       ##   ##                 ##     ## ######### ##  #### ##    ##  ##       ##   ##
// ##     ## ##     ## ##   ### ##    ##  ##       ##    ##  ####          ##     ## ##     ## ##   ### ##    ##  ##       ##    ##  ####
// ########  ##     ## ##    ##  ######   ######## ##     ## ####          ########  ##     ## ##    ##  ######   ######## ##     ## ####
//
// ___  ____    _  _ ____ ___    _ _  _ ____ _    _  _ ___  ____    ____ _  _ _   _    ____ _ _    ____ ____    _ _  _    ___ _  _ _ ____
// |  \ |  |    |\ | |  |  |     | |\ | |    |    |  | |  \ |___    |__| |\ |  \_/     |___ | |    |___ [__     | |\ |     |  |__| | [__
// |__/ |__|    | \| |__|  |     | | \| |___ |___ |__| |__/ |___    |  | | \|   |      |    | |___ |___ ___]    | | \|     |  |  | | ___]
//
//            ____ _  _ ____ ___  ____ ____    ____ _  _ ____ ____ ___  ___    ____ _   _ ____ ___ ____ _  _  ____ _  _ ____   /
//            [__  |__| |__| |  \ |___ |__/    |___  \/  |    |___ |__]  |     [__   \_/  [__   |  |___ |\/|  |___  \/  |     /
//            ___] |  | |  | |__/ |___ |  \    |___ _/\_ |___ |___ |     |     ___]   |   ___]  |  |___ |  | .|    _/\_ |___ .
//
//=============================================================================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Used to draw UI elements";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// MODES: Default();

#include "sbox/pass.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
Feature( F_ASPECT_FILL_FROM_CENTER, 0..1, "Aspect Fill" );
Feature( F_TRANSLUCENT, 0..1, "Translucent" );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// !!! DO NOT INCLUDE ANY OTHER FILES IN THIS SHADER !!!
#include "system.fxc"
// !!! DO NOT INCLUDE ANY OTHER FILES IN THIS SHADER !!!

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionSs			: POSITION < Semantic( PosXyz ); >;
	float4 vColor				: COLOR0 < Semantic( Color ); >;
	float2 vTexCoord			: TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float4 vColor				: COLOR0;
	float2 vTexCoord			: TEXCOORD0;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs	: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
// DO NOT INCLUDE ANY FILES IN THIS SHADER!

// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;

	o.vPositionPs.xy = i.vPositionSs.xy;
	o.vPositionPs.y *= -1.0;
	o.vPositionPs.z = 1.0;
	o.vPositionPs.w = 1.0;

	o.vColor.rgba = i.vColor.rgba;
	o.vTexCoord.xy = i.vTexCoord.xy;

	return o;
}

#endif // VS

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_PS )
	// DO NOT INCLUDE ANY FILES IN THIS SHADER!

	// Combos -------------------------------------------------------------------------------------------------------------------------------------------------
	StaticCombo( S_ASPECT_FILL_FROM_CENTER, F_ASPECT_FILL_FROM_CENTER, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );

	// Artist generated textures ------------------------------------------------------------------------------------------------------------------------------
	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color", Default4( 1.0, 1.0, 1.0, 1.0 ) );
#if ( S_TRANSLUCENT )
	Texture2D g_tColor < Channel( RGBA, Box( TextureColor ), Srgb );
								  OutputFormat( DXT5 ); SrgbRead( true ); >;
#else
 	Texture2D g_tColor < Channel( RGB, Box( TextureColor ), Srgb );
								  OutputFormat( DXT1 ); SrgbRead( true ); >;
#endif

	TextureAttribute( RepresentativeTexture, g_tColor );

	// Render State -------------------------------------------------------------------------------------------------------------------------------------------
	// Always alpha blend; we don't write Z so it doesn't matter
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	// Always write rgb
	RenderState( ColorWriteEnable0, RGB );
	RenderState( FillMode, SOLID );

	// Never cull
	RenderState( CullMode, NONE );

	// No depth
	RenderState( DepthEnable, false );
	RenderState( DepthWriteEnable, false );

	#if ( S_ASPECT_FILL_FROM_CENTER )
		//float4 g_vTextureSize < Source( TextureDim ); SourceArg( g_tColor ); >;
		float4 g_vViewport < Source( Viewport ); >;
		float2 g_vImageAspectRatio < Default2( 16.0, 9.0 ); Range2( 1.0, 1.0, 16.0, 16.0 ); UiGroup( "Aspect Fill" ); >;
	#endif

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
	struct PS_OUTPUT
	{
		float4 vColor : SV_Target0;
	};

	[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;

		float2 vTexCoord = i.vTexCoord.xy;

		#if ( S_ASPECT_FILL_FROM_CENTER )
		{
			if ( ( g_vViewport.z / g_vImageAspectRatio.x ) / ( g_vViewport.w / g_vImageAspectRatio.y ) > 1.0 )
			{
				// Image is taller than screen, so crop top and bottom of image
				vTexCoord.xy = i.vTexCoord.xy;
				vTexCoord.y -= 0.5;
				vTexCoord.y /= ( g_vViewport.z / g_vImageAspectRatio.x ) / ( g_vViewport.w / g_vImageAspectRatio.y );
				vTexCoord.y += 0.5;
			}
			else
			{
				// Image is wider than screen, so crop sides of image
				vTexCoord.xy = i.vTexCoord.xy;
				vTexCoord.x -= 0.5;
				vTexCoord.x *= ( g_vViewport.z / g_vImageAspectRatio.x ) / ( g_vViewport.w / g_vImageAspectRatio.y );
				vTexCoord.x += 0.5;
			}
		}
		#endif

		o.vColor = i.vColor.rgba * g_tColor.Sample( g_sDefault, vTexCoord.xy );
		return o;
	}

#endif // PS

