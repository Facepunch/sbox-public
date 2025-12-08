using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using NativeEngine;
using Sandbox;
using Sandbox.Engine.Emulation.Common;
using Sandbox.Engine.Emulation.RenderAttributes;
using Sandbox.Rendering;

namespace Sandbox.Engine.Emulation.Rendering;

/// <summary>
/// Emulated implementation of ISceneLayer exports (indices 2138-2162).
/// Stores state in managed objects and returns handles compatible with NativeEngine.ISceneLayer.
/// </summary>
internal static unsafe class EmulatedSceneLayer
{
    private class LayerData
    {
        public StringToken ObjectMatchId;
        public SceneObjectFlags RequiredMask;
        public SceneObjectFlags ExcludedMask;
        public string DebugName = "EmulatedSceneLayer";
        public IntPtr RenderAttributesPtr;
        public SceneLayerMSAAMode_t MsaaMode = SceneLayerMSAAMode_t.On;
        public uint AttrFlags;
        public LayerFlags LayerFlags;
        public SceneLayerType LayerEnum = SceneLayerType.Translucent;
        public RenderViewport Viewport = new RenderViewport(0, 0, 1280, 720);
        public int ClearFlags;
        public System.Numerics.Vector4 ClearColor;
        public IntPtr ColorTarget;
        public IntPtr DepthTarget;
        public readonly Dictionary<StringToken, IntPtr> TextureValues = new();
        public readonly Dictionary<StringToken, (SceneViewRenderTargetHandle Handle, SceneLayerMSAAMode_t Msaa, uint Flags)> AttrTargets = new();
        public IntPtr DebugNamePtr;
    }

    // Handles created by AddRenderLayer/AddManagedProceduralLayer live in HandleManager.
    // For handles coming from elsewhere, we keep a fallback map to avoid crashes.
    private static readonly Dictionary<IntPtr, LayerData> _fallback = new();

