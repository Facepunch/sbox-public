using Borealis.Gpu;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Borealis.Source2.Shaders;

/// <summary>
/// What a SPIR-V module binds and reads, from its decorations and types: descriptor bindings, vertex inputs, entry
/// points and specialization constant ids. vfx strips debug names, so this is about shapes, not names; the VCS tables
/// say what each binding is for.
/// </summary>
public sealed class SpirvReflection
{
	/// <summary>Descriptor bindings, by set then binding.</summary>
	public IReadOnlyList<SpirvBinding> Bindings { get; }

	/// <summary>Stage inputs with a location (vertex inputs, for a vertex shader).</summary>
	public IReadOnlyList<SpirvInput> Inputs { get; }

	/// <summary>Entry point names.</summary>
	public IReadOnlyList<string> EntryPoints { get; }

	/// <summary>Specialization constant ids it declares.</summary>
	public IReadOnlyList<uint> SpecializationIds { get; }

	/// <summary>
	/// Uniform buffers' members as compiled: offsets and sizes always, names where the module keeps them (vfx strips them,
	/// Slang keeps them). A compiler may drop members nothing reads.
	/// </summary>
	public IReadOnlyList<SpirvBlock> Blocks { get; }

	SpirvReflection( List<SpirvBinding> bindings, List<SpirvInput> inputs, List<string> entryPoints, List<uint> specialization, List<SpirvBlock> blocks )
	{
		Bindings = bindings;
		Inputs = inputs;
		EntryPoints = entryPoints;
		SpecializationIds = specialization;
		Blocks = blocks;
	}

	const int OpEntryPoint = 15, OpTypeVoid = 19, OpTypeBool = 20, OpTypeInt = 21, OpTypeFloat = 22, OpTypeVector = 23, OpTypeMatrix = 24;
	const int OpTypeImage = 25, OpTypeSampler = 26, OpTypeSampledImage = 27, OpTypeArray = 28, OpTypeRuntimeArray = 29, OpTypeStruct = 30;
	const int OpTypePointer = 32, OpConstant = 43, OpVariable = 59, OpDecorate = 71, OpMemberDecorate = 72, OpName = 5, OpMemberName = 6;
	const int DecorationSpecId = 1, DecorationBlock = 2, DecorationBufferBlock = 3, DecorationArrayStride = 6, DecorationMatrixStride = 7, DecorationBuiltIn = 11, DecorationLocation = 30;
	const int DecorationOffset = 35;
	const int DecorationBinding = 33, DecorationDescriptorSet = 34;
	const int StorageUniformConstant = 0, StorageInput = 1, StorageUniform = 2, StorageBuffer = 12;

