---
name: sbox-cli
description: Drives a running s&box editor or game through sbox-cli, the engine's MCP bridge, to inspect and edit scenes, prefabs, assets and Hammer maps, run C# inside the engine, check compile results, enter play mode and take screenshots. Use when working in an s&box project (a folder with a .sbproj file), when asked to do anything in the s&box editor, or when asked to launch, attach to or automate s&box.
---

# sbox-cli

`sbox-cli` ships with every s&box installation. It connects to a running editor or game over a
local named pipe and gives you its live tools: the scene hierarchy, components, assets, Hammer
maps, play mode, the console, and arbitrary C# run inside the process.

**When an editor has the project open, work through it instead of hand-editing scene, prefab or
asset files.** The editor validates changes, records undo for its scene tools, recompiles code and
shows you what actually happened. Hand-edited `.scene`/`.prefab` JSON is easy to get subtly wrong.

Everything runs locally, as the current user, against the user's own editor. There is no network
listener and no port to configure.

## 1. Find sbox-cli

It is `<engine>/bin/managed/sbox-cli.exe`, where `<engine>` is the folder containing `sbox.exe`.
On Linux and macOS the files have no `.exe`: `sbox-cli`, `sbox` and `sbox-dev`.

1. The `FACEPUNCH_ENGINE` environment variable. The launcher sets it as a Windows **user**
   variable, so a shell started before that may lack it — check the user scope too
   (`[Environment]::GetEnvironmentVariable('FACEPUNCH_ENGINE','User')`).
2. The default Steam folder, `C:\Program Files (x86)\Steam\steamapps\common\sbox`.
3. The folder of a running `sbox-dev.exe`.
4. Ask the user.

Don't hard-code the result into project files; it differs per machine.

## 2. Use it

Run it **from the project folder** (or any folder inside it). It finds the `.sbproj` there and
talks to the editor that has that project open — never to another project's editor. Outside any
project it uses the only running instance. `--connect <pid|project>` picks one explicitly;
`--list` shows what's running.

### One call at a time

```bash
sbox-cli --call editor_status
sbox-cli --call search_tools '{"query":"scene"}'
sbox-cli --call call_tools '{"calls":[{"name":"play_start"},{"name":"read_console","arguments":{"minimumLevel":"Warn"}}]}'
sbox-cli --call run_csharp '{"path":"C:/path/to/script.cs"}'
```

`--call <tool> [json-arguments]` connects, calls one tool, prints the result and disconnects; the
editor keeps running. Structured results print as JSON, text as-is, and images (screenshots) are
saved to files whose paths are printed. Exit `0` = success, `2` = the tool reported an error
(message on stderr), `1` = couldn't connect or bad arguments.

Each call is a fresh connection, so batch related calls with `call_tools`. Pass long or
quote-heavy input through a file (`run_csharp` takes `path`). Windows PowerShell 5.1 strips
double quotes from native arguments — escape them (`'{\"query\":\"scene\"}'`) or use pwsh/bash.

### As an MCP server

For a long session, register it with the client so tools appear natively over one connection:

```bash
claude mcp add sbox -- "<engine>/bin/managed/sbox-cli.exe" --connect
```

The client's working directory picks the project, as above. With `--connect` the server fails to
start if that project's editor isn't open. `--project` instead attaches to the open editor or, if
there is none, opens one that the server **owns** — it closes, discarding unsaved work, when the
client disconnects. Restart the client's server after the editor restarts.

### Lifetimes

| Command | Does | When the CLI exits |
|---|---|---|
| `--connect [pid\|project]` | Attaches to a running editor, or a game started with `-mcp` | Instance keeps running |
| `--call <tool> [json]` | Attaches (like `--connect`), one call | Instance keeps running |
| `--project [project]` | Attaches to the project's open editor, else opens one | Closes only an editor it opened |
| `--game <project\|org.ident>` | Starts a game | Game closes |

Editors accept connections unless **Project Settings > AI > Enable MCP Communication** is off.
If the user has the project open, attach to it — never launch a second copy.

### New projects

Don't write a `.sbproj` by hand. Create the project from a template, the same way the launcher
does, then open it:

