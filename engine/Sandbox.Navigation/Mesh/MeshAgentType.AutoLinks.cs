using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using Nav.Build;

namespace Nav;

internal sealed partial class MeshAgentType
{
	// What the owner keeps about the links of a tile built from triangles. Own: found on the tile's own
	// field. Seam: into other tiles, found by this tile's build over its apron, by a build of the tile
	// they land in, or by a job from the held spans of both. Open: the edge points those start from.
	sealed class TileLinks
	{
		public FoundLink[] Own = [], Seam = [];
		public EdgePoint[] Open = [];
		public float MinZ;

		// Those of them that are good now, in a fixed order. Where the mesh inserts them: their ids, and
		// how many found no free id.
		public FoundLink[] Made = [];
		public int[] Ids = [];
		public int Refused;

		// The solid spans of the build, held while a tile around is still to be built: a bit per such
		// tile, which edge points have been judged or handed to a job, whether any may be judged now,
		// whether the job in flight judges some, and whether a tile around changed under that job.
		// Round: the first round of judging taken after the last tile around came back. The tiles around
		// read these spans in it.
		public HeldField? Held;
		public int Waits, Round;
		public bool[] Tried = [];
		public bool Judge, Judging, Stale;
	}

	// A tile lets its spans go early when more than this many are held: whichever tile around it is
	// built later then looks over an apron instead.
	internal int MaxHeldFields = 1024;

	readonly NavLinkSettings links;
	readonly Dictionary<(int X, int Y), TileLinks> tileLinks = new();
	readonly List<(int X, int Y)> held = new();
	readonly HashSet<(int X, int Y)> relink = new(), linkChanges = new();
	readonly List<(int X, int Y)> relinkOrder = new();
	readonly ConcurrentBag<HeldField> spares = new();
	readonly List<FoundLink> linkScratch = new();
	readonly List<(Vector3 Start, Vector3 End, FoundLink Up)> upScratch = new();
	readonly List<int> pointScratch = new();
	LinkBuilder? linkJudge;
	NavQuery? linkQuery;
	int linksRefused;

	// The job in flight that judges links across tile borders, and the record of each tile in it. Every
	// TakeJobs with no such job in flight is a round: it hands out all there is to judge. seamsDone is
	// the last round that is back.
	TileJob? seamJob;
	readonly List<TileLinks> seamRecords = new();
	int seamRound, seamsDone;

	internal long HeldBytes, HeldPeak;
	internal int HeldFields => held.Count;

	public int LinksRefused => linksRefused;

	public bool GetLink( int id, out NavLink link, out bool generated )
	{
		ref readonly var entry = ref State.LinkAt( id );
		bool found = entry.Serial != 0;
		link = found ? entry.Def : default;
		generated = link.Generated;
		return found;
	}

	public int GetGeneratedLinks( List<int> ids )
	{
		var pages = State.LinkPages;
		int first = ids.Count;
		for ( int page = 0; page < pages.Length; page++ )
		{
			var entries = pages[page];
			if ( entries == null ) continue;
			for ( int i = 0; i < entries.Length; i++ )
			{
				if ( entries[i].Serial != 0 && entries[i].Def.Generated ) ids.Add( page << MeshState.LinkPageBits | i );
			}
		}
		return ids.Count - first;
	}

	public int GetGeneratedLinks( int tileX, int tileY, List<NavLink> result )
	{
		if ( !tileLinks.TryGetValue( (tileX, tileY), out var t ) ) return 0;
		var s = State;
		linkQuery ??= new NavQuery( this );
		float reach = 2 * settings.AgentClimb;
		foreach ( ref readonly var made in t.Made.AsSpan() )
		{
			var link = ToLink( made );
			if ( linkQuery.Under( s, link.Start, link.Start.Z - reach, link.Start.Z + reach, out _, out float startZ ) ) link.Start.Z = startZ;
			if ( linkQuery.Under( s, link.End, link.End.Z - reach, link.End.Z + reach, out _, out float endZ ) ) link.End.Z = endZ;
			result.Add( link );
		}
		return t.Made.Length;
	}

