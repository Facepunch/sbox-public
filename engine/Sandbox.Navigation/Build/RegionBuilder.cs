namespace Nav.Build;

/// <summary>
/// Layer regions: every row is swept into runs, runs join the region below them when they match it
/// exactly, then touching regions merge as long as no two spans of one region end up in one column.
/// </summary>
internal sealed class RegionBuilder
{
	public const ushort Border = 0x8000;
	const int NoNeighbour = 0xFFFF;

	// Per run of the row being swept
	int[] runNeighbour = [];
	int[] runSamples = [];
	ushort[] runRegion = [];
	int[] samplesBelow = [];
	int run;

	// Per region
	int[] spanCounts = [];
	byte[] regionArea = [];
	bool[] touchesBorder = [];
	ushort[] layer = [];
	int[] remap = [];
	int[] queue = [];
	readonly IntLists connections = new();
	readonly IntLists overlaps = new();
	int[] columnRegions = new int[256];
	int[] lastColumnRegions = new int[256];

	/// <summary>
	/// Regions for the field without its border, which is only read: it stands for whatever continues in
	/// the next tile. open are the open blocks of the field for the same border.
	/// </summary>
	public bool Build( CompactField field, OpenBlocks open, int border, int minRegionCells, out string? error )
	{
		int w = field.Width, h = field.Height;
		var index = field.Index;
		if ( field.Region.Length < field.SpanCount ) field.Region = new ushort[field.SpanCount + field.SpanCount / 2];
		var region = field.Region;

		int x0 = border, x1 = w - border, y0 = border, y1 = h - border;

		int longestRow = 0;
		for ( int y = 0; y < h; y++ )
		{
			int first = index[y * w];
			int count = index[w + y * w] - first;
			Array.Clear( region, first, count );
			longestRow = Math.Max( longestRow, count );
		}

		// The border gets four regions of its own, one per side
		int id = 1;
		if ( border > 0 )
		{
			Paint( field, 0, x0, 0, h, 1 | Border );
			Paint( field, x1, w, 0, h, 2 | Border );
			Paint( field, 0, w, 0, y0, 3 | Border );
			Paint( field, 0, w, y1, h, 4 | Border );
			id = 5;
		}

		if ( runNeighbour.Length < longestRow + 2 )
		{
			int size = longestRow + 2;
			runNeighbour = new int[size];
			runSamples = new int[size];
			runRegion = new ushort[size];
		}

		if ( !Sweep( field, open, border, ref id ) )
		{
			error = "Too many regions";
			return false;
		}

		Collect( field, open, border, id + 1 );
		MergeLayers( id + 1, minRegionCells, field );
		for ( int y = y0; y < y1; y++ ) CompactField.Map( region, index[x0 + y * w], index[x1 + y * w], layer, Border );

		error = null;
		return true;
	}

	static void Paint( CompactField field, int x0, int x1, int y0, int y1, int id )
	{
		var index = field.Index;
		var area = field.Area;
		var region = field.Region;
		for ( int y = y0; y < y1; y++ )
		{
			for ( int i = index[x0 + y * field.Width], end = index[x1 + y * field.Width]; i < end; i++ )
			{
				if ( area[i] != 0 ) region[i] = (ushort)id;
			}
		}
	}

	// Row by row: a span joins the run of its western neighbour, a run joins the region to the south when
	// the run is all that region has above it and the region is all the run has below it.
	bool Sweep( CompactField field, OpenBlocks open, int border, ref int id )
	{
		int w = field.Width;
		var index = field.Index;
		var region = field.Region;
		int x0 = border, x1 = w - border;
		const int blockSize = CompactField.BlockSize;

		for ( int y = border; y < field.Height - border; y++ )
		{
			if ( samplesBelow.Length < id + 1 ) samplesBelow = new int[(id + 1) * 2];
			Array.Clear( samplesBelow, 0, id + 1 );
			run = 1;
			int row = y * w;
			int block = (y - border) * open.PerRow;

			for ( int x = x0; x < x1; x += blockSize, block++ )
			{
				int end = Math.Min( x + blockSize, x1 );
				if ( end - x == blockSize && open.Open[block] && SweepOpenBlock( field, x + row ) ) continue;
				if ( !SweepColumns( field, x + row, end + row ) ) return false;
			}

			runRegion[0] = 0;
			for ( int i = 1; i < run; i++ )
			{
				int below = runNeighbour[i];
				if ( below != NoNeighbour && below != 0 && samplesBelow[below] == runSamples[i] )
				{
					runRegion[i] = (ushort)below;
				}
				else
				{
					if ( id >= Border ) return false;
					runRegion[i] = (ushort)id++;
				}
			}

			CompactField.Map( region, index[x0 + row], index[x1 + row], runRegion, run );
		}
		return true;
	}

