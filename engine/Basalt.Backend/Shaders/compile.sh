#!/bin/sh
# Compile Basalt.Backend's HLSL to SPIR-V with DXC: the repo's (src/thirdparty/dxc, downloaded by the native build) or
# the Vulkan SDK's. Vertex shaders invert Y as s&box's do; constant buffers use D3D packing. The .spv files are checked in.
set -e
cd "$(dirname "$0")"
DXC="../../../src/thirdparty/dxc/bin/linuxsteamrt64/dxc"
[ -x "$DXC" ] || DXC="$(command -v dxc || true)"
[ -n "$DXC" ] || { echo "No dxc: run the native build or install the Vulkan SDK"; exit 1; }
FLAGS="-spirv -fspv-target-env=vulkan1.3 -fvk-use-dx-layout -O3 -HV 2021"
"$DXC" $FLAGS -T vs_6_6 -E MainVs -fvk-invert-y -Fo forward.vs.spv forward.hlsl
"$DXC" $FLAGS -T ps_6_6 -E MainPs -D PIXEL -Fo forward.ps.spv forward.hlsl
echo Compiled