    public static void Init(void** native)
    {
        native[2138] = (void*)(delegate* unmanaged<IntPtr, StringToken, void>)&ISceneLayer_SetObjectMatchID;
        native[2139] = (void*)(delegate* unmanaged<IntPtr, long, void>)&ISceneLayer_AddObjectFlagsRequiredMask;
        native[2140] = (void*)(delegate* unmanaged<IntPtr, long, void>)&ISceneLayer_AddObjectFlagsExcludedMask;
        native[2141] = (void*)(delegate* unmanaged<IntPtr, long, void>)&ISceneLayer_RemoveObjectFlagsRequiredMask;
        native[2142] = (void*)(delegate* unmanaged<IntPtr, long, void>)&ISceneLayer_RemoveObjectFlagsExcludedMask;
        native[2143] = (void*)(delegate* unmanaged<IntPtr, long>)&ISceneLayer_GetObjectFlagsRequiredMask;
        native[2144] = (void*)(delegate* unmanaged<IntPtr, long>)&ISceneLayer_GetObjectFlagsExcludedMask;
        native[2145] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneLayer_GetDebugName;
        native[2146] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneLayer_GetRenderAttributesPtr;
        native[2147] = (void*)(delegate* unmanaged<IntPtr, StringToken, IntPtr, long, uint, void>)&ISceneLayer_SetAttr;
        native[2148] = (void*)(delegate* unmanaged<IntPtr, float, void>)&ISceneLayer_SetBoundingVolumeSizeCullThresholdInPercent;
        native[2149] = (void*)(delegate* unmanaged<IntPtr, Vector4*, int, void>)&ISceneLayer_SetClearColor;
        native[2150] = (void*)(delegate* unmanaged<IntPtr, StringToken, IntPtr, IntPtr>)&ISceneLayer_GetTextureValue;
        native[2151] = (void*)(delegate* unmanaged<IntPtr, StringToken, IntPtr>)&ISceneLayer_GetTextureValue_1;
        native[2152] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneLayer_GetColorTarget;
        native[2153] = (void*)(delegate* unmanaged<IntPtr, IntPtr>)&ISceneLayer_GetDepthTarget;
        native[2154] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, void>)&ISceneLayer_SetOutput;
        native[2155] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, long>)&Get__ISceneLayer_m_nLayerFlags;
        native[2156] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, long, void>)&Set__ISceneLayer_m_nLayerFlags;
        native[2157] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, long>)&Get__ISceneLayer_LayerEnum;
        native[2158] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, long, void>)&Set__ISceneLayer_LayerEnum;
        native[2159] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, RenderViewport>)&Get__ISceneLayer_m_viewport;
        native[2160] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, RenderViewport, void>)&Set__ISceneLayer_m_viewport;
        native[2161] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int>)&Get__ISceneLayer_m_nClearFlags;
        native[2162] = (void*)(delegate* unmanaged[SuppressGCTransition]<IntPtr, int, void>)&Set__ISceneLayer_m_nClearFlags;
    }

    public static IntPtr CreateLayer(string debugName, RenderViewport viewport, SceneLayerType layerType)
    {
        var data = new LayerData
        {
            DebugName = string.IsNullOrWhiteSpace(debugName) ? "SceneLayer" : debugName,
            LayerEnum = layerType,
            Viewport = viewport,
            RenderAttributesPtr = RenderAttributes.RenderAttributes.CreateRenderAttributesInternal(),
            DebugNamePtr = AllocUtf8(debugName ?? "SceneLayer")
        };

        int handle = HandleManager.Register(data);
        return (IntPtr)handle;
    }

    private static IntPtr AllocUtf8(string value)
    {
        value ??= string.Empty;
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        return ptr;
    }

    private static LayerData GetLayerData(IntPtr self)
    {
        if (self == IntPtr.Zero) return EnsureFallback(self);

        var fromHandle = HandleManager.Get<LayerData>((int)self);
        if (fromHandle != null)
        {
            return fromHandle;
        }

        return EnsureFallback(self);
    }

    private static LayerData EnsureFallback(IntPtr self)
    {
        lock (_fallback)
        {
            if (!_fallback.TryGetValue(self, out var data))
            {
                data = new LayerData
                {
                    DebugName = "SceneLayer",
                    RenderAttributesPtr = RenderAttributes.RenderAttributes.CreateRenderAttributesInternal()
                };
                _fallback[self] = data;
            }
            return data;
        }
    }

    /// <summary>
    /// Helper managé pour définir les outputs (color/depth) sans passer par UnmanagedCallersOnly.
    /// </summary>
    public static void SetOutputManaged(IntPtr self, IntPtr hColor, IntPtr hDepth)
    {
        var data = GetLayerData(self);
        data.ColorTarget = hColor;
        data.DepthTarget = hDepth;
    }

    /// <summary>
    /// Helper managé pour mettre à jour le viewport.
    /// </summary>
    public static void SetViewportManaged(IntPtr self, RenderViewport viewport)
    {
        var data = GetLayerData(self);
        data.Viewport = viewport;
    }

    [UnmanagedCallersOnly]
    public static void ISceneLayer_SetObjectMatchID(IntPtr self, StringToken nTok)
    {
        GetLayerData(self).ObjectMatchId = nTok;
    }

    [UnmanagedCallersOnly]
    public static void ISceneLayer_AddObjectFlagsRequiredMask(IntPtr self, long nRequiredFlags)
    {
        var data = GetLayerData(self);
        data.RequiredMask |= (SceneObjectFlags)nRequiredFlags;
    }

    [UnmanagedCallersOnly]
    public static void ISceneLayer_AddObjectFlagsExcludedMask(IntPtr self, long nExcludedFlags)
    {
        var data = GetLayerData(self);
        data.ExcludedMask |= (SceneObjectFlags)nExcludedFlags;
    }

    [UnmanagedCallersOnly]
    public static void ISceneLayer_RemoveObjectFlagsRequiredMask(IntPtr self, long nRequiredFlags)
    {
        var data = GetLayerData(self);
        data.RequiredMask &= ~(SceneObjectFlags)nRequiredFlags;
    }

    [UnmanagedCallersOnly]
    public static void ISceneLayer_RemoveObjectFlagsExcludedMask(IntPtr self, long nExcludedFlags)
    {
        var data = GetLayerData(self);
        data.ExcludedMask &= ~(SceneObjectFlags)nExcludedFlags;
    }

    [UnmanagedCallersOnly]
    public static long ISceneLayer_GetObjectFlagsRequiredMask(IntPtr self)
    {
        return (long)GetLayerData(self).RequiredMask;
    }

    [UnmanagedCallersOnly]
    public static long ISceneLayer_GetObjectFlagsExcludedMask(IntPtr self)
    {
        return (long)GetLayerData(self).ExcludedMask;
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneLayer_GetDebugName(IntPtr self)
    {
        var data = GetLayerData(self);
        if (data.DebugNamePtr == IntPtr.Zero)
        {
            data.DebugNamePtr = AllocUtf8(data.DebugName ?? "SceneLayer");
        }
        return data.DebugNamePtr;
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneLayer_GetRenderAttributesPtr(IntPtr self)
    {
        var data = GetLayerData(self);
        if (data.RenderAttributesPtr == IntPtr.Zero)
        {
            data.RenderAttributesPtr = RenderAttributes.RenderAttributes.CreateRenderAttributesInternal();
        }
        return data.RenderAttributesPtr;
    }

    [UnmanagedCallersOnly]
    public static void ISceneLayer_SetAttr(IntPtr self, StringToken nTokenID, IntPtr hRenderTarget, long msaa, uint flags)
    {
        var data = GetLayerData(self);
        data.AttrTargets[nTokenID] = (new SceneViewRenderTargetHandle { self = hRenderTarget }, (SceneLayerMSAAMode_t)msaa, flags);
    }

    [UnmanagedCallersOnly]
    public static void ISceneLayer_SetBoundingVolumeSizeCullThresholdInPercent(IntPtr self, float flSizeCullThreshold)
    {
        // Not used in emulation yet; keep silent.
    }

    [UnmanagedCallersOnly]
    public static void ISceneLayer_SetClearColor(IntPtr self, Vector4* vecColor, int nRenderTargetIndex)
    {
        if (vecColor == null) return;
        GetLayerData(self).ClearColor = *vecColor;
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneLayer_GetTextureValue(IntPtr self, StringToken nTokenID, IntPtr nDefaultValue)
    {
        var data = GetLayerData(self);
        if (data.TextureValues.TryGetValue(nTokenID, out var val))
        {
            return val;
        }
        return nDefaultValue;
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneLayer_GetTextureValue_1(IntPtr self, StringToken nTokenID)
    {
        var data = GetLayerData(self);
        if (data.TextureValues.TryGetValue(nTokenID, out var val))
        {
            return val;
        }
        return IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneLayer_GetColorTarget(IntPtr self)
    {
        return GetLayerData(self).ColorTarget;
    }

    [UnmanagedCallersOnly]
    public static IntPtr ISceneLayer_GetDepthTarget(IntPtr self)
    {
        return GetLayerData(self).DepthTarget;
    }

    [UnmanagedCallersOnly]
    public static void ISceneLayer_SetOutput(IntPtr self, IntPtr hColor, IntPtr hDepth)
    {
        var data = GetLayerData(self);
        data.ColorTarget = hColor;
        data.DepthTarget = hDepth;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static long Get__ISceneLayer_m_nLayerFlags(IntPtr self) => (long)GetLayerData(self).LayerFlags;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__ISceneLayer_m_nLayerFlags(IntPtr self, long value) => GetLayerData(self).LayerFlags = (LayerFlags)value;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static long Get__ISceneLayer_LayerEnum(IntPtr self) => (long)GetLayerData(self).LayerEnum;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__ISceneLayer_LayerEnum(IntPtr self, long value) => GetLayerData(self).LayerEnum = (SceneLayerType)value;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static RenderViewport Get__ISceneLayer_m_viewport(IntPtr self) => GetLayerData(self).Viewport;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__ISceneLayer_m_viewport(IntPtr self, RenderViewport value) => GetLayerData(self).Viewport = value;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static int Get__ISceneLayer_m_nClearFlags(IntPtr self) => GetLayerData(self).ClearFlags;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvSuppressGCTransition) })]
    public static void Set__ISceneLayer_m_nClearFlags(IntPtr self, int value) => GetLayerData(self).ClearFlags = value;
}
