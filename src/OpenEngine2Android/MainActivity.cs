using Android.App;
using Android.Opengl;
using Android.OS;
using JEGL = Javax.Microedition.Khronos.Egl;
using Javax.Microedition.Khronos.Opengles;
using System;

namespace OpenEngine2Android;

[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true, ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation | Android.Content.PM.ConfigChanges.ScreenSize | Android.Content.PM.ConfigChanges.KeyboardHidden)]
public class MainActivity : Activity
{
    private GLSurfaceView? _glView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _glView = new GLSurfaceView(this)
        {
            PreserveEGLContextOnPause = true
        };
        _glView.SetEGLContextClientVersion(3);
        _glView.SetRenderer(new TriangleRenderer());
        SetContentView(_glView);
    }

    protected override void OnPause()
    {
        base.OnPause();
        _glView?.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _glView?.OnResume();
    }

    private class TriangleRenderer : Java.Lang.Object, GLSurfaceView.IRenderer
    {
        private int _program;
        private int _vbo;
        private int _vao;

        private const string VertexSrc = @"
#version 300 es
layout (location = 0) in vec2 aPos;
layout (location = 1) in vec3 aCol;
out vec3 vCol;
void main()
{
    vCol = aCol;
    gl_Position = vec4(aPos, 0.0, 1.0);
}";

        private const string FragmentSrc = @"
#version 300 es
precision mediump float;
in vec3 vCol;
out vec4 FragColor;
void main()
{
    FragColor = vec4(vCol, 1.0);
}";

        private readonly float[] _vertices =
        {
            // positions   // colors
             0.0f,  0.5f,    1f, 0f, 0f,
            -0.5f, -0.5f,    0f, 1f, 0f,
             0.5f, -0.5f,    0f, 0f, 1f
        };

        public void OnSurfaceCreated(IGL10? gl, Javax.Microedition.Khronos.Egl.EGLConfig? config)
        {
            GLES30.GlClearColor(0.1f, 0.1f, 0.15f, 1f);
            _program = CompileProgram(VertexSrc, FragmentSrc);

            int[] buffers = new int[1];
            GLES30.GlGenBuffers(1, buffers, 0);
            _vbo = buffers[0];
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _vbo);
            Java.Nio.FloatBuffer vertexBuffer = Java.Nio.ByteBuffer.AllocateDirect(_vertices.Length * sizeof(float))
                .Order(Java.Nio.ByteOrder.NativeOrder())
                .AsFloatBuffer();
            vertexBuffer.Put(_vertices).Position(0);
            GLES30.GlBufferData(GLES30.GlArrayBuffer, _vertices.Length * sizeof(float), vertexBuffer, GLES30.GlStaticDraw);

            GLES30.GlGenVertexArrays(1, buffers, 0);
            _vao = buffers[0];
            GLES30.GlBindVertexArray(_vao);

            int stride = 5 * sizeof(float);
            GLES30.GlEnableVertexAttribArray(0);
            GLES30.GlVertexAttribPointer(0, 2, GLES30.GlFloat, false, stride, 0);
            GLES30.GlEnableVertexAttribArray(1);
            GLES30.GlVertexAttribPointer(1, 3, GLES30.GlFloat, false, stride, 2 * sizeof(float));
        }

        public void OnSurfaceChanged(IGL10? gl, int width, int height)
        {
            GLES30.GlViewport(0, 0, width, height);
        }

        public void OnDrawFrame(IGL10? gl)
        {
            GLES30.GlClear(GLES30.GlColorBufferBit);
            GLES30.GlUseProgram(_program);
            GLES30.GlBindVertexArray(_vao);
            GLES30.GlDrawArrays(GLES30.GlTriangles, 0, 3);
        }

        private static int CompileProgram(string vs, string fs)
        {
            int v = CompileShader(GLES30.GlVertexShader, vs);
            int f = CompileShader(GLES30.GlFragmentShader, fs);
            if (v == 0 || f == 0) return 0;

            int program = GLES30.GlCreateProgram();
            GLES30.GlAttachShader(program, v);
            GLES30.GlAttachShader(program, f);
            GLES30.GlLinkProgram(program);
            int[] linkStatus = new int[1];
            GLES30.GlGetProgramiv(program, GLES30.GlLinkStatus, linkStatus, 0);
            int linked = linkStatus[0];
            GLES30.GlDeleteShader(v);
            GLES30.GlDeleteShader(f);
            if (linked == 0)
            {
                string log = GLES30.GlGetProgramInfoLog(program);
                Android.Util.Log.Error("Harness", $"Program link failed: {log}");
                GLES30.GlDeleteProgram(program);
                return 0;
            }
            return program;
        }

        private static int CompileShader(int type, string src)
        {
            int shader = GLES30.GlCreateShader(type);
            GLES30.GlShaderSource(shader, src);
            GLES30.GlCompileShader(shader);
            int[] compileStatus = new int[1];
            GLES30.GlGetShaderiv(shader, GLES30.GlCompileStatus, compileStatus, 0);
            int ok = compileStatus[0];
            if (ok == 0)
            {
                string log = GLES30.GlGetShaderInfoLog(shader) ?? string.Empty;
                Android.Util.Log.Error("Harness", $"Shader compile failed ({type}): {log}");
                GLES30.GlDeleteShader(shader);
                return 0;
            }
            return shader;
        }
    }
}
