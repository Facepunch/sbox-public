using System;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Sandbox.Engine.Emulation.Rendering;

/// <summary>
/// Basic OpenGL shaders for UI rendering
/// </summary>
internal static class BasicShaders
{
    private const string VertexShaderSource = @"
#version 330 core
layout (location = 0) in vec3 aPosition;
layout (location = 1) in vec4 aColor;
layout (location = 2) in vec3 aNormal;
layout (location = 3) in vec4 aTexCoord0;
layout (location = 4) in vec4 aTexCoord1;
layout (location = 5) in vec4 aTangent;

out vec4 vColor;
out vec4 vTexCoord0;

void main()
{
    gl_Position = vec4(aPosition, 1.0);
    vColor = aColor;
    vTexCoord0 = aTexCoord0;
}
";

    private const string FragmentShaderSource = @"
#version 330 core
in vec4 vColor;
in vec4 vTexCoord0;
out vec4 FragColor;

void main()
{
    FragColor = vColor;
}
";

    public static uint CreateShaderProgram(GL gl)
    {
        // Compile vertex shader
        uint vertexShader = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vertexShader, VertexShaderSource);
        gl.CompileShader(vertexShader);
        
        // Check compilation
        gl.GetShader(vertexShader, ShaderParameterName.CompileStatus, out int vertexSuccess);
        if (vertexSuccess == 0)
        {
            string infoLog = gl.GetShaderInfoLog(vertexShader);
            Console.WriteLine($"[NativeAOT] Vertex shader compilation failed: {infoLog}");
            gl.DeleteShader(vertexShader);
            return 0;
        }
        
        // Compile fragment shader
        uint fragmentShader = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fragmentShader, FragmentShaderSource);
        gl.CompileShader(fragmentShader);
        
        // Check compilation
        gl.GetShader(fragmentShader, ShaderParameterName.CompileStatus, out int fragmentSuccess);
        if (fragmentSuccess == 0)
        {
            string infoLog = gl.GetShaderInfoLog(fragmentShader);
            Console.WriteLine($"[NativeAOT] Fragment shader compilation failed: {infoLog}");
            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);
            return 0;
        }
        
        // Create shader program
        uint program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        gl.LinkProgram(program);
        
        // Check linking
        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linkSuccess);
        if (linkSuccess == 0)
        {
            string infoLog = gl.GetProgramInfoLog(program);
            Console.WriteLine($"[NativeAOT] Shader program linking failed: {infoLog}");
            gl.DeleteShader(vertexShader);
            gl.DeleteShader(fragmentShader);
            gl.DeleteProgram(program);
            return 0;
        }
        
        // Clean up shaders (they're linked into the program)
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);
        
        Console.WriteLine("[NativeAOT] Basic shader program created successfully");
        return program;
    }
}


