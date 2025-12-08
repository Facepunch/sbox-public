using System;
using System.Runtime.CompilerServices;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Sandbox.Engine.Emulation;

namespace EmulationHarnessDesktop;

internal unsafe static class Program
{
    private static IWindow? _window;
    private static GL? _gl;
    private static uint _vao;
    private static uint _vbo;
    private static uint _ebo;
    private static uint _program;

    // Simple positions (x,y) + colors (r,g,b)
    private static readonly float[] Vertices =
    {
        // positions   // colors
        0.0f,  0.5f,    1f, 0f, 0f,
       -0.5f, -0.5f,    0f, 1f, 0f,
        0.5f, -0.5f,    0f, 0f, 1f
    };

    private static readonly ushort[] Indices = { 0, 1, 2 };

    private const string VertexSrc = @"
#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec3 aCol;
out vec3 vCol;
void main()
{
    vCol = aCol;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";

    private const string FragmentSrc = @"
#version 330 core
in vec3 vCol;
out vec4 FragColor;
void main()
{
    FragColor = vec4(vCol, 1.0);
}";

    public static void Main(string[] args)
    {
        // Touch the emulation assembly to ensure it loads
        _ = typeof(EngineExports);

        var options = WindowOptions.Default;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3));
        options.Title = "Emulation Harness (Desktop)";
        _window = Window.Create(options);

        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClosing;

        _window.Run();
    }

    private static void OnLoad()
    {
        _gl = GL.GetApi(_window!);
        _gl.ClearColor(0.1f, 0.1f, 0.15f, 1f);

        _program = CompileProgram(VertexSrc, FragmentSrc);
        if (_program == 0)
        {
            Console.WriteLine("[Harness] Failed to compile shader program.");
            _window!.Close();
            return;
        }

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _ebo = _gl.GenBuffer();

        _gl.BindVertexArray(_vao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        unsafe
        {
            fixed (float* v = Vertices)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(Vertices.Length * sizeof(float)), v, BufferUsageARB.StaticDraw);
            }
        }

        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
        unsafe
        {
            fixed (ushort* i = Indices)
            {
                _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(Indices.Length * sizeof(ushort)), i, BufferUsageARB.StaticDraw);
            }
        }

        uint stride = (uint)(5 * sizeof(float));
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(2 * sizeof(float)));
    }

    private static void OnRender(double dt)
    {
        if (_gl == null) return;
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _gl.UseProgram(_program);
        _gl.BindVertexArray(_vao);
        _gl.DrawElements(PrimitiveType.Triangles, (uint)Indices.Length, DrawElementsType.UnsignedShort, null);
    }

    private static void OnClosing()
    {
        if (_gl == null) return;
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_vbo);
        _gl.DeleteBuffer(_ebo);
        _gl.DeleteProgram(_program);
    }

    private static uint CompileProgram(string vs, string fs)
    {
        if (_gl == null) return 0;
        uint v = CompileShader(ShaderType.VertexShader, vs);
        uint f = CompileShader(ShaderType.FragmentShader, fs);
        if (v == 0 || f == 0)
            return 0;

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, v);
        _gl.AttachShader(program, f);
        _gl.LinkProgram(program);
        _gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
        _gl.DeleteShader(v);
        _gl.DeleteShader(f);
        if (linked == 0)
        {
            string info = _gl.GetProgramInfoLog(program);
            Console.WriteLine($"[Harness] Program link failed: {info}");
            _gl.DeleteProgram(program);
            return 0;
        }
        return program;
    }

    private static uint CompileShader(ShaderType type, string src)
    {
        if (_gl == null) return 0;
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, src);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int ok);
        if (ok == 0)
        {
            string info = _gl.GetShaderInfoLog(shader);
            Console.WriteLine($"[Harness] Shader compile failed ({type}): {info}");
            _gl.DeleteShader(shader);
            return 0;
        }
        return shader;
    }
}

