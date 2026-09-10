@echo off
setlocal

set "PROJECT_DIR=%~dp0"
set "OUTPUT_DIR=%~dp0..\..\..\steamworks\editor-launcher-output"

echo Building Editor Launcher (NativeAOT)...
dotnet publish "%PROJECT_DIR%EditorLauncher.csproj" -c Release -o "%OUTPUT_DIR%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo Build failed!
    exit /b 1
)

echo.
echo Build complete. Output: %OUTPUT_DIR%\sbox-editor.exe
