// copytexture: a standalone module, the whole shader for Borealis.Gpu (docs/reference/borealis-standalone-modules.md).
// Copyright (c) Valve Corporation, All rights reserved. ======================================================================================================

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
// HEADER: DevShader = true;
// HEADER: Description = "Copy texture";

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

#include "sbox/pass.slangh"

Mode( Default );

//-------------------------------------------------------------------------------------------------------------------------------------------------------------

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#include "system.fxc" // This should always be the first include in COMMON
#include "common.fxc"
#include "sbox/math.slangh"

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float3 vPositionSs : POSITION < Semantic( PosXyz ); >;
	float2 vTexCoords : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float2 vTexCoords				: TEXCOORD0;

	// VS only
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs			: SV_Position;
	#endif

	// PS only
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs			: SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
#if ( PROGRAM == VFX_PROGRAM_VS )
[shader( "vertex" )] PS_INPUT MainVs( const VS_INPUT i )
{
	PS_INPUT o;

	o.vPositionPs.xyzw = float4( i.vPositionSs, 1.0 );
	o.vTexCoords.xy = i.vTexCoords.xy;
	return o;
}

#endif // VS

#if ( PROGRAM == VFX_PROGRAM_PS )
DynamicCombo( D_VOLUME_TEXTURE, 0..1, Sys( ALL ) );
DynamicCombo( D_CHANNEL_CHOICE, 0..1, Sys( ALL ) );
DynamicCombo( D_MSAA, 0..1, Sys( ALL ) );
DynamicCombo( D_MSAA_COLOR, 0..1, Sys( ALL ) ); // A multisampled colour source, reads its first sample

// Moves the scene in and out of a gamma-space UI target on float render targets, see CManagedRenderPipeline::OnUIHook.
// 1: write the source sRGB-encoded with zero coverage. 2: decode the source back over the target where it has coverage.
DynamicCombo( D_SRGB_TRANSFER, 0..2, Sys( ALL ) );

#if ( D_MSAA_COLOR )
	Texture2DMS< float4 > g_tCopyColor < Attribute( "CustomTexture1" ); SrgbRead( false ); >;
#elif !( D_MSAA )
	#if ( D_VOLUME_TEXTURE )
		Texture3D g_tCopyColor < Attribute( "CustomTexture1" ); SrgbRead( false ); >;
	#else
		Texture2D g_tCopyColor < Attribute( "CustomTexture1" ); SrgbRead( false ); >;
	#endif
#else
	Texture2DMS< float > g_tCopyColor < Attribute( "CustomTexture1" ); SrgbRead( false ); >;
#endif

RenderState( ColorWriteEnable0, RGBA );
RenderState( DepthEnable, false );
RenderState( DepthWriteEnable, false );

#if ( D_SRGB_TRANSFER == 2 )
	// Replace color, alpha is UI coverage over what was there
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ZERO );
	RenderState( SrcBlendAlpha, ONE );
	RenderState( DstBlendAlpha, INV_SRC_ALPHA );
#else
	RenderState( BlendEnable, false );
#endif

float4 g_vColorTint < Attribute( "ColorTint" ); Default4( 1.0, 1.0, 1.0, 1.0 ); >;
bool FlipX < Attribute( "FlipX" );>;
bool FlipY < Attribute( "FlipY" );>;

float g_flTexCoordW < Attribute( "VolumeSliceTexCoord" ); Default( 0.0f ); >;
float2 SrcViewportOffset < Attribute( "SrcViewportOffset" ); Default2( 0.0f, 0.0f ); >;

struct PS_OUTPUT
{
	float4 vColor : SV_Target0;
};

[shader( "pixel" )] PS_OUTPUT MainPs( PS_INPUT i )
{
	PS_OUTPUT o;

	float2 vScreenUv = CalculateViewportUv( i.vPositionSs.xy );

	if ( FlipX ) i.vTexCoords.x = 1 - i.vTexCoords.x;
	if ( FlipY ) i.vTexCoords.y = 1 - i.vTexCoords.y;

	float4 vColorOutput = 0;

	{
		// Color
		#if ( D_MSAA_COLOR )
		{
			vColorOutput = g_tCopyColor.Load( int2( i.vPositionSs.xy + SrcViewportOffset ), 0 );
		}
		#elif !( D_MSAA )
		{
			#if ( D_VOLUME_TEXTURE )
			{
				vColorOutput.rgba = g_tCopyColor.Sample( g_sPointWrap, float3( i.vTexCoords.xy, g_flTexCoordW ).xyz );
			}
			#else
			{
				vColorOutput.rgba = g_tCopyColor.Sample( g_sPointWrap, i.vTexCoords.xy );
			}
			#endif
		}
		#else
		{
			float2 uv = (i.vPositionSs.xy + SrcViewportOffset).xy;

			if ( FlipX ) uv.x = 1 - uv.x;
			if ( FlipY ) uv.y = 1 - uv.y;

			// Todo: MSAA blit's only really used on Hi-Z depth, don't bother with texcoord sampling for now
			vColorOutput.rgba = g_tCopyColor.Load( uv, 0 );
		}
		#endif // D_MSAA

		// Tint
		vColorOutput.rgb *= g_vColorTint.rgb;
	}

	#if ( D_SRGB_TRANSFER == 1 )
		vColorOutput = float4( SrgbLinearToGamma( vColorOutput.rgb ), 0.0 );
	#elif ( D_SRGB_TRANSFER == 2 )
		clip( vColorOutput.a > 0.0 ? 1.0 : -1.0 );
		vColorOutput.rgb = SrgbGammaToLinear( vColorOutput.rgb );
	#endif

	o.vColor.rgba = vColorOutput.rgba;
	return o;
}

#endif // PS

