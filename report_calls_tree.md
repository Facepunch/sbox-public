# Flow d’appel (launcher → moteur)

## Entrée du launcher (desktop)
```
engine/Launcher/Shared/Startup.cs
  Program.Main() [STAThread]
    -> LauncherEnvironment.Init()
       - Définit GamePath / ManagedDllPath, PATH natif (bin/<platform>)
       - AssemblyResolve pour charger les DLL managées depuis bin/managed
    -> Launch() => Launcher.Main() (variant selon binaire : Sbox, SboxServer, SboxDev, etc.)
```

## Variants Launcher.Main
- `engine/Launcher/Sbox/Launcher.cs`
  ```
  var appSystem = new GameAppSystem();
  appSystem.Run();
  ```
- `SboxServer` (DedicatedServerAppSystem), `SboxBench` (GameAppSystem + bench mode),
  `SboxStandalone` (StandaloneAppSystem), `SboxDev` (redirige vers sbox-launcher sauf -project/-test),
  `SboxAndroid` (MainActivity), etc. Toutes héritent de `AppSystem`.

## AppSystem.Run (engine/Sandbox.AppSystem/AppSystem.cs)
```
Run():
  SetupEnvironment()              // culture, calendar
  Application.TryLoadVersionInfo()
  Init()                          // overridable; AppSystem.Init sets GC, NetCore.InitializeInterop
  EngineGlobal.Plat_SetCurrentFrame(0)
  while (RunFrame()) { BlockingLoopPumper.Run(() => RunFrame()); }
  Shutdown()
```

## AppSystem.InitGame (appelé depuis GameAppSystem.Init, etc.)
```
CreateGame()/CreateMenu()/CreateEditor() selon l’app
_appSystem = CMaterialSystem2AppSystemDict.Create(createInfo...)
  Flags: IsGameApp / IsDedicatedServer / IsStandaloneGame / IsEditor / IsUnitTest...
_appSystem.SetInToolsMode / SetInTestMode / SetInStandaloneApp / SetDedicatedServer
_appSystem.SetSteamAppId(Application.AppId)
SourceEnginePreInit(commandLine, _appSystem)
Bootstrap.PreInit(_appSystem)
Standalone.Init() si Standalone
SourceEngineInit(_appSystem)
Bootstrap.Init()
```

## Boucle RunFrame (AppSystem)
```
RunFrame():
  EngineLoop.RunFrame(_appSystem, out wantsToQuit)
  return !wantsToQuit
```
- `EngineLoop.RunFrame` (Sandbox.Engine) est le coeur : appelle le moteur natif via Interop.

## SourceEngine* (interop natif)
- `SourceEnginePreInit` et `SourceEngineInit` sont appelés depuis AppSystem.InitGame (ci-dessus).
- `SourceEngineFrame` est appelé depuis le moteur natif au fil des frames (via EngineLoop). Dans l’émulation, l’export index 1594 est mappé (dans `PlatformFunctions.Init`) vers `PlatformFunctions.SourceEngineFrame` (boucle GL/GLFW côté NativeAOT).
- `SourceEngineShutdown` est appelé dans `AppSystem.Shutdown`.

## Vue d’ensemble de la séquence (cas GameAppSystem)
```
Program.Main (Startup)
  -> LauncherEnvironment.Init
  -> Launcher.Main (Sbox) -> new GameAppSystem().Run()
      Run()
        Init() (LoadSteamDll/TestSystemRequirements via GameAppSystem; AppSystem.Init sets GC/interop)
        InitGame()
          CMaterialSystem2AppSystemDict.Create(...)
          SourceEnginePreInit(...)
          Bootstrap.PreInit(...)
          SourceEngineInit(...)
          Bootstrap.Init()
        loop RunFrame -> EngineLoop.RunFrame (app system)
        Shutdown -> EngineGlobal.SourceEngineShutdown(...)
```