	/// <summary>Reflect a SPIR-V module.</summary>
	public static SpirvReflection Read( ReadOnlySpan<byte> spirv )
	{
		if ( spirv.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian( spirv ) != 0x07230203 ) throw new InvalidDataException( "Not SPIR-V" );

		var words = new uint[spirv.Length / 4];
		for ( int i = 0; i < words.Length; i++ ) words[i] = BinaryPrimitives.ReadUInt32LittleEndian( spirv[(i * 4)..] );

		var types = new Dictionary<uint, (int Op, int Start, int Count)>();
		var constants = new Dictionary<uint, uint>();
		var decorations = new Dictionary<uint, Dictionary<int, uint>>();
		var variables = new List<(uint Id, uint Type, int Storage)>();
		var entryPoints = new List<string>();
		var specialization = new List<uint>();
		var names = new Dictionary<uint, string>();
		var memberNames = new Dictionary<(uint, int), string>();
		var memberOffsets = new Dictionary<(uint, int), uint>();
		var matrixStrides = new Dictionary<(uint, int), uint>();

		for ( int i = 5; i < words.Length; )
		{
			var op = (int)(words[i] & 0xFFFF);
			var count = (int)(words[i] >> 16);
			if ( count == 0 || i + count > words.Length ) throw new InvalidDataException( "Malformed SPIR-V instruction" );

			switch ( op )
			{
				case OpEntryPoint:
					entryPoints.Add( ReadString( words, i + 3, i + count ) );
					break;
				case OpName:
					names[words[i + 1]] = ReadString( words, i + 2, i + count );
					break;
				case OpMemberName:
					memberNames[(words[i + 1], (int)words[i + 2])] = ReadString( words, i + 3, i + count );
					break;
				case OpMemberDecorate when count > 4 && words[i + 3] == DecorationOffset:
					memberOffsets[(words[i + 1], (int)words[i + 2])] = words[i + 4];
					break;
				case OpMemberDecorate when count > 4 && words[i + 3] == DecorationMatrixStride:
					matrixStrides[(words[i + 1], (int)words[i + 2])] = words[i + 4];
					break;
				case OpDecorate:
					var target = words[i + 1];
					var decoration = (int)words[i + 2];
					if ( !decorations.TryGetValue( target, out var set ) ) decorations[target] = set = new();
					set[decoration] = count > 3 ? words[i + 3] : 1;
					if ( decoration == DecorationSpecId ) specialization.Add( words[i + 3] );
					break;
				case OpTypeVoid or OpTypeBool or OpTypeInt or OpTypeFloat or OpTypeVector or OpTypeMatrix or OpTypeImage or OpTypeSampler
					or OpTypeSampledImage or OpTypeArray or OpTypeRuntimeArray or OpTypeStruct or OpTypePointer:
					types[words[i + 1]] = (op, i + 2, count - 2);
					break;
				case OpConstant:
					constants[words[i + 2]] = words[i + 3];
					break;
				case OpVariable:
					variables.Add( (words[i + 2], words[i + 1], (int)words[i + 3]) );
					break;
			}

			i += count;
		}

		var bindings = new List<SpirvBinding>();
		var inputs = new List<SpirvInput>();
		var blocks = new List<SpirvBlock>();
		foreach ( var (id, pointerType, storage) in variables )
		{
			decorations.TryGetValue( id, out var decorated );
			var pointee = words[types[pointerType].Start + 1];

			if ( storage == StorageInput )
			{
				if ( decorated is null || !decorated.TryGetValue( DecorationLocation, out var location ) || decorated.ContainsKey( DecorationBuiltIn ) ) continue;
				inputs.Add( Input( (int)location, pointee, types, words ) );
				continue;
			}

			if ( decorated is null || !decorated.TryGetValue( DecorationDescriptorSet, out var descriptorSet ) || !decorated.TryGetValue( DecorationBinding, out var binding ) ) continue;

			// Arrays: a length, or 0 for a runtime (bindless) array
			var arrayCount = 1;
			var element = pointee;
			var (elementOp, elementStart, _) = types[element];
			if ( elementOp is OpTypeArray or OpTypeRuntimeArray )
			{
				arrayCount = elementOp == OpTypeArray ? (int)constants.GetValueOrDefault( words[elementStart + 1] ) : 0;
				element = words[elementStart];
				(elementOp, elementStart, _) = types[element];
			}

			BindingType type;
			var dimension = SpirvImageDimension.None;
			switch ( elementOp )
			{
				case OpTypeStruct:
					decorations.TryGetValue( element, out var structDecorations );
					type = storage == StorageBuffer || (structDecorations?.ContainsKey( DecorationBufferBlock ) ?? false) ? BindingType.StorageBuffer : BindingType.UniformBuffer;
					break;
				case OpTypeImage:
					dimension = (SpirvImageDimension)words[elementStart + 1];
					var arrayed = words[elementStart + 3] != 0;
					if ( arrayed ) dimension = dimension == SpirvImageDimension.Cube ? SpirvImageDimension.CubeArray : dimension == SpirvImageDimension.Texture2D ? SpirvImageDimension.Texture2DArray : dimension;

					// Texture2DMS: only a multisampled texture binds there, and only there
					else if ( dimension == SpirvImageDimension.Texture2D && words[elementStart + 4] != 0 ) dimension = SpirvImageDimension.Texture2DMS;
					type = words[elementStart + 5] == 2 ? BindingType.StorageTexture : BindingType.SampledTexture;
					break;
				case OpTypeSampler:
					type = BindingType.Sampler;
					break;
				default:
					throw new InvalidDataException( $"SPIR-V binding {descriptorSet}.{binding} of a type Borealis.Gpu can't bind (op {elementOp})" );
			}

			if ( storage != StorageUniformConstant && storage != StorageUniform && storage != StorageBuffer ) continue;
			bindings.Add( new SpirvBinding( (int)descriptorSet, (int)binding, type, arrayCount, dimension ) );

			// A uniform buffer's members, in declaration order
			if ( type == BindingType.UniformBuffer && elementOp == OpTypeStruct )
			{
				var memberCount = types[element].Count - 1;
				var members = new SpirvMember[memberCount];
				for ( int m = 0; m < memberCount; m++ )
				{
					var memberType = words[elementStart + m];
					var size = SizeOf( memberType, types, words, constants, decorations, matrixStrides.GetValueOrDefault( (element, m) ) );
					members[m] = new SpirvMember( memberNames.GetValueOrDefault( (element, m) ), (int)memberOffsets.GetValueOrDefault( (element, m) ), size );
				}

				blocks.Add( new SpirvBlock( (int)descriptorSet, (int)binding, names.GetValueOrDefault( element ) ?? names.GetValueOrDefault( id ), members ) );
			}
		}

		bindings.Sort( ( a, b ) => a.Set != b.Set ? a.Set.CompareTo( b.Set ) : a.Binding.CompareTo( b.Binding ) );
		inputs.Sort( ( a, b ) => a.Location.CompareTo( b.Location ) );
		blocks.Sort( ( a, b ) => a.Set != b.Set ? a.Set.CompareTo( b.Set ) : a.Binding.CompareTo( b.Binding ) );
		return new SpirvReflection( bindings, inputs, entryPoints, specialization, blocks );
	}

