using System.Buffers.Binary;
using System.Text;

namespace Sandbox.Resources;

/// <summary>
/// RERL (Resource External Reference List) support for ResourceWriter.
/// Tracks external resource references and serializes them as a RERL block.
/// </summary>
internal partial class ResourceWriter
{
	const uint RESOURCE_BLOCK_ID_RERL = 0x4C524552; // 'RERL'

	readonly record struct ExternalRef( ulong Id, string Path );
	readonly List<ExternalRef> _externalRefs = [];

	/// <summary>
	/// Register an external resource reference (e.g., a material path).
	/// These are written as a RERL block so the resource system can resolve dependencies.
	/// </summary>
	public void AddExternalReference( string resourcePath )
	{
		if ( string.IsNullOrEmpty( resourcePath ) )
			return;

		var normalized = Resource.FixPath( resourcePath );
		var id = MurmurHash64( normalized );

		foreach ( var existing in _externalRefs )
		{
			if ( existing.Id == id )
				return;
		}

		_externalRefs.Add( new ExternalRef( id, normalized ) );
	}

	/// <summary>
	/// Build the RERL block binary data.
	///
	/// Layout:
	///   ResourceExtRefList_t (8 bytes):
	///     int32  m_resourceRefInfoList.m_nOffset  (relative to this field)
	///     uint32 m_resourceRefInfoList.m_nCount
	///
	///   ResourceReferenceInfo_t[count] (16 bytes each):
	///     uint64 m_nId                            (8-byte aligned)
	///     int32  m_pResourceName.m_nOffset        (relative to this field)
	///     int32  padding
	///
	///   Null-terminated UTF-8 string data
	/// </summary>
	byte[] BuildRERLBlock()
	{
		int count = _externalRefs.Count;

		const int headerSize = 8;   // CResourceArray: int32 offset + uint32 count
		const int entrySize = 16;   // ResourceReferenceInfo_t: uint64 + int32 + int32 pad
		int entriesStart = headerSize;
		int stringsStart = entriesStart + count * entrySize;

		var stringBytes = new byte[count][];
		int totalStringSize = 0;
		for ( int i = 0; i < count; i++ )
		{
			stringBytes[i] = Encoding.UTF8.GetBytes( _externalRefs[i].Path );
			totalStringSize += stringBytes[i].Length + 1; // +1 null terminator
		}

		var data = new byte[stringsStart + totalStringSize];

		// CResourceArray header
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 0 ), entriesStart );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 4 ), (uint)count );

		// Entries + strings
		int stringOffset = stringsStart;
		for ( int i = 0; i < count; i++ )
		{
			int entryPos = entriesStart + i * entrySize;
			int nameFieldPos = entryPos + 8; // position of m_pResourceName field

			BinaryPrimitives.WriteUInt64LittleEndian( data.AsSpan( entryPos ), _externalRefs[i].Id );
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( nameFieldPos ), stringOffset - nameFieldPos );
			// [entryPos + 12..15] = padding, already zero

			Array.Copy( stringBytes[i], 0, data, stringOffset, stringBytes[i].Length );
			// null terminator already zero from array init
			stringOffset += stringBytes[i].Length + 1;
		}

		return data;
	}

	/// <summary>
	/// Computes the resource ID hash using the native MurmurHash64.
	/// Input should already be normalized (lowercase, forward slashes).
	/// </summary>
	static unsafe ulong MurmurHash64( string input )
	{
		var key = Encoding.UTF8.GetBytes( input );
		fixed ( byte* ptr = key )
		{
			return NativeLowLevel.MurmurHash64( (IntPtr)ptr, key.Length, 0xEDABCDEF );
		}
	}
}
