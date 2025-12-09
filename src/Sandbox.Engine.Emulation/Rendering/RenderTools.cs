using System;
using System.Runtime.InteropServices;
using System.Numerics;
using Sandbox;
using NativeEngine;
using Sandbox.Engine.Emulation.Platform;
using Sandbox.Engine.Emulation.Texture;
using Sandbox.Engine.Emulation.Common;
using Sandbox.Engine.Emulation.Rendering;
using Silk.NET.OpenGL;

namespace Sandbox.Engine.Emulation.Rendering;

/// <summary>
/// Implémentation émulée de RenderTools (interop RenderTools_*).
/// Couvre les exports indices 2371+ (voir engine.Generated.cs / Interop.Engine.cs).
/// Implémentation minimale/no-op pour éviter les NotImplemented côté moteur.
/// </summary>
public static unsafe class RenderTools
{
    // One-time debug log to know if RenderTools.Draw is reached.
    private static bool _loggedDraw = false;
    private static bool _loggedSetRenderState = false;
    private static bool _loggedDrawModel = false;
    private static bool _loggedDrawSceneObject = false;

    // Minimal vertex format matching the default stride (80 bytes) used in EmulatedRenderContext.SetupVertexLayout:
    // pos (vec3) @0, color (RGBA8) @12, normal (vec3) @16, tex0 (vec4) @28, tex1 (vec4) @44, tangent (vec4) @60, pad (float) @76 -> stride 80.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BasicVertex
    {
        public Vector3 Position;   // 12
        public uint Color;         // 4 -> 16
        public Vector3 Normal;     // 12 -> 28
        public Vector4 Tex0;       // 16 -> 44
        public Vector4 Tex1;       // 16 -> 60
        public Vector4 Tangent;    // 16 -> 76
        public float Pad;          // 4 -> 80
    }

    // Static cube (unit cube centered at origin) to render when no real mesh data is available.
    private static readonly BasicVertex[] _fallbackCubeVertices = CreateFallbackCubeVertices();
    private static readonly ushort[] _fallbackCubeIndices =
    {
        0,1,2, 2,1,3,       // -Z
        4,6,5, 5,6,7,       // +Z
        0,2,4, 4,2,6,       // -X
        1,5,3, 3,5,7,       // +X
        0,4,1, 1,4,5,       // -Y
        2,3,6, 6,3,7        // +Y
    };

    private static BasicVertex[] CreateFallbackCubeVertices()
    {
        const float s = 0.5f;
        uint white = 0xFFFFFFFF;
        Vector4 zero4 = default;
        Vector4 tangent = new Vector4(1, 0, 0, 1);

        return new[]
        {
            new BasicVertex { Position = new Vector3(-s, -s, -s), Color = white, Normal = new Vector3(0, 0, -1), Tex0 = zero4, Tex1 = zero4, Tangent = tangent, Pad = 0 },
            new BasicVertex { Position = new Vector3( s, -s, -s), Color = white, Normal = new Vector3(0, 0, -1), Tex0 = zero4, Tex1 = zero4, Tangent = tangent, Pad = 0 },
            new BasicVertex { Position = new Vector3(-s,  s, -s), Color = white, Normal = new Vector3(0, 0, -1), Tex0 = zero4, Tex1 = zero4, Tangent = tangent, Pad = 0 },
            new BasicVertex { Position = new Vector3( s,  s, -s), Color = white, Normal = new Vector3(0, 0, -1), Tex0 = zero4, Tex1 = zero4, Tangent = tangent, Pad = 0 },

            new BasicVertex { Position = new Vector3(-s, -s,  s), Color = white, Normal = new Vector3(0, 0, 1), Tex0 = zero4, Tex1 = zero4, Tangent = tangent, Pad = 0 },
            new BasicVertex { Position = new Vector3( s, -s,  s), Color = white, Normal = new Vector3(0, 0, 1), Tex0 = zero4, Tex1 = zero4, Tangent = tangent, Pad = 0 },
            new BasicVertex { Position = new Vector3(-s,  s,  s), Color = white, Normal = new Vector3(0, 0, 1), Tex0 = zero4, Tex1 = zero4, Tangent = tangent, Pad = 0 },
            new BasicVertex { Position = new Vector3( s,  s,  s), Color = white, Normal = new Vector3(0, 0, 1), Tex0 = zero4, Tex1 = zero4, Tangent = tangent, Pad = 0 },
        };
    }

