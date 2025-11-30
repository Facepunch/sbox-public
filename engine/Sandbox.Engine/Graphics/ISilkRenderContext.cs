using System;
using System.Numerics;

namespace Sandbox.Engine.Graphics
{
    /// <summary>
    /// Defines the interface for a hardware-accelerated rendering context using Silk.NET.
    /// This interface aims to abstract the underlying graphics API (e.g., OpenGL, Vulkan)
    /// to provide a consistent rendering surface for the engine.
    /// </summary>
    public interface ISilkRenderContext : IDisposable
    {
        // Drawing commands
        void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);
        void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance);

        // State management
        void SetViewport(int x, int y, int width, int height);
        void SetScissorRect(int x, int y, int width, int height);
        void Clear(Vector4 color, float depth, int stencil);
        
        // Resource binding (simplified for now)
        void BindVertexBuffer(uint slot, IntPtr bufferHandle, uint stride, uint offset);
        void BindIndexBuffer(IntPtr bufferHandle, IndexType type, uint offset);
        void BindTexture(uint slot, IntPtr textureHandle);
        void BindVertexShader(IntPtr shaderHandle);
        void BindPixelShader(IntPtr shaderHandle);

        // Render target management
        void BindRenderTargets(IntPtr[] colorTargetHandles, IntPtr depthStencilTargetHandle);
        void RestoreRenderTargets();

        // Other utility functions
        void GenerateMipMaps(IntPtr textureHandle);
        void Submit();
        void BeginEvent(string name);
        void EndEvent();
        void SetMarker(string name);
    }

    public enum IndexType
    {
        UInt16,
        UInt32
    }
}