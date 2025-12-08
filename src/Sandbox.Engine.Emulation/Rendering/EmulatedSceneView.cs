using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NativeEngine;
using Sandbox;
using Sandbox.Engine.Emulation.Common;
using Sandbox.Engine.Emulation.RenderAttributes;
using Sandbox.Engine.Emulation.Rendering;
using Sandbox.Engine.Emulation.Platform;
using Sandbox.Rendering;
using Sandbox.Engine.Emulation.Scene;

namespace Sandbox.Engine.Emulation.Rendering;

/// <summary>
/// Emulated implementation of ISceneView exports (indices 2163-2185).
/// </summary>
internal static unsafe class EmulatedSceneView
{
    private class SceneViewData
    {
        public RenderViewport MainViewport = new RenderViewport(0, 0, 1280, 720);
        public IntPtr SwapChain = IntPtr.Zero;
        public IntPtr RenderAttributesPtr = IntPtr.Zero;
        public long DefaultRequiredFlags;
        public long DefaultExcludedFlags;
        public readonly List<IntPtr> RenderLayers = new();
        public readonly List<IntPtr> Worlds = new();
        public readonly List<IntPtr> DependentViews = new();
        public IntPtr Parent = IntPtr.Zero;
        public int Priority;
        public int ViewUniqueId = 1;
        public int ManagedCameraId;
        public bool PostProcessEnabled = true;
        public int ToolsVisMode;
        public IntPtr FrustumPtr = IntPtr.Zero;
    }

    private static readonly Dictionary<IntPtr, SceneViewData> _views = new();

    public static IntPtr CreateView(RenderViewport viewport, int managedCameraId = 1, int priority = 0, IntPtr swapChain = default)
    {
        var data = new SceneViewData
        {
            MainViewport = viewport,
            ManagedCameraId = managedCameraId,
            Priority = priority,
            RenderAttributesPtr = RenderAttributes.RenderAttributes.CreateRenderAttributesInternal(),
            SwapChain = swapChain
        };

        int handle = HandleManager.Register(data);
        lock (_views)
        {
            _views[(IntPtr)handle] = data;
        }
        return (IntPtr)handle;
    }

    public static void SetSwapChainManaged(IntPtr self, IntPtr swapChain)
    {
        var data = GetView(self);
        data.SwapChain = swapChain;
    }

