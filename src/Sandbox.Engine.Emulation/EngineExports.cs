using System.Runtime.InteropServices;
using Sandbox.Engine.Emulation.Generated;
using Sandbox.Engine.Emulation.Physics;
using Sandbox.Engine.Emulation.Common;
using NativeEngine;
using Silk.NET.Maths;
using System.IO;
using Silk.NET.GLFW;
using Sandbox;
using Sandbox.UI;
using Silk.NET.OpenGL;
using Sandbox.Engine.Emulation.Rendering;
using Sandbox.Engine.Emulation.Camera;
using Sandbox.Engine.Emulation.RenderAttributes;
using Sandbox.Engine.Emulation.Material;
using Sandbox.Engine.Emulation.Platform;
using Sandbox.Engine.Emulation.Steam;
using Sandbox.Engine.Emulation.Engine;
using Sandbox.Engine.Emulation.Resource;
using Sandbox.Engine.Emulation.Audio;
using Sandbox.Engine.Emulation.Input;
using Sandbox.Engine.Emulation.Video;
using Sandbox.Engine.Emulation.CUtl;
using Sandbox.Engine;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sandbox.Engine.Emulation;

public static unsafe class EngineExports
{
    private static bool _isReady = false;
    
    // Rendering stubs - created once and reused
    private static EmulatedRenderContext? _renderContext;
    // SceneView / SceneLayer placeholders were removed; handled by dedicated modules.
    
    // Note: _glfw, _windowHandle, _gl sont maintenant gérés par PlatformFunctions
    // Utiliser PlatformFunctions.GetGL(), GetGlfw(), GetWindowHandle() pour y accéder

    /// <summary>
    /// Main engine initialization function called by the managed runtime.
    /// This is the entry point that receives function pointers from both sides.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "igen_engine")]
    public static void IGenEngine(int hash, void* managedFunctions, void* nativeFunctions, int* structSizes)
    {
        Console.WriteLine($"[NativeAOT Engine] igen_engine called with hash: {hash}");

        void** managed = (void**)managedFunctions;
        void** native  = (void**)nativeFunctions;

        // 1. Store managed function pointers (Imports)
        EngineGlue.StoreImports(managed);

        // 2. Fill native function pointers (Exports) AVANT nos patchs
        //    pour éviter qu'ils soient écrasés.
        Exports.FillNativeFunctionsEngine(managed, native, structSizes);
        
        // 3. Initialize all modules (in dependency order) qui patchent native[]
        RenderAttributes.RenderAttributes.Init(native);
        Material.MaterialSystem.Init(native);
        Model.ModelSystem.Init(native);
        Texture.TextureSystem.Init(native);
        Rendering.RenderDevice.Init(native); // RenderDevice doit être initialisé après TextureSystem
        Rendering.VertexLayoutInterop.Init(native);
        Rendering.RenderTools.Init(native);
        Rendering.EmulatedSceneLayer.Init(native);
        Rendering.EmulatedSceneView.Init(native);
        Camera.CameraRenderer.Init(native);
        Platform.PlatformFunctions.Init(native);
        Steam.SteamAPI.Init(native);
        Steam.SteamInventoryResult.Init(native);
        Steam.SteamInterfaces.Init(native);
        Engine.EngineGlue.Init(native);
        Resource.ResourceCompilerSystem.Init(native); // ResourceCompilerSystem doit être initialisé avant ResourceSystem
        Resource.ResourceSystem.Init(native);
        Audio.AudioMixBuffer.Init(native);
        Audio.AudioMixDeviceBuffers.Init(native);
        Audio.DspPreset.Init(native);
        Audio.AudioDevice.Init(native);
        Physics.PhysicsSystem.Init(native);
        Scene.SceneSystem.Init(native);
        Scene.EmulatedSceneWorld.Init(native);
        Input.InputService.Init(native);
        Input.InputSystem.Init(native);
        Video.VideoPlayer.Init(native);
        CUtl.CUtlBuffer.Init(native);
        CUtl.CUtlSymbolTable.Init(native);
        CUtl.CUtlVectorString.Init(native);
        CUtl.CUtlVectorFloat.Init(native);
        CUtl.CUtlVectorTexture.Init(native);
        CUtl.CUtlVectorTraceResult.Init(native);
        CUtl.CUtlVectorUInt32.Init(native);
        CUtl.CUtlVectorVector.Init(native);
        
        // 4. Initialize cross-module references (after modules are initialized)
        // Note: OpenGL sera initialisé dans PlatformFunctions.SourceEngineInit
        // Les références seront partagées via PlatformFunctions.GetGL(), GetGlfw(), GetWindowHandle()
        var windowHandle = Platform.PlatformFunctions.GetWindowHandle();
        if (windowHandle != null)
        {
            Material.MaterialSystem.SetWindowHandle(windowHandle);
        }
        
        // MaterialSystem patching is now done in MaterialSystem.Init()
        // PlatformFunctions patching is now done in PlatformFunctions.Init()

        // g_pngnSrvcMgr_GetEngineSwapChain: Line 1433 -> Index 1420
        native[1420] = (void*)(delegate* unmanaged<void*>)&g_pngnSrvcMgr_GetEngineSwapChain;

        native[1421] = (void*)(delegate* unmanaged< void*, void*, void* >)&GetEngineSwapChainSize;

        native[1474] = (void*)(delegate* unmanaged< void*, int, void*, void*, int, void* >)&FindOrCreateTexture2;
        
        // SourceEngineFrame reste ici car il a une logique de rendu complexe
        // Signature exacte depuis Interop.Engine.cs ligne 16341: delegate* unmanaged< IntPtr, double, double, int >
        // native[1594] = (void*)(delegate* unmanaged<IntPtr, double, double, int>)&SourceEngineFrame;

        // Physics System Exports are now handled by PhysicsSystem.Init() and PhysicsWorld.Init()
        // All g_pPhysicsSystem_* and IPhysicsWorld_* functions are patched in their respective modules
        
        // File System Exports - Critical for finding compiled DLLs
        // g_pFllFlSystm_ResetProjectPaths: Line 1440 -> Index 1427
        native[1427] = (void*)(delegate* unmanaged<int, void*>)&g_pFllFlSystm_ResetProjectPaths;
        
        // g_pFllFlSystm_AddProjectPath: Line 1441 -> Index 1428
        native[1428] = (void*)(delegate* unmanaged<void*, void*, void*>)&g_pFllFlSystm_AddProjectPath;
        
        // g_pFllFlSystm_AddCloudPath: Line 1442 -> Index 1429
        native[1429] = (void*)(delegate* unmanaged<void*, void*, void*>)&g_pFllFlSystm_AddCloudPath;
        
        // Resource System Exports sont maintenant gérés par Resource.ResourceSystem.Init()
        
        // Yoga Functions - Patch all globalYoga_* functions
        // Starting from index 1638 (globalYoga_YGNodeNew)
        PatchYogaFunctions(native);
        
        // DSP Preset Functions sont maintenant gérées par Audio.DspPreset.Init()
        // Audio Mix Buffer Functions sont maintenant gérées par Audio.AudioMixBuffer.Init()
        // Audio Mix Device Buffers Functions sont maintenant gérées par Audio.AudioMixDeviceBuffers.Init()

        // PerformanceTrace module initialization
        PerformanceTrace.PerformanceTrace.Init(native);

        _isReady = true;
    }
    
