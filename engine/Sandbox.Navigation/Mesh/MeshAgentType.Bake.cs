using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;

namespace Nav;

/// <summary>
/// One agent type's part of a bake (NavMesh.Bake.cs has the layout): its settings, and its tiles as their
/// polygons and the cells their vertices sit on. Everything else a tile holds follows from those and is
/// made again at load: the neighbours of every polygon, the tree, the portal edges, bounds and area.
/// </summary>
internal sealed partial class MeshAgentType
{
	internal const int SettingsBytes = 44;

	// The least a tile takes: two coordinates, two counts, and a polygon of three corners over three vertices
	internal const int MinTileBytes = 4 + 4 + 9;

	// The most a tile takes: coordinates, counts and hash, then a byte and six indices of up to three
	// bytes per polygon, and three numbers of up to five bytes per vertex
	internal static long MostBytes( Tile tile ) => 28 + tile.Polys.Length * 19L + tile.Verts.Length * 15L;

	// The tiles of a state in the order of y, then x, whatever the slots are, so equal meshes give equal
	// bytes; and the most bytes they can take.
	internal static Tile[] SortedTiles( MeshState s, out long most )
	{
		var tiles = new Tile[s.TileCount];
		int count = 0;
		most = 0;
		foreach ( var page in s.Pages )
		{
			if ( page == null ) continue;
			foreach ( var tile in page )
			{
				if ( tile == null ) continue;
				tiles[count++] = tile;
				most += MostBytes( tile );
			}
		}
		Array.Sort( tiles, static ( a, b ) => a.Y != b.Y ? a.Y.CompareTo( b.Y ) : a.X.CompareTo( b.X ) );
		return tiles;
	}

	internal static void WriteSettings( Span<byte> head, in NavSettings settings )
	{
		BinaryPrimitives.WriteSingleLittleEndian( head, settings.CellSize );
		BinaryPrimitives.WriteSingleLittleEndian( head[4..], settings.CellHeight );
		BinaryPrimitives.WriteInt32LittleEndian( head[8..], settings.TileSize );
		BinaryPrimitives.WriteSingleLittleEndian( head[12..], settings.AgentHeight );
		BinaryPrimitives.WriteSingleLittleEndian( head[16..], settings.AgentRadius );
		BinaryPrimitives.WriteSingleLittleEndian( head[20..], settings.AgentClimb );
		BinaryPrimitives.WriteSingleLittleEndian( head[24..], settings.AgentMaxSlope );
		BinaryPrimitives.WriteInt32LittleEndian( head[28..], settings.MaxEdgeCells );
		BinaryPrimitives.WriteInt32LittleEndian( head[32..], settings.MinRegionCells );
		BinaryPrimitives.WriteSingleLittleEndian( head[36..], settings.MaxEdgeError );
		BinaryPrimitives.WriteInt32LittleEndian( head[40..], settings.MaxTilePolygons );
	}

	internal static NavSettings ReadSettings( ReadOnlySpan<byte> data )
	{
		return new NavSettings
		{
			CellSize = BinaryPrimitives.ReadSingleLittleEndian( data ),
			CellHeight = BinaryPrimitives.ReadSingleLittleEndian( data[4..] ),
			TileSize = BinaryPrimitives.ReadInt32LittleEndian( data[8..] ),
			AgentHeight = BinaryPrimitives.ReadSingleLittleEndian( data[12..] ),
			AgentRadius = BinaryPrimitives.ReadSingleLittleEndian( data[16..] ),
			AgentClimb = BinaryPrimitives.ReadSingleLittleEndian( data[20..] ),
			AgentMaxSlope = BinaryPrimitives.ReadSingleLittleEndian( data[24..] ),
			MaxEdgeCells = BinaryPrimitives.ReadInt32LittleEndian( data[28..] ),
			MinRegionCells = BinaryPrimitives.ReadInt32LittleEndian( data[32..] ),
			MaxEdgeError = BinaryPrimitives.ReadSingleLittleEndian( data[36..] ),
			MaxTilePolygons = BinaryPrimitives.ReadInt32LittleEndian( data[40..] )
		};
	}

