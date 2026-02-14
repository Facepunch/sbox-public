using System.Runtime.InteropServices;

namespace Sandbox.Navigation.Generation;

[SkipHotload]
internal static class RegionBuilder
{
	/// <summary>
	/// Reusable context for region building. Cache and reuse to avoid allocations.
	/// </summary>
	[SkipHotload]
	public sealed class RegionBuilderContext
	{
		public List<int> SpanCounts = new( 1024 );
		public List<ushort> Ids = new( 1024 );
		public List<int> Areas = new( 1024 );
		public List<int> YMins = new( 1024 );
		public List<int> YMaxs = new( 1024 );
		public List<byte> Flags = new( 1024 );
		public List<List<int>> Connections = new( 1024 );
		public List<List<int>> Floors = new( 1024 );
		public List<int> LRegs = new( 64 );
		public List<int> Stack = new( 256 );

		public void Init( int n )
		{
			CollectionsMarshal.SetCount( SpanCounts, n );
			CollectionsMarshal.SetCount( Ids, n );
			CollectionsMarshal.SetCount( Areas, n );
			CollectionsMarshal.SetCount( YMins, n );
			CollectionsMarshal.SetCount( YMaxs, n );
			CollectionsMarshal.SetCount( Flags, n );

			CollectionsMarshal.AsSpan( SpanCounts ).Clear();
			CollectionsMarshal.AsSpan( Flags ).Clear();
			CollectionsMarshal.AsSpan( Areas ).Fill( Constants.NULL_AREA );
			CollectionsMarshal.AsSpan( YMins ).Fill( 0xffff );
			CollectionsMarshal.AsSpan( YMaxs ).Clear();

			while ( Connections.Count < n ) Connections.Add( new( 8 ) );
			while ( Floors.Count < n ) Floors.Add( new( 4 ) );

			for ( int i = 0; i < n; i++ )
			{
				Ids[i] = (ushort)i;
				Connections[i].Clear();
				Floors[i].Clear();
			}
		}
	}

	[SkipHotload]
	public struct SweepSpan
	{
		public ushort Rid;   // Row id
		public ushort Id;    // Region id
		public ushort Ns;    // Number of samples
		public ushort Nei;   // Neighbor id
	}

