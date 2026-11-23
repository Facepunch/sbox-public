using System.Numerics;
using System.Runtime.InteropServices;

namespace Nav;

public sealed partial class NavMesh
{
	readonly record struct Change( int X, int Y, Tile? Tile, TileJob? Job );

	const byte Touched = 1, Fresh = 2, Relinked = 4, Reattach = 8;

	// Tile removals and builds waiting for the next state swap, in order.
	readonly List<Change> changes = new();

	// Stamps every published build. Mesh-wide, so a reused slot never validates an old PolyRef. These
	// are the 32 serial bits of a PolyRef. After four billion commits, more than two years at sixty a
	// second, the count starts again at 1, 0 being no serial. A PolyRef kept through all of them could
	// then name a later build of its slot, which is accepted: a wider serial would cost every PolyRef.
	uint commits;
	int slotCount;
	readonly PriorityQueue<int, int> freeSlots = new();

	// Scratch of one commit.
	Tile?[]?[] workPages = [];
	LinkEntry[]?[] workLinks = [];
	TileIndex workIndex = null!;
	bool pagesOwned, linksOwned, indexOwned;
	uint[] pageStamps = [], linkPageStamps = [];
	readonly List<(int X, int Y)> touched = new();
	readonly HashSet<(int X, int Y)> touchedSet = new();
	readonly Dictionary<int, byte> slotFlags = new();
	readonly List<ExtLink> extScratch = new();
	int[] facingScratch = [];
	readonly List<ulong> attachKeys = new();
	readonly List<int> attachSlots = new();
	readonly List<int> linkCandidates = new();

	// Owner side, per tile: the published links whose snap boxes overlap it. An end snaps only to a tile
	// of its box, so these are all the links the link rule can resolve again when the tile changes, and
	// all that attach to it. A link whose two boxes cover more than WideTiles tiles is in wideLinks
	// instead, which every commit that changes tiles goes through.
	readonly Dictionary<(int X, int Y), List<int>> linksNear = new();
	readonly List<int> wideLinks = new();
	const int WideTiles = 64;
	NavQuery? snapQuery;

	// A link end snaps to any polygon.
	static readonly NavFilter anyPoly = NavFilter.Create();

	/// <summary>
	/// Publishes finished tiles in one state swap. Returns how many did not fit the tile table.
	/// A tile object that was committed before is published as a new build.
	/// </summary>
	internal int CommitTiles( ReadOnlySpan<Tile> tiles )
	{
		foreach ( var tile in tiles ) changes.Add( new( tile.X, tile.Y, tile, null ) );
		return Apply();
	}