	public int TakeLinkChanges( List<(int X, int Y)> tiles )
	{
		int count = linkChanges.Count;
		tiles.AddRange( linkChanges );
		linkChanges.Clear();
		return count;
	}

	void LinksChanged( (int X, int Y) at )
	{
		if ( !links.Insert ) linkChanges.Add( at );
	}

	NavLink ToLink( in FoundLink link ) => new() { Start = link.Start, End = link.End, Radius = settings.CellSize, Bidirectional = link.Bidirectional, Area = NavFilter.DefaultArea, Type = link.Kind == LinkKind.Drop ? links.DropType : links.JumpType };

	bool SeamsPending( (int X, int Y) at ) => tileLinks.TryGetValue( at, out var t ) && t.Held != null && (t.Judge || t.Judging);

	int seamsDue
	{
		get
		{
			int count = 0;
			foreach ( var at in held ) count += SeamsPending( at ) ? 1 : 0;
			return count;
		}
	}

	// Arrays of spans that were let go, for the builds still to come.
	internal HeldField? TakeSpare() => spares.TryTake( out var spare ) ? spare : null;

	static int Bit( int dx, int dy ) => 1 << ((dy + 1) * 3 + dx + 1);

	// Dirty for a build that finds links, or in one.
	bool LinkBuildPending( (int X, int Y) at ) => marks.TryGetValue( at, out var mark ) && (mark.NeedsGeometry || mark.InFlight is { FindLinks: true });

	// Called for a job that will find links. The build keeps its solid spans for the mesh when a tile
	// around it is still to be built or holds its own. When one is built and holds none, the build looks
	// over an apron instead, and also from that tile's edge points, and takes triangles from as far.
	void PrepareLinks( TileJob job )
	{
		var s = State;
		BuildConfig.TryCreate( settings, out var config, out _ );
		List<(EdgePoint[], FieldFrame, int, int)>? around = null;
		for ( int dy = -1; dy <= 1; dy++ )
		{
			for ( int dx = -1; dx <= 1; dx++ )
			{
				var at = (job.X + dx, job.Y + dy);
				if ( dx == 0 && dy == 0 ) continue;
				tileLinks.TryGetValue( at, out var other );
				if ( other?.Held != null )
				{
					job.KeepSolid = true;
					other.Waits |= Bit( -dx, -dy );
				}
				else if ( LinkBuildPending( at ) ) job.KeepSolid = true;
				else if ( other != null || s.Find( at.Item1, at.Item2 ) != null )
				{
					job.Apron = true;
					if ( other is { Open.Length: > 0 } ) (around ??= new()).Add( (other.Open, config.Frame( at.Item1, at.Item2 ) with { MinZ = other.MinZ }, at.Item1, at.Item2) );
				}
			}
		}
		if ( !job.Apron ) return;

		if ( around != null ) job.Around = around.ToArray();
		var wider = new Vector3( 1, 1, 0 ) * (BuildConfig.ApronCells( settings, links ) * settings.CellSize);
		job.BoundsMin -= wider;
		job.BoundsMax += wider;
	}

	// A job for this tile came back, built or failed: the tiles around no longer wait for it.
	void LinksArrived( (int X, int Y) at )
	{
		for ( int dy = -1; dy <= 1; dy++ )
		{
			for ( int dx = -1; dx <= 1; dx++ )
			{
				if ( !tileLinks.TryGetValue( (at.X + dx, at.Y + dy), out var other ) || (other.Waits & Bit( -dx, -dy )) == 0 ) continue;
				other.Waits &= ~Bit( -dx, -dy );
				other.Judge = true;
			}
		}
	}