	private static bool MergeAndFilterLayerRegions( int minRegionArea, ref ushort maxRegionId, CompactHeightfield chf, Span<ushort> srcReg, RegionBuilderContext ctx )
	{
		int w = chf.Width;
		int h = chf.Height;
		int nreg = maxRegionId + 1;

		ctx.Init( nreg );

		var cells = chf.Cells;
		var spans = chf.Spans;
		var areas = chf.Areas;

		for ( int y = 0; y < h; ++y )
		{
			for ( int x = 0; x < w; ++x )
			{
				CompactCell c = cells[x + y * w];
				ctx.LRegs.Clear();

				for ( int i = (int)c.Index, ni = (int)(c.Index + c.Count); i < ni; ++i )
				{
					CompactSpan s = spans[i];
					int area = areas[i];
					ushort ri = srcReg[i];
					if ( ri == 0 || ri >= nreg ) continue;

					ctx.SpanCounts[ri]++;
					ctx.Areas[ri] = area;
					int startY = s.StartY;
					if ( startY < ctx.YMins[ri] ) ctx.YMins[ri] = startY;
					if ( startY > ctx.YMaxs[ri] ) ctx.YMaxs[ri] = startY;

					// Collect all region layers
					ctx.LRegs.Add( ri );

					// Update neighbors - unrolled for 4 directions
					var riConns = ctx.Connections[ri];
					int con0 = Utils.GetCon( s, 0 );
					if ( con0 != Constants.NOT_CONNECTED )
					{
						int ai = (int)cells[x - 1 + y * w].Index + con0;
						ushort rai = srcReg[ai];
						if ( rai > 0 && rai < nreg && rai != ri && !riConns.Contains( rai ) )
							riConns.Add( rai );
						if ( (rai & ContourRegionFlags.BORDER_REG) != 0 )
							ctx.Flags[ri] |= 2;
					}

					int con1 = Utils.GetCon( s, 1 );
					if ( con1 != Constants.NOT_CONNECTED )
					{
						int ai = (int)cells[x + (y + 1) * w].Index + con1;
						ushort rai = srcReg[ai];
						if ( rai > 0 && rai < nreg && rai != ri && !riConns.Contains( rai ) )
							riConns.Add( rai );
						if ( (rai & ContourRegionFlags.BORDER_REG) != 0 )
							ctx.Flags[ri] |= 2;
					}

					int con2 = Utils.GetCon( s, 2 );
					if ( con2 != Constants.NOT_CONNECTED )
					{
						int ai = (int)cells[x + 1 + y * w].Index + con2;
						ushort rai = srcReg[ai];
						if ( rai > 0 && rai < nreg && rai != ri && !riConns.Contains( rai ) )
							riConns.Add( rai );
						if ( (rai & ContourRegionFlags.BORDER_REG) != 0 )
							ctx.Flags[ri] |= 2;
					}

					int con3 = Utils.GetCon( s, 3 );
					if ( con3 != Constants.NOT_CONNECTED )
					{
						int ai = (int)cells[x + (y - 1) * w].Index + con3;
						ushort rai = srcReg[ai];
						if ( rai > 0 && rai < nreg && rai != ri && !riConns.Contains( rai ) )
							riConns.Add( rai );
						if ( (rai & ContourRegionFlags.BORDER_REG) != 0 )
							ctx.Flags[ri] |= 2;
					}
				}

				// Update overlapping regions
				for ( int i = 0; i < ctx.LRegs.Count - 1; ++i )
				{
					for ( int j = i + 1; j < ctx.LRegs.Count; ++j )
					{
						int li = ctx.LRegs[i], lj = ctx.LRegs[j];
						if ( li != lj )
						{
							var fi = ctx.Floors[li];
							var fj = ctx.Floors[lj];
							if ( !fi.Contains( lj ) ) fi.Add( lj );
							if ( !fj.Contains( li ) ) fj.Add( li );
						}
					}
				}
			}
		}

		// Create 2D layers from regions
		ushort layerId = 1;

		for ( int i = 0; i < nreg; ++i )
			ctx.Ids[i] = 0;

		// Merge monotone regions to create non-overlapping areas
		ctx.Stack.Clear();

		for ( int i = 1; i < nreg; ++i )
		{
			if ( ctx.Ids[i] != 0 )
				continue;

			ctx.Ids[i] = layerId;

			ctx.Stack.Clear();
			ctx.Stack.Add( i );
			int stackHead = 0;

			while ( stackHead < ctx.Stack.Count )
			{
				int idx = ctx.Stack[stackHead++];

				foreach ( int nei in ctx.Connections[idx] )
				{
					if ( ctx.Ids[nei] != 0 )
						continue;
					if ( ctx.Areas[idx] != ctx.Areas[nei] )
						continue;

					// Skip if the neighbour is overlapping root region
					if ( ctx.Floors[i].Contains( nei ) )
						continue;

					ctx.Stack.Add( nei );
					ctx.Ids[nei] = layerId;

					// Merge current layers to root
					var rootFloors = ctx.Floors[i];
					foreach ( int floor in ctx.Floors[nei] )
						if ( !rootFloors.Contains( floor ) ) rootFloors.Add( floor );

					ctx.YMins[i] = Math.Min( ctx.YMins[i], ctx.YMins[nei] );
					ctx.YMaxs[i] = Math.Max( ctx.YMaxs[i], ctx.YMaxs[nei] );
					ctx.SpanCounts[i] += ctx.SpanCounts[nei];
					ctx.SpanCounts[nei] = 0;
					ctx.Flags[i] |= (byte)(ctx.Flags[nei] & 2); // Merge ConnectsToBorder flag
				}
			}

			layerId++;
		}

		// Build layerId -> newId remap
		while ( ctx.LRegs.Count < layerId ) ctx.LRegs.Add( 0 );
		for ( int i = 0; i < layerId; ++i )
			ctx.LRegs[i] = 0;

		// Mark layers that should be removed (small regions not connecting to border)
		for ( int i = 0; i < nreg; ++i )
		{
			ushort id = ctx.Ids[i];
			if ( id == 0 || (id & ContourRegionFlags.BORDER_REG) != 0 ) continue;

			// If small and not connected to border, mark for removal
			if ( ctx.SpanCounts[i] > 0 && ctx.SpanCounts[i] < minRegionArea && (ctx.Flags[i] & 2) == 0 )
				ctx.LRegs[id] = -1;
		}

		// Assign new IDs to layers that are kept
		ushort regIdGen = 0;
		for ( int i = 1; i < layerId; ++i )
		{
			if ( ctx.LRegs[i] == -1 )
				ctx.LRegs[i] = 0;
			else
				ctx.LRegs[i] = ++regIdGen;
		}

		// Apply remap to all regions
		for ( int i = 0; i < nreg; ++i )
		{
			ushort id = ctx.Ids[i];
			if ( id == 0 || (id & ContourRegionFlags.BORDER_REG) != 0 ) continue;
			ctx.Ids[i] = (ushort)ctx.LRegs[id];
		}
		maxRegionId = regIdGen;

		// Remap regions
		for ( int i = 0; i < chf.SpanCount; ++i )
		{
			if ( (srcReg[i] & ContourRegionFlags.BORDER_REG) == 0 )
				srcReg[i] = ctx.Ids[srcReg[i]];
		}

		return true;
	}

