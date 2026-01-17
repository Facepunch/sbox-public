#!/bin/bash


dotnet run --project ./engine/Tools/SboxBuild/SboxBuild.csproj -- build --config Developer
dotnet run --project ./engine/Tools/SboxBuild/SboxBuild.csproj -- build-shaders
#HACK: Currently Facepunch doesn't ship native binary for contentbuilder. Run this instead via wine.
wine game/bin/win64/contentbuilder.exe -b game