## Comment Sandbox.Engine pilote Sandbox.Engine.Emulation (rendu & natives)
- Les appels natifs sont résolus via Interop (`engine/Sandbox.Engine/Interop.Engine.cs`, généré). Au chargement, `FillNativeFunctionsEngine` dans la lib native (émulation) remplit les pointeurs.
- Côté émulation, `EngineExports`/`PlatformFunctions` patchent le tableau `native[]` aux bons indices (SourceEngine*, RenderDevice, RenderAttributes, RenderTools, etc.). `PlatformFunctions.Init` accroche notamment SourceEngineFrame à l’indice 1594.
- La boucle `EngineLoop.RunFrame` (Sandbox.Engine) appelle `EngineGlobal.SourceEngineFrame` (interop), qui exécute la boucle native/émulée. Côté moteur managé, le rendu est orchestré par le RenderPipeline : `RenderLayer.AddToView` crée des `ISceneLayer` natifs, et les layers procéduraux enregistrent des callbacks managés (`ProceduralRenderLayer.Internal_OnRender`).
- `Graphics.OnLayer` est invoqué côté managé quand la pipeline de couches (RenderPipeline) déclenche ces callbacks (procédural) ou via les hooks internes du moteur. Le rendu UI (stage -1) est géré dans `Graphics.OnLayer` (RenderUiOverlay).
- Dans l’émulation actuelle, `PlatformFunctions.SourceEngineFrame` ne reconstruit pas de `SceneView/SceneLayer` ni n’invoque les callbacks de layers procéduraux : la boucle se limite à clear+swap. Tant que l’émulation n’instancie pas ces views/layers ou n’appelle pas explicitement `Graphics.OnLayer`, aucun draw n’est émis même si le code managé est prêt.
- Le swapchain/backbuffer est géré côté émulation (RenderDevice + PlatformFunctions/GLFW). Les appels managés obtiennent des handles/structs via `NativeEngine` mappés sur les handles émulés (HandleManager) et les objets GL (textures, FBO, buffers).
- Le chargement/assemblage des DLL et natives est fait avant tout (LauncherEnvironment.Init + NetCore.NativeDllPath). La lib native (libengine2.so) fournit tous les exports utilisés par `NativeEngine` dans Sandbox.Engine.

### Détail du chemin rendu (managé → émulé)
- Dans Sandbox.Engine (managé) :
  - `Graphics.OnLayer` est appelé depuis le RenderPipeline (RenderLayer/ProceduralRenderLayer via callbacks natifs) et exécute le rendu UI (stage -1) ou de caméra (OnRenderStage). Les appels réels passent par les delegates interop `NativeEngine.RenderTools_*`, `RenderDevice_*`, `RenderAttributes_*`.
- Dans l’émulation :
  - `FillNativeFunctionsEngine` mappe les indices RenderDevice/RenderAttributes/RenderTools.
  - `RenderDevice` implémente textures, buffers, shaders, swapchain/backbuffer, présent, en OpenGL.
  - `RenderAttributes` gère paramètres matériaux/constantes via HandleManager.
  - Les handles managés `NativeEngine.*` correspondent aux handles émulés (HandleManager) qui enveloppent les ressources GL.
- Swap/present :
  - `PlatformFunctions.SourceEngineFrame` pilote GLFW et le swap. Faute de création de `SceneView/SceneLayer` et de déclenchement des callbacks de layers, aucun draw n’est émis actuellement (clear+swap).

