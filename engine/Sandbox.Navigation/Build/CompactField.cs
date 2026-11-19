using System.Runtime.Intrinsics;

namespace Nav.Build;

/// <summary>
/// The open space above the solid spans: one span per place an agent could stand, with the neighbour it
/// can step to in each direction. Directions: 0 = -x, 1 = +y, 2 = +x, 3 = -y.
/// Floor and Con are either the builder's own arrays or those of a CachedField, which is never written.
/// </summary>
internal sealed class CompactField
{
	public int Width, Height;
	public int SpanCount;

	/// <summary>
	/// First span of each column, with one more entry behind the last column.
	/// </summary>
	public int[] Index = [];

	public ushort[] Floor = [];

	/// <summary>
	/// Four 6-bit connections. The top byte is only meaningful in a cached field.
	/// </summary>
	public uint[] Con = [];

	/// <summary>
	/// Area per span after erosion and volumes. 0 = not walkable.
	/// </summary>
	public byte[] Area = [];

	public ushort[] Region = [];
	public int RegionCount;

	public const int BlockSize = 16;

	public static int Connection( uint con, int direction ) => (int)(con >> (direction * 6)) & 63;

	/// <summary>
	/// True when the 16 values from at on all equal value.
	/// </summary>
	public static bool All( ushort[] values, int at, int value )
	{
		var block = values.AsSpan( at, BlockSize );
		if ( Vector256.IsHardwareAccelerated ) return Vector256.Create<ushort>( block ) == Vector256.Create( (ushort)value );
		return block.IndexOfAnyExcept( (ushort)value ) < 0;
	}

	public static bool All( byte[] values, int at, int value )
	{
		var block = values.AsSpan( at, BlockSize );
		if ( Vector128.IsHardwareAccelerated ) return Vector128.Create<byte>( block ) == Vector128.Create( (byte)value );
		return block.IndexOfAnyExcept( (byte)value ) < 0;
	}

	public static bool AllAtLeast( byte[] values, int at, int value )
	{
		var block = values.AsSpan( at, BlockSize );
		if ( Vector128.IsHardwareAccelerated ) return Vector128.GreaterThanOrEqualAll( Vector128.Create<byte>( block ), Vector128.Create( (byte)value ) );
		foreach ( byte b in block )
		{
			if ( b < value ) return false;
		}
		return true;
	}

	/// <summary>
	/// values[i] = table[values[i]] for a stretch of spans, a block at a time where they all hold the same value.
	/// Values from limit on are left alone.
	/// </summary>
	public static void Map( ushort[] values, int from, int to, ushort[] table, int limit )
	{
		int i = from;
		for ( ; i + BlockSize <= to; i += BlockSize )
		{
			int first = values[i];
			if ( All( values, i, first ) )
			{
				if ( first < limit ) values.AsSpan( i, BlockSize ).Fill( table[first] );
				continue;
			}

			for ( int k = i; k < i + BlockSize; k++ )
			{
				if ( values[k] < limit ) values[k] = table[values[k]];
			}
		}

		for ( ; i < to; i++ )
		{
			if ( values[i] < limit ) values[i] = table[values[i]];
		}
	}

	public int Offset( int direction ) => direction switch
	{
		0 => -1,
		1 => Width,
		2 => 1,
		_ => -Width
	};
}

/// <summary>
/// Which blocks of BlockSize columns of a field are level open ground, for the stages that skip them.
/// The field is looked at without a margin of cells on every side, row by row, in blocks from the margin on.
/// A block is open when every column of it, of the blocks above and below and the two columns beside it
/// holds exactly one span, and each span of the block connects to all four neighbours. The neighbours of
/// span i of such a block are i - 1, i + 1 and the spans at the same place in the rows above and below.
/// </summary>
internal sealed class OpenBlocks
{
	public bool[] Open = [];
	public int PerRow;

	public void Find( CompactField field, int margin )
	{
		const int blockSize = CompactField.BlockSize;
		int w = field.Width;
		int x0 = margin, y0 = margin, x1 = w - margin, y1 = field.Height - margin;
		PerRow = (x1 - x0) / blockSize;
		int blocks = PerRow * (y1 - y0);
		if ( Open.Length < blocks ) Open = new bool[blocks];

		// Without a margin there are no neighbour rows or columns to look at
		bool inside = margin > 0;
		var index = field.Index;
		var con = field.Con;
		for ( int y = y0, block = 0; y < y1; y++ )
		{
			for ( int k = 0; k < PerRow; k++, block++ )
			{
				int column = x0 + k * blockSize + y * w;
				bool open = inside
					&& index[column + blockSize + 1] - index[column - 1] == blockSize + 2
					&& index[column + blockSize - w] - index[column - w] == blockSize
					&& index[column + blockSize + w] - index[column + w] == blockSize;
				if ( open )
				{
					uint all = 0;
					for ( int i = index[column], end = i + blockSize; i < end; i++ ) all |= con[i];
					open = (all & 0xFFFFFF) == 0;
				}
				Open[block] = open;
			}
		}
	}
}