    public static void Init(void** native)
    {
        // Ordre conforme à engine.Generated.cs
        native[2394] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int>)&RenderTools_SetRenderState;
        native[2395] = (void*)(delegate* unmanaged<IntPtr, long, IntPtr, IntPtr, int, IntPtr, int, IntPtr, void>)&RenderTools_Draw;
        native[2396] = (void*)(delegate* unmanaged<IntPtr, IntPtr, NativeRect*, void>)&RenderTools_ResolveFrameBuffer;
        native[2397] = (void*)(delegate* unmanaged<IntPtr, IntPtr, NativeRect*, void>)&RenderTools_ResolveDepthBuffer;
        native[2398] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, Transform*, System.Numerics.Vector4*, IntPtr, IntPtr, void>)&RenderTools_DrawSceneObject;
        native[2399] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, int, IntPtr, void>)&RenderTools_DrawModel;
        native[2400] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, int, IntPtr, void>)&RenderTools_DrawModel_1;
        native[2401] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, int, int, int, void>)&RenderTools_Compute;
        native[2402] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, uint, void>)&RenderTools_ComputeIndirect;
        native[2403] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, uint, uint, uint, void>)&RenderTools_TraceRays;
        native[2404] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, uint, void>)&RenderTools_TraceRaysIndirect;
        native[2405] = (void*)(delegate* unmanaged<IntPtr, StringToken, IntPtr, IntPtr, int, void>)&RenderTools_SetDynamicConstantBufferData;
        native[2406] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, NativeRect*, int, int, uint, uint, uint, uint, void>)&RenderTools_CopyTexture;
        native[2407] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, uint, uint, void>)&RenderTools_SetGPUBufferData;
        native[2408] = (void*)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, uint, void>)&RenderTools_CopyGPUBufferHiddenStructureCount;
        native[2409] = (void*)(delegate* unmanaged<IntPtr, IntPtr, uint, void>)&RenderTools_SetGPUBufferHiddenStructureCount;
    }

    [UnmanagedCallersOnly]
    public static int RenderTools_SetRenderState(IntPtr context, IntPtr attributes, IntPtr materialMode, IntPtr layout, IntPtr stats)
    {
        if (context == IntPtr.Zero)
            return 0;

        var renderContext = EmulatedRenderContext.GetInstance(context);
        if (renderContext == null)
        {
            Console.WriteLine("[NativeAOT] RenderTools_SetRenderState: Failed to get EmulatedRenderContext instance");
            return 0;
        }

        try
        {
            if (!_loggedSetRenderState)
            {
                Console.WriteLine($"[NativeAOT] RenderTools_SetRenderState first call: ctx=0x{context.ToInt64():X} attrs=0x{attributes.ToInt64():X} layout=0x{layout.ToInt64():X}");
                _loggedSetRenderState = true;
            }

            // Setup vertex layout if provided; materials/attributes to be wired later.
            if (layout != IntPtr.Zero)
            {
                var vertexLayout = new NativeEngine.VertexLayout { self = layout };
                renderContext.SetupVertexLayout(vertexLayout);
            }

            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] RenderTools_SetRenderState error: {ex}");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_Draw(IntPtr context, long type, IntPtr layout, IntPtr vertices, int numVertices, IntPtr indices, int numIndices, IntPtr stats)
    {
        if (context == IntPtr.Zero)
            return;

        var renderContext = EmulatedRenderContext.GetInstance(context);
        if (renderContext == null)
        {
            Console.WriteLine("[NativeAOT] RenderTools_Draw: Failed to get EmulatedRenderContext instance");
            return;
        }

        try
        {
            if (!_loggedDraw)
            {
                Console.WriteLine($"[NativeAOT] RenderTools_Draw first call: type={type}, numVertices={numVertices}, numIndices={numIndices}, layout=0x{layout.ToInt64():X} verticesPtr=0x{vertices.ToInt64():X}");
                _loggedDraw = true;
            }

            // If the managed side didn't provide CPU vertex data, render a debug quad so we can detect the call.
            if (vertices == IntPtr.Zero || numVertices <= 0)
            {
                renderContext.DrawQuadFallback();
                return;
            }

            // Upload provided vertices/indices if any
            const int fallbackVertexSize = 80;
            int vertexSize = fallbackVertexSize;
            if (layout != IntPtr.Zero && VertexLayoutInterop.TryGetLayout(layout, out var layoutData) && layoutData != null && layoutData.Size > 0)
            {
                vertexSize = layoutData.Size;
            }
            int vertexDataSize = numVertices * vertexSize;
            renderContext.UploadVertexData(vertices, vertexDataSize);

            if (indices != IntPtr.Zero && numIndices > 0)
            {
                int indexDataSize = numIndices * sizeof(ushort);
                renderContext.UploadIndexData(indices, indexDataSize);
            }

            if (layout != IntPtr.Zero)
            {
                var vertexLayout = new NativeEngine.VertexLayout { self = layout };
                renderContext.SetupVertexLayout(vertexLayout);
            }

            var primitiveType = (NativeEngine.RenderPrimitiveType)type;

            if (indices != IntPtr.Zero && numIndices > 0)
            {
                renderContext.DrawIndexed(primitiveType, 0, numIndices, numVertices, 0);
            }
            else
            {
                renderContext.Draw(primitiveType, 0, numVertices);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NativeAOT] RenderTools_Draw error: {ex}");
        }
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_ResolveFrameBuffer(IntPtr renderContext, IntPtr texture, NativeRect* viewport)
    {
        if (renderContext == IntPtr.Zero || texture == IntPtr.Zero) return;
        var gl = PlatformFunctions.GetGL();
        if (gl == null) return;

        var texData = TextureSystem.GetTextureData(texture);
        if (texData == null || texData.OpenGLHandle == 0) return;

        // Déterminer la taille à partir du viewport ou du niveau 0
        int width = 0, height = 0;
        if (viewport != null)
        {
            width = viewport->w;
            height = viewport->h;
        }
        else
        {
            gl.GetTextureLevelParameter(texData.OpenGLHandle, 0, GetTextureParameter.TextureWidth, out width);
            gl.GetTextureLevelParameter(texData.OpenGLHandle, 0, GetTextureParameter.TextureHeight, out height);
        }
        if (width <= 0 || height <= 0) return;

        // Blit depuis le framebuffer par défaut (0) vers un FBO attachant la texture
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, fbo);
        gl.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texData.OpenGLHandle, 0);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, (uint)ClearBufferMask.ColorBufferBit, GLEnum.Linear);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.DeleteFramebuffers(1, in fbo);
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_ResolveDepthBuffer(IntPtr renderContext, IntPtr texture, NativeRect* viewport)
    {
        if (renderContext == IntPtr.Zero || texture == IntPtr.Zero) return;
        var gl = PlatformFunctions.GetGL();
        if (gl == null) return;

        var texData = TextureSystem.GetTextureData(texture);
        if (texData == null || texData.OpenGLHandle == 0) return;

        int width = 0, height = 0;
        if (viewport != null)
        {
            width = viewport->w;
            height = viewport->h;
        }
        else
        {
            gl.GetTextureLevelParameter(texData.OpenGLHandle, 0, GetTextureParameter.TextureWidth, out width);
            gl.GetTextureLevelParameter(texData.OpenGLHandle, 0, GetTextureParameter.TextureHeight, out height);
        }
        if (width <= 0 || height <= 0) return;

        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, fbo);
        gl.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, texData.OpenGLHandle, 0);
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, (uint)ClearBufferMask.DepthBufferBit, GLEnum.Nearest);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.DeleteFramebuffers(1, in fbo);
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_DrawSceneObject(IntPtr renderContext, IntPtr sceneLayer, IntPtr sceneObject, Transform* transform, System.Numerics.Vector4* color, IntPtr material, IntPtr attributes)
    {
        if (!_loggedDrawSceneObject)
        {
            Console.WriteLine($"[NativeAOT] RenderTools_DrawSceneObject first call: ctx=0x{renderContext.ToInt64():X} obj=0x{sceneObject.ToInt64():X} mat=0x{material.ToInt64():X} attrs=0x{attributes.ToInt64():X}");
            _loggedDrawSceneObject = true;
        }

        // For now, route to DrawModel fallback (we don't have a scene graph hookup yet).
        
    }

    public static void RenderTools_DrawModelInternal(IntPtr renderContext, IntPtr sceneLayer, IntPtr hModel, IntPtr transforms, int numTransforms, IntPtr attributes)
    {
        if (!_loggedDrawModel)
        {
            Console.WriteLine($"[NativeAOT] RenderTools_DrawModel first call: ctx=0x{renderContext.ToInt64():X} model=0x{hModel.ToInt64():X} transforms={numTransforms} attrs=0x{attributes.ToInt64():X}");
            _loggedDrawModel = true;
        }

        if (renderContext == IntPtr.Zero)
            return;

        var ctx = EmulatedRenderContext.GetInstance(renderContext);
        if (ctx == null)
        {
            Console.WriteLine("[NativeAOT] RenderTools_DrawModel: Failed to get EmulatedRenderContext instance");
            return;
        }

        // Fallback: draw a unit cube with the basic shader to validate the pipeline.
        unsafe
        {
            fixed (BasicVertex* vptr = _fallbackCubeVertices)
            fixed (ushort* iptr = _fallbackCubeIndices)
            {
                int vertexSize = sizeof(BasicVertex); // 80 bytes to match default layout
                int vertexDataSize = _fallbackCubeVertices.Length * vertexSize;
                int indexDataSize = _fallbackCubeIndices.Length * sizeof(ushort);

                ctx.UploadVertexData((IntPtr)vptr, vertexDataSize);
                ctx.UploadIndexData((IntPtr)iptr, indexDataSize);

                // Setup default layout (stride 80) so attributes are enabled.
                ctx.SetupVertexLayout(new NativeEngine.VertexLayout { self = IntPtr.Zero });

                ctx.DrawIndexed(NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLES, 0, _fallbackCubeIndices.Length, _fallbackCubeVertices.Length, 0);
            }
        }
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_DrawModel(IntPtr renderContext, IntPtr sceneLayer, IntPtr hModel, IntPtr transforms, int numTransforms, IntPtr attributes)
    {
        RenderTools_DrawModelInternal(renderContext, sceneLayer, hModel, transforms, numTransforms, attributes);
        
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_DrawModel_1(IntPtr renderContext, IntPtr sceneLayer, IntPtr hModel, IntPtr hDrawArgBuffer, int nBufferOffset, IntPtr attributes)
    {
        Console.WriteLine("[NativeAOT] RenderTools_DrawModel_1: indirect draw not yet wired");
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_Compute(IntPtr renderContext, IntPtr attributes, IntPtr pMode, int tx, int ty, int tz)
    {
        Console.WriteLine("[NativeAOT] RenderTools_Compute: compute shaders not supported in GL path");
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_ComputeIndirect(IntPtr renderContext, IntPtr attributes, IntPtr pMode, IntPtr hIndirectBuffer, uint nIndirectBufferOffset)
    {
        Console.WriteLine("[NativeAOT] RenderTools_ComputeIndirect: compute indirect not supported");
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_TraceRays(IntPtr renderContext, IntPtr attributes, IntPtr pMode, uint tx, uint ty, uint tz)
    {
        Console.WriteLine("[NativeAOT] RenderTools_TraceRays: ray tracing not supported");
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_TraceRaysIndirect(IntPtr renderContext, IntPtr attributes, IntPtr pMode, IntPtr hIndirectBuffer, uint nIndirectBufferOffset)
    {
        Console.WriteLine("[NativeAOT] RenderTools_TraceRaysIndirect: ray tracing not supported");
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_SetDynamicConstantBufferData(IntPtr attributes, StringToken nTokenID, IntPtr renderContext, IntPtr data, int dataSize)
    {
        if (attributes == IntPtr.Zero || data == IntPtr.Zero || dataSize <= 0) return;
        // Stocker un blob binaire dans RenderAttributes via PtrValues (pointeur managé)
        var buffer = new byte[dataSize];
        Marshal.Copy(data, buffer, 0, dataSize);
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        RenderAttributes.RenderAttributes.SetPtrValueHelper(attributes, nTokenID, (IntPtr)handle.AddrOfPinnedObject());
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_CopyTexture(IntPtr renderContext, IntPtr sourceTexture, IntPtr destTexture, NativeRect* pSrcRect, int nDestX, int nDestY, uint nSrcMipSlice, uint nSrcArraySlice, uint nDstMipSlice, uint nDstArraySlice)
    {
        var gl = PlatformFunctions.GetGL();
        if (gl == null || sourceTexture == IntPtr.Zero || destTexture == IntPtr.Zero) return;

        var src = TextureSystem.GetTextureData(sourceTexture);
        var dst = TextureSystem.GetTextureData(destTexture);
        if (src == null || dst == null || src.OpenGLHandle == 0 || dst.OpenGLHandle == 0) return;

        uint srcFbo = gl.GenFramebuffer();
        uint dstFbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, srcFbo);
        gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, dstFbo);
        gl.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, src.OpenGLHandle, (int)nSrcMipSlice);
        gl.FramebufferTexture2D(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, dst.OpenGLHandle, (int)nDstMipSlice);

        int srcW, srcH;
        gl.GetTextureLevelParameter(src.OpenGLHandle, (int)nSrcMipSlice, GetTextureParameter.TextureWidth, out srcW);
        gl.GetTextureLevelParameter(src.OpenGLHandle, (int)nSrcMipSlice, GetTextureParameter.TextureHeight, out srcH);

        int x0 = pSrcRect != null ? pSrcRect->x : 0;
        int y0 = pSrcRect != null ? pSrcRect->y : 0;
        int x1 = pSrcRect != null ? pSrcRect->x + pSrcRect->w : srcW;
        int y1 = pSrcRect != null ? pSrcRect->y + pSrcRect->h : srcH;

        int dx0 = nDestX;
        int dy0 = nDestY;
        int dx1 = dx0 + (x1 - x0);
        int dy1 = dy0 + (y1 - y0);

        gl.BlitFramebuffer(x0, y0, x1, y1, dx0, dy0, dx1, dy1, (uint)ClearBufferMask.ColorBufferBit, GLEnum.Linear);
        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.DeleteFramebuffers(1, in srcFbo);
        gl.DeleteFramebuffers(1, in dstFbo);
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_SetGPUBufferData(IntPtr renderContext, IntPtr hGpuBuffer, IntPtr pData, uint nDataSize, uint nOffset)
    {
        if (hGpuBuffer == IntPtr.Zero || pData == IntPtr.Zero || nDataSize == 0) return;
        var gl = PlatformFunctions.GetGL();
        if (gl == null) return;

        var bufferData = HandleManager.Get<RenderDevice.BufferData>((int)hGpuBuffer);
        if (bufferData == null || bufferData.OpenGLBufferHandle == 0) return;

        gl.BindBuffer(bufferData.BufferType, bufferData.OpenGLBufferHandle);
        gl.BufferSubData(bufferData.BufferType, (IntPtr)nOffset, nDataSize, (void*)pData);
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_CopyGPUBufferHiddenStructureCount(IntPtr renderContext, IntPtr hSrcBuffer, IntPtr hDestBuffer, uint nDestBufferOffset)
    {
        Console.WriteLine("[NativeAOT] RenderTools_CopyGPUBufferHiddenStructureCount: not supported (hidden counter)");
    }

    [UnmanagedCallersOnly]
    public static void RenderTools_SetGPUBufferHiddenStructureCount(IntPtr renderContext, IntPtr hBuffer, uint nCounter)
    {
        Console.WriteLine("[NativeAOT] RenderTools_SetGPUBufferHiddenStructureCount: not supported (hidden counter)");
    }
}

