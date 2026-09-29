# Sandbox.SceneRenderer

A C# scene renderer for s&box, intended to replace the native scenesystem's world rendering.
It manages render objects, visibility, lighting and frame execution while using the engine's existing
materials, shaders and graphics backend.

The renderer is experimental. To use it for GameObject scenes, enable `r_managed_scene 1`.
It is disabled by default. [SceneLab](../Launcher/SceneLab/) runs the managed and native renderers
against the same scenes for visual comparison and performance testing.

This README covers the project structure and development workflow. The
[renderer reference](../../docs/managed/scene-renderer.md) contains the implementation details,
native equivalents, performance investigations and debugging history.

## What it renders

- Static and skinned meshes, with LOD selection, instancing, morphs and model deformers.
- Opaque, translucent and faded geometry, decals, glass, viewmodels and screen overlays.
- Clustered point and spot lights; directional, spot and point shadows; contact shadows.
- Environment maps, baked lighting, light probe volumes, 2D and 3D skies, and gradient, cubemap and volumetric fog.
- Custom objects such as particles, sprites, terrain, clutter, debug geometry and world panels.
- Camera command lists, post processing, auto exposure and UI through the existing camera stages.
- Render-to-texture targets, MSAA, tools materials and debug views.

The GameObject bridge mirrors scene changes into the renderer, including material overrides,
body groups, tags, per-object attributes and camera settings.

## Architecture

`RenderWorld` owns the scene data. `RenderView` describes a camera, and `RenderSystem` turns the visible
part of a world into a frame. Features prepare and draw different kinds of objects; layers specify the
steps of the frame. `FrameRecorder` decides which layers are needed and records their work for submission.

Rendering uses a dedicated native `IRenderContext`, rather than native `ISceneSystem`, `ISceneView` or
`ISceneLayer` objects. The renderer still relies on shared engine services, including the material
system, shader code, shadow-map policy and skinning vertex cache.

### Source layout

| Folder | Responsibility |
| --- | --- |
| [World](World/) | Scene foundations: `RenderWorld`, `RenderObject` and the `CustomObject` extension point. |
| [World/Objects](World/Objects/) | Meshes, lights, environment-map objects and decals, plus their `RenderMesh` data. |
| [World/Environment](World/Environment/) | Scene lighting, light probe volumes, fog settings and the 3D skybox. |
| [Culling](Culling/) | The dynamic spatial tree, frustum tests and screen-size culling. |
| [Pipeline](Pipeline/) | Cameras, frames, targets, collection, preparation and command recording. |
| [Pipeline/Features](Pipeline/Features/) | `RenderFeature`, mesh preparation and drawing, and clustered light/probe/decal binning. |
| [Pipeline/Layers](Pipeline/Layers/) | Render passes and their resource dependencies. |
| [Pipeline/Shadows](Pipeline/Shadows/) | Integration with the engine's shared `ShadowMapper`. |
| [Pipeline/Fog](Pipeline/Fog/) | Volumetric fog lighting, temporal accumulation and integration. |
| [Gpu](Gpu/) | Native rendering access, GPU uploads, transform buffers and shader constants. |
| [Bridge](Bridge/) | Synchronization with GameObject scenes and execution of existing camera stages. |
| [Diagnostics](Diagnostics/) | Frame comparison, rendering statistics and profiler instrumentation. |

Folders describe responsibilities; they do not all introduce namespaces. Public types use
`Sandbox.SceneRenderer`. Internal subsystems also use `.Features`, `.Culling`, `.Shadows`, `.Gpu` and
`.Bridge`, imported within the project by [Assembly.cs](Assembly.cs).

Shared math belongs in `Sandbox.System`. In particular,
[Matrix3x4](../Sandbox.System/Math/Matrix3x4.cs) lives alongside `Matrix` and represents the native affine
layout used for bones and transform buffers.

### How a frame runs

`RenderSystem.Render( world, view, target )` renders into a `ViewTarget`. The internal swap-chain
overload is used by the engine and SceneLab.

1. **Collect:** query the spatial tree, test exact bounds and screen size, then group visible objects by feature.
2. **Prepare:** choose LODs, sort draw runs, pack lights and write transform data. The shadow system requests
   additional views, which are culled and prepared in the same way.
3. **Setup:** create or resolve GPU resources, upload constants and buffers, and dispatch feature setup work.
4. **Draw:** record the required layers and submit them in order, resolving or copying the final output as needed.

Collect and Prepare are CPU-only. This keeps them testable without booting the native engine.

The layer order is defined in [RenderSystem.Layers.cs](Pipeline/RenderSystem.Layers.cs).
Layers declare the resources they read and write through `FrameResources`. Planning walks those
dependencies backwards and drops producers whose output is unused. `IsNeeded` handles whether a layer
can do useful work for the current frame.

