using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Sandbox.Engine.Shaders;

/// <summary>
/// A source version of the shader. 
/// In the future we'll use this to pre-process the shader, work out what programs it contains, and pull out all of the meta data that we need. 
/// </summary>
class ShaderSource
{
	public string AbsolutePath { get; internal set; }
	public string RelativePath { get; internal set; }

	public List<ProgramSource> Programs { get; set; } = new();

	public bool IsOutOfDate { get; set; }

	public void Read()
	{
		var text = System.IO.File.ReadAllLines( AbsolutePath );
		ReadPrograms( text );

		// default them all to our of date
		foreach ( var p in Programs )
		{
			p.IsOutOfDate = true;
		}

		CheckOutOfDate();
	}

	/// <summary>
	/// Updates IsOutOfDate
	/// </summary>
	void CheckOutOfDate()
	{
		var vfx = new Shader();

		bool loaded = vfx.LoadFromCompiledUnlessOutOfDate( AbsolutePath );

		//
		// the compiled shader loaded, compare to see what needs recompiling
		//
		if ( loaded )
		{
			foreach ( var p in Programs )
			{
				var program = vfx.GetProgram( p.ProgramType );
				p.IsOutOfDate = !program.WasLoadedFromCompiled;
			}
		}

		// Needs a compile
		IsOutOfDate = Programs.Any( x => x.IsOutOfDate );
	}

	/// <summary>
	/// Go through each line and find the programs that we have
	/// </summary>
	void ReadPrograms( string[] lines )
	{
		Programs.Clear();

		foreach ( var line in lines )
		{
			var trimmed = line.Trim();

			if ( trimmed == "VS" ) AddProgram( ShaderProgramType.VFX_PROGRAM_VS );
			if ( trimmed == "PS" ) AddProgram( ShaderProgramType.VFX_PROGRAM_PS );
			if ( trimmed == "GS" ) AddProgram( ShaderProgramType.VFX_PROGRAM_GS );
			if ( trimmed == "HS" ) throw new System.Exception( "HS does nothing!" ); //  AddProgram( lines, ShaderProgram.VFX_PROGRAM_HS );
			if ( trimmed == "DS" ) throw new System.Exception( "DS does nothing!" ); //	AddProgram( lines, ShaderProgram.VFX_PROGRAM_DS );
			if ( trimmed == "CS" ) AddProgram( ShaderProgramType.VFX_PROGRAM_CS );
			if ( trimmed == "PS_RENDER_STATE" ) AddProgram( ShaderProgramType.VFX_PROGRAM_PS_RENDER_STATE );
			if ( trimmed == "RTX" ) AddProgram( ShaderProgramType.VFX_PROGRAM_RTX );
		}
	}

	/// <summary>
	/// Add a program to compile
	/// </summary>
	private void AddProgram( ShaderProgramType type )
	{
		var ss = new ProgramSource { ProgramType = type };

		if ( Programs.Any( s => s.ProgramType == type ) )
			throw new System.Exception( $"Program {type} defined twice!" );

		Programs.Add( ss );
	}

	/// <summary>
	/// Serialize to the correct format
	/// </summary>
	internal byte[] Serialize( Shader vfx, ShaderCompile.Results results, bool serializeSource )
	{
		ProgramHeader_t[] programHeaders = new ProgramHeader_t[(int)ShaderProgramType.VFX_PROGRAM_MAX];
		var headerSize = Marshal.SizeOf<ProgramHeader_t>() * (int)(ShaderProgramType.VFX_PROGRAM_MAX);

		using var body = ByteStream.Create( 1024 );

		uint offset = (uint)headerSize;

		for ( var nProgramType = ShaderProgramType.VFX_PROGRAM_FEATURE; nProgramType < ShaderProgramType.VFX_PROGRAM_MAX; nProgramType++ )
		{
			if ( !vfx.HasProgram( nProgramType ) )
				continue;


			using var programbody = ByteStream.Create( 1024 );

			//
			// Features really should not be a fucking program. They really fucked this format.
			//
			if ( nProgramType == ShaderProgramType.VFX_PROGRAM_FEATURE )
			{
				using var buffer = CUtlBuffer.Create();
				vfx.native.WriteProgramToBuffer( nProgramType, default, buffer );

				var data = buffer.ToArray();
				programbody.Write( data );
			}
			else
			{
				// a normal program
				var program = Programs.FirstOrDefault( x => x.ProgramType == nProgramType );
				if ( program is null )
				{
					throw new System.Exception( $"Program {nProgramType} not found. It really should have been found. We have a mismatch somewhere." );
				}

				var data = program.BuildCompiledShader( vfx );
				programbody.Write( data );
			}

			body.Write( programbody );

			//Console.WriteLine( $"{nProgramType} was {programbody.Length}" );

			programHeaders[(int)nProgramType].m_nOffset = offset;
			programHeaders[(int)nProgramType].m_nSize = (uint)programbody.Length;

			offset += (uint)programbody.Length;
		}

		using ByteStream index = ByteStream.Create( 32 );
		index.WriteArray( programHeaders, false );

		Assert.AreEqual( index.Length, headerSize );

		using var final = ByteStream.Create( 1024 );

		// spirv
		{
			final.Write<uint>( (uint)(index.Length + body.Length) );
			final.Write( index.ToArray() );
			final.Write( body.ToArray() );
		}

		// hlsl source
		if ( serializeSource )
		{
			var programSources = new Dictionary<string, string>();
			foreach ( var program in results.Programs )
			{
				programSources[program.Name] = program.Source;
			}

			var json = JsonSerializer.Serialize( new { Programs = programSources } );
			final.Write( json );
		}
		else
		{
			final.Write( 0 );
		}

		return final.ToArray();
	}

