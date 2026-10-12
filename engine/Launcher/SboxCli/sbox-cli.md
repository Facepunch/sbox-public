# Using sbox-cli

This guide ships beside `sbox-cli.exe` in `bin/managed`. Use the executable from that
installation to discover, launch and control an s&box editor or game. It speaks MCP over
stdin/stdout and connects to one instance through a local named pipe. No HTTP URL or port
configuration is needed.

Projects created by the launcher or opened in the editor get a short root `AGENTS.md`
section pointing to `.sbox/agent-guide.md`. Existing author instructions are preserved.
The local guide refreshes on editor startup with the project and installation paths.

Project Settings > AI stores shared preferences in the `.sbproj`. Both options default to on:

- **Write to AGENTS.md** controls automatic instruction and local guide updates. Disabling it
  leaves existing files untouched. While enabled, a deleted `AGENTS.md` is recreated on open.
- **Enable MCP Communication** controls connections to this project's editor. Disabling it
  disconnects existing clients when saved. Running scripts still cancel cooperatively.

## Choose an instance

```powershell
# Discover running instances. Prints JSON and exits.
.\sbox-cli.exe --list

# Attach to a PID from that list. Disconnecting leaves the instance running.
.\sbox-cli.exe --connect 12345

# Open a project in a visible editor. The CLI owns this new instance.
.\sbox-cli.exe --project "C:\projects\my-game\my-game.sbproj"

# Launch a local game project or a published game.
.\sbox-cli.exe --game "C:\projects\my-game\my-game.sbproj"
.\sbox-cli.exe --game facepunch.sandbox
```

Use an explicit `.sbproj` path. A directory argument looks for `.sbproj` inside that directory.
Editors expose a pipe unless disabled in Project Settings > AI. An existing game must have started with `-mcp` to accept
`--connect`. Owned launches currently require Windows. `--engine <directory>` overrides the
installation directory containing `sbox.exe` and `sbox-dev.exe`.

## Keep one connection open

An MCP client can launch the executable with the arguments above as a stdio server. For
example, use the absolute executable path as `command` and `["--connect", "12345"]` as `args`.
The client handles initialization and tool calls.

For direct process control, redirect stdin and stdout and keep the process and stdin open
across requests. Read stderr separately for diagnostics. Do not start a new CLI for every
request or pipe a finite batch into it: EOF disconnects immediately and cancels pending work.

Each message is one UTF-8 JSON object followed by a newline. Do not pretty-print messages
across lines. Use unique request IDs and match each response to its ID. Stdout contains only
protocol messages; startup diagnostics use stderr.

Send this first and wait for its response:

```jsonl
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"my-agent","version":"1"}}}
```

Read `result.instructions` and `result._meta`. They identify the connected process, project
and session exchange directory. Then send the notification below, which has no response,
followed by `tools/list`:

```jsonl
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
```

Wait for each response before proceeding with work that depends on it.

## Find and call tools

Start with `instance_status`, or `editor_status` in an editor. Check the project before
changing anything. `editor_status` also reports compilation and play state.

```jsonl
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"editor_status","arguments":{}}}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"search_tools","arguments":{"query":"scene"}}}
```

`tools/list` lists entry points. Use `search_tools`, `list_toolsets` and `describe_toolset`
to discover the live editor/addon tools and their argument schemas. They can change when
code hotloads. Invoke the discovered name through `tools/call`, or through `call_tool`.
Use the returned schema rather than guessing arguments.

Read both JSON-RPC `error` and tool-result `isError`. Tool output is in `result.content`
and sometimes `result.structuredContent`. `read_console` returns recent logs and a cursor
for subsequent reads. After editing code, check `editor_status` for compile completion.

## Run C# and exchange files

Find `run_csharp` using `search_tools` with `query: "csharp"`. It accepts an async C# method
body in `code`, or an absolute UTF-8 script file `path`, but not both.

```jsonl
{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"run_csharp","arguments":{"code":"return new { MainThread = ThreadSafe.IsMainThread, IsPlaying = Game.IsPlaying };"}}}
```

The body runs on the engine main thread in game scope. `System`, `System.Linq`, threading,
tasks and `Sandbox` are imported. Use fully qualified editor types for editor APIs.
`Game.ActiveScene` describes the runtime scene; use `editor_status` for the scene being
edited. Scripts have full local process privileges and do not create undo steps automatically.

The script parameters `exchangeDirectory` and `cancellationToken` are available. Use the
absolute `exchangeDirectory` returned by initialization for scripts and output files. Long
scripts should honor cancellation. Compilation errors refer to script line numbers.

Exchange files survive disconnect. Copy outputs you want to keep, then remove that session's
directory when finished. Do not assume the next connection will reuse it.

## Finish the session

Wait for the last response, close stdin, then wait for the CLI to exit. With `--project` or
`--game`, this also closes the owned instance. With `--connect`, the instance stays running.
A CLI-owned process tree is also terminated if the CLI crashes or is forcibly killed.

Timeout options are seconds: `--startup-timeout` defaults to 180, `--idle-timeout` to 600,
and `--request-timeout` to 300. Idle timeouts apply only without outstanding requests.
If a mutation times out or the connection fails, inspect the resulting state before retrying;
already running work may have completed. Engine startup logs are in the installation's
`logs` directory. `--help` prints the command summary and the path to this guide.
