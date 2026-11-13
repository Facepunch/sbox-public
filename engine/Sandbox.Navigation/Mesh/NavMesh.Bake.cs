using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;

namespace Nav;

/// <summary>
/// The bake: finished tiles, with no links, no volumes and no rebuild cache. A tile is written as its
/// polygons and the cells its vertices sit on. Everything else a tile holds follows from those and is
/// made again at load: the neighbours of every polygon, the tree, the portal edges, bounds and area.
/// The bytes are small numbers meant for a general codec such as LZ4 on top.
///   Header   "NAVB", version, the settings field by field, tile count, all four bytes wide
///   Tiles    in the order of y, then x. Numbers are varints, seven bits a byte with the low bits
///            first, and a signed one keeps its sign in the lowest bit. Per tile:
///              x and y, as differences to the tile before
///              polygon count times two, plus one when a volume-set hash follows
///              vertex count
///              the volume-set hash, eight bytes, when there is one
///              a byte per polygon: vertex count less three in the low two bits, the area above
///              the vertex index of every polygon corner, as the difference to the corner before
///              x of every vertex, then y of every vertex, then z of every vertex, each as the
///              difference to the vertex before: x and y in cells from the tile's corner, z in cell
///              heights from zero
/// </summary>
public sealed partial class NavMesh
{
	const uint BakeMagic = 0x4256414E, BakeVersion = 2;
	const int HeaderBytes = 52;

	// The least a tile takes: two coordinates, two counts, and a polygon of three corners over three vertices
	const int MinTileBytes = 4 + 4 + 9;

	// The most a tile takes: coordinates, counts and hash, then a byte and six indices of up to three
	// bytes per polygon, and three numbers of up to five bytes per vertex
	static long MostBytes( Tile tile ) => 28 + tile.Polys.Length * 19L + tile.Verts.Length * 15L;