Large frames record work in parallel. Each recording segment has its own native context, created on
the thread that records it. Shadow culling is also parallelized. Camera stages and custom-object work
that requires the main thread stay there. Small frames avoid the overhead of parallel recording.
With `r_managed_async_compute`, depth-chain, contact-shadow and AO work can overlap shadow-map rendering.

### Connections to the rest of the engine

- [ManagedScene](../Sandbox.Engine/Systems/Render/ManagedScene/) owns the convars, renderer loading,
  scene-change notifications and shutdown integration.
- [ShadowMapper](../Sandbox.Engine/Systems/Render/Shadows/) supplies shared shadow allocation, caching
  and time-slicing policy for both renderers.
- [Graphics.IFrameView](../Sandbox.Engine/Systems/Render/Graphics.FrameView.cs) lets camera effects and
  custom drawing operate on either renderer. Use the `Graphics` abstraction rather than assuming a
  native scene view or layer exists.
- [RenderTools.def](../Definitions/engine/Render/RenderTools.def) exposes native helpers implemented in
  [rendertools.cpp](../../src/engine2/sbox/rendertools.cpp).
- [RenderPipeline](../Sandbox.Engine/Systems/Render/RenderPipeline/) contains existing effect helpers
  reused for depth downsampling, bloom and refraction.

## Building and testing

Run these commands from the repository root. Close SceneLab before building because it locks the
assemblies in `game/bin/managed`. See the [build guide](../../docs/architecture/build-system.md) for
initial setup and native build options.

```powershell
# Build managed code and regenerate bindings, using the existing native build.
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- build --skip-native

# Run the renderer's unit tests after building.
dotnet test -c Release --no-build engine/Tests/Sandbox.Test.Unit --filter "FullyQualifiedName~SceneRendererTests"
```

The [unit tests](../Tests/Sandbox.Test.Unit/SceneRenderer/) cover culling, object storage, transforms,
GPU layouts, light packing, layer selection and allocation behavior. Tests that prepare frames use
`[DoNotParallelize]` because `ShadowMapper` and `Application.FrameCount` contain shared static state.

Visual changes require a parity sweep and inspection of the captured images. A passing comparison
alone cannot show that a feature rendered correctly: both renderers might have drawn nothing or used
the same missing asset. A new feature needs a SceneLab preset and a matching native reference.
Disable the feature once to confirm the comparison detects its absence.

### Visual comparison

With a game loaded, run these commands in the console:

```text
r_managed_scene 1
r_managed_scene_compare 120
```

The comparison waits 120 camera renders, then captures the next camera through both renderers.
Results go to the console and images to `game/screenshots/managed_scene/`. Allow the scene to finish
loading before comparing.

In SceneLab, use **View > Compare With Native** for the current scene. To run a preset sweep, launch
the built-in runner directly from `game/`:

```text
bin/managed/scenelab.exe -parity
bin/managed/scenelab.exe -parity "Sun Shadows;Reflections" -zoom 0.3
```

The sweep writes its results and captures to `game/screenshots/scenelab/parity/`, and exits with code 1
if any comparison fails. Use `-parity-out <folder>` to choose another output location.

- `<scene>.png`: the managed frame.
- `<scene>.native.png`: the native frame.
- `<scene>.diff.png`: the managed frame dimmed, with differences highlighted in red.

Effects with shared or temporal state need isolated comparisons. Use `-parity -isolated` for those checks;
AO and SSR presets already request isolation. Test scene changes as well as initial rendering, and
check representative games when changing the bridge.

Changes to the shared shadow mapper also require a native-to-native comparison. Run
`bin/managed/scenelab.exe -parity -save-native before` from `game/`, make the change, then run
`bin/managed/scenelab.exe -parity -compare-native before`.
For deterministic presets, those frames should be byte-identical.

### Performance and frame inspection

From `game/`, run:

```powershell
bin/managed/scenelab.exe -scene "All Shadows"
bin/managed/scenelab.exe -benchmark "10k Boxes;50k Mixed" -novsync
```

Benchmark results go to `game/screenshots/scenelab/benchmark.md`. Warm up maps before measuring so asset
loading, JIT compilation and shader compilation do not dominate the result. Include scene switching
when testing resource lifetime.

Use `overlay_scene_plan 1` in a game, or **View > Frame Plan** in SceneLab, to see active layers,
resource dependencies, recording segments and timings. Level `2` also shows skipped layers.
Tracy zones are defined in [Diagnostics/Zones.cs](Diagnostics/Zones.cs); they cover collection,
preparation, setup, recording, worker waits and submission. SceneLab's `-serialrecording` switch helps
compare serial and parallel execution.

## Development conventions

Read the relevant section of the [renderer reference](../../docs/managed/scene-renderer.md) before
changing a subsystem. When porting behavior, cite the native function or file in the code comment so
the implementation can be checked against its source.