	// Writes the tiles, each as one span, with the volume-set hash each was built with: hashes is the
	// table published with or before the state the tiles come from.
	internal void WriteTiles( IBufferWriter<byte> output, Tile[] tiles, (int Y, int X, ulong Hash)[] hashes )
	{
		double perCell = 1.0 / settings.CellSize, perHeight = 1.0 / settings.CellHeight;
		long lastX = 0, lastY = 0;
		int next = 0;
		foreach ( var tile in tiles )
		{
			var to = output.GetSpan( (int)MostBytes( tile ) );
			int at = 0;
			PutSigned( to, ref at, tile.X - lastX );
			PutSigned( to, ref at, tile.Y - lastY );
			lastX = tile.X;
			lastY = tile.Y;

			// Both in the order of y, then x
			while ( next < hashes.Length && (hashes[next].Y, hashes[next].X).CompareTo( (tile.Y, tile.X) ) < 0 ) next++;
			ulong hash = next < hashes.Length && hashes[next].Y == tile.Y && hashes[next].X == tile.X ? hashes[next].Hash : 0;
			Put( to, ref at, (ulong)tile.Polys.Length * 2 + (hash != 0 ? 1u : 0u) );
			Put( to, ref at, (ulong)tile.Verts.Length );
			if ( hash != 0 )
			{
				BinaryPrimitives.WriteUInt64LittleEndian( to[at..], hash );
				at += 8;
			}

			foreach ( ref readonly var poly in tile.Polys.AsSpan() ) to[at++] = (byte)(poly.VertCount - 3 | poly.Area << 2);
			int last = 0;
			foreach ( ref readonly var poly in tile.Polys.AsSpan() )
			{
				for ( int j = 0; j < poly.VertCount; j++ )
				{
					PutSigned( to, ref at, poly.Verts[j] - last );
					last = poly.Verts[j];
				}
			}

			// The same number for a cell as the builder makes of it: from its place in the world
			long cellX = (long)tile.X * settings.TileSize, cellY = (long)tile.Y * settings.TileSize;
			var verts = tile.Verts;
			long before = 0;
			foreach ( var v in verts )
			{
				long cell = Math.Clamp( CellOf( v.X, perCell ) - cellX, 0, settings.TileSize );
				PutSigned( to, ref at, cell - before );
				before = cell;
			}
			before = 0;
			foreach ( var v in verts )
			{
				long cell = Math.Clamp( CellOf( v.Y, perCell ) - cellY, 0, settings.TileSize );
				PutSigned( to, ref at, cell - before );
				before = cell;
			}
			before = 0;
			foreach ( var v in verts )
			{
				long cell = Math.Clamp( CellOf( v.Z, perHeight ), int.MinValue, int.MaxValue );
				PutSigned( to, ref at, cell - before );
				before = cell;
			}
			output.Advance( at );
		}
	}

	// perCell is one over the cell size. Every vertex lies on a cell, so the product rounds to the cell as the quotient would.
	static long CellOf( float value, double perCell ) => (long)Math.Clamp( Math.Round( value * perCell ), -1e15, 1e15 );

	static void Put( Span<byte> to, ref int at, ulong value )
	{
		for ( ; value >= 0x80; value >>= 7 ) to[at++] = (byte)(value | 0x80);
		to[at++] = (byte)value;
	}

	static void PutSigned( Span<byte> to, ref int at, long value ) => Put( to, ref at, (ulong)((value << 1) ^ (value >> 63)) );

	// Reads the numbers of a bake. Past the end it hands out zeros and remembers that it did, so the
	// loops over it need no checks of their own.
	internal ref struct BakeReader( ReadOnlySpan<byte> data, int at )
	{
		readonly ReadOnlySpan<byte> data = data;
		int at = at;

		public bool Failed;
		public readonly int Left => data.Length - at;

		public uint Byte()
		{
			if ( at < data.Length ) return data[at++];
			Failed = true;
			return 0;
		}

		// Five bytes hold every number a bake has
		public ulong Number()
		{
			ulong value = 0;
			for ( int shift = 0; shift < 35; shift += 7 )
			{
				uint part = Byte();
				value |= (ulong)(part & 0x7F) << shift;
				if ( part < 0x80 ) return value;
			}
			Failed = true;
			return 0;
		}

		public long Signed()
		{
			ulong value = Number();
			return (long)(value >> 1) ^ -(long)(value & 1);
		}

		public ulong Hash()
		{
			if ( Left < 8 )
			{
				Failed = true;
				return 0;
			}
			at += 8;
			return BinaryPrimitives.ReadUInt64LittleEndian( data[(at - 8)..] );
		}
	}