	int Apply()
	{
		if ( changes.Count == 0 && linkQueue.Count == 0 && relink.Count == 0 && held.Count == 0 ) return 0;

		var old = state;
		if ( ++commits == 0 )
		{
			// Pages and index blocks are stamped with the serial of the commit that copied them. None of
			// the last round may pass for a copy of this commit.
			commits = 1;
			Array.Clear( pageStamps );
			Array.Clear( linkPageStamps );
			old.Index.ForgetStamps();
		}
		uint serial = commits;
		workPages = old.Pages;
		workLinks = old.LinkPages;
		workIndex = old.Index;
		pagesOwned = linksOwned = indexOwned = false;
		int tileCount = old.TileCount, failed = 0;

		foreach ( var change in changes )
		{
			int slot = workIndex.Find( change.X, change.Y );
			var tile = change.Tile;
			if ( tile == null )
			{
				if ( slot < 0 ) continue;
				SetTile( slot, null, serial );
				OwnIndex().Set( change.X, change.Y, -1, serial );
				freeSlots.Enqueue( slot, slot );
				tileCount--;
				slotFlags[slot] = Touched;
			}
			else
			{
				if ( slot < 0 )
				{
					if ( freeSlots.Count > 0 ) slot = freeSlots.Dequeue();
					else if ( slotCount < MaxTiles ) slot = slotCount++;
					else
					{
						failed++;
						change.Job?.Complete( TileJobStatus.Failed, null, $"the mesh already holds {MaxTiles} tiles" );
						continue;
					}
					OwnIndex().Set( change.X, change.Y, slot, serial );
					tileCount++;
				}

				if ( tile.Serial != 0 ) tile = new Tile( tile );
				tile.Slot = slot;
				tile.Serial = serial;
				tile.Ext = [];
				tile.Attach = [];
				SetTile( slot, tile, serial );
				slotFlags[slot] = Touched | Fresh;
			}
			if ( touchedSet.Add( (change.X, change.Y) ) ) touched.Add( (change.X, change.Y) );
		}
		changes.Clear();

		// A changed tile and its four edge neighbours get new connections.
		foreach ( var (x, y) in touched )
		{
			for ( int k = 0; k < 5; k++ )
			{
				int slot = workIndex.Find( x + (k == 1 ? 1 : k == 3 ? -1 : 0), y + (k == 2 ? 1 : k == 4 ? -1 : 0) );
				if ( slot < 0 ) continue;
				ref byte flags = ref CollectionsMarshal.GetValueRefOrAddDefault( slotFlags, slot, out _ );
				if ( (flags & Relinked) != 0 ) continue;
				flags |= Relinked;
				var tile = FreshTile( slot, ref flags, serial );
				tile.Ext = BuildExt( tile );
			}
		}

		if ( indexOwned ) workIndex.Seal();
		// Link ends snap against the new tiles. Attaching them may still replace tile objects.
		var next = new MeshState( workPages, workIndex, old.LinkPages, 0, tileCount, polyBits, old.LatestIslands );
		if ( relink.Count > 0 || held.Count > 0 ) SettleLinks( next );
		bool linksChanged = (linkQueue.Count > 0 || touched.Count > 0) && ResolveLinks( old, next, serial );

		if ( touched.Count > 0 || linksChanged ) Volatile.Write( ref state, new MeshState( workPages, workIndex, workLinks, old.Version + 1, tileCount, polyBits, old.LatestIslands ) );

		touched.Clear();
		touchedSet.Clear();
		slotFlags.Clear();
		workPages = [];
		workLinks = [];
		workIndex = null!;
		return failed;
	}

	TileIndex OwnIndex()
	{
		if ( !indexOwned )
		{
			workIndex = workIndex.Clone();
			indexOwned = true;
		}
		return workIndex;
	}

	void SetTile( int slot, Tile? tile, uint serial ) => Owned( ref workPages, ref pagesOwned, ref pageStamps, slot, MeshState.PageBits, serial ) = tile;

	// An element of a paged table that this commit may write. The root is copied once per commit, a
	// page once per commit that writes to it: pages carry the serial of the commit that copied them.
	static ref T Owned<T>( ref T[]?[] pages, ref bool owned, ref uint[] stamps, int index, int bits, uint serial )
	{
		int page = index >> bits;
		if ( !owned || page >= pages.Length )
		{
			var root = new T[]?[Math.Max( pages.Length, page + 1 )];
			pages.CopyTo( root, 0 );
			pages = root;
			owned = true;
			if ( stamps.Length < root.Length ) Array.Resize( ref stamps, Math.Max( root.Length, stamps.Length * 2 ) );
		}
		if ( stamps[page] != serial )
		{
			pages[page] = pages[page] is { } shared ? (T[])shared.Clone() : new T[1 << bits];
			stamps[page] = serial;
		}
		return ref pages[page]![index & ((1 << bits) - 1)];
	}

	Tile? WorkTile( int x, int y )
	{
		int slot = workIndex.Find( x, y );
		return slot < 0 ? null : workPages[slot >> MeshState.PageBits]![slot & (MeshState.PageSize - 1)];
	}

	// The tile object of this commit: a published one is replaced by a copy that shares its arrays and serial.
	Tile FreshTile( int slot, ref byte flags, uint serial )
	{
		var tile = workPages[slot >> MeshState.PageBits]![slot & (MeshState.PageSize - 1)]!;
		if ( (flags & Fresh) == 0 )
		{
			tile = new Tile( tile );
			SetTile( slot, tile, serial );
			flags |= Fresh;
		}
		return tile;
	}

