# Fonctions/stubs à implémenter (Sandbox.Engine.Emulation)

## Physics
- `src/Sandbox.Engine.Emulation/Physics/PhysicsWorld.cs` : `IPhysicsWorld_AddWeldJoint` (placeholder handle, pas de joint Bepu), `IPhysicsWorld_AddSpringJoint`, `IPhysicsWorld_AddRevoluteJoint`, `IPhysicsWorld_AddPrismaticJoint`, `IPhysicsWorld_AddSphericalJoint`, `IPhysicsWorld_AddMotorJoint`, `IPhysicsWorld_AddWheelJoint` (exception explicite, toujours non implémenté).
- `src/Sandbox.Engine.Emulation/CUtl/CUtlVectorTraceResult.cs` : `CtlVctrPhyscsTrc_Result_Element` retourne toujours `null` (pas de mapping vers `PhysicsTrace.Result`).

## Rendering
- `src/Sandbox.Engine.Emulation/Rendering/EmulatedRenderContext.cs` : `DrawInstanced`, `DrawIndexedInstanced`, `BindVertexBuffer` (2 overloads), `BindIndexBuffer`, `BindTexture`, `BindRenderTargets` (2 overloads), `RestoreRenderTargets`, `GenerateMipMaps` (tous no-op / false). Les marqueurs PIX sont aussi des no-op.
- `src/Sandbox.Engine.Emulation/Rendering/EmulatedSceneView.cs` : `AddRenderLayer`, `AddManagedProceduralLayer`, `AddWorldToRenderList`, `FindOrCreateRenderTarget`, `SetParent`, `GetParent`, `GetPriority/SetPriority`, `GetPostProcessEnabled`, `GetToolsVisMode` (tous stubs renvoyant valeurs par défaut).
- `src/Sandbox.Engine.Emulation/Rendering/RenderDevice.cs` :
  - Swap chain/présentation : `g_pRenderDevice_GetSwapChainTexture` renvoie toujours `IntPtr.Zero` (tracking manquant).
  - Textures : `g_pRenderDevice_FindOrCreateFileTexture` (NotImplemented), `g_pRenderDevice_AsyncSetTextureData2` (update synchrone only, ignore rect), `g_pRenderDevice_GetTextureDesc` / `GetOnDiskTextureDesc` (dimensions hardcodées 1x1, fuite potentielle), `g_pRenderDevice_GetTextureViewIndex` (retourne 0), `g_pRenderDevice_GetTextureResidencyInfo` (no-op).
  - Textures animées : `g_pRenderDevice_GetSheetInfo` / `GetSequenceCount` renvoient des valeurs par défaut, `g_pRenderDevice_GetSequence` lève NotImplemented.
  - Contexte rendu : `g_pRenderDevice_CreateRenderContext` stocke un simple `object` placeholder.
  - Shaders : `g_pRenderDevice_CompileAndCreateShader` lève NotImplemented (compilation OpenGL manquante).
  - Divers : TODO non traités (tracking swap chain textures, frame time GPU, etc.) dans le fichier.
- `src/Sandbox.Engine.Emulation/Camera/CameraRenderer.cs` : `CCameraRenderer_Render`, `RenderToTexture`, `RenderToCubeTexture`, `RenderToBitmap`, `RenderStereo`, `SubmitStereo`, `BlitStereo` sont des no-op/TODO. `CCameraRenderer_Create` ignore le vrai `StringToken` (nom placeholder).

## Video
- `src/Sandbox.Engine.Emulation/Video/VideoPlayer.cs` : `CVideoPlayer_Play` ne fait que setter l’état (pas de lecture réelle), `CVideoPlayer_Update` no-op (pas de décodage/avancement), `CVideoPlayer_GetSpectrum` remplit des zéros, `CVideoPlayer_GetAmplitude` repose sur données fictives, le pipeline audio/vidéo n’est pas implémenté.

## Input
- `src/Sandbox.Engine.Emulation/Input/InputSystem.cs` :
  - IME : `g_pInputSystem_SetIMEAllowed`, `SetIMETextLocation`, `DismissIME` sont des no-op (pas d’IME réel).
  - Mapping boutons/clavier : `g_pInputSystem_CodeToString`, `StringToButtonCode`, `VirtualKeyToButtonCode`, `ButtonCodeToVirtualKey` retournent 0/`IntPtr.Zero` et log only.
  - Curseurs : `g_pInputSystem_SetCursorStandard`, `SetCursorUser`, `LoadCursorFromFile` (retourne 0), `ShutdownUserCursors` (no-op) – aucune création de curseur GLFW.
- `src/Sandbox.Engine.Emulation/Input/InputService.cs` : `g_pInputService_Key_NameForBinding`, `g_pInputService_GetBinding` retournent `IntPtr.Zero` (pas de mapping binding <-> touche).

## Resources
- `src/Sandbox.Engine.Emulation/Resource/ResourceSystem.cs` : `g_pRsrcSystm_GetAllCodeManifests` ne remplit pas le `CUtlVectorString` natif (log uniquement, TODO explicite).

## Audio
- `src/Sandbox.Engine.Emulation/Audio/DspPreset.cs` : `DspPreset_Instantiate` crée un handle factice, aucun effet DSP OpenAL réel n’est construit (TODO explicite).

## Platform
- `src/Sandbox.Engine.Emulation/Platform/PlatformFunctions.cs` : le module n’initialise qu’un sous-ensemble des fonctions `Plat_*`; commentaire TODO indiquant ~27 fonctions Platform encore absentes du patch table.

## Scene
- `src/Sandbox.Engine.Emulation/Scene/SceneSystem.cs` : en-tête indique que beaucoup de fonctions restent des placeholders; plusieurs fonctionnalités (culling, lights, envmaps, etc.) sont implémentées minimalement voire no-op. Un passage complet est requis pour remplacer ces stubs.

