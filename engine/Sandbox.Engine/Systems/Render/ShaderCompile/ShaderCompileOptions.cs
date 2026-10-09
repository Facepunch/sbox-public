namespace Sandbox.Engine.Shaders;

/// <summary>
/// Options used when compiling a shader
/// </summary>
public struct ShaderCompileOptions
{
	public bool SingleThreaded { get; set; }
	public bool ForceRecompile { get; set; }

	/// <summary>
	/// Write to console. Used when running from the command line.
	/// </summary>
	public bool ConsoleOutput { get; set; }

	/// <summary>
	/// Write each combo's SPIR-V under this folder instead of writing the <c>.shader_c</c>, for offline analysis
	/// (<c>SboxBuild shader-stats</c>). Up to date shaders are compiled anyway. See <see cref="SpirvDump"/>.
	/// </summary>
	public string SpirvDumpPath { get; set; }
}
