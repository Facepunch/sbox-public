using System;
using System.Runtime.InteropServices;
using NativeEngine;
using Sandbox;
using Sandbox.Rendering;

namespace Sandbox.Engine.Emulation.Rendering;

/// <summary>
/// Emulated ISceneLayer for UI rendering.
/// This class wraps an IntPtr and implements the methods expected by ISceneLayer.
/// </summary>
internal unsafe class EmulatedSceneLayer
{
    private IntPtr _selfPtr;
    private GCHandle _gcHandle;
    private IntPtr _renderAttributesPtr;
    private SceneLayerType _layerEnum;
    private bool _isDisposed;

    public EmulatedSceneLayer()
    {
        // Allocate memory for CRenderAttributes
        _renderAttributesPtr = Marshal.AllocHGlobal(sizeof(IntPtr));
        Marshal.WriteIntPtr(_renderAttributesPtr, IntPtr.Zero);
        
        // Create a GCHandle to convert this object to IntPtr
        _gcHandle = GCHandle.Alloc(this, GCHandleType.Pinned);
        _selfPtr = GCHandle.ToIntPtr(_gcHandle);
        
        // Set default layer type to Translucent (closest to UI)
        _layerEnum = SceneLayerType.Translucent;
    }

    public IntPtr Self => _selfPtr;

    // ISceneLayer methods based on Interop.Engine.cs

    public SceneLayerType LayerEnum
    {
        get => _layerEnum;
        set => _layerEnum = value;
    }

    public LayerFlags m_nLayerFlags { get; set; } = 0;

    public NativeEngine.CRenderAttributes GetRenderAttributesPtr()
    {
        return new NativeEngine.CRenderAttributes(_renderAttributesPtr);
    }

    public void SetObjectMatchID(StringToken nTok)
    {
        // Stub - no-op
    }

    public void AddObjectFlagsRequiredMask(SceneObjectFlags nRequiredFlags)
    {
        // Stub - no-op
    }

    public void AddObjectFlagsExcludedMask(SceneObjectFlags nExcludedFlags)
    {
        // Stub - no-op
    }

    public void RemoveObjectFlagsRequiredMask(SceneObjectFlags nRequiredFlags)
    {
        // Stub - no-op
    }

    public void RemoveObjectFlagsExcludedMask(SceneObjectFlags nExcludedFlags)
    {
        // Stub - no-op
    }

    public SceneObjectFlags GetObjectFlagsRequiredMask()
    {
        return SceneObjectFlags.None;
    }

    public SceneObjectFlags GetObjectFlagsExcludedMask()
    {
        return SceneObjectFlags.None;
    }

    public string GetDebugName()
    {
        return "EmulatedSceneLayer";
    }

    public void SetAttr(StringToken nTokenID, SceneViewRenderTargetHandle hRenderTarget, SceneLayerMSAAMode_t msaa, uint flags)
    {
        // Stub - no-op
    }

    public void SetBoundingVolumeSizeCullThresholdInPercent(float flSizeCullThreshold)
    {
        // Stub - no-op
    }

    public unsafe void SetClearColor(System.Numerics.Vector4 vecColor, int nRenderTargetIndex)
    {
        // Stub - no-op
    }

    public NativeEngine.ITexture GetTextureValue(StringToken nTokenID, NativeEngine.ITexture nDefaultValue)
    {
        return nDefaultValue;
    }

    public NativeEngine.ITexture GetTextureValue(StringToken nTokenID)
    {
        return new NativeEngine.ITexture { self = IntPtr.Zero };
    }

    public NativeEngine.ITexture GetColorTarget()
    {
        return new NativeEngine.ITexture { self = IntPtr.Zero };
    }

    public NativeEngine.ITexture GetDepthTarget()
    {
        return new NativeEngine.ITexture { self = IntPtr.Zero };
    }

    public void SetOutput(SceneViewRenderTargetHandle hColor, SceneViewRenderTargetHandle hDepth)
    {
        // Stub - no-op
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        if (_renderAttributesPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_renderAttributesPtr);
            _renderAttributesPtr = IntPtr.Zero;
        }
        
        if (_gcHandle.IsAllocated)
        {
            _gcHandle.Free();
        }
        
        _isDisposed = true;
    }
}

