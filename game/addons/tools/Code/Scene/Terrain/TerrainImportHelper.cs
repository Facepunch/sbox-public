using SkiaSharp;
using System.Runtime.InteropServices;

namespace Editor.TerrainEditor;

internal static class TerrainImportHelper
{
	internal static ushort[] ResampleHeightmap( Span<ushort> original, int originalSize, int newSize )
	{
		using var bitmap = new SKBitmap( originalSize, originalSize, SKColorType.Alpha16, SKAlphaType.Opaque );
		using ( var pixmap = bitmap.PeekPixels() )
		{
			var dataBytes = MemoryMarshal.AsBytes( original );
			unsafe
			{
				fixed ( byte* source = dataBytes )
				{
					Buffer.MemoryCopy( source, (void*)pixmap.GetPixels(), dataBytes.Length, dataBytes.Length );
				}
			}
		}

		using var newBitmap = bitmap.Resize( new SKSizeI( newSize, newSize ), new SKSamplingOptions( SKFilterMode.Linear, SKMipmapMode.None ) );
		using var newPixmap = newBitmap.PeekPixels();
		return newPixmap.GetPixelSpan<ushort>().ToArray();
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

		var oldHeightMap = (ushort[])storage.HeightMap.Clone();
		var oldControlMap = (UInt32[])storage.ControlMap.Clone();
		int oldRes = storage.Resolution;

		// SetResolution allocates fresh arrays and fixes the Resolution property (private set)
		storage.SetResolution( newResolution );

		storage.HeightMap = ResampleHeightmap( oldHeightMap, oldRes, newResolution );
		storage.ControlMap = ResampleControlMap( oldControlMap, oldRes, newResolution );
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
