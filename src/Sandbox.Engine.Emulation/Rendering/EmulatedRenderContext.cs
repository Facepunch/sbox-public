using System;
using System.Runtime.InteropServices;
using System.Numerics;
using Silk.NET.OpenGL;
using Sandbox;
using Sandbox.Engine.Emulation.Common;
using Sandbox.Engine.Emulation.Video;
using NativeEngine;
using Sandbox.Engine.Emulation.Rendering;

namespace Sandbox.Engine.Emulation.Rendering;

/// <summary>
/// Emulated IRenderContext that uses Silk.NET OpenGL for rendering.
/// This class wraps an IntPtr and implements the methods expected by IRenderContext.
/// </summary>
internal unsafe class EmulatedRenderContext
{
    private const int DefaultVertexStride = 80; // align stride with RenderTools fallback
    private readonly GL _gl;
    private IntPtr _selfPtr;
    private GCHandle _gcHandle;
    private NativeEngine.RenderViewport _currentViewport;
    private IntPtr _renderAttributesPtr;
    private bool _isDisposed;
    
    // Buffer management for dynamic rendering
    private uint _tempVBO;
    private uint _tempEBO;
    private uint _tempVAO;
    private bool _buffersInitialized;
    
    // Basic shader program for UI rendering
    private uint _basicShaderProgram;
    private bool _shaderInitialized;

