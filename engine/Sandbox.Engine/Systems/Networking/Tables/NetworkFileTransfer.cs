using System.IO;
using System.Threading;

namespace Sandbox;

/// <summary>File parts are separate messages, each bounded before compression and packetization.</summary>
internal static class NetworkFileTransfer
{
	internal const int PartSize = 4 * 1024 * 1024;

	internal static bool IsValidRange( long size, long offset, int length ) =>
		size >= 0 && offset >= 0 && offset <= size && length > 0 && length <= PartSize && length <= size - offset;

	internal static async Task<byte[]> ReadPartAsync( Stream stream, long offset, int length )
	{
		if ( !IsValidRange( stream.Length, offset, length ) )
			throw new InvalidDataException( "Invalid network file range" );

		stream.Seek( offset, SeekOrigin.Begin );
		var data = new byte[length];
		await stream.ReadExactlyAsync( data );
		return data;
	}

	internal static async Task DownloadAsync( Stream output, long size, ulong crc,
		Func<long, int, CancellationToken, Task<byte[]>> request, CancellationToken token )
	{
		if ( size < 0 ) throw new InvalidDataException( "Invalid network file size" );
		for ( long offset = 0; offset < size; )
		{
			token.ThrowIfCancellationRequested();
			var length = (int)Math.Min( PartSize, size - offset );
			var data = await request( offset, length, token );
			if ( data is null || data.Length != length )
				throw new InvalidDataException( "Incomplete network file part" );

			await output.WriteAsync( data, token );
			offset += data.Length;
		}

		token.ThrowIfCancellationRequested();
		if ( output.Length != size ) throw new InvalidDataException( "Network file size mismatch" );
		output.Position = 0;
		if ( await Utility.Crc64.FromStreamAsync( output ) != crc )
			throw new InvalidDataException( "Network file CRC mismatch" );
		token.ThrowIfCancellationRequested();
	}
}