	// Reads count tiles of one agent type, in strictly increasing order, each with its volume-set hash
	// (0 for none). Null when anything is wrong.
	internal static Tile[]? ReadTiles( ref BakeReader reader, in NavSettings settings, int count, out ulong[] hashes )
	{
		var loader = new TileLoader( settings );
		var tiles = new Tile[count];
		hashes = new ulong[count];
		long x = 0, y = 0;
		for ( int i = 0; i < count; i++ )
		{
			long nextX = x + reader.Signed(), nextY = y + reader.Signed();
			// In order, so no tile is there twice
			if ( i > 0 && (nextY < y || (nextY == y && nextX <= x)) ) return null;
			if ( nextX != (int)nextX || nextY != (int)nextY ) return null;
			x = nextX;
			y = nextY;

			var tile = loader.Read( ref reader, (int)x, (int)y, out hashes[i] );
			if ( tile == null ) return null;
			tiles[i] = tile;
		}
		return tiles;
	}

	// A load: the tiles of the bake and the volume set each was built with.
	internal void Loaded( Tile[] tiles, ulong[] hashes )
	{
		CommitTiles( tiles );
		SetVolumeHashes( tiles, hashes );
	}
	// Reads tiles and builds what a bake leaves out, the way the builder's last stages do: neighbours as
	// PolyBuilder finds them, tree and portal edges as TileAssembler lays them out. The polygons are in
	// the tree's order already, so the tree needs no sorting.
	sealed class TileLoader( NavSettings settings )
	{
		struct Edge
		{
			public ushort End, Poly0, Poly1;
			public byte Edge0, Edge1;
		}

		ushort[] xs = [], ys = [];
		int[] firstEdge = [], nextEdge = [];
		Edge[] edges = [];
		Vector3[] mins = [], maxs = [];

		public Tile? Read( ref BakeReader reader, int tileX, int tileY, out ulong hash )
		{
			hash = 0;
			ulong head = reader.Number(), vertNumber = reader.Number();
			if ( head < 2 || head >> 1 > (ulong)settings.MaxTilePolygons || vertNumber < 3 || vertNumber > Poly.MaxVertsPerTile ) return null;
			int polyCount = (int)(head >> 1), vertCount = (int)vertNumber;

			// Before anything is allocated: a polygon takes at least four bytes, a vertex three
			if ( polyCount * 4L + vertCount * 3L > reader.Left ) return null;
			if ( (head & 1) != 0 ) hash = reader.Hash();

			var polys = new Poly[polyCount];
			int corners = 0;
			for ( int p = 0; p < polyCount; p++ )
			{
				uint packed = reader.Byte();
				polys[p].VertCount = (byte)((packed & 3) + 3);
				polys[p].Area = (byte)(packed >> 2);
				corners += polys[p].VertCount;
			}
			long index = 0;
			for ( int p = 0; p < polyCount; p++ )
			{
				ref var poly = ref polys[p];
				for ( int j = 0; j < poly.VertCount; j++ )
				{
					index += reader.Signed();
					if ( (ulong)index >= (ulong)vertCount ) return null;
					poly.Verts[j] = (ushort)index;
				}
			}

			if ( xs.Length < vertCount )
			{
				xs = new ushort[vertCount + vertCount / 2];
				ys = new ushort[xs.Length];
				firstEdge = new int[xs.Length];
			}
			int size = settings.TileSize;
			long cell = 0;
			for ( int v = 0; v < vertCount; v++ )
			{
				cell += reader.Signed();
				if ( (ulong)cell > (ulong)size ) return null;
				xs[v] = (ushort)cell;
			}
			cell = 0;
			for ( int v = 0; v < vertCount; v++ )
			{
				cell += reader.Signed();
				if ( (ulong)cell > (ulong)size ) return null;
				ys[v] = (ushort)cell;
			}

			var verts = new Vector3[vertCount];
			long cellX = (long)tileX * size, cellY = (long)tileY * size;
			cell = 0;
			for ( int v = 0; v < vertCount; v++ )
			{
				cell += reader.Signed();
				if ( cell != (int)cell ) return null;
				var vertex = new Vector3( (float)((cellX + xs[v]) * (double)settings.CellSize), (float)((cellY + ys[v]) * (double)settings.CellSize), (float)(cell * (double)settings.CellHeight) );
				if ( !Geo.IsFinite( vertex ) ) return null;
				verts[v] = vertex;
			}
			if ( reader.Failed ) return null;

			Neighbours( polys, vertCount, corners );
			var tree = Tree( tileX, tileY, polys, verts, out var origin );
			return new Tile( tileX, tileY, polys, verts, tree, Portals( polys, verts, origin ), settings );
		}

