using System.Runtime.CompilerServices;

namespace Nav.Build;

/// <summary>
/// Turns the solid spans into the eroded open-space field, the part of a build that is cached.
/// </summary>
internal sealed class FieldBuilder
{
	ushort[] floors = [];
	ushort[] tops = [];
	uint[] cons = [];
	byte[] distances = [];
	readonly OpenBlocks openBlocks = new();

	/// <summary>
	/// Ceiling of each open span of the last Compact, for tests.
	/// </summary>
	public ushort[] Tops => tops;

	/// <summary>
	/// One pass per row: decides which solid spans can be stood on (low obstacles, clearance, ledges) and writes an
	/// open span above each of those, then connects the row's spans to each other and to the row before.
	/// </summary>
	public bool Compact( SolidField solid, CompactField field, int walkableHeight, int walkableClimb, out string? error )
	{
		int w = solid.Width;
		int h = solid.Height;
		int columns = w * h;

		if ( floors.Length < solid.SpanCount )
		{
			int size = solid.SpanCount + solid.SpanCount / 2;
			floors = new ushort[size];
			tops = new ushort[size];
			cons = new uint[size];
		}
		if ( field.Index.Length < columns + 1 ) field.Index = new int[columns + 1];

		var index = field.Index;
		int write = 0;
		error = null;

		for ( int y = 0; y < h; y++ )
		{
			write = WriteRow( solid, index, y, write, walkableHeight, walkableClimb );
			if ( write < 0 )
			{
				error = "A column has more than 255 walkable layers";
				return false;
			}

			// The first span of the next row, the value it gets when that row is written
			index[(y + 1) * w] = write;
			ConnectRow( index, y * w, w, y > 0, walkableHeight, walkableClimb );
		}

		index[columns] = write;
		field.Width = w;
		field.Height = h;
		field.SpanCount = write;
		field.Floor = floors;
		field.Con = cons;
		return true;
	}

	// Writes the open spans of a row from write on. Returns where the next row starts, or -1 for a column with more
	// than 255 of them. A method of its own, as is ConnectRow: the JIT keeps more of each loop in registers.
	[MethodImpl( MethodImplOptions.NoInlining )]
	int WriteRow( SolidField solid, int[] index, int y, int write, int walkableHeight, int walkableClimb )
	{
		int w = solid.Width;
		int capacity = solid.Capacity;
		int stride = w * capacity;
		var spans = solid.Spans;
		var counts = solid.Counts;
		var floors = this.floors;
		var tops = this.tops;
		var cons = this.cons;
		bool rowInside = y > 0 && y + 1 < solid.Height;
		for ( int x = 0, column = y * w, start = y * stride; x < w; x++, column++, start += capacity )
		{
			int first = write;
			index[column] = first;
			int count = counts[column];
			if ( count == 0 ) continue;

			// A column on the edge of the field has a neighbour missing, which makes everything in it a ledge
			bool inside = rowInside && x > 0 && x + 1 < w;
			bool previousWalkable = false;
			int previousMax = 0;
			ulong span = spans[start];
			for ( int i = 1; i <= count; i++ )
			{
				ulong next = i < count ? spans[start + i] : BuildConfig.MaxSpanHeight;
				int floor = SolidField.Max( span );
				int ceiling = SolidField.Min( next );

				// A low obstacle on top of walkable ground is walkable and skips the other two tests
				bool wasWalkable = SolidField.Area( span ) != 0;
				bool walkable;
				if ( !wasWalkable )
				{
					walkable = previousWalkable && floor - previousMax <= walkableClimb;
				}
				else if ( !inside || ceiling - floor < walkableHeight )
				{
					walkable = false;
				}
				else
				{
					int lowest = floor, highest = floor;
					walkable = (SameGap( spans, start + capacity, counts[column + 1], i, span, next ) || Reachable( spans, start + capacity, counts[column + 1], floor, ceiling, walkableHeight, walkableClimb, ref lowest, ref highest ))
						&& (SameGap( spans, start + stride, counts[column + w], i, span, next ) || Reachable( spans, start + stride, counts[column + w], floor, ceiling, walkableHeight, walkableClimb, ref lowest, ref highest ))
						&& (SameGap( spans, start - capacity, counts[column - 1], i, span, next ) || Reachable( spans, start - capacity, counts[column - 1], floor, ceiling, walkableHeight, walkableClimb, ref lowest, ref highest ))
						&& (SameGap( spans, start - stride, counts[column - w], i, span, next ) || Reachable( spans, start - stride, counts[column - w], floor, ceiling, walkableHeight, walkableClimb, ref lowest, ref highest ))
						&& highest - lowest <= walkableClimb;
				}

				previousWalkable = wasWalkable;
				previousMax = floor;
				span = next;
				if ( !walkable ) continue;

				floors[write] = (ushort)floor;
				tops[write] = (ushort)ceiling;
				cons[write] = 0xFFFFFF;
				write++;
			}

			if ( write - first > 255 ) return -1;
		}

		return write;
	}

