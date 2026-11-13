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
	TileIndex workIndex = null!;
	bool pagesOwned, indexOwned;
	uint[] pageStamps = [];
	readonly List<(int X, int Y)> touched = new();
	readonly HashSet<(int X, int Y)> touchedSet = new();
	readonly Dictionary<int, byte> slotFlags = new();
	readonly List<ExtLink> extScratch = new();
	readonly List<ulong> attachKeys = new();
	readonly List<int> attachSlots = new();
	NavQuery? snapQuery;

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
		if ( changes.Count == 0 && !linksDirty ) return 0;

		var old = state;
		if ( ++commits == 0 )
		{
			// Pages and index blocks are stamped with the serial of the commit that copied them. None of
			// the last round may pass for a copy of this commit.
			commits = 1;
			Array.Clear( pageStamps );
			old.Index.ForgetStamps();
		}
		uint serial = commits;
		workPages = old.Pages;
		workIndex = old.Index;
		pagesOwned = indexOwned = false;
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
					else if ( slotCount < PolyRef.MaxTiles ) slot = slotCount++;
					else
					{
						failed++;
						change.Job?.Complete( TileJobStatus.Failed, null, $"the mesh already holds {PolyRef.MaxTiles} tiles" );
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
		var links = old.Links;
		bool linksChanged = linkHigh > 0 && (linksDirty || touched.Count > 0) && ResolveLinks( new MeshState( workPages, workIndex, links, 0, tileCount ), serial, ref links );
		linksDirty = false;

		if ( touched.Count > 0 || linksChanged ) Volatile.Write( ref state, new MeshState( workPages, workIndex, links, old.Version + 1, tileCount ) );

		touched.Clear();
		touchedSet.Clear();
		slotFlags.Clear();
		workPages = [];
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

	void SetTile( int slot, Tile? tile, uint serial )
	{
		int page = slot >> MeshState.PageBits;
		if ( !pagesOwned || page >= workPages.Length )
		{
			var root = new Tile?[]?[Math.Max( workPages.Length, page + 1 )];
			workPages.CopyTo( root, 0 );
			workPages = root;
			pagesOwned = true;
			if ( pageStamps.Length < root.Length ) Array.Resize( ref pageStamps, Math.Max( root.Length, pageStamps.Length * 2 ) );
		}
		if ( pageStamps[page] != serial )
		{
			workPages[page] = workPages[page] is { } shared ? (Tile?[])shared.Clone() : new Tile?[MeshState.PageSize];
			pageStamps[page] = serial;
		}
		workPages[page]![slot & (MeshState.PageSize - 1)] = tile;
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

		foreach ( ref readonly var edge in tile.Portals.AsSpan() )
		{
			var other = edge.Side == 0 ? east : edge.Side == 1 ? north : edge.Side == 2 ? west : south;
			if ( other == null ) continue;
			bool ascending = EdgeHeights( tile, edge, out float zLo, out float zHi );

			int facing = edge.Side ^ 2, last = -1;
			foreach ( ref readonly var candidate in other.Portals.AsSpan() )
			{
				// One connection per neighbour polygon and edge.
				if ( candidate.Side != facing || candidate.Poly == last ) continue;
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
					Target = (uint)other.Slot << PolyRef.PolyBits | candidate.Poly,
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
	// removed tile, or when one of its polygons lives in one. Returns true when any link entry changed.
	bool ResolveLinks( MeshState next, uint serial, ref LinkEntry[] links )
	{
		bool owned = false;
		attachSlots.Clear();

		for ( int id = 0; id < linkHigh; id++ )
		{
			byte status = linkStatus[id];
			if ( status == LinkFree ) continue;
			var entry = id < links.Length ? links[id] : default;
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
			if ( !owned )
			{
				var copy = new LinkEntry[Math.Max( links.Length, linkHigh )];
				links.CopyTo( copy, 0 );
				links = copy;
				owned = true;
			}
			links[id] = updated;
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

		if ( !owned ) return false;
		if ( attachSlots.Count == 0 ) return true;

		// Polygon id above, link id in the low 16 bits: sorting groups by tile, then polygon.
		attachKeys.Clear();
		for ( int id = 0; id < links.Length; id++ )
		{
			ref readonly var entry = ref links[id];
			if ( entry.Serial == 0 || !entry.Connected || entry.StartPoly == entry.EndPoly ) continue;
			if ( NeedsReattach( entry.StartPoly ) ) attachKeys.Add( (ulong)entry.StartPoly << 16 | (uint)id );
			if ( entry.Def.Bidirectional && NeedsReattach( entry.EndPoly ) ) attachKeys.Add( (ulong)entry.EndPoly << 16 | (uint)id );
		}
		attachKeys.Sort();

		var keys = CollectionsMarshal.AsSpan( attachKeys );
		foreach ( int slot in attachSlots )
		{
			if ( workPages[slot >> MeshState.PageBits]?[slot & (MeshState.PageSize - 1)] == null ) continue;
			int first = 0, end = keys.Length;
			ulong low = (ulong)slot << (PolyRef.PolyBits + 16);
			while ( first < end )
			{
				int middle = (first + end) >> 1;
				if ( keys[middle] < low ) first = middle + 1;
				else end = middle;
			}
			int count = 0;
			while ( first + count < keys.Length && (int)(keys[first + count] >> (PolyRef.PolyBits + 16)) == slot ) count++;

			ref byte flags = ref CollectionsMarshal.GetValueRefOrNullRef( slotFlags, slot );
			var tile = FreshTile( slot, ref flags, serial );
			tile.Attach = count == 0 ? [] : new LinkAttach[count];
			for ( int i = 0; i < count; i++ )
				tile.Attach[i] = new LinkAttach { Poly = (ushort)(keys[first + i] >> 16 & PolyRef.PolyMask), Link = (ushort)keys[first + i] };
		}
		return true;
	}

	void MarkReattach( uint poly )
	{
		ref byte flags = ref CollectionsMarshal.GetValueRefOrAddDefault( slotFlags, (int)(poly >> PolyRef.PolyBits), out _ );
		if ( (flags & Reattach) != 0 ) return;
		flags |= Reattach;
		attachSlots.Add( (int)(poly >> PolyRef.PolyBits) );
	}

	bool NeedsReattach( uint poly ) => slotFlags.TryGetValue( (int)(poly >> PolyRef.PolyBits), out byte flags ) && (flags & Reattach) != 0;

	bool SlotTouched( uint poly ) => poly != PolyRef.NoId && slotFlags.TryGetValue( (int)(poly >> PolyRef.PolyBits), out byte flags ) && (flags & Touched) != 0;

	bool LinkTouched( in LinkEntry entry )
	{
		return SlotTouched( entry.StartPoly ) || SlotTouched( entry.EndPoly ) || BoxTouched( entry.Def.Start, entry.Def.Radius ) || BoxTouched( entry.Def.End, entry.Def.Radius );
	}

	bool BoxTouched( Vector3 point, float radius )
	{
		int minX = TileOf( point.X - radius ), maxX = TileOf( point.X + radius );
		int minY = TileOf( point.Y - radius ), maxY = TileOf( point.Y + radius );
		for ( int y = minY; y <= maxY; y++ )
			for ( int x = minX; x <= maxX; x++ )
				if ( touchedSet.Contains( (x, y) ) ) return true;
		return false;
	}

	LinkEntry Resolve( MeshState next, in NavLink link )
	{
		var entry = new LinkEntry { Def = link, StartPoint = link.Start, EndPoint = link.End, StartPoly = PolyRef.NoId, EndPoly = PolyRef.NoId };
		var half = new Vector3( link.Radius, link.Radius, MathF.Max( link.Radius, settings.AgentClimb ) );
		snapQuery ??= new NavQuery( this );
		if ( snapQuery.Nearest( next, link.Start, half, default, null, 0, out uint start, out var startPoint ) && WithinRadius( startPoint, link.Start, link.Radius ) )
		{
			entry.StartPoly = start;
			entry.StartPoint = startPoint;
		}
		if ( snapQuery.Nearest( next, link.End, half, default, null, 0, out uint end, out var endPoint ) && WithinRadius( endPoint, link.End, link.Radius ) )
		{
			entry.EndPoly = end;
			entry.EndPoint = endPoint;
		}
		return entry;
	}

	static bool WithinRadius( Vector3 a, Vector3 b, float radius ) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) <= radius * radius;
}
