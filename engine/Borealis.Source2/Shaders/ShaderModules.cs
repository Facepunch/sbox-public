using Borealis.Gpu;

namespace Borealis.Source2.Shaders;

/// <summary>
/// Shaders drawn from source modules instead of their compiled programs: a <see cref="ShaderLibrary"/> asks it for each
/// variant of a shader it has a module for (<see cref="ShaderLibrary.Modules"/>). The shader file still names the
/// shader, its modes and its features; the stages come from the module. See docs/managed/shader-format-replacement.md
/// ("Pass modules").
/// </summary>
public interface IShaderModules
{
	/// <summary>Whether <paramref name="shader"/> is drawn from a module.</summary>
	bool Has( ShaderFile shader );

	/// <summary>
	/// The stages that draw <paramref name="mode"/> of <paramref name="shader"/> with these feature values (by the shader
	/// file's feature index), dynamic combos and attributes (specialization constants), or null when its module doesn't
	/// compile: then the shader's own programs draw it.
	/// </summary>
	ModuleProgram Compile( ShaderFile shader, ShaderMode mode, ReadOnlySpan<int> features, Func<string, int?> dynamicCombos, Func<string, int?> attributes );
}

/// <summary>A shader's stages from its module, for one mode and combo values.</summary>
public sealed class ModuleProgram
{
	/// <summary>The vertex stage, or null.</summary>
	public ModuleStage Vertex { get; init; }

	/// <summary>The pixel stage, or null.</summary>
	public ModuleStage Pixel { get; init; }

	/// <summary>The compute stage, or null.</summary>
	public ModuleStage Compute { get; init; }

	/// <summary>Identifies the stages and the values compiled in: variants with the same key are the same.</summary>
	public string Key { get; init; }
}

/// <summary>
/// One stage compiled from a module: its SPIR-V (entry point named by stage, <c>MainVs</c>, <c>MainPs</c>, <c>MainCs</c>) and
/// what each binding is, as a compiled program's tables say it for a shader file's stages.
/// </summary>
public sealed class ModuleStage
{
	/// <summary>Which stage.</summary>
	public ShaderProgramType Type { get; init; }

	/// <summary>A name for diagnostics.</summary>
	public string Name { get; init; }

	/// <summary>The SPIR-V.</summary>
	public byte[] Spirv { get; init; }

	/// <summary>Each vertex input location's semantic, <c>usage &lt;&lt; 4 | index</c> (<see cref="ShaderBytecode.AttributeMap"/>).</summary>
	public byte[] AttributeMap { get; init; } = [];

	/// <summary>A compute stage's thread group size.</summary>
	public (int X, int Y, int Z) ThreadGroupSize { get; init; }

	/// <summary>Specialization constants to make its pipelines with.</summary>
	public SpecializationConstant[] Constants { get; init; } = [];

	/// <summary>Its bindings in sets 0 to 3 and what each holds.</summary>
	public IReadOnlyList<ModuleResource> Resources { get; init; } = [];

	/// <summary>Its <c>$Globals</c> constant buffer, which binds as a shader file's material constants; set -1 for none.</summary>
	public (int Set, int Binding, int Size) Globals { get; init; } = (-1, -1, 0);

	/// <summary>The <c>$Globals</c> members and their byte offsets: attributes, engine-computed values or defaults.</summary>
	public IReadOnlyList<(ShaderVariable Variable, int Offset)> GlobalMembers { get; init; } = [];

	/// <summary>Constant buffers the engine fills, with the sizes compiled.</summary>
	public IReadOnlyList<ConstantBufferLayout> ConstantBuffers { get; init; } = [];

	/// <summary>The pixel stage's render states, as render-state literals (<c>VfxRenderState_t</c>, variable).</summary>
	public IReadOnlyList<(int State, ShaderVariable Variable)> RenderStates { get; init; } = [];
}

/// <summary>
/// A binding of a module stage: where it is and the variable that names it (<see cref="ShaderVariable.Attribute"/> or its
/// name, with <see cref="ShaderVariable.RenderStateId"/> 1 for a texture read as sRGB); a sampler's state.
/// </summary>
public readonly record struct ModuleResource( int Set, int Binding, ShaderVariable Variable, SamplerInfo? Sampler = null );
