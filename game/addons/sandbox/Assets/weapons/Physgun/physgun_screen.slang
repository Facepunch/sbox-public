// physgun_screen: the physgun's LCD, a surface. Its scope traces (the object's GraphData texture, three channels in
// bands) over a dot grid, with what the weapon paints (the object's Emissive render target) on top, drawn as RGB
// sub-pixel cells that fade to plain colour with distance and wash out at grazing angles, lit as emission over the
// display's own colour, normal and roughness. The weapon sets the traces' layout and colours as object attributes
// (Physgun.Effects). Was physgun-screen.shader; fwidth is the uv's screen gradients.
import sbox.surface;

[Material, Features( "Common" ), Vertex( "Common" )]
public struct PhysgunScreen : ISurface
{
	[Sampler( "Anisotropic", "Wrap" )] SamplerState ScreenFiltering;
	[Sampler( "Trilinear", "Clamp" )] SamplerState ScreenTrilinearClamp;
	[Sampler( "Point", "Clamp" )] SamplerState ScreenPointClamp;

	[Srgb, InputWith( "TextureColor", "Material,10/10", "Srgb; _color; Default4( 1.0, 1.0, 1.0, 1.0 )" ), Pack( "RGBA: Color", "BC7" )] Texture2D ColorMap;
	[InputWith( "TextureNormal", "Material,10/15", "NormalizeNormals; _normal; Default3( 0.5, 0.5, 1.0 )" ), Pack( "RGB: Normal", "BC7" )] Texture2D NormalMap;
	[InputWith( "TextureRoughness", "Material,10/20", "Linear; _rough; Default( 0.5 )" ), Pack( "RGBA: Roughness", "BC7" )] Texture2D RoughnessMap;
	[Srgb, InputWith( "TextureEmission", "Material,10/25", "Srgb; _emit; Default4( 0.0, 0.0, 0.0, 0.0 )" ), Pack( "RGBA: Emission", "BC7" )] Texture2D EmissionMap;
	[Group( "Material,10/30" ), Default( 1.0 ), Range( 0, 10 )] float EmissionPower;

	// What the weapon hands the screen
	[Engine( "Emissive" )] Texture2D g_tSelfIllumMask;
	[Engine( "GraphData" )] Texture2D g_tGraphData;
	[Engine( "Grid" ), Default4( 8, 5, 0.1, 0 )] float4 g_vGrid;
	[Engine( "GraphInfo" ), Default4( 128, 0, 0, 0 )] float4 g_vGraphInfo;
	[Engine( "Ch1Color" ), Default4( 0, 1, 1, 1 )] float4 g_vCh1Color;
	[Engine( "Ch2Color" ), Default4( 1, 1, 0, 1 )] float4 g_vCh2Color;
	[Engine( "Ch3Color" ), Default4( 0, 0.8, 0.2, 1 )] float4 g_vCh3Color;
	[Engine( "Band1" ), Default4( 0.0, 0.33, 0, 0 )] float4 g_vBand1;
	[Engine( "Band2" ), Default4( 0.33, 0.33, 0, 0 )] float4 g_vBand2;
	[Engine( "Band3" ), Default4( 0.66, 0.34, 0, 0 )] float4 g_vBand3;