	private static void PaintRectRegion( int minx, int maxx, int miny, int maxy, ushort regId,
									  CompactHeightfield chf, Span<ushort> srcReg )
	{
		int w = chf.Width;
		for ( int y = miny; y < maxy; ++y )
		{
			for ( int x = minx; x < maxx; ++x )
			{
				CompactCell c = chf.Cells[x + y * w];
				for ( int i = (int)c.Index, ni = (int)(c.Index + c.Count); i < ni; ++i )
				{
					if ( chf.Areas[i] != Constants.NULL_AREA )
						srcReg[i] = regId;
				}
			}
		}
	}

	public static bool BuildLayerRegions( CompactHeightfield chf, int borderSize, int minRegionArea, List<int> prevCache, RegionBuilderContext ctx )
	{
		int w = chf.Width;
		int h = chf.Height;
		ushort id = 1;

		using var pooledSrcReg = new PooledSpan<ushort>( chf.SpanCount );
		var srcRegs = pooledSrcReg.Span;
		srcRegs.Fill( 0 );

		var nsweeps = Math.Max( chf.Width, chf.Height );
		using var pooledSweeps = new PooledSpan<SweepSpan>( nsweeps );
		var sweeps = pooledSweeps.Span;
		sweeps.Clear();

		// Cache array references for hot path
		var cells = chf.Cells;
		var spans = chf.Spans;
		var areas = chf.Areas;

		// Mark border regions
		if ( borderSize > 0 )
		{
			// Make sure border will not overflow
			int bw = Math.Min( w, borderSize );
			int bh = Math.Min( h, borderSize );
			// Paint regions
			PaintRectRegion( 0, bw, 0, h, (ushort)(id | ContourRegionFlags.BORDER_REG), chf, srcRegs ); id++;
			PaintRectRegion( w - bw, w, 0, h, (ushort)(id | ContourRegionFlags.BORDER_REG), chf, srcRegs ); id++;
			PaintRectRegion( 0, w, 0, bh, (ushort)(id | ContourRegionFlags.BORDER_REG), chf, srcRegs ); id++;
			PaintRectRegion( 0, w, h - bh, h, (ushort)(id | ContourRegionFlags.BORDER_REG), chf, srcRegs ); id++;
		}

		chf.BorderSize = borderSize;

		// Sweep one line at a time
		for ( int y = borderSize; y < h - borderSize; ++y )
		{
			// Collect spans from this row
			// Ensure capacity and fill with zeros efficiently
			int requiredSize = id + 1;
			CollectionsMarshal.SetCount( prevCache, Math.Max( prevCache.Count, requiredSize ) );
			// Clear the used portion using span (vectorized memset)
			CollectionsMarshal.AsSpan( prevCache ).Slice( 0, requiredSize ).Clear();
			ushort rid = 1;

			int yOffset = y * w;

			for ( int x = borderSize; x < w - borderSize; ++x )
			{
				CompactCell c = cells[x + yOffset];

				for ( int i = (int)c.Index, ni = (int)(c.Index + c.Count); i < ni; ++i )
				{
					CompactSpan s = spans[i];
					int areaI = areas[i];
					if ( areaI == Constants.NULL_AREA ) continue;

					// -x direction (dir=0)
					ushort previd = 0;
					int con0 = Utils.GetCon( s, 0 );
					if ( con0 != Constants.NOT_CONNECTED )
					{
						int ai = (int)cells[x - 1 + yOffset].Index + con0;
						ushort srcAi = srcRegs[ai];
						if ( (srcAi & ContourRegionFlags.BORDER_REG) == 0 && areaI == areas[ai] )
							previd = srcAi;
					}

					if ( previd == 0 )
					{
						previd = rid++;
						sweeps[previd].Rid = previd;
						sweeps[previd].Ns = 0;
						sweeps[previd].Nei = 0;
					}

					// -y direction (dir=3)
					int con3 = Utils.GetCon( s, 3 );
					if ( con3 != Constants.NOT_CONNECTED )
					{
						int ai = (int)cells[x + yOffset - w].Index + con3;
						ushort srcAi = srcRegs[ai];
						if ( srcAi != 0 && (srcAi & ContourRegionFlags.BORDER_REG) == 0 && areaI == areas[ai] )
						{
							if ( sweeps[previd].Nei == 0 || sweeps[previd].Nei == srcAi )
							{
								sweeps[previd].Nei = srcAi;
								sweeps[previd].Ns++;
								prevCache[srcAi]++;
							}
							else
							{
								sweeps[previd].Nei = 0xffff; // RC_NULL_NEI
							}
						}
					}

					srcRegs[i] = previd;
				}
			}

			// Create unique ID
			for ( int i = 1; i < rid; ++i )
			{
				if ( sweeps[i].Nei != 0xffff && sweeps[i].Nei != 0 &&
					prevCache[sweeps[i].Nei] == sweeps[i].Ns )
				{
					sweeps[i].Id = sweeps[i].Nei;
				}
				else
				{
					sweeps[i].Id = id++;
				}
			}

			// Remap IDs
			for ( int x = borderSize; x < w - borderSize; ++x )
			{
				CompactCell c = cells[x + yOffset];

				for ( int i = (int)c.Index, ni = (int)(c.Index + c.Count); i < ni; ++i )
				{
					ushort sr = srcRegs[i];
					if ( sr > 0 && sr < rid )
						srcRegs[i] = sweeps[sr].Id;
				}
			}
		}

		// Merge monotone regions to layers and remove small regions
		chf.MaxRegions = id;
		if ( !MergeAndFilterLayerRegions( minRegionArea, ref chf.MaxRegions, chf, srcRegs, ctx ) )
		{
			return false;
		}

		// Store the result
		for ( int i = 0; i < chf.SpanCount; ++i ) spans[i].Region = srcRegs[i];

		return true;
	}
}
