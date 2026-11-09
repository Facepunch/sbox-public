@echo off
rem Compile Basalt.Backend's HLSL to SPIR-V with DXC: the repo's (src/thirdparty/dxc, downloaded by the native build) or
rem the Vulkan SDK's. Vertex shaders invert Y as s&box's do (Slang's VulkanInvertY); constant buffers use D3D packing,
rem as Basalt's C# mirrors of them do. The .spv files are checked in.
setlocal
cd /d "%~dp0"
set DXC=%~dp0..\..\..\src\thirdparty\dxc\bin\win64\dxc.exe
if not exist "%DXC%" set DXC=%VULKAN_SDK%\Bin\dxc.exe
if not exist "%DXC%" ( echo No dxc: run the native build or install the Vulkan SDK & exit /b 1 )
set FLAGS=-spirv -fspv-target-env=vulkan1.3 -fvk-use-dx-layout -O3 -HV 2021

"%DXC%" %FLAGS% -T vs_6_6 -E MainVs -fvk-invert-y -Fo forward.vs.spv forward.hlsl || exit /b 1
"%DXC%" %FLAGS% -T ps_6_6 -E MainPs -D PIXEL -Fo forward.ps.spv forward.hlsl || exit /b 1
echo Compiled
