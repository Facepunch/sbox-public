#!/bin/sh
# Compile the sample's HLSL to SPIR-V with DXC: the repo's (src/thirdparty/dxc, downloaded by the native build) or
# the Vulkan SDK's. Vertex shaders invert Y as s&box's do (Slang's VulkanInvertY). The .spv files are checked in.
set -e
cd "$(dirname "$0")"
DXC="../../../../src/thirdparty/dxc/bin/linuxsteamrt64/dxc"
[ -x "$DXC" ] || DXC="$(command -v dxc || true)"
[ -n "$DXC" ] || { echo "No dxc: run the native build or install the Vulkan SDK"; exit 1; }
FLAGS="-spirv -fspv-target-env=vulkan1.3 -O3 -HV 2021"
for s in triangle mesh; do
	"$DXC" $FLAGS -T vs_6_6 -E MainVs -fvk-invert-y -Fo $s.vs.spv $s.hlsl
	"$DXC" $FLAGS -T ps_6_6 -E MainPs -Fo $s.ps.spv $s.hlsl
done
"$DXC" $FLAGS -T cs_6_6 -E MainCs -Fo pattern.cs.spv pattern.hlsl
"$DXC" $FLAGS -T cs_6_6 -E MainCs -Fo slots.cs.spv slots.hlsl
echo Compiled