	/// <summary>A member type's size in bytes as laid out: scalars by width, vectors by count, matrices by stride, arrays by stride; 0 if unknown.</summary>
	static int SizeOf( uint type, Dictionary<uint, (int Op, int Start, int Count)> types, uint[] words, Dictionary<uint, uint> constants, Dictionary<uint, Dictionary<int, uint>> decorations, uint matrixStride )
	{
		if ( !types.TryGetValue( type, out var t ) ) return 0;
		switch ( t.Op )
		{
			case OpTypeBool:
				return 4;
			case OpTypeInt or OpTypeFloat:
				return (int)words[t.Start] / 8;
			case OpTypeVector:
				return SizeOf( words[t.Start], types, words, constants, decorations, 0 ) * (int)words[t.Start + 1];
			case OpTypeMatrix:
				var columns = (int)words[t.Start + 1];
				return matrixStride != 0 ? (int)matrixStride * columns : SizeOf( words[t.Start], types, words, constants, decorations, 0 ) * columns;
			case OpTypeArray:
				var length = (int)constants.GetValueOrDefault( words[t.Start + 1] );
				var stride = decorations.TryGetValue( type, out var arrayDecorations ) && arrayDecorations.TryGetValue( DecorationArrayStride, out var s ) ? (int)s : SizeOf( words[t.Start], types, words, constants, decorations, matrixStride );
				return stride * length;
			default:
				return 0;
		}
	}

	static SpirvInput Input( int location, uint type, Dictionary<uint, (int Op, int Start, int Count)> types, uint[] words )
	{
		var (op, start, _) = types[type];
		var components = 1;
		if ( op == OpTypeVector )
		{
			components = (int)words[start + 1];
			(op, start, _) = types[words[start]];
		}

		var kind = op switch
		{
			OpTypeFloat => SpirvScalar.Float,
			OpTypeInt => words[start + 1] != 0 ? SpirvScalar.Int : SpirvScalar.UInt,
			_ => SpirvScalar.Float,
		};

		return new SpirvInput( location, kind, components );
	}

	static string ReadString( uint[] words, int start, int end )
	{
		var bytes = new List<byte>();
		for ( int i = start; i < end; i++ )
		{
			for ( int b = 0; b < 4; b++ )
			{
				var c = (byte)(words[i] >> (8 * b));
				if ( c == 0 ) return Encoding.UTF8.GetString( bytes.ToArray() );
				bytes.Add( c );
			}
		}

		return Encoding.UTF8.GetString( bytes.ToArray() );
	}
}

/// <summary>
/// A descriptor binding: set, binding, what it is and how many (0 for a runtime array).
/// </summary>
public readonly record struct SpirvBinding( int Set, int Binding, BindingType Type, int Count, SpirvImageDimension Dimension );

/// <summary>
/// A uniform buffer: its set, binding, name (null where stripped) and members.
/// </summary>
public sealed record SpirvBlock( int Set, int Binding, string Name, SpirvMember[] Members );

/// <summary>
/// A uniform buffer member: its name (null where stripped), byte offset and size.
/// </summary>
public readonly record struct SpirvMember( string Name, int Offset, int Size );

/// <summary>
/// A stage input: its location, component type and count.
/// </summary>
public readonly record struct SpirvInput( int Location, SpirvScalar Kind, int Components );

/// <summary>
/// A stage input's component type.
/// </summary>
public enum SpirvScalar
{
	/// <summary>32-bit float.</summary>
	Float,
	/// <summary>32-bit signed integer.</summary>
	Int,
	/// <summary>32-bit unsigned integer.</summary>
	UInt,
}

/// <summary>
/// An image binding's shape (SPIR-V's <c>Dim</c>, with arrayed 2D and cube folded in).
/// </summary>
public enum SpirvImageDimension
{
#pragma warning disable CS1591
	Texture1D = 0,
	Texture2D = 1,
	Texture3D = 2,
	Cube = 3,
	Rect = 4,
	Buffer = 5,
	SubpassData = 6,
	Texture2DArray = 100,
	CubeArray = 101,
	Texture2DMS = 102,
	None = -1,
#pragma warning restore CS1591
}