	// Two border edges connect when they overlap along the border and, at the two ends of the shared
	// stretch, their heights cross or come within the climb threshold.
	ExtLink[] BuildExt( Tile tile )
	{
		extScratch.Clear();
		float threshold = PortalClimbFactor * settings.AgentClimb;
		threshold *= threshold;
		Tile? east = WorkTile( tile.X + 1, tile.Y ), north = WorkTile( tile.X, tile.Y + 1 ), west = WorkTile( tile.X - 1, tile.Y ), south = WorkTile( tile.X, tile.Y - 1 );

		// Per side, the portals of the tile beyond that face this one, in their order.
		int total = (east?.Portals.Length ?? 0) + (north?.Portals.Length ?? 0) + (west?.Portals.Length ?? 0) + (south?.Portals.Length ?? 0);
		if ( facingScratch.Length < total ) facingScratch = new int[Math.Max( total, facingScratch.Length * 2 )];
		Span<int> first = stackalloc int[5];
		int gathered = 0;
		for ( int side = 0; side < 4; side++ )
		{
			first[side] = gathered;
			var beyond = side == 0 ? east : side == 1 ? north : side == 2 ? west : south;
			if ( beyond == null ) continue;
			for ( int c = 0; c < beyond.Portals.Length; c++ )
			{
				if ( beyond.Portals[c].Side == (side ^ 2) ) facingScratch[gathered++] = c;
			}
		}
		first[4] = gathered;

		foreach ( ref readonly var edge in tile.Portals.AsSpan() )
		{
			var other = edge.Side == 0 ? east : edge.Side == 1 ? north : edge.Side == 2 ? west : south;
			if ( other == null ) continue;
			bool ascending = EdgeHeights( tile, edge, out float zLo, out float zHi );

			int last = -1;
			for ( int f = first[edge.Side]; f < first[edge.Side + 1]; f++ )
			{
				ref readonly var candidate = ref other.Portals[facingScratch[f]];
				// One connection per neighbour polygon and edge.
				if ( candidate.Poly == last ) continue;
				int lo = Math.Max( edge.Lo, candidate.Lo ), hi = Math.Min( edge.Hi, candidate.Hi );
				if ( lo >= hi ) continue;

				EdgeHeights( other, candidate, out float otherLo, out float otherHi );
				float low = HeightAt( candidate, otherLo, otherHi, lo ) - HeightAt( edge, zLo, zHi, lo );
				float high = HeightAt( candidate, otherLo, otherHi, hi ) - HeightAt( edge, zLo, zHi, hi );
				if ( low * high >= 0 && low * low > threshold && high * high > threshold ) continue;
				last = candidate.Poly;

				float length = edge.Hi - edge.Lo;
				float tmin = (lo - edge.Lo) / length, tmax = (hi - edge.Lo) / length;
				if ( !ascending ) (tmin, tmax) = (1 - tmax, 1 - tmin);

				extScratch.Add( new ExtLink
				{
					Target = (uint)other.Slot << polyBits | candidate.Poly,
					Poly = edge.Poly,
					Edge = edge.Edge,
					Side = edge.Side,
					TMin = (byte)MathF.Round( tmin * 255 ),
					TMax = (byte)MathF.Round( tmax * 255 ),
					Area = other.Polys[candidate.Poly].Area
				} );
			}
		}
		return extScratch.Count == 0 ? [] : extScratch.ToArray();
	}

	// The heights of a border edge at its Lo and Hi ends. True when the edge runs from Lo to Hi.
	static bool EdgeHeights( Tile tile, in PortalEdge edge, out float zLo, out float zHi )
	{
		ref readonly var poly = ref tile.Polys[edge.Poly];
		var a = tile.Verts[poly.Verts[edge.Edge]];
		var b = tile.Verts[poly.Verts[edge.Edge + 1 < poly.VertCount ? edge.Edge + 1 : 0]];
		bool ascending = (edge.Side & 1) == 0 ? a.Y < b.Y : a.X < b.X;
		zLo = ascending ? a.Z : b.Z;
		zHi = ascending ? b.Z : a.Z;
		return ascending;
	}

	static float HeightAt( in PortalEdge edge, float zLo, float zHi, int cell ) => zLo + (zHi - zLo) * (cell - edge.Lo) / (edge.Hi - edge.Lo);

