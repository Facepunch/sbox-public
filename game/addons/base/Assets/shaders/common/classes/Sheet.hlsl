#ifndef SHEET_HLSL
#define SHEET_HLSL

#include "sheet_sampling.fxc"

//
// garry: I don't want people having to define this shit in their shaeders when it's a backend, built in thing.
//		  My assumption is that if this isn't used, it'll get compiled out and no harm done.
//		  If I broke shaders that DO define this then I fucked it sorry - remove the definition in their file
//		   whtever you do, don't start a fucking #IF SHEET_TEXTUERRE_DEFINED thing 
//
Texture2D g_SheetTexture < Attribute( "SheetTexture" ); SrgbRead( false ); >;

//
// Public Sheet Api.
//
struct Sheet
{

	static bool Blended( in float4 data, in float sequence, in float time, in float2 uv, out float2 a, out float2 b, out float blend )
	{
		a = uv;
		b = uv;
		blend = 0;

		if ( data.w == 0 )
			return false;

		SheetDataSamplerParams_t params;
		params.m_flSheetTextureBaseV = data.x;
		params.m_flOOSheetTextureWidth = 1.0f / data.y;
		params.m_flOOSheetTextureHeight = data.z;
		params.m_flSheetTextureWidth = data.y;
		params.m_flSheetSequenceCount = data.w;
		params.m_flSequenceAnimationTimescale = 1.0f;
		params.m_flSequenceIndex = fmod( sequence, data.w );
		params.m_flSequenceAnimationTime = time;

		SheetDataSamplerOutput_t o = SampleSheetData( g_sPointWrap, g_SheetTexture, params, false);
	
		o.m_vFrame0Bounds.zw -= o.m_vFrame0Bounds.xy;
		a = o.m_vFrame0Bounds.xy + ( uv * o.m_vFrame0Bounds.zw );

		o.m_vFrame1Bounds.zw -= o.m_vFrame1Bounds.xy;
		b = o.m_vFrame1Bounds.xy + ( uv * o.m_vFrame1Bounds.zw );
		
		blend = o.m_flAnimationBlendValue;

		return true;
	}

	//
	// Most basic implementation. Get the bounds for a single
	//
	static float2 Single( float4 data, float sequence, float time, in float2 uv )
	{
		if ( data.w == 0 ) return uv;

		float2 a;
		float2 b;
		float blend;

		Sheet::Blended( data, sequence, time, uv, a, b, blend );

		if ( blend > 0.5 ) return b;

		return a;
	}

};

#endif //SHEET_HLSL