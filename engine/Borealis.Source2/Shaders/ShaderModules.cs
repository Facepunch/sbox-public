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

	/// <summary>
	/// A shader with no compiled file but a module where it would be (<paramref name="compiledPath"/>, the
	/// <c>.shader_c</c>'s full path): what the module declares (<see cref="ShaderFile.FromModule"/>), or null when there's
	/// no module there.
	/// </summary>
	ShaderFile Describe( string compiledPath );
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

	/// <summary>The task stage, or null: with <see cref="Mesh"/>, in place of <see cref="Vertex"/> where the device has mesh shaders.</summary>
	public ModuleStage Task { get; init; }

	/// <summary>The mesh stage, or null.</summary>
	public ModuleStage Mesh { get; init; }

	/// <summary>Identifies the stages and the values compiled in: variants with the same key are the same.</summary>
	public string Key { get; init; }
}

/// <summary>
/// One stage compiled from a module: its SPIR-V (entry point named by stage, <c>MainVs</c>, <c>MainPs</c>, <c>MainCs</c>, <c>MainTs</c>, <c>MainMs</c>) and
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

	/// <summary>A compute, task or mesh stage's thread group size.</summary>
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

	/// <summary>A vertex stage's expansion of the draw's primitives (<c>ExpandPrimitives</c>), or none.</summary>
	public PrimitiveExpansion Expansion { get; init; }
}

/// <summary>What a vertex stage that expands primitives takes each instance of its draw from.</summary>
public enum ExpandedPrimitive
{
	/// <summary>It draws the vertices it's given.</summary>
	None,

	/// <summary>Each point of a point list.</summary>
	Points,

	/// <summary>Each line of a line list or strip; its second end is read at semantic indices 4 up.</summary>
	Lines,

	/// <summary>Each triangle of a triangle list; its second and third corners are read at semantic indices 4 and 8 up.</summary>
	Triangles,

	/// <summary>Each vertex, whatever the topology.</summary>
	Vertices,
}

/// <summary>
/// A vertex stage that makes each primitive's geometry itself, in place of a <c>.shader</c>'s geometry stage
/// (<c>ExpandPrimitives</c> in a pass module): the draw runs <see cref="Vertices"/> vertices as <see cref="Topology"/> for
/// each primitive it's given, one instance per primitive, with the primitive's vertices as per-instance inputs.
/// </summary>
public readonly record struct PrimitiveExpansion( ExpandedPrimitive From, int Vertices, PrimitiveTopology Topology )
{
	/// <summary>Whether the stage expands primitives.</summary>
	public bool Expands => From != ExpandedPrimitive.None;

	/// <summary>Vertices of the draw each instance reads: two for a line, three for a triangle, else one.</summary>
	public int Corners => From switch { ExpandedPrimitive.Lines => 2, ExpandedPrimitive.Triangles => 3, _ => 1 };

	/// <summary>Which of the primitive's vertices an input reads (0 for the first), by its semantic index.</summary>
	public int Corner( in VertexInput input ) => input.SemanticIndex >= 4 && input.SemanticIndex < 4 * Corners ? input.SemanticIndex / 4 : 0;

	/// <summary>The semantic index an input reads in its vertex's stream.</summary>
	public int SemanticIndex( in VertexInput input ) => input.SemanticIndex - 4 * Corner( input );

	/// <summary>Vertices of the draw from one primitive to the next: a line list's two, a triangle list's three, else one.</summary>
	public int Step( PrimitiveTopology topology ) => From switch
	{
		ExpandedPrimitive.Lines when topology == PrimitiveTopology.LineList => 2,
		ExpandedPrimitive.Triangles => 3,
		_ => 1,
	};

	/// <summary>
	/// The primitives (instances) a draw of <paramref name="vertexCount"/> vertices as <paramref name="topology"/> gives, or
	/// -1 when the stage can't take that topology, as a geometry stage's input wouldn't match it.
	/// </summary>
	public int Primitives( PrimitiveTopology topology, int vertexCount ) => From switch
	{
		ExpandedPrimitive.Vertices => vertexCount,
		ExpandedPrimitive.Points when topology == PrimitiveTopology.PointList => vertexCount,
		ExpandedPrimitive.Lines when topology == PrimitiveTopology.LineList => vertexCount / 2,
		ExpandedPrimitive.Lines when topology == PrimitiveTopology.LineStrip => Math.Max( vertexCount - 1, 0 ),
		ExpandedPrimitive.Triangles when topology == PrimitiveTopology.TriangleList => vertexCount / 3,
		_ => -1,
	};

	/// <summary>An expansion from a module's <c>ExpandPrimitives( from, vertices, topology )</c>, or none when it doesn't read.</summary>
	public static PrimitiveExpansion Parse( string from, int vertices, string topology )
	{
		var kind = from.ToUpperInvariant() switch
		{
			"POINTS" => ExpandedPrimitive.Points,
			"LINES" => ExpandedPrimitive.Lines,
			"TRIANGLES" => ExpandedPrimitive.Triangles,
			"VERTICES" => ExpandedPrimitive.Vertices,
			_ => ExpandedPrimitive.None,
		};

		PrimitiveTopology? output = topology.ToUpperInvariant() switch
		{
			"TRIANGLE_STRIP" => PrimitiveTopology.TriangleStrip,
			"TRIANGLE_LIST" => PrimitiveTopology.TriangleList,
			"LINE_LIST" => PrimitiveTopology.LineList,
			"LINE_STRIP" => PrimitiveTopology.LineStrip,
			_ => null,
		};

		return kind == ExpandedPrimitive.None || output is null || vertices <= 0 ? default : new PrimitiveExpansion( kind, vertices, output.Value );
	}
}

/// <summary>
/// A binding of a module stage: where it is and the variable that names it (<see cref="ShaderVariable.Attribute"/> or its
/// name, with <see cref="ShaderVariable.RenderStateId"/> 1 for a texture read as sRGB); a sampler's state. An append or
/// consume buffer's hidden counter names no variable and gives its buffer's variable name as <see cref="CounterOf"/>.
/// </summary>
public readonly record struct ModuleResource( int Set, int Binding, ShaderVariable Variable, SamplerInfo? Sampler = null, string CounterOf = null );
