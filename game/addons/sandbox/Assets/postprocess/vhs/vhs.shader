HEADER
{
	Description = "VHS playback";
}

MODES
{
	Default();
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

struct VertexInput
{
	float3 position : POSITION < Semantic( PosXyz ); >;
	float2 uv : TEXCOORD0 < Semantic( LowPrecisionUv ); >;
};

struct PixelInput
{
	float4 position : SV_Position;
	float2 uv : TEXCOORD0;
};

VS
{
	PixelInput MainVs( VertexInput input )
	{
		PixelInput output;
		output.position = float4( input.position.xy, 0, 1 );
		output.uv = input.uv;
		return output;
	}
}

PS
{
	#include "postprocess/common.hlsl"

	Texture2D SceneColor < Attribute( "ColorBuffer" ); SrgbRead( true ); >;
	float2 PictureScale < Attribute( "PictureScale" ); Default2( 1, 1 ); >;
	float TapeTime < Attribute( "TapeTime" ); >;
	float Tracking < Attribute( "Tracking" ); >;
	float TapeNoise < Attribute( "TapeNoise" ); >;
	float ColorBleed < Attribute( "ColorBleed" ); >;
	float Scanlines < Attribute( "Scanlines" ); >;
	float Strength < Attribute( "Strength" ); >;

	#define LUMA_WEIGHTS float3( 0.299, 0.587, 0.114 )

	float Noise( uint x, uint y )
	{
		uint h = x * 0x9e3779b9u + y * 0x85ebca6bu;
		h ^= h >> 16;
		h *= 0x7feb352du;
		h ^= h >> 15;
		return float( h >> 8 ) / 16777216.0;
	}

	float3 VideoSample( float2 uv )
	{
		uv = 0.5 + (uv - 0.5) * PictureScale;
		return SrgbLinearToGamma( saturate( SceneColor.SampleLevel( g_sBilinearClamp, uv, 0 ).rgb ) );
	}

	float Drift( float position, uint seed )
	{
		uint segment = (uint)position;
		return lerp( Noise( segment, seed ), Noise( segment + 1, seed ), smoothstep( 0, 1, frac( position ) ) ) * 2 - 1;
	}

	float Luma( float3 rgb )
	{
		return dot( rgb, LUMA_WEIGHTS );
	}

	float3 DecodeTape( float2 uv )
	{
		uint line = (uint)(uv.y * 480);
		uint field = (uint)floor( TapeTime * 59.94 );
		uint x = (uint)(uv.x * 640);
		float hiss = Noise( line, field );
		const float sampleWidth = 1.0 / 320.0;

		// Tracking drifts across lines, with a stronger kick at the bottom of the picture.
		float cycle = TapeTime / 4.1;
		float sweep = frac( cycle ) * lerp( 1.9, 2.6, Noise( (uint)cycle, 19 ) ) - 0.55;
		float band = (1 - smoothstep( 0.004, 0.049, abs( uv.y - sweep ) )) * Tracking;
		float head = smoothstep( 0.973, 0.998, uv.y );
		float coarse = Drift( line / 52.0 + TapeTime * 3.1, 41 );
		float fine = Drift( line / 17.0 + TapeTime * 6.7, 73 );
		float shift = Tracking * Tracking * (0.9 * coarse + 0.30 * fine + 0.10 * (hiss - 0.5));
		shift += band * Tracking * (2.4 + 1.6 * fine) + head * Tracking * (2 * coarse + hiss - 0.5);
		uv.x += shift * sampleWidth;

		// Soften luma and leave a small echo behind sharp edges.
		float3 center = VideoSample( uv );
		float2 tap = float2( sampleWidth * (0.65 + 1.5 * band), 0 );
		float soft = 0.44 * Luma( center ) + 0.28 * (Luma( VideoSample( uv - tap ) ) + Luma( VideoSample( uv + tap ) ));
		float y = soft + 0.13 * (soft - Luma( VideoSample( uv - 2.8 * tap ) )) * (1 - band);

		// Chroma arrives a little later than luma.
		float2 lag = float2( sampleWidth * ColorBleed, 0 );
		float3 color = 0.22 * VideoSample( uv + lag * 1.8 ) + 0.20 * center
			+ 0.40 * VideoSample( uv - lag * 2.2 ) + 0.18 * VideoSample( uv - lag * 6.2 );
		float2 chroma = color.rb - Luma( color );
		float phaseError = (hiss - 0.5) * (0.09 + 0.22 * band) * TapeNoise * ColorBleed;
		chroma += float2( -chroma.y, chroma.x ) * phaseError;

		float speckle = Noise( x + field * 47, line );
		float streak = Noise( (x + (uint)(hiss * 5)) / 5 + field * 29, line );
		float grain = lerp( speckle, streak, 0.55 ) - 0.5;
		float shadow = 1 - smoothstep( 0.10, 0.75, y );
		float colorNoise = Noise( x / 4 + field * 53, line ) - 0.5;
		chroma += float2( colorNoise, -0.65 * colorNoise ) * shadow * TapeNoise * 0.085;
		chroma *= 1 - 0.10 * ColorBleed - 0.50 * band;
		y += TapeNoise * (grain * (0.04 + 0.10 * shadow + 0.16 * band) + (hiss - 0.5) * 0.03);

		// Dropouts borrow luma from the line above.
		uint span = x + (uint)(hiss * 24);
		if ( TapeNoise > 0 && Noise( span / 24 + field * 131, line ) > 0.9995 - band * 0.05 )
		{
			float recovered = Luma( VideoSample( uv - float2( 0, 1.0 / 480.0 ) ) );
			float onset = saturate( 1 - (span % 24) / 4.0 );
			y = lerp( y, recovered + 0.24 * onset, TapeNoise );
		}

		float green = -dot( chroma, LUMA_WEIGHTS.rb ) / LUMA_WEIGHTS.g;
		float3 signal = saturate( y + float3( chroma.x, green, chroma.y ) );
		return SrgbGammaToLinear( signal );
	}

	float4 MainPs( PixelInput input ) : SV_Target0
	{
		float2 p = (input.uv * 2 - 1) / PictureScale;
		float2 curved = p * (1 + 0.012 * p.yx * p.yx);
		float2 uv = curved * 0.5 + 0.5;
		float3 crt = DecodeTape( saturate( uv ) );

		// Fade the scanlines when the screen resoluton is too low to draw them.
		float row = uv.y * 240;
		float resolved = 1 - smoothstep( 0.35, 1, fwidth( row ) );
		float beam = 0.5 + 0.5 * sin( row * 6.2831853 ) * resolved;
		crt *= 1 - Scanlines * beam * 0.44;
		crt *= 1 - 0.09 * dot( p, p );
		float edge = min( 1 - abs( curved.x ), 1 - abs( curved.y ) );
		crt *= smoothstep( 0, max( fwidth( edge ), 0.001 ), edge );

		float4 source = SceneColor.SampleLevel( g_sBilinearClamp, input.uv, 0 );
		return float4( lerp( source.rgb, crt, Strength ), source.a );
	}
}
