# MCP tools and sbox-cli

For agent-facing usage, see the [`sbox-cli` skill](../../Launcher/SboxCli/skills/sbox-cli/SKILL.md),
which ships beside the executable and is printed by `sbox-cli --skill sbox-cli`. Agents find it
through a small bootstrap skill ([`bootstrap/sbox`](../../Launcher/SboxCli/bootstrap/sbox/SKILL.md),
also published as `Facepunch/sbox-skills`), installed for the user. The editor offers it once when
it finds Claude Code or Codex; Editor Preferences › AI Agents and `sbox-cli --skill-install` manage it.

`game/bin/managed/sbox-cli.exe` is a .NET 10 console application that keeps one MCP connection
open over stdin/stdout, or makes a single call with `--call`. It has no engine dependencies of its own. Editors expose a local
named pipe by default, controlled by **Project Settings > AI > Enable MCP Communication**;
games expose one when launched with `-mcp`. The pipe is restricted to the current
OS user and named using the process ID and start time. On Windows the CLI also verifies the
server's actual PID. Multiple editors and games can therefore run independently without TCP ports.

The preference is stored in the `.sbproj` and shared with collaborators. Saving MCP as disabled
immediately closes connected transports and cancels pending work; already running scripts must
cooperate with cancellation.

```powershell
# Discover running instances that support the new transport.
game/bin/managed/sbox-cli.exe --list

# One call against the editor for the project in the current directory, then exit.
game/bin/managed/sbox-cli.exe --call editor_status
game/bin/managed/sbox-cli.exe --call search_tools '{"query":"scene"}'

# Attach; closing the CLI leaves the process running. Takes a PID, a project, or nothing for
# the project containing the current directory (or the only instance, outside any project).
game/bin/managed/sbox-cli.exe --connect
game/bin/managed/sbox-cli.exe --connect 12345

# Attach to the project's editor if it's open, otherwise launch and own one.
game/bin/managed/sbox-cli.exe --project C:/projects/my-game

# Create a project from a template, like the launcher does. Prints the new .sbproj path.
game/bin/managed/sbox-cli.exe --templates
game/bin/managed/sbox-cli.exe --new "My Game" --template game.minimal --in C:/projects

# Launch and own a game.
game/bin/managed/sbox-cli.exe --game C:/projects/my-game/my-game.sbproj
game/bin/managed/sbox-cli.exe --game facepunch.sandbox
```

A project is a `.sbproj` or the directory containing it. Inside a project, the CLI only ever
attaches to that project's instance. Launches open normal visible windows. `--engine <directory>` overrides the game installation
inferred from the CLI's location. Owned launches currently require Windows. An existing process
must be running an engine build with the pipe support; restart older editors after rebuilding.

## Stdio protocol and session files

Configure an MCP client to run the CLI with one connection mode and keep that process open.
Do not start a new CLI for every request. Each request/response is one UTF-8 JSON-RPC line:

```jsonl
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"my-agent","version":"1"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"instance_status","arguments":{}}}
{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"search_tools","arguments":{"query":"scene"}}}
```

Wait for the initialization response before sending subsequent messages. Its `instructions`
explain tool discovery, conventions and the session exchange directory. `_meta` contains `pid`,
`kind`, `project` and `exchangeDirectory` for programs. The unique directory is created under
`%TEMP%/sbox-cli/`; use it for scripts, screenshots and other files shared with the instance.
It is retained after disconnect so outputs remain usable. Copy wanted outputs to the project,
then remove the session directory when finished. Discovery alone does not create directories.

Stdout contains only protocol messages in connection modes. Startup messages and diagnostics
go to stderr. Requests are limited to 8 MiB, responses to 64 MiB, with at most 32 outstanding
requests per CLI. Each connection executes requests in order; separate connections share the
engine main thread. Tools, discovery and binding run on that thread in game scope.

## C# scripts

Use `search_tools` with `csharp` to find `run_csharp`, then call it directly or through `call_tool`:

```jsonl
{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"run_csharp","arguments":{"code":"return Game.ActiveScene?.Name;"}}}
```

Supply a C# async method body in `code`, or an absolute UTF-8 file `path` (not both).
`System`, `System.Linq`, `System.Threading`, `System.Threading.Tasks`, `Sandbox`, and the game
global namespace are imported. `await`, `return`, `exchangeDirectory` and `cancellationToken`
are available. References come from loaded assemblies, including the active addon assemblies;
editor types can be fully qualified in an editor. Compile diagnostics use script line numbers.
This is trusted local automation with full process privileges, and mutations have no automatic
undo. The protocol, transport, registry, session state and built-in tool classes are internal.
Built-in tool methods are also internal and described only in a private MCP registry; never add
`[Expose]`, `[Library]` or `[ActionGraphInclude]` to them. Game code must not reach them through
its TypeLibrary.
Only the tool declaration attributes, hints and `McpResult` builders are public for addon authors.

