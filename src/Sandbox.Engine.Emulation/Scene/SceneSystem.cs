// TODO: Many function implemented but not really because its still place holder for many
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Sandbox.Engine.Emulation.Common;
using Sandbox;

namespace Sandbox.Engine.Emulation.Scene;

/// <summary>
/// Module d'émulation pour SceneSystem (g_pSceneSystem_*).
/// Gère la création et la gestion des mondes de scène.
/// </summary>
public static unsafe class SceneSystem
{
    /// <summary>
    /// Données internes pour un monde de scène émulé.
    /// </summary>
    private class SceneWorldData
    {
        public string DebugName { get; set; } = "";
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
    }
    
    /// <summary>
    /// Structure correspondant à SceneSystemPerFrameStats_t dans Source 2.
    /// Cette structure est allouée en mémoire non managée pour être accessible depuis le code natif.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SceneSystemPerFrameStats_t
    {
        // Rendering stats
        public uint m_nTrianglesRendered;
        public uint m_nArtistTrianglesRendered;
        public uint m_nRenderBatchDraws;
        public uint m_nDrawCalls;
        public uint m_nDrawPrimitives;
        public uint m_nBaseSceneObjectPrimDraws;
        public uint m_nAnimatableObjectPrimDraws;
        public uint m_nAggregateSceneObjectPrimDraws;
        public uint m_nAggregateSceneObjectsFullyCulled;
        public uint m_nAggregateSceneObjectDrawCalls;
        
        // Material stats
        public uint m_nNumMaterialCompute;
        public uint m_nNumMaterialSet;
        public uint m_nNumSimilarMaterialSet;
        public uint m_nNumTextureOnlyMaterialSet;
        public uint m_nNumVfxEval;
        public uint m_nNumVfxRule;
        public uint m_nNumConstantBufferUpdates;
        public uint m_nNumConstantBufferBytes;
        public uint m_nMaterialChangesNonShadow;
        public uint m_nMaterialChangesNonShadowInitial;
        public uint m_nMaterialChangesShadow;
        public uint m_nMaterialChangesShadowInitial;
        public uint m_nMaterialChangesShadowAlphaTested;
        public uint m_nCopyMaterialChangesNonShadow;
        
        // Transform stats
        public uint m_nMaxTransformRow;
        public uint m_nNumRowsUsed;
        
        // Object culling stats
        public uint m_nNumObjectsTested;
        public uint m_nNumObjectsPreCullCheck;
        public uint m_nNumObjectsPassingCullCheck;
        public uint m_nNumVerticesReferenced;
        
        // Context stats
        public uint m_nNumPrimaryContexts;
        public uint m_nNumSecondaryContexts;
        public uint m_nNumDisplayListsSubmitted;
        public int m_nNumViewsRendered;
        public uint m_nNumResolves;
        
        // Culling box stats
        public uint m_nNumCullBoxes;
        public ulong m_nCullingBoxCycleCount;
        public uint m_nNumObjectsTestedAgainstCullingBoxes;
        public uint m_nNumObjectsRejectedByBoundsIndex;
        public uint m_nNumObjectsRejectedByCullBoxes;
        
        // Rejection stats
        public uint m_nNumObjectsRejectedByVis;
        public uint m_nNumObjectsRejectedByBackfaceCulling;
        public uint m_nNumObjectsRejectedByScreenSizeCulling;
        public uint m_nNumObjectsRejectedByFading;
        public uint m_nNumFadingObjects;
        
        // Material and light stats
        public uint m_nNumUniqueMaterialsSeen;
        public uint m_nNumUnshadowedLightsInView;
        public uint m_nNumShadowedLightsInView;
        public uint m_nNumShadowMaps;
        
        // Render target stats
        public uint m_nNumRenderTargetBinds;
        public uint m_nPushConstantSets;
    }
    
    /// <summary>
    /// Données internes pour les stats par frame du SceneSystem.
    /// Les stats sont un singleton réutilisé chaque frame.
    /// Contient un pointeur vers la structure SceneSystemPerFrameStats_t en mémoire non managée.
    /// </summary>
    private class ScenePerFrameStatsData
    {
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
        public IntPtr StatsPtr { get; set; } = IntPtr.Zero; // Pointeur vers SceneSystemPerFrameStats_t en mémoire non managée
    }
    
    /// <summary>
    /// Données internes pour un SkyBox.
    /// </summary>
    private class SkyBoxData
    {
        public IntPtr SkyMaterial { get; set; } = IntPtr.Zero;
        public IntPtr World { get; set; } = IntPtr.Zero;
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
    }
    
    /// <summary>
    /// Données internes pour un Decal.
    /// </summary>
    private class DecalData
    {
        public IntPtr World { get; set; } = IntPtr.Zero;
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
    }
    
    /// <summary>
    /// Données internes pour une Light.
    /// </summary>
    private class LightData
    {
        public string LightType { get; set; } = "";
        public IntPtr World { get; set; } = IntPtr.Zero;
        public Vector3 Direction { get; set; } = Vector3.Zero;
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
    }
    
    /// <summary>
    /// Données internes pour un EnvMap.
    /// </summary>
    private class EnvMapData
    {
        public IntPtr World { get; set; } = IntPtr.Zero;
        public int ProjectionMode { get; set; } = 0;
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
    }
    
    /// <summary>
    /// Données internes pour un LightProbeVolume.
    /// </summary>
    private class LightProbeVolumeData
    {
        public IntPtr World { get; set; } = IntPtr.Zero;
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
    }
    
    /// <summary>
    /// Données internes pour un RayTraceWorld.
    /// </summary>
    private class RayTraceWorldData
    {
        public string DebugName { get; set; } = "";
        public int MaxRayTypes { get; set; } = 0;
        public IntPtr BindingPtr { get; set; } = IntPtr.Zero;
    }
    
    /// <summary>
    /// Données internes pour une CullingBox.
    /// </summary>
    private class CullingBoxData
    {
        public IntPtr World { get; set; } = IntPtr.Zero;
        public int CullMode { get; set; } = 0;
        public Vector3 Origin { get; set; } = Vector3.Zero;
        public Angles Angles { get; set; } = Angles.Zero;
        public Vector3 Extents { get; set; } = Vector3.Zero;
    }
    
