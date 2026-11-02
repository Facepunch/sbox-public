#!/bin/sh
# s&box setup for Linux and macOS (Windows: Setup.bat).
# Downloads the prebuilt engine artifacts, builds the managed engine, shaders and
# content, and installs git hooks that keep the artifacts current after a pull,
# rebase or branch switch. Pass --verbose for full build output.
set -e
cd -- "$(dirname -- "$0")"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "The .NET 10 SDK is required but 'dotnet' was not found on PATH."
    echo "Install it from https://dotnet.microsoft.com/download and rerun ./Setup.sh."
    exit 1
fi

exec dotnet run --verbosity quiet --project ./engine/Tools/SboxBuild/SboxBuild.csproj -- bootstrap "$@"
