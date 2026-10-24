# Tools

The tool registry is live: tools are static methods in engine, editor and addon code, discovered
through the type library. They appear and disappear as code compiles and hotloads, and a project
can add its own. **This page is a map, not a contract** — always confirm names and schemas with
`search_tools` or `describe_toolset` before calling.

## Entry points (always in `tools/list`)

| Tool | Available in | Purpose |
|---|---|---|
| `instance_status` | editor, game | PID, kind, engine version, project, game ident, active scene, play/loading state, exchange directory |
| `editor_status` | editor | Project, active scene and its unsaved state, play/pause state, tool count, compile state (`IsCompiling`, `LastCompileSucceeded`, `LastCompileErrors`) and key paths (engine, logs, project root, project settings) |
| `search_tools` | editor, game | Keyword search over names, descriptions and parameters; every keyword must match; empty returns all. Returns full input schemas |
| `list_toolsets` | editor, game | Every toolset with its tool names |
| `describe_toolset` | editor, game | Full schemas for one toolset |
| `call_tool` | editor, game | Invoke any registry tool by `name` with `arguments` |
| `call_tools` | editor, game | Ordered batch of `{name, arguments}`; stops at the first failure |
| `read_console` | editor, game | Recent log entries, oldest first. `limit` (≤500), `minimumLevel` (`Trace`, `Info`, `Warn`, `Error`), `filter`, `since` (cursor from the previous read) |

Found through search, but always present:

| Tool | Purpose |
|---|---|
| `run_csharp` | Compile and run a C# method body inside the process. See [run-csharp.md](run-csharp.md) |
| `instance_console_command` | Run one engine console command with local-console privileges (can quit the instance) |

## Performance (editor and game)

**`perf`** — the numbers the dev tools (`devtools`) shows, as data, with the dev tools closed.
Don't open the dev tools and screenshot it. Times are ms, memory MiB.
`perf_overview` (start here: frame stats, what limits the frame rate, hitches, flagged issues,
top CPU and GPU costs), `perf_frames`, `perf_cpu`, `perf_gpu`, `perf_memory` (`allocationSeconds`
tracks allocations by type), `perf_rendering`, `perf_network` (`seconds` records messages),
`perf_ui`. The first GPU read takes about a second while timings warm up.

## Editor toolsets

These ship with the editor (the built-in `tools` addon and Hammer) and are present in any editor
session. Games only have the entry points above, the `perf` tools, and whatever their own code
registers.

**`scene`** — open scenes, hierarchy, objects and components, editor camera.
`list_scenes`, `scene_tree`, `find_game_objects`, `get_game_object`, `create_game_object`,
`set_game_object`, `delete_game_object`, `add_component`, `set_component`, `remove_component`,
`get_selection`, `set_selection`, `spawn_model`, `spawn_models`, `scene_trace`, `undo`, `redo`,
`get_editor_camera`, `set_editor_camera`, `editor_camera_screenshot`, `camera_screenshot`.

**`scenefile`** — scene tabs. `open_scene`, `switch_scene`, `save_scene`, `close_scene`.

**`component`** — `get_component_type`: every inspector property of a component type, with types
and descriptions. Use it before `add_component` / `set_component`.

**`asset`** — the asset system. `asset_search` (filters for type, failed/uncompiled), `asset_info`,
`asset_dependencies`, `asset_files`, `asset_find_by_file`, `asset_types`, `asset_read` /
`asset_write` (raw JSON of GameResource assets such as prefabs and sound events — not models or
textures), `create_asset`, `asset_compile`, `asset_thumbnail` (image).

**`play`** — `play_start`, `play_stop`, `play_pause`.

**`editor`** — `editor_status`, `compile_status` (every compiler's errors and warnings with file
and line), `console_command`.

**`ui`** — live Razor/CSS UI inspection: `ui_panel_dump`, `ui_matched_rules`, `ui_hit_test`,
`ui_screenshot`, `ui_pixels`, `ui_probe`, `ui_probe_batch`, `ui_set_style`, `ui_set_pseudo`,
`text_measure`, `ui_batch_stats`.

**`network`** — multiplayer testing from the editor: `network_status`, `network_start_hosting`,
`network_spawn_instance` (local client instance), `network_disconnect`,
`network_migrate_to_new_instance` (host migration).

**`package`** — the sbox.game backend: `find_packages`, `get_package`, `install_package`.

**`log`** — `log_info`, `log_warning`, `log_error` (write to the editor console).

**`hammer`** — the map editor. Maps (`.vmap`) are not scenes; these tools work on the map open in
Hammer and identify nodes by integer id. `hammer_status`, `hammer_open_map`, `hammer_save`,
`hammer_find_nodes`, `hammer_get_node`, `hammer_set_node`, `hammer_select`, `hammer_get_selection`,
`hammer_create_entity`, `hammer_create_entities`, `hammer_entity_classes`,
`hammer_create_game_object`, `hammer_set_components`, `hammer_create_primitive`,
`hammer_list_primitives`, `hammer_create_mesh`, `hammer_mesh_faces`, `hammer_mesh_stats`,
`hammer_set_face_material`, `hammer_assign_asset`, `hammer_replace_material`, `hammer_move_nodes`,
`hammer_duplicate_nodes`, `hammer_delete_nodes`, `hammer_trace`, `hammer_focus_nodes`,
`hammer_set_camera`, `hammer_views`, `hammer_screenshot`, `hammer_run_command`,
`hammer_list_commands`, `hammer_build_map`, `hammer_build_status`, `hammer_compiled_map_stats`,
`hammer_undo`, `hammer_redo`, and optimisation helpers.

## Publishing

`publishing_preview` builds the same asset preview as Project Settings > Publishing, optionally
with proposed rules. It returns a summary and a local JSON report path instead of thousands of
rows. No settings are saved or content uploaded; included assets may compile into build caches.
For the full review workflow read `sbox-cli --skill sbox-publishing`.

## Patterns

**Find, then act by id.** Tools chain: `find_game_objects` / `scene_tree` return GUIDs that
`get_game_object`, `set_component` etc. take; `asset_search` returns paths that `asset_info`,
`spawn_model` and component properties take.

```json
{"name":"find_game_objects","arguments":{"component":"PlayerController"}}
{"name":"get_game_object","arguments":{"id":"<guid>","includeComponentProperties":true}}
```

**Know a component before setting it.** `get_component_type` gives property names and types.
`add_component` and `set_component` take a `properties` object using the serialized forms
`get_game_object` shows: strings, numbers, `"x,y,z"` vectors, `"r,g,b,a"` colors, asset paths.
`set_component` validates everything before applying anything, and both are undoable.

**Look at it.** `editor_camera_screenshot` (after `set_editor_camera`), `camera_screenshot` in play
mode, `ui_screenshot`, `hammer_screenshot` and `asset_thumbnail` return images. `sbox-cli --call`
saves them to files and prints the paths so you can view them.

**Batch cheap reads.** `call_tools` runs several calls in one round trip:

```json
{"name":"call_tools","arguments":{"calls":[{"name":"editor_status"},{"name":"compile_status"},{"name":"read_console","arguments":{"minimumLevel":"Warn","limit":20}}]}}
```

**Watch the console incrementally.** Every `read_console` result ends with `Cursor: N`. Pass
`since: N` next time to get only newer entries.
