@echo off
rem Compile the sample's HLSL to SPIR-V with DXC: the repo's (src/thirdparty/dxc, downloaded by the native build) or
rem the Vulkan SDK's. Vertex shaders invert Y as s&box's do (Slang's VulkanInvertY). The .spv files are checked in.
setlocal
cd /d "%~dp0"
set DXC=%~dp0..\..\..\..\src\thirdparty\dxc\bin\win64\dxc.exe
if not exist "%DXC%" set DXC=%VULKAN_SDK%\Bin\dxc.exe
if not exist "%DXC%" ( echo No dxc: run the native build or install the Vulkan SDK & exit /b 1 )
set FLAGS=-spirv -fspv-target-env=vulkan1.3 -O3 -HV 2021

for %%s in (triangle mesh) do (
	"%DXC%" %FLAGS% -T vs_6_6 -E MainVs -fvk-invert-y -Fo %%s.vs.spv %%s.hlsl || exit /b 1
	"%DXC%" %FLAGS% -T ps_6_6 -E MainPs -Fo %%s.ps.spv %%s.hlsl || exit /b 1
)
"%DXC%" %FLAGS% -T as_6_6 -E MainTs -fspv-extension=SPV_EXT_mesh_shader -Fo meshtriangle.ts.spv meshtriangle.hlsl || exit /b 1
"%DXC%" %FLAGS% -T ms_6_6 -E MainMs -fvk-invert-y -fspv-extension=SPV_EXT_mesh_shader -Fo meshtriangle.ms.spv meshtriangle.hlsl || exit /b 1
"%DXC%" %FLAGS% -T cs_6_6 -E MainCs -Fo pattern.cs.spv pattern.hlsl || exit /b 1
"%DXC%" %FLAGS% -T cs_6_6 -E MainCs -Fo slots.cs.spv slots.hlsl || exit /b 1
echo Compiled