	bool SweepColumns( CompactField field, int from, int to )
	{
		int w = field.Width;
		var index = field.Index;
		var con = field.Con;
		var area = field.Area;
		var region = field.Region;

		for ( int column = from; column < to; column++ )
		{
			for ( int i = index[column], end = index[column + 1]; i < end; i++ )
			{
				int spanArea = area[i];
				if ( spanArea == 0 ) continue;

				uint c = con[i];
				int current = 0;
				int west = (int)(c & 63);
				if ( west != 63 )
				{
					int neighbour = index[column - 1] + west;
					int neighbourRun = region[neighbour];
					if ( (neighbourRun & Border) == 0 && spanArea == area[neighbour] ) current = neighbourRun;
				}

				if ( current == 0 )
				{
					if ( run >= Border ) return false;
					current = run++;
					runSamples[current] = 0;
					runNeighbour[current] = 0;
				}

				int south = (int)((c >> 18) & 63);
				if ( south != 63 )
				{
					int neighbour = index[column - w] + south;
					int below = region[neighbour];
					if ( below != 0 && (below & Border) == 0 && spanArea == area[neighbour] ) Sample( current, below, 1 );
				}

				region[i] = (ushort)current;
			}
		}
		return true;
	}

	// A block of level open ground of one area whose southern neighbours all belong to one region simply
	// continues the run to its west: sixteen spans in one step. False leaves the block to SweepColumns.
	bool SweepOpenBlock( CompactField field, int column )
	{
		var area = field.Area;
		var region = field.Region;
		int i = field.Index[column];
		int south = field.Index[column - field.Width];

		int spanArea = area[i];
		int current = region[i - 1];
		int below = region[south];
		if ( spanArea == 0 || current == 0 || (current & Border) != 0 || area[i - 1] != spanArea || below == 0 || (below & Border) != 0 ) return false;
		if ( !CompactField.All( area, i, spanArea ) || !CompactField.All( region, south, below ) || !CompactField.All( area, south, spanArea ) ) return false;

		region.AsSpan( i, CompactField.BlockSize ).Fill( (ushort)current );
		Sample( current, below, CompactField.BlockSize );
		return true;
	}

	void Sample( int run, int below, int count )
	{
		if ( runNeighbour[run] == 0 || runNeighbour[run] == below )
		{
			runNeighbour[run] = below;
			runSamples[run] += count;
			samplesBelow[below] += count;
		}
		else
		{
			runNeighbour[run] = NoNeighbour;
		}
	}

	void EnsureRegions( int count )
	{
		if ( spanCounts.Length >= count ) return;
		int size = count * 2;
		Array.Resize( ref spanCounts, size );
		Array.Resize( ref regionArea, size );
		Array.Resize( ref touchesBorder, size );
		layer = new ushort[size];
		remap = new int[size];
		queue = new int[size];
		connections.Ensure( size );
		overlaps.Ensure( size );
	}

	void StartRegion( int region, int area )
	{
		spanCounts[region] = 0;
		regionArea[region] = (byte)area;
		touchesBorder[region] = false;
		connections.Clear( region );
		overlaps.Clear( region );
	}

	// Size, area, border contact, neighbours and overlaps of every region, from one pass over the spans.
	void Collect( CompactField field, OpenBlocks open, int border, int regionCount )
	{
		int w = field.Width;
		var index = field.Index;
		var con = field.Con;
		var area = field.Area;
		var region = field.Region;

		EnsureRegions( regionCount );
		for ( int i = 0; i < regionCount; i++ ) StartRegion( i, 0 );
		int lastInColumn = 0;

		const int blockSize = CompactField.BlockSize;
		int x1 = w - border;
		for ( int y = border; y < field.Height - border; y++ )
		{
			int row = y * w;
			int block = (y - border) * open.PerRow;
			for ( int x = border; x < x1; x += blockSize, block++ )
			{
				int end = Math.Min( x + blockSize, x1 );

				// Level open ground all of one region, with that region on every side, only adds to its size
				if ( end - x == blockSize && open.Open[block] )
				{
					int i = index[x + row];
					int r = region[i];
					if ( CompactField.All( region, i, r ) && region[i - 1] == r && region[i + blockSize] == r
						&& CompactField.All( region, index[x + row - w], r ) && CompactField.All( region, index[x + row + w], r ) )
					{
						if ( r == 0 ) continue;
						spanCounts[r] += blockSize;
						regionArea[r] = area[i];
						continue;
					}
				}

				for ( int column = x + row; column < end + row; column++ )
				{
					int inColumn = 0;
					for ( int i = index[column], last = index[column + 1]; i < last; i++ )
					{
						int r = region[i];
						if ( r == 0 || r >= regionCount ) continue;

						spanCounts[r]++;
						regionArea[r] = area[i];
						columnRegions[inColumn++] = r;

						uint c = con[i];
						int west = (int)(c & 63), north = (int)((c >> 6) & 63), east = (int)((c >> 12) & 63), south = (int)((c >> 18) & 63);
						if ( west != 63 ) Neighbour( r, region[index[column - 1] + west], regionCount );
						if ( north != 63 ) Neighbour( r, region[index[column + w] + north], regionCount );
						if ( east != 63 ) Neighbour( r, region[index[column + 1] + east], regionCount );
						if ( south != 63 ) Neighbour( r, region[index[column - w] + south], regionCount );
					}

					// Regions stacked in one column overlap. The column before usually held the same stack.
					if ( inColumn < 2 || columnRegions.AsSpan( 0, inColumn ).SequenceEqual( lastColumnRegions.AsSpan( 0, lastInColumn ) ) ) continue;
					for ( int i = 0; i < inColumn - 1; i++ )
					{
						for ( int j = i + 1; j < inColumn; j++ )
						{
							int a = columnRegions[i], b = columnRegions[j];
							if ( a == b ) continue;
							overlaps.AddUnique( a, b );
							overlaps.AddUnique( b, a );
						}
					}
					(columnRegions, lastColumnRegions) = (lastColumnRegions, columnRegions);
					lastInColumn = inColumn;
				}
			}
		}
	}

