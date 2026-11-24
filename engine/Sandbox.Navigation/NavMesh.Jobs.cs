using System.Numerics;
using Nav.Build;

namespace Nav;

public sealed partial class NavMesh
{
	// The commit count every published build is stamped from (Serial), and whether the commit under way
	// has counted yet. maxRound: the highest count whose serials still fit 32 bits.
	uint commits;
	bool counted;
	readonly uint maxRound;

	// Counted up before a commit of several agent types publishes and again after, so a Save reads
	// every type as of one commit: odd while a commit is publishing.
	int publishing;

	// Which type TakeJobs serves first.
	int rotation;

	/// <summary>
	/// The serial of every build agent type type publishes in the commit under way: the mesh's commit
	/// count times the number of types, plus the type. So a reused slot never validates an old PolyRef,
	/// and a PolyRef of one type never validates in another: for a mesh of one type the serial is the
	/// count itself. These are the 32 serial bits of a PolyRef. After 2^32 / types commits (more than two
	/// years at sixty a second for one type, one year for two, 52 days for sixteen) the count starts
	/// again at 1, 0 being no serial. A PolyRef kept through all of them could then name a later build
	/// of its slot, which is accepted: a wider serial would cost every PolyRef.
	/// </summary>
	internal uint Serial( int type )
	{
		if ( !counted )
		{
			counted = true;
			if ( ++commits == 0 || commits > maxRound )
			{
				commits = 1;
				foreach ( var t in types ) t.ForgetStamps();
			}
		}
		return commits * (uint)types.Length + (uint)type;
	}

	/// <summary>
	/// The whole level in one call: sorts the triangles into tiles, builds every tile they reach on this
	/// thread for every agent type and commits in batches. Nothing of the input is kept. A triangle is
	/// ground when the cross product of v1 - v0 and v2 - v0 points up, within the slope limit. A triangle
	/// with an index out of range or a coordinate that is not finite is left out and counted as rejected.
	/// Tiles that were already dirty are built too, and one of those that no triangle reaches any more
	/// is removed. Returns the number of failed tiles; failures, when given, gets each of them with its
	/// reason. A failures list of this kind cannot say which agent type failed: on a mesh of several
	/// types pass the other.
	/// </summary>
	public int Build( NavBuilder builder, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, List<(int X, int Y, string Error)>? failures = null )
	{
		if ( failures != null && types.Length > 1 ) throw new ArgumentException( $"The mesh has {types.Length} agent types: pass a list of failures that names the agent type", nameof( failures ) );
		return Build( builder, vertices, indices, failures, null );
	}

	/// <summary>
	/// Build, with each failed tile's agent type.
	/// </summary>
	public int Build( NavBuilder builder, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, List<(int AgentType, int X, int Y, string Error)> failures )
	{
		return Build( builder, vertices, indices, null, failures );
	}

	int Build( NavBuilder builder, ReadOnlySpan<Vector3> vertices, ReadOnlySpan<int> indices, List<(int X, int Y, string Error)>? failures, List<(int AgentType, int X, int Y, string Error)>? typed )
	{
		var bins = new TriangleBins[types.Length];
		var wide = new TriangleBins?[types.Length];
		foreach ( var type in types )
		{
			var sorted = bins[type.Index] = new TriangleBins( type.Settings, vertices, indices );
			Interlocked.Add( ref type.TrianglesRejected, sorted.Invalid );
			foreach ( var (x, y) in sorted.Tiles ) type.MarkDirty( x, y );
		}

		var jobs = new TileJob[64];
		int failed = 0;
		while ( true )
		{
			int count = TakeJobs( jobs );
			if ( count == 0 ) break;
			for ( int i = 0; i < count; i++ )
			{
				var type = jobs[i].Owner!;
				// A tile built beside one that is not looks further for its links.
				var sorted = jobs[i].Apron ? wide[type.Index] ??= new TriangleBins( type.Settings, vertices, indices, BuildConfig.ApronCells( type.Settings, type.LinkSettings ) ) : bins[type.Index];
				builder.Build( jobs[i], vertices, sorted.Indices( jobs[i].X, jobs[i].Y ).Span );
			}
			failed += Commit( jobs.AsSpan( 0, count ) );
			for ( int i = 0; i < count; i++ )
			{
				if ( jobs[i].Status != TileJobStatus.Failed ) continue;
				failures?.Add( (jobs[i].X, jobs[i].Y, jobs[i].Error ?? "") );
				typed?.Add( (jobs[i].AgentType, jobs[i].X, jobs[i].Y, jobs[i].Error ?? "") );
			}
		}
		return failed + Commit();
	}

