using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Sandbox.ShaderCompiler;

/// <summary>
/// What the compiler needs from SPIR-V itself: the descriptor variables a module really uses (Slang's reflection lists
/// every declared global, used or not) with their names, the vertex inputs it reads, and moving a pixel shader's
/// resources from set 0 to set 1 as vfx does, so the two stages' automatically numbered bindings never collide.
/// </summary>
internal static class Spirv
{
	const int OpName = 5, OpTypeImage = 25, OpTypeSampler = 26, OpTypeSampledImage = 27, OpTypeArray = 28, OpTypeRuntimeArray = 29;
	const int OpTypeStruct = 30, OpTypePointer = 32, OpConstant = 43, OpVariable = 59, OpDecorate = 71;
	const int DecorationBufferBlock = 3, DecorationBuiltIn = 11, DecorationLocation = 30, DecorationBinding = 33, DecorationDescriptorSet = 34;
	const int StorageUniformConstant = 0, StorageInput = 1, StorageUniform = 2, StoragePushConstant = 9, StorageBuffer = 12;

	/// <summary>A descriptor variable: name, set, binding, what it is, array count (0 unbounded), image shape.</summary>
	public readonly record struct Variable( string Name, int Set, int Binding, BindingKind Kind, int Count, TextureShape Shape );

	/// <summary>The module's descriptor variables and the locations of its stage inputs.</summary>
	public static (List<Variable> Variables, List<int> Inputs, bool PushConstants) Read( ReadOnlySpan<byte> spirv )
	{
		var words = Words( spirv );
		var names = new Dictionary<uint, string>();
		var types = new Dictionary<uint, (int Op, int Start)>();
		var constants = new Dictionary<uint, uint>();
		var decorations = new Dictionary<uint, Dictionary<int, uint>>();
		var variables = new List<(uint Id, uint Type, int Storage)>();

		for ( int i = 5; i < words.Length; )
		{
			var op = (int)(words[i] & 0xFFFF);
			var count = (int)(words[i] >> 16);
			if ( count == 0 || i + count > words.Length ) throw new InvalidDataException( "Malformed SPIR-V" );
			switch ( op )
			{
				case OpName:
					names[words[i + 1]] = ReadString( words, i + 2, i + count );
					break;
				case OpDecorate:
					if ( !decorations.TryGetValue( words[i + 1], out var set ) ) decorations[words[i + 1]] = set = new();
					set[(int)words[i + 2]] = count > 3 ? words[i + 3] : 1;
					break;
				case OpTypeImage or OpTypeSampler or OpTypeSampledImage or OpTypeArray or OpTypeRuntimeArray or OpTypeStruct or OpTypePointer:
					types[words[i + 1]] = (op, i + 2);
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

		var result = new List<Variable>();
		var inputs = new List<int>();
		var push = false;
		foreach ( var (id, pointerType, storage) in variables )
		{
			decorations.TryGetValue( id, out var decorated );
			if ( storage == StorageInput )
			{
				if ( decorated is not null && decorated.TryGetValue( DecorationLocation, out var location ) && !decorated.ContainsKey( DecorationBuiltIn ) ) inputs.Add( (int)location );
				continue;
			}

			if ( storage == StoragePushConstant )
			{
				push = true;
				continue;
			}

			if ( decorated is null || !decorated.TryGetValue( DecorationDescriptorSet, out var descriptorSet ) || !decorated.TryGetValue( DecorationBinding, out var binding ) ) continue;
			if ( storage != StorageUniformConstant && storage != StorageUniform && storage != StorageBuffer ) continue;

			var element = words[types[pointerType].Start + 1];
			var arrayCount = 1;
			if ( types.TryGetValue( element, out var t ) && t.Op is OpTypeArray or OpTypeRuntimeArray )
			{
				arrayCount = t.Op == OpTypeArray ? (int)constants.GetValueOrDefault( words[t.Start + 1] ) : 0;
				element = words[t.Start];
			}

			var (elementOp, elementStart) = types.TryGetValue( element, out var e ) ? e : (0, 0);
			var kind = BindingKind.Texture;
			var shape = TextureShape.None;
			switch ( elementOp )
			{
				case OpTypeStruct:
					decorations.TryGetValue( element, out var structDecorations );
					kind = storage == StorageBuffer || (structDecorations?.ContainsKey( DecorationBufferBlock ) ?? false) ? BindingKind.Buffer : BindingKind.ConstantBuffer;
					break;
				case OpTypeImage:
					var dim = words[elementStart + 1];
					var arrayed = words[elementStart + 3] != 0;
					shape = dim switch
					{
						2 => TextureShape.Texture3D,
						3 => arrayed ? TextureShape.CubeArray : TextureShape.Cube,
						_ => arrayed ? TextureShape.Texture2DArray : TextureShape.Texture2D,
					};
					kind = words[elementStart + 5] == 2 ? BindingKind.StorageTexture : BindingKind.Texture;
					break;
				case OpTypeSampler:
					kind = BindingKind.Sampler;
					break;
				default:
					throw new InvalidDataException( $"SPIR-V binding {descriptorSet}.{binding} of a type the compiler doesn't describe (op {elementOp})" );
			}

			// Aliases (a byte-address buffer read through two views, the bindless arrays) share a binding: keep the first
			if ( result.Exists( v => v.Set == (int)descriptorSet && v.Binding == (int)binding ) ) continue;
			result.Add( new Variable( names.GetValueOrDefault( id ) ?? "", (int)descriptorSet, (int)binding, kind, arrayCount, shape ) );
		}

		result.Sort( ( a, b ) => a.Set != b.Set ? a.Set.CompareTo( b.Set ) : a.Binding.CompareTo( b.Binding ) );
		inputs.Sort();
		return (result, inputs, push);
	}

	/// <summary>Move every descriptor in set <paramref name="from"/> to set <paramref name="to"/>.</summary>
	public static byte[] MoveSet( byte[] spirv, int from, int to )
	{
		var copy = (byte[])spirv.Clone();
		var words = Words( copy );
		for ( int i = 5; i < words.Length; )
		{
			var op = (int)(words[i] & 0xFFFF);
			var count = (int)(words[i] >> 16);
			if ( op == OpDecorate && count >= 4 && words[i + 2] == DecorationDescriptorSet && words[i + 3] == from )
				BinaryPrimitives.WriteUInt32LittleEndian( copy.AsSpan( (i + 3) * 4 ), (uint)to );
			i += count;
		}

		return copy;
	}

	static uint[] Words( ReadOnlySpan<byte> spirv )
	{
		if ( spirv.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian( spirv ) != 0x07230203 ) throw new InvalidDataException( "Not SPIR-V" );
		var words = new uint[spirv.Length / 4];
		for ( int i = 0; i < words.Length; i++ ) words[i] = BinaryPrimitives.ReadUInt32LittleEndian( spirv[(i * 4)..] );
		return words;
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
