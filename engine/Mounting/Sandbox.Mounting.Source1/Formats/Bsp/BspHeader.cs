using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class BspHeader
{
	internal const int Size = 8 + 64 * 16 + 4;
	internal long Start { get; }
	internal (int offset, int length, int version, int uncompressedSize)[] Lumps { get; } = new (int, int, int, int)[64];

	internal BspHeader( Stream stream )
	{
		if ( !stream.CanRead || !stream.CanSeek )
			throw new ArgumentException( "BSP loading requires a readable, seekable stream.", nameof( stream ) );
		Start = stream.Position;
		var remaining = stream.Length - Start;
		if ( remaining < Size )
			throw new InvalidDataException( "Invalid Source BSP file size." );
		Span<byte> header = stackalloc byte[Size];
		stream.ReadExactly( header );
		if ( BitConverter.ToInt32( header[..4] ) != 0x50534256 )
			throw new InvalidDataException( "Expected a little-endian VBSP file." );
		var version = BitConverter.ToInt32( header.Slice( 4, 4 ) );
		if ( version is < 19 or > 21 )
			throw new NotSupportedException( $"Source BSP version {version} is not supported (expected 19-21)." );
		for ( var i = 0; i < Lumps.Length; i++ )
		{
			var data = header.Slice( 8 + i * 16, 16 );
			var lump = (offset: BitConverter.ToInt32( data[..4] ), length: BitConverter.ToInt32( data.Slice( 4, 4 ) ),
				version: BitConverter.ToInt32( data.Slice( 8, 4 ) ), uncompressedSize: BitConverter.ToInt32( data.Slice( 12, 4 ) ));
			BspFile.ValidateRange( lump.offset, lump.length, remaining, $"BSP lump {i}" );
			if ( lump.length > 0 && lump.offset < Size )
				throw new InvalidDataException( $"BSP lump {i} overlaps the header." );
			Lumps[i] = lump;
		}
	}
}
