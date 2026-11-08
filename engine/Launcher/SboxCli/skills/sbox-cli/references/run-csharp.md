# run_csharp

`run_csharp` compiles a C# **async method body** against every assembly loaded in the process —
engine, editor, and the project's own game and editor code — and runs it on the engine main thread
in game scope. Use it for anything the dedicated tools don't cover: bulk edits, queries the tools
don't expose, checking an API actually behaves the way you think.

It is trusted local automation with the full privileges of the editor process. Changes it makes
have **no undo**; prefer the scene tools when they can do the job.

## Arguments

Exactly one of:

- `code` — the method body as a string.
- `path` — an absolute path to a UTF-8 file containing the body (≤ 8 MiB). Write it into the
  session exchange directory. Use this for anything longer than a few lines; it avoids JSON
  escaping and compile errors refer to the file's line numbers.

## What's in scope

- `using System; System.Linq; System.Threading; System.Threading.Tasks; Sandbox;` and the game's
  global namespace.
- `await` and `return` (the method returns `Task<object>`). The return value is shaped like any
  tool result: strings as text, objects as JSON, `GameObject`/`Component` as compact references,
  `Bitmap` as an image.
- `exchangeDirectory` (string) and `cancellationToken` (CancellationToken) parameters.
- Editor types work in an editor, but must be **fully qualified** (`Editor.SceneEditorSession`).
- When hotload left several versions of an assembly loaded, the current type library's version wins.

## Examples

```csharp
return new { Thread = ThreadSafe.IsMainThread, Game.IsPlaying, Scene = Game.ActiveScene?.Name };
```

The scene being **edited** (not the runtime copy in play mode):

```csharp
var scene = Editor.SceneEditorSession.Active?.Scene;
return scene.GetAllObjects( true )
    .Where( go => go.Components.Get<ModelRenderer>() is not null )
    .Select( go => new { go.Name, go.Id, Model = go.Components.Get<ModelRenderer>().Model?.ResourcePath } )
    .Take( 50 )
    .ToArray();
```

Long-running work must honour cancellation, or it can't be stopped short of closing the editor:

```csharp
foreach ( var go in Game.ActiveScene.GetAllObjects( true ) )
{
    cancellationToken.ThrowIfCancellationRequested();
    // ...
    await Task.Yield();
}
```

Write outputs where you can read them:

```csharp
var file = System.IO.Path.Combine( exchangeDirectory, "objects.json" );
await System.IO.File.WriteAllTextAsync( file, Json.Serialize( Game.ActiveScene.GetAllObjects( true ).Select( x => x.Name ) ) );
return file;
```

These snippets show the shape of a script; check the engine XML docs or `get_component_type` for
exact member names before relying on them.

## Gotchas

- `Game.ActiveScene` is the runtime scene in play mode. Use `Editor.SceneEditorSession.Active.Scene`
  (or `editor_status`) for the scene the user is editing.
- Compile errors come back as a tool error with line numbers relative to your method body.
- Nothing you do is marked as an undoable edit, and the scene's unsaved flag may not change. Tell
  the user what you changed, and save through `save_scene` if needed.
- Disconnecting cancels the token; code that ignores it keeps running.
