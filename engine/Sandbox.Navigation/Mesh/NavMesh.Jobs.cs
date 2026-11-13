using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nav;

public sealed partial class NavMesh
{
	// Per tile: whether something changed that no job has seen yet, and the job in flight. A tile that
	// is neither dirty nor in flight has no entry. NeedsGeometry belongs to the dirty mark: the triangles
	// changed. Discard is set by a RemoveTile on the job in flight.
	struct Mark
	{
		public bool Dirty, NeedsGeometry, Discard;
		public TileJob? InFlight;
	}

	readonly Dictionary<(int X, int Y), Mark> marks = new();
	// In the order tiles became dirty. A dirty tile without a job has at least one entry; an entry for
	// anything else is skipped when it comes up.
	readonly Queue<(int X, int Y)> dirtyQueue = new();
	readonly List<TileJob> published = new();

	const byte LinkFree = 0, LinkUsed = 1, LinkDirty = 2, LinkRemoved = 3;

	NavLink[] linkDefs = [];
	byte[] linkStatus = [];
	int linkHigh;
	bool linksDirty;

	/// <summary>
	/// Tiles that are dirty or have a job in flight. Owner thread.
	/// </summary>
	public int PendingTiles => marks.Count;

	/// <summary>
	/// Whether this tile is dirty or has a job in flight. False once every change made to it so far has
	/// been built and committed, or failed. Owner thread.
	/// </summary>
	public bool IsPending( int tileX, int tileY ) => marks.ContainsKey( (tileX, tileY) );

	/// <summary>
	/// Geometry changed inside this box: every tile it can affect is rebuilt by the next jobs.
	/// </summary>
	public void MarkDirty( Vector3 min, Vector3 max )
	{
		if ( !Geo.IsFinite( min ) || !Geo.IsFinite( max ) ) throw new ArgumentException( "The box must be finite" );
		float border = BorderCells * settings.CellSize;
		int minX = TileOf( min.X - border ), maxX = TileOf( max.X + border ), minY = TileOf( min.Y - border ), maxY = TileOf( max.Y + border );
		// More marks than a mesh can hold tiles is a mistake, and a box that saturates would never end.
		if ( ((double)maxX - minX + 1) * ((double)maxY - minY + 1) > PolyRef.MaxTiles ) throw new ArgumentException( "The box covers more tiles than a mesh can hold" );
		for ( int y = minY; y <= maxY; y++ )
			for ( int x = minX; x <= maxX; x++ )
				MarkDirty( x, y, true );
	}

	/// <summary>
	/// The triangles of this tile changed: it is rebuilt by a job that asks for them. Owner thread.
	/// </summary>
	public void MarkDirty( int tileX, int tileY ) => MarkDirty( tileX, tileY, true );

	// geometry: the triangles changed, so the rebuild cache cannot serve the next job. Without it only
	// what is marked on the tile changed.
	internal void MarkDirty( int tileX, int tileY, bool geometry )
	{
		ref var mark = ref CollectionsMarshal.GetValueRefOrAddDefault( marks, (tileX, tileY), out _ );
		mark.NeedsGeometry |= geometry;
		if ( mark.Dirty ) return;
		mark.Dirty = true;
		// A tile in flight is queued again when its job comes back.
		if ( mark.InFlight == null ) dirtyQueue.Enqueue( (tileX, tileY) );
	}

	/// <summary>
	/// Takes effect at the next Commit. The result of a job in flight for this tile is discarded.
	/// </summary>
	public void RemoveTile( int tileX, int tileY )
	{
		changes.Add( new( tileX, tileY, null, null ) );
		TileRemoved( tileX, tileY );
		ref var mark = ref CollectionsMarshal.GetValueRefOrNullRef( marks, (tileX, tileY) );
		if ( Unsafe.IsNullRef( ref mark ) ) return;
		mark.Dirty = mark.NeedsGeometry = false;
		mark.Discard = mark.InFlight != null;
		if ( mark.InFlight == null ) marks.Remove( (tileX, tileY) );
	}

	/// <summary>
	/// Hands out jobs for dirty tiles, oldest first, at most one in flight per tile. Returns how many.
	/// A tile that is marked again while its job is in flight gets its next job after that one has been
	/// committed.
	/// </summary>
	public int TakeJobs( Span<TileJob> jobs )
	{
		CheckLoadedTiles();
		int count = 0;
		float border = BorderCells * settings.CellSize;
		while ( count < jobs.Length && dirtyQueue.TryDequeue( out var at ) )
		{
			ref var mark = ref CollectionsMarshal.GetValueRefOrNullRef( marks, at );
			if ( Unsafe.IsNullRef( ref mark ) || !mark.Dirty || mark.InFlight != null ) continue;

			var job = new TileJob
			{
				X = at.X,
				Y = at.Y,
				NeedsGeometry = true,
				BoundsMin = new Vector3( at.X * TileWorld - border, at.Y * TileWorld - border, float.NegativeInfinity ),
				BoundsMax = new Vector3( (at.X + 1) * TileWorld + border, (at.Y + 1) * TileWorld + border, float.PositiveInfinity ),
				Mesh = this,
				GeometryChanged = mark.NeedsGeometry
			};

			bool issue = true;
			PrepareJob( job, mark.NeedsGeometry, ref issue );
			if ( !issue )
			{
				marks.Remove( at );
				continue;
			}
			mark.Dirty = mark.NeedsGeometry = false;
			mark.InFlight = job;
			jobs[count++] = job;
		}
		return count;
	}