	// True when the neighbour column has, at the same place in its stack, a span with the same top and the
	// same gap above it. Level ground: nothing to fall into and nothing that changes lowest or highest, since
	// every other gap of that column lies below the floor or above the ceiling.
	static bool SameGap( ulong[] spans, int start, int count, int next, ulong span, ulong above )
	{
		if ( next > count ) return false;
		ulong neighbourAbove = next < count ? spans[start + next] : BuildConfig.MaxSpanHeight;
		return ((spans[start + next - 1] ^ span) & 0xFFFF0000) == 0 && ((neighbourAbove ^ above) & 0xFFFF) == 0;
	}

	// False when stepping from the span into this neighbour column means a drop of more than the climb: the
	// column is empty, open below its lowest span, or has room to stand further down than that.
	// Otherwise the floors within the climb widen lowest and highest; too wide apart means too steep.
	static bool Reachable( ulong[] spans, int start, int count, int floor, int ceiling, int height, int climb, ref int lowest, ref int highest )
	{
		if ( count == 0 ) return false;

		ulong span = spans[start];
		if ( Math.Min( ceiling, SolidField.Min( span ) ) - floor >= height ) return false;

		for ( int i = 1; i <= count; i++ )
		{
			ulong next = i < count ? spans[start + i] : BuildConfig.MaxSpanHeight;
			int neighbourFloor = SolidField.Max( span );
			int difference = neighbourFloor - floor;
			if ( difference > climb ) break;

			span = next;
			if ( Math.Min( ceiling, SolidField.Min( next ) ) - Math.Max( floor, neighbourFloor ) < height ) continue;
			if ( difference < -climb ) return false;
			if ( neighbourFloor < lowest ) lowest = neighbourFloor;
			if ( neighbourFloor > highest ) highest = neighbourFloor;
		}
		return true;
	}

	// Connects the spans of a row to those before them in the row and in the row below. Each span and direction is
	// linked once, so the order does not matter.
	[MethodImpl( MethodImplOptions.NoInlining )]
	void ConnectRow( int[] index, int row, int w, bool south, int height, int climb )
	{
		for ( int column = row; column < row + w; column++ )
		{
			int a = index[column], open = index[column + 1] - a;
			if ( open == 0 ) continue;
			int west = column > row ? index[column - 1] : a, westCount = a - west;
			int below = south ? index[column - w] : 0, belowCount = south ? index[column - w + 1] - below : 0;
			if ( open == 1 && westCount <= 1 && belowCount <= 1 )
			{
				// One span with at most one beside it, the case of open ground: span 0 of each other or not linked
				int floor = floors[a], top = tops[a];
				uint con = 0xFFFFFF;
				if ( westCount == 1 && Math.Abs( floor - floors[west] ) <= climb && Math.Min( top, tops[west] ) - Math.Max( floor, floors[west] ) >= height )
				{
					con &= ~63u;
					cons[west] &= ~(63u << 12);
				}
				if ( belowCount == 1 && Math.Abs( floor - floors[below] ) <= climb && Math.Min( top, tops[below] ) - Math.Max( floor, floors[below] ) >= height )
				{
					con &= ~(63u << 18);
					cons[below] &= ~(63u << 6);
				}
				cons[a] = con;
				continue;
			}
			if ( westCount != 0 ) ConnectStacks( west, westCount, a, open, 0, height, climb );
			if ( belowCount != 0 ) ConnectStacks( below, belowCount, a, open, 3, height, climb );
		}
	}

	void ConnectStacks( int b, int bCount, int a, int aCount, int direction, int height, int climb )
	{
		int shift = direction * 6;
		int oppositeShift = ((direction + 2) & 3) * 6;

		// The same stack on both sides: span i is in reach of span i alone, when it is tall enough
		if ( aCount == bCount && floors.AsSpan( a, aCount ).SequenceEqual( floors.AsSpan( b, bCount ) ) && tops.AsSpan( a, aCount ).SequenceEqual( tops.AsSpan( b, bCount ) ) )
		{
			for ( int i = 0; i < aCount && i < BuildConfig.NotConnected; i++ )
			{
				if ( tops[a + i] - floors[a + i] < height ) continue;
				cons[a + i] = (cons[a + i] & ~(63u << shift)) | ((uint)i << shift);
				cons[b + i] = (cons[b + i] & ~(63u << oppositeShift)) | ((uint)i << oppositeShift);
			}
			return;
		}

		int first = 0;
		for ( int i = 0; i < aCount; i++ )
		{
			int aFloor = floors[a + i];
			int aTop = tops[a + i];
			while ( first < bCount && floors[b + first] < aFloor - climb ) first++;
			for ( int j = first; j < bCount; j++ )
			{
				int bFloor = floors[b + j];
				if ( bFloor > aFloor + climb ) break;
				if ( Math.Min( aTop, tops[b + j] ) - Math.Max( aFloor, bFloor ) < height ) continue;

				if ( j < BuildConfig.NotConnected && ((cons[a + i] >> shift) & 63) == BuildConfig.NotConnected )
					cons[a + i] = (cons[a + i] & ~(63u << shift)) | ((uint)j << shift);
				if ( i < BuildConfig.NotConnected && ((cons[b + j] >> oppositeShift) & 63) == BuildConfig.NotConnected )
					cons[b + j] = (cons[b + j] & ~(63u << oppositeShift)) | ((uint)i << oppositeShift);
			}
		}
	}

	/// <summary>
	/// Removes everything closer than radius cells to an edge: a two-pass distance transform over the
	/// connections (2 per straight step, 3 per diagonal). The area lands in the top byte of each connection word.
	/// Returns how many spans stay walkable.
	/// </summary>
	public int Erode( CompactField field, int radius )
	{
		int w = field.Width;
		int h = field.Height;
		int spanCount = field.SpanCount;
		var index = field.Index;
		var con = field.Con;

		if ( radius <= 0 )
		{
			for ( int i = 0; i < spanCount; i++ ) con[i] = (con[i] & 0xFFFFFF) | (uint)NavFilter.DefaultArea << 24;
			return spanCount;
		}

		if ( distances.Length < spanCount ) distances = new byte[spanCount + spanCount / 2];
		var distance = distances;

		// A span with a missing neighbour is an edge
		for ( int i = 0; i < spanCount; i++ )
		{
			uint c = con[i];
			bool edge = (c & 63) == 63 || ((c >> 6) & 63) == 63 || ((c >> 12) & 63) == 63 || ((c >> 18) & 63) == 63;
			distance[i] = edge ? (byte)0 : (byte)255;
		}

		// Only distances below keep decide anything. A block of open ground whose inputs are all too far to
		// bring it below keep is skipped and keeps whatever it holds, which is at least keep as well.
		int keep = radius * 2;
		int far = keep - 2;
		const int blockSize = CompactField.BlockSize;
		openBlocks.Find( field, 1 );
		var open = openBlocks.Open;
		int blocksPerRow = openBlocks.PerRow;

		for ( int y = 0; y < h; y++ )
		{
			int row = y * w;
			if ( y < 1 || y >= h - 1 )
			{
				Forward( field, row, row + w );
				continue;
			}

			Forward( field, row, row + 1 );
			int x = 1;
			for ( int k = 0; k < blocksPerRow; k++, x += blockSize )
			{
				int column = x + row;
				if ( open[(y - 1) * blocksPerRow + k] )
				{
					// West, the row below, and the two diagonals that reach outside the block
					int i = index[column];
					int south = index[column - w];
					int southWest = (int)((con[i - 1] >> 18) & 63);
					int southEast = (int)((con[south + blockSize - 1] >> 12) & 63);
					if ( distance[i - 1] >= far && CompactField.AllAtLeast( distance, south, far )
						&& (southWest == 63 || distance[index[column - 1 - w] + southWest] >= far)
						&& (southEast == 63 || distance[index[column + blockSize - w] + southEast] >= far) ) continue;
				}
				Forward( field, column, column + blockSize );
			}
			Forward( field, x + row, row + w );
		}

		for ( int y = h - 1; y >= 0; y-- )
		{
			int row = y * w;
			if ( y < 1 || y >= h - 1 )
			{
				Backward( field, row, row + w );
				continue;
			}

			int tail = 1 + blocksPerRow * blockSize;
			Backward( field, tail + row, row + w );
			for ( int k = blocksPerRow - 1; k >= 0; k-- )
			{
				int column = 1 + k * blockSize + row;
				if ( open[(y - 1) * blocksPerRow + k] )
				{
					// The block itself, which the first pass may have lowered, east, the row above, and the two
					// diagonals that reach outside the block
					int i = index[column];
					int north = index[column + w];
					int northEast = (int)((con[i + blockSize] >> 6) & 63);
					int northWest = (int)(con[north] & 63);
					if ( distance[i + blockSize] >= far && CompactField.AllAtLeast( distance, i, keep ) && CompactField.AllAtLeast( distance, north, far )
						&& (northEast == 63 || distance[index[column + blockSize + w] + northEast] >= far)
						&& (northWest == 63 || distance[index[column - 1 + w] + northWest] >= far) ) continue;
				}
				Backward( field, column, column + blockSize );
			}
			Backward( field, row, row + 1 );
		}

		int walkable = 0;
		for ( int i = 0; i < spanCount; i++ )
		{
			uint area = distance[i] < keep ? NavFilter.NoArea : NavFilter.DefaultArea;
			con[i] = (con[i] & 0xFFFFFF) | area << 24;
			walkable += (int)area;
		}
		return walkable;
	}

