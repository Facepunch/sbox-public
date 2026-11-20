FEATURES
{
    #include "common/features.hlsl"
}

MODES
{
    Forward();
    Depth();
}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
    #include "common/pixel.hlsl"

	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default4( 1.0, 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Material,10/15", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( TextureRoughness, Linear, 8, "", "_rough", "Material,10/20", Default( 0.5 ) );
	CreateInputTexture2D( TextureEmission, Srgb, 8, "", "_emit", "Material,10/25", Default4( 0.0, 0.0, 0.0, 0.0 ) );

	Texture2D g_tColorMap < Channel( RGBA, Box( TextureColor ), Srgb ); SrgbRead( true ); OutputFormat( BC7 ); >;
	Texture2D g_tNormalMap < Channel( RGB, Box( TextureNormal ), Linear ); SrgbRead( false ); OutputFormat( BC7 ); >;
	Texture2D g_tRoughnessMap < Channel( RGBA, Box( TextureRoughness ), Linear ); SrgbRead( false ); OutputFormat( BC7 ); >;
	Texture2D g_tEmissionMap < Channel( RGBA, Box( TextureEmission ), Srgb ); SrgbRead( true ); OutputFormat( BC7 ); >;

	SamplerState g_sSampler0 < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	float g_flEmissionPower < UiGroup( "Material,10/30" ); Default1( 1.0 ); Range1( 0, 10 ); >;

	Texture2D g_tSelfIllumMask < Attribute( "Emissive" ); >;
	Texture2D g_tGraphData < Attribute( "GraphData" ); >;

	float4 g_vGrid < Attribute( "Grid" ); Default4( 8, 5, 0.1, 0 ); >;
	float4 g_vGraphInfo < Attribute( "GraphInfo" ); Default4( 128, 0, 0, 0 ); >;

	float4 g_vCh1Color < Attribute( "Ch1Color" ); Default4( 0, 1, 1, 1 ); >;
	float4 g_vCh2Color < Attribute( "Ch2Color" ); Default4( 1, 1, 0, 1 ); >;
	float4 g_vCh3Color < Attribute( "Ch3Color" ); Default4( 0, 0.8, 0.2, 1 ); >;

	float4 g_vBand1 < Attribute( "Band1" ); Default4( 0.0, 0.33, 0, 0 ); >;
	float4 g_vBand2 < Attribute( "Band2" ); Default4( 0.33, 0.33, 0, 0 ); >;
	float4 g_vBand3 < Attribute( "Band3" ); Default4( 0.66, 0.34, 0, 0 ); >;

	float DrawGrid( float2 uv, float divisionsX, float divisionsY, float brightness )
	{
		float lineWidth = 0.01;

		float gridX = abs( frac( uv.x * divisionsX + 0.5 ) - 0.5 );
		float lineX = step( gridX, lineWidth * divisionsX );

		float gridY = abs( frac( uv.y * divisionsY + 0.5 ) - 0.5 );
		float lineY = step( gridY, lineWidth * divisionsY );

		return max( lineX, lineY ) * brightness;
	}

	float ReadSample( float sampleX, int channel )
	{
		// Read from row 0 of the texture (data row)
        float2 dataUV = float2(sampleX, 0.005);
        float3 data = g_tGraphData.Sample(g_sTrilinearClamp, dataUV ).rgb;
		if ( channel == 0 ) return data.r;
		if ( channel == 1 ) return data.g;
		return data.b;
	}

	float DrawChannel( float2 uv, int channel, float bandTop, float bandHeight, float sampleCount )
	{
		float pixelStep = 1.0 / sampleCount;

		// Current and previous sample
		float val = ReadSample( uv.x, channel );
		float prevVal = ReadSample( uv.x - pixelStep, channel );

		float lineY = bandTop + bandHeight * ( 1.0 - val );
		float prevLineY = bandTop + bandHeight * ( 1.0 - prevVal );

		float thickness = 0.012;
		float yAA = max( fwidth( uv.y ), 1e-5 );

		// Horizontal segment at current value
		float hLine = smoothstep( thickness + yAA, thickness - yAA, abs( uv.y - lineY ) );

		// Fill between previous and current value (vertical connector)
		float minY = min( prevLineY, lineY );
		float maxY = max( prevLineY, lineY );
		float localX = frac( uv.x * sampleCount );

		float xAA = max( fwidth( uv.x * sampleCount ), 1e-5 );
		float xMask = smoothstep( -xAA, xAA, localX ) * ( 1.0 - smoothstep( 0.12 - xAA, 0.12 + xAA, localX ) );

		float yMin = minY - thickness;
		float yMax = maxY + thickness;
		float yMask = smoothstep( yMin - yAA, yMin + yAA, uv.y ) * ( 1.0 - smoothstep( yMax - yAA, yMax + yAA, uv.y ) );
		float vLine = xMask * yMask;

		return max( hLine, vLine );
	}


	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::From( i );

		float2 uv = i.vTextureCoords.xy;
		float sampleCount = g_vGraphInfo.x;
		float3 lcd = float3( 0, 0, 0 );

		// Dot grid overlay
		float gridVal = DrawGrid( uv, g_vGrid.x, g_vGrid.y, g_vGrid.z );
		lcd += float3( gridVal * 0.05, gridVal * 0.05, gridVal * 0.05 );

		// Plot channels
		float ch1 = DrawChannel( uv, 0, g_vBand1.x, g_vBand1.y, sampleCount );
		lcd += g_vCh1Color.rgb * g_vCh1Color.a * ch1;

		float ch2 = DrawChannel( uv, 1, g_vBand2.x, g_vBand2.y, sampleCount );
		lcd += g_vCh2Color.rgb * g_vCh2Color.a * ch2;

		float ch3 = DrawChannel( uv, 2, g_vBand3.x, g_vBand3.y, sampleCount );
		lcd += g_vCh3Color.rgb * g_vCh3Color.a * ch3;

		// LCD pixelation effect
        float2 lcdRes = float2(64, 48);
        g_tSelfIllumMask.GetDimensions(lcdRes.x, lcdRes.y);
        float2 cellUv = frac(uv * lcdRes);

        float2 cellScreenSize = fwidth(uv * lcdRes);
        float lcdFade = 1.0 - saturate(max(cellScreenSize.x, cellScreenSize.y) * 2.0);

        // Painted overlay from render target
        float3 overlay = g_tSelfIllumMask.Sample(g_sTrilinearClamp, uv).rgb;
        float3 overlayPoint = g_tSelfIllumMask.Sample(g_sPointClamp, uv).rgb;
		
		lcd += lerp( overlay, overlayPoint, lcdFade ) + 0.002;

		// Save pre-LCD color for distance blending
		float3 rawLcd = lcd;

		// Each pixel cell has 3 vertical RGB sub-pixel columns
		float subX = cellUv.x * 3.0;
		float subLocal = frac( subX );
		float subIdx = floor( subX );

		// Rounded shape with visible gaps between columns and rows
		float subMask = smoothstep( 0.0, 0.2, subLocal ) * smoothstep( 1.0, 0.8, subLocal );
		float rowMask = smoothstep( 0.0, 0.15, cellUv.y ) * smoothstep( 1.0, 0.85, cellUv.y );
		float shape = subMask * rowMask;

		// Tint each sub-pixel strip to its primary color, slight bleed to neighbors
		float3 subColor;
		subColor.r = ( subIdx == 0 ) ? 1.0 : 0.06;
		subColor.g = ( subIdx == 1 ) ? 1.0 : 0.06;
		subColor.b = ( subIdx == 2 ) ? 1.0 : 0.06;

		// Apply LCD
		lcd *= subColor * shape * 5.8;

		// Fade out grid at distance
		lcd = lerp( rawLcd, lcd, lcdFade );

		float3 viewDir = normalize( g_vCameraPositionWs.xyz - ( i.vPositionWithOffsetWs.xyz + g_vHighPrecisionLightingOffsetWs.xyz ) );
		float NdotV = saturate( dot( normalize( i.vNormalWs ), viewDir ) );

		// Sharp falloff
		float angleDim = pow( NdotV, 10.0f );

		// At extreme angles, colors wash out toward a dim bluish backlight tint
		float3 washout = float3( 0.008, 0.008, 0.012 ) + ( float3( lcd.b, lcd.r, lcd.g ) * 0.05 );
		lcd = lerp( washout, lcd, angleDim );

		m.Albedo = Tex2DS( g_tColorMap, g_sSampler0, i.vTextureCoords.xy ).rgb;
		m.Normal = TransformNormal( DecodeNormal( Tex2DS( g_tNormalMap, g_sSampler0, i.vTextureCoords.xy ).rgb ), i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.Emission = Tex2DS( g_tEmissionMap, g_sSampler0, i.vTextureCoords.xy ).rgb + lcd * g_flEmissionPower;
		m.Roughness = Tex2DS( g_tRoughnessMap, g_sSampler0, i.vTextureCoords.xy ).r;
		m.Metalness = 0.0;

		return ShadingModelStandard::Shade( i, m );
	}
}