# sbox-cli reference

The command line, and the protocol for MCP clients and programs that hold a connection open.
For one-off calls from a shell, `sbox-cli --call` (see SKILL.md) handles all of this for you.

## Command line

```text
sbox-cli --connect [pid|project]    Attach to a running editor, or a game started with -mcp
sbox-cli --project [project]        Attach to the project's editor, or open and own one
sbox-cli --game <project|ident>     Start a game and own its lifetime
sbox-cli --call <tool> [json]       Call one tool on a running instance, print the result, exit
sbox-cli --list                     Print running instances as JSON and exit
sbox-cli --new <title>              Create a project from a template [--template <name>] [--in <folder>]
sbox-cli --templates                List the project templates as JSON
sbox-cli --skill [name[/file]]      List the agent skills for this installation, or print one
sbox-cli --skill-install [target]   Install the s&box bootstrap skill for your agents
sbox-cli --skill-uninstall [target] Remove it again
sbox-cli --skill-status             Show where it's installed, as JSON
sbox-cli --help                     Print usage and the path of this skill

Options: --engine <game-directory>      installation containing sbox.exe / sbox-dev.exe
         --startup-timeout <seconds>    default 180
         --idle-timeout <seconds>       default 600
         --request-timeout <seconds>    default 300
```

- At most one mode; no mode prints help.
  `--call` combines only with `--connect` and implies it.
- A *project* is a `.sbproj` file or a directory containing exactly one. When `--connect`,
  `--project` or `--call` gets none, the CLI uses the `.sbproj` in the current directory or the
  nearest parent. Inside a project it only ever attaches to that project's instance. Outside any
  project, `--connect`/`--call` attach to the only running instance and refuse when there are
  several.
- `--connect <pid>` attaches to exactly that process.
- `--project` never opens a second editor on a project that is already open; it attaches instead,
  and then leaves that editor running on exit.
- `--game` takes a `.sbproj` path or a published package ident such as `facepunch.sandbox`.
- `--engine` defaults to two folders above the CLI (`<engine>/bin/managed/../..`); it's only needed
  for a CLI copied elsewhere.
- Launches (`--project`, `--game`) currently require Windows. They start `sbox-dev.exe -mcp
  -project <path>` or `sbox.exe -mcp +game <target>` with normal visible windows.
- Timeouts are 1–86400 seconds. The idle timeout only counts time with no outstanding request;
  log output doesn't renew it. On any timeout the connection closes.
- Exit codes: `0` success; `1` error (message on stderr prefixed `sbox-cli:`); `2` for `--call`
  when the tool reports an error.
- Skill targets are `claude` (`~/.claude/skills`) and `agents` (`~/.agents/skills`, read by Codex
  and others). Without one, install covers the agents found on this machine (`~/.claude`,
  `~/.codex` or `~/.agents` exists) and uninstall covers all. Only copies carrying the
  `sbox-bootstrap` frontmatter key are ever replaced or removed. The editor's startup prompt and
  Preferences › AI Agents run the same commands.
- `--list` asks every `sbox*` process for its identity and prints `[{"pid":…,"kind":"editor"|"game",
  "project":"…/x.sbproj"}]`. Processes that don't answer within 300 ms are skipped.

## Transport

- One UTF-8 JSON object per line on stdin and stdout. Don't pretty-print across lines.
- Don't write a byte order mark. Older engine builds reject a first message that starts with one
  (`-32700`, `id: null`); .NET Framework's `Process` (Windows PowerShell 5.1) adds one to
  redirected stdin when the console encoding is UTF-8. Treat any error response with a null `id`
  as fatal rather than waiting for a reply.
- **Stdout carries only protocol messages.** Diagnostics (`sbox-cli: connecting to PID …`,
  `connected`, errors) go to stderr — read it separately or you can deadlock on a full buffer.
- Keep the process and its stdin open for the whole session. **EOF on stdin disconnects
  immediately and cancels pending work**, so `echo '...' | sbox-cli --connect N` loses the reply.
- Limits: 8 MiB per request, 64 MiB per response, 32 outstanding requests per CLI.
- Requests on one connection run in order. Separate connections share the engine main thread.
- The pipe is per OS user and named from the target's PID and start time. On Windows the CLI
  verifies that the pipe's server really is the requested PID.

## Handshake

Send `initialize` and wait for its response:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"my-agent","version":"1"}}}
```

The result has:

- `instructions` — registry-wide conventions plus session-specific notes. Read them.
- `_meta` — `{pid, kind, project, exchangeDirectory}`.

Then send the initialized notification (no response) and list the entry points:

```json
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
```

Other methods: `ping` returns `{}`; `sbox/instance` returns the same object as `_meta`.

## Calling tools

```json
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"editor_status","arguments":{}}}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"search_tools","arguments":{"query":"scene"}}}
{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"call_tool","arguments":{"name":"scene_tree","arguments":{"maxDepth":2}}}}
```

`tools/list` contains only the stable entry points. Tools found through `search_tools` can be
called directly by name with `tools/call` or through `call_tool`; both go to the live registry.
`call_tools` runs several calls in order in one request and stops at the first failure.

Responses:

- **Protocol errors** come back as JSON-RPC `error` (bad JSON, unknown method, missing tool name).
- **Tool failures** come back as a normal `result` with `isError: true`. The text explains what
  went wrong and usually what to call instead (near-miss tool names, the real parameter list).
- Output is in `result.content` — `text` blocks and `image` blocks (`data` is base64, with
  `mimeType`). Tools with a declared return type also include `result.structuredContent`.
- Unsolicited messages may appear; match responses by `id` and ignore the rest.

## Exchange directory

Each connection gets a unique directory under `%TEMP%/sbox-cli/`, reported as
`_meta.exchangeDirectory` and by `instance_status`. Use it for script files you pass to
`run_csharp` and for files the engine writes for you (screenshots, dumps).

It survives disconnect. Copy anything worth keeping into the project, then delete that session's
directory. A new connection gets a new directory — don't assume reuse.

## Ending the session

Wait for the last response, close stdin, then wait for the CLI to exit.

- Attached (`--connect`, or `--project` on an open editor): the instance keeps running. An
  attached instance is never killed, even to stop a stuck script.
- Owned (`--project` that opened the editor, `--game`): the CLI sends a `quit` console command, waits up to 2 seconds, then
  terminates the owned process tree (a Windows job object, which also fires if the CLI crashes or
  is killed). Unsaved editor work is lost.

Running scripts are cancelled cooperatively on disconnect. Calls still queued for the main thread
are cancelled on disconnect or after 30 seconds without being picked up. A mutation that was
already running may have completed — inspect state before retrying it.