	// The link rule: a link is resolved again when it changed, when its snap boxes overlap a committed or
	// removed tile, or when one of its polygons lives in one. Only links added, set or removed since the
	// last commit and those listed at a changed tile can be such. Returns true when any entry changed.
	bool ResolveLinks( MeshState old, MeshState next, uint serial )
	{
		attachSlots.Clear();
		var candidates = linkCandidates;
		candidates.Clear();
		candidates.AddRange( linkQueue );
		linkQueue.Clear();
		if ( touched.Count > 0 )
		{
			foreach ( var at in touched )
			{
				if ( linksNear.TryGetValue( at, out var near ) ) candidates.AddRange( near );
			}
			candidates.AddRange( wideLinks );
		}
		candidates.Sort();

		int previous = -1;
		foreach ( int id in candidates )
		{
			if ( id == previous ) continue;
			previous = id;
			byte status = linkStatus[id];
			if ( status == LinkFree ) continue;
			var entry = old.LinkAt( id );
			LinkEntry updated = default;

			if ( status == LinkRemoved ) linkStatus[id] = LinkFree;
			else
			{
				bool dirty = status == LinkDirty;
				if ( !dirty && !LinkTouched( entry ) ) continue;
				linkStatus[id] = LinkUsed;
				updated = Resolve( next, linkDefs[id] );
				bool same = !dirty && entry.Serial != 0 && updated.StartPoly == entry.StartPoly && updated.EndPoly == entry.EndPoly
					&& updated.StartPoint == entry.StartPoint && updated.EndPoint == entry.EndPoint
					&& !SlotTouched( updated.StartPoly ) && !SlotTouched( updated.EndPoly );
				if ( same ) continue;
				updated.Serial = serial;
			}

			if ( entry.Serial == 0 && updated.Serial == 0 ) continue;
			Owned( ref workLinks, ref linksOwned, ref linkPageStamps, id, MeshState.LinkPageBits, serial ) = updated;

			if ( entry.Serial == 0 || updated.Serial == 0 || SnapBox( entry.Def.Start, entry.Def.Radius ) != SnapBox( updated.Def.Start, updated.Def.Radius )
				|| SnapBox( entry.Def.End, entry.Def.Radius ) != SnapBox( updated.Def.End, updated.Def.Radius ) )
			{
				if ( entry.Serial != 0 ) ListLink( id, entry.Def, false );
				if ( updated.Serial != 0 ) ListLink( id, updated.Def, true );
			}

			if ( entry.Serial != 0 && entry.Connected )
			{
				MarkReattach( entry.StartPoly );
				MarkReattach( entry.EndPoly );
			}
			if ( updated.Serial != 0 && updated.Connected )
			{
				MarkReattach( updated.StartPoly );
				MarkReattach( updated.EndPoly );
			}
		}

		if ( !linksOwned ) return false;

		// Polygon id above, link id in the low 16 bits: sorting groups by polygon.
		foreach ( int slot in attachSlots )
		{
			var target = workPages[slot >> MeshState.PageBits]?[slot & (MeshState.PageSize - 1)];
			if ( target == null ) continue;
			attachKeys.Clear();
			if ( linksNear.TryGetValue( (target.X, target.Y), out var near ) ) AttachKeys( near, slot );
			AttachKeys( wideLinks, slot );
			attachKeys.Sort();

			ref byte flags = ref CollectionsMarshal.GetValueRefOrNullRef( slotFlags, slot );
			var tile = FreshTile( slot, ref flags, serial );
			int count = attachKeys.Count;
			tile.Attach = count == 0 ? [] : new LinkAttach[count];
			for ( int i = 0; i < count; i++ )
				tile.Attach[i] = new LinkAttach { Poly = (ushort)(attachKeys[i] >> 16 & polyMask), Link = (ushort)attachKeys[i] };
		}
		return true;
	}

	void AttachKeys( List<int> ids, int slot )
	{
		foreach ( int id in ids )
		{
			ref readonly var entry = ref workLinks[id >> MeshState.LinkPageBits]![id & (MeshState.LinkPageSize - 1)];
			if ( !entry.Connected || entry.StartPoly == entry.EndPoly ) continue;
			if ( (int)(entry.StartPoly >> polyBits) == slot ) attachKeys.Add( (ulong)entry.StartPoly << 16 | (uint)id );
			if ( entry.Def.Bidirectional && (int)(entry.EndPoly >> polyBits) == slot ) attachKeys.Add( (ulong)entry.EndPoly << 16 | (uint)id );
		}
	}

	readonly record struct TileBox( int MinX, int MinY, int MaxX, int MaxY )
	{
		public static readonly TileBox Empty = new( 0, 0, -1, -1 );
		public long Area => MaxX < MinX || MaxY < MinY ? 0 : ((long)MaxX - MinX + 1) * ((long)MaxY - MinY + 1);
		public bool Contains( int x, int y ) => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
	}

