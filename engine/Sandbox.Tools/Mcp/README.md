# MCP tools

The editor runs a [Model Context Protocol](https://modelcontextprotocol.io/) server so AI agents
like Claude Code can read and drive the open project. It's on by default (Editor → Preferences →
MCP Server), listens on `http://127.0.0.1:7269/mcp`, and is only ever reachable from this machine.

A tool is a static method. There is no registration - discovery goes through EditorTypeLibrary,
so tools in addons and hotloaded assemblies appear and disappear as code compiles. Write the
method, save, and an agent can call it.

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
is told the editor is blocked; the call still runs when it unblocks.

Tools can be `async`. The main thread invokes the method, the result is awaited off it - so do slow
work (network, compiles) after your last touch of engine state, or hop back with `MainThread`.

## Conventions

Registry-wide rules live in one place - the `Instructions` string in `McpServer.cs`, which every
client injects into its agent's context at connect. Add new rules there instead of repeating them
per tool. The current ones:

- Paging is `limit` and `offset`. Say the default and max in the parameter description.
- Vectors and angles are comma strings - `'x,y,z'`, `'pitch,yaw,roll'`.
- Coordinates are Source engine convention: one unit is one inch, +x forward, +y left, +z up,
  angles in degrees.
- Game objects and components are identified by guid, assets by the relative path `asset_search`
  returns.

## Trying it out

Connect Claude Code with:

```
claude mcp add --transport http sbox http://127.0.0.1:7269/mcp
```

Then ask it to `search_tools` for yours and call it. `read_console` shows what the editor logged,
which is also where your tool's errors and prints end up.