	// Called before the state swap for every job that built a tile or found it unchanged.
	void LinksPublished( TileJob job )
	{
		var at = (job.X, job.Y);
		RelinkAround( at );
		if ( !job.FindLinks ) return;

		var found = job.Found ?? new LinkSet();
		bool kept = job.Found != null || job.Solid != null;
		var t = new TileLinks { Own = found.Own, Seam = found.Seam, Open = found.Open, MinZ = found.MinZ };
		if ( tileLinks.TryGetValue( at, out var old ) )
		{
			// What the tile had stays until the links of this build are made: a tile built again with
			// the same links keeps them, ids and all.
			if ( kept ) (t.Made, t.Ids, t.Refused, old.Made, old.Ids, old.Refused) = (old.Made, old.Ids, old.Refused, [], [], 0);
			Forget( at, old );
		}
		Rejudge( at, found.Incoming, found.IncomingFrom );
		if ( !kept ) return;

		tileLinks[at] = t;
		if ( job.Solid == null ) return;

		t.Held = job.Solid;
		t.Judge = true;
		t.Tried = new bool[t.Open.Length];
		for ( int dy = -1; dy <= 1; dy++ )
		{
			for ( int dx = -1; dx <= 1; dx++ )
			{
				if ( (dx != 0 || dy != 0) && LinkBuildPending( (at.X + dx, at.Y + dy) ) ) t.Waits |= Bit( dx, dy );
			}
		}
		held.Add( at );
		HeldBytes += t.Held.Bytes;
		HeldPeak = Math.Max( HeldPeak, HeldBytes );
	}

	void LinksRemoved( (int X, int Y) at )
	{
		if ( !links.Enabled ) return;
		RelinkAround( at );
		LinksArrived( at );
		if ( tileLinks.TryGetValue( at, out var t ) ) Forget( at, t );
		Rejudge( at, [], [] );
	}

	// The tile was built again or removed: what the tiles around had found into it is void. In its place
	// come the links its build found from their edge points; a tile that holds spans judges again.
	void Rejudge( (int X, int Y) at, FoundLink[] incoming, (int X, int Y)[] incomingFrom )
	{
		BuildConfig.TryCreate( settings, out var config, out _ );
		float reach = links.Reach( settings ) + 2 * settings.AgentRadius;
		for ( int dy = -1; dy <= 1; dy++ )
		{
			for ( int dx = -1; dx <= 1; dx++ )
			{
				var from = (at.X + dx, at.Y + dy);
				if ( (dx == 0 && dy == 0) || !tileLinks.TryGetValue( from, out var other ) ) continue;
				linkScratch.Clear();
				foreach ( ref readonly var link in other.Seam.AsSpan() )
				{
					if ( GetTileAt( link.End ) != at ) linkScratch.Add( link );
				}
				for ( int i = 0; i < incoming.Length; i++ )
				{
					if ( incomingFrom[i] == from ) linkScratch.Add( incoming[i] );
				}
				if ( linkScratch.Count != other.Seam.Length || incoming.Length > 0 ) other.Seam = linkScratch.ToArray();
				if ( other.Held == null ) continue;

				// Its edge points that were judged against what stood here before.
				var frame = config.Frame( from.Item1, from.Item2 ) with { MinZ = other.MinZ };
				for ( int i = 0; i < other.Tried.Length; i++ )
				{
					if ( !other.Tried[i] ) continue;
					other.Open[i].Unpack( frame, out var edge, out var outward );
					if ( !LinkBuilder.Toward( edge, outward, reach, settings.AgentRadius, new Vector2( at.X, at.Y ) * TileWorld, TileWorld ) ) continue;
					other.Tried[i] = false;
					other.Judge = true;
					other.Stale = other.Judging;
				}
			}
		}
	}

	void Forget( (int X, int Y) at, TileLinks t )
	{
		if ( t.Made.Length > 0 ) LinksChanged( at );
		Unmake( t );
		if ( t.Held != null )
		{
			HeldBytes -= t.Held.Bytes;
			held.Remove( at );
		}
		tileLinks.Remove( at );
	}

	void Unmake( TileLinks t )
	{
		foreach ( int id in t.Ids ) Mesh.FreeLink( id );
		linksRefused -= t.Refused;
		t.Made = [];
		t.Ids = [];
		t.Refused = 0;
	}

	void RelinkAround( (int X, int Y) at )
	{
		if ( !links.Enabled ) return;
		for ( int dy = -1; dy <= 1; dy++ )
		{
			for ( int dx = -1; dx <= 1; dx++ ) relink.Add( (at.X + dx, at.Y + dy) );
		}
	}

