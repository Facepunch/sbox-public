# SceneLab — agent guide

> Scope: `scenelab.exe`, the proving ground for the managed scene renderer. Last reviewed: 2026-11-07.
> The renderer's own guide is [`engine/Sandbox.SceneRenderer/README.md`](../../Sandbox.SceneRenderer/README.md);
> read it first. What breaks it is in [managed-renderer.md](../../../docs/reference/managed-renderer.md).

SceneLab is to the managed renderer what PanelGallery is to panels. It's a borderless `PanelWindow` that draws
a `RenderWorld` into its swap chain, with panel UI over the top: a menu bar and a stats overlay. It can draw
the same world through native too, to compare the two.

## Files

| File | What it does |
|---|---|
| `SceneLabAppSystem.cs` | Boots the panel app and opens the window. `DrawsScenes` keeps the engine's scene buffers full size (no `-panelapp`) and loads the native modules a game does (world renderer, physics, animation, sound: `SourceEnginePanelAppInit`), so maps load. The owner's call (2026-09-24): SceneLab doesn't need the launcher's slim startup. |
| `SceneLabWindow.cs` | The window. It holds the menus and the stats overlay. `Load` builds a preset's world, `OnRenderBackground` renders each frame, and it handles capture, parity and the benchmark hookup. |
| `SceneLabScene.cs` | What a scene is: models on a grid, lights, floor, probe, fog, shadow settings, or a GameObject scene (`Game`, with `ChangeGame` and a per-frame `Frame` hook). It holds the `Presets` (the Scene menu, a submenu per `Group`) and the command line parsing. `Checker` gives every other object another material or tint alpha. `Build` places objects by hand instead of the grid (`LabLayout`: meshes, standing them on the floor, the camera and its near and far planes), and `Origin` moves the whole scene away from the world's origin. |
| `CoreScenes.cs`, `LabLayout.cs` | The lab presets placed by hand: Texture Formats, Texture Filtering (bark's `F_TEXTURE_FILTERING`, strips at a grazing angle), Non-Uniform Scale, Mirrored, Depth Range (near 0.05, far two million), Material Mix (one model, a material per pass), Shadow Casters and Shadow Bias. Far From Origin is All Shadows 100 km out (`Origin`). |
| `TestTextures.cs` | Runtime textures with full mip chains: uncompressed, half float, and BC1/BC3/BC4/BC5/BC6H/BC7 from a small encoder of its own, plus a texture whose mips are each a colour. |
| `SceneLabWindow.Game.cs` | GameObject presets: loading, the orbit camera around `GameFocus`, ticking the scene, rendering through the bridge or natively, and their captures, compared through the bridge's compare before and after `ChangeGame`, or with `-golden-truth` checked against golden truth images. `WithGameUI` swaps the window's `UISystem` in for screen UI. |
| `MapScenes.cs` | Maps from the cloud through a `MapInstance` (the Maps group: Flatgrass, Flatgrass Skyline, out on the grass looking at the 3D skybox, Flatgrass Interior, inside the building under its skylights, and Flatgrass Props, low over grass clumps lit from light probe volumes). It sets up what a game's boot does for downloads and a panel app's doesn't (downloads folder, asset cache, `ServerPackages`), names the map once the backend is up (`PanelAppSystem.ApiReady`), and says when it's loaded (`SceneLabScene.Ready`), so captures wait for it. |
| `GameScenes.cs` | The GameObject presets past the bridge scene: post processing, depth of field, auto exposure, screen UI and a HUD, early UI, a world panel, the output layers (overlays after post processing, after-UI objects), sprites and particles, decals, decal geometry (the decal layer), tools views and debug visualisations (the Tools group), contact shadows, volumetric fog (its camera rendered natively every frame too, `NativeAlongside`), refraction (glass: the frame buffer copy and the refraction stencil), soft particles (the depth chain), a viewmodel (the game overlay layers), the viewmodel's depth chain (the overlay prepass stencil) and bloom objects (the bloom layer). Each with a change to compare after, where there's one to make. |
| `OutfitScenes.cs` | Workshop clothing on explicit neck and nose deformation fixtures (the Avatar group). Hotdog Costume (`glitchworkshop.thehotdogcostume`), Banana Suit (`falkoworkshop.mrbananasuit`) Katana (`glitchworkshop.katana`) and Golf Backpack (`tarbaganchik.golfbackpack`) each download their package once the backend is up, and Pearl Necklace loads the citizen addon's own, and each dresses three citizens in it; Fried Onion Earrings (`pkxuni.earring_onio`) frames four faces close: the deforms reaching the earrings, opted out, opted out under a big head of its own (an inflate deformer), and rigid under it (`SkinnedModelRenderer.DeformationMode`); All Outfits puts every one in one scene, three groups across the front and two behind, each named and its citizens tagged (`Ready` waits for that), each labelled by a world panel at its feet: a reference without deformers, one whose deforms (a thin neck and big nose) reach the costume, and one whose costume has `DeformationMode` set to `None`, so its body is deformed and the costume isn't. |
| `DynamicScene.cs` | The Dynamic preset (Stress group): 400 props fading, recolouring and moving every frame, four circling shadowed point lights, sprites, particles and a second camera rendering into a texture - the renderer and bridge measured under change and with two cameras. |
| `NativeReference.cs` | `NativeScene` mirrors the world as native `SceneModel`s, lights, cubemaps and fog. `NativeReference.Compare` is the per-pixel comparison. |
| `HeavyScene.cs` | The Heavy Scene preset (Stress group): 900 varied citizen props (overrides, attributes of their own, a tenth moving), 40 animating citizens, 24 circling shadowed point and spot lights, the sun's contact shadows, glass, sprites, particles, tonemapping and bloom - a busy game's frame, GPU included. `-width 3840 -height 2160` makes it GPU-bound. Heavy Scene AO adds ambient occlusion, without the particles, since it compares isolated. |
| `OcclusionScene.cs` | The Occlusion City preset (Stress group): a street-level lap of a city of hollow buildings with props along the pavements and props and dense sculptures (about 200k triangles each) in their courtyards, one lap per 600 frames - the case culling is for, so a regression shows in its GPU time. View > Occlusion Culling (or `+r_managed_occlusion 1` / `+r_occlusion_culling 1`) switches culling and shows the occluded count. The benchmark runs presets marked `CompareOcclusion` with culling off and then on. |
| `TerrainScenes.cs` | Terrain and Clutter (rolling terrain under scattered boxes and spheres), and three Stress presets over one forest: about 100k clutter spheres across a 32768 square, seen with a 90° field of view and a 100k far plane, turning a full circle per 600 frames. Clutter Forest is that forest flat under the sun, so its cascades come from every side; Clutter Terrain Forest scatters it onto rolling hills with the eye just over the ridges, so the terrain clipmap draws into the cascades too and the hills hide part of the forest; Clutter Point Lights swaps the sun's shadows for a ring of 12 shadowed point lights around the camera (3000 out, 4000 radius), so what's measured is 72 cube faces of forest. View > Shadow Caster Culling (or `+r.shadows.castercull 0 +clutter_shadow_caster_cull 0`) switches both the shadow view cull and clutter's caster volume cull; the benchmark runs presets marked `CompareCasterCull` with them off and then on. |
| `Benchmark.cs`, `FrameTimer.cs` | `-benchmark`: both renderers, warm-up, measured frames, results table, and the managed main thread by section (collect, prepare, setup, recording wall time and wait, the workers' total, stages, submit) from `RenderStats`. `-gpuprofile` adds GPU time by layer, from the engine's GPU profiler. |
| `SceneViewport.cs` | The see-through panel the world renders into: orbiting and zooming. |
| `LodTestModel.cs`, `SkyCube.cs` | Test assets built at runtime: a model whose LODs are different shapes, and a real cubemap for fog. |
| `BridgeScene.cs` | The GameObject Bridge preset (and `-gamescene builtin`): a GameObject scene built from components, for the GameObject bridge. |
| `PlanOverlay.cs` | The frame plan over the viewport (View > Frame Plan, `-plan [level]`): the engine's `overlay_scene_plan`, drawn by a panel, and saved beside a capture as `<name>.plan.png`. |
| `ParityRun.cs` | `-parity`: every preset or some compared with native one after another in this process (about a minute for all of them), isolated presets in two fresh passes, native frames saved or compared across a change (`-save-native`/`-compare-native`), a table at the end (`parity.txt`) and exit code 1 on a failure. |
| `GoldenTruthRun.cs` | `-golden-truth`: a capture checked against committed golden truth images instead of native (`engine/Tests/GoldenTruth/Windows.Vulkan/<Device>/<Preset>.png`, e.g. `AllShadows.png`, plus `AllShadows.variantN.png` alternates for a device that renders differently on different machines), by a histogram of each pixel's largest channel difference against `thresholds.jsonc`, with Vulkan validation messages failing the preset unless `validation.jsonc` allows them. Writes `.results.json` and `.diff.png` beside the capture. |

## The one rule

**Everything the managed world can express has to reach `NativeScene` too.** If you add a setting to a
`RenderObject`, `SceneLighting` or a preset, mirror it in `NativeReference.cs`. Otherwise parity compares
different scenes, and a pass means nothing. The mirror uses the engine's own types where they exist: the
native sun, `SceneCubemap`, `GradientFogSetup`, `CubemapFogController`, material overrides.

## Running it

From `game/`:

```
bin/managed/scenelab.exe -scene "All Shadows"                          # interactive
bin/managed/scenelab.exe -scene "Reflections" -capture out.png -native # parity; exit code 2 on failure
bin/managed/scenelab.exe -benchmark "10k Boxes;50k Mixed" -novsync     # ; separates scenes
bin/managed/scenelab.exe -benchmark "Clutter Forest;Clutter Terrain Forest;Clutter Point Lights" -novsync -gpuprofile # caster cull off vs on
bin/managed/scenelab.exe -parity                                    # compare every preset
bin/managed/scenelab.exe -parity "Sun Shadows;Reflections"            # compare selected presets
```

Other switches:

- **Scene:** `-model`, `-grid`, `-lights`, `-lightradius`, `-spot`, `-sun`, `-lightshadows`, `-floor` build a
  scene from the command line.
- **GameObject scenes:** the GameObject presets (groups GameObject, Post Processing, UI and Effects) build a scene
  from engine components and core assets, and with `-capture -native` compare its camera through native and the
  managed renderer's GameObject bridge, then again ten frames after their change (`<name>_changed.png`).
  `-gamescene builtin` is the GameObject Bridge preset, and `-gamescene path.scene` loads a scene file; their frames
  go to `screenshots/scenelab/gamescene`. The exit code is 2 on a failure. A panel app doesn't register game resources
  (`Clothing`, `DecalDefinition` and the like) the way a game does, so `GameScenes.LoadResource` loads them from their
  compiled files. It has no game `UISystem` either, so screen UI renders with the window's (`WithGameUI`).
- **Native against itself** (`scenelab.exe -parity -save-native before` / `scenelab.exe -parity -compare-native before`) works for every preset but Auto Exposure,
  which adapts in real time, Sprites and Particles and Soft Particles, whose particles are random, Dynamic, which
  animates, and the two Flatgrass maps: native differs run to run on those, while they still match managed within a
  run. The GameObject Bridge's first frame and Spot Light have each flaked once; rerun them. `parity/before_post` holds the older presets' native frames
  and `parity/stages` the other GameObject presets' (2026-09-24).
- **A new GameObject preset:** a `Func<Scene>` in `GameScenes.cs` (start from `Stage()`), a `ChangeGame` that changes
  it through its components so the second compare proves changes arrive, and an entry in `Presets` with its `Group`.
  Switch the feature off once and see the compare fail, or the pass means nothing. Check its frame isn't one flat
  colour: native parity treats pixels matching the first one as background and skips them, so a flat frame compares
  nothing and passes, and a flat golden truth image tells a reviewer nothing. The Better Fog sky presets were flat red
  until 2026-11-07, when their camera was turned to look into a corner of the sky.
- **Isolated:** `-isolated` captures a GameObject preset through the chosen renderer alone: the camera renders once a
  frame, and on capture frames into a readback target instead of the swap chain. With `-native-renderer` it saves
  `name.native.png`; without, `name.png`, compared with the native one when it's there (exit code 2 on a failure).
  `-parity` does the same in one process: an isolated preset gets a native pass and then a managed pass, each from a
  fresh load after 10 frames with nothing loaded, so the render target pool (which frees targets unused for 8 frames)
  has forgotten the last pass's effect history. The results match separate processes (SSR 0.33/255 either way; without
  the idle, the managed pass inherited native's history and failed at 5.47). Presets marked `Isolated` (AO, SSR) always
  run that way; `-isolated` makes every GameObject preset do so.
- **Parity in one process:** `-parity ["A;B"] [-parity-out folder] [-save-native name | -compare-native name]
  [-parity-timeout s]`. A preset that doesn't finish in 180 s, or throws while loading, is reported and skipped. Native
  frames are repeatable within this mode but not always between it and one-process-per-preset runs (a preset's native
  frame can differ in the last bit after the ones before it, e.g. the citizen presets), so save and compare native
  frames in the same mode.
- **Golden truth:** `-golden-truth folder [-golden-truth-update] [-golden-truth-device name]`, with `-capture` or
  `-parity`, checks presets against golden truth images rather than native. A GameObject preset renders nothing
  natively: its capture frames render the managed camera into a readback target, as an isolated capture does (so an
  isolated preset gets no native pass), and the frame after its `ChangeGame` is checked against `<Preset>Changed.png`.
  Every run renders the same pixels: textures load whole (`SetForcePreloadStreamingData`) instead of streaming in over
  real time, which had moved the citizens' heads and reflections by a few mips, scenes tick at a fixed 1/60 s, and every
  GameObject load seeds the particles' random numbers (`ParticleRandom`) and `Game.Random`. `SboxBuild render-tests
  [--suite core|extended|all] [--preset X] [--update | --from-ci [run]]` renders presets on Lavapipe, Mesa's software Vulkan driver
  (fetched into `src/thirdparty/lavapipe`, the runner's tool cache on CI, or `LAVAPIPE_ICD`), at 640x360 with
  `-vulkan_allow_cpu -vulkan_enable_validation -vulkan_validation_warnings` and `-capture-frame 10` (Lavapipe takes up
  to a couple of seconds over a post processed frame), so it needs no GPU. Its presets come in two suites
  (`RenderTests.Core` and `RenderTests.Extended`): core is the renderer itself in lab presets (textures, lights,
  shadows, fog, reflections, sky, blending, LOD, precision), extended is features built on it in GameObject presets
  (post processing, effects, UI, fog). Append new presets to the right one. The presets are dealt round-robin into four
  SceneLab runs side by side (each keeps about two cores busy: Lavapipe executes a frame's command buffers, and compiles
  its shader variants, on one thread), in the same runs on every machine, with `-parity-timeout 600` for slow runners.
  Mirrored isn't in core yet: the managed renderer doesn't flip a mirrored object's winding (native does,
  `BATCH_FLAGS_FLIP_WINDING`), so it draws them inside out and fails parity. It writes `index.html`
  beside the frames (CI uploads the folder on failure): a tile per preset (a changed frame gets its own), failures
  first, and an inspector with split compare, capture, golden truth, heatmap and over-threshold views, keys 1-5, j/k
  between presets, f for failing only. Both suites take about 4.5 minutes here (9.5 in one run) and five times that on
  a CI runner. Terrain and Clutter, Morphs and Model Deformers repeat exactly too but are left out of extended for
  time (3.5 minutes between them). Lavapipe is pixel-exact run to run (Ambient Occlusion's first frame once came out
  with 4 pixels 1 off, inside the default threshold); a GPU differs from it by 40-80 dB PSNR, so each device has its own
  bucket. Lavapipe also differs a little between CPU models, enough to fail some GameObject presets, so golden truth
  images come from CI: `render-tests --from-ci [run ID or URL]` downloads a run's
  `render-tests` artifact, the newest for the commit checked out without one, and copies its failing captures over their
  golden truth images (`--preset` narrows it; needs `gh`). The report's accept command names its run. Local
  `--update` warns. A Lavapipe run deletes the GPU's Vulkan pipeline cache (vendor mismatch), so the next GPU run recompiles its
  pipelines. It fails if a requested preset isn't checked, or if the validation layer doesn't load (no Vulkan SDK)
  rather than passing unvalidated. On CI it runs after the tests: on a pull request only when the PR changes rendering
  (`RenderTests.RenderingPaths`), which also gives it the `render-tests` label; add that label to run them anyway or
  `skip-render-tests` to skip them. Every master commit runs them all, telling Slack on a failure without holding up
  staging.
- **Runtime textures and materials:** a texture given a full mip chain needs `WithStaticUsage()`, or the engine makes it a
  render target to generate mips (which a BC format can't be) and it comes out blank; the chain goes smallest mip first, as in
  a vtex. `LINEAR_RGBA8888` is undefined on Vulkan; use `RGBA8888_LINEAR`. Make each material whose features change its
  samplers with `Material.Create`: copies of one `Material.FromShader(...)` given different `F_TEXTURE_FILTERING` all drew
  with the first one's sampler natively (2026-11-07).
- **Animation:** `-sequence-time f` (0 to 1) poses skinned presets at another frame. Skinned objects are posed by a
  `SceneModel` in a world that's never rendered, and the native reference sets the same bones on its own.
- **Camera and window:** `-zoom`, `-width`, `-height`. The size is in pixels whatever the display scale, so a capture
  is the size asked for; without either the window is 1280x760 in UI units.
- **Renderer:** `-noprepass`, `-nosizecull`, `-noshadows`, `-debugshader`, `-native-renderer`, `-novsync`,
  `-serialrecording` (record every segment on the main thread, for the lab's renderer and the bridge's alike).
- **Target:** `-target` renders into a `ViewTarget` instead of the swap chain; `-msaa 2|4|8` multisamples it and
  `-targetformat` picks its colour format (RGBA16F by default). With `-native`, a multisampled capture's
  reference comes from native `RenderToTexture` at the same count, three frames later.
- **Window:** multisampled at 4x by default, as a game's is; `-swapchain-msaa 2|8` for another count, `-swapchain-msaa 1`
  for none (the native frames saved before 2026-09-29 were captured without).
- **Capture:** `-capture-frame` (default 30). `-debugmode <SceneCameraDebugMode>` captures a GameObject preset's debug
  visualisation instead of its lit frame, e.g. `-debugmode AmbientOcclusion` for the AO alone; with `+r_ao_reference 128`
  for a converged ground truth to compare AO changes against.
- **Console variables:** `+name value` sets one before the first scene loads (`ConVarSystem.Run`), managed ones included
  (SceneLab registers the engine's, under its own cookies; before 2026-09-27 they were unknown commands). Native scenesystem
  convars such as `sc_fbcopy_depthaware_mask` aren't reachable this way.
- **Changed a core shader?** `build` restores the published `.shader_c` over `game/core/shaders/`, so a source change is
  silently reverted and the benchmark measures the old shader. Run `bin/managed/ShaderCompiler.exe "*"` (a couple of
  seconds, it skips what's up to date) after every build, and check the attribute is really in there with
  `grep -a MyAttribute core/shaders/my.shader_c`. This is why `clutter_cull_cs`'s caster planes measured as doing
  nothing at all until 2026-11-08.

Captures render the whole window with the lights frozen, then read the swap chain (or target) back before the panels
draw, so they're repeatable.

A running SceneLab locks the DLLs in `game/bin/managed`. Close it before building, or the copy fails.