	static float DrawGrid( float2 uv, float divisionsX, float divisionsY, float brightness )
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
		float2 dataUV = float2( sampleX, 0.005 );
		float3 data = g_tGraphData.Sample( ScreenTrilinearClamp, dataUV ).rgb;
		if ( channel == 0 ) return data.r;
		if ( channel == 1 ) return data.g;
		return data.b;
	}

	// uvWidth is fwidth( uv ): the uv's change across a pixel
	float DrawChannel( float2 uv, float2 uvWidth, int channel, float bandTop, float bandHeight, float sampleCount )
	{
		float pixelStep = 1.0 / sampleCount;

		// Current and previous sample
		float val = ReadSample( uv.x, channel );
		float prevVal = ReadSample( uv.x - pixelStep, channel );

		float lineY = bandTop + bandHeight * ( 1.0 - val );
		float prevLineY = bandTop + bandHeight * ( 1.0 - prevVal );

		float thickness = 0.012;
		float yAA = max( uvWidth.y, 1e-5 );

		// Horizontal segment at current value
		float hLine = smoothstep( thickness + yAA, thickness - yAA, abs( uv.y - lineY ) );

		// Fill between previous and current value (vertical connector)
		float minY = min( prevLineY, lineY );
		float maxY = max( prevLineY, lineY );
		float localX = frac( uv.x * sampleCount );

		float xAA = max( uvWidth.x * sampleCount, 1e-5 );
		float xMask = smoothstep( -xAA, xAA, localX ) * ( 1.0 - smoothstep( 0.12 - xAA, 0.12 + xAA, localX ) );

		float yMin = minY - thickness;
		float yMax = maxY + thickness;
		float yMask = smoothstep( yMin - yAA, yMin + yAA, uv.y ) * ( 1.0 - smoothstep( yMax - yAA, yMax + yAA, uv.y ) );
		float vLine = xMask * yMask;

		return max( hLine, vLine );
	}

	public Material Evaluate( SurfaceInput i )
	{
		float2 uv = i.Uv;
		float2 uvWidth = abs( i.UvDdx ) + abs( i.UvDdy );
		float sampleCount = g_vGraphInfo.x;
		float3 lcd = float3( 0, 0, 0 );

		// Dot grid overlay
		float gridVal = DrawGrid( uv, g_vGrid.x, g_vGrid.y, g_vGrid.z );
		lcd += float3( gridVal * 0.05, gridVal * 0.05, gridVal * 0.05 );

		// Plot channels
		float ch1 = DrawChannel( uv, uvWidth, 0, g_vBand1.x, g_vBand1.y, sampleCount );
		lcd += g_vCh1Color.rgb * g_vCh1Color.a * ch1;

		float ch2 = DrawChannel( uv, uvWidth, 1, g_vBand2.x, g_vBand2.y, sampleCount );
		lcd += g_vCh2Color.rgb * g_vCh2Color.a * ch2;

		float ch3 = DrawChannel( uv, uvWidth, 2, g_vBand3.x, g_vBand3.y, sampleCount );
		lcd += g_vCh3Color.rgb * g_vCh3Color.a * ch3;

		// LCD pixelation effect
		float2 lcdRes = float2( 64, 48 );
		g_tSelfIllumMask.GetDimensions( lcdRes.x, lcdRes.y );
		float2 cellUv = frac( uv * lcdRes );

		float2 cellScreenSize = uvWidth * lcdRes;
		float lcdFade = 1.0 - saturate( max( cellScreenSize.x, cellScreenSize.y ) * 2.0 );

		// Painted overlay from render target
		float3 overlay = g_tSelfIllumMask.Sample( ScreenTrilinearClamp, uv ).rgb;
		float3 overlayPoint = g_tSelfIllumMask.Sample( ScreenPointClamp, uv ).rgb;

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

		float NdotV = saturate( dot( normalize( i.Normal ), normalize( i.Camera - i.Position ) ) );

		// Sharp falloff
		float angleDim = pow( NdotV, 10.0f );

		// At extreme angles, colors wash out toward a dim bluish backlight tint
		float3 washout = float3( 0.008, 0.008, 0.012 ) + ( float3( lcd.b, lcd.r, lcd.g ) * 0.05 );
		lcd = lerp( washout, lcd, angleDim );

		Material m = Material.Init( i );
		m.Albedo = i.Sample( ColorMap, uv, ScreenFiltering ).rgb;
		m.Normal = TransformNormal( DecodeNormal( i.Sample( NormalMap, uv, ScreenFiltering ).rgb ), i.Normal, i.TangentU, i.TangentV );
		m.Emission = i.Sample( EmissionMap, uv, ScreenFiltering ).rgb + lcd * EmissionPower;
		m.Roughness = i.Sample( RoughnessMap, uv, ScreenFiltering ).r;
		m.Metalness = 0.0;
		return m;
	}
}
