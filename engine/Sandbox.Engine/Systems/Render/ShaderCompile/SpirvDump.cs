using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Engine.Shaders;

/// <summary>
/// Writes the SPIR-V the driver would see for every combo of a program, for offline tools such as
/// <c>SboxBuild shader-stats</c>. Modules are deduplicated by content into <c>spirv/{hash}.spv</c>,
/// and <c>programs/{shader}.{program}.json</c> maps each combo onto one.
/// </summary>
static class SpirvDump
{
	// CVfxShaderFileVulkan: SHADER_FILE_VERSION, or SHADER_FILE_VERSION_SPECIALIZATION when a
	// (constant_id, value) list for the combo's specialization combos is appended at the end
	const int Version = 4;
	const int VersionSpecialization = 5;

	public record struct Combo( ulong Static, ulong Dynamic, string Name, string Spirv );
	public record class Program( string Shader, string Stage, List<Combo> Combos );

	internal static void Write( string root, string shaderPath, Shader vfx, ShaderProgramType programType, IEnumerable<CompiledCombo> combos )
	{
		var spirvDir = Path.Combine( root, "spirv" );
		var programDir = Path.Combine( root, "programs" );
		Directory.CreateDirectory( spirvDir );
		Directory.CreateDirectory( programDir );

		var stage = programType.ToString().Replace( "VFX_PROGRAM_", "" );
		var program = new Program( shaderPath.Replace( '\\', '/' ), stage, new() );

		foreach ( var combo in combos.OrderBy( x => x.StaticCombo ).ThenBy( x => x.DynamicCombo ) )
		{
			var spirv = GetSpirv( combo.GetResult().GetCompiledShader().ToArray() );

			// No entry point for this stage, or a program like PS_RENDER_STATE that has no bytecode
			if ( spirv is null )
				continue;

			var hash = Convert.ToHexString( SHA256.HashData( spirv ), 0, 16 ).ToLowerInvariant();
			var file = Path.Combine( spirvDir, hash + ".spv" );
			if ( !File.Exists( file ) )
				File.WriteAllBytes( file, spirv );

			var name = vfx.native.DescribeCombo( programType, combo.StaticCombo, combo.DynamicCombo );
			program.Combos.Add( new Combo( combo.StaticCombo, combo.DynamicCombo, name, hash ) );
		}

		if ( program.Combos.Count == 0 )
			return;

		var fileName = program.Shader.Replace( '/', '_' ).Replace( ':', '_' ) + "." + stage + ".json";
		File.WriteAllText( Path.Combine( programDir, fileName ), JsonSerializer.Serialize( program ) );
	}

	/// <summary>
	/// The SPIR-V from a compiled combo blob, with specialization constants baked into their defaults so the
	/// module is what the driver compiles for this combo rather than the shared canonical one.
	/// </summary>
	internal static byte[] GetSpirv( byte[] blob )
	{
		if ( blob.Length < 8 )
			return null;

		var version = BinaryPrimitives.ReadInt32LittleEndian( blob );
		var size = BinaryPrimitives.ReadInt32LittleEndian( blob.AsSpan( 4 ) );
		if ( version != Version && version != VersionSpecialization )
			throw new InvalidDataException( $"Unknown compiled shader version {version}" );
		if ( size <= 0 )
			return null;

		var spirv = blob.AsSpan( 8, size ).ToArray();
		if ( version == VersionSpecialization )
			Specialize( spirv, ReadSpecializationConstants( blob, 8 + size ) );

		return spirv;
	}

	/// <summary>
	/// The list sits at the end of the blob after variable length reflection: uint count, then count
	/// (id, value) pairs whose ids run 0..count-1. Find the count that makes that hold.
	/// </summary>
	static Dictionary<uint, uint> ReadSpecializationConstants( byte[] blob, int start )
	{
		uint U32( int offset ) => BinaryPrimitives.ReadUInt32LittleEndian( blob.AsSpan( offset ) );

		for ( int count = 1; count <= 64; count++ )
		{
			var offset = blob.Length - 4 - count * 8;
			if ( offset < start ) break;
			if ( U32( offset ) != count ) continue;

			bool sequential = true;
			for ( int i = 0; i < count && sequential; i++ )
				sequential = U32( offset + 4 + i * 8 ) == i;
			if ( !sequential ) continue;

			var constants = new Dictionary<uint, uint>();
			for ( int i = 0; i < count; i++ )
				constants[(uint)i] = U32( offset + 8 + i * 8 );
			return constants;
		}

		throw new InvalidDataException( "Compiled shader has no readable specialization constant list" );
	}

	/// <summary>
	/// Rewrite the default of every OpSpecConstant decorated SpecId n with constants[n].
	/// </summary>
	static void Specialize( byte[] spirv, Dictionary<uint, uint> constants )
	{
		const int OpDecorate = 71, DecorationSpecId = 1;
		const int OpSpecConstantTrue = 48, OpSpecConstantFalse = 49, OpSpecConstant = 50;

		var words = MemoryMarshal.Cast<byte, uint>( spirv.AsSpan() );
		var specIds = new Dictionary<uint, uint>(); // result id -> SpecId

		for ( int pass = 0; pass < 2; pass++ )
		{
			for ( int i = 5; i < words.Length; )
			{
				var count = (int)(words[i] >> 16);
				var opcode = (int)(words[i] & 0xffff);
				if ( count == 0 ) throw new InvalidDataException( "Malformed SPIR-V" );

				if ( pass == 0 && opcode == OpDecorate && count >= 4 && words[i + 2] == DecorationSpecId )
				{
					specIds[words[i + 1]] = words[i + 3];
				}
				else if ( pass == 1 && opcode is OpSpecConstant or OpSpecConstantTrue or OpSpecConstantFalse
					&& specIds.TryGetValue( words[i + 2], out var specId ) && constants.TryGetValue( specId, out var value ) )
				{
					if ( opcode == OpSpecConstant ) words[i + 3] = value;
					else words[i] = (words[i] & 0xffff0000) | (uint)(value != 0 ? OpSpecConstantTrue : OpSpecConstantFalse);
				}

				i += count;
			}
		}
	}
}