    private static void PatchYogaFunctions(void** native)
    {
        // Node creation and management - Indexes from Interop.Engine.cs
        native[1624] = (void*)(delegate* unmanaged<void*>)&globalYoga_YGNodeNew;
        native[1625] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeNewWithConfig;
        native[1626] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeFree;
        native[1627] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeReset;
        native[1628] = (void*)(delegate* unmanaged<void*, float, float, long, void*>)&globalYoga_YGNodeCalculateLayout;
        native[1629] = (void*)(delegate* unmanaged<void*, int>)&globalYoga_YGNodeGetHasNewLayout;
        native[1630] = (void*)(delegate* unmanaged<void*, int, void*>)&globalYoga_YGNodeSetHasNewLayout;
        native[1631] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeIsDirty;
        native[1632] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeMarkDirty;
        native[1633] = (void*)(delegate* unmanaged<void*, void*, int, void*>)&globalYoga_YGNodeInsertChild;
        native[1634] = (void*)(delegate* unmanaged<void*, void*, void*>)&globalYoga_YGNodeRemoveChild;
        native[1635] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeRemoveAllChildren;
        native[1636] = (void*)(delegate* unmanaged<void*, ulong>)&globalYoga_YGNodeGetChildCount;
        native[1637] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeGetParent;
        native[1638] = (void*)(delegate* unmanaged<void*, void*, void*>)&globalYoga_YGNodeSetConfig;
        
        // Layout getters
        native[1639] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeLayoutGetLeft;
        native[1640] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeLayoutGetTop;
        native[1641] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeLayoutGetRight;
        native[1642] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeLayoutGetBottom;
        native[1643] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeLayoutGetWidth;
        native[1644] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeLayoutGetHeight;
        native[1645] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeLayoutGetDirection;
        native[1646] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeLayoutGetHadOverflow;
        native[1647] = (void*)(delegate* unmanaged<void*, long, float>)&globalYoga_YGNodeLayoutGetMargin;
        native[1648] = (void*)(delegate* unmanaged<void*, long, float>)&globalYoga_YGNodeLayoutGetBorder;
        native[1649] = (void*)(delegate* unmanaged<void*, long, float>)&globalYoga_YGNodeLayoutGetPadding;
        
        // Config functions
        native[1650] = (void*)(delegate* unmanaged<void*>)&globalYoga_YGConfigNew;
        native[1651] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGConfigFree;
        native[1652] = (void*)(delegate* unmanaged<void*, int, void*>)&globalYoga_YGConfigSetUseWebDefaults;
        native[1653] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGConfigSetPointScaleFactor;
        native[1654] = (void*)(delegate* unmanaged<void*, void*, void*>)&globalYoga_YGNodeCopyStyle;
        
        // Style setters/getters - Direction
        native[1655] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetDirection;
        native[1656] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetDirection;
        native[1657] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetFlexDirection;
        native[1658] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetFlexDirection;
        native[1659] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetJustifyContent;
        native[1660] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetJustifyContent;
        native[1661] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetAlignContent;
        native[1662] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetAlignContent;
        native[1663] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetAlignItems;
        native[1664] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetAlignItems;
        native[1665] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetAlignSelf;
        native[1666] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetAlignSelf;
        native[1667] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetPositionType;
        native[1668] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetPositionType;
        native[1669] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetFlexWrap;
        native[1670] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetFlexWrap;
        native[1671] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetOverflow;
        native[1672] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetOverflow;
        native[1673] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetDisplay;
        native[1674] = (void*)(delegate* unmanaged<void*, long>)&globalYoga_YGNodeStyleGetDisplay;
        
        // Style setters/getters - Flex
        native[1675] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetFlex;
        native[1676] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeStyleGetFlex;
        native[1677] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetFlexGrow;
        native[1678] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeStyleGetFlexGrow;
        native[1679] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetFlexShrink;
        native[1680] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeStyleGetFlexShrink;
        native[1681] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetFlexBasis;
        native[1682] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetFlexBasisPercent;
        native[1683] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeStyleSetFlexBasisAuto;
        native[1684] = (void*)(delegate* unmanaged<void*, YGValue>)&globalYoga_YGNodeStyleGetFlexBasis;
        
        // Style setters/getters - Position, Margin, Padding, Border
        native[1685] = (void*)(delegate* unmanaged<void*, long, float, void*>)&globalYoga_YGNodeStyleSetPosition;
        native[1686] = (void*)(delegate* unmanaged<void*, long, float, void*>)&globalYoga_YGNodeStyleSetPositionPercent;
        native[1687] = (void*)(delegate* unmanaged<void*, long, YGValue>)&globalYoga_YGNodeStyleGetPosition;
        native[1688] = (void*)(delegate* unmanaged<void*, long, float, void*>)&globalYoga_YGNodeStyleSetMargin;
        native[1689] = (void*)(delegate* unmanaged<void*, long, float, void*>)&globalYoga_YGNodeStyleSetMarginPercent;
        native[1690] = (void*)(delegate* unmanaged<void*, long, void*>)&globalYoga_YGNodeStyleSetMarginAuto;
        native[1691] = (void*)(delegate* unmanaged<void*, long, YGValue>)&globalYoga_YGNodeStyleGetMargin;
        native[1692] = (void*)(delegate* unmanaged<void*, long, float, void*>)&globalYoga_YGNodeStyleSetPadding;
        native[1693] = (void*)(delegate* unmanaged<void*, long, float, void*>)&globalYoga_YGNodeStyleSetPaddingPercent;
        native[1694] = (void*)(delegate* unmanaged<void*, long, YGValue>)&globalYoga_YGNodeStyleGetPadding;
        native[1695] = (void*)(delegate* unmanaged<void*, long, float, void*>)&globalYoga_YGNodeStyleSetBorder;
        native[1696] = (void*)(delegate* unmanaged<void*, long, float>)&globalYoga_YGNodeStyleGetBorder;
        native[1697] = (void*)(delegate* unmanaged<void*, long, float, void*>)&globalYoga_YGNodeStyleSetGap;
        native[1698] = (void*)(delegate* unmanaged<void*, long, float>)&globalYoga_YGNodeStyleGetGap;
        
        // Style setters/getters - Width, Height
        native[1699] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetWidth;
        native[1700] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetWidthPercent;
        native[1701] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeStyleSetWidthAuto;
        native[1702] = (void*)(delegate* unmanaged<void*, YGValue>)&globalYoga_YGNodeStyleGetWidth;
        native[1703] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetHeight;
        native[1704] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetHeightPercent;
        native[1705] = (void*)(delegate* unmanaged<void*, void*>)&globalYoga_YGNodeStyleSetHeightAuto;
        native[1706] = (void*)(delegate* unmanaged<void*, YGValue>)&globalYoga_YGNodeStyleGetHeight;
        native[1707] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetMinWidth;
        native[1708] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetMinWidthPercent;
        native[1709] = (void*)(delegate* unmanaged<void*, YGValue>)&globalYoga_YGNodeStyleGetMinWidth;
        native[1710] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetMinHeight;
        native[1711] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetMinHeightPercent;
        native[1712] = (void*)(delegate* unmanaged<void*, YGValue>)&globalYoga_YGNodeStyleGetMinHeight;
        native[1713] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetMaxWidth;
        native[1714] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetMaxWidthPercent;
        native[1715] = (void*)(delegate* unmanaged<void*, YGValue>)&globalYoga_YGNodeStyleGetMaxWidth;
        native[1716] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetMaxHeight;
        native[1717] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetMaxHeightPercent;
        native[1718] = (void*)(delegate* unmanaged<void*, YGValue>)&globalYoga_YGNodeStyleGetMaxHeight;
        native[1719] = (void*)(delegate* unmanaged<void*, float, void*>)&globalYoga_YGNodeStyleSetAspectRatio;
        native[1720] = (void*)(delegate* unmanaged<void*, float>)&globalYoga_YGNodeStyleGetAspectRatio;
        native[1721] = (void*)(delegate* unmanaged<void*, void*, void*>)&globalYoga_YGNodeSetMeasureFunc;
        native[1722] = (void*)(delegate* unmanaged<void*, int>)&globalYoga_YGNodeHasMeasureFunc;
    }
    