	// In a commit, with the tiles of the new state in place. A tile that holds spans lets them go when
	// every tile around it has come back and neither it nor one of those has edge points left to judge.
	// Then the links of every tile that changed, or has a changed tile beside it, are made again.
	void SettleLinks( MeshState next )
	{
		linkJudge ??= new LinkBuilder();
		linkQuery ??= new NavQuery( this );

		// With nothing left to judge anywhere there is no round to wait for.
		bool due = seamJob != null;
		for ( int i = 0; i < held.Count; i++ )
		{
			var t = tileLinks[held[i]];
			if ( i < held.Count - MaxHeldFields ) (t.Waits, t.Judge) = (0, true);
			t.Round = t.Waits != 0 ? 0 : t.Round != 0 ? t.Round : seamRound + 1;
			if ( !t.Judge ) continue;
			pointScratch.Clear();
			Judgeable( held[i], t, pointScratch );
			t.Judge = pointScratch.Count > 0;
			due |= t.Judge && !marks.ContainsKey( held[i] );
		}
		held.RemoveAll( at =>
		{
			var t = tileLinks[at];
			if ( t.Waits != 0 || t.Judge || t.Judging || t.Held!.Readers > 0 || (due && seamsDone < t.Round) ) return false;
			HeldBytes -= t.Held.Bytes;
			if ( marks.Count > 0 && t.Held.Reusable ) spares.Add( t.Held );
			t.Held = null;
			t.Tried = [];
			// With the tiles after it, which leave out what this one has.
			relink.Add( at );
			relink.Add( (at.X + 1, at.Y) );
			for ( int dx = -1; dx <= 1; dx++ ) relink.Add( (at.X + dx, at.Y + 1) );
			return true;
		} );
		if ( marks.Count == 0 ) spares.Clear();

		relinkOrder.Clear();
		relinkOrder.AddRange( relink );
		relinkOrder.Sort( RowOrder );
		relink.Clear();
		foreach ( var at in relinkOrder )
		{
			if ( tileLinks.TryGetValue( at, out var t ) && t.Held == null ) MakeLinks( next, at, t );
		}
	}

	// The edge points of a held tile that are still to be judged and whose way comes over no tile that
	// is still to come back.
	void Judgeable( (int X, int Y) at, TileLinks t, List<int> points )
	{
		BuildConfig.TryCreate( settings, out var config, out _ );
		var frame = config.Frame( at.X, at.Y ) with { MinZ = t.MinZ };
		float reach = links.Reach( settings ) + 2 * settings.AgentRadius;
		for ( int i = 0; i < t.Open.Length; i++ )
		{
			if ( t.Tried[i] ) continue;
			bool waits = false;
			if ( t.Waits != 0 )
			{
				t.Open[i].Unpack( frame, out var edge, out var outward );
				for ( int k = 0; k < 9 && !waits; k++ )
				{
					int dx = k % 3 - 1, dy = k / 3 - 1;
					waits = (t.Waits & Bit( dx, dy )) != 0 && LinkBuilder.Toward( edge, outward, reach, settings.AgentRadius, new Vector2( at.X + dx, at.Y + dy ) * TileWorld, TileWorld );
				}
			}
			if ( !waits ) points.Add( i );
		}
	}

	// What a job judges of links across tile borders, on a worker: every held tile with edge points
	// that can be judged now, each with the spans of the tiles around it, which stay as they are until
	// the job is back. One such job at a time. A tile that is dirty or being built is left out: it is
	// judged when it is back.
	SeamTask[]? TakeSeams()
	{
		if ( seamJob != null || held.Count == 0 ) return null;
		seamRound++;
		List<SeamTask>? tasks = null;
		foreach ( var at in held )
		{
			var t = tileLinks[at];
			if ( !t.Judge || marks.ContainsKey( at ) ) continue;
			t.Judge = false;
			pointScratch.Clear();
			Judgeable( at, t, pointScratch );
			if ( pointScratch.Count == 0 ) continue;

			var task = new SeamTask { X = at.X, Y = at.Y, MinZ = t.MinZ, Open = t.Open, Points = pointScratch.ToArray() };
			foreach ( int point in task.Points ) t.Tried[point] = true;
			for ( int k = 0; k < 9; k++ )
			{
				if ( !tileLinks.TryGetValue( (at.X + k % 3 - 1, at.Y + k / 3 - 1), out var other ) || other.Held == null ) continue;
				task.Held[k] = other.Held;
				other.Held.Readers++;
			}
			t.Judging = true;
			(tasks ??= new()).Add( task );
			seamRecords.Add( t );
		}
		if ( tasks == null ) seamsDone = seamRound;
		return tasks?.ToArray();
	}

