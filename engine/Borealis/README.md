# Borealis

A C# scene renderer for s&box, intended to replace the native scenesystem's world rendering.
It manages render objects, visibility, lighting and frame execution, and draws through a device its host supplies
(`GpuDevice`, `GpuContext`). In s&box that is [Borealis.Backend](../Borealis.Backend/) on Borealis.Gpu, Borealis's own
Vulkan backend, with the game's own compiled shaders, materials and textures.

Borealis knows nothing about GameObject scenes, cameras or native's scene views. s&box connects to it from
[Sandbox.Rendering](../Sandbox.Rendering/README.md), which mirrors scenes into Borealis and also holds the layers of
native's own rendering pipeline. Keep it that way: code that needs `SceneObject`, `SceneWorld` or `ISceneView` goes there.

The renderer is experimental. A process launched with `-borealisgpu` draws on Borealis.Gpu and renders every
GameObject scene through Borealis; any other process renders through native's scene system.
[BorealisLab](../Launcher/BorealisLab/) runs Borealis on its own Borealis.Gpu device, for captures, render tests and
benchmarks.

This README covers the project structure and development workflow. The
[renderer reference](../../docs/reference/borealis.md) contains the implementation details and
native equivalents, and [managed-renderer.md](../../docs/reference/managed-renderer.md) lists what breaks it.

## What it renders

- Static and skinned meshes, with LOD selection, instancing, morphs and model deformers.
- Map world geometry in aggregates, culled per fragment and drawn as one multi-draw each.
- Opaque, translucent and faded geometry, decals, glass, viewmodels and screen overlays.
- Clustered point and spot lights; directional, spot and point shadows; contact shadows.
- Environment maps, baked lighting, light probe volumes, 2D and 3D skies, and gradient, cubemap and volumetric fog.
- Custom objects such as particles, sprites, terrain, clutter, debug geometry and world panels.
- Camera command lists, post processing, auto exposure and UI through the existing camera stages.
- Render-to-texture targets, MSAA, tools materials and debug views.

The GameObject bridge in Sandbox.Rendering mirrors scene changes into the renderer, including material overrides,
body groups, tags, per-object attributes and camera settings.

## Architecture

`RenderWorld` owns the scene data. `RenderView` describes a camera, and `RenderSystem` turns the visible
part of a world into a frame. Features prepare and draw different kinds of objects; layers specify the
steps of the frame. `FrameRecorder` decides which layers are needed and records their work for submission.

Rendering records through the host's `GpuDevice` and `GpuContext`, not native `ISceneSystem`, `ISceneView` or
`ISceneLayer` objects. The renderer still shares engine code with native's: shader sources and the shadow-map policy.

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
| [Gpu](Gpu/) | The device abstraction (`GpuDevice`, `GpuContext`), GPU uploads, transform buffers and shader constants. |
| [Diagnostics](Diagnostics/) | Frame comparison, rendering statistics and profiler instrumentation. |

Folders describe responsibilities; they do not all introduce namespaces. Public types use
`Borealis`. Internal subsystems also use `.Features`, `.Culling`, `.Shadows` and `.Gpu`,
which are imported within the project by [Assembly.cs](Assembly.cs).

Shared math belongs in `Sandbox.System`. In particular,
[Matrix3x4](../Sandbox.System/Math/Matrix3x4.cs) lives alongside `Matrix` and represents the native affine
layout used for bones and transform buffers.

### How a frame runs

`RenderSystem.Render( world, view, target )` renders into a `ViewTarget`.

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
When the host allows async compute (`RenderHost.AsyncCompute`), depth-chain, contact-shadow and AO work can overlap
shadow-map rendering; s&box's host (`SboxRenderHost`) turns it off. Borealis draws no normals G-buffer: AO rebuilds normals from depth, and SSR is off.

Declare each pass's inputs, outputs and compute capability. The pipeline itself stays a plain list:

```csharp
// On the AO pass:
public override FrameResources Reads( RenderFrame frame ) => FrameResources.DepthChain;
public override FrameResources Writes => FrameResources.AmbientOcclusion;
public override bool SupportsAsyncCompute => true;

// In the pipeline, in serial execution order:
new DepthChainLayer(),
new EarlyStageLayer( Stage.AfterDepthPrepass, PostProcessQueue.AsyncCompute ),
contactShadows,
new EarlyStageLayer( Stage.AfterDepthPrepass, PostProcessQueue.Graphics ),
new ShadowLayer(),
new ShadowsReadyLayer(),
```

`FrameRecorder` finds consecutive compute-capable passes and the following independent graphics work. It
splits compute submissions at the results graphics needs and inserts waits for read/write, write/read and
write/write conflicts. Read-only sharing needs no wait. Graphics producers submit before the next compute
batch's handoff; outstanding compute joins before that handoff, another frame or untracked work. No groups,
named pass dependencies or manual semaphore wiring are required in the pipeline.
Reusing an earlier compute pass's input or output starts another batch, so graphics preparation cannot clear it too early.

`Reads` defaults to `FrameResources.Untracked`: an undeclared pass is a conservative scheduling boundary.
To allow overlap, override it with all resources the pass reads (`None` for no inputs), and declare its
outputs in `Writes`. Include `Untracked` alongside known inputs for arbitrary camera hooks or custom drawing.
These declarations also drive output culling; `HasSideEffects` keeps work such as the depth prepass even
when no later pass explicitly reads its output. This does not inspect shader bindings or allocate resources.

Each queue preserves list order. Disabled/unsupported async compute and windows with no independent graphics
work stay serial. CPU worker thresholds do not control GPU overlap. Graphics-only preparation and result
transitions remain in `HandOff` and `TakeBack`; preparation must only initialize outputs and transition inputs
that already exist before the batch. Early camera effects stay at their normal stage on the serial path.

### Connections to the rest of the engine

- [ManagedScene](../Sandbox.Engine/Systems/Render/ManagedScene/) owns the `-borealisgpu` check, the convars, scene-change notifications
  and shutdown integration. The app system registers Sandbox.Rendering's bridge with the engine at startup
  ([EngineHooks.cs](../Sandbox.Rendering/EngineHooks.cs)).
- [ShadowMapper](../Sandbox.Engine/Systems/Render/Shadows/) supplies shared shadow allocation, caching
  and time-slicing policy for both renderers.
- [Graphics.IFrameView](../Sandbox.Engine/Systems/Render/Graphics.FrameView.cs) lets camera effects and
  custom drawing operate on either renderer. Use the `Graphics` abstraction rather than assuming a
  native scene view or layer exists.
- [RenderTools.def](../Definitions/engine/Render/RenderTools.def) exposes native helpers implemented in
  [rendertools.cpp](../../src/engine2/sbox/rendertools.cpp), which the bridge uses to read native draw and light state.
- [Gpu/SharedPasses.cs](Gpu/SharedPasses.cs) holds the depth chain, quarter depth and bloom and refraction
  targets, built from the same shaders as native's pipeline's own (`Sandbox.Rendering/Pipeline/SharedPasses.cs`).

## Building and testing

Run these commands from the repository root. Close BorealisLab and SceneLab before building because they lock the
assemblies in `game/bin/managed`. See the [build guide](../../docs/reference/internals.md) for
initial setup and native build options.

```powershell
# Build managed code and regenerate bindings, using the existing native build.
dotnet run --project engine/Tools/SboxBuild/SboxBuild.csproj -- build --skip-native

# Run the renderer's unit tests after building.
dotnet test -c Release --no-build engine/Tests/Borealis.Test
```

The [tests](../Tests/Borealis.Test/) run on a headless device that records what frames submit, with
nothing of the engine. They cover culling, object storage, transforms, GPU layouts, light packing,
layer selection, allocation behavior, and the order and queues of a whole frame's work. Tests that prepare frames use
`[DoNotParallelize]` because `ShadowMapper` and `Application.FrameCount` contain shared static state.