    public EmulatedRenderContext(GL gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));
        Sandbox.Engine.Emulation.Video.VideoPlayer.SetSharedGL(_gl);
        
        // Allocate memory for CRenderAttributes
        // CRenderAttributes is just an IntPtr, so we allocate a small buffer
        _renderAttributesPtr = Marshal.AllocHGlobal(sizeof(IntPtr));
        Marshal.WriteIntPtr(_renderAttributesPtr, IntPtr.Zero);
        
        // Create a GCHandle to convert this object to IntPtr
        // Use Normal instead of Pinned so we can use FromIntPtr later
        _gcHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        _selfPtr = GCHandle.ToIntPtr(_gcHandle);
        
        // Register this instance in the static mapping
        RegisterInstance(_selfPtr, this);
        
        // Initialize viewport to default
        _currentViewport = new NativeEngine.RenderViewport(0, 0, 1280, 720);
        
        // Initialize buffers lazily when first needed
        _buffersInitialized = false;
        _tempVBO = 0;
        _tempEBO = 0;
        _tempVAO = 0;
        
        // Initialize shader lazily when first needed
        _shaderInitialized = false;
        _basicShaderProgram = 0;
    }
    
    private void EnsureShaderInitialized()
    {
        if (_shaderInitialized) return;
        
        _basicShaderProgram = BasicShaders.CreateShaderProgram(_gl);
        _shaderInitialized = true;
    }
    
    private void EnsureBuffersInitialized()
    {
        if (_buffersInitialized) return;
        
        // Generate VAO
        _tempVAO = _gl.GenVertexArray();
        _gl.BindVertexArray(_tempVAO);
        
        // Generate VBO and EBO
        _tempVBO = _gl.GenBuffer();
        _tempEBO = _gl.GenBuffer();
        
        _buffersInitialized = true;
    }
    
    public unsafe void UploadVertexData(IntPtr data, int dataSize)
    {
        if (_isDisposed) return;
        
        EnsureBuffersInitialized();
        
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _tempVBO);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)dataSize, (void*)data, BufferUsageARB.DynamicDraw);
    }
    
    public unsafe void UploadIndexData(IntPtr data, int dataSize)
    {
        if (_isDisposed) return;
        
        EnsureBuffersInitialized();
        
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _tempEBO);
        _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)dataSize, (void*)data, BufferUsageARB.DynamicDraw);
    }
    
    public void SetupVertexLayout(NativeEngine.VertexLayout layout)
    {
        if (_isDisposed) return;
        
        EnsureBuffersInitialized();
        EnsureShaderInitialized();
        
        _gl.BindVertexArray(_tempVAO);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _tempVBO);
        
        // Use the basic shader program
        if (_basicShaderProgram != 0)
        {
            _gl.UseProgram(_basicShaderProgram);
        }
        
        int stride = DefaultVertexStride;
        if (layout.self != IntPtr.Zero && VertexLayoutInterop.TryGetLayout(layout.self, out var layoutData) && layoutData != null && layoutData.Size > 0)
        {
            stride = layoutData.Size;
        }

        // Layout supposée alignée sur un stride fixe (configurée ci-dessus)
        // Position (vec3) à l’offset 0
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, (GLEnum)VertexAttribPointerType.Float, false, (uint)stride, (void*)0);
        
        // Color (vec4 as Color32) à l’offset 12 - normalisé [0,1]
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 4, (GLEnum)VertexAttribPointerType.UnsignedByte, true, (uint)stride, (void*)12);
        
        // Normal (vec3) à l’offset 16
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 3, (GLEnum)VertexAttribPointerType.Float, false, (uint)stride, (void*)16);
        
        // TexCoord0 (vec4) à l’offset 28
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 4, (GLEnum)VertexAttribPointerType.Float, false, (uint)stride, (void*)28);
        
        // TexCoord1 (vec4) à l’offset 44
        _gl.EnableVertexAttribArray(4);
        _gl.VertexAttribPointer(4, 4, (GLEnum)VertexAttribPointerType.Float, false, (uint)stride, (void*)44);
        
        // Tangent (vec4) à l’offset 60
        _gl.EnableVertexAttribArray(5);
        _gl.VertexAttribPointer(5, 4, (GLEnum)VertexAttribPointerType.Float, false, (uint)stride, (void*)60);
    }
    
    // Static mapping for IntPtr -> EmulatedRenderContext
    private static readonly System.Collections.Generic.Dictionary<IntPtr, EmulatedRenderContext> _instances = new();
    
    private static void RegisterInstance(IntPtr ptr, EmulatedRenderContext instance)
    {
        lock (_instances)
        {
            _instances[ptr] = instance;
        }
    }
    
    public static EmulatedRenderContext? GetInstance(IntPtr ptr)
    {
        lock (_instances)
        {
            _instances.TryGetValue(ptr, out var instance);
            return instance;
        }
    }

    public IntPtr Self => _selfPtr;

    // IRenderContext methods based on Interop.Engine.cs

    public unsafe void SetViewport(NativeRect rect)
    {
        _gl.Viewport(rect.x, rect.y, (uint)rect.w, (uint)rect.h);
    }

    public void SetViewport(int x, int y, int w, int h)
    {
        _gl.Viewport(x, y, (uint)w, (uint)h);
    }

    public unsafe void SetViewport(NativeEngine.RenderViewport viewport)
    {
        _currentViewport = viewport;
        var rect = viewport.Rect;
        _gl.Viewport((int)rect.Left, (int)rect.Top, (uint)rect.Width, (uint)rect.Height);
    }

    public NativeEngine.RenderViewport GetViewport()
    {
        return _currentViewport;
    }

    public unsafe void SetScissorRect(NativeRect rect)
    {
        _gl.Scissor(rect.x, rect.y, (uint)rect.w, (uint)rect.h);
    }

    public NativeEngine.CRenderAttributes GetAttributesPtrForModify()
    {
        return new NativeEngine.CRenderAttributes(_renderAttributesPtr);
    }

    public void Draw(NativeEngine.RenderPrimitiveType type, int nFirstVertex, int nVertexCount)
    {
        if (_isDisposed) return;
        
        // Ensure VAO is bound
        if (_buffersInitialized)
        {
            _gl.BindVertexArray(_tempVAO);
        }
        
        // Convert RenderPrimitiveType to OpenGL primitive type
        var glType = ConvertPrimitiveType(type);
        
        // Draw using OpenGL
        _gl.DrawArrays(glType, nFirstVertex, (uint)nVertexCount);
    }

    public void DrawIndexed(NativeEngine.RenderPrimitiveType type, int nFirstIndex, int nIndexCount, int nMaxVertexCount, int nBaseVertex)
    {
        if (_isDisposed) return;
        
        // Ensure VAO is bound
        if (_buffersInitialized)
        {
            _gl.BindVertexArray(_tempVAO);
            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _tempEBO);
        }
        
        // Convert RenderPrimitiveType to OpenGL primitive type
        var glType = ConvertPrimitiveType(type);
        
        // Draw indexed using OpenGL
        // nFirstIndex is in bytes, convert to element offset
        unsafe
        {
            int elementOffset = nFirstIndex / sizeof(ushort);
            _gl.DrawElements(glType, (uint)nIndexCount, DrawElementsType.UnsignedShort, (void*)(elementOffset * sizeof(ushort)));
        }
    }

    public void DrawInstanced(NativeEngine.RenderPrimitiveType type, int nFirstVertex, int nVertexCountPerInstance, int nInstanceCount)
    {
        if (_isDisposed) return;
        EnsureShaderInitialized();
        EnsureBuffersInitialized();
        var glType = ConvertPrimitiveType(type);
        _gl.BindVertexArray(_tempVAO);
        _gl.DrawArraysInstanced((GLEnum)glType, nFirstVertex, (uint)nVertexCountPerInstance, (uint)nInstanceCount);
    }

    public void DrawIndexedInstanced(NativeEngine.RenderPrimitiveType type, int nFirstIndex, int nIndexCountPerInstance, int nInstanceCount, int nMaxVertexCount, int nBaseVertex)
    {
        if (_isDisposed) return;
        EnsureShaderInitialized();
        EnsureBuffersInitialized();
        var glType = ConvertPrimitiveType(type);
        _gl.BindVertexArray(_tempVAO);
        int elementOffset = nFirstIndex / sizeof(ushort);
        _gl.DrawElementsInstanced((GLEnum)glType, (uint)nIndexCountPerInstance, (GLEnum)DrawElementsType.UnsignedShort, (void*)(elementOffset * sizeof(ushort)), (uint)nInstanceCount);
    }

    public unsafe void Clear(System.Numerics.Vector4 col, bool clearColor, bool clearDepth, bool clearStencil)
    {
        if (_isDisposed) return;
        
        uint clearMask = 0;
        if (clearColor)
        {
            _gl.ClearColor(col.X, col.Y, col.Z, col.W);
            clearMask |= (uint)ClearBufferMask.ColorBufferBit;
        }
        if (clearDepth)
        {
            _gl.ClearDepth(1.0);
            clearMask |= (uint)ClearBufferMask.DepthBufferBit;
        }
        if (clearStencil)
        {
            _gl.ClearStencil(0);
            clearMask |= (uint)ClearBufferMask.StencilBufferBit;
        }
        
        if (clearMask != 0)
        {
            _gl.Clear(clearMask);
        }
    }

    public void Submit()
    {
        // OpenGL doesn't have explicit submit - rendering happens immediately
        // This is a no-op for OpenGL
    }

    public bool BindVertexBuffer(int nSlot, NativeEngine.VertexBufferHandle_t hVertexBuffer, int nOffset)
    {
        return BindVertexBuffer(nSlot, hVertexBuffer, nOffset, DefaultVertexStride);
    }

    public bool BindVertexBuffer(int nSlot, NativeEngine.VertexBufferHandle_t hVertexBuffer, int nOffset, int nStride)
    {
        if (_isDisposed) return false;
        EnsureBuffersInitialized();
        int handle = (int)hVertexBuffer.self;
        var bufferData = Common.HandleManager.Get<RenderDevice.BufferData>(handle);
        if (bufferData == null || bufferData.OpenGLBufferHandle == 0)
        return false;
        
        _gl.BindVertexArray(_tempVAO);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, bufferData.OpenGLBufferHandle);
        
        // Basic layout: position/color/normal/uv/tangent with stride param
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, (uint)nStride, (void*)(nOffset + 0));
        
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, (uint)nStride, (void*)(nOffset + 12));
        
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, (uint)nStride, (void*)(nOffset + 16));
        
        _gl.EnableVertexAttribArray(3);
        _gl.VertexAttribPointer(3, 4, VertexAttribPointerType.Float, false, (uint)nStride, (void*)(nOffset + 28));
        
        _gl.EnableVertexAttribArray(4);
        _gl.VertexAttribPointer(4, 4, VertexAttribPointerType.Float, false, (uint)nStride, (void*)(nOffset + 44));
        
        return true;
    }

    public bool BindIndexBuffer(NativeEngine.IndexBufferHandle_t hIndexBuffer, int nOffset)
    {
        if (_isDisposed) return false;
        EnsureBuffersInitialized();
        int handle = (int)hIndexBuffer.self;
        var bufferData = Common.HandleManager.Get<RenderDevice.BufferData>(handle);
        if (bufferData == null || bufferData.OpenGLBufferHandle == 0)
        return false;
        
        _gl.BindVertexArray(_tempVAO);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, bufferData.OpenGLBufferHandle);
        return true;
    }

    public void BindVertexShader(NativeEngine.RenderShaderHandle_t hVertexShader, NativeEngine.VertexBufferHandle_t hInputLayout)
    {
        // For now, use our basic shader program
        EnsureShaderInitialized();
        if (_basicShaderProgram != 0)
        {
            _gl.UseProgram(_basicShaderProgram);
        }
    }

    public void BindPixelShader(NativeEngine.RenderShaderHandle_t hShader)
    {
        // For now, use our basic shader program (already set in BindVertexShader)
        EnsureShaderInitialized();
        if (_basicShaderProgram != 0)
        {
            _gl.UseProgram(_basicShaderProgram);
        }
    }

    public void BindTexture(int nTextureIndex, NativeEngine.ITexture hTexture)
    {
        if (_isDisposed) return;
        var gl = _gl;
        var texData = Texture.TextureSystem.GetTextureData(hTexture.self);
        if (texData == null || texData.OpenGLHandle == 0) return;
        gl.ActiveTexture(TextureUnit.Texture0 + nTextureIndex);
        gl.BindTexture(GLEnum.Texture2D, texData.OpenGLHandle);
    }

    public void BindRenderTargets(NativeEngine.ITexture colorTexture, NativeEngine.ITexture depthTexture, NativeEngine.ISceneLayer layer)
    {
        // Minimal placeholder: bind default framebuffer
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void BindRenderTargets(IntPtr swapChain, bool color, bool depth)
    {
        // Minimal placeholder: bind default framebuffer
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void RestoreRenderTargets(NativeEngine.ISceneLayer layer)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void GenerateMipMaps(NativeEngine.ITexture material)
    {
        var texData = Texture.TextureSystem.GetTextureData(material.self);
        if (texData == null || texData.OpenGLHandle == 0) return;
        _gl.BindTexture(GLEnum.Texture2D, texData.OpenGLHandle);
        _gl.GenerateMipmap(GLEnum.Texture2D);
    }

    public void BeginPixEvent(string name)
    {
        // Debug marker - no-op for now
    }

    public void EndPixEvent()
    {
        // Debug marker - no-op for now
    }

    public void PixSetMarker(string name)
    {
        // Debug marker - no-op for now
    }

    private static PrimitiveType ConvertPrimitiveType(NativeEngine.RenderPrimitiveType type)
    {
        return type switch
        {
            NativeEngine.RenderPrimitiveType.RENDER_PRIM_POINTS => PrimitiveType.Points,
            NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINES => PrimitiveType.Lines,
            NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINE_STRIP => PrimitiveType.LineStrip,
            NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLES => PrimitiveType.Triangles,
            NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLE_STRIP => PrimitiveType.TriangleStrip,
            _ => PrimitiveType.Triangles
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        // Clean up OpenGL buffers
        if (_buffersInitialized)
        {
            if (_tempVAO != 0)
            {
                _gl.DeleteVertexArray(_tempVAO);
                _tempVAO = 0;
            }
            if (_tempVBO != 0)
            {
                _gl.DeleteBuffer(_tempVBO);
                _tempVBO = 0;
            }
            if (_tempEBO != 0)
            {
                _gl.DeleteBuffer(_tempEBO);
                _tempEBO = 0;
            }
            _buffersInitialized = false;
        }
        
        // Clean up shader
        if (_shaderInitialized && _basicShaderProgram != 0)
        {
            _gl.DeleteProgram(_basicShaderProgram);
            _basicShaderProgram = 0;
            _shaderInitialized = false;
        }
        
        // Unregister from static mapping
        lock (_instances)
        {
            _instances.Remove(_selfPtr);
        }
        
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

