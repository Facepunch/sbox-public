using System;
using System.Numerics;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Sandbox.Engine.Graphics
{
    public class SilkRenderContext : ISilkRenderContext
    {
        private GL _gl;
        private IWindow _window;

        public SilkRenderContext(IWindow window)
        {
            _window = window;
            _gl = window.CreateOpenGL();
        }

        public void Draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: Draw(vertexCount: {vertexCount}, instanceCount: {instanceCount}) - NOT IMPLEMENTED");
        }

        public void DrawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int vertexOffset, uint firstInstance)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: DrawIndexed(indexCount: {indexCount}, instanceCount: {instanceCount}) - NOT IMPLEMENTED");
        }

        public void SetViewport(int x, int y, int width, int height)
        {
            _gl.Viewport(x, y, (uint)width, (uint)height);
            Console.WriteLine($"SilkRenderContext: SetViewport(x: {x}, y: {y}, width: {width}, height: {height})");
        }

        public void SetScissorRect(int x, int y, int width, int height)
        {
            _gl.Scissor(x, y, (uint)width, (uint)height);
            Console.WriteLine($"SilkRenderContext: SetScissorRect(x: {x}, y: {y}, width: {width}, height: {height})");
        }

        public void Clear(Vector4 color, float depth, int stencil)
        {
            _gl.ClearColor(color.x, color.y, color.z, color.w);
            _gl.ClearDepth(depth);
            _gl.ClearStencil(stencil);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            Console.WriteLine($"SilkRenderContext: Clear(color: {color}, depth: {depth}, stencil: {stencil})");
        }

        public void BindVertexBuffer(uint slot, IntPtr bufferHandle, uint stride, uint offset)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: BindVertexBuffer(slot: {slot}, bufferHandle: {bufferHandle}, stride: {stride}, offset: {offset}) - NOT IMPLEMENTED");
        }

        public void BindIndexBuffer(IntPtr bufferHandle, IndexType type, uint offset)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: BindIndexBuffer(bufferHandle: {bufferHandle}, type: {type}, offset: {offset}) - NOT IMPLEMENTED");
        }

        public void BindTexture(uint slot, IntPtr textureHandle)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: BindTexture(slot: {slot}, textureHandle: {textureHandle}) - NOT IMPLEMENTED");
        }

        public void BindVertexShader(IntPtr shaderHandle)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: BindVertexShader(shaderHandle: {shaderHandle}) - NOT IMPLEMENTED");
        }

        public void BindPixelShader(IntPtr shaderHandle)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: BindPixelShader(shaderHandle: {shaderHandle}) - NOT IMPLEMENTED");
        }

        public void BindRenderTargets(IntPtr[] colorTargetHandles, IntPtr depthStencilTargetHandle)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine("SilkRenderContext: BindRenderTargets - NOT IMPLEMENTED");
        }

        public void RestoreRenderTargets()
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine("SilkRenderContext: RestoreRenderTargets - NOT IMPLEMENTED");
        }

        public void GenerateMipMaps(IntPtr textureHandle)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: GenerateMipMaps(textureHandle: {textureHandle}) - NOT IMPLEMENTED");
        }

        public void Submit()
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine("SilkRenderContext: Submit - NOT IMPLEMENTED");
        }

        public void BeginEvent(string name)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: BeginEvent(name: {name}) - NOT IMPLEMENTED");
        }

        public void EndEvent()
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine("SilkRenderContext: EndEvent - NOT IMPLEMENTED");
        }

        public void SetMarker(string name)
        {
            // TODO: Implement using Silk.NET.OpenGL
            Console.WriteLine($"SilkRenderContext: SetMarker(name: {name}) - NOT IMPLEMENTED");
        }

        public void Dispose()
        {
            _gl.Dispose();
            Console.WriteLine("SilkRenderContext: Disposed");
        }
    }
}