Visual changes require inspecting captured images. A passing comparison alone cannot show that a feature rendered
correctly: both sides might have drawn nothing or used the same missing asset. Disable the feature once to confirm the
comparison detects its absence.

### Visual checks

BorealisLab's render tests capture the lab presets on a Borealis.Gpu device and check them against Borealis's approved
images in `engine/Tests/GoldenTruth/Borealis` (`-golden-truth`). `SboxBuild borealis-render-tests` runs the core suite;
`-golden-truth-update` writes a capture as the approved image. See [borealis-host.md](../../docs/reference/borealis-host.md)
for the switches and output.

Against native, compare a game launched with `-borealisgpu` with the same game launched without it. BorealisLab's
`-post-parity` and `-exposure-parity` check post processing and auto exposure against Source 2's passes, and
`-model-parity` checks Borealis.Source2's model reader against the engine's loading.

Changes to the shared shadow mapper also need a native-to-native comparison. Run
`bin/managed/scenelab.exe -parity -save-native before` from `game/`, make the change, then run
`bin/managed/scenelab.exe -parity -compare-native before`.
For deterministic presets, those frames should be byte-identical.

### Performance and frame inspection

From `game/`, compare Borealis.Gpu against native on the same map:

```powershell
bin/managed/borealislab.exe -construct -real
bin/managed/scenelab.exe -benchmark "Construct Overview"
```

`-construct -real` times Construct's frames on Borealis.Gpu, recorded on one thread and on several. Warm up maps before
measuring so asset loading, JIT compilation and shader compilation do not dominate the result. Include scene switching
when testing resource lifetime.

The frame plan on the dev tools' Rendering tab (`DebugOverlay.ScenePlan`) shows active layers, resource dependencies,
recording segments and timings.
Borealis's own debug views - LOD colours, cull reasons and a shadow map viewer (`r_managed_debug_lod`, `r_managed_debug_cull`,
`r_managed_debug_shadows`, under `-borealisgpu`) - are in [Diagnostics/DebugViews.cs](Diagnostics/DebugViews.cs) and the
renderer reference's *Debug views*.
Tracy zones are defined in [Diagnostics/Zones.cs](Diagnostics/Zones.cs); they cover collection,
preparation, setup, recording, worker waits and submission.

## Development conventions

Read the relevant section of the [renderer reference](../../docs/reference/borealis.md) before
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

Borealis reaches the GPU only through `GpuDevice` and `GpuContext`; what a device does with them is Borealis.Backend's.
Native helpers the bridge needs go through `rendertools` and the `.def` bindings, regenerated with
`tools/BuildBindings.bat`. Never hand-edit generated interop. Native changes require a native build; shader changes
require shader compilation before testing.

Dispose owned GPU resources before shutdown. Materials returned by `GpuDevice.MaterialFromShader` are shared; only
destroy materials created and owned by the caller.

GPU structs must match the shader layouts, including packing and matrix conventions. Skinning writes the device's
vertex cache (`GpuContext.AllocateVertexCache`); morph and vertex-cache preparation must complete before draw submission.

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
4. Add bridge synchronization in Sandbox.Rendering where needed, and a lab preset (`LabPresets`) for BorealisLab's captures.

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

The [renderer reference](../../docs/reference/borealis.md) covers color space, shadow behavior, skinning,
material binding, bridge synchronization and parallel recording, and
[managed-renderer.md](../../docs/reference/managed-renderer.md) lists the rules that matter most.

Remaining work includes moving more camera effects into this renderer, threading main-view collection
and preparation, and reducing per-draw overhead. Known gaps include morphs on meshes without a
skeleton, glass shadow dithering differences, layer-specific attribute overrides and DDGI parity with
baked test data. An intermittent native shutdown crash has also been observed in 256-light scenes;
it still needs a native stack trace.