	/// <summary>
	/// Writes the bake. False, with nothing written, when it could exceed 2 GB. Call it on the owner
	/// thread. The tiles written are one state, but the volume set each was built with is what the owner
	/// holds at that moment: a save that overlaps a Commit can write a tile next to the volume set of
	/// its next build, and a mesh loaded from that takes the tile for up to date. The writer has to hand
	/// out the header as one span and each tile as one span.
	/// A vertex is written as the cell it sits on, which is where the builder puts every vertex, so a
	/// loaded tile is the built one bit for bit.
	/// </summary>
	public bool Save( IBufferWriter<byte> output )
	{
		var s = State;
		var tiles = new Tile[s.TileCount];
		int count = 0;
		long most = HeaderBytes;
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
		if ( most > int.MaxValue ) return false;

		// One order whatever the slots are, so equal meshes give equal bytes
		Array.Sort( tiles, static ( a, b ) => a.Y != b.Y ? a.Y.CompareTo( b.Y ) : a.X.CompareTo( b.X ) );

		var head = output.GetSpan( HeaderBytes )[..HeaderBytes];
		BinaryPrimitives.WriteUInt32LittleEndian( head, BakeMagic );
		BinaryPrimitives.WriteUInt32LittleEndian( head[4..], BakeVersion );
		BinaryPrimitives.WriteSingleLittleEndian( head[8..], settings.CellSize );
		BinaryPrimitives.WriteSingleLittleEndian( head[12..], settings.CellHeight );
		BinaryPrimitives.WriteInt32LittleEndian( head[16..], settings.TileSize );
		BinaryPrimitives.WriteSingleLittleEndian( head[20..], settings.AgentHeight );
		BinaryPrimitives.WriteSingleLittleEndian( head[24..], settings.AgentRadius );
		BinaryPrimitives.WriteSingleLittleEndian( head[28..], settings.AgentClimb );
		BinaryPrimitives.WriteSingleLittleEndian( head[32..], settings.AgentMaxSlope );
		BinaryPrimitives.WriteInt32LittleEndian( head[36..], settings.MaxEdgeCells );
		BinaryPrimitives.WriteInt32LittleEndian( head[40..], settings.MinRegionCells );
		BinaryPrimitives.WriteSingleLittleEndian( head[44..], settings.MaxEdgeError );
		BinaryPrimitives.WriteInt32LittleEndian( head[48..], count );
		output.Advance( HeaderBytes );

		double cellSize = settings.CellSize, cellHeight = settings.CellHeight;
		long lastX = 0, lastY = 0;
		foreach ( var tile in tiles )
		{
			var to = output.GetSpan( (int)MostBytes( tile ) );
			int at = 0;
			PutSigned( to, ref at, tile.X - lastX );
			PutSigned( to, ref at, tile.Y - lastY );
			lastX = tile.X;
			lastY = tile.Y;

			ulong hash = VolumeHashOf( tile.X, tile.Y );
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
				long cell = Math.Clamp( CellOf( v.X, cellSize ) - cellX, 0, settings.TileSize );
				PutSigned( to, ref at, cell - before );
				before = cell;
			}
			before = 0;
			foreach ( var v in verts )
			{
				long cell = Math.Clamp( CellOf( v.Y, cellSize ) - cellY, 0, settings.TileSize );
				PutSigned( to, ref at, cell - before );
				before = cell;
			}
			before = 0;
			foreach ( var v in verts )
			{
				long cell = Math.Clamp( CellOf( v.Z, cellHeight ), int.MinValue, int.MaxValue );
				PutSigned( to, ref at, cell - before );
				before = cell;
			}
			output.Advance( at );
		}
		return true;
	}

	static long CellOf( float value, double cell ) => (long)Math.Clamp( Math.Round( value / cell ), -1e15, 1e15 );

	static void Put( Span<byte> to, ref int at, ulong value )
	{
		for ( ; value >= 0x80; value >>= 7 ) to[at++] = (byte)(value | 0x80);
		to[at++] = (byte)value;
	}

	static void PutSigned( Span<byte> to, ref int at, long value ) => Put( to, ref at, (ulong)((value << 1) ^ (value >> 63)) );

	// Reads the numbers of a bake. Past the end it hands out zeros and remembers that it did, so the
	// loops over it need no checks of their own.
	ref struct BakeReader( ReadOnlySpan<byte> data, int at )
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

	/// <summary>
	/// False when the data is not a bake of this format version or its settings are not valid.
	/// </summary>
	public static bool TryReadSettings( ReadOnlySpan<byte> data, out NavSettings settings )
	{
		settings = default;
		if ( data.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian( data ) != BakeMagic || BinaryPrimitives.ReadUInt32LittleEndian( data[4..] ) != BakeVersion ) return false;
		settings = new NavSettings
		{
			CellSize = BinaryPrimitives.ReadSingleLittleEndian( data[8..] ),
			CellHeight = BinaryPrimitives.ReadSingleLittleEndian( data[12..] ),
			TileSize = BinaryPrimitives.ReadInt32LittleEndian( data[16..] ),
			AgentHeight = BinaryPrimitives.ReadSingleLittleEndian( data[20..] ),
			AgentRadius = BinaryPrimitives.ReadSingleLittleEndian( data[24..] ),
			AgentClimb = BinaryPrimitives.ReadSingleLittleEndian( data[28..] ),
			AgentMaxSlope = BinaryPrimitives.ReadSingleLittleEndian( data[32..] ),
			MaxEdgeCells = BinaryPrimitives.ReadInt32LittleEndian( data[36..] ),
			MinRegionCells = BinaryPrimitives.ReadInt32LittleEndian( data[40..] ),
			MaxEdgeError = BinaryPrimitives.ReadSingleLittleEndian( data[44..] )
		};
		return settings.IsValid( out _ );
	}

	/// <summary>
	/// Makes a mesh from a bake. Null when anything in the data is wrong: every count, index and
	/// coordinate is checked before it is used, and what is not in the data is built here, so it
	/// cannot be wrong. Keeps nothing of data.
	/// A bake holds neither volumes nor links: add those of the scene to the new mesh before its first
	/// TakeJobs. That call compares each tile's volumes with the ones it was baked with and issues a job,
	/// which asks for geometry, for every tile where they differ.
	/// </summary>
	public static NavMesh? Load( ReadOnlySpan<byte> data, NavCodec codec = default, bool keepFields = true )
	{
		if ( !TryReadSettings( data, out var settings ) ) return null;
		int count = BinaryPrimitives.ReadInt32LittleEndian( data[48..] );
		if ( count < 0 || count > PolyRef.MaxTiles || HeaderBytes + (long)count * MinTileBytes > data.Length ) return null;

		var reader = new BakeReader( data, HeaderBytes );
		var loader = new TileLoader( settings );
		var tiles = new Tile[count];
		var hashes = new ulong[count];
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
		if ( reader.Left != 0 ) return null;

		var mesh = new NavMesh( settings, codec, keepFields );
		mesh.CommitTiles( tiles );
		mesh.SetVolumeHashes( tiles, hashes );
		return mesh;
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
			if ( head < 2 || head >> 1 > Poly.MaxPerTile || vertNumber < 3 || vertNumber > Poly.MaxVertsPerTile ) return null;
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
