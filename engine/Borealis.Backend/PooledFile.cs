namespace Borealis.Backend;

/// <summary>
/// A file read whole into an array from the shared pool, for caches that copy what they keep out of it: reading them with
/// File.ReadAllBytes made each read's bytes garbage. Give the array back with <see cref="Return"/>.
/// </summary>
internal static class PooledFile
{
	/// <summary>The file at <paramref name="path"/> in a pooled array, its first <paramref name="length"/> bytes.</summary>
	public static byte[] Read( string path, out int length )
	{
		using var file = new System.IO.FileStream( path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read, 1, System.IO.FileOptions.SequentialScan );
		length = checked((int)file.Length);
		var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent( length );
		try
		{
			file.ReadExactly( buffer, 0, length );
			return buffer;
		}
		catch
		{
			System.Buffers.ArrayPool<byte>.Shared.Return( buffer );
			throw;
		}
	}

	/// <summary>Give back an array <see cref="Read"/> returned.</summary>
	public static void Return( byte[] buffer ) => System.Buffers.ArrayPool<byte>.Shared.Return( buffer );
}