    public static void Init(void** native)
    {
        native[2163] = (void*)(delegate* unmanaged<IntPtr, RenderViewport>)&ISceneView_GetMainViewport;
        native[2164] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneView_GetSwapChain;
        native[2165] = (void*)(delegate* unmanaged<IntPtr, IntPtr, int, void>)&ISceneView_AddDependentView;
        native[2166] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneView_GetRenderAttributesPtr;
        native[2167] = (void*)(delegate* unmanaged<IntPtr, IntPtr, RenderViewport*, StringToken, IntPtr, IntPtr>)&ISceneView_AddRenderLayer;
        native[2168] = (void*)(delegate* unmanaged<IntPtr, IntPtr, RenderViewport*, IntPtr, IntPtr, int, IntPtr>)&ISceneView_AddManagedProceduralLayer;
        native[2169] = (void*)(delegate* unmanaged<IntPtr, long, void>)&ISceneView_SetDefaultLayerObjectRequiredFlags;
        native[2170] = (void*)(delegate* unmanaged<IntPtr, long, void>)&ISceneView_SetDefaultLayerObjectExcludedFlags;
        native[2171] = (void*)(delegate* unmanaged<IntPtr, long>)&ISceneView_GetDefaultLayerObjectRequiredFlags;
        native[2172] = (void*)(delegate* unmanaged<IntPtr, long>)&ISceneView_GetDefaultLayerObjectExcludedFlags;
        native[2173] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISceneView_AddWorldToRenderList;
        native[2174] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int, IntPtr>)&ISceneView_FindOrCreateRenderTarget;
        native[2175] = (void*)(delegate* unmanaged<IntPtr, IntPtr, void>)&ISceneView_SetParent;
        native[2176] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneView_GetParent;
        native[2177] = (void*)(delegate* unmanaged<IntPtr, int>)&ISceneView_GetPriority;
        native[2178] = (void*)(delegate* unmanaged<IntPtr, int, void>)&ISceneView_SetPriority;
        native[2179] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneView_GetFrustum;
        native[2180] = (void*)(delegate* unmanaged<IntPtr, int>)&ISceneView_GetPostProcessEnabled;
        native[2181] = (void*)(delegate* unmanaged<IntPtr, int>)&ISceneView_GetToolsVisMode;
        native[2182] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__ISceneView_m_ViewUniqueId;
        native[2183] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__ISceneView_m_ViewUniqueId;
        native[2184] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__ISceneView_m_ManagedCameraId;
        native[2185] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__ISceneView_m_ManagedCameraId;
    }

    private static SceneViewData GetView(IntPtr self)
    {
        if (self == IntPtr.Zero) self = (IntPtr)(-1); // stable key for fallback
        lock (_views)
        {
            if (!_views.TryGetValue(self, out var data))
            {
                var window = PlatformFunctions.GetWindowHandle();
                data = new SceneViewData
                {
                    RenderAttributesPtr = RenderAttributes.RenderAttributes.CreateRenderAttributesInternal(),
                    SwapChain = window != null ? (IntPtr)window : IntPtr.Zero
                };
                _views[self] = data;
            }
            return data;
        }
    }

    private static IntPtr CreateLayerForView(IntPtr self, IntPtr pszDebugName, RenderViewport* viewport, SceneLayerType layerType = SceneLayerType.Translucent)
    {
        string debugName = pszDebugName != IntPtr.Zero ? Marshal.PtrToStringUTF8(pszDebugName) ?? "RenderLayer" : "RenderLayer";
        var vp = viewport != null ? *viewport : new RenderViewport(0, 0, 1280, 720);
        var handle = EmulatedSceneLayer.CreateLayer(debugName, vp, layerType);
        var data = GetView(self);
        lock (data.RenderLayers)
        {
            data.RenderLayers.Add(handle);
        }
        return handle;
    }

    [UnmanagedCallersOnly]
    public static RenderViewport ISceneView_GetMainViewport(IntPtr self) => GetView(self).MainViewport;

    [UnmanagedCallersOnly]
    public static IntPtr ISceneView_GetSwapChain(IntPtr self) => GetView(self).SwapChain;

    [UnmanagedCallersOnly]
    public static void ISceneView_AddDependentView(IntPtr self, IntPtr pView, int nSlot)
    {
        var data = GetView(self);
        lock (data.DependentViews)
        {
            data.DependentViews.Add(pView);
        }
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneView_GetRenderAttributesPtr(IntPtr self)
    {
        var data = GetView(self);
        if (data.RenderAttributesPtr == IntPtr.Zero)
        {
            data.RenderAttributesPtr = RenderAttributes.RenderAttributes.CreateRenderAttributesInternal();
        }
        return data.RenderAttributesPtr;
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneView_AddRenderLayer(IntPtr self, IntPtr pszDebugName, RenderViewport* viewport, StringToken eShaderMode, IntPtr pAddBefore)
    {
        return CreateLayerForView(self, pszDebugName, viewport, SceneLayerType.Translucent);
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneView_AddManagedProceduralLayer(IntPtr self, IntPtr pszDebugName, RenderViewport* viewport, IntPtr renderCallback, IntPtr pAddBefore, int bDeleteWhenDone)
    {
        // Procedural layer modeled as a normal layer; callback not invoked yet.
        return CreateLayerForView(self, pszDebugName, viewport, SceneLayerType.Translucent);
    }

    [UnmanagedCallersOnly]
    public static void ISceneView_SetDefaultLayerObjectRequiredFlags(IntPtr self, long nFlags)
    {
        GetView(self).DefaultRequiredFlags = nFlags;
    }

    [UnmanagedCallersOnly]
    public static void ISceneView_SetDefaultLayerObjectExcludedFlags(IntPtr self, long nFlags)
    {
        GetView(self).DefaultExcludedFlags = nFlags;
    }

    [UnmanagedCallersOnly]
    public static long ISceneView_GetDefaultLayerObjectRequiredFlags(IntPtr self) => GetView(self).DefaultRequiredFlags;

    [UnmanagedCallersOnly]
    public static long ISceneView_GetDefaultLayerObjectExcludedFlags(IntPtr self) => GetView(self).DefaultExcludedFlags;

    [UnmanagedCallersOnly]
    public static void ISceneView_AddWorldToRenderList(IntPtr self, IntPtr pWorld)
    {
        var data = GetView(self);
        lock (data.Worlds)
        {
            data.Worlds.Add(pWorld);
        }
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneView_FindOrCreateRenderTarget(IntPtr self, IntPtr pName, IntPtr hTexture, int nFlags)
    {
        // Minimal: reuse texture handle if provided, else return zero.
        return hTexture;
    }

    [UnmanagedCallersOnly]
    public static void ISceneView_SetParent(IntPtr self, IntPtr pParent)
    {
        GetView(self).Parent = pParent;
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneView_GetParent(IntPtr self) => GetView(self).Parent;

    [UnmanagedCallersOnly]
    public static int ISceneView_GetPriority(IntPtr self) => GetView(self).Priority;

    [UnmanagedCallersOnly]
    public static void ISceneView_SetPriority(IntPtr self, int nPriority) => GetView(self).Priority = nPriority;

    [UnmanagedCallersOnly]
    public static IntPtr ISceneView_GetFrustum(IntPtr self)
    {
        var data = GetView(self);
        if (data.FrustumPtr == IntPtr.Zero)
        {
            // Provide a stable non-null pointer for callers expecting a struct pointer.
            var frustum = new global::CFrustum { self = IntPtr.Zero };
            var handle = GCHandle.Alloc(frustum, GCHandleType.Pinned);
            data.FrustumPtr = GCHandle.ToIntPtr(handle);
        }
        return data.FrustumPtr;
    }

    [UnmanagedCallersOnly]
    public static int ISceneView_GetPostProcessEnabled(IntPtr self) => GetView(self).PostProcessEnabled ? 1 : 0;

    [UnmanagedCallersOnly]
    public static int ISceneView_GetToolsVisMode(IntPtr self) => GetView(self).ToolsVisMode;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__ISceneView_m_ViewUniqueId(IntPtr self) => GetView(self).ViewUniqueId;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__ISceneView_m_ViewUniqueId(IntPtr self, int value) => GetView(self).ViewUniqueId = value;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__ISceneView_m_ManagedCameraId(IntPtr self) => GetView(self).ManagedCameraId;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__ISceneView_m_ManagedCameraId(IntPtr self, int value) => GetView(self).ManagedCameraId = value;
}
