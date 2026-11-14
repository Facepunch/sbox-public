using Basalt.Gpu;
using Basalt.Source2.Resources;
using System.Buffers.Binary;
using System.IO;

namespace Basalt.Source2.Shaders;

/// <summary>
/// One compiled static combo of a program (<c>CVfxStaticComboData</c>): for each dynamic combo, the variables it sets,
/// where its material constant buffer goes, its shader and (pixel shaders) its render state.
/// </summary>
public sealed class StaticCombo
{
	readonly DynamicCombo[] dynamicById;

	/// <summary>The program it belongs to.</summary>
	public ShaderProgram Program { get; }

	/// <summary>Its id.</summary>
	public ulong Id { get; }

	/// <summary>Every variable any of its dynamic combos uses.</summary>
	public VariableBinding[] Used { get; }

	/// <summary>Shader attributes it exports (<c>BoolAttribute( translucent, true )</c>...).</summary>
	public ShaderAttribute[] Attributes { get; }

	/// <summary>Size of the material constant buffer, in bytes; 0 for none.</summary>
	public int ConstantBufferSize { get; }

	/// <summary>Its distinct shaders; null where a combo has none (a depth-only pixel shader).</summary>
	public ShaderBytecode[] Shaders { get; }

	/// <summary>The dynamic combos that were compiled.</summary>
	public DynamicCombo[] DynamicCombos { get; }

	internal StaticCombo( ShaderProgram program, ulong id, byte[] blob )
	{
		Program = program;
		Id = id;
		var type = program.Type;
		var reader = new ByteReader( blob );

		Used = ReadBindings( reader, type != ShaderProgramType.Features );

		var attributes = new ShaderAttribute[reader.Short()];
		for ( int i = 0; i < attributes.Length; i++ )
		{
			var name = reader.Terminated();
			reader.UInt();
			var attributeType = (VariableType)reader.Byte();
			var binding = reader.Short();
			object value = null;
			if ( binding == -1 )
			{
				var expression = (int)reader.UInt();
				if ( expression > 0 )
				{
					value = reader.Bytes( expression );
				}
				else
				{
					value = attributeType switch
					{
						VariableType.Bool => reader.Byte() != 0,
						VariableType.Int => reader.Int(),
						VariableType.Float => reader.Float(),
						VariableType.Float2 => new[] { reader.Float(), reader.Float() },
						VariableType.Float3 => new[] { reader.Float(), reader.Float(), reader.Float() },
						VariableType.Float4 => new[] { reader.Float(), reader.Float(), reader.Float(), reader.Float() },
						_ => null,
					};
				}
			}

			attributes[i] = new ShaderAttribute( name, attributeType, binding, value );
		}

		Attributes = attributes;

		short[] signatures = [];
		if ( type is ShaderProgramType.Vertex or ShaderProgramType.Features )
		{
			signatures = new short[reader.CompactCount()];
			for ( int i = 0; i < signatures.Length; i++ ) signatures[i] = reader.Short();
		}

		if ( type == ShaderProgramType.Features )
		{
			Shaders = [];
			DynamicCombos = [];
			dynamicById = [];
			return;
		}

		var variables = new VariableBinding[reader.CompactCount()][];
		for ( int i = 0; i < variables.Length; i++ ) variables[i] = ReadBindings( reader, true );

		var buffers = new (int Slot, int Set)[reader.CompactCount()];
		for ( int i = 0; i < buffers.Length; i++ ) buffers[i] = (reader.Byte(), reader.Byte());

		ConstantBufferSize = (int)reader.UInt();
		reader.Byte();

		var shaderStart = reader.Position;
		var shaders = new ShaderBytecode[reader.Int()];
		var shaderOffsets = new int[shaders.Length];
		reader.Byte();
		for ( int i = 0; i < shaders.Length; i++ )
		{
			shaderOffsets[i] = reader.Position - shaderStart;
			var size = reader.Int();
			var bytes = reader.Span( size );
			reader.Skip( 16 );
			shaders[i] = size == 0 ? null : ShaderBytecode.Read( program, bytes.ToArray() );
		}

		Shaders = shaders;

		var dynamic = new DynamicCombo[reader.Int()];
		dynamicById = new DynamicCombo[program.DynamicComboCount];
		for ( int i = 0; i < dynamic.Length; i++ )
		{
			var dynamicId = reader.Long();
			var shader = reader.Int();
			reader.Int();

			ShaderRenderState renderState = null;
			if ( type is ShaderProgramType.Pixel or ShaderProgramType.PixelRenderState ) renderState = ShaderRenderState.Read( reader );
			if ( type == ShaderProgramType.Hull ) reader.Byte();

			var index = (int)dynamicId;
			dynamic[i] = new DynamicCombo
			{
				Id = (ulong)dynamicId,
				Shader = shaders[shader],
				Variables = index < variables.Length ? variables[index] : [],
				ConstantBuffer = index < buffers.Length && buffers[index].Slot != 255 ? buffers[index] : (-1, -1),
				InputSignature = index < signatures.Length && signatures[index] >= 0 ? program.InputSignatures[signatures[index]] : null,
				RenderState = renderState,
			};

			if ( index < dynamicById.Length ) dynamicById[index] = dynamic[i];
		}

		DynamicCombos = dynamic;
		if ( reader.Remaining != 0 ) throw new InvalidDataException( $"{program}: static combo {id} has {reader.Remaining} bytes left over" );
	}

