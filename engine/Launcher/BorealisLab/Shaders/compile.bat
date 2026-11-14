@echo off
rem Compile Borealis Lab's map shader to SPIR-V with DXC: the repo's (src/thirdparty/dxc, downloaded by the native build) or
rem the Vulkan SDK's. One file per MODE (see map.hlsl). Vertex shaders invert Y as s&box's do. The .spv files are checked in.
setlocal
cd /d "%~dp0"
set DXC=%~dp0..\..\..\..\src\thirdparty\dxc\bin\win64\dxc.exe
if not exist "%DXC%" set DXC=%VULKAN_SDK%\Bin\dxc.exe
if not exist "%DXC%" ( echo No dxc: run the native build or install the Vulkan SDK & exit /b 1 )
set FLAGS=-spirv -fspv-target-env=vulkan1.3 -O3 -HV 2021

for %%m in (0 1 2) do (
	"%DXC%" %FLAGS% -D MODE=%%m -T vs_6_6 -E MainVs -fvk-invert-y -Fo map%%m.vs.spv map.hlsl || exit /b 1
	"%DXC%" %FLAGS% -D MODE=%%m -T ps_6_6 -E MainPs -Fo map%%m.ps.spv map.hlsl || exit /b 1
	"%DXC%" %FLAGS% -D MODE=%%m -T ps_6_6 -E MainDepthPs -Fo map%%m.depth.spv map.hlsl || exit /b 1
)
echo Compiled