### Allocation and threading

- Steady-state per-frame work must not allocate. Reuse buffers and pool per-view state by stable
  identity, such as a shadow map and face. `PipelineTests.CollectAndPrepareDoNotAllocate` checks the
  collection and preparation path, including shadows.
- Avoid LINQ, interface-based enumeration, iterators, allocating `params` calls, string formatting,
  capturing lambdas and repeatedly created delegates in hot paths.
- Resolve textures, bindless indices and other native resources during Setup, not Collect or Prepare.
- Worker jobs read prepared state and write only their own context, attributes and view-local lists.
  Warm mutable caches before dispatching jobs; do not lazily create materials on a recording thread.
- Mark work that needs `Graphics` or other main-thread-only engine code through
  `RenderFeature.CanRecordOffMainThread`.
- Each pass binds its own targets, viewport, depth range and view constants through `BeginPass` or
  `BindFrame`. Use `SetPassCombo` for pass-local shader choices. A pass must not depend on state left
  by the previous pass.

### GPU resources and interoperability

Keep native rendering calls in `Gpu/RenderContext`. Add new native helpers through `rendertools` and
the `.def` bindings, then regenerate with `tools/BuildBindings.bat`. Never hand-edit generated interop.
Native changes require a native build; shader changes require shader compilation before parity testing.

Dispose owned GPU resources before native shutdown. Cached materials returned by `Material.FromShader`
are shared; only destroy materials created and owned by the caller. Context state changes must go
through `RenderContext` helpers so cached material bindings are invalidated correctly.

GPU structs must match the native and shader layouts, including packing and matrix conventions.
Check those layouts when updating native dependencies. Skinning uses the engine's shared vertex cache;
morph and vertex-cache preparation needed by this renderer must complete before its draw submission.

### C# style

Use the engine's `Matrix`, `Vector2`, `Vector3`, `Vector4` and `Rotation` types. They already wrap
`System.Numerics`; direct Numerics usage needs a concrete reason, such as an interface that requires it.
Avoid shorthand aliases such as `V3`, `V4` and `M4`. Remember that `Vector3 ==` is approximate; use
`Equals` when cache invalidation or validation requires exact equality.

Follow the repository's `.editorconfig`: tabs, spaces inside parentheses, `_camelCase` instance fields
and `s_camelCase` mutable static fields. Prefer collection expressions and target-typed `new` where
appropriate, while retaining preallocated capacity for scratch arrays. Document public types and
members (`CS1591` is an error here), and give internal types a summary explaining their role.

## Extending the renderer

### Add an object type

1. Derive from `RenderObject` in `World/Objects/`. Override `SizeCulled` for objects that should stay out
   of the size-culling tree, as lights and probes do.
2. Add its preparation and drawing in `Pipeline/Features/`, using the `RenderFeature` contract.
   Include shadow support and the main-thread recording requirement where applicable.
3. Register the feature in `RenderSystem`. Registration order matters; light binning runs first.
4. Add bridge synchronization where needed, a SceneLab preset, and its native equivalent in `NativeScene`.

### Add a layer

Add the layer in `Pipeline/Layers/` and register it in `RenderSystem.Layers.cs` at the appropriate point.
Declare resource `Reads` and `Writes`; let dependency planning decide whether a producer's output is
needed rather than duplicating all reader conditions in `IsNeeded`.

Mesh layers select their run lists and shader mode. Additional run lists belong in `MeshRuns` and the
classifier in `MeshRenderFeature.Lists.cs`, with a corresponding `ClassifierTests` case.

### Add shader inputs or bridge state

Use native view setup and light-binning code as the reference for shader inputs. Missing constants
often produce incorrect pixels without an error. `ViewEnvironment` supplies shared environment inputs;
each pass must still supply constants for its own target and viewport.

Bridge changes must preserve native object flags, layer membership, material overrides and camera
attributes. Read native draw state rather than deriving it again. New state setters must notify the
mirror, including changes made indirectly by native code. Verify the scene both before and after a change.

## Debugging and remaining work

For incorrect frames, start with pass constants, resource dependencies, object flags and shader modes.
Use debug views to isolate shading terms and inspect the depth chain for depth-dependent effects.
Check that the assets exist and that both renderers have comparable temporal history.

The [renderer reference](../../docs/managed/scene-renderer.md) records detailed investigations into
color space, shadow behavior, skinning, material binding, bridge synchronization and parallel recording.

Remaining work includes moving more camera effects into this renderer, threading main-view collection
and preparation, and reducing native per-draw overhead. Known gaps include morphs on meshes without a
skeleton, glass shadow dithering differences, layer-specific attribute overrides and DDGI parity with
baked test data. An intermittent native shutdown crash has also been observed in 256-light scenes;
it still needs a native stack trace.