		// Every edge is filed under its lower vertex by the polygon that walks it upwards, then matched by
		// the polygon that walks it downwards. An edge nobody matched that runs along the tile's edge leads
		// into the tile beyond.
		void Neighbours( Poly[] polys, int vertCount, int corners )
		{
			if ( edges.Length < corners )
			{
				edges = new Edge[corners + corners / 2];
				nextEdge = new int[edges.Length];
			}
			Array.Fill( firstEdge, -1, 0, vertCount );

			int edgeCount = 0;
			for ( int p = 0; p < polys.Length; p++ )
			{
				ref var poly = ref polys[p];
				for ( int j = 0; j < poly.VertCount; j++ )
				{
					poly.Neis[j] = Poly.Wall;
					ushort v0 = poly.Verts[j], v1 = poly.Verts[j + 1 < poly.VertCount ? j + 1 : 0];
					if ( v0 >= v1 ) continue;
					edges[edgeCount] = new Edge { End = v1, Poly0 = (ushort)p, Edge0 = (byte)j, Poly1 = (ushort)p };
					nextEdge[edgeCount] = firstEdge[v0];
					firstEdge[v0] = edgeCount++;
				}
			}

			for ( int p = 0; p < polys.Length; p++ )
			{
				ref readonly var poly = ref polys[p];
				for ( int j = 0; j < poly.VertCount; j++ )
				{
					ushort v0 = poly.Verts[j], v1 = poly.Verts[j + 1 < poly.VertCount ? j + 1 : 0];
					if ( v0 <= v1 ) continue;
					for ( int e = firstEdge[v1]; e != -1; e = nextEdge[e] )
					{
						ref var edge = ref edges[e];
						if ( edge.End != v0 || edge.Poly0 != edge.Poly1 ) continue;
						edge.Poly1 = (ushort)p;
						edge.Edge1 = (byte)j;
						break;
					}
				}
			}

			for ( int e = 0; e < edgeCount; e++ )
			{
				ref readonly var edge = ref edges[e];
				if ( edge.Poly0 == edge.Poly1 ) continue;
				polys[edge.Poly0].Neis[edge.Edge0] = edge.Poly1;
				polys[edge.Poly1].Neis[edge.Edge1] = edge.Poly0;
			}

			// Sides as in Poly: 0 = +x, 1 = +y, 2 = -x, 3 = -y. An edge without length is left a wall.
			int size = settings.TileSize;
			for ( int p = 0; p < polys.Length; p++ )
			{
				ref var poly = ref polys[p];
				for ( int j = 0; j < poly.VertCount; j++ )
				{
					if ( poly.Neis[j] != Poly.Wall ) continue;
					int a = poly.Verts[j], b = poly.Verts[j + 1 < poly.VertCount ? j + 1 : 0];
					if ( xs[a] == xs[b] && ys[a] == ys[b] ) continue;
					if ( xs[a] == size && xs[b] == size ) poly.Neis[j] = Poly.Border;
					else if ( ys[a] == size && ys[b] == size ) poly.Neis[j] = Poly.Border + 1;
					else if ( xs[a] == 0 && xs[b] == 0 ) poly.Neis[j] = Poly.Border + 2;
					else if ( ys[a] == 0 && ys[b] == 0 ) poly.Neis[j] = Poly.Border + 3;
				}
			}
		}