## Lifetime

Closing stdin, losing the pipe, or Ctrl+C shuts down the bridge. An attached instance
(`--connect`, `--call`, or `--project` on an editor that was already open) is never terminated.
For an instance it launched, the CLI first requests a graceful quit,
waits up to two seconds, then closes its Windows job to terminate the owned process tree.
The instance is assigned to the job atomically during creation, before running; the job also closes if the
CLI crashes or is forcibly killed. Engine processes do not inherit the CLI's stdio handles.

The defaults are `--startup-timeout 180`, `--idle-timeout 600`, and `--request-timeout 300`
(seconds, configurable from 1 to 86400). Idle timeouts apply only without outstanding requests;
logs and other unsolicited output do not renew them. A running request has its own deadline.
On timeout the connection closes. Queued calls are cancelled on disconnect or after a 30-second
main-thread pickup timeout. Running code cancels cooperatively; use `cancellationToken` in
long scripts. A running mutation may have completed before disconnection, so inspect state
before retrying. An attached instance is never killed to stop a stuck script.

## Tool discovery

A tool is a static method. There is no registration - discovery goes through the editor or game TypeLibrary,
so tools in addons and hotloaded assemblies appear and disappear as code compiles. Write the
method, save, and an agent can call it.

The registry and protocol live in `engine/Sandbox.Engine/Systems/Mcp/`.
The established `Editor.Mcp` attribute/result namespace remains available for source and binary
compatibility, including game tools. Editor-specific tools stay in `Sandbox.Tools` and editor
addons.

## Adding a tool

```csharp
namespace Editor.Mcp;

[McpToolset( "car", "Everything about the cars in the scene" )]
public static class CarTools
{
	/// <summary>
	/// Find cars by name. Returns each car's id - pass one to get_car for details.
	/// </summary>
	/// <param name="query">Case insensitive substring of the car's name. Empty matches everything.</param>
	/// <param name="limit">How many results to return. Default 20, max 100.</param>
	[McpTool( "find_cars" )]
	public static object FindCars( string query = "", int limit = 20 )
	{
		...
		return new { Total = total, Cars = cars };
	}
}
```

That's everything. In addon code the XML summary becomes the tool description and `<param>` docs
become the parameter descriptions (codegen turns them into attributes). In engine assemblies there
is no codegen - put `[Description( ... )]` on the method and each parameter instead.

The description is the API. Agents pick tools by reading it, so say what the tool returns, what to
call next, and anything surprising. Cross reference other tools by name - "as returned by
find_cars" tells an agent exactly where a value comes from.

- `[McpTool]` - marks the method. Pass a name (`"find_cars"`) or one gets derived from the method
  name in snake_case (`FindCars` → `find_cars`). Names are public API - agents' workflows break
  when they change.
- `[McpToolset]` on the class groups its tools under a name and description, browsable through
  `list_toolsets`. Without it the name derives from the class name (`CarTools` → `car`).

Tools don't go in the client's static tool list - agents find them live through `search_tools` and
invoke them through `call_tool`, which is what keeps the registry fresh across hotloads. (The
handful of built in entry points are listed via the internal `[McpListed]`, and that's deliberate -
a list a client fetched once must never go stale.)

## Parameters

Parameters become the tool's json schema. A parameter with a default value is optional; without
one it's required.

| C# type | What the agent sends |
|---|---|
| `string` | A string. Anything else gets stringified, so this is the most forgiving type. |
| `bool` | true / false |
| `int`, `long`, `byte`... | An integer |
| `float`, `double`, `decimal` | A number |
| enums | The name as a string, case insensitive. The schema lists every value. |
| `Vector2/3/4`, `Vector2Int/3Int`, `Angles`, `Rotation` | A comma string - `"100,0,50"`, `"pitch,yaw,roll"` |
| `Guid`, `DateTime`, `TimeSpan`, `char` | A string |
| `T[]`, `List<T>` | An array of T |
| `Dictionary<string, T>` | An object with T values |
| `JsonObject`, `JsonArray`, `JsonNode` | Raw json, taken as sent |
| A class or struct you wrote | An object - see below |

