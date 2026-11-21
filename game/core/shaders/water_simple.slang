// water_simple: a surface, the whole shader for Borealis.Gpu (docs/reference/borealis-slang-materials.md).
import sbox.surface;

// What the material compiler makes of a .vmat's images (SlangMaterialCompiler): the common inputs' textures
#if MATERIAL_INPUTS
// The material editor's FEATURES lines, as the .shader declared them
#include "common/features.hlsl"
// The material's parameters as the material editor shows them, with the feature states that use them
float3 g_flTintColor < UiType( Color ); Default3( 1, 1, 1 ); UiGroup( "Material,10/90" ); >;
float RefractionNormalScale < Default( 0.5 ); Range( 0.01, 1 ); UiGroup( "Refraction" ); >;
float Translucency < Default( 1 ); Range( 0, 1 ); UiGroup( "Refraction" ); >;
float Refraction < Default( 1 ); Range( 0, 1 ); UiGroup( "Water" ); >;
float BigWaveSize < Default( 0.5 ); Range( 0, 1 ); UiGroup( "Wave" ); >;
float BigWaveScale < Default( 100 ); Range( 0, 256 ); UiGroup( "Wave" ); >;
float BigWaveTime < Default( 1 ); Range( 0, 10 ); UiGroup( "Wave" ); >;
float NormalScale < Default( 0.5 ); Range( 0, 2 ); UiGroup( "Tweaks" ); >;
float g_fRoughness < Default( 0.02 ); Range( 0.01, 1 ); UiGroup( "Tweaks" ); >;
float g_fMetalness < Default( 0.85 ); Range( 0.01, 1 ); UiGroup( "Tweaks" ); >;
float g_fAmbientOcclusion < Default( 0.5 ); Range( 0.01, 1 ); UiGroup( "Tweaks" ); >;
bool g_bFogEnabled < Default( 1 ); UiGroup( "Fog,300" ); >;
BoolAttribute( NeedsLightProbe, true );
BoolAttribute( CanBatchWithDynamicShaderConstants, true );
// End of the material editor's lines
#include "common/utils/Material.CommonInputs.hlsl"
#endif

Texture2D g_RefractionTexture < Attribute("RefractionTexture");   SrgbRead( true ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( CLAMP );     AddressV( CLAMP ); >;
SamplerState g_RefractionTexture_sampler < Attribute("RefractionTexture");   SrgbRead( true ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( CLAMP );     AddressV( CLAMP ); >;
Texture2D g_ReflectionTexture < Attribute("ReflectionTexture");   SrgbRead( true ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( CLAMP );     AddressV( CLAMP ); >;
SamplerState g_ReflectionTexture_sampler < Attribute("ReflectionTexture");   SrgbRead( true ); Filter(MIN_MAG_MIP_LINEAR);    AddressU( CLAMP );     AddressV( CLAMP ); >;
bool g_bRefraction < Default(0.0f); Attribute( "HasRefractionTexture" ); > ;
bool g_bReflection < Default(0.0f); Attribute( "HasReflectionTexture" ); > ;

[Material]
public struct WaterSimple : ICompositeSurface
{
	[Group( "Material" ), Srgb, Opacity] Texture2D Color;
	[Group( "Material" )] Texture2D Normal;
	[Group( "Material" )] Texture2D Rma;
	[Group( "Material" ), Default3( 1, 1, 1 )] float3 TintColor;
	[Default( 0.5f ), Range( 0.01f, 1.0f ), Group( "Refraction" )] float RefractionNormalScale;
	[Default( 1.0f ), Range( 0.0f, 1.0f ), Group( "Refraction" )] float Translucency;
	[Default( 0.5f ), Range( 0.01f, 1.0f ), Group( "Reflection" )] float RelectionNormalScale;
	[Default( 1.0f ), Range( 0.0f, 1 ), Group( "Water" )] float Refraction;
	[Default( 0.5f ), Range( 0.0f, 1 ), Group( "Wave" )] float BigWaveSize;
	[Default( 100.0f ), Range( 0.0f, 256 ), Group( "Wave" )] float BigWaveScale;
	[Default( 1.0f ), Range( 0.0f, 10 ), Group( "Wave" )] float BigWaveTime;
	[Default( 0.5f ), Range( 0.0f, 2 ), Group( "Tweaks" )] float NormalScale;
	[Default( 0.02f ), Range( 0.01f, 1.0f ), Group( "Tweaks" )] float Roughness;
	[Default( 0.85f ), Range( 0.01f, 1.0f ), Group( "Tweaks" )] float Metalness;
	[Default( 0.5f ), Range( 0.01f, 1.0f ), Group( "Tweaks" )] float AmbientOcclusion;

	Material GetWave( SurfaceInput ii, float waveTime, float waveScale, float scale )
	{

		SurfaceInput i = ii;



		float3 worldPos = i.Camera + ( i.Position - i.Camera );

		

		worldPos.x += sin( (worldPos.x / 12) + (i.Time * 16) ) * 0.1;

		worldPos.y += cos( (worldPos.y / 12) + (i.Time * 22) ) * 0.1;



		worldPos /= scale;



		worldPos.x += sin( (worldPos.x ) + (i.Time * waveTime) ) * waveScale;

		worldPos.y += cos( (worldPos.y ) + (i.Time * waveTime) ) * waveScale;



		i.Uv = worldPos.xy;



		Material m = Material.FromCommonInputs( i, Color, Normal, Rma, TintColor );



		return m;

	}

	public Material Evaluate( SurfaceInput i )
	{

		Material a = GetWave( i, BigWaveTime, BigWaveSize, BigWaveScale );

		Material b = GetWave( i, BigWaveTime, BigWaveSize, BigWaveScale * -1 );



		Material m = Material.Lerp( a, b, 0.5 ); // could do this with big noise to reduce tiling?



		float3 camdir = normalize( i.Camera - ( ( i.Position - i.Camera ) + i.Camera ) );



		// scale normals, artist adjustment

		m.Normal = lerp( i.Normal, m.Normal, NormalScale );



		float fresnel = pow( 1.0 - dot( ( m.Normal ), camdir ), 5 );

	

		m.Opacity = 1;

		m.Roughness = Roughness;

		m.Metalness = Metalness;

		m.AmbientOcclusion = AmbientOcclusion;



		// let depth write



		// 

		// Add reflection

		//

		if ( g_bReflection )

        {

			float2 distortion = m.Normal.xy;



            float2 vPositionSs = i.ScreenPosition.xy;

			float2 uv = i.ScreenUv; 

			uv += distortion * 0.1;

			float3 col = g_ReflectionTexture.Sample(g_ReflectionTexture_sampler, uv).rgb;



			m.Emission.rgb += col * fresnel;

        }



		return m;
	}

	public float4 Composite( float4 shaded, Material m, SurfaceInput i )
	{
		float3 camdir = normalize( i.Camera - ( ( i.Position - i.Camera ) + i.Camera ) );
		float fresnel = pow( 1.0 - dot( ( m.Normal ), camdir ), 5 );
		float4 outCol = shaded;


		//

		// Add refraction

		//

		if ( g_bRefraction )

		{

			float colorSplit = 1;

			float2 uv = i.ScreenUv; 

			uv += -m.Normal.xy * Refraction * 0.1 * RefractionNormalScale;



           	float3 col = g_RefractionTexture.SampleLevel( g_RefractionTexture_sampler, uv, 0 ).rgb;

			outCol.rgb = lerp( outCol.rgb, col, (1-fresnel) * Translucency );

		}



		return outCol;

	}
}