	TileBox SnapBox( Vector3 point, float radius ) => new( TileOf( point.X - radius ), TileOf( point.Y - radius ), TileOf( point.X + radius ), TileOf( point.Y + radius ) );

	// Lists or unlists a link at every tile of its snap boxes, once per tile.
	void ListLink( int id, in NavLink link, bool add )
	{
		TileBox start = SnapBox( link.Start, link.Radius ), end = SnapBox( link.End, link.Radius );
		if ( start.Area + end.Area > WideTiles )
		{
			if ( add ) wideLinks.Add( id );
			else wideLinks.Remove( id );
			return;
		}
		ListLink( id, start, TileBox.Empty, add );
		ListLink( id, end, start, add );
	}

	void ListLink( int id, TileBox box, TileBox skip, bool add )
	{
		for ( int y = box.MinY; y <= box.MaxY; y++ )
		{
			for ( int x = box.MinX; x <= box.MaxX; x++ )
			{
				if ( skip.Contains( x, y ) ) continue;
				if ( add )
				{
					ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault( linksNear, (x, y), out _ );
					(list ??= new List<int>()).Add( id );
					continue;
				}
				var near = linksNear[(x, y)];
				int at = near.IndexOf( id );
				near[at] = near[^1];
				near.RemoveAt( near.Count - 1 );
				if ( near.Count == 0 ) linksNear.Remove( (x, y) );
			}
		}
	}

	void MarkReattach( uint poly )
	{
		ref byte flags = ref CollectionsMarshal.GetValueRefOrAddDefault( slotFlags, (int)(poly >> polyBits), out _ );
		if ( (flags & Reattach) != 0 ) return;
		flags |= Reattach;
		attachSlots.Add( (int)(poly >> polyBits) );
	}

	bool SlotTouched( uint poly ) => poly != PolyRef.NoId && slotFlags.TryGetValue( (int)(poly >> polyBits), out byte flags ) && (flags & Touched) != 0;

	bool LinkTouched( in LinkEntry entry )
	{
		return SlotTouched( entry.StartPoly ) || SlotTouched( entry.EndPoly ) || BoxTouched( entry.Def.Start, entry.Def.Radius ) || BoxTouched( entry.Def.End, entry.Def.Radius );
	}

	bool BoxTouched( Vector3 point, float radius )
	{
		var box = SnapBox( point, radius );
		if ( box.Area > touched.Count )
		{
			foreach ( var (x, y) in touched )
			{
				if ( box.Contains( x, y ) ) return true;
			}
			return false;
		}
		for ( int y = box.MinY; y <= box.MaxY; y++ )
			for ( int x = box.MinX; x <= box.MaxX; x++ )
				if ( touchedSet.Contains( (x, y) ) ) return true;
		return false;
	}

	LinkEntry Resolve( MeshState next, in NavLink link )
	{
		var entry = new LinkEntry { Def = link, StartPoint = link.Start, EndPoint = link.End, StartPoly = PolyRef.NoId, EndPoly = PolyRef.NoId };
		var half = new Vector3( link.Radius, link.Radius, MathF.Max( link.Radius, settings.AgentClimb ) );
		snapQuery ??= new NavQuery( this );
		if ( link.Generated )
		{
			// Its ends are over the polygons its tile build found them on, at the height of the ground
			// there, which is within two climbs of their planes.
			float reach = 2 * settings.AgentClimb;
			if ( snapQuery.Under( next, link.Start, link.Start.Z - reach, link.Start.Z + reach, out entry.StartPoly, out float startZ ) ) entry.StartPoint.Z = startZ;
			if ( snapQuery.Under( next, link.End, link.End.Z - reach, link.End.Z + reach, out entry.EndPoly, out float endZ ) ) entry.EndPoint.Z = endZ;
			return entry;
		}
		if ( snapQuery.Nearest( next, link.Start, half, anyPoly, null, 0, out uint start, out var startPoint ) && WithinRadius( startPoint, link.Start, link.Radius ) )
		{
			entry.StartPoly = start;
			entry.StartPoint = startPoint;
		}
		if ( snapQuery.Nearest( next, link.End, half, anyPoly, null, 0, out uint end, out var endPoint ) && WithinRadius( endPoint, link.End, link.Radius ) )
		{
			entry.EndPoly = end;
			entry.EndPoint = endPoint;
		}
		return entry;
	}

	static bool WithinRadius( Vector3 a, Vector3 b, float radius ) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) <= radius * radius;
}
