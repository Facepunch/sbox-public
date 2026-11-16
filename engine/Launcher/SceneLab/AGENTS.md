# SceneLab — agent guide

> Scope: `scenelab.exe`, the lab for native's scene system. Last reviewed: 2026-11-16.
> Borealis has its own lab, BorealisLab ([`docs/managed/borealis-host.md`](../../../docs/managed/borealis-host.md)). How the
> two compare is in [`docs/managed/borealis.md`](../../../docs/managed/borealis.md) (*Labs and comparisons*).

SceneLab renders GameObject scenes, maps and scripted scenarios through native's scene system, to measure and capture it. It's
a borderless `PanelWindow`: native draws the scene's camera into its swap chain, and panel UI draws over it (a menu bar and a
stats overlay). It's for changes to native, and to code both renderers share: capture native before and after, and compare.

## Files

| File | What it does |
|---|---|
| `Launcher.cs` | The entry point the shared launcher startup calls: runs `SceneLabAppSystem`. |
| `SceneLabAppSystem.cs` | Boots the panel app and opens the window. `DrawsScenes` keeps the engine's scene buffers full size and loads the native modules a game does (world renderer, physics, animation, sound), so maps load. Registers the engine's managed console variables under SceneLab's own cookies and runs `+name value` from the command line. |
| `SceneLabWindow.cs` | The window: the menus (Scene, a submenu per `Group`; View: Occlusion Culling, VSync, Reset Camera, Capture), the stats overlay, `Load`, `OnRenderBackground`, and the capture, parity and benchmark hookup. `-swapchain-msaa` sets the window's sample count. |
| `SceneLabWindow.Game.cs` | Loading a preset's scene, the orbit camera around `GameFocus`, ticking the scene, rendering it through `SceneRenderer.Native`, and its captures (`CaptureGame`, `CaptureIsolated`). `WithGameUI` swaps the window's `UISystem` in for screen UI. |
| `SceneLabScene.cs` | What a preset is (a `Func<Scene>`, its `ChangeGame`, per-frame `Frame`, `Ready`, `Isolated`, `Rewind` and the rest) and the `Presets` list, in Scene menu order. |
| `SceneViewport.cs` | The see-through panel the scene shows through: left drag orbits, the wheel zooms. |
| `GameScenes.cs` | The GameObject presets built on a shared `Stage()` (camera, sun with sky light, default sky, floor): post processing, depth of field, auto exposure, blur, highlight outlines, screen UI and a HUD, early UI, a world panel, output overlays, sprites and particles, decals, decal geometry, tools views and debug views, volumetric fog and dense smoke, contact shadows, soft particles, particle refraction, glass refraction, a viewmodel and its depth chain, bloom objects, scene attributes, scene object children, morphs, model deformers, AO and SSR, light cookies, light modulation, light switching and the debug overlay. Each with a change to capture after, where there's one to make. `LoadResource` loads game resources from their compiled files, which a panel app doesn't register. |
| `GameScenes.Fog.cs` | The Better Fog presets (Environment group): distance, height, sky, 3D skybox, blending, overlays, sprites, lines, water and text in fog. |
| `StageAttributesScene.cs` | Stage Attributes: attributes a camera command list sets stay in its stage. |
| `ExtraWorldsScene.cs` | Extra Worlds: a camera's extra world (`SceneCamera.Worlds`) and the scene's debug world, in front of and behind a wall. Its capture renders a `SceneCamera` set up from the scene's camera with the extra world added, since the camera component clears `Worlds` every frame (`SceneLabScene.ExtraWorld`). |
| `TerrainScenes.cs` | Terrain and Clutter, and Terrain Ambient Occlusion: terrain and clutter, which draw themselves as custom objects. |
| `InstancedScene.cs` | Instanced Models: a custom object drawing models with `Graphics.DrawModelInstanced`, casting shadows. |
| `ObjectCommandListScene.cs` | Object Command Lists: a renderer's own command list (`Renderer.ExecuteBefore`) painting a texture its material samples. |
| `ViewSetupScenes.cs` | View Projections, View Oblique, View Overrides: monitors rendering into textures, each with its own `ViewSetup` (`SceneLabScene.TargetSetup`). |
| `WaterScene.cs` | Water Refraction, Water Planar, Water Planar Shadows: a water body's refraction capture and planar reflections. |
| `MirrorScene.cs` | Mirror Reflection and Mirror Reflection Shadows: a camera command list's `DrawReflection` shown in the same frame. |
| `EnvmapCaptureScene.cs` | Envmap Capture: an envmap probe captured every frame (six faces and the GGX filter); the isolated capture saves the cube's faces too. |
| `DDGIBakeScene.cs` | DDGI Bake: a small indirect light volume baked in a room of coloured walls; the isolated capture saves the last probe's captures and the baked volumes too. |
| `LightModulatorScenes.cs` | Light Modulation Baked Sun, Dynamic Sun and Gallery: textured transmission through light modulators. |
| `OutfitScenes.cs` | Workshop clothing on Citizen 2.0's avatar deforms (the Avatar group): Hotdog Costume, Banana Suit, Katana, Golf Backpack, Pearl Necklace, Fried Onion Earrings and All Outfits. Workshop packages download once the backend is up; `Ready` waits for the citizens to be dressed. |
| `Outfits.cs` | Dressing citizens in the citizen addon's clothing, for the scenes that dress them. |
| `MapScenes.cs` | Maps from the cloud through a `MapInstance` (the Maps group: Flatgrass, Flatgrass Skyline, Flatgrass Interior, Flatgrass Props, Construct Skyline, Construct Overview, Construct Skyline Fog). Sets up what a game's boot does for downloads, starts once the backend is up, and says when it's loaded (`Ready`), so captures wait. `-map-ident`, `-camera-pos` and `-camera-lookat` override a map preset's map and camera. |
| `DynamicScene.cs` | Dynamic (Stress group): 400 props fading, recolouring and moving every frame, four circling shadowed point lights, sprites, particles and a second camera rendering into a texture. |
| `HeavyScene.cs` | Heavy Scene, Heavy Scene AO, Heavy Scene AO SSR (Stress group): 900 varied citizen props, 40 animating citizens, 24 circling shadowed lights, contact shadows, glass, sprites, particles, tonemapping and bloom. `-width 3840 -height 2160` makes it GPU-bound. |
| `OcclusionScene.cs` | Occlusion City (Stress group): a street-level lap of a city of hollow buildings and dense courtyard sculptures. View > Occlusion Culling (or `+r_occlusion_culling 1`) switches culling; the benchmark measures it off and then on (`CompareOcclusion`). |
| `Scenarios.cs` | What the Scenarios group shares: game-like scenes scripted by frame count from fixed seeds, each with a `Rewind` the benchmark calls as it starts measuring. Helpers: dressed animated citizens, random layered outfits, `CameraPath`, emitter-less particles (`Particles`, `Emit`), nameplates, a light's flicker, mounting a package's files without its map (`Mounted`). |
| `FirefightScene.cs` | Firefight: 28 dressed citizens in two teams on Flatgrass, running and shooting, every shot traced: muzzle flashes, dust and debris, bullet hole decals, nameplates and a HUD. |
| `FlythroughScene.cs` | Flythrough: a scripted camera through Construct, walking then driving; its whole path is the benchmark's run. |
| `CrowdScene.cs` | Crowd Near, Crowd Mid, Crowd Far: 240 dressed citizens in a plaza on their animation graphs, with face morphs, seen from three distances. |
| `ForestScene.cs` | Forest: Construct's oaks, bushes and clutter over hilly terrain under a low sun's cascades, the camera walking a loop. Mounts `facepunch.construct` without loading its map. |
| `NightInteriorScene.cs` | Night Interior: a corridor with eight rooms lit at night by 48 shadowed lights, some flickering, walked through. |
| `SmokeAndGlassScene.cs` | Smoke and Glass: a street at dusk in volumetric fog, refracting shop and bus shelter glass, three smoke grenades and two shadowed street lamps. |
| `Benchmark.cs`, `FrameTimer.cs` | `-benchmark`: each scene rendered natively with vsync off, a warm-up, then a measured run, and a table (markdown and CSV in `screenshots/scenelab`): FPS, frame time and percentiles, the main thread's render time, GPU time, allocations and GCs, and native's scenesystem counters. `-gpuprofile` adds GPU time by layer. |
| `ParityRun.cs` | `-parity`: presets captured natively one after another in this process, a table at the end (`parity.txt`) and exit code 1 on a failure. `-save-native name` keeps the frames under a name; `-compare-native name` compares each with the saved one, pixel for pixel. |
| `SceneLab.csproj` | The project. It imports `../Shared/LaunchShared.projitems` and `../LabShared/LabShared.projitems` (SceneLab uses its `Args`). |