    /// <summary>
    /// Données internes pour une VolumetricFogVolume.
    /// </summary>
    private class VolumetricFogVolumeData
    {
        public IntPtr World { get; set; } = IntPtr.Zero;
    }
    
    // Singleton pour les stats par frame (réutilisé chaque frame)
    private static ScenePerFrameStatsData? _perFrameStats = null;
    private static int _perFrameStatsHandle = 0;
    
    // Compteurs pour les IDs uniques
    private static uint _nextCullingBoxId = 1;
    private static uint _nextVolumetricFogVolumeId = 1;
    
    // Dictionnaires pour stocker les objets
    private static Dictionary<uint, CullingBoxData> _cullingBoxes = new();
    private static Dictionary<uint, VolumetricFogVolumeData> _volumetricFogVolumes = new();
    
    // Queue pour les suppressions différées (DeleteSceneObjectAtFrameEnd)
    private static readonly Queue<IntPtr> _pendingDeletes = new();
    
    // Cache pour les textures et matériaux bien connus
    private static readonly Dictionary<long, IntPtr> _wellKnownTextures = new();
    private static readonly Dictionary<long, IntPtr> _wellKnownMaterials = new();
    
    /// <summary>
    /// Initialise le module SceneSystem en patchant les fonctions natives.
    /// Indices depuis Interop.Engine.cs lignes 16387-16414 :
    /// - g_pSceneSystem_DeleteSceneObject: 1522
    /// - g_pSceneSystem_DeleteSceneObjectAtFrameEnd: 1523
    /// - g_pSceneSystem_CreateSkyBox: 1524
    /// - g_pSceneSystem_CreateDecal: 1525
    /// - g_pSceneSystem_BeginRenderingDynamicView: 1526
    /// - g_pSceneSystem_GetWellKnownTexture: 1527
    /// - g_pSceneSystem_GetWellKnownMaterialHandle: 1528
    /// - g_pSceneSystem_GetPerFrameStats: 1529
    /// - g_pSceneSystem_CreateWorld: 1530
    /// - g_pSceneSystem_DestroyWorld: 1531
    /// - g_pSceneSystem_SetupPerObjectLighting: 1532
    /// - g_pSceneSystem_CreatePointLight: 1533
    /// - g_pSceneSystem_CreateSpotLight: 1534
    /// - g_pSceneSystem_CreateOrthoLight: 1535
    /// - g_pSceneSystem_CreateDirectionalLight: 1536
    /// - g_pSceneSystem_CreateEnvMap: 1537
    /// - g_pSceneSystem_CreateLightProbeVolume: 1538
    /// - g_pSceneSystem_MarkEnvironmentMapObjectUpdated: 1539
    /// - g_pSceneSystem_MarkLightProbeVolumeObjectUpdated: 1540
    /// - g_pSceneSystem_AddCullingBox: 1541
    /// - g_pSceneSystem_RemoveCullingBox: 1542
    /// - g_pSceneSystem_AddVolumetricFogVolume: 1543
    /// - g_pSceneSystem_RemoveVolumetricFogVolume: 1544
    /// - g_pSceneSystem_DownsampleTexture: 1545
    /// - g_pSceneSystem_RenderTiledLightCulling: 1546
    /// - g_pSceneSystem_BindTransformSlot: 1547
    /// - g_pSceneSystem_CreateRayTraceWorld: 1548
    /// - g_pSceneSystem_DestroyRayTraceWorld: 1549
    /// </summary>
    public static void Init(void** native)
    {
        // Indices 1522-1549
        native[1522] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pSceneSystem_DeleteSceneObject;
        native[1523] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pSceneSystem_DeleteSceneObjectAtFrameEnd;
        native[1524] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int>)&g_pSceneSystem_CreateSkyBox;
        native[1525] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pSceneSystem_CreateDecal;
        native[1526] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pSceneSystem_BeginRenderingDynamicView;
        native[1527] = (void*)(delegate* unmanaged<long, IntPtr>)&g_pSceneSystem_GetWellKnownTexture;
        native[1528] = (void*)(delegate* unmanaged<long, IntPtr>)&g_pSceneSystem_GetWellKnownMaterialHandle;
        native[1529] = (void*)(delegate* unmanaged<void*>)&g_pSceneSystem_GetPerFrameStats;
        native[1530] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pSceneSystem_CreateWorld;
        native[1531] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pSceneSystem_DestroyWorld;
        native[1532] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&g_pSceneSystem_SetupPerObjectLighting;
        native[1533] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pSceneSystem_CreatePointLight;
        native[1534] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pSceneSystem_CreateSpotLight;
        native[1535] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pSceneSystem_CreateOrthoLight;
        native[1536] = (void*)(delegate* unmanaged<IntPtr, Vector3*, int>)&g_pSceneSystem_CreateDirectionalLight;
        native[1537] = (void*)(delegate* unmanaged<IntPtr, int, int>)&g_pSceneSystem_CreateEnvMap;
        native[1538] = (void*)(delegate* unmanaged<IntPtr, int>)&g_pSceneSystem_CreateLightProbeVolume;
        native[1539] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pSceneSystem_MarkEnvironmentMapObjectUpdated;
        native[1540] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pSceneSystem_MarkLightProbeVolumeObjectUpdated;
        native[1541] = (void*)(delegate* unmanaged<IntPtr, int, Vector3*, Angles*, Vector3*, uint>)&g_pSceneSystem_AddCullingBox;
        native[1542] = (void*)(delegate* unmanaged<IntPtr, uint, void>)&g_pSceneSystem_RemoveCullingBox;
        native[1543] = (void*)(delegate* unmanaged<IntPtr, IntPtr, uint>)&g_pSceneSystem_AddVolumetricFogVolume;
        native[1544] = (void*)(delegate* unmanaged<IntPtr, uint, void>)&g_pSceneSystem_RemoveVolumetricFogVolume;
        native[1545] = (void*)(delegate* unmanaged<IntPtr, IntPtr, byte, void>)&g_pSceneSystem_DownsampleTexture;
        native[1546] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&g_pSceneSystem_RenderTiledLightCulling;
        native[1547] = (void*)(delegate* unmanaged<IntPtr, int, int, void>)&g_pSceneSystem_BindTransformSlot;
        native[1548] = (void*)(delegate* unmanaged<IntPtr, int, IntPtr>)&g_pSceneSystem_CreateRayTraceWorld;
        native[1549] = (void*)(delegate* unmanaged<IntPtr, void>)&g_pSceneSystem_DestroyRayTraceWorld;
        