    // Yoga wrapper functions that match the expected signatures from engine.Generated.cs
    // These call YogaInterop which uses P/Invoke to call YogaLayout functions
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeNew()
    {
        return (void*)YogaInterop.YGNodeNew();
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeNewWithConfig(void* config)
    {
        return (void*)YogaInterop.YGNodeNewWithConfig((IntPtr)config);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeFree(void* r)
    {
        YogaInterop.YGNodeFree((IntPtr)r);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeReset(void* r)
    {
        YogaInterop.YGNodeReset((IntPtr)r);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeCalculateLayout(void* r, float ownerWidth, float ownerHeight, long direction)
    {
        YogaInterop.YGNodeCalculateLayout((IntPtr)r, ownerWidth, ownerHeight, (int)direction);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static int globalYoga_YGNodeGetHasNewLayout(void* r)
    {
        return YogaInterop.YGNodeGetHasNewLayout((IntPtr)r);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeSetHasNewLayout(void* r, int b)
    {
        YogaInterop.YGNodeSetHasNewLayout((IntPtr)r, b);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeIsDirty(void* r)
    {
        YogaInterop.YGNodeIsDirty((IntPtr)r);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeMarkDirty(void* r)
    {
        YogaInterop.YGNodeMarkDirty((IntPtr)r);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeInsertChild(void* owner, void* child, int index)
    {
        YogaInterop.YGNodeInsertChild((IntPtr)owner, (IntPtr)child, index);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeRemoveChild(void* owner, void* child)
    {
        YogaInterop.YGNodeRemoveChild((IntPtr)owner, (IntPtr)child);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeRemoveAllChildren(void* owner)
    {
        YogaInterop.YGNodeRemoveAllChildren((IntPtr)owner);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static ulong globalYoga_YGNodeGetChildCount(void* owner)
    {
        return YogaInterop.YGNodeGetChildCount((IntPtr)owner);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeGetParent(void* owner)
    {
        return (void*)YogaInterop.YGNodeGetParent((IntPtr)owner);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeSetConfig(void* r, void* config)
    {
        YogaInterop.YGNodeSetConfig((IntPtr)r, (IntPtr)config);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetLeft(void* r)
    {
        return YogaInterop.YGNodeLayoutGetLeft((IntPtr)r);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetTop(void* r)
    {
        return YogaInterop.YGNodeLayoutGetTop((IntPtr)r);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetRight(void* r)
    {
        return YogaInterop.YGNodeLayoutGetRight((IntPtr)r);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetBottom(void* r)
    {
        return YogaInterop.YGNodeLayoutGetBottom((IntPtr)r);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetWidth(void* r)
    {
        return YogaInterop.YGNodeLayoutGetWidth((IntPtr)r);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetHeight(void* r)
    {
        return YogaInterop.YGNodeLayoutGetHeight((IntPtr)r);
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeLayoutGetDirection(void* r)
    {
        return YogaInterop.YGNodeLayoutGetDirection((IntPtr)r);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetHadOverflow(void* r)
    {
        return YogaInterop.YGNodeLayoutGetHadOverflow((IntPtr)r);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetMargin(void* node, long edge)
    {
        return YogaInterop.YGNodeLayoutGetMargin((IntPtr)node, edge);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetBorder(void* node, long edge)
    {
        return YogaInterop.YGNodeLayoutGetBorder((IntPtr)node, edge);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeLayoutGetPadding(void* node, long edge)
    {
        return YogaInterop.YGNodeLayoutGetPadding((IntPtr)node, edge);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGConfigNew()
    {
        IntPtr config = YogaInterop.YGConfigNew();
        // Explicitly set the default logger to ensure it's properly initialized
        // Passing IntPtr.Zero (nullptr) makes YGConfigSetLogger use getDefaultLogger()
        return (void*)config;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGConfigFree(void* c)
    {
        YogaInterop.YGConfigFree((IntPtr)c);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGConfigSetUseWebDefaults(void* config, int enabled)
    {
        YogaInterop.YGConfigSetUseWebDefaults((IntPtr)config, enabled);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGConfigSetPointScaleFactor(void* config, float pixelsInPoint)
    {
        YogaInterop.YGConfigSetPointScaleFactor((IntPtr)config, pixelsInPoint);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeCopyStyle(void* dstNode, void* srcNode)
    {
        YogaInterop.YGNodeCopyStyle((IntPtr)dstNode, (IntPtr)srcNode);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetDirection(void* node, long direction)
    {
        YogaInterop.YGNodeStyleSetDirection((IntPtr)node, direction);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetDirection(void* node)
    {
        return YogaInterop.YGNodeStyleGetDirection((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetFlexDirection(void* node, long flexDirection)
    {
        YogaInterop.YGNodeStyleSetFlexDirection((IntPtr)node, flexDirection);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetFlexDirection(void* node)
    {
        return YogaInterop.YGNodeStyleGetFlexDirection((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetJustifyContent(void* node, long justifyContent)
    {
        YogaInterop.YGNodeStyleSetJustifyContent((IntPtr)node, justifyContent);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetJustifyContent(void* node)
    {
        return YogaInterop.YGNodeStyleGetJustifyContent((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetAlignContent(void* node, long alignContent)
    {
        YogaInterop.YGNodeStyleSetAlignContent((IntPtr)node, alignContent);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetAlignContent(void* node)
    {
        return YogaInterop.YGNodeStyleGetAlignContent((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetAlignItems(void* node, long alignItems)
    {
        YogaInterop.YGNodeStyleSetAlignItems((IntPtr)node, alignItems);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetAlignItems(void* node)
    {
        return YogaInterop.YGNodeStyleGetAlignItems((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetAlignSelf(void* node, long alignSelf)
    {
        YogaInterop.YGNodeStyleSetAlignSelf((IntPtr)node, alignSelf);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetAlignSelf(void* node)
    {
        return YogaInterop.YGNodeStyleGetAlignSelf((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetPositionType(void* node, long positionType)
    {
        YogaInterop.YGNodeStyleSetPositionType((IntPtr)node, positionType);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetPositionType(void* node)
    {
        return YogaInterop.YGNodeStyleGetPositionType((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetFlexWrap(void* node, long flexWrap)
    {
        YogaInterop.YGNodeStyleSetFlexWrap((IntPtr)node, flexWrap);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetFlexWrap(void* node)
    {
        return YogaInterop.YGNodeStyleGetFlexWrap((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetOverflow(void* node, long overflow)
    {
        YogaInterop.YGNodeStyleSetOverflow((IntPtr)node, overflow);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetOverflow(void* node)
    {
        return YogaInterop.YGNodeStyleGetOverflow((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetDisplay(void* node, long display)
    {
        YogaInterop.YGNodeStyleSetDisplay((IntPtr)node, display);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static long globalYoga_YGNodeStyleGetDisplay(void* node)
    {
        return YogaInterop.YGNodeStyleGetDisplay((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetFlex(void* node, float flex)
    {
        YogaInterop.YGNodeStyleSetFlex((IntPtr)node, flex);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeStyleGetFlex(void* node)
    {
        return YogaInterop.YGNodeStyleGetFlex((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetFlexGrow(void* node, float flexGrow)
    {
        YogaInterop.YGNodeStyleSetFlexGrow((IntPtr)node, flexGrow);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeStyleGetFlexGrow(void* node)
    {
        return YogaInterop.YGNodeStyleGetFlexGrow((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetFlexShrink(void* node, float flexShrink)
    {
        YogaInterop.YGNodeStyleSetFlexShrink((IntPtr)node, flexShrink);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeStyleGetFlexShrink(void* node)
    {
        return YogaInterop.YGNodeStyleGetFlexShrink((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetFlexBasis(void* node, float flexBasis)
    {
        YogaInterop.YGNodeStyleSetFlexBasis((IntPtr)node, flexBasis);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetFlexBasisPercent(void* node, float flexBasis)
    {
        YogaInterop.YGNodeStyleSetFlexBasisPercent((IntPtr)node, flexBasis);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetFlexBasisAuto(void* node)
    {
        return (void*)YogaInterop.YGNodeStyleSetFlexBasisAuto((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetFlexBasis(void* node)
    {
        return YogaInterop.YGNodeStyleGetFlexBasis((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetPosition(void* node, long edge, float position)
    {
        YogaInterop.YGNodeStyleSetPosition((IntPtr)node, edge, position);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetPositionPercent(void* node, long edge, float position)
    {
        YogaInterop.YGNodeStyleSetPositionPercent((IntPtr)node, edge, position);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetPosition(void* node, long edge)
    {
        return YogaInterop.YGNodeStyleGetPosition((IntPtr)node, edge);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMargin(void* node, long edge, float margin)
    {
        YogaInterop.YGNodeStyleSetMargin((IntPtr)node, edge, margin);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMarginPercent(void* node, long edge, float margin)
    {
        YogaInterop.YGNodeStyleSetMarginPercent((IntPtr)node, edge, margin);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMarginAuto(void* node, long edge)
    {
        YogaInterop.YGNodeStyleSetMarginAuto((IntPtr)node, edge);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetMargin(void* node, long edge)
    {
        return YogaInterop.YGNodeStyleGetMargin((IntPtr)node, edge);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetPadding(void* node, long edge, float padding)
    {
        YogaInterop.YGNodeStyleSetPadding((IntPtr)node, edge, padding);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetPaddingPercent(void* node, long edge, float padding)
    {
        YogaInterop.YGNodeStyleSetPaddingPercent((IntPtr)node, edge, padding);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetPadding(void* node, long edge)
    {
        return YogaInterop.YGNodeStyleGetPadding((IntPtr)node, edge);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetBorder(void* node, long edge, float border)
    {
        YogaInterop.YGNodeStyleSetBorder((IntPtr)node, edge, border);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeStyleGetBorder(void* node, long edge)
    {
        return YogaInterop.YGNodeStyleGetBorder((IntPtr)node, edge);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetGap(void* node, long gutter, float gapLength)
    {
        YogaInterop.YGNodeStyleSetGap((IntPtr)node, gutter, gapLength);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeStyleGetGap(void* node, long gutter)
    {
        return YogaInterop.YGNodeStyleGetGap((IntPtr)node, gutter);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetWidth(void* node, float width)
    {
        if (node == null) return null;
        YogaInterop.YGNodeStyleSetWidth((IntPtr)node, width);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetWidthPercent(void* node, float width)
    {
        if (node == null) return null;
        YogaInterop.YGNodeStyleSetWidthPercent((IntPtr)node, width);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetWidthAuto(void* node)
    {
        if (node == null) return null;
        YogaInterop.YGNodeStyleSetWidthAuto((IntPtr)node);
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetWidth(void* node)
    {
        if (node == null) return default;
        return YogaInterop.YGNodeStyleGetWidth((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetHeight(void* node, float height)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetHeight((IntPtr)node, height);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetHeightPercent(void* node, float height)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetHeightPercent((IntPtr)node, height);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetHeightAuto(void* node)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetHeightAuto((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetHeight(void* node)
    {
        if (node == null) return default;
        return YogaInterop.YGNodeStyleGetHeight((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMinWidth(void* node, float minWidth)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetMinWidth((IntPtr)node, minWidth);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMinWidthPercent(void* node, float minWidth)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetMinWidthPercent((IntPtr)node, minWidth);
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetMinWidth(void* node)
    {
        if (node == null) return default;
        return YogaInterop.YGNodeStyleGetMinWidth((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMinHeight(void* node, float minHeight)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetMinHeight((IntPtr)node, minHeight);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMinHeightPercent(void* node, float minHeight)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetMinHeightPercent((IntPtr)node, minHeight);
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetMinHeight(void* node)
    {
        if (node == null) return default;
        return YogaInterop.YGNodeStyleGetMinHeight((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMaxWidth(void* node, float maxWidth)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetMaxWidth((IntPtr)node, maxWidth);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMaxWidthPercent(void* node, float maxWidth)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetMaxWidthPercent((IntPtr)node, maxWidth);
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetMaxWidth(void* node)
    {
        if (node == null) return default;
        return YogaInterop.YGNodeStyleGetMaxWidth((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMaxHeight(void* node, float maxHeight)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetMaxHeight((IntPtr)node, maxHeight);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetMaxHeightPercent(void* node, float maxHeight)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetMaxHeightPercent((IntPtr)node, maxHeight);
    }
    
    [UnmanagedCallersOnly]
    public static YGValue globalYoga_YGNodeStyleGetMaxHeight(void* node)
    {
        if (node == null) return default;
        return YogaInterop.YGNodeStyleGetMaxHeight((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeStyleSetAspectRatio(void* node, float aspectRatio)
    {
        if (node == null) return null;
        return (void*)YogaInterop.YGNodeStyleSetAspectRatio((IntPtr)node, aspectRatio);
    }
    
    [UnmanagedCallersOnly]
    public static float globalYoga_YGNodeStyleGetAspectRatio(void* node)
    {
        if (node == null) return 0.0f;
        return YogaInterop.YGNodeStyleGetAspectRatio((IntPtr)node);
    }
    
    [UnmanagedCallersOnly]
    public static void* globalYoga_YGNodeSetMeasureFunc(void* node, void* measureFunc)
    {
        // Note: Requires special handling for function pointers - simplified for now
        if (node == null) return null;
        return null;
    }
    
    [UnmanagedCallersOnly]
    public static int globalYoga_YGNodeHasMeasureFunc(void* node)
    {
        if (node == null) return 0;
        return YogaInterop.YGNodeHasMeasureFunc((IntPtr)node);
    }

    // DSP Preset Functions sont maintenant dans Audio.DspPreset.cs
    // Audio Mix Buffer Functions sont maintenant dans Audio.AudioMixBuffer.cs
    // Audio Mix Device Buffers Functions sont maintenant dans Audio.AudioMixDeviceBuffers.cs
    
    // RenderTools Functions

    [UnmanagedCallersOnly(EntryPoint = "Debug_Error")]
    public static void DebugError(IntPtr message)
    {
        string? msg = Marshal.PtrToStringUTF8(message);
        Console.WriteLine($"[NativeAOT ERROR] {msg}");
    }

    // CMtrlSystm2ppSys functions have been migrated to Material/MaterialSystem.cs

    [UnmanagedCallersOnly]
    public static void* g_pngnSrvcMgr_GetEngineSwapChain()
    {
        Console.WriteLine("[NativeAOT] g_pngnSrvcMgr_GetEngineSwapChain");
        // Return window handle as SwapChain
        var windowHandle = Platform.PlatformFunctions.GetWindowHandle();
        return windowHandle != null ? (void*)windowHandle : null;
    }

    [UnmanagedCallersOnly]
    public static void* FindOrCreateTexture2(void* pResourceName, int bIsAnonymous, void* pDescriptor, void* data, int dataSize)
    {
        var desc = (TextureCreationConfig_t*)pDescriptor;

        if (desc->m_nWidth <= 0)
            desc->m_nWidth = 1;

        if (desc->m_nHeight <= 0)
            desc->m_nHeight = 1;

        if (desc->m_nDepth <= 0)
            desc->m_nDepth = 1;

        if (desc->m_nNumMipLevels <= 0)
            desc->m_nNumMipLevels = 1;

        if (desc->m_nImageFormat == ImageFormat.None)
            desc->m_nImageFormat = ImageFormat.RGBA8888;

        desc->m_nDisplayRectWidth = desc->m_nWidth;
        desc->m_nDisplayRectHeight = desc->m_nHeight;

        uint tex = 0;
        
        // Utiliser le GL de PlatformFunctions (partagé)
        var gl = Platform.PlatformFunctions.GetGL();
        
        // Si OpenGL est disponible, créer la texture OpenGL réelle
        if (gl != null)
        {
            gl.GenTextures(1, out tex);
            gl.BindTexture(GLEnum.Texture2D, tex);

            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMinFilter, (int)GLEnum.Linear);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureMagFilter, (int)GLEnum.Linear);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapS, (int)GLEnum.ClampToEdge);
            gl.TexParameter(GLEnum.Texture2D, GLEnum.TextureWrapT, (int)GLEnum.ClampToEdge);

            gl.TexImage2D(
                GLEnum.Texture2D,
                0,
                (int)GLEnum.Rgba8,
                (uint)desc->m_nWidth,
                (uint)desc->m_nHeight,
                0,
                GLEnum.Rgba,
                GLEnum.UnsignedByte,
                data
            );
        }
        else
        {
            // Si OpenGL n'est pas encore initialisé, créer une texture "virtuelle"
            // Le handle OpenGL sera créé plus tard quand OpenGL sera disponible
            Console.WriteLine("[NativeAOT] FindOrCreateTexture2: OpenGL not initialized yet, creating virtual texture");
        }
        
        // Créer une entrée dans TextureSystem pour que CTextureBase_IsStrongHandleValid fonctionne
        // Même si OpenGL n'est pas encore initialisé, on crée quand même l'entrée
        string? resourceName = pResourceName != null ? Marshal.PtrToStringUTF8((IntPtr)pResourceName) : null;
        IntPtr textureHandle = Texture.TextureSystem.CreateTextureWithOpenGLHandle(resourceName ?? "", tex);
        
        if (textureHandle == IntPtr.Zero)
        {
            Console.WriteLine("[NativeAOT] FindOrCreateTexture2: Failed to create texture handle");
            return null;
        }
        
        return (void*)textureHandle;
    }

    // Platform functions have been migrated to Platform/PlatformFunctions.cs

    /// <summary>
    /// Internal helper that can be called directly to create a GLFW window.
    /// Note: Cette fonction n'est plus utilisée car la création de fenêtre est gérée par PlatformFunctions.SourceEngineInit
    /// et MaterialSystem.CMtrlSystm2ppSys_CreateAppWindow.
    /// </summary>
    private static unsafe void* CreateAppWindowInternal(string title, int w, int h)
    {
        // Utiliser la fenêtre existante de PlatformFunctions
        var windowHandle = Platform.PlatformFunctions.GetWindowHandle();
        if (windowHandle != null)
        {
            return (void*)windowHandle;
        }
        
        // Si aucune fenêtre n'existe, retourner null (normalement ne devrait pas arriver)
        Console.WriteLine("[NativeAOT] CreateAppWindowInternal: No window available, should be created by PlatformFunctions.SourceEngineInit");
        return null;
    }
    
    /// <summary>
    /// Helper pour CMtrlSystm2ppSys_CreateAppWindow (9 paramètres au lieu de 8).
    /// Cette fonction n'est PAS [UnmanagedCallersOnly] pour pouvoir être appelée depuis MaterialSystem.
    /// </summary>
    public static IntPtr CreateAppWindowHelper(IntPtr self, IntPtr pTitle, int x, int y, int w, int h, int flags, int parentWindow, IntPtr icon)
    {
        // Ignore icon et parentWindow, utilise flags comme nPlatWindowFlags
        string title = Marshal.PtrToStringUTF8(pTitle) ?? "S&box NativeAOT";
        unsafe
        {
            void* result = CreateAppWindowInternal(title, w, h);
            return (IntPtr)result;
        }
    }

    [UnmanagedCallersOnly]
    public static void* GetEngineSwapChainSize( void* w, void* h )
    {
        var windowHandle = Platform.PlatformFunctions.GetWindowHandle();
        var glfw = Platform.PlatformFunctions.GetGlfw();
        
        if (windowHandle == null || glfw == null)
            return null;

        int width, height;
        glfw.GetFramebufferSize(windowHandle, out width, out height);

        *(int*)w = width;
        *(int*)h = height;

        return null;
    }

    // SourceEngineInit has been migrated to Platform/PlatformFunctions.cs
    // Note: SourceEngineFrame remains here because it has complex rendering logic

    // [UnmanagedCallersOnly]
    // public static int SourceEngineFrame(IntPtr appDict, double currentTime, double previousTime)
    // {
    //     var windowHandle = Platform.PlatformFunctions.GetWindowHandle();
    //     var glfw = Platform.PlatformFunctions.GetGlfw();
    //     var gl = Platform.PlatformFunctions.GetGL();
        
    //     if (windowHandle == null || glfw == null || gl == null) return 0;

    //     glfw.PollEvents();
        
    //     if (glfw.WindowShouldClose(windowHandle)) return 0;

    //     // Initialize rendering stubs if not already done
    //     if (_renderContext == null && gl != null)
    //     {
    //         _renderContext = new EmulatedRenderContext(gl);
    //         _sceneView = new EmulatedSceneView();
    //         _sceneLayer = new EmulatedSceneLayer();
    //     }

    //     // Clear the screen
    //     if (gl != null)
    //     {
    //         gl.ClearColor(0.1f, 0.1f, 0.1f, 1.0f);
    //         gl.Clear((uint)ClearBufferMask.ColorBufferBit | (uint)ClearBufferMask.DepthBufferBit);
    //     }

    //     // Render UI if stubs are ready
    //     if (_renderContext != null && _sceneView != null && _sceneLayer != null)
    //     {
    //         try
    //         {
    //             // Create ManagedRenderSetup_t with our emulated stubs
    //             var setup = new ManagedRenderSetup_t
    //             {
    //                 renderContext = new NativeEngine.IRenderContext(_renderContext.Self),
    //                 sceneView = new NativeEngine.ISceneView(_sceneView.Self),
    //                 sceneLayer = new NativeEngine.ISceneLayer(_sceneLayer.Self),
    //                 colorImageFormat = Sandbox.ImageFormat.RGBA8888,
    //                 msaaLevel = NativeEngine.RenderMultisampleType.RENDER_MULTISAMPLE_NONE,
    //                 stats = default(global::SceneSystemPerFrameStats_t)
    //             };

    //             // Call Graphics.OnLayer(-1, ...) to render UI
    //             // Note: Graphics.OnLayer est dans le namespace Sandbox, pas Sandbox.Engine
    //             Sandbox.Graphics.OnLayer(-1, setup);
    //         }
    //         catch (Exception ex)
    //         {
    //             // Log error but don't crash - rendering is optional for now
    //             Console.WriteLine($"[NativeAOT] Error in Graphics.OnLayer: {ex}");
    //         }
    //     }
        
    //     glfw.SwapBuffers(windowHandle);
        
    //     return 1;
    // }

    // UpdateWindowSize has been migrated to Platform/PlatformFunctions.cs

    // Physics functions have been migrated to Physics.PhysicsSystem and Physics.PhysicsWorld

    // IPhysicsBody Stubs
    [UnmanagedCallersOnly]
    public static Vector3 IPhysicsBody_GetPosition(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        return body?.GetPosition() ?? Vector3.Zero;
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetPosition(void* bodyPtr, Vector3 pos)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetPosition(pos);
    }

    [UnmanagedCallersOnly]
    public static System.Numerics.Quaternion IPhysicsBody_GetOrientation(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        return body != null ? body.GetOrientation() : new System.Numerics.Quaternion(0,0,0,1);
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetOrientation(void* bodyPtr, System.Numerics.Quaternion orientation)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetOrientation(orientation);
    }

    [UnmanagedCallersOnly]
    public static Vector3 IPhysicsBody_GetLinearVelocity(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        return body?.GetLinearVelocity() ?? Vector3.Zero;
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetLinearVelocity(void* bodyPtr, Vector3 velocity)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetLinearVelocity(velocity);
    }

    [UnmanagedCallersOnly]
    public static Vector3 IPhysicsBody_GetAngularVelocity(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        return body?.GetAngularVelocity() ?? Vector3.Zero;
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetAngularVelocity(void* bodyPtr, Vector3 velocity)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetAngularVelocity(velocity);
    }

    [UnmanagedCallersOnly]
    public static float IPhysicsBody_GetMass(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        return body?.GetMass() ?? 0f;
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_SetMass(void* bodyPtr, float mass)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        body?.SetMass(mass);
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_Enable(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        body?.Enable();
    }

    [UnmanagedCallersOnly]
    public static void IPhysicsBody_Disable(void* bodyPtr)
    {
        int handle = (int)(long)bodyPtr;
        var body = Common.HandleManager.Get<BepuPhysicsBody>(handle);
        body?.Disable();
    }

    // Physics_AddBody and Physics_Step have been migrated to Physics.PhysicsWorld

    // File System Functions - Critical for package loading and finding compiled DLLs
    [UnmanagedCallersOnly]
    public static void* g_pFllFlSystm_ResetProjectPaths(int includeCloudAssets)
    {
        Console.WriteLine($"[NativeAOT] g_pFllFlSystm_ResetProjectPaths(includeCloudAssets={includeCloudAssets})");
        // Stub implementation - the virtual filesystem already manages paths
        // This is called to reset project paths in the native filesystem
        return null;
    }

    [UnmanagedCallersOnly]
    public static void* g_pFllFlSystm_AddProjectPath(void* ident, void* fullPath)
    {
        string identStr = Marshal.PtrToStringUTF8((IntPtr)ident) ?? "";
        string pathStr = Marshal.PtrToStringUTF8((IntPtr)fullPath) ?? "";
        Console.WriteLine($"[NativeAOT] g_pFllFlSystm_AddProjectPath(ident={identStr}, path={pathStr})");
        // Stub implementation - the virtual filesystem already manages paths
        // This is critical for the native engine to find compiled DLLs in /.bin/ directories
        // Without this, PackageLoader cannot find assemblies
        return null;
    }

    [UnmanagedCallersOnly]
    public static void* g_pFllFlSystm_AddCloudPath(void* ident, void* fullPath)
    {
        string identStr = Marshal.PtrToStringUTF8((IntPtr)ident) ?? "";
        string pathStr = Marshal.PtrToStringUTF8((IntPtr)fullPath) ?? "";
        Console.WriteLine($"[NativeAOT] g_pFllFlSystm_AddCloudPath(ident={identStr}, path={pathStr})");
        // Stub implementation for cloud paths
        return null;
    }

    // Resource System Functions ont été migrées vers Resource/ResourceSystem.cs
}

// Audio helper classes and handle manager
// AudioMixBuffer et AudioMixDeviceBuffers sont maintenant dans Audio.AudioMixBuffer.cs et Audio.AudioMixDeviceBuffers.cs