### Zoom complet sur RenderTools (interop 2394-2409)
- Patchage : `RenderTools.Init` place les pointeurs natifs aux indices 2394-2409 (Interop.Engine.cs / engine.Generated.cs). Côté managé, ces indices sont consommés via `NativeEngine.RenderTools_*`.
- SetRenderState (2394) : récupère l’`EmulatedRenderContext`, prépare la vertex layout (attributs GL). Matériaux/attributes seront branchés ensuite.
- Draw (2395) : upload des vertex data (stride fallback 80 aligné EmulatedRenderContext), upload éventuel des indices (UInt16), setup layout, draw indexed ou non. Type de primitive dérivé de `RenderPrimitiveType`. Stride réel inconnu faute d’accès à `VertexLayout`.
- ResolveFrameBuffer / ResolveDepthBuffer (2396/2397) : blit color/depth du framebuffer par défaut vers la texture ciblée via FBO temporaire.
- DrawSceneObject (2398) : log-only (non câblé aux meshes/shaders).
- DrawModel / DrawModel_1 (2399/2400) : log-only (pas d’instancing/indirect câblé).
- Compute / ComputeIndirect (2401/2402) : log-only (compute non supporté GL).
- TraceRays / TraceRaysIndirect (2403/2404) : log-only (RT non supporté).
- SetDynamicConstantBufferData (2405) : stocke un blob via RenderAttributes PtrValues (GCHandle pinné).
- CopyTexture (2406) : blit texture→texture via FBO (mip slice pris en compte).
- SetGPUBufferData (2407) : BufferSubData sur le buffer GL enregistré dans RenderDevice.
- CopyGPUBufferHiddenStructureCount / SetGPUBufferHiddenStructureCount (2408/2409) : log-only (counter non supporté).
- Interaction RenderDevice/HandleManager : RenderTools consomme les handles buffers/textures/layouts créés par RenderDevice ; EmulatedRenderContext gère VBO/IBO/VAO/shader basique. HandleManager assure la correspondance managé ↔ natif.
- Pour atteindre “0 stub” : exposer le `VertexLayout` managé (stride/attributs) côté interop, câbler DrawSceneObject/DrawModel aux meshes/shaders, supporter compute/ray/copy buffer counter, et brancher le pipeline RenderPipeline (création SceneView/Layer) pour déclencher réellement `Graphics.OnLayer`.

### Sélection des exports et indices (interop)
- Indices SourceEngine* : 1592 (PreInit), 1593 (Init), 1594 (Frame), 1595 (Shutdown), 1596 (UpdateWindowSize).
- RenderAttributes : 689-727 (Create/Delete/Set/Get de float, vectors, matrices, textures, samplers, buffers, ptr…).
- RenderDevice : 1470-1510+ (sampler, swapchain info/texture, textures, shaders, GPU buffers, présent, etc.).
- Ces indices proviennent de `engine/Sandbox.Engine/Interop.Engine.cs` et sont patchés par l’émulation au chargement.

## Gaps actuels côté émulation (rendu)
- `RenderTools` : DrawSceneObject/DrawModel/Compute/TraceRays/HiddenCounter restent log-only ; CopyTexture/Resolve color/depth sont blit FBO ; SetGPUBufferData fait BufferSubData ; SetDynamicConstantBufferData stocke un blob via PtrValues.
- `VertexLayout` : handle opaque, stride/attributs non exposés ; fallback 80 octets en attendant un getter managé.
- `SceneView/SceneLayer/SceneWorld` : exports 2138-2199 sont désormais implémentés (handles, attrs, flags, PVS, skybox) mais aucune création n’est déclenchée par la boucle actuelle.
- Pipeline managé : `PlatformFunctions.SourceEngineFrame` ne construit toujours pas les `SceneView/SceneLayer` ni n’appelle `Graphics.OnLayer`; `g_pSceneSystem_BeginRenderingDynamicView` est encore un stub. Sans pont RenderPipeline → emulation (CameraRenderer/SceneSystem → SceneView/Layer → RenderTools), on reste sur clear+swap (écran gris).

## Android / autres
- Android : `engine/Launcher/SboxAndroid/MainActivity.cs` lance via `Launcher.Main()` analogue mais dans Activity.
- StandaloneTest/Dev/Server/Bench adaptent juste les flags et initialisations pré-InitGame.

## Remarques
- Toute la résolution de DLL et des natives est configurée avant les premiers appels moteur via `LauncherEnvironment.Init`.
- Les exports natifs (SourceEngine*, RenderDevice, RenderAttributes, etc.) sont fournis par la couche natif/interop (libengine2.so en émulation) et branchés via `FillNativeFunctionsEngine`.

