namespace Sandbox.Resources;

/// <summary>
/// Writes the Source 2 resource container format.
///
/// File layout:
/// - ResourceFileHeader_t (16 bytes)
/// - ResourceBlockEntry_t[] (12 bytes each)
/// - Block data (DATA block, etc.)
/// - Streaming data (texture bits, etc.)
///
/// All offsets in CResourcePointer/CResourceArray are relative to the field's own position.
/// </summary>
internal class ResourceWriter
{
	const ushort RESOURCE_FILE_HEADER_VERSION = 12;
	const uint RESOURCE_BLOCK_ID_DATA = 0x41544144; // 'DATA'

	public ushort ResourceVersion { get; set; } = 0;

	byte[] _dataBlock;
	byte[] _streamingData;

	/// <summary>
	/// Set the DATA block content (e.g., VTEX_Header_t for textures)
	/// </summary>
	public void SetDataBlock( byte[] data )
	{
		_dataBlock = data;
	}

	/// <summary>
	/// Set the streaming data (e.g., texture bits)
	/// </summary>
	public void SetStreamingData( byte[] data )
	{
		_streamingData = data;
	}

	/// <summary>
	/// Write the complete resource file
	/// </summary>
	public byte[] ToArray()
	{
		int blockCount = _dataBlock != null ? 1 : 0;
		int dataBlockSize = _dataBlock?.Length ?? 0;
		int streamingSize = _streamingData?.Length ?? 0;

		//
		// Layout (with 1 DATA block):
		// 0:  ResourceFileHeader_t (16 bytes)
		//     0:  uint32 m_nNonStreamingSize
		//     4:  uint16 m_nHeaderVersion
		//     6:  uint16 m_nResourceVersion
		//     8:  int32  m_ResourceBlocks.m_nOffset  -> points to 16
		//     12: uint32 m_ResourceBlocks.m_nCount
		// 16: ResourceBlockEntry_t[0] - DATA block (12 bytes)
		//     16: uint32 m_nBlockType ('DATA')
		//     20: int32  m_pBlockData.m_nOffset -> points to 28
		//     24: uint32 m_nBlockSize
		// 28: Block data (VTEX_Header_t, etc.)
		// ??: Streaming data
		//

		const int headerSize = 16;
		const int blockEntrySize = 12;

		int blockArrayPos = headerSize;                                    // 16
		int dataBlockPos = blockArrayPos + (blockEntrySize * blockCount);  // 28
		int nonStreamingSize = dataBlockPos + dataBlockSize;

		using var buffer = ByteStream.Create( nonStreamingSize + streamingSize );

		// ===== ResourceFileHeader_t (16 bytes) =====
		buffer.Write( (uint)nonStreamingSize );               // offset 0: m_nNonStreamingSize
		buffer.Write( RESOURCE_FILE_HEADER_VERSION );         // offset 4: m_nHeaderVersion
		buffer.Write( ResourceVersion );                      // offset 6: m_nResourceVersion

		// CResourceArray<ResourceBlockEntry_t> m_ResourceBlocks
		if ( blockCount > 0 )
		{
			// offset 8: m_nOffset - relative from position 8, pointing to position 16
			buffer.Write( (int)(blockArrayPos - 8) );         // = 8
			buffer.Write( (uint)blockCount );                 // offset 12: m_nCount
		}
		else
		{
			buffer.Write( (int)0 );
			buffer.Write( (uint)0 );
		}

		// ===== ResourceBlockEntry_t[0] - DATA block (12 bytes) =====
		if ( _dataBlock != null )
		{
			buffer.Write( RESOURCE_BLOCK_ID_DATA );           // offset 16: m_nBlockType = 'DATA'
															  // offset 20: m_pBlockData.m_nOffset - relative from position 20, pointing to position 28
			buffer.Write( (int)(dataBlockPos - 20) );         // = 8
			buffer.Write( (uint)dataBlockSize );              // offset 24: m_nBlockSize
		}

		// ===== Block data =====
		if ( _dataBlock != null )
		{
			buffer.Write( _dataBlock );                       // offset 28: DATA block content
		}

		// ===== Streaming data =====
		if ( _streamingData != null )
		{
			buffer.Write( _streamingData );
		}

		return buffer.ToArray();
	}
}
