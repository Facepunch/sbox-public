namespace Sandbox.Engine.Shaders;

/// <summary>
/// Options used when compiling a shader
/// </summary>
public struct ShaderCompileOptions
{
	public bool SingleThreaded { get; set; }
	public bool ForceRecompile { get; set; }

	/// <summary>
	/// Most combos to compile at once. 0 picks from the core count and free memory.
	/// </summary>
	public int MaxThreads { get; set; }

	/// <summary>
	/// Write to console. Used when running from the command line.
	/// </summary>
	public bool ConsoleOutput { get; set; }

	/// <summary>
	/// Write each combo's SPIR-V under this folder instead of writing the <c>.shader_c</c>, for offline analysis
	/// (<c>SboxBuild shader-stats</c>). Up to date shaders are compiled anyway. See <see cref="SpirvDump"/>.
	/// </summary>
	public string SpirvDumpPath { get; set; }

	/// <summary>
	/// Compile Borealis's GPU-driven variant (<c>BOREALIS_GPU_DRIVEN</c>, entry points wrapped to pull vertices and read
	/// materials from tables) into <c>name.gpudriven.shader_c</c> beside the shader, leaving its <c>.shader_c</c> alone.
	/// See docs/managed/borealis-gpu-driven.md.
	/// </summary>
	internal bool GpuDriven { get; set; }

	/// <summary>
	/// Called as each combo is compiled, from the threads compiling them, for progress
	/// </summary>
	internal Action ComboCompiled { get; set; }
}
