HEADER
{
	DevShader = true;
	Description = "YUV 4:2:0 to RGB compute resolve. Writes directly to a UAV, avoiding a render pass.";
}

MODES
{
	Default();
}

FEATURES
{
}

COMMON
{
	#include "system.fxc"
}

CS
{
	Texture2D<float>    g_tTextureY       < Attribute( "TextureY" ); >;
	Texture2D<float>    g_tTextureU       < Attribute( "TextureU" ); >;
	Texture2D<float>    g_tTextureV       < Attribute( "TextureV" ); >;
	Texture2D<float>    g_tTextureAlpha   < Attribute( "TextureAlpha" ); >;  // VP9 alpha Y-plane (I8)
	RWTexture2D<float4> g_tOutputRGB      < Attribute( "OutputRGB" ); >;

	// 0 = full-range (JPEG/PC swing: Y/Cb/Cr in [0..255])
	// 1 = limited-range (studio/TV swing: Y in [16..235], Cb/Cr in [16..240])
	DynamicCombo( D_LIMITED_RANGE, 0..1, Sys( ALL ) );

	// 0 = ITU-R BT.601 (SD content, 576i/480i)
	// 1 = ITU-R BT.709 (HD content, 720p/1080p/4K)
	DynamicCombo( D_BT709, 0..1, Sys( ALL ) );

	// 0 = opaque output (alpha = 1.0)
	// 1 = read per-pixel alpha from g_tTextureAlpha (VP9 transparent WebM)
	DynamicCombo( D_ALPHA, 0..1, Sys( ALL ) );

	[numthreads( 8, 8, 1 )]
	void MainCs( uint3 vThreadId : SV_DispatchThreadID )
	{
		uint2 vDim;
		g_tOutputRGB.GetDimensions( vDim.x, vDim.y );
		if ( vThreadId.x >= vDim.x || vThreadId.y >= vDim.y )
			return;

		// Y is full-resolution; U and V are half-resolution (4:2:0 subsampling).
		// Clamp UV coordinates to the actual chroma texture dimensions. The C++ side creates
		// U/V textures with floor(W/2) x floor(H/2) dimensions, so for odd luma sizes the
		// last thread column/row would produce an out-of-bounds Load without this clamp.
		uint2 vUVDim;
		g_tTextureU.GetDimensions( vUVDim.x, vUVDim.y );
		int2 vUVCoord = (int2)min( vThreadId.xy >> 1, vUVDim - 1 );

		float Y = g_tTextureY.Load( int3( vThreadId.xy, 0 ) ).r;
		float U = g_tTextureU.Load( int3( vUVCoord, 0 ) ).r;
		float V = g_tTextureV.Load( int3( vUVCoord, 0 ) ).r;

		float R, G, B;
		#if ( D_LIMITED_RANGE && D_BT709 )
		{
			// ITU-R BT.709 limited-range (studio/TV swing). Used by HD H.264/H.265.
			// Y ∈ [16/255, 235/255], Cb/Cr ∈ [16/255, 240/255].
			float Yp = Y - 16.0f / 255.0f;
			float Up = U - 128.0f / 255.0f;
			float Vp = V - 128.0f / 255.0f;
			R = 1.16438f * Yp + 1.79274f * Vp;
			G = 1.16438f * Yp - 0.21325f * Up - 0.53291f * Vp;
			B = 1.16438f * Yp + 2.11240f * Up;
		}
		#elif ( D_BT709 )
		{
			// ITU-R BT.709 full-range (JPEG/PC swing).
			// Y/Cb/Cr ∈ [0/255, 255/255].
			R = Y + 1.5748f  * ( V - 0.5f );
			G = Y - 0.18732f * ( U - 0.5f ) - 0.46812f * ( V - 0.5f );
			B = Y + 1.8556f  * ( U - 0.5f );
		}
		#elif ( D_LIMITED_RANGE )
		{
			// ITU-R BT.601 limited-range (studio/TV swing). Used by SD content.
			// Y ∈ [16/255, 235/255], Cb/Cr ∈ [16/255, 240/255].
			// Coefficients: Y scale = 255/219, chroma scaled by 255/224.
			float Yp = Y - 16.0f / 255.0f;
			float Up = U - 128.0f / 255.0f;
			float Vp = V - 128.0f / 255.0f;
			R = 1.16438f * Yp + 1.59603f * Vp;
			G = 1.16438f * Yp - 0.39176f * Up - 0.81297f * Vp;
			B = 1.16438f * Yp + 2.01723f * Up;
		}
		#else
		{
			// ITU-R BT.601 full-range (JPEG/PC swing).
			// Y/Cb/Cr ∈ [0/255, 255/255].
			R = Y + 1.402f   * ( V - 0.5f );
			G = Y - 0.34414f * ( U - 0.5f ) - 0.71414f * ( V - 0.5f );
			B = Y + 1.772f   * ( U - 0.5f );
		}
		#endif

		// Alpha: read from dedicated alpha texture (VP9 transparent WebM), or default to 1.
		#if D_ALPHA
			float fAlpha = g_tTextureAlpha.Load( int3( vThreadId.xy, 0 ) ).r;
		#else
			float fAlpha = 1.0f;
		#endif

		g_tOutputRGB[ vThreadId.xy ] = float4( R, G, B, fAlpha );
	}
}