        // Initialiser le singleton des stats par frame
        _perFrameStats = new ScenePerFrameStatsData();
        _perFrameStatsHandle = HandleManager.Register(_perFrameStats);
        _perFrameStats.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(_perFrameStatsHandle);
        
        // Allouer la structure SceneSystemPerFrameStats_t en mémoire non managée
        int statsSize = Marshal.SizeOf<SceneSystemPerFrameStats_t>();
        _perFrameStats.StatsPtr = Marshal.AllocHGlobal(statsSize);
        
        // Initialiser la structure à zéro
        unsafe
        {
            var stats = (SceneSystemPerFrameStats_t*)_perFrameStats.StatsPtr;
            *stats = new SceneSystemPerFrameStats_t();
        }
        
        // Patcher toutes les fonctions getter/setter pour SceneSystemPerFrameStats_t
        // Indices depuis Interop.Engine.cs lignes 17313-17414 (2448-2549)
        PatchSceneSystemPerFrameStatsFunctions(native);
        
        Console.WriteLine("[NativeAOT] SceneSystem module initialized");
    }
    
    /// <summary>
    /// Patche toutes les fonctions getter/setter pour SceneSystemPerFrameStats_t.
    /// Indices depuis Interop.Engine.cs lignes 17313-17414.
    /// </summary>
    private static void PatchSceneSystemPerFrameStatsFunctions(void** native)
    {
        // Rendering stats (2448-2467)
        native[2448] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nTrianglesRendered;
        native[2449] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nTrianglesRendered;
        native[2450] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nArtistTrianglesRendered;
        native[2451] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nArtistTrianglesRendered;
        native[2452] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nRenderBatchDraws;
        native[2453] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nRenderBatchDraws;
        native[2454] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nDrawCalls;
        native[2455] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nDrawCalls;
        native[2456] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nDrawPrimitives;
        native[2457] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nDrawPrimitives;
        native[2458] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nBaseSceneObjectPrimDraws;
        native[2459] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nBaseSceneObjectPrimDraws;
        native[2460] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nAnimatableObjectPrimDraws;
        native[2461] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nAnimatableObjectPrimDraws;
        native[2462] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nAggregateSceneObjectPrimDraws;
        native[2463] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nAggregateSceneObjectPrimDraws;
        native[2464] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nAggregateSceneObjectsFullyCulled;
        native[2465] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nAggregateSceneObjectsFullyCulled;
        native[2466] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nAggregateSceneObjectDrawCalls;
        native[2467] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nAggregateSceneObjectDrawCalls;
        
        // Material stats (2468-2495)
        native[2468] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumMaterialCompute;
        native[2469] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumMaterialCompute;
        native[2470] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumMaterialSet;
        native[2471] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumMaterialSet;
        native[2472] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumSimilarMaterialSet;
        native[2473] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumSimilarMaterialSet;
        native[2474] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumTextureOnlyMaterialSet;
        native[2475] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumTextureOnlyMaterialSet;
        native[2476] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumVfxEval;
        native[2477] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumVfxEval;
        native[2478] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumVfxRule;
        native[2479] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumVfxRule;
        native[2480] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumConstantBufferUpdates;
        native[2481] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumConstantBufferUpdates;
        native[2482] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumConstantBufferBytes;
        native[2483] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumConstantBufferBytes;
        native[2484] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nMaterialChangesNonShadow;
        native[2485] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nMaterialChangesNonShadow;
        native[2486] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nMaterialChangesNonShadowInitial;
        native[2487] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nMaterialChangesNonShadowInitial;
        native[2488] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nMaterialChangesShadow;
        native[2489] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nMaterialChangesShadow;
        native[2490] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nMaterialChangesShadowInitial;
        native[2491] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nMaterialChangesShadowInitial;
        native[2492] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nMaterialChangesShadowAlphaTested;
        native[2493] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nMaterialChangesShadowAlphaTested;
        native[2494] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nCopyMaterialChangesNonShadow;
        native[2495] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nCopyMaterialChangesNonShadow;
        
        // Transform stats (2496-2499)
        native[2496] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nMaxTransformRow;
        native[2497] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nMaxTransformRow;
        native[2498] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumRowsUsed;
        native[2499] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumRowsUsed;
        
        // Object culling stats (2500-2507)
        native[2500] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsTested;
        native[2501] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsTested;
        native[2502] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsPreCullCheck;
        native[2503] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsPreCullCheck;
        native[2504] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsPassingCullCheck;
        native[2505] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsPassingCullCheck;
        native[2506] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumVerticesReferenced;
        native[2507] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumVerticesReferenced;
        
        // Context stats (2508-2517)
        native[2508] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumPrimaryContexts;
        native[2509] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumPrimaryContexts;
        native[2510] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumSecondaryContexts;
        native[2511] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumSecondaryContexts;
        native[2512] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumDisplayListsSubmitted;
        native[2513] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumDisplayListsSubmitted;
        native[2514] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get_ScnSystmPrFrmStt_m_nNumViewsRendered;
        native[2515] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set_ScnSystmPrFrmStt_m_nNumViewsRendered;
        native[2516] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumResolves;
        native[2517] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumResolves;
        
        // Culling box stats (2518-2527)
        native[2518] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumCullBoxes;
        native[2519] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumCullBoxes;
        native[2520] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, ulong>)&Get_ScnSystmPrFrmStt_m_nCullingBoxCycleCount;
        native[2521] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, ulong, void>)&Set_ScnSystmPrFrmStt_m_nCullingBoxCycleCount;
        native[2522] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsTestedAgainstCullingBoxes;
        native[2523] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsTestedAgainstCullingBoxes;
        native[2524] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByBoundsIndex;
        native[2525] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByBoundsIndex;
        native[2526] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByCullBoxes;
        native[2527] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByCullBoxes;
        
        // Rejection stats (2528-2537)
        native[2528] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByVis;
        native[2529] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByVis;
        native[2530] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByBackfaceCulling;
        native[2531] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByBackfaceCulling;
        native[2532] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByScreenSizeCulling;
        native[2533] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByScreenSizeCulling;
        native[2534] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByFading;
        native[2535] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByFading;
        native[2536] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumFadingObjects;
        native[2537] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumFadingObjects;
        
        // Material and light stats (2538-2545)
        native[2538] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumUniqueMaterialsSeen;
        native[2539] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumUniqueMaterialsSeen;
        native[2540] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumUnshadowedLightsInView;
        native[2541] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumUnshadowedLightsInView;
        native[2542] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumShadowedLightsInView;
        native[2543] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumShadowedLightsInView;
        native[2544] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumShadowMaps;
        native[2545] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumShadowMaps;
        
        // Render target stats (2546-2549)
        native[2546] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nNumRenderTargetBinds;
        native[2547] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nNumRenderTargetBinds;
        native[2548] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint>)&Get_ScnSystmPrFrmStt_m_nPushConstantSets;
        native[2549] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, uint, void>)&Set_ScnSystmPrFrmStt_m_nPushConstantSets;
    }
    
    /// <summary>
    /// Helper pour obtenir le pointeur vers la structure SceneSystemPerFrameStats_t.
    /// </summary>
    private static SceneSystemPerFrameStats_t* GetStatsPtr(IntPtr self)
    {
        if (_perFrameStats == null || _perFrameStats.StatsPtr == IntPtr.Zero)
            return null;
        
        // self devrait pointer vers le BindingPtr, mais on utilise directement StatsPtr
        return (SceneSystemPerFrameStats_t*)_perFrameStats.StatsPtr;
    }
    
    /// <summary>
    /// Get per-frame statistics for the scene system.
    /// 
    /// **Comportement Source 2** : Retourne un pointeur vers les stats par frame du SceneSystem.
    /// **Comportement émulation** : Retourne un pointeur vers la structure SceneSystemPerFrameStats_t en mémoire non managée.
    /// 
    /// **Responsabilité mémoire** : Les stats sont un singleton, ne pas libérer.
    /// </summary>
    /// <returns>Pointeur vers les stats par frame (SceneSystemPerFrameStats_t)</returns>
    [UnmanagedCallersOnly]
    public static void* g_pSceneSystem_GetPerFrameStats()
    {
        if (_perFrameStats == null)
        {
            // Initialiser si pas encore fait (fallback)
            _perFrameStats = new ScenePerFrameStatsData();
            _perFrameStatsHandle = HandleManager.Register(_perFrameStats);
            _perFrameStats.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(_perFrameStatsHandle);
            
            // Allouer la structure SceneSystemPerFrameStats_t en mémoire non managée
            int statsSize = Marshal.SizeOf<SceneSystemPerFrameStats_t>();
            _perFrameStats.StatsPtr = Marshal.AllocHGlobal(statsSize);
            
            // Initialiser la structure à zéro
            unsafe
            {
                var stats = (SceneSystemPerFrameStats_t*)_perFrameStats.StatsPtr;
                *stats = new SceneSystemPerFrameStats_t();
            }
        }
        
        return (void*)_perFrameStats.StatsPtr;
    }
    
    /// <summary>
    /// Create a new scene world.
    /// 
    /// **Comportement Source 2** : Crée un nouveau monde de scène avec un nom de debug.
    /// **Comportement émulation** : Crée un SceneWorldData et retourne un handle HandleManager.
    /// 
    /// **Responsabilité mémoire** : L'appelant doit appeler g_pSceneSystem_DestroyWorld() pour libérer.
    /// </summary>
    /// <param name="debugNamePtr">Pointeur vers le nom de debug (string UTF-8)</param>
    /// <returns>Handle vers le monde de scène créé, ou 0 en cas d'erreur</returns>
    [UnmanagedCallersOnly]
    public static int g_pSceneSystem_CreateWorld(IntPtr debugNamePtr)
    {
        string debugName = Marshal.PtrToStringUTF8(debugNamePtr) ?? "";
        
        var worldData = new SceneWorldData
        {
            DebugName = debugName
        };
        
        int handle = HandleManager.Register(worldData);
        worldData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreateWorld: debugName={debugName}, handle={handle}");
        
        return handle;
    }
    
    /// <summary>
    /// Destroy a scene world.
    /// 
    /// **Comportement Source 2** : Détruit un monde de scène et libère ses ressources.
    /// **Comportement émulation** : Libère le handle HandleManager et les ressources associées.
    /// </summary>
    /// <param name="worldPtr">Handle vers le monde de scène à détruire</param>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_DestroyWorld(IntPtr worldPtr)
    {
        if (worldPtr == IntPtr.Zero)
            return;
        
        int handle = (int)(long)worldPtr;
        var worldData = HandleManager.Get<SceneWorldData>(handle);
        
        if (worldData != null)
        {
            HandleManager.Unregister(handle);
            Console.WriteLine($"[NativeAOT] g_pSceneSystem_DestroyWorld: handle={handle}, debugName={worldData.DebugName}");
        }
    }
    
    // ========== Gestion des objets de scène ==========
    
    /// <summary>
    /// Delete a scene object immediately.
    /// 
    /// **Comportement Source 2** : Supprime immédiatement un objet de scène.
    /// **Comportement émulation** : Utilise HandleManager pour trouver et supprimer l'objet.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_DeleteSceneObject(IntPtr pObj)
    {
        DeleteSceneObjectInternal(pObj);
    }
    
    /// <summary>
    /// Delete a scene object at the end of the frame.
    /// 
    /// **Comportement Source 2** : Ajoute l'objet à une queue de suppression différée.
    /// **Comportement émulation** : Ajoute l'objet à la queue _pendingDeletes.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_DeleteSceneObjectAtFrameEnd(IntPtr pObj)
    {
        if (pObj == IntPtr.Zero)
            return;
        
        lock (_pendingDeletes)
        {
            _pendingDeletes.Enqueue(pObj);
        }
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_DeleteSceneObjectAtFrameEnd: queued for deletion, pObj={pObj}");
    }
    
    /// <summary>
    /// Process pending deletes (should be called at the end of each frame).
    /// This is called internally by the engine, but we provide it for completeness.
    /// </summary>
    internal static void ProcessPendingDeletes()
    {
        lock (_pendingDeletes)
        {
            while (_pendingDeletes.Count > 0)
            {
                IntPtr pObj = _pendingDeletes.Dequeue();
                // Appeler directement la logique de suppression sans passer par UnmanagedCallersOnly
                DeleteSceneObjectInternal(pObj);
            }
        }
    }
    
    /// <summary>
    /// Logique interne de suppression d'un objet de scène (appelable depuis le code managé).
    /// </summary>
    private static void DeleteSceneObjectInternal(IntPtr pObj)
    {
        if (pObj == IntPtr.Zero)
            return;
        
        // pObj est un BindingPtr, on peut le trouver via HandleManager
        int bindingHandle = (int)(long)pObj;
        int handle = HandleManager.GetHandleByBindingHandle(bindingHandle);
        
        if (handle != 0)
        {
            // Essayer de trouver et supprimer différents types d'objets de scène
            var skyBox = HandleManager.GetByBindingHandle<SkyBoxData>(bindingHandle);
            if (skyBox != null)
            {
                HandleManager.Unregister(handle);
                Console.WriteLine($"[NativeAOT] DeleteSceneObjectInternal: deleted SkyBox, handle={handle}");
                return;
            }
            
            var decal = HandleManager.GetByBindingHandle<DecalData>(bindingHandle);
            if (decal != null)
            {
                HandleManager.Unregister(handle);
                Console.WriteLine($"[NativeAOT] DeleteSceneObjectInternal: deleted Decal, handle={handle}");
                return;
            }
            
            var light = HandleManager.GetByBindingHandle<LightData>(bindingHandle);
            if (light != null)
            {
                HandleManager.Unregister(handle);
                Console.WriteLine($"[NativeAOT] DeleteSceneObjectInternal: deleted Light ({light.LightType}), handle={handle}");
                return;
            }
            
            var envMap = HandleManager.GetByBindingHandle<EnvMapData>(bindingHandle);
            if (envMap != null)
            {
                HandleManager.Unregister(handle);
                Console.WriteLine($"[NativeAOT] DeleteSceneObjectInternal: deleted EnvMap, handle={handle}");
                return;
            }
            
            var lightProbe = HandleManager.GetByBindingHandle<LightProbeVolumeData>(bindingHandle);
            if (lightProbe != null)
            {
                HandleManager.Unregister(handle);
                Console.WriteLine($"[NativeAOT] DeleteSceneObjectInternal: deleted LightProbeVolume, handle={handle}");
                return;
            }
        }
        
        // Si on n'a pas trouvé, log un warning mais ne crash pas
        Console.WriteLine($"[NativeAOT] DeleteSceneObjectInternal: object not found, pObj={pObj}");
    }
    
    // ========== Création d'objets ==========
    
    /// <summary>
    /// Create a skybox.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pSceneSystem_CreateSkyBox(IntPtr skyMaterial, IntPtr world)
    {
        var skyBoxData = new SkyBoxData
        {
            SkyMaterial = skyMaterial,
            World = world
        };
        
        int handle = HandleManager.Register(skyBoxData);
        skyBoxData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreateSkyBox: handle={handle}");
        return handle;
    }
    
    /// <summary>
    /// Create a decal.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pSceneSystem_CreateDecal(IntPtr world)
    {
        var decalData = new DecalData
        {
            World = world
        };
        
        int handle = HandleManager.Register(decalData);
        decalData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreateDecal: handle={handle}");
        return handle;
    }
    
    /// <summary>
    /// Create a point light.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pSceneSystem_CreatePointLight(IntPtr pWorld)
    {
        var lightData = new LightData
        {
            LightType = "Point",
            World = pWorld
        };
        
        int handle = HandleManager.Register(lightData);
        lightData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreatePointLight: handle={handle}");
        return handle;
    }
    
    /// <summary>
    /// Create a spot light.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pSceneSystem_CreateSpotLight(IntPtr pWorld)
    {
        var lightData = new LightData
        {
            LightType = "Spot",
            World = pWorld
        };
        
        int handle = HandleManager.Register(lightData);
        lightData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreateSpotLight: handle={handle}");
        return handle;
    }
    
    /// <summary>
    /// Create an orthographic light.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pSceneSystem_CreateOrthoLight(IntPtr pWorld)
    {
        var lightData = new LightData
        {
            LightType = "Ortho",
            World = pWorld
        };
        
        int handle = HandleManager.Register(lightData);
        lightData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreateOrthoLight: handle={handle}");
        return handle;
    }
    
    /// <summary>
    /// Create a directional light.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pSceneSystem_CreateDirectionalLight(IntPtr pWorld, Vector3* direction)
    {
        var lightData = new LightData
        {
            LightType = "Directional",
            World = pWorld,
            Direction = direction != null ? *direction : Vector3.Zero
        };
        
        int handle = HandleManager.Register(lightData);
        lightData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreateDirectionalLight: handle={handle}, direction={lightData.Direction}");
        return handle;
    }
    
    /// <summary>
    /// Create an environment map.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pSceneSystem_CreateEnvMap(IntPtr pWorld, int nProjectionMode)
    {
        var envMapData = new EnvMapData
        {
            World = pWorld,
            ProjectionMode = nProjectionMode
        };
        
        int handle = HandleManager.Register(envMapData);
        envMapData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreateEnvMap: handle={handle}, projectionMode={nProjectionMode}");
        return handle;
    }
    
    /// <summary>
    /// Create a light probe volume.
    /// </summary>
    [UnmanagedCallersOnly]
    public static int g_pSceneSystem_CreateLightProbeVolume(IntPtr pWorld)
    {
        var lightProbeData = new LightProbeVolumeData
        {
            World = pWorld
        };
        
        int handle = HandleManager.Register(lightProbeData);
        lightProbeData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreateLightProbeVolume: handle={handle}");
        return handle;
    }
    
    /// <summary>
    /// Create a ray trace world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pSceneSystem_CreateRayTraceWorld(IntPtr pWorldDebugName, int nMaxRayTypes)
    {
        string debugName = Marshal.PtrToStringUTF8(pWorldDebugName) ?? "";
        
        var rayTraceWorldData = new RayTraceWorldData
        {
            DebugName = debugName,
            MaxRayTypes = nMaxRayTypes
        };
        
        int handle = HandleManager.Register(rayTraceWorldData);
        rayTraceWorldData.BindingPtr = (IntPtr)HandleManager.GetBindingHandle(handle);
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_CreateRayTraceWorld: debugName={debugName}, maxRayTypes={nMaxRayTypes}, handle={handle}");
        return rayTraceWorldData.BindingPtr;
    }
    
    /// <summary>
    /// Destroy a ray trace world.
    /// 
    /// **Comportement Source 2** : Détruit un monde de ray tracing et libère ses ressources.
    /// **Comportement émulation** : Utilise HandleManager pour trouver et supprimer le RayTraceWorld.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_DestroyRayTraceWorld(IntPtr pRayTraceSceneWorld)
    {
        if (pRayTraceSceneWorld == IntPtr.Zero)
            return;
        
        // pRayTraceSceneWorld est un BindingPtr
        int bindingHandle = (int)(long)pRayTraceSceneWorld;
        int handle = HandleManager.GetHandleByBindingHandle(bindingHandle);
        
        if (handle != 0)
        {
            var rayTraceWorld = HandleManager.GetByBindingHandle<RayTraceWorldData>(bindingHandle);
            if (rayTraceWorld != null)
            {
                HandleManager.Unregister(handle);
                Console.WriteLine($"[NativeAOT] g_pSceneSystem_DestroyRayTraceWorld: deleted RayTraceWorld '{rayTraceWorld.DebugName}', handle={handle}");
                return;
            }
        }
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_DestroyRayTraceWorld: RayTraceWorld not found, pRayTraceSceneWorld={pRayTraceSceneWorld}");
    }
    
    // ========== Rendu ==========
    
    /// <summary>
    /// Begin rendering a dynamic view.
    /// 
    /// **Comportement Source 2** : Démarre le rendu d'une vue dynamique.
    /// **Comportement émulation** : Non implémenté, nécessite une intégration complexe avec le système de rendu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_BeginRenderingDynamicView(IntPtr pView)
    {
        throw new NotImplementedException("g_pSceneSystem_BeginRenderingDynamicView is not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Setup per-object lighting.
    /// 
    /// **Comportement Source 2** : Configure l'éclairage par objet pour le rendu.
    /// **Comportement émulation** : Non implémenté, nécessite une intégration complexe avec le système de rendu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_SetupPerObjectLighting(IntPtr renderAttributes, IntPtr pSceneObject, IntPtr pSceneLayerInterface)
    {
        throw new NotImplementedException("g_pSceneSystem_SetupPerObjectLighting is not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Downsample a texture.
    /// 
    /// **Comportement Source 2** : Réduit la résolution d'une texture.
    /// **Comportement émulation** : Non implémenté, nécessite des opérations OpenGL complexes.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_DownsampleTexture(IntPtr ctx, IntPtr src, byte nDownsampleType)
    {
        throw new NotImplementedException("g_pSceneSystem_DownsampleTexture is not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Render tiled light culling.
    /// 
    /// **Comportement Source 2** : Effectue le culling de lumière en tuiles pour optimiser le rendu.
    /// **Comportement émulation** : Non implémenté, nécessite une intégration complexe avec le système de rendu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_RenderTiledLightCulling(IntPtr pCtx, IntPtr pView, IntPtr viewport)
    {
        throw new NotImplementedException("g_pSceneSystem_RenderTiledLightCulling is not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Bind a transform slot.
    /// 
    /// **Comportement Source 2** : Lie un slot de transformation pour le rendu.
    /// **Comportement émulation** : Non implémenté, nécessite une intégration avec le système de rendu.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_BindTransformSlot(IntPtr pCtx, int nVBSlot, int nTransformSlotIndex)
    {
        throw new NotImplementedException("g_pSceneSystem_BindTransformSlot is not yet implemented in the linux emulation layer");
    }
    
    // ========== Utilitaires ==========
    
    /// <summary>
    /// Get a well-known texture by ID.
    /// 
    /// **Comportement Source 2** : Retourne une texture système bien connue (white, black, checkerboard, etc.).
    /// **Comportement émulation** : Crée et cache les textures bien connues à la demande.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pSceneSystem_GetWellKnownTexture(long a)
    {
        // Vérifier le cache
        lock (_wellKnownTextures)
        {
            if (_wellKnownTextures.TryGetValue(a, out IntPtr cached))
            {
                return cached;
            }
        }
        
        // Créer la texture bien connue selon l'ID
        IntPtr textureHandle = CreateWellKnownTexture(a);
        
        if (textureHandle != IntPtr.Zero)
        {
            lock (_wellKnownTextures)
            {
                _wellKnownTextures[a] = textureHandle;
            }
        }
        
        return textureHandle;
    }
    
    /// <summary>
    /// Crée une texture bien connue selon son ID.
    /// </summary>
    private static IntPtr CreateWellKnownTexture(long textureId)
    {
        // Utiliser RenderDevice.FindOrCreateTexture2 pour créer les textures
        // Les textures bien connues sont généralement 1x1 ou 2x2
        string textureName = $"__wellknown_texture_{textureId}";
        
        // Pour l'instant, retourner NotImplementedException car la création de textures
        // nécessite une intégration avec RenderDevice qui n'est pas encore complète
        // Les textures bien connues seront créées par le système de rendu natif
        throw new NotImplementedException($"g_pSceneSystem_GetWellKnownTexture: texture ID {textureId} not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Get a well-known material handle by ID.
    /// 
    /// **Comportement Source 2** : Retourne un matériau système bien connu (point light, spot light, etc.).
    /// **Comportement émulation** : Crée et cache les matériaux bien connus à la demande.
    /// </summary>
    [UnmanagedCallersOnly]
    public static IntPtr g_pSceneSystem_GetWellKnownMaterialHandle(long a)
    {
        // Vérifier le cache
        lock (_wellKnownMaterials)
        {
            if (_wellKnownMaterials.TryGetValue(a, out IntPtr cached))
            {
                return cached;
            }
        }
        
        // Créer le matériau bien connu selon l'ID
        IntPtr materialHandle = CreateWellKnownMaterial(a);
        
        if (materialHandle != IntPtr.Zero)
        {
            lock (_wellKnownMaterials)
            {
                _wellKnownMaterials[a] = materialHandle;
            }
        }
        
        return materialHandle;
    }
    
    /// <summary>
    /// Crée un matériau bien connu selon son ID.
    /// </summary>
    private static IntPtr CreateWellKnownMaterial(long materialId)
    {
        // Utiliser MaterialSystem pour créer les matériaux bien connus
        // Les matériaux bien connus utilisent des shaders spécifiques du système
        // Pour l'instant, retourner NotImplementedException car la création de matériaux
        // bien connus nécessite une intégration avec MaterialSystem qui n'est pas encore complète
        throw new NotImplementedException($"g_pSceneSystem_GetWellKnownMaterialHandle: material ID {materialId} not yet implemented in the linux emulation layer");
    }
    
    /// <summary>
    /// Mark an environment map object as updated.
    /// 
    /// **Comportement Source 2** : Marque un environment map comme mis à jour pour forcer le rafraîchissement.
    /// **Comportement émulation** : No-op pour l'instant (l'objet est déjà géré par HandleManager).
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_MarkEnvironmentMapObjectUpdated(IntPtr pEnvMap)
    {
        if (pEnvMap == IntPtr.Zero)
            return;
        
        // No-op : L'objet est déjà géré par HandleManager
        // Dans Source 2, cela déclencherait une mise à jour du rendu, mais pour l'émulation c'est suffisant
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_MarkEnvironmentMapObjectUpdated: pEnvMap={pEnvMap}");
    }
    
    /// <summary>
    /// Mark a light probe volume object as updated.
    /// 
    /// **Comportement Source 2** : Marque un light probe volume comme mis à jour pour forcer le rafraîchissement.
    /// **Comportement émulation** : No-op pour l'instant (l'objet est déjà géré par HandleManager).
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_MarkLightProbeVolumeObjectUpdated(IntPtr pLightProbe)
    {
        if (pLightProbe == IntPtr.Zero)
            return;
        
        // No-op : L'objet est déjà géré par HandleManager
        // Dans Source 2, cela déclencherait une mise à jour du rendu, mais pour l'émulation c'est suffisant
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_MarkLightProbeVolumeObjectUpdated: pLightProbe={pLightProbe}");
    }
    
    // ========== Culling et Fog ==========
    
    /// <summary>
    /// Add a culling box to a world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint g_pSceneSystem_AddCullingBox(IntPtr pWorld, int nCullMode, Vector3* vOrigin, Angles* vAngles, Vector3* vExtents)
    {
        if (pWorld == IntPtr.Zero)
            return 0;
        
        var cullingBox = new CullingBoxData
        {
            World = pWorld,
            CullMode = nCullMode,
            Origin = vOrigin != null ? *vOrigin : Vector3.Zero,
            Angles = vAngles != null ? *vAngles : Angles.Zero,
            Extents = vExtents != null ? *vExtents : Vector3.Zero
        };
        
        uint id = _nextCullingBoxId++;
        _cullingBoxes[id] = cullingBox;
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_AddCullingBox: id={id}, cullMode={nCullMode}");
        return id;
    }
    
    /// <summary>
    /// Remove a culling box from a world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_RemoveCullingBox(IntPtr pWorld, uint nBoxId)
    {
        if (pWorld == IntPtr.Zero || nBoxId == 0)
            return;
        
        if (_cullingBoxes.Remove(nBoxId))
        {
            Console.WriteLine($"[NativeAOT] g_pSceneSystem_RemoveCullingBox: id={nBoxId}");
        }
    }
    
    /// <summary>
    /// Add a volumetric fog volume to a world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static uint g_pSceneSystem_AddVolumetricFogVolume(IntPtr pWorld, IntPtr volume)
    {
        if (pWorld == IntPtr.Zero)
            return 0;
        
        // volume is a pointer to NativeEngine.SceneVolumetricFogVolume (internal type, use IntPtr)
        var fogVolume = new VolumetricFogVolumeData
        {
            World = pWorld
        };
        
        uint id = _nextVolumetricFogVolumeId++;
        _volumetricFogVolumes[id] = fogVolume;
        
        Console.WriteLine($"[NativeAOT] g_pSceneSystem_AddVolumetricFogVolume: id={id}");
        return id;
    }
    
    /// <summary>
    /// Remove a volumetric fog volume from a world.
    /// </summary>
    [UnmanagedCallersOnly]
    public static void g_pSceneSystem_RemoveVolumetricFogVolume(IntPtr pWorld, uint nId)
    {
        if (pWorld == IntPtr.Zero || nId == 0)
            return;
        
        if (_volumetricFogVolumes.Remove(nId))
        {
            Console.WriteLine($"[NativeAOT] g_pSceneSystem_RemoveVolumetricFogVolume: id={nId}");
        }
    }
    
    // ========== SceneSystemPerFrameStats_t getter/setter functions ==========
    // Toutes ces fonctions utilisent [SuppressGCTransition] pour éviter les transitions GC
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nTrianglesRendered(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nTrianglesRendered : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nTrianglesRendered(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nTrianglesRendered = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nArtistTrianglesRendered(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nArtistTrianglesRendered : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nArtistTrianglesRendered(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nArtistTrianglesRendered = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nRenderBatchDraws(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nRenderBatchDraws : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nRenderBatchDraws(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nRenderBatchDraws = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nDrawCalls(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nDrawCalls : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nDrawCalls(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nDrawCalls = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nDrawPrimitives(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nDrawPrimitives : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nDrawPrimitives(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nDrawPrimitives = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nBaseSceneObjectPrimDraws(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nBaseSceneObjectPrimDraws : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nBaseSceneObjectPrimDraws(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nBaseSceneObjectPrimDraws = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nAnimatableObjectPrimDraws(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nAnimatableObjectPrimDraws : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nAnimatableObjectPrimDraws(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nAnimatableObjectPrimDraws = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nAggregateSceneObjectPrimDraws(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nAggregateSceneObjectPrimDraws : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nAggregateSceneObjectPrimDraws(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nAggregateSceneObjectPrimDraws = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nAggregateSceneObjectsFullyCulled(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nAggregateSceneObjectsFullyCulled : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nAggregateSceneObjectsFullyCulled(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nAggregateSceneObjectsFullyCulled = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nAggregateSceneObjectDrawCalls(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nAggregateSceneObjectDrawCalls : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nAggregateSceneObjectDrawCalls(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nAggregateSceneObjectDrawCalls = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumMaterialCompute(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumMaterialCompute : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumMaterialCompute(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumMaterialCompute = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumMaterialSet(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumMaterialSet : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumMaterialSet(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumMaterialSet = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumSimilarMaterialSet(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumSimilarMaterialSet : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumSimilarMaterialSet(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumSimilarMaterialSet = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumTextureOnlyMaterialSet(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumTextureOnlyMaterialSet : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumTextureOnlyMaterialSet(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumTextureOnlyMaterialSet = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumVfxEval(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumVfxEval : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumVfxEval(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumVfxEval = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumVfxRule(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumVfxRule : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumVfxRule(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumVfxRule = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumConstantBufferUpdates(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumConstantBufferUpdates : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumConstantBufferUpdates(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumConstantBufferUpdates = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumConstantBufferBytes(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumConstantBufferBytes : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumConstantBufferBytes(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumConstantBufferBytes = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nMaterialChangesNonShadow(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nMaterialChangesNonShadow : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nMaterialChangesNonShadow(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nMaterialChangesNonShadow = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nMaterialChangesNonShadowInitial(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nMaterialChangesNonShadowInitial : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nMaterialChangesNonShadowInitial(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nMaterialChangesNonShadowInitial = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nMaterialChangesShadow(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nMaterialChangesShadow : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nMaterialChangesShadow(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nMaterialChangesShadow = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nMaterialChangesShadowInitial(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nMaterialChangesShadowInitial : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nMaterialChangesShadowInitial(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nMaterialChangesShadowInitial = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nMaterialChangesShadowAlphaTested(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nMaterialChangesShadowAlphaTested : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nMaterialChangesShadowAlphaTested(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nMaterialChangesShadowAlphaTested = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nCopyMaterialChangesNonShadow(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nCopyMaterialChangesNonShadow : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nCopyMaterialChangesNonShadow(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nCopyMaterialChangesNonShadow = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nMaxTransformRow(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nMaxTransformRow : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nMaxTransformRow(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nMaxTransformRow = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumRowsUsed(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumRowsUsed : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumRowsUsed(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumRowsUsed = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsTested(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsTested : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsTested(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsTested = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsPreCullCheck(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsPreCullCheck : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsPreCullCheck(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsPreCullCheck = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsPassingCullCheck(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsPassingCullCheck : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsPassingCullCheck(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsPassingCullCheck = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumVerticesReferenced(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumVerticesReferenced : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumVerticesReferenced(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumVerticesReferenced = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumPrimaryContexts(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumPrimaryContexts : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumPrimaryContexts(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumPrimaryContexts = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumSecondaryContexts(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumSecondaryContexts : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumSecondaryContexts(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumSecondaryContexts = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumDisplayListsSubmitted(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumDisplayListsSubmitted : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumDisplayListsSubmitted(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumDisplayListsSubmitted = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get_ScnSystmPrFrmStt_m_nNumViewsRendered(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumViewsRendered : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumViewsRendered(IntPtr self, int value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumViewsRendered = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumResolves(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumResolves : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumResolves(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumResolves = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumCullBoxes(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumCullBoxes : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumCullBoxes(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumCullBoxes = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static ulong Get_ScnSystmPrFrmStt_m_nCullingBoxCycleCount(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nCullingBoxCycleCount : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nCullingBoxCycleCount(IntPtr self, ulong value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nCullingBoxCycleCount = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsTestedAgainstCullingBoxes(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsTestedAgainstCullingBoxes : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsTestedAgainstCullingBoxes(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsTestedAgainstCullingBoxes = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByBoundsIndex(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsRejectedByBoundsIndex : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByBoundsIndex(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsRejectedByBoundsIndex = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByCullBoxes(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsRejectedByCullBoxes : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByCullBoxes(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsRejectedByCullBoxes = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByVis(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsRejectedByVis : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByVis(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsRejectedByVis = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByBackfaceCulling(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsRejectedByBackfaceCulling : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByBackfaceCulling(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsRejectedByBackfaceCulling = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByScreenSizeCulling(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsRejectedByScreenSizeCulling : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByScreenSizeCulling(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsRejectedByScreenSizeCulling = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumObjectsRejectedByFading(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumObjectsRejectedByFading : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumObjectsRejectedByFading(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumObjectsRejectedByFading = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumFadingObjects(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumFadingObjects : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumFadingObjects(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumFadingObjects = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumUniqueMaterialsSeen(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumUniqueMaterialsSeen : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumUniqueMaterialsSeen(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumUniqueMaterialsSeen = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumUnshadowedLightsInView(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumUnshadowedLightsInView : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumUnshadowedLightsInView(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumUnshadowedLightsInView = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumShadowedLightsInView(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumShadowedLightsInView : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumShadowedLightsInView(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumShadowedLightsInView = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumShadowMaps(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumShadowMaps : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumShadowMaps(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumShadowMaps = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nNumRenderTargetBinds(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nNumRenderTargetBinds : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nNumRenderTargetBinds(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nNumRenderTargetBinds = value;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static uint Get_ScnSystmPrFrmStt_m_nPushConstantSets(IntPtr self)
    {
        var stats = GetStatsPtr(self);
        return stats != null ? stats->m_nPushConstantSets : 0;
    }
    
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set_ScnSystmPrFrmStt_m_nPushConstantSets(IntPtr self, uint value)
    {
        var stats = GetStatsPtr(self);
        if (stats != null) stats->m_nPushConstantSets = value;
    }
}