	/// <summary>
	/// Publishes built jobs, pending tile removals and link changes in one state swap. Returns the number
	/// of failed jobs. A failed tile stays clean until it is marked dirty again. A job still Pending is
	/// abandoned: its tile stays dirty and a later result for it is ignored. A tile that was marked dirty
	/// while its job was in flight gets that job's result all the same and stays dirty, so a tile that
	/// changes all the time shows a build that is one job old instead of none. Only a RemoveTile makes
	/// the job in flight count for nothing.
	/// </summary>
	public int Commit( ReadOnlySpan<TileJob> jobs )
	{
		int failed = 0;
		foreach ( var job in jobs )
		{
			var at = (job.X, job.Y);
			ref var mark = ref CollectionsMarshal.GetValueRefOrNullRef( marks, at );
			if ( job.Mesh != this || Unsafe.IsNullRef( ref mark ) || mark.InFlight != job ) continue;
			mark.InFlight = null;

			var status = job.Status;
			if ( mark.Discard ) mark.Discard = false;
			else if ( status is TileJobStatus.Built or TileJobStatus.Unchanged )
			{
				if ( status == TileJobStatus.Built ) changes.Add( new( job.X, job.Y, job.Built, job ) );
				published.Add( job );
			}
			else
			{
				// Nothing of this job is kept, so the next one needs the triangles if this one did.
				mark.NeedsGeometry |= job.GeometryChanged || status == TileJobStatus.NeedsGeometry;
				if ( status == TileJobStatus.Failed ) failed++;
				else mark.Dirty = true;
			}

			if ( mark.Dirty ) dirtyQueue.Enqueue( at );
			else marks.Remove( at );
		}

		// What a job leaves behind is kept only once its tile is in: a tile the table had no room for
		// comes out of Apply as Failed.
		failed += Apply();
		foreach ( var job in published )
		{
			if ( job.Status != TileJobStatus.Failed ) JobPublished( job );
		}
		published.Clear();
		return failed;
	}

	/// <summary>
	/// Publishes pending link and RemoveTile changes without jobs.
	/// </summary>
	public int Commit() => Commit( default );

	// The border a tile build reads around itself, in cells.
	int BorderCells => global::Nav.Build.BuildConfig.BorderCells( settings );

	// Where generation hooks in, see NavMesh.Build.cs. PrepareJob gives a job its volumes and cached
	// field, or clears issue when the tile turns out clean. JobPublished keeps what a job leaves behind
	// for later rebuilds. TileRemoved drops what was kept for a tile.
	partial void PrepareJob( TileJob job, bool geometryForced, ref bool issue );
	partial void JobPublished( TileJob job );
	partial void TileRemoved( int tileX, int tileY );

	/// <summary>
	/// Takes effect at the next Commit. Returns the link id, or -1 when no id is free.
	/// </summary>
	public int AddLink( in NavLink link )
	{
		int id = 0;
		while ( id < linkHigh && linkStatus[id] is LinkUsed or LinkDirty ) id++;
		if ( id == PolyRef.MaxLinks ) return -1;
		if ( id == linkDefs.Length )
		{
			int capacity = Math.Min( PolyRef.MaxLinks, Math.Max( 16, linkDefs.Length * 2 ) );
			Array.Resize( ref linkDefs, capacity );
			Array.Resize( ref linkStatus, capacity );
		}
		linkHigh = Math.Max( linkHigh, id + 1 );
		linkDefs[id] = Checked( link );
		linkStatus[id] = LinkDirty;
		linksDirty = true;
		return id;
	}

	// An area id out of range is refused: a filter has one bit per id. No area means ground.
	static NavLink Checked( in NavLink link )
	{
		if ( link.Area > NavFilter.MaxArea ) throw new ArgumentOutOfRangeException( nameof( link ), $"Link area {link.Area} is above {NavFilter.MaxArea}" );
		var value = link;
		if ( value.Area == NavFilter.NoArea ) value.Area = NavFilter.DefaultArea;
		return value;
	}

	/// <summary>
	/// Throws for an id that is not in use and for an area id above NavFilter.MaxArea, as AddLink does.
	/// </summary>
	public void SetLink( int id, in NavLink link )
	{
		if ( (uint)id >= (uint)linkHigh || linkStatus[id] is LinkFree or LinkRemoved ) throw new ArgumentException( "No such link", nameof( id ) );
		linkDefs[id] = Checked( link );
		linkStatus[id] = LinkDirty;
		linksDirty = true;
	}

	public void RemoveLink( int id )
	{
		if ( (uint)id >= (uint)linkHigh || linkStatus[id] is LinkFree or LinkRemoved ) return;
		linkStatus[id] = LinkRemoved;
		linksDirty = true;
	}

	/// <summary>
	/// The ends of a link as of the last Commit: snapped where an end found the mesh, as placed otherwise.
	/// </summary>
	public bool GetLinkEnds( int id, out Vector3 start, out Vector3 end, out bool startConnected, out bool endConnected )
	{
		var links = State.Links;
		if ( (uint)id >= (uint)links.Length || links[id].Serial == 0 )
		{
			start = end = default;
			startConnected = endConnected = false;
			return false;
		}
		ref readonly var entry = ref links[id];
		start = entry.StartPoint;
		end = entry.EndPoint;
		startConnected = entry.StartPoly != PolyRef.NoId;
		endConnected = entry.EndPoly != PolyRef.NoId;
		return true;
	}
}
