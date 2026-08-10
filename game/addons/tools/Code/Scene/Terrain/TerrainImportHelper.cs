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
	/// Rescales heightmap texels so world-space heights are unchanged after
	/// TerrainHeight changes from oldHeight to newHeight. Values clamp at ushort.MaxValue.
	/// Returns a new array; never mutates the input.
	/// </summary>
	internal static ushort[] RescaleHeightmap( ushort[] original, float oldHeight, float newHeight )
	{
		var result = new ushort[original.Length];
		double scale = (double)oldHeight / newHeight;
		for ( int i = 0; i < original.Length; i++ )
		{
			result[i] = (ushort)Math.Min( (uint)Math.Round( original[i] * scale ), ushort.MaxValue );
		}
		return result;
	}

	/// <summary>
	/// Resizes the terrain's world footprint from oldSize to newSize keeping geometry
	/// fixed in world space (corner-anchored, like the engine). Growing pads flat ground
	/// (height 0, control 0) at the far edges; shrinking crops them. Resolution unchanged.
	/// Use the Move Terrain tool afterwards to place the result.
	/// Returns new arrays; never mutates the inputs.
	/// </summary>
	internal static (ushort[] heightMap, UInt32[] controlMap) CanvasResize(
		ushort[] heightMap, UInt32[] controlMap, int resolution, float oldSize, float newSize )
	{
		if ( newSize == oldSize )
			return (heightMap, controlMap);

		if ( newSize > oldSize )
		{
			// Grow: existing content shrinks into the corner k×k block, the rest is flat ground.
			int k = Math.Clamp( (int)MathF.Round( resolution * oldSize / newSize ), 1, resolution );
			if ( k == resolution )
				return (heightMap, controlMap);

			var smallHeight = ResampleHeightmap( heightMap, resolution, k );
			var smallControl = ResampleControlMap( controlMap, resolution, k );

			var newHeightMap = new ushort[resolution * resolution];
			var newControlMap = new UInt32[resolution * resolution];

			for ( int y = 0; y < k; y++ )
			{
				Array.Copy( smallHeight, y * k, newHeightMap, y * resolution, k );
				Array.Copy( smallControl, y * k, newControlMap, y * resolution, k );
			}

			return (newHeightMap, newControlMap);
		}

		// Shrink: keep the corner k×k block the new footprint covers, stretch it back out.
		int keep = Math.Clamp( (int)MathF.Round( resolution * newSize / oldSize ), 1, resolution );
		if ( keep == resolution )
			return (heightMap, controlMap);

		var croppedHeight = new ushort[keep * keep];
		var croppedControl = new UInt32[keep * keep];

		for ( int y = 0; y < keep; y++ )
		{
			Array.Copy( heightMap, y * resolution, croppedHeight, y * keep, keep );
			Array.Copy( controlMap, y * resolution, croppedControl, y * keep, keep );
		}

		return (ResampleHeightmap( croppedHeight, keep, resolution ),
			ResampleControlMap( croppedControl, keep, resolution ));
	}

	/// <summary>
	/// Average height of the map's outer ring. Used as the fill level for ground exposed by a
	/// shift, so the new edge sits level with the terrain's borders instead of dropping to zero.
	/// </summary>
	internal static ushort BorderAverage( ushort[] heightMap, int resolution )
	{
		if ( resolution <= 0 || heightMap.Length == 0 ) return 0;
		if ( resolution == 1 ) return heightMap[0];

		long total = 0;
		int count = 0;

		for ( int x = 0; x < resolution; x++ )
		{
			total += heightMap[x];                                       // bottom row
			total += heightMap[(resolution - 1) * resolution + x];       // top row
			count += 2;
		}

		// corners already counted, so skip them on the sides
		for ( int y = 1; y < resolution - 1; y++ )
		{
			total += heightMap[y * resolution];                          // left column
			total += heightMap[y * resolution + resolution - 1];         // right column
			count += 2;
		}

		return (ushort)(total / count);
	}

	/// <summary>
	/// Slides the maps by whole texels. Heights are never touched - a vertical move is just the
	/// terrain object moving, which costs nothing and keeps the sculpt exactly as it is.
	/// Ground exposed by the slide is filled at the border average (see <see cref="BorderAverage"/>)
	/// and inherits the nearest edge's materials, so no cliff or bald patch appears.
	/// Content pushed past an edge is discarded.
	/// Returns new arrays; never mutates the inputs.
	/// </summary>
	internal static (ushort[] heightMap, UInt32[] controlMap) ShiftContent(
		ushort[] heightMap, UInt32[] controlMap, int resolution, int shiftX, int shiftY )
	{
		if ( shiftX == 0 && shiftY == 0 )
			return (heightMap, controlMap);

		var newHeightMap = new ushort[heightMap.Length];
		var newControlMap = new UInt32[controlMap.Length];

		ushort fill = BorderAverage( heightMap, resolution );

		for ( int y = 0; y < resolution; y++ )
		{
			int srcY = y - shiftY;
			bool rowInside = srcY >= 0 && srcY < resolution;
			int clampedY = Math.Clamp( srcY, 0, resolution - 1 );

			for ( int x = 0; x < resolution; x++ )
			{
				int srcX = x - shiftX;
				int dst = y * resolution + x;

				if ( rowInside && srcX >= 0 && srcX < resolution )
				{
					int src = srcY * resolution + srcX;
					newHeightMap[dst] = heightMap[src];
					newControlMap[dst] = controlMap[src];
					continue;
				}

				// Exposed ground: level fill, materials taken from the nearest edge texel
				newHeightMap[dst] = fill;
				newControlMap[dst] = controlMap[clampedY * resolution + Math.Clamp( srcX, 0, resolution - 1 )];
			}
		}

		return (newHeightMap, newControlMap);
	}

	/// <summary>
	/// Raises or lowers every height by a constant, keeping the terrain object where it is.
	/// Values clamp at the floor and the ceiling, so ground driven past either is flattened
	/// against it and lost. Returns a new array; never mutates the input.
	/// </summary>
	internal static ushort[] OffsetHeights( ushort[] heightMap, int delta )
	{
		if ( delta == 0 )
			return heightMap;

		var result = new ushort[heightMap.Length];

		for ( int i = 0; i < heightMap.Length; i++ )
			result[i] = (ushort)Math.Clamp( heightMap[i] + delta, 0, ushort.MaxValue );

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
