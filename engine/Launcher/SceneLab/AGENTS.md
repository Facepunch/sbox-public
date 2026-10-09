# SceneLab — agent guide

> Scope: `scenelab.exe`, the proving ground for the managed scene renderer. Last reviewed: 2026-09-29.
> The renderer's own guide is [`engine/Sandbox.SceneRenderer/README.md`](../../Sandbox.SceneRenderer/README.md);
> read it first. The details are in [`docs/managed/scene-renderer.md`](../../../docs/managed/scene-renderer.md)
> (*SceneLab*, *Parity with native*, *Benchmark*).

SceneLab is to the managed renderer what PanelGallery is to panels. It's a borderless `PanelWindow` that draws
a `RenderWorld` into its swap chain, with panel UI over the top: a menu bar and a stats overlay. It can draw
the same world through native too, to compare the two.

## Files

| File | What it does |
|---|---|
| `SceneLabAppSystem.cs` | Boots the panel app and opens the window. `DrawsScenes` keeps the engine's scene buffers full size (no `-panelapp`) and loads the native modules a game does (world renderer, physics, animation, sound: `SourceEnginePanelAppInit`), so maps load. The owner's call (2026-09-24): SceneLab doesn't need the launcher's slim startup. |
| `SceneLabWindow.cs` | The window. It holds the menus and the stats overlay. `Load` builds a preset's world, `OnRenderBackground` renders each frame, and it handles capture, parity and the benchmark hookup. |
| `SceneLabScene.cs` | What a scene is: models on a grid, lights, floor, probe, fog, shadow settings, or a GameObject scene (`Game`, with `ChangeGame` and a per-frame `Frame` hook). It holds the `Presets` (the Scene menu, a submenu per `Group`) and the command line parsing. `Checker` gives every other object another material or tint alpha. |
| `SceneLabWindow.Game.cs` | GameObject presets: loading, the orbit camera around `GameFocus`, ticking the scene, rendering through the bridge or natively, and their captures, compared through the bridge's compare before and after `ChangeGame`. `WithGameUI` swaps the window's `UISystem` in for screen UI. |
| `MapScenes.cs` | Maps from the cloud through a `MapInstance` (the Maps group: Flatgrass, Flatgrass Skyline, out on the grass looking at the 3D skybox, Flatgrass Interior, inside the building under its skylights, and Flatgrass Props, low over grass clumps lit from light probe volumes). It sets up what a game's boot does for downloads and a panel app's doesn't (downloads folder, asset cache, `ServerPackages`), names the map once the backend is up (`PanelAppSystem.ApiReady`), and says when it's loaded (`SceneLabScene.Ready`), so captures wait for it. |
| `GameScenes.cs` | The GameObject presets past the bridge scene: post processing, depth of field, auto exposure, screen UI and a HUD, early UI, a world panel, the output layers (overlays after post processing, after-UI objects), sprites and particles, decals, decal geometry (the decal layer), tools views and debug visualisations (the Tools group), contact shadows, volumetric fog (its camera rendered natively every frame too, `NativeAlongside`), refraction (glass: the frame buffer copy and the refraction stencil), soft particles (the depth chain), a viewmodel (the game overlay layers), the viewmodel's depth chain (the overlay prepass stencil) and bloom objects (the bloom layer). Each with a change to compare after, where there's one to make. |
| `OutfitScenes.cs` | Workshop clothing on Citizen 2.0's avatar deforms (the Avatar group). Hotdog Costume (`glitchworkshop.thehotdogcostume`), Banana Suit (`falkoworkshop.mrbananasuit`) Katana (`glitchworkshop.katana`) and Golf Backpack (`tarbaganchik.golfbackpack`) each download their package once the backend is up, and Pearl Necklace loads the citizen addon's own, and each dresses three citizens in it; Fried Onion Earrings (`pkxuni.earring_onio`) frames four faces close: the deforms reaching the earrings, opted out, opted out under a big head of its own (an inflate deformer), and rigid under it (`SkinnedModelRenderer.DeformationMode`); All Outfits puts every one in one scene, three groups across the front and two behind, each named and its citizens tagged (`Ready` waits for that), each labelled by a world panel at its feet: a reference without `citizen_deforms.prefab`, one whose deforms (a thin neck and big nose, past the sliders' range) reach the costume, and one whose costume has `DeformationMode` set to `None`, so its body is deformed and the costume isn't. |
| `DynamicScene.cs` | The Dynamic preset (Stress group): 400 props fading, recolouring and moving every frame, four circling shadowed point lights, sprites, particles and a second camera rendering into a texture - the renderer and bridge measured under change and with two cameras. |
| `NativeReference.cs` | `NativeScene` mirrors the world as native `SceneModel`s, lights, cubemaps and fog. `NativeReference.Compare` is the per-pixel comparison. |
| `HeavyScene.cs` | The Heavy Scene preset (Stress group): 900 varied citizen props (overrides, attributes of their own, a tenth moving), 40 animating citizens, 24 circling shadowed point and spot lights, the sun's contact shadows, glass, sprites, particles, tonemapping and bloom - a busy game's frame, GPU included. `-width 3840 -height 2160` makes it GPU-bound. Heavy Scene AO adds ambient occlusion, without the particles, since it compares isolated. |
| `Benchmark.cs`, `FrameTimer.cs` | `-benchmark`: both renderers, warm-up, measured frames, results table, and the managed main thread by section (collect, prepare, setup, recording wall time and wait, the workers' total, stages, submit) from `RenderStats`. `-gpuprofile` adds GPU time by layer, from the engine's GPU profiler. |
| `SceneViewport.cs` | The see-through panel the world renders into: orbiting and zooming. |
| `LodTestModel.cs`, `SkyCube.cs` | Test assets built at runtime: a model whose LODs are different shapes, and a real cubemap for fog. |
| `BridgeScene.cs` | The GameObject Bridge preset (and `-gamescene builtin`): a GameObject scene built from components, for the GameObject bridge. |
| `PlanOverlay.cs` | The frame plan over the viewport (View > Frame Plan, `-plan [level]`): the engine's `overlay_scene_plan`, drawn by a panel, and saved beside a capture as `<name>.plan.png`. |
| `ParityRun.cs` | `-parity`: every preset or some compared with native one after another in this process (about a minute for all of them), isolated presets in two fresh passes, native frames saved or compared across a change (`-save-native`/`-compare-native`), a table at the end (`parity.txt`) and exit code 1 on a failure. |

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
  it through its components so the second compare proves changes arrive, and an entry in `Presets` with its
  `Group`. Switch the feature off once and see the compare fail, or the pass means nothing.
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
- **Animation:** `-sequence-time f` (0 to 1) poses skinned presets at another frame. Skinned objects are posed by a
  `SceneModel` in a world that's never rendered, and the native reference sets the same bones on its own.
- **Camera and window:** `-zoom`, `-width`, `-height`.
- **Renderer:** `-noprepass`, `-nosizecull`, `-noshadows`, `-debugshader`, `-native-renderer`, `-novsync`,
  `-serialrecording` (record every segment on the main thread, for the lab's renderer and the bridge's alike).
- **Target:** `-target` renders into a `ViewTarget` instead of the swap chain; `-msaa 2|4|8` multisamples it and
  `-targetformat` picks its colour format (RGBA16F by default). With `-native`, a multisampled capture's
  reference comes from native `RenderToTexture` at the same count, three frames later.
- **Window:** multisampled at 4x by default, as a game's is; `-swapchain-msaa 2|8` for another count, `-swapchain-msaa 1`
  for none (the native frames saved before 2026-09-29 were captured without).
- **Capture:** `-capture-frame` (default 30).
- **Console variables:** `+name value` sets one before the first scene loads (`ConVarSystem.Run`), managed ones included
  (SceneLab registers the engine's, under its own cookies; before 2026-09-27 they were unknown commands). Native scenesystem
  convars such as `sc_fbcopy_depthaware_mask` aren't reachable this way.

Captures render the whole window with the lights frozen, then read the swap chain (or target) back before the panels
draw, so they're repeatable.

A running SceneLab locks the DLLs in `game/bin/managed`. Close it before building, or the copy fails.
