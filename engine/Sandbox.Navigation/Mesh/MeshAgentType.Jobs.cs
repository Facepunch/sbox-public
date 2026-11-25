using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Nav;

internal sealed partial class MeshAgentType
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
	// In the order tiles became dirty, from dirtyHead on. A dirty tile without a job has at least one
	// entry; an entry for anything else is skipped when it comes up. With links, the tiles that became
	// dirty between two TakeJobs are handed out row by row, so that a generation holds the solid spans
	// of two rows of tiles and not of all of them: dirtySorted is where the tiles not yet sorted begin.
	readonly List<(int X, int Y)> dirtyQueue = new();
	int dirtyHead, dirtySorted;
	readonly List<TileJob> published = new();
	readonly List<(int X, int Y)> arrived = new();

	internal const byte LinkFree = 0, LinkUsed = 1, LinkDirty = 2, LinkRemoved = 3;

	// Per link id of the mesh, what it is in this type. The definitions and ids are the mesh's.
	byte[] linkStatus = [];
	// Ids added, set or removed here since the last commit, each once.
	readonly List<int> linkQueue = new();

	/// <summary>
	/// Tiles that are dirty or have a job in flight, and those whose links across a tile border a job is
	/// still to judge. Owner thread.
	/// </summary>
	public int PendingTiles => marks.Count + seamsDue;

	/// <summary>
	/// Whether this tile is dirty, has a job in flight or has links across its borders still to be
	/// judged. False once every change made to it so far has been built and committed, or failed.
	/// Owner thread.
	/// </summary>
	public bool IsPending( int tileX, int tileY ) => marks.ContainsKey( (tileX, tileY) ) || SeamsPending( (tileX, tileY) );

	// Throws for a box MarkDirty refuses.
	internal void CheckDirtyBox( Vector3 min, Vector3 max )
	{
		if ( !Geo.IsFinite( min ) || !Geo.IsFinite( max ) ) throw new ArgumentException( "The box must be finite" );
		float border = BorderCells * settings.CellSize;
		int minX = TileOf( min.X - border ), maxX = TileOf( max.X + border ), minY = TileOf( min.Y - border ), maxY = TileOf( max.Y + border );
		// More marks than a mesh can hold tiles is a mistake, and a box that saturates would never end.
		if ( ((double)maxX - minX + 1) * ((double)maxY - minY + 1) > MaxTiles ) throw new ArgumentException( "The box covers more tiles than a mesh can hold" );
	}

	// Geometry changed inside this box, checked by CheckDirtyBox: every tile it can affect is rebuilt by
	// the next jobs.
	internal void MarkDirty( Vector3 min, Vector3 max )
	{
		float border = BorderCells * settings.CellSize;
		int minX = TileOf( min.X - border ), maxX = TileOf( max.X + border ), minY = TileOf( min.Y - border ), maxY = TileOf( max.Y + border );
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
		if ( mark.InFlight == null ) dirtyQueue.Add( (tileX, tileY) );
	}

	/// <summary>
	/// Takes effect at the next Commit. The result of a job in flight for this tile is discarded.
	/// </summary>
	public void RemoveTile( int tileX, int tileY )
	{
		changes.Add( new( tileX, tileY, null, null ) );
		TileRemoved( tileX, tileY );
		LinksRemoved( (tileX, tileY) );
		ref var mark = ref CollectionsMarshal.GetValueRefOrNullRef( marks, (tileX, tileY) );
		if ( Unsafe.IsNullRef( ref mark ) ) return;
		mark.Dirty = mark.NeedsGeometry = false;
		mark.Discard = mark.InFlight != null;
		if ( mark.InFlight == null ) marks.Remove( (tileX, tileY) );
	}

	/// <summary>
	/// Hands out jobs for dirty tiles, oldest first, at most one in flight per tile. Returns how many.
	/// A tile that is marked again while its job is in flight gets its next job after that one has been
	/// committed. With generated links, one job at a time also judges links across tile borders; when no
	/// tile is dirty that is a job that builds no tile and needs no geometry, built and committed like
	/// the others.
	/// </summary>
	public int TakeJobs( Span<TileJob> jobs )
	{
		CheckLoadedTiles();
		int count = 0;
		float border = BorderCells * settings.CellSize;
		var seams = jobs.Length > 0 ? TakeSeams() : null;
		if ( links.Enabled && dirtySorted < dirtyQueue.Count )
		{
			dirtySorted = Math.Max( dirtySorted, dirtyHead );
			dirtyQueue.Sort( dirtySorted, dirtyQueue.Count - dirtySorted, RowOrder );
			dirtySorted = dirtyQueue.Count;
		}
		while ( count < jobs.Length && dirtyHead < dirtyQueue.Count )
		{
			var at = dirtyQueue[dirtyHead++];
			ref var mark = ref CollectionsMarshal.GetValueRefOrNullRef( marks, at );
			// The tile a job that judges links is named after waits for it: one job in flight per tile.
			if ( Unsafe.IsNullRef( ref mark ) || !mark.Dirty || mark.InFlight != null || (seamJob is { SeamsOnly: true } && at == (seamJob.X, seamJob.Y)) ) continue;

			var job = new TileJob
			{
				X = at.X,
				Y = at.Y,
				NeedsGeometry = true,
				BoundsMin = new Vector3( at.X * TileWorld - border, at.Y * TileWorld - border, float.NegativeInfinity ),
				BoundsMax = new Vector3( (at.X + 1) * TileWorld + border, (at.Y + 1) * TileWorld + border, float.PositiveInfinity ),
				Owner = this,
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

			// The first job takes the judging of links across borders along.
			if ( seams == null ) continue;
			(job.Seams, job.SeamState, seamJob, seams) = (seams, State, job, null);
		}
		if ( seams != null ) jobs[count++] = seamJob = new TileJob { X = seams[0].X, Y = seams[0].Y, Owner = this, Seams = seams, SeamState = State, SeamsOnly = true };
		if ( dirtyHead == dirtyQueue.Count )
		{
			dirtyQueue.Clear();
			dirtyHead = dirtySorted = 0;
		}
		return count;
	}

	static readonly Comparer<(int X, int Y)> RowOrder = Comparer<(int X, int Y)>.Create( static ( a, b ) => a.Y != b.Y ? a.Y.CompareTo( b.Y ) : a.X.CompareTo( b.X ) );

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
			if ( job.Owner == this && job == seamJob )
			{
				SeamsJudged( job );
				if ( job.SeamsOnly ) continue;
			}
			ref var mark = ref CollectionsMarshal.GetValueRefOrNullRef( marks, at );
			if ( job.Owner != this || Unsafe.IsNullRef( ref mark ) || mark.InFlight != job ) continue;
			mark.InFlight = null;

			var status = job.Status;
			if ( links.Enabled && status is not (TileJobStatus.Pending or TileJobStatus.NeedsGeometry) ) arrived.Add( at );
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

			if ( mark.Dirty ) dirtyQueue.Add( at );
			else marks.Remove( at );
		}

		// What a job leaves behind is kept only once its tile is in: a tile the table had no room for
		// comes out of Apply as Failed.
		if ( links.Enabled )
		{
			foreach ( var job in published ) LinksPublished( job );
			foreach ( var at in arrived ) LinksArrived( at );
			arrived.Clear();
		}
		failed += Apply();
		foreach ( var job in published )
		{
			if ( job.Status != TileJobStatus.Failed ) JobPublished( job );
		}
		published.Clear();
		PublishVolumeHashes();
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
	// for later rebuilds. TileRemoved drops what was kept for a tile. PublishVolumeHashes hands Save the
	// volume hashes once the state they describe is out.
	partial void PrepareJob( TileJob job, bool geometryForced, ref bool issue );
	partial void JobPublished( TileJob job );
	partial void TileRemoved( int tileX, int tileY );
	partial void PublishVolumeHashes();

	// The mesh pends a link id for this type's next commit: LinkDirty for a link added or set here,
	// LinkRemoved for one removed.
	internal void Pend( int id, byte status )
	{
		if ( linkStatus.Length <= id ) Array.Resize( ref linkStatus, Mesh.LinkCapacity );
		if ( linkStatus[id] is LinkFree or LinkUsed ) linkQueue.Add( id );
		linkStatus[id] = status;
	}

	internal bool LinkInUse( int id ) => (uint)id < (uint)linkStatus.Length && linkStatus[id] is LinkUsed or LinkDirty;

	/// <summary>
	/// The ends of a link as of the last Commit: snapped where an end found the mesh, as placed otherwise.
	/// </summary>
	public bool GetLinkEnds( int id, out Vector3 start, out Vector3 end, out bool startConnected, out bool endConnected )
	{
		ref readonly var entry = ref State.LinkAt( id );
		if ( entry.Serial == 0 )
		{
			start = end = default;
			startConnected = endConnected = false;
			return false;
		}
		start = entry.StartPoint;
		end = entry.EndPoint;
		startConnected = entry.StartPoly != PolyRef.NoId;
		endConnected = entry.EndPoly != PolyRef.NoId;
		return true;
	}
}
