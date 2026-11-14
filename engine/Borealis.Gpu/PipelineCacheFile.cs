using System.Buffers.Binary;
using System.IO;

namespace Borealis.Gpu;

/// <summary>
/// The device's pipeline cache on disk (<see cref="DeviceOptions.PipelineCacheFolder"/>): a file per GPU, driver and
/// pipeline cache UUID, so data from another GPU or driver is never offered; a header with the payload's length and
/// checksum, so a torn or corrupt file loads as nothing; written to a temporary file and renamed over the old one, so a
/// crash mid-write leaves the last good file.
/// </summary>
internal static class PipelineCacheFile
{
	const uint Magic = 0x43505242; // "BRPC"
	const uint Version = 1;
	const int HeaderSize = 20;

	/// <summary>The most it writes: a cache grown past this isn't saved.</summary>
	public const int MaxBytes = 256 << 20;

	/// <summary>The file for a device: <c>vendor-device-driver-uuid.bin</c> in <paramref name="folder"/>.</summary>
	public static string PathFor( string folder, uint vendor, uint device, uint driver, ReadOnlySpan<byte> uuid ) => Path.Combine( folder, $"{vendor:x4}-{device:x4}-{driver:x8}-{Convert.ToHexStringLower( uuid )}.bin" );

	/// <summary>The cache data saved at <paramref name="path"/>, or null when there's none or it's damaged.</summary>
	public static byte[] Load( string path )
	{
		try
		{
			if ( !File.Exists( path ) ) return null;
			var bytes = File.ReadAllBytes( path );
			if ( bytes.Length < HeaderSize ) return null;

			var header = bytes.AsSpan( 0, HeaderSize );
			if ( BinaryPrimitives.ReadUInt32LittleEndian( header ) != Magic || BinaryPrimitives.ReadUInt32LittleEndian( header[4..] ) != Version ) return null;
			if ( BinaryPrimitives.ReadUInt32LittleEndian( header[8..] ) != bytes.Length - HeaderSize ) return null;

			var payload = bytes.AsSpan( HeaderSize );
			return Checksum( payload ) == BinaryPrimitives.ReadUInt64LittleEndian( header[12..] ) ? payload.ToArray() : null;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			return null;
		}
	}

	/// <summary>Write <paramref name="data"/> to <paramref name="path"/> through a temporary file; false if it couldn't.</summary>
	public static bool Save( string path, ReadOnlySpan<byte> data )
	{
		if ( data.IsEmpty || data.Length > MaxBytes ) return false;

		var temporary = $"{path}.{Environment.ProcessId}.tmp";
		try
		{
			Directory.CreateDirectory( Path.GetDirectoryName( path ) );
			Span<byte> header = stackalloc byte[HeaderSize];
			BinaryPrimitives.WriteUInt32LittleEndian( header, Magic );
			BinaryPrimitives.WriteUInt32LittleEndian( header[4..], Version );
			BinaryPrimitives.WriteUInt32LittleEndian( header[8..], (uint)data.Length );
			BinaryPrimitives.WriteUInt64LittleEndian( header[12..], Checksum( data ) );

			using ( var stream = File.Create( temporary ) )
			{
				stream.Write( header );
				stream.Write( data );
			}

			File.Move( temporary, path, overwrite: true );
			return true;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			try
			{
				File.Delete( temporary );
			}
			catch ( Exception cleanup ) when ( cleanup is IOException or UnauthorizedAccessException )
			{
				// Left for the next save to overwrite
			}

			return false;
		}
	}

	/// <summary>64-bit FNV-1a, eight bytes at a time.</summary>
	public static ulong Checksum( ReadOnlySpan<byte> data )
	{
		var hash = 0xcbf29ce484222325UL;
		var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ulong>( data );
		foreach ( var word in words ) hash = (hash ^ word) * 0x100000001b3UL;
		for ( int i = words.Length * 8; i < data.Length; i++ ) hash = (hash ^ data[i]) * 0x100000001b3UL;
		return hash;
	}
}
