using System.Numerics;
using Nav.Build;

namespace Nav;

public sealed partial class NavMesh
{
	// maxRound: the highest commit count whose serials still fit 32 bits.
	uint commits;
	bool counted;
	readonly uint maxRound;

	// Odd while a commit is publishing, so a Save reads every type as of one commit.
	int publishing;

	// Which type TakeJobs serves first.
	int rotation;

	// The PolyRef serial of a build of type in the commit under way: commit count * types + type, so a reused
	// slot never validates an old PolyRef and no PolyRef validates in another type. After 2^32 / types commits
	// the count restarts at 1; a PolyRef kept that long may then name a later build of its slot, accepted
	// because a wider serial would cost every PolyRef.
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
	/// The whole level in one call: sorts the triangles into tiles, builds every tile they reach for every
	/// agent type on this thread and commits in batches. Nothing of the input is kept. A triangle is ground
	/// when the cross product of v1 - v0 and v2 - v0 points up, within the slope limit; one with an index out
	/// of range or a coordinate that is not finite is rejected. Already dirty tiles are built too, and those
	/// no triangle reaches are removed. Returns the number of failed tiles, listed with their reasons in
	/// failures when given. On a mesh of several types use the overload whose failures name the type.
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
	/// Tiles of every agent type that are dirty, have a job in flight, or have border links still to judge.
	/// Owner thread.
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
	/// Whether this tile is dirty, has a job in flight or has border links still to judge. Owner thread.
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
	/// Hands out jobs for dirty tiles, oldest first per agent type, at most one in flight per tile, and
	/// returns how many. A tile marked again while its job is in flight gets its next job after that one is
	/// committed. With generated links, one job at a time per type also judges links across tile borders,
	/// which may be a job that builds no tile and needs no geometry. Several types share the span evenly,
	/// starting with another type each call.
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
	/// Publishes built jobs, pending tile removals and link changes, one new state per agent type that
	/// changed, and returns the number of failed jobs. A failed tile stays clean until marked dirty again.
	/// A job still Pending is abandoned: its tile stays dirty and a later result is ignored. A tile marked
	/// dirty while its job was in flight still gets that job's result and stays dirty; only RemoveTile
	/// discards it. Jobs of another mesh are left out.
	/// </summary>
	public int Commit( ReadOnlySpan<TileJob> jobs )
	{
		counted = false;
		uint before = 0, after = 0;
		foreach ( var type in types ) before += type.State.Version;
		Interlocked.Increment( ref publishing );
		int failed = 0;
		try
		{
			foreach ( var type in types ) failed += type.Commit( jobs );
		}
		finally
		{
			foreach ( var type in types ) after += type.State.Version;
			if ( after != before ) Volatile.Write( ref version, version + 1 );
			Interlocked.Increment( ref publishing );
		}
		return failed;
	}

	/// <summary>
	/// Publishes pending link and RemoveTile changes without jobs.
	/// </summary>
	public int Commit() => Commit( default );
}