	/// <summary>
	/// Tiles of every agent type that are dirty or have a job in flight, and those whose links across a
	/// tile border a job is still to judge. Owner thread.
	/// </summary>
	public int PendingTiles
	{
		get
		{
			int count = 0;
			foreach ( var type in types ) count += type.PendingTiles;
			return count;
		}
	}

	/// <summary>
	/// Whether this tile is dirty, has a job in flight or has links across its borders still to be
	/// judged. False once every change made to it so far has been built and committed, or failed.
	/// Owner thread.
	/// </summary>
	public bool IsPending( int tileX, int tileY ) => Single.IsPending( tileX, tileY );
	public bool IsPending( int agentType, int tileX, int tileY ) => TypeAt( agentType ).IsPending( tileX, tileY );

	/// <summary>
	/// Geometry changed inside this box: every tile of every agent type it can affect is rebuilt by the
	/// next jobs. Throws for a box that is not finite or covers more tiles than a type can hold.
	/// </summary>
	public void MarkDirty( Vector3 min, Vector3 max )
	{
		foreach ( var type in types ) type.CheckDirtyBox( min, max );
		foreach ( var type in types ) type.MarkDirty( min, max );
	}

	/// <summary>
	/// The triangles of this tile changed: it is rebuilt by a job that asks for them. Owner thread.
	/// </summary>
	public void MarkDirty( int tileX, int tileY ) => Single.MarkDirty( tileX, tileY );
	public void MarkDirty( int agentType, int tileX, int tileY ) => TypeAt( agentType ).MarkDirty( tileX, tileY );

	/// <summary>
	/// Takes effect at the next Commit. The result of a job in flight for this tile is discarded.
	/// </summary>
	public void RemoveTile( int tileX, int tileY ) => Single.RemoveTile( tileX, tileY );
	public void RemoveTile( int agentType, int tileX, int tileY ) => TypeAt( agentType ).RemoveTile( tileX, tileY );

	/// <summary>
	/// Hands out jobs for dirty tiles of every agent type, oldest first per type, at most one in flight
	/// per tile. Returns how many. A tile that is marked again while its job is in flight gets its next
	/// job after that one has been committed. With generated links, one job at a time per type also
	/// judges links across tile borders; when no tile is dirty that is a job that builds no tile and
	/// needs no geometry, built and committed like the others. On a mesh of several types the span is
	/// shared out evenly between them, starting with another type each call, and what one type cannot
	/// use goes to the others.
	/// </summary>
	public int TakeJobs( Span<TileJob> jobs )
	{
		int n = types.Length;
		if ( n == 1 ) return types[0].TakeJobs( jobs );

		int count = 0, first = rotation;
		rotation = (rotation + 1) % n;
		Span<bool> filled = stackalloc bool[n];
		for ( int i = 0; i < n; i++ )
		{
			int k = (first + i) % n, share = (jobs.Length - count + n - i - 1) / (n - i);
			int taken = types[k].TakeJobs( jobs.Slice( count, share ) );
			filled[k] = share > 0 && taken == share;
			count += taken;
		}
		for ( int i = 0; i < n && count < jobs.Length; i++ )
		{
			int k = (first + i) % n;
			if ( filled[k] ) count += types[k].TakeJobs( jobs[count..] );
		}
		return count;
	}

	/// <summary>
	/// Publishes built jobs of every agent type, pending tile removals and link changes, one new state
	/// per type that changed. Returns the number of failed jobs. A failed tile stays clean until it is
	/// marked dirty again. A job still Pending is abandoned: its tile stays dirty and a later result for
	/// it is ignored. A tile that was marked dirty while its job was in flight gets that job's result all
	/// the same and stays dirty, so a tile that changes all the time shows a build that is one job old
	/// instead of none. Only a RemoveTile makes the job in flight count for nothing. Jobs of another mesh
	/// are left out.
	/// </summary>
	public int Commit( ReadOnlySpan<TileJob> jobs )
	{
		counted = false;
		if ( types.Length == 1 ) return types[0].Commit( jobs );

		uint before = 0, after = 0;
		foreach ( var type in types ) before += type.State.Version;
		Interlocked.Increment( ref publishing );
		int failed = 0;
		foreach ( var type in types ) failed += type.Commit( jobs );
		foreach ( var type in types ) after += type.State.Version;
		if ( after != before ) Volatile.Write( ref version, version + 1 );
		Interlocked.Increment( ref publishing );
		return failed;
	}

	/// <summary>
	/// Publishes pending link and RemoveTile changes without jobs.
	/// </summary>
	public int Commit() => Commit( default );
}