		BvNode[] Tree( int tileX, int tileY, Poly[] polys, Vector3[] verts, out Vector3 origin )
		{
			int count = polys.Length;
			if ( mins.Length < count )
			{
				mins = new Vector3[count + count / 2];
				maxs = new Vector3[mins.Length];
			}

			float minZ = float.MaxValue, maxZ = float.MinValue;
			for ( int p = 0; p < count; p++ )
			{
				ref readonly var poly = ref polys[p];
				Vector3 min = verts[poly.Verts[0]], max = min;
				for ( int j = 1; j < poly.VertCount; j++ )
				{
					min = Vector3.Min( min, verts[poly.Verts[j]] );
					max = Vector3.Max( max, verts[poly.Verts[j]] );
				}
				mins[p] = min;
				maxs[p] = max;
				minZ = MathF.Min( minZ, min.Z );
				maxZ = MathF.Max( maxZ, max.Z );
			}

			Tile.TreeFrame( tileX, tileY, minZ, maxZ, settings, out origin, out var cell );
			// Every leaf but the last is full, and every other node has two children
			var nodes = new BvNode[(count + BvNode.LeafSize - 1) / BvNode.LeafSize * 2 - 1];
			int next = 0;
			Node( nodes, ref next, 0, count, origin, Vector3.One / cell );
			return nodes;
		}

		void Node( BvNode[] nodes, ref int next, int lo, int hi, Vector3 origin, Vector3 perCell )
		{
			int index = next++;
			Vector3 min = mins[lo], max = maxs[lo];
			for ( int i = lo + 1; i < hi; i++ )
			{
				min = Vector3.Min( min, mins[i] );
				max = Vector3.Max( max, maxs[i] );
			}

			var low = Vector3.Clamp( (min - origin) * perCell, Vector3.Zero, new Vector3( 65535 ) );
			var high = Vector3.Clamp( (max - origin) * perCell, Vector3.Zero, new Vector3( 65535 ) );
			ref var node = ref nodes[index];
			node.MinX = (ushort)MathF.Floor( low.X );
			node.MinY = (ushort)MathF.Floor( low.Y );
			node.MinZ = (ushort)MathF.Floor( low.Z );
			node.MaxX = (ushort)MathF.Ceiling( high.X );
			node.MaxY = (ushort)MathF.Ceiling( high.Y );
			node.MaxZ = (ushort)MathF.Ceiling( high.Z );

			if ( hi - lo <= BvNode.LeafSize )
			{
				node.First = (ushort)lo;
				node.Count = (ushort)(hi - lo);
				return;
			}

			// The split TileAssembler makes: the left half takes a multiple of the leaf size
			int middle = lo + (((hi - lo + 1) / 2 + BvNode.LeafSize - 1) & ~(BvNode.LeafSize - 1));
			Node( nodes, ref next, lo, middle, origin, perCell );
			Node( nodes, ref next, middle, hi, origin, perCell );
			nodes[index].First = BvNode.Inner;
			nodes[index].Count = (ushort)(next - index);
		}

		PortalEdge[] Portals( Poly[] polys, Vector3[] verts, Vector3 origin )
		{
			int count = 0;
			foreach ( ref readonly var poly in polys.AsSpan() )
			{
				for ( int j = 0; j < poly.VertCount; j++ )
				{
					if ( poly.Neis[j] >= Poly.Border && poly.Neis[j] != Poly.Wall ) count++;
				}
			}
			if ( count == 0 ) return [];

			var portals = new PortalEdge[count];
			count = 0;
			for ( int p = 0; p < polys.Length; p++ )
			{
				ref readonly var poly = ref polys[p];
				for ( int j = 0; j < poly.VertCount; j++ )
				{
					if ( poly.Neis[j] < Poly.Border || poly.Neis[j] == Poly.Wall ) continue;
					int side = poly.Neis[j] - Poly.Border;
					var a = verts[poly.Verts[j]];
					var b = verts[poly.Verts[j + 1 < poly.VertCount ? j + 1 : 0]];
					bool alongY = (side & 1) == 0;
					float alongA = ((alongY ? a.Y : a.X) - (alongY ? origin.Y : origin.X)) / settings.CellSize;
					float alongB = ((alongY ? b.Y : b.X) - (alongY ? origin.Y : origin.X)) / settings.CellSize;
					portals[count++] = new PortalEdge
					{
						Poly = (ushort)p,
						Edge = (byte)j,
						Side = (byte)side,
						Lo = (ushort)Math.Clamp( MathF.Round( MathF.Min( alongA, alongB ) ), 0, 65535 ),
						Hi = (ushort)Math.Clamp( MathF.Round( MathF.Max( alongA, alongB ) ), 0, 65535 )
					};
				}
			}
			return portals;
		}
	}
}