	/// <summary>The one static combo of a stage compiled from a module, with its material constant buffer's size.</summary>
	internal StaticCombo( ShaderProgram program, int constantBufferSize )
	{
		Program = program;
		Used = [];
		Attributes = [];
		Shaders = [];
		DynamicCombos = [];
		dynamicById = [];
		ConstantBufferSize = constantBufferSize;
	}

	static VariableBinding[] ReadBindings( ByteReader reader, bool offsets )
	{
		var count = reader.Int();
		reader.Int();
		reader.Int();
		var bindings = new VariableBinding[count];
		for ( int i = 0; i < count; i++ )
		{
			var packed = reader.UShort();
			var offset = offsets ? reader.UShort() : 0;
			bindings[i] = new VariableBinding( packed & 0xFFF, packed >> 12, offset );
		}

		return bindings;
	}

	/// <summary>A dynamic combo by id, or null if rules skipped it.</summary>
	public DynamicCombo Dynamic( ulong id ) => id < (ulong)dynamicById.Length ? dynamicById[id] : null;
}

/// <summary>
/// One compiled dynamic combo of a static combo.
/// </summary>
public sealed class DynamicCombo
{
	/// <summary>Its id.</summary>
	public ulong Id { get; init; }

	/// <summary>Its shader, or null for none (a depth-only pixel shader).</summary>
	public ShaderBytecode Shader { get; init; }

	/// <summary>The variables it reads and where: texture and sampler registers, constant offsets, buffers.</summary>
	public VariableBinding[] Variables { get; init; }

	/// <summary>The material constant buffer's <c>b</c> register and set, or (-1, -1) for none.</summary>
	public (int Slot, int Set) ConstantBuffer { get; init; }

	/// <summary>The vertex inputs it reads, by semantic (vertex shaders).</summary>
	public InputElement[] InputSignature { get; init; }

	/// <summary>Its fixed render state (pixel shaders); parts that are dynamic come from render-state variables.</summary>
	public ShaderRenderState RenderState { get; init; }
}

/// <summary>
/// One shader module (<c>CVfxShaderFileVulkan</c>): SPIR-V, the vertex attribute map, entry point naming and
/// specialization constants. A version 6 reference to a shared module is resolved when it's read.
/// </summary>
public sealed class ShaderBytecode
{
	/// <summary>The SPIR-V.</summary>
	public byte[] Spirv { get; private init; }

	/// <summary>
	/// Each vertex input location's semantic, <c>usage &lt;&lt; 4 | index</c> (<c>SemanticUsageVulkan_t</c>: position 0,
	/// blend weight 1, blend indices 2, normal 3, psize 4, texcoord 5, tangent 6, binormal 7, positiont 8, color 9).
	/// </summary>
	public byte[] AttributeMap { get; private init; }

	/// <summary>Entry points are named by stage (<c>MainVs</c>, <c>MainPs</c>...) rather than <c>main</c>.</summary>
	public bool StageEntryNames { get; private init; }

