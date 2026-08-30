#include "vr_common_features.fxc"

Feature( F_TEXTURE_FILTERING, 0..4 ( 0="Anisotropic", 1="Bilinear", 2="Trilinear", 3="Point Sample", 4="Nearest Neighbour" ), "Texture Filtering" );
Feature( F_ADDITIVE_BLEND, 0..1, "Blending" );