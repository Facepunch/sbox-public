using SkiaSharp;

namespace Editor.TerrainEditor;

internal static class TerrainImportHelper
{
	internal static unsafe ushort[] ResampleHeightmap( Span<ushort> original, int originalSize, int newSize )
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( originalSize );
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero( newSize );
		var sourceLength = checked(originalSize * originalSize);
		if ( original.Length > sourceLength )
			throw new ArgumentException( "Heightmap exceeds its resolution.", nameof( original ) );

		// The old bitmap copy left missing samples at zero on incomplete maps.
		if ( original.Length < sourceLength )
		{
			var padded = new ushort[sourceLength];
			original.CopyTo( padded );
			original = padded;
		}

		var result = new ushort[checked(newSize * newSize)];
		fixed ( ushort* input = original, output = result )
		{
			var info = new SKImageInfo( originalSize, originalSize, SKColorType.Alpha16, SKAlphaType.Opaque );
			using var source = new SKPixmap( info, (IntPtr)input );
			using var destination = new SKPixmap( info.WithSize( newSize, newSize ), (IntPtr)output );
			if ( !source.ScalePixels( destination, new SKSamplingOptions( SKFilterMode.Linear ) ) )
				throw new InvalidOperationException( "Couldn't resample the terrain heightmap." );
		}

		return result;
	}

	/// <summary>
	/// Nearest-neighbor resample of a packed uint control map.
	/// Must use nearest-neighbor to preserve the packed material bit fields.
	/// </summary>
	internal static UInt32[] ResampleControlMap( UInt32[] original, int originalSize, int newSize )
	{
		var result = new UInt32[newSize * newSize];
		for ( int y = 0; y < newSize; y++ )
		{
			int srcY = Math.Clamp( (int)MathF.Round( (float)y / Math.Max( newSize - 1, 1 ) * (originalSize - 1) ), 0, originalSize - 1 );
			for ( int x = 0; x < newSize; x++ )
			{
				int srcX = Math.Clamp( (int)MathF.Round( (float)x / Math.Max( newSize - 1, 1 ) * (originalSize - 1) ), 0, originalSize - 1 );
				result[y * newSize + x] = original[srcY * originalSize + srcX];
			}
		}
		return result;
	}

	/// <summary>
	/// Resamples a TerrainStorage to a new resolution in-place.
	/// HeightMap is bilinearly interpolated via SkiaSharp; ControlMap uses nearest-neighbor.
	/// </summary>
	internal static void ResampleStorage( TerrainStorage storage, int newResolution )
	{
		if ( newResolution == storage.Resolution )
			return;

		var heightMap = ResampleHeightmap( storage.HeightMap, storage.Resolution, newResolution );
		var controlMap = ResampleControlMap( storage.ControlMap, storage.Resolution, newResolution );

		// SetResolution allocates fresh arrays and fixes the Resolution property (private set)
		storage.SetResolution( newResolution );

		storage.HeightMap = heightMap;
		storage.ControlMap = controlMap;
	}

	internal static int RoundDownToPowerOfTwo( int value )
	{
		value = value | (value >> 1);
		value = value | (value >> 2);
		value = value | (value >> 4);
		value = value | (value >> 8);
		value = value | (value >> 16);
		return value - (value >> 1);
	}
}