Binding is deliberately tolerant, because agents make predictable mistakes: argument names match
case insensitively, values that arrive json-encoded inside a string get unwrapped, numbers arrive
as strings, enum names in the wrong case still count. Unknown argument names fail loudly - silently
ignoring a typo'd argument does the wrong thing invisibly.

## Custom classes and structs

A plain data type works as a parameter (or inside one - arrays, lists, nested properties). The
schema is generated from its public settable properties:

```csharp
/// <summary>
/// One waypoint on a patrol route.
/// </summary>
public class Waypoint
{
	/// <summary>Where to stand, as 'x,y,z'.</summary>
	public required Vector3 Position { get; set; }

	/// <summary>Seconds to wait before moving on.</summary>
	public float Wait { get; set; }
}

[McpTool( "set_patrol" )]
public static object SetPatrol( string id, Waypoint[] waypoints ) { ... }
```

- Every public property with a public setter becomes a schema property, advertised in camelCase
  (`Position` → `position`). Binding is case insensitive so both spellings work.
- The C# `required` keyword makes the property required in the schema, and the serializer enforces
  it - a call missing `position` fails with an error saying so.
- Describe properties the same way as everything else - XML summaries in addon code,
  `[Description]` in engine code.
- `[JsonPropertyName]` and `[JsonIgnore]` are respected.

Engine and framework types don't get their properties reflected - most serialize through custom
converters (`Vector3` is the string `"x,y,z"`, not `{x, y, z}`), so a guessed shape would be wrong.
They keep an open schema; prefer the types in the table above.

## Return values

Return whatever is most honest, it gets shaped into an MCP result:

| Return | What the agent gets |
|---|---|
| `string` | The text as is |
| Any object | Serialized to json - anonymous objects are the usual shape |
| `GameObject` / `Component`, at any depth | A compact reference - `{ "type": "PointLight", "id": ..., "gameObject": ... }` - never a serialized scene subtree. The id chains into get_game_object. |
| `Resource` (Model, Material, any GameResource) | Its resource path - `"models/chair.vmdl"` - which chains into asset_info and friends. That's the engine's own serialization; no special handling. |
| `Bitmap` | A png image |
| `McpResult` | Full control - compose text and image blocks, e.g. `McpResult.Image( thumb ).WithText( path )` |
| `null` / `void` | "ok" |
| `Task<T>` / `async` | Awaited, then shaped as above |

Shape returns for a reader, not a serializer: return counts alongside truncated lists
(`new { Total, Showing, Assets }`), format dates as strings, and leave out fields nobody asked for.

Declare a data class as the return type instead of `object` and the tool advertises an
`outputSchema` generated from its properties, and every result carries `structuredContent` the
client can bind to it. Anonymous returns stay plain text - use a class when the shape is stable
and worth promising.

## Errors

Throw. The exception's message reaches the agent as an error result it will read and adapt to, so
write the message for that reader: say what was wrong and what to do instead.

```csharp
throw new Exception( $"No car named '{name}'. Find cars with find_cars." );
```

Everything from tool lookup onward goes back in-band - unknown tools, arguments that don't bind,
failures thrown mid-tool - written with the same care: suggestions for near-miss names, the tool's
real parameter list. The agent reads the error and self corrects. You get all of that for free.

## Threading and lifetime

Tools run on the editor main thread - touch scenes, assets and widgets freely, no marshalling. If
the main thread doesn't pick the call up within 30 seconds (modal dialog, long operation) the agent
is told the editor is blocked; the queued call is cancelled and will not run later.

Tools can be `async`. The main thread invokes the method, the result is awaited off it - so do slow
work (network, compiles) after your last touch of engine state, or hop back with `MainThread`.

## Conventions

Registry-wide rules live in one place - the `Instructions` string in `McpProtocol.cs`, which every
client injects into its agent's context at connect. Add new rules there instead of repeating them
per tool. The current ones:

- Paging is `limit` and `offset`. Say the default and max in the parameter description.
- Vectors and angles are comma strings - `'x,y,z'`, `'pitch,yaw,roll'`.
- Coordinates are Source engine convention: one unit is one inch, +x forward, +y left, +z up,
  angles in degrees.
- Game objects and components are identified by guid, assets by the relative path `asset_search`
  returns.

## Trying it out

From the project directory, `sbox-cli --call search_tools '{"query":"car"}'` finds your tool and
`sbox-cli --call find_cars` calls it. For an MCP client, configure a stdio server with the full
path to `sbox-cli.exe` and `["--connect"]` (or `["--project"]`) as arguments; the client's working
directory selects the project.

Then ask it to `search_tools` for yours and call it. `read_console` shows what the editor logged,
which is also where your tool's errors and prints end up.
