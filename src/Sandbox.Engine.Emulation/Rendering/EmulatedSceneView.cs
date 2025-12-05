using System;
using System.Runtime.InteropServices;
using NativeEngine;
using Sandbox;
using Sandbox.Rendering;

namespace Sandbox.Engine.Emulation.Rendering;

/// <summary>
/// Emulated ISceneView for UI rendering.
/// This class wraps an IntPtr and implements the methods expected by ISceneView.
/// </summary>
internal unsafe class EmulatedSceneView
{
    private IntPtr _selfPtr;
    private GCHandle _gcHandle;
    private int _managedCameraId;
    private IntPtr _renderAttributesPtr;
    private bool _isDisposed;
    private global::CFrustum _frustum;

    public EmulatedSceneView()
    {
        // Allocate memory for CRenderAttributes
        _renderAttributesPtr = Marshal.AllocHGlobal(sizeof(IntPtr));
        Marshal.WriteIntPtr(_renderAttributesPtr, IntPtr.Zero);
        
        // Create a GCHandle to convert this object to IntPtr
        _gcHandle = GCHandle.Alloc(this, GCHandleType.Pinned);
        _selfPtr = GCHandle.ToIntPtr(_gcHandle);
        
        // Initialize frustum stub (will need proper implementation later)
        _frustum = new global::CFrustum { self = IntPtr.Zero };
    }

    public IntPtr Self => _selfPtr;

    // ISceneView methods based on Interop.Engine.cs

    public unsafe NativeEngine.RenderViewport GetMainViewport()
    {
        // Return default viewport for UI
        return new NativeEngine.RenderViewport(0, 0, 1280, 720);
    }

    public IntPtr GetSwapChain()
    {
        // Return swap chain handle (will be set from EngineExports)
        return IntPtr.Zero;
    }

    public NativeEngine.CRenderAttributes GetRenderAttributesPtr()
    {
        return new NativeEngine.CRenderAttributes(_renderAttributesPtr);
    }

    public global::CFrustum GetFrustum()
    {
        // Return a default frustum for UI rendering
        // For UI, we don't need a real 3D frustum
        return _frustum;
    }

    public int m_ManagedCameraId
    {
        get => _managedCameraId;
        set => _managedCameraId = value;
    }

    public int m_ViewUniqueId { get; set; } = 1;

    public unsafe NativeEngine.ISceneLayer AddRenderLayer(string pszDebugName, NativeEngine.RenderViewport viewport, StringToken eShaderMode, NativeEngine.ISceneLayer pAddBefore)
    {
        // Stub - return empty layer
        return new NativeEngine.ISceneLayer { self = IntPtr.Zero };
    }

    public unsafe NativeEngine.ISceneLayer AddManagedProceduralLayer(string pszDebugName, NativeEngine.RenderViewport viewport, IntPtr renderCallback, NativeEngine.ISceneLayer pAddBefore, bool bDeleteWhenDone)
    {
        // Stub - return empty layer
        return new NativeEngine.ISceneLayer { self = IntPtr.Zero };
    }

    public void AddWorldToRenderList(SceneWorld pWorld)
    {
        // Stub - no-op for UI
    }

    public SceneViewRenderTargetHandle FindOrCreateRenderTarget(string pName, NativeEngine.ITexture hTexture, int nFlags)
    {
        // Stub - return invalid handle (default struct)
        return default(SceneViewRenderTargetHandle);
    }

    public void SetParent(NativeEngine.ISceneView pParent)
    {
        // Stub - no-op
    }

    public NativeEngine.ISceneView GetParent()
    {
        return new NativeEngine.ISceneView { self = IntPtr.Zero };
    }

    public int GetPriority()
    {
        return 0;
    }

    public void SetPriority(int nPriority)
    {
        // Stub - no-op
    }

    public bool GetPostProcessEnabled()
    {
        return false;
    }

    public int GetToolsVisMode()
    {
        return 0;
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