```bash
sbox-cli --templates                                   # names, titles and descriptions
sbox-cli --new "My Game" --template game.minimal --in C:/projects
sbox-cli --project C:/projects/my_game                 # opens the editor; it closes when the CLI exits
```

`--new` makes a folder named after the project's ident (`my_game`) and prints the `.sbproj` path.
Without `--template` it uses the launcher's default; without `--in`, the current directory.

## 3. Work loop

1. **Orient.** Call `editor_status` (editor) or `instance_status` (game). Confirm the project, the
   active scene, whether it has unsaved changes, play state and compile state.
2. **Discover.** Only a handful of entry points are in `tools/list`. The real tools are registered
   by editor and addon code and change as code hotloads. Find them with `search_tools` (every
   keyword must match) or `list_toolsets` → `describe_toolset`, then invoke them by name or
   through `call_tool`. **Use the returned input schema; don't guess arguments.** A catalog of
   what usually exists is in [references/tools.md](references/tools.md).
3. **Act.** Prefer the dedicated tools (`scene`, `asset`, `hammer`, `play`, `ui` toolsets).
   Fall back to `run_csharp` for anything they don't cover — see
   [references/run-csharp.md](references/run-csharp.md).
4. **Verify.** `read_console` shows what the engine logged (pass back its cursor as `since` to see
   only new output). Tool failures come back as errors explaining what to do instead — read them
   and adapt. Use screenshots for anything visual.
5. **After editing C#:** saving triggers compile and hotload. Poll `editor_status` until
   `IsCompiling` is false, then check `LastCompileSucceeded` / `LastCompileErrors`, and
   `compile_status` for the diagnostics. Re-run `search_tools` if you changed tool code.
6. **Save** the scene if you changed it and the user expects it on disk (`save_scene`).

Check engine APIs in the XML docs beside the CLI — `Sandbox.Engine.xml` (game code) and
`Sandbox.Tools.xml` (editor code) — or in existing project code. **s&box is not Unity**; Unity
APIs guessed from memory will not compile.

## Conventions

- Vectors and angles are comma strings: `"x,y,z"`, `"pitch,yaw,roll"` — not arrays or objects.
- Source engine coordinates: 1 unit = 1 inch, **+X forward, +Y left, +Z up**, angles in degrees.
- Game objects and components are identified by GUID; assets by the relative path that
  `asset_search` returns (`models/chair.vmdl`).
- Paging is `limit` / `offset`; out-of-range values clamp.
- Hammer maps (`.vmap`) are not scenes. Scene tools can't see map contents; use `hammer_*` tools.

## Things that bite

- **Edited scene vs. running scene.** In play mode `Game.ActiveScene` is the runtime copy; changes
  there vanish when play stops. `editor_status` reports the scene being edited.
- **Undo.** Editor scene tools document their undo behavior. `run_csharp`, runtime tools and console
  commands mutate live state with no undo.
- **Timeouts and retries.** If a mutation times out or the connection drops, inspect the current
  state before retrying — the work may have completed. A call that waits more than 30 s for the
  main thread (modal dialog, long operation) is cancelled and reported as the editor being blocked.
- **Can't connect.** Nothing in `--list` means no editor or game is running, MCP is disabled in
  Project Settings > AI, the game wasn't started with `-mcp`, or the engine build predates the
  pipe. Engine startup logs are in `<engine>/logs`.
- **Generated files.** The `.slnx` / `.csproj` files are generated by the editor. Edit source files
  and `.sbproj` settings, not the generated projects.

## References

| Topic | File |
|---|---|
| Raw protocol for MCP clients and programs: handshake, framing, results, timeouts, limits, exchange directory | [references/protocol.md](references/protocol.md) |
| Built-in entry points and the usual toolsets (scene, asset, hammer, play, ui, network…) | [references/tools.md](references/tools.md) |
| `run_csharp`: what's in scope, script files, cancellation, examples | [references/run-csharp.md](references/run-csharp.md) |
| s&box project layout, compile/hotload loop, validating changes | [references/project.md](references/project.md) |
| Adding your own MCP tools to a project or addon (`[McpTool]`) | [references/authoring-tools.md](references/authoring-tools.md) |
