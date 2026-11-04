#include "common/features.hlsl"
Feature( F_SOURCE_LIGHTMAP, 0..1, "Source 1" );
Feature( F_SOURCE_LIGHTMAP_HDR, 0..1, "Source 1" );
Feature( F_SOURCE_PROP_LIGHTING, 0..1, "Source 1" );
Feature( F_SOURCE_ALPHA_SINE, 0..1, "Source 1" );
Feature( F_SOURCE_PROXIES, 0..1, "Source 1" );
Feature( F_SOURCE_TEXTURE_ANIMATION, 0..1, "Source 1" );
Feature( F_SOURCE_FRAMEBUFFER, 0..1, "Source 1" );
Feature( F_ALPHA_TEST, 0..1, "Transparency" );
Feature( F_TRANSLUCENT, 0..1, "Transparency" );
FeatureRule( Requires1( F_SOURCE_LIGHTMAP_HDR, F_SOURCE_LIGHTMAP ), "HDR lightmaps require baked lighting" );
FeatureRule( Requires1( F_SOURCE_FRAMEBUFFER, F_TRANSLUCENT ), "Framebuffer surfaces require translucency" );
FeatureRule( Requires1( F_ADDITIVE_BLEND, F_TRANSLUCENT ), "Additive requires translucency" );
