#ifndef MATERIAL_COMMON_INPUTS_HLSL
#define MATERIAL_COMMON_INPUTS_HLSL

#include "common/utils/normal.hlsl"

CreateInputTexture2D(TextureColor, Srgb, 8, "", "_color", "Material,10/10", Default3(1.0, 1.0, 1.0));
CreateInputTexture2D(TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Material,10/20", Default3(0.5, 0.5, 1.0));
CreateInputTexture2D(TextureRoughness, Linear, 8, "", "_rough", "Material,10/30", Default(0.5));
CreateInputTexture2D(TextureMetalness, Linear, 8, "", "_metal", "Material,10/40", Default(1.0));
CreateInputTexture2D(TextureAmbientOcclusion, Linear, 8, "", "_ao", "Material,10/50", Default(1.0));
CreateInputTexture2D(TextureBlendMask, Linear, 8, "", "_blend", "Material,10/60", Default(1.0));
CreateInputTexture2D(TextureTranslucency, Linear, 8, "", "_trans", "Material,10/70", Default3(1.0, 1.0, 1.0));
CreateInputTexture2D(TextureTintMask, Linear, 8, "", "_tint", "Material,10/70", Default(1.0));

float3  g_flTintColor      < Default3( 1.0, 1.0, 1.0);  UiGroup("Material,10/90");  UiType(Color); > ;
float   g_flSelfIllumScale < Default ( 1.0 );           UiGroup("Material,10/91");  Range(0.0, 16.0); >;

Texture2D g_tColor < Channel(RGB, AlphaWeighted(TextureColor, TextureTranslucency), Srgb); Channel(A, Box(TextureTranslucency), Linear); OutputFormat(BC7); SrgbRead(true); > ;
Texture2D g_tNormal < Channel(RGB, Box(TextureNormal), Linear); Channel(A, Box(TextureTintMask), Linear); OutputFormat(BC7); SrgbRead(false); > ;
Texture2D g_tRma < Channel(R, Box(TextureRoughness), Linear); Channel(G, Box(TextureMetalness), Linear); Channel(B, Box(TextureAmbientOcclusion), Linear); Channel(A, Box(TextureBlendMask), Linear); OutputFormat(BC7); SrgbRead(false); > ;

// Texture Material::Opacity comes from; alpha-to-coverage needs its size for mip compensation.
#define MATERIAL_ALPHA_TEXTURE g_tColor

// For VRAD3
TextureAttribute(LightSim_DiffuseAlbedoTexture, g_tColor);
TextureAttribute(RepresentativeTexture, g_tColor);

BoolAttribute( DoNotCastShadows, F_DO_NOT_CAST_SHADOWS ? true : false );
BoolAttribute( SupportsMappingDimensions, true );
BoolAttribute( renderbackfaces, F_RENDER_BACKFACES ? true : false );

#ifndef CUSTOM_TEXTURE_FILTERING
    SamplerState TextureFiltering < Filter((F_TEXTURE_FILTERING == 0 ? ANISOTROPIC : (F_TEXTURE_FILTERING == 1 ? BILINEAR : (F_TEXTURE_FILTERING == 2 ? TRILINEAR : (F_TEXTURE_FILTERING == 3 ? POINT : NEAREST))))); MaxAniso(8); > ;
#endif

#endif