	void Neighbour( int r, int other, int regionCount )
	{
		if ( other == r ) return;
		if ( other > 0 && other < regionCount ) connections.AddUnique( r, other );
		if ( (other & Border) != 0 ) touchesBorder[r] = true;
	}

	// Floods from each unassigned region over neighbours of the same area that do not overlap what the
	// flood has taken so far, drops small layers that do not reach the border and numbers the rest from 1.
	// Afterwards layer[region] is the final id.
	void MergeLayers( int regionCount, int minRegionCells, CompactField field )
	{
		Array.Clear( layer, 0, regionCount );

		int layerId = 1;
		for ( int root = 1; root < regionCount; root++ )
		{
			if ( layer[root] != 0 ) continue;
			layer[root] = (ushort)layerId;
			queue[0] = root;
			int queued = 1;
			for ( int head = 0; head < queued; head++ )
			{
				int current = queue[head];
				for ( int k = 0, n = connections.Count( current ); k < n; k++ )
				{
					int neighbour = connections.Item( current, k );
					if ( layer[neighbour] != 0 ) continue;
					if ( regionArea[current] != regionArea[neighbour] ) continue;
					if ( overlaps.Contains( root, neighbour ) ) continue;

					queue[queued++] = neighbour;
					layer[neighbour] = (ushort)layerId;
					for ( int o = 0, overlapCount = overlaps.Count( neighbour ); o < overlapCount; o++ ) overlaps.AddUnique( root, overlaps.Item( neighbour, o ) );
					spanCounts[root] += spanCounts[neighbour];
					spanCounts[neighbour] = 0;
					touchesBorder[root] |= touchesBorder[neighbour];
				}
			}
			layerId++;
		}

		Array.Clear( remap, 0, layerId );
		for ( int i = 0; i < regionCount; i++ )
		{
			if ( layer[i] != 0 && spanCounts[i] > 0 && spanCounts[i] < minRegionCells && !touchesBorder[i] ) remap[layer[i]] = -1;
		}

		int kept = 0;
		for ( int i = 1; i < layerId; i++ ) remap[i] = remap[i] == -1 ? 0 : ++kept;
		for ( int i = 0; i < regionCount; i++ ) layer[i] = (ushort)remap[layer[i]];
		field.RegionCount = kept;
	}

	/// <summary>
	/// One list of ints per owner, kept in insertion order. The lists are kept between builds.
	/// </summary>
	sealed class IntLists
	{
		int[][] items = [];
		int[] counts = [];

		public void Ensure( int owners )
		{
			if ( counts.Length >= owners ) return;
			Array.Resize( ref items, owners );
			Array.Resize( ref counts, owners );
		}

		public void Clear( int owner ) => counts[owner] = 0;
		public int Count( int owner ) => counts[owner];
		public int Item( int owner, int position ) => items[owner][position];

		public bool Contains( int owner, int value )
		{
			int count = counts[owner];
			return count > 0 && items[owner].AsSpan( 0, count ).Contains( value );
		}

		public void AddUnique( int owner, int value )
		{
			int count = counts[owner];
			var list = items[owner];
			if ( count > 0 && list.AsSpan( 0, count ).Contains( value ) ) return;
			if ( list == null || count == list.Length )
			{
				Array.Resize( ref list, Math.Max( count * 2, 8 ) );
				items[owner] = list;
			}

			list[count] = value;
			counts[owner] = count + 1;
		}
	}
}