	// One row stretch of the first pass: distances come in from the west and the south.
	void Forward( CompactField field, int from, int to )
	{
		int w = field.Width;
		var index = field.Index;
		var con = field.Con;
		var distance = distances;
		for ( int column = from; column < to; column++ )
		{
			for ( int i = index[column], end = index[column + 1]; i < end; i++ )
			{
				uint c = con[i];
				int d = distance[i];
				int west = (int)(c & 63);
				if ( west != 63 )
				{
					int a = index[column - 1] + west;
					d = Math.Min( d, distance[a] + 2 );
					int south = (int)((con[a] >> 18) & 63);
					if ( south != 63 ) d = Math.Min( d, distance[index[column - 1 - w] + south] + 3 );
				}

				int below = (int)((c >> 18) & 63);
				if ( below != 63 )
				{
					int a = index[column - w] + below;
					d = Math.Min( d, distance[a] + 2 );
					int east = (int)((con[a] >> 12) & 63);
					if ( east != 63 ) d = Math.Min( d, distance[index[column - w + 1] + east] + 3 );
				}
				distance[i] = (byte)d;
			}
		}
	}

	// One row stretch of the second pass, columns from high to low: distances come in from the east and the north.
	void Backward( CompactField field, int from, int to )
	{
		int w = field.Width;
		var index = field.Index;
		var con = field.Con;
		var distance = distances;
		for ( int column = to - 1; column >= from; column-- )
		{
			for ( int i = index[column], end = index[column + 1]; i < end; i++ )
			{
				uint c = con[i];
				int d = distance[i];
				int east = (int)((c >> 12) & 63);
				if ( east != 63 )
				{
					int a = index[column + 1] + east;
					d = Math.Min( d, distance[a] + 2 );
					int north = (int)((con[a] >> 6) & 63);
					if ( north != 63 ) d = Math.Min( d, distance[index[column + 1 + w] + north] + 3 );
				}

				int above = (int)((c >> 6) & 63);
				if ( above != 63 )
				{
					int a = index[column + w] + above;
					d = Math.Min( d, distance[a] + 2 );
					int west = (int)(con[a] & 63);
					if ( west != 63 ) d = Math.Min( d, distance[index[column + w - 1] + west] + 3 );
				}
				distance[i] = (byte)d;
			}
		}
	}
}
