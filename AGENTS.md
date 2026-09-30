# s&box — notes for AI coding agents

s&box is a game engine built on Valve's Source 2 with a .NET 10 managed layer on top. This repo is the
**managed engine, editor, and tooling** in C#. The native C++ core is not here — `Setup.bat` (`./Setup.sh` on Linux and macOS)
downloads prebuilt native binaries into `game/bin/win64/`, and the managed side calls into them through a
generated interop layer (never hand-edit `Interop.*.cs` or anything under `obj/.generated/`).

## Layout

| Path | What |
|------|------|
| `engine/` | Managed engine (`Sandbox.Engine`), editor (`Sandbox.Tools`), launcher, and build tools (`engine/Tools/`). Solution: `engine/Sandbox-Engine.slnx`. |
| `engine/Tests/` | Test projects: `Sandbox.Test.Unit` (pure managed), `Sandbox.Test.Engine` (loads native DLLs), `Sandbox.Test.Integration` (boots the engine). |
| `game/addons/` | Shipped addons and editor tooling written against the public API (`base`, `tools`, ...). |
| `game/core/` | Shipped core content: shaders, materials, models. |
| `game/bin/` | Build output. Don't commit anything here. |

## Build, test, format

Everything goes through the `SboxBuild` tool. First run `Setup.bat` (or `./Setup.sh`) once; after that:

```bash
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- build --config Developer
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- test --filter "TestCategory!=LiveBackend"
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- format
```

Pull requests run the same `build`, `test`, and `format --verify` steps, so run them before pushing.
Engine and Integration tests need the `FACEPUNCH_ENGINE` environment variable pointing at `game/`
(the folder containing `sbox.exe`). Run the built engine from `game/bin/win64/sbox.exe`.

## Documentation

- **Docs index for agents:** https://sbox.game/llms.txt — start here.
- **Developer docs:** https://sbox.game/dev/doc/ — any page is available as raw markdown by appending `.md` to its URL.
- **API reference:** https://sbox.game/api
- Contributing guidelines: `CONTRIBUTING.md`.

## Conventions

- Standard .NET style, enforced by `.editorconfig` and `format --verify`. Match surrounding code.
- Keep pull requests small and in scope. Reference the issue you're fixing. Add a unit test where one fits.
- The public API surface that addons compile against is deliberately controlled. Exposing a new .NET
  type to game code means adding it to the access allow-list in `engine/Sandbox.Access/`, not bypassing it.