	// 'HLSL' little-endian. The resource compiler splits Serialize's payload into a SPRV block (the spirv blob)
	// and an HLSL block holding the embedded source JSON verbatim (only written for non-core shaders).
	const uint RESOURCE_BLOCK_ID_HLSL = 0x4C534C48;

	/// <summary>
	/// Reads the per-program HLSL source that <see cref="Serialize"/> embeds in an addon <c>.shader_c</c> (stored
	/// as JSON in the <c>HLSL</c> resource block - see the <c>serializeSource</c> path). Returns the source keyed
	/// by program name (e.g. <c>VFX_PROGRAM_VS</c>), or null when the shader has no embedded source (core shaders,
	/// which aren't published anyway, don't write an HLSL block).
	/// </summary>
	internal static Dictionary<string, string> ReadEmbeddedSource( string compiledFilePath )
	{
		var bytes = System.IO.File.ReadAllBytes( compiledFilePath );

		// The HLSL block is the source JSON verbatim - the block size is the length, no prefix.
		if ( !TryGetResourceBlock( bytes, RESOURCE_BLOCK_ID_HLSL, out var block ) || block.Length == 0 )
			return null;

		var json = System.Text.Encoding.UTF8.GetString( block );

		var doc = JsonSerializer.Deserialize<EmbeddedSource>( json );
		return doc?.Programs;
	}

	/// <summary>
	/// Pulls a single block's bytes out of a Source 2 resource container by its type id. Mirror of the layout
	/// written by <see cref="Sandbox.Resources.ResourceWriter"/>.
	/// </summary>
	static bool TryGetResourceBlock( byte[] file, uint typeId, out byte[] block )
	{
		block = null;

		// ResourceFileHeader_t: [0] uint nonStreamingSize, [4] ushort headerVersion, [6] ushort resourceVersion,
		// [8] int blocksOffset (relative to pos 8), [12] uint blockCount.
		if ( file.Length < 16 )
			return false;

		var blocksOffset = BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( 8 ) );
		var blockCount = BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( 12 ) );

		var entryPos = 8 + blocksOffset;

		for ( var i = 0; i < blockCount; i++ )
		{
			// ResourceBlockEntry_t (12 bytes): uint typeId, int dataOffset (relative to the pointer field), uint size.
			if ( entryPos + 12 > file.Length )
				return false;

			var entryTypeId = BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( entryPos ) );
			var pointerFieldPos = entryPos + 4;
			var dataOffset = BinaryPrimitives.ReadInt32LittleEndian( file.AsSpan( pointerFieldPos ) );
			var size = BinaryPrimitives.ReadUInt32LittleEndian( file.AsSpan( entryPos + 8 ) );

			if ( entryTypeId == typeId )
			{
				var dataPos = pointerFieldPos + dataOffset;
				if ( dataPos < 0 || dataPos + (long)size > file.Length )
					return false;

				block = file.AsSpan( dataPos, (int)size ).ToArray();
				return true;
			}

			entryPos += 12;
		}

		return false;
	}

	class EmbeddedSource
	{
		public Dictionary<string, string> Programs { get; set; }
	}
}

file struct ProgramHeader_t
{
	public uint m_nOffset;
	public uint m_nSize;
}
