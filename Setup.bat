@echo off
setlocal
pushd "%~dp0"
if errorlevel 1 exit /b 1

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET 10 SDK is required but 'dotnet' was not found on PATH.
    echo Install it from https://dotnet.microsoft.com/download and rerun Setup.bat.
    popd
    exit /b 1
)

dotnet run --verbosity quiet --project ".\engine\Tools\SboxBuild\SboxBuild.csproj" -- bootstrap %*
popd & exit /b %errorlevel%