	/// <summary>Specialization constants to set when making a pipeline.</summary>
	public SpecializationConstant[] Constants { get; private init; }

	/// <summary>Compute thread group size.</summary>
	public (int X, int Y, int Z) ThreadGroupSize { get; private init; }

	/// <summary>A required subgroup size, or 0.</summary>
	public int RequiredSubgroupSize { get; private init; }

	/// <summary>The shared module a reference resolved to, so modules can be made once per shared module.</summary>
	public ShaderBytecode Shared { get; private set; }

	internal static ShaderBytecode Read( ShaderProgram program, byte[] bytes )
	{
		var version = BinaryPrimitives.ReadInt32LittleEndian( bytes );
		var reader = new ByteReader( bytes, 4 );

		if ( version == 6 )
		{
			var index = (int)reader.UInt();
			var count = (int)reader.UInt();
			if ( count > 64 || bytes.Length != 12 + count * 8 ) throw new InvalidDataException( "Malformed shared shader reference" );
			var constants = new SpecializationConstant[count];
			for ( int i = 0; i < count; i++ ) constants[i] = new SpecializationConstant( reader.UInt(), reader.UInt() );
			return program.SharedModule( index, constants );
		}

		if ( version is not (4 or 5) ) throw new InvalidDataException( $"Shader file version {version}" );

		var spirv = reader.Bytes( reader.Int() );
		var mapSize = reader.Int();
		var map = mapSize > 0 ? reader.Bytes( mapSize ) : [];

		// Reflection masks: SPIR-V reflection gives the same and more, so they're skipped
		reader.Skip( 2 + 2 + 2 + 8 + 2 + 16 + 16 + 2 + 2 + 16 );
		reader.Skip( reader.Short() * 4 );
		reader.Skip( reader.Short() * 2 );
		var threads = (reader.Int(), reader.Int(), reader.Int());
		reader.Skip( reader.Short() * 96 );
		var bits = reader.UShort();
		reader.UShort();
		reader.Skip( reader.Short() * 4 );
		reader.Skip( (int)reader.UInt() * 4 );
		var subgroup = reader.Short();

		SpecializationConstant[] own = [];
		if ( version == 5 )
		{
			own = new SpecializationConstant[(int)reader.UInt()];
			for ( int i = 0; i < own.Length; i++ ) own[i] = new SpecializationConstant( reader.UInt(), reader.UInt() );
		}

		var module = new ShaderBytecode
		{
			Spirv = spirv,
			AttributeMap = map,
			StageEntryNames = (bits & (1 << 12)) != 0,
			Constants = own,
			ThreadGroupSize = threads,
			RequiredSubgroupSize = subgroup,
		};

		module.Shared = module;
		return module;
	}

	/// <summary>SPIR-V compiled from a module, its entry points named by stage.</summary>
	internal static ShaderBytecode FromModule( ModuleStage stage )
	{
		var module = new ShaderBytecode
		{
			Spirv = stage.Spirv,
			AttributeMap = stage.AttributeMap ?? [],
			StageEntryNames = true,
			Constants = stage.Constants ?? [],
			ThreadGroupSize = stage.ThreadGroupSize,
		};

		module.Shared = module;
		return module;
	}

	ShaderBytecode With( SpecializationConstant[] constants ) => new()
	{
		Spirv = Spirv,
		AttributeMap = AttributeMap,
		StageEntryNames = StageEntryNames,
		Constants = constants,
		ThreadGroupSize = ThreadGroupSize,
		RequiredSubgroupSize = RequiredSubgroupSize,
		Shared = Shared,
	};

	internal ShaderBytecode WithConstants( SpecializationConstant[] constants ) => With( constants );

	/// <summary>The entry point for a stage.</summary>
	public string EntryPoint( ShaderProgramType stage ) => !StageEntryNames ? "main" : stage switch
	{
		ShaderProgramType.Vertex => "MainVs",
		ShaderProgramType.Pixel => "MainPs",
		ShaderProgramType.Geometry => "MainGs",
		ShaderProgramType.Hull => "MainHs",
		ShaderProgramType.Domain => "MainDs",
		ShaderProgramType.Compute => "MainCs",
		_ => "main",
	};
}
