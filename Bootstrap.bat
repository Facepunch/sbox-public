@echo off
setlocal

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK was not found in PATH.
    echo Please install the required SDK and run this script again.
    exit /b 1
)

call :run_step "Build engine" dotnet run --project .\engine\Tools\SboxBuild\SboxBuild.csproj -- build --config Developer
call :run_step "Build shaders" dotnet run --project .\engine\Tools\SboxBuild\SboxBuild.csproj -- build-shaders
call :run_step "Build content" dotnet run --project .\engine\Tools\SboxBuild\SboxBuild.csproj -- build-content

echo.
echo [OK] Bootstrap completed successfully.
exit /b 0

:run_step
set "stepName=%~1"
echo.
echo [STEP] %stepName%
shift
%*
if errorlevel 1 (
    echo [ERROR] Step failed: %stepName%
    exit /b 1
)
exit /b 0
