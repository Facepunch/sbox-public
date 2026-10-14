# Adding MCP tools to a project

A project or addon can give agents its own tools. A tool is a public static method with
`[McpTool]`. There is no registration: the registry finds tools through the type library, so a
tool appears as soon as the code compiles and hotloads, and `search_tools` / `call_tool` reach it
without reconnecting.

Put editor-only tools in `Editor/`; tools that must also work in a running game (`--game`, or a
game started with `-mcp`) go in `Code/`.

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

- `[McpTool( "name" )]` marks the method. Without a name it's derived as snake_case
  (`FindCars` → `find_cars`). Names are API — renaming one breaks agent workflows.
  `[McpTool.ReadOnly( ... )]` marks tools that don't change anything.
- `[McpToolset( name, description )]` groups the class's tools for `list_toolsets`. Without it the
  name is derived from the class (`CarTools` → `car`).
- In project/addon code, the XML `<summary>` becomes the tool description and `<param>` docs the
  parameter descriptions. **The description is the interface**: say what it returns, what to call
  next, and anything surprising. Refer to other tools by name ("id as returned by find_cars").

## Parameters

A parameter with a default is optional; without one it's required.

| C# type | Agent sends |
|---|---|
| `string` | A string (anything else is stringified — the most forgiving type) |
| `bool` | `true` / `false` |
| integer and floating types | A number |
| enums | The name as a string, case insensitive; the schema lists the values |
| `Vector2/3/4`, `Vector2Int/3Int`, `Angles`, `Rotation` | A comma string: `"100,0,50"` |
| `Guid`, `DateTime`, `TimeSpan`, `char` | A string |
| `T[]`, `List<T>` | An array |
| `Dictionary<string, T>` | An object |
| `JsonObject`, `JsonArray`, `JsonNode` | Raw JSON |
| Your own class or struct | An object built from its public settable properties (camelCase in the schema, case-insensitive binding, C# `required` enforced, `[JsonPropertyName]` / `[JsonIgnore]` respected) |

Binding is tolerant of common mistakes — case, JSON-encoded strings, numbers as strings — but
unknown argument names fail loudly. Engine types other than those listed keep an open schema;
prefer the listed types.

## Return values

| Return | Agent gets |
|---|---|
| `string` | The text |
| any object | JSON (anonymous objects are the usual shape) |
| `GameObject` / `Component` at any depth | A compact reference with its id, never a serialized subtree |
| `Resource` | Its resource path |
| `Bitmap` | A PNG image |
| `McpResult` | Full control, e.g. `McpResult.Image( thumb ).WithText( path )` |
| `null` / `void` | `ok` |
| `Task<T>` | Awaited, then shaped as above |

Return counts alongside truncated lists (`new { Total, Showing, Items }`), format dates as
strings and leave out fields nobody asked for. Declaring a named data class as the return type
(instead of `object`) also advertises an `outputSchema` and fills `structuredContent`.

## Errors, threading

- **Throw** to report failure. The message reaches the agent as an `isError` result, so write it
  for that reader: what was wrong and what to do instead.
  `throw new Exception( $"No car named '{name}'. Find cars with find_cars." );`
- Tools run on the main thread; touch scenes, assets and widgets freely. `async` tools are awaited
  off the main thread after the first `await`, so do slow work after your last engine access.
- If the main thread doesn't pick a call up within 30 seconds the agent is told the editor is
  blocked, and the call is cancelled.

## Conventions to follow

- Paging is `limit` and `offset`; state the default and max in the parameter description.
- Vectors and angles are comma strings. Coordinates are Source convention (inches, +X forward,
  +Y left, +Z up, degrees).
- Identify game objects and components by GUID, assets by the relative path `asset_search` returns.
- Document undo behaviour for anything that edits a scene.

After saving, check `editor_status` for a clean compile, then `search_tools` for your tool's name.
