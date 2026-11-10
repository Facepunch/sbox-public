using System.Threading;

namespace Sandbox;

public partial class Shader
{
	/// <summary>
	/// Compile .shader source from a string into an anonymous shader, like an anonymous texture or material: nothing can
	/// find it by name, it lives in memory, and it goes when nothing uses it. Use it with
	/// <see cref="Material.FromShader(Shader)"/> or <see cref="Material.Create(string, Shader, bool)"/>. Each call makes
	/// a new shader. Includes come from the engine's shaders and the game's. A dedicated server compiles it too, so
	/// errors show up there, but returns null, the same as <see cref="Load(string)"/>.
	/// </summary>
	/// <param name="source">The whole .shader file</param>
	/// <param name="token">Stops the compile</param>
	/// <exception cref="ShaderCompileException">The shader didn't compile</exception>
	public static Task<Shader> CompileAsync( string source, CancellationToken token = default )
	{
		return Engine.Shaders.GameShaders.CompileAnonymous( source, token );
	}

	/// <summary>
	/// Compile .shader source from a string. The name identifies it, so compiling the same name again swaps the new
	/// version in, and anything using the shader picks it up. Includes come from the engine's shaders and the game's.
	/// Unchanged source isn't compiled again. A dedicated server compiles it too, so errors show up there, but returns
	/// null, the same as <see cref="Load(string)"/>.
	/// </summary>
	/// <param name="name">What to call it, like "water" or "effects/water.shader"</param>
	/// <param name="source">The whole .shader file</param>
	/// <param name="token">Stops the compile</param>
	/// <exception cref="ShaderCompileException">The shader didn't compile</exception>
	public static Task<Shader> CompileAsync( string name, string source, CancellationToken token = default )
	{
		return Engine.Shaders.GameShaders.CompileSource( name, source, token );
	}

	/// <summary>
	/// Compile a .shader from the game's files. Compiling the same path again swaps the new version in, and anything
	/// using the shader picks it up. Unchanged source isn't compiled again. A dedicated server compiles it too, so
	/// errors show up there, but returns null, the same as <see cref="Load(string)"/> does there.
	/// </summary>
	/// <exception cref="ShaderCompileException">The shader didn't compile</exception>
	public static Task<Shader> CompileFileAsync( string path, CancellationToken token = default )
	{
		return CompileFileAsync( FileSystem.Mounted, path, token );
	}

	/// <summary>
	/// Compile a .shader from a filesystem, like <see cref="FileSystem.Data"/> for shaders the game writes itself.
	/// Includes are read from the same filesystem, then from the engine's shaders. Compiling the same path again
	/// swaps the new version in, and anything using the shader picks it up. Unchanged source isn't compiled again.
	/// A dedicated server compiles it too, so errors show up there, but returns null, the same as <see cref="Load(string)"/>.
	/// </summary>
	/// <exception cref="ShaderCompileException">The shader didn't compile</exception>
	public static Task<Shader> CompileFileAsync( BaseFileSystem fileSystem, string path, CancellationToken token = default )
	{
		return Engine.Shaders.GameShaders.Compile( fileSystem, path, token );
	}
}

/// <summary>
/// Thrown by <see cref="Shader.CompileAsync(string, string, CancellationToken)"/> and
/// <see cref="Shader.CompileFileAsync(string, CancellationToken)"/> when a shader doesn't compile
/// </summary>
public sealed class ShaderCompileException : Exception
{
	/// <summary>
	/// The shader that didn't compile
	/// </summary>
	public string Path { get; }

	/// <summary>
	/// What the compiler said, with file and line
	/// </summary>
	public IReadOnlyList<string> Errors { get; }

	internal ShaderCompileException( string path, IReadOnlyList<string> errors )
		: base( errors.Count > 0 ? $"{path} didn't compile: {errors[0]}" : $"{path} didn't compile" )
	{
		Path = path;
		Errors = errors;
	}
}
