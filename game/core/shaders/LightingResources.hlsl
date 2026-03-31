// Copyright (c) Facepunch

#ifndef LIGHTING_HLSL
#define LIGHTING_HLSL

// Descriptor Set Lightbinner
// Everything in this set is per scene/view, nothing resets per mesh/material
// 0 - 
// 1 - 
// 2 - Local Lights
// 3 - Envmaps
// 4 - Decals
// 5 - Tiled Culling ( Lights, envmaps, decals, etc. )

// Per Scene:
// StructuredBuffer<DirectionalLight>

// Per View:
// InstanceData
// TileGridWidth
// StructuredBuffer<PointLight>
// StructuredBuffer<SpotLight>
// StructuredBuffer<ProjectedShadow>
// StructuredBuffer<DirectionalLightShadow>
// Append Area Lights as fit:
// StructuredBuffer<RectLight>
// StructuredBuffer<CapsuleLight>

#endif