	// The job that judged links across tile borders is back, or was abandoned.
	void SeamsJudged( TileJob job )
	{
		seamJob = null;
		seamsDone = seamRound;
		bool done = job.Status != TileJobStatus.Pending;
		var tasks = job.Seams!;
		for ( int i = 0; i < tasks.Length; i++ )
		{
			var task = tasks[i];
			foreach ( var field in task.Held )
			{
				if ( field == null ) continue;
				field.Readers--;
				// An abandoned job may still be reading.
				field.Reusable &= done;
			}

			// Built again or removed since: its record is another, or gone.
			var t = seamRecords[i];
			t.Judging = false;
			if ( !tileLinks.TryGetValue( (task.X, task.Y), out var current ) || current != t ) continue;
			if ( !done || t.Stale )
			{
				// Again: the job was abandoned, or a tile around was built again or removed while it ran.
				foreach ( int point in task.Points ) t.Tried[point] = false;
				(t.Judge, t.Stale) = (true, false);
				continue;
			}

			// An edge point judged again brings links the tile already has.
			linkScratch.Clear();
			linkScratch.AddRange( task.Found );
			linkScratch.RemoveAll( link => Array.Exists( t.Seam, other => other.Start == link.Start && other.End == link.End && other.Kind == link.Kind ) );
			if ( linkScratch.Count > 0 ) t.Seam = [.. t.Seam, .. linkScratch];
		}
		seamRecords.Clear();

		// The tile a job that did nothing else was named after was not handed out meanwhile.
		var named = (job.X, job.Y);
		if ( job.SeamsOnly && marks.TryGetValue( named, out var mark ) && mark.Dirty && mark.InFlight == null ) dirtyQueue.Add( named );
	}

	// What the builds found becomes links of the mesh, a drop with Back and the jump back up each on its
	// own: both ends on a polygon, no blocking volume in the way, not as short to walk, thinned, and not one
	// a tile before this one already has. The jump back up stays or goes with its drop, so thinning never
	// keeps the one of a pair here and the other a few steps along the ledge.
	void MakeLinks( MeshState next, (int X, int Y) at, TileLinks t )
	{
		float spacing = links.Spacing;
		linkScratch.Clear();
		upScratch.Clear();
		for ( int i = 0, own = t.Own.Length; i < own + t.Seam.Length; i++ )
		{
			ref readonly var link = ref i < own ? ref t.Own[i] : ref t.Seam[i - own];
			FoundLink up = default;
			bool made = Make( next, link with { Back = false }, out var drop ), back = link.Back && Make( next, link.Up, out up );
			if ( made ) linkScratch.Add( drop with { Back = back } );
			if ( made && back ) upScratch.Add( (drop.Start, drop.End, up) );
			else if ( back ) linkScratch.Add( up );
		}
		linkJudge!.Thin( linkScratch, spacing );
		linkScratch.RemoveAll( link =>
		{
			// The row before, and the tile before in this row: the order links are made in.
			for ( int dy = -1; dy <= 0; dy++ )
			{
				for ( int dx = -1; dx <= (dy < 0 ? 1 : -1); dx++ )
				{
					if ( !tileLinks.TryGetValue( (at.X + dx, at.Y + dy), out var before ) ) continue;
					foreach ( ref readonly var other in before.Made.AsSpan() )
					{
						if ( LinkBuilder.Same( other, link, spacing ) ) return true;
					}
				}
			}
			return false;
		} );

		// Beside each drop that stays, its jump back up.
		for ( int i = 0, count = linkScratch.Count; i < count; i++ )
		{
			var link = linkScratch[i];
			if ( !link.Back ) continue;
			linkScratch[i] = link with { Back = false };
			foreach ( var (start, end, up) in upScratch )
			{
				if ( start != link.Start || end != link.End ) continue;
				linkScratch.Add( up );
				break;
			}
		}

		// The same links as before keep their ids, and the paths over them stay good.
		var span = CollectionsMarshal.AsSpan( linkScratch );
		bool same = span.Length == t.Made.Length;
		for ( int i = 0; i < span.Length && same; i++ ) same = span[i].Start == t.Made[i].Start && span[i].End == t.Made[i].End && span[i].Kind == t.Made[i].Kind && span[i].Bidirectional == t.Made[i].Bidirectional;
		if ( same && t.Refused == 0 ) return;

		Unmake( t );
		t.Made = span.ToArray();
		if ( !same ) LinksChanged( at );
		if ( !links.Insert ) return;

		t.Ids = new int[span.Length];
		for ( int i = 0; i < span.Length; i++ )
		{
			t.Ids[i] = Mesh.AddLink( Index, ToLink( span[i] ) with { Generated = true } );
			if ( t.Ids[i] < 0 ) t.Refused++;
		}
		linksRefused += t.Refused;
	}

