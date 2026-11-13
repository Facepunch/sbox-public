using System.Numerics;
using System.Runtime.InteropServices;

namespace Nav.Build;

/// <summary>
/// A triangle soup sorted into tiles once: per tile the indices of every triangle whose box touches the
/// tile's padded box, in the order of the soup.
/// </summary>
internal sealed class TriangleBins
{
	/// <summary>
	/// Tiles a triangle reaches into, border not counted, in the order the soup first reaches them.
	/// A tile that only has triangles in its border cannot have polygons and is not listed.
	/// </summary>
	public readonly List<(int X, int Y)> Tiles = new();

	/// <summary>
	/// Triangles left out: an index out of range or a coordinate that is not finite.
	/// </summary>
	public int Invalid;

	const int MaxTilesPerTriangle = 1 << 22;

	readonly Dictionary<(int X, int Y), int> slots = new();
	readonly int[] indices;
	int[] starts = new int[16];
	int[] counts = new int[16];
	bool[] inside = new bool[16];

	public TriangleBins( in NavSettings settings, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> soup )
	{
		float tile = settings.TileSize * settings.CellSize;
		float pad = BuildConfig.BorderCells( settings ) * settings.CellSize;
		int triangles = soup.Length / 3;

		// First the sizes, then the indices
		for ( int pass = 0; pass < 2; pass++ )
		{
			for ( int t = 0; t < triangles; t++ )
			{
				int i0 = soup[t * 3], i1 = soup[t * 3 + 1], i2 = soup[t * 3 + 2];
				if ( (uint)i0 >= (uint)vertices.Length || (uint)i1 >= (uint)vertices.Length || (uint)i2 >= (uint)vertices.Length )
				{
					Invalid += 1 - pass;
					continue;
				}

				Vector3 a = vertices[i0], b = vertices[i1], c = vertices[i2];

				// x - x is zero unless x is infinite or NaN
				if ( (a - a) + (b - b) + (c - c) != Vector3.Zero )
				{
					Invalid += 1 - pass;
					continue;
				}

				var min = Vector3.Min( Vector3.Min( a, b ), c );
				var max = Vector3.Max( Vector3.Max( a, b ), c );
				float x0 = MathF.Floor( (min.X - pad) / tile ), x1 = MathF.Floor( (max.X + pad) / tile );
				float y0 = MathF.Floor( (min.Y - pad) / tile ), y1 = MathF.Floor( (max.Y + pad) / tile );

				// A triangle a billion tiles out, or over millions of tiles, is as good as not finite
				if ( !(MathF.Abs( x0 ) < 1e9f && MathF.Abs( x1 ) < 1e9f && MathF.Abs( y0 ) < 1e9f && MathF.Abs( y1 ) < 1e9f) || ((double)x1 - x0 + 1) * ((double)y1 - y0 + 1) > MaxTilesPerTriangle )
				{
					Invalid += 1 - pass;
					continue;
				}

				for ( int y = (int)y0; y <= (int)y1; y++ )
				{
					for ( int x = (int)x0; x <= (int)x1; x++ )
					{
						int slot = Slot( x, y );
						if ( pass == 0 )
						{
							counts[slot] += 3;
							inside[slot] |= max.X > x * tile && min.X < (x + 1) * tile && max.Y > y * tile && min.Y < (y + 1) * tile;
							continue;
						}

						int at = starts[slot] + counts[slot];
						indices![at] = i0;
						indices[at + 1] = i1;
						indices[at + 2] = i2;
						counts[slot] += 3;
					}
				}
			}

			if ( pass == 1 ) break;
			int total = 0;
			for ( int slot = 0; slot < slots.Count; slot++ )
			{
				starts[slot] = total;
				total += counts[slot];
				counts[slot] = 0;
			}
			indices = new int[total];
		}

		indices ??= [];
		foreach ( var (at, slot) in slots )
		{
			if ( inside[slot] ) Tiles.Add( at );
		}
		Tiles.Sort( ( a, b ) => slots[a].CompareTo( slots[b] ) );
	}

	// The tile asked for last. Triangles that follow each other in a soup are mostly in the same tile, and
	// the lookup is most of what sorting them costs.
	int lastX, lastY, lastSlot = -1;

	int Slot( int x, int y )
	{
		if ( x == lastX && y == lastY && lastSlot >= 0 ) return lastSlot;
		lastX = x;
		lastY = y;
		ref int slot = ref CollectionsMarshal.GetValueRefOrAddDefault( slots, (x, y), out bool exists );
		if ( exists ) return lastSlot = slot;
		lastSlot = slot = slots.Count - 1;
		if ( slot == counts.Length )
		{
			Array.Resize( ref starts, slot * 2 );
			Array.Resize( ref counts, slot * 2 );
			Array.Resize( ref inside, slot * 2 );
		}
		return slot;
	}

	/// <summary>
	/// The triangles of one tile, three indices each. Empty for a tile no triangle comes near.
	/// </summary>
	public ReadOnlyMemory<int> Indices( int x, int y )
	{
		return slots.TryGetValue( (x, y), out int slot ) ? indices.AsMemory( starts[slot], counts[slot] ) : default;
	}
}
