using System;
using System.IO;
using static Sandbox.Mounting.Source1.BspGeometry;

namespace Sandbox.Mounting.Source1;

internal partial class BspFile
{
	internal static byte[] DecodeLump( byte[] data, int? expectedSize = null )
	{
		const int headerSize = 17;
		if ( data.Length < headerSize || BitConverter.ToUInt32( data, 0 ) != 0x414D5A4C )
			throw new InvalidDataException( "Invalid Source BSP LZMA header." );
		uint actualSize = BitConverter.ToUInt32( data, 4 );
		uint compressedSize = BitConverter.ToUInt32( data, 8 );
		if ( actualSize > Array.MaxLength || expectedSize < 0 || (expectedSize.HasValue && actualSize != expectedSize.Value) )
			throw new InvalidDataException( "BSP LZMA uncompressed size does not match its lump." );
		if ( compressedSize != data.Length - headerSize || compressedSize < 5 )
			throw new InvalidDataException( "BSP LZMA payload length does not match its header." );
		return LzmaDecoder.Decode( data, headerSize, (int)actualSize, data.AsSpan( 12, 5 ) );
	}
}