	bool Make( MeshState next, in FoundLink link, out FoundLink snapped )
	{
		return linkQuery!.OnMesh( next, link, out uint from, out uint to, out snapped ) && !LinkBlocked( snapped ) && !linkQuery.ShortWalk( next, from, snapped.Start, to, snapped.End );
	}

	// Whether the body of an agent on this link passes through a blocking volume, or over or under one
	// by less than its height.
	bool LinkBlocked( in FoundLink link )
	{
		Vector3 start = link.Start, end = link.End;
		float dx = end.X - start.X, dy = end.Y - start.Y, flat = MathF.Sqrt( dx * dx + dy * dy );
		float cell = settings.CellSize, height = settings.AgentHeight, rise = MathF.Max( settings.CellHeight, height * 0.25f );
		var side = flat > 0 ? new Vector3( -dy / flat, dx / flat, 0 ) * cell : default;
		int steps = Math.Max( 1, (int)MathF.Ceiling( flat / cell ) ), lanes = Math.Max( BuildConfig.RadiusCells( settings ) - 1, 0 );
		var min = Vector3.Min( start, end ) - new Vector3( settings.AgentRadius, settings.AgentRadius, height + links.MaxJumpUp );
		var max = Vector3.Max( start, end ) + new Vector3( settings.AgentRadius, settings.AgentRadius, height + links.MaxJumpUp );

		var mesh = Mesh;
		for ( int id = 0; id < mesh.VolumeHigh; id++ )
		{
			if ( !mesh.VolumeUsed[id] || mesh.VolumeDefs[id].Area != NavFilter.NoArea ) continue;
			ref readonly var volume = ref mesh.VolumeDefs[id];
			if ( !VolumeMarker.WorldBounds( volume, out var volumeMin, out var volumeMax ) ) continue;
			if ( volumeMin.X > max.X || volumeMax.X < min.X || volumeMin.Y > max.Y || volumeMax.Y < min.Y || volumeMin.Z > max.Z || volumeMax.Z < min.Z ) continue;

			for ( int i = 0; i <= steps; i++ )
			{
				float t = i / (float)steps;
				float z = link.Kind == LinkKind.Drop ? start.Z : links.JumpHeight( settings, start, end, t );
				float feet = link.Kind == LinkKind.Drop && i == steps ? end.Z : z;
				for ( int lane = -lanes; lane <= lanes; lane++ )
				{
					var point = new Vector3( start.X + dx * t, start.Y + dy * t, 0 ) + side * lane;
					for ( point.Z = feet - height; point.Z <= z + height; point.Z += rise )
					{
						if ( VolumeMarker.Contains( volume, point ) ) return true;
					}
				}
			}
		}
		return false;
	}
}
