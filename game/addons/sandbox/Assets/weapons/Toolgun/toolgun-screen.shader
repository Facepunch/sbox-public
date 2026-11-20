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
		// Add your vertex manipulation functions here
		return FinalizeVertex( o );
	}
}

PS
{
    #include "common/pixel.hlsl"

	Texture2D g_tSelfIllumMask < Attribute( "Emissive" ); >;
	CreateInputTexture2D( TextureAmbientOcclusion, Linear, 8, "", "_ao", "Material,10/Ambient Occlusion", Default( 1.0 ) );
	CreateInputTexture2D( TextureRoughness, Linear, 8, "", "_rough", "Material,10/Roughness", Default( 0.5 ) );
	CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Material,10/Normal", Default3( 0.5, 0.5, 1.0 ) );
	CreateTexture2D( g_tAo ) < Channel( R, Box( TextureAmbientOcclusion ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateTexture2D( g_tRoughness ) < Channel( R, Box( TextureRoughness ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	CreateTexture2D( g_tNormal ) < Channel( RGB, Box( TextureNormal ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;

		// One dot per pixel of the screen texture
		float2 grid;
		g_tSelfIllumMask.GetDimensions( grid.x, grid.y );

		// Sample emissive content
		float3 emissive = g_tSelfIllumMask.SampleLevel( g_sPointClamp, uv, 0 ).rgb;

		// Dot matrix cell coordinates
		float2 cell = frac( uv * grid );
		float2 cellCentered = cell - 0.5;

		float dotRadius = 0.5;
		float dist = length( cellCentered );
		float dotMask = 1.0 - smoothstep( dotRadius - 0.08, dotRadius + 0.08, dist );

		// Per-dot hemisphere normals in tangent space
		float2 nxy = cellCentered / dotRadius;
		float nzSq = saturate( 1.0 - dot( nxy, nxy ) );
		float3 domeNormalTs = normalize( float3( -nxy.x, nxy.y, sqrt( nzSq ) ) );

		float3 glassNormalTs = DecodeNormal( g_tNormal.Sample( g_sAniso, uv ).rgb );
		float bump = dotMask * 0.025;
		float3 normalTs = normalize( float3( glassNormalTs.xy + domeNormalTs.xy * bump, glassNormalTs.z * lerp( 1.0, domeNormalTs.z, bump ) ) );

		// Dome shading falloff
		float dome = 1.0 - smoothstep( 0.0, dotRadius, dist ) * 0.25;

		// LCD emissive color
		float3 lcdColor = emissive * dotMask * dome * 5.0f;

		// Scanline refresh sweep
		float scanline = sin( ( uv.y - g_flTime * 1.5 ) * 80.0 ) * 0.5 + 0.5;
		scanline = smoothstep( 0.3, 0.7, scanline );
		lcdColor *= lerp( 0.9, 1.0, scanline );

		// Horizontal row gaps
		float rowLine = smoothstep( 0.0, 0.06, abs( cellCentered.y ) );
		lcdColor *= lerp( 0.85, 1.0, rowLine );

		Material m = Material::From( i );
		m.Albedo = 0.0f;
		m.AmbientOcclusion = g_tAo.Sample( g_sAniso, uv ).r;
		m.Roughness = min( g_tRoughness.Sample( g_sAniso, uv ).r, lerp( 1.0, 0.25, dotMask ) );
		m.Normal = TransformNormal( normalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		m.Emission = lcdColor;

		return ShadingModelStandard::Shade( i, m );
	}
}