`FrameComparison` (`engine/Sandbox.Rendering/Bridge/FrameComparison.cs`) holds the pixel comparison and PNG writing the
captures use.

## Running it

From `game/`:

```
bin/managed/scenelab.exe -scene "Heavy Scene"                         # interactive
bin/managed/scenelab.exe -scene "Decals" -capture out.png              # saves out.native.png (and out_changed.native.png)
bin/managed/scenelab.exe -benchmark "Heavy Scene;Crowd Near" -novsync  # ; separates scenes
bin/managed/scenelab.exe -parity -save-native before                  # capture every preset and keep them
bin/managed/scenelab.exe -parity -compare-native before               # after a change: compare with them
bin/managed/scenelab.exe -parity "Heavy Scene;Decals"                 # selected presets
```

Other switches:

- **Capture:** `-capture path.png` saves the camera's frame beside it as `path.native.png`, rendered natively into an 8-bit
  bitmap, effects and UI included, at the capture frame (`-capture-frame`, default 30, or the preset's own), then again ten
  frames after the preset's change (`path_changed.native.png`), then exits. A preset's `ValidateGame` failing sets exit
  code 2. View > Capture saves into `screenshots/scenelab`.
- **Isolated:** `-isolated` captures with the camera rendered once on capture frames, into a readback target instead of the
  swap chain, for effects whose history belongs to the camera (AO, SSR). Presets marked `Isolated` always capture that way,
  and `-parity` loads them fresh, after 10 frames with nothing loaded, so the render target pool (which frees targets unused
  for 8 frames) has forgotten the last preset's history.
- **Parity:** `-parity ["A;B"] [-parity-out folder] [-save-native name | -compare-native name] [-parity-timeout s]`. Frames go
  to `screenshots/scenelab/parity` by default. A preset that doesn't finish in 180 s, or throws while loading, is reported and
  skipped. Save and compare frames in the same mode: a preset's frame can differ in the last bit between `-parity` and
  one-process-per-preset runs. Native differs run to run on Auto Exposure (it adapts in real time), Sprites and Particles and
  Soft Particles (random particles), Dynamic (it animates) and the Flatgrass maps.
- **Benchmark:** `-benchmark ["A;B"]` (default Heavy Scene, Dynamic, Crowd Near, Night Interior), `-benchmark-frames N`
  (default 600, or a scripted preset's whole script), `-gpuprofile`.
- **Debug visualisation:** `-debugmode <SceneCameraDebugMode>` captures a preset's debug visualisation instead of its lit
  frame, e.g. `-debugmode AmbientOcclusion`; with `+r_ao_reference 128` for a converged AO reference.
- **Window:** `-width`, `-height`, `-fullscreen [exclusive]`, `-novsync`. Multisampled at 4x by default, as a game's is;
  `-swapchain-msaa 2|8` for another count, `-swapchain-msaa 1` for none. Captures render at that sample count.
- **Console variables:** `+name value` sets one before the first scene loads, managed ones included. Native scenesystem
  convars such as `sc_fbcopy_depthaware_mask` aren't reachable this way.

**A new preset:** a `Func<Scene>` (in `GameScenes.cs` start from `Stage()`), a `ChangeGame` that changes it through its
components so the second capture shows changes arrive, and an entry in `SceneLabScene.Presets` with its `Group`.

A panel app doesn't register game resources (`Clothing`, `DecalDefinition` and the like) the way a game does, so
`GameScenes.LoadResource` loads them from their compiled files. It has no game `UISystem` either, so screen UI renders with
the window's (`WithGameUI`).

A running SceneLab locks the DLLs in `game/bin/managed`. Close it before building, or the copy fails.
