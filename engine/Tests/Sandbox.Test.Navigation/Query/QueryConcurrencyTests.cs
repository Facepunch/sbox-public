using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

[TestClass]
public class QueryConcurrencyTests
{
	const int Size = 5;

	// The middle column of tiles is cut by walls with one passage per tile. Layout 0 has the passage
	// at the bottom of each tile, layout 1 at the top, so a path found on one layout is wrong on the other.
	static Tile Build( int x, int y, int layout )
	{
		if ( x != 2 ) return GridMesh.Rects( x, y, [0, 32, 64], [0, 32, 64] );
		return GridMesh.Rects( x, y, [0, 64], [0, 8, 56, 64], ( i, j ) => j == (layout == 0 ? 0 : 2) );
	}

	/// <summary>
	/// Queries on several threads while the owner thread keeps removing, rebuilding and relinking tiles.
	/// Nothing may throw, and no path may go through a tile that was not there in the state it was
	/// found on: every commit replaces the middle column by the other layout.
	/// </summary>
	[TestMethod]
	public void QueriesStayConsistentWhileTilesAreCommitted()
	{
		var mesh = GridMesh.Create();
		var tiles = new List<Tile>();
		for ( int y = 0; y < Size; y++ )
			for ( int x = 0; x < Size; x++ )
				tiles.Add( Build( x, y, 0 ) );
		mesh.CommitTiles( tiles.ToArray() );
		var far = new Vector3( Size * 640 - 100, Size * 640 - 100, 0 );
		int link = mesh.AddLink( new NavLink { Start = new Vector3( 100, 100, 0 ), End = far, Radius = 16, Bidirectional = true, Area = 1 } );
		mesh.Commit();
		// From here on an even version is layout 0 and an odd one layout 1.
		Assert.AreEqual( 0u, mesh.Version & 1 );

		using var stop = new CancellationTokenSource();
		var failures = new List<string>();
		long queries = 0, paths = 0;
		var threads = new Thread[6];
		for ( int t = 0; t < threads.Length; t++ )
		{
			int seed = t;
			threads[t] = new Thread( () =>
			{
				try
				{
					var query = new NavQuery( mesh );
					var path = new NavPath();
					var random = new Random( seed );
					var corners = new Vector3[32];
					var visited = new PolyRef[16];
					var walls = new Vector3[16];
					uint randomSeed = (uint)seed + 1;
					while ( !stop.IsCancellationRequested )
					{
						Vector3 from = new( random.NextSingle() * 1200 + 20, random.NextSingle() * Size * 640, 0 );
						Vector3 to = new( Size * 640 - 20 - random.NextSingle() * 1200, random.NextSingle() * Size * 640, 0 );
						uint versionBefore = mesh.Version;
						var status = query.FindPath( from, to, GridMesh.Snap, default, path );
						uint versionAfter = mesh.Version;
						Interlocked.Increment( ref queries );
						if ( status >= PathStatus.Partial )
						{
							Interlocked.Increment( ref paths );
							string? problem = Check( path, versionBefore == versionAfter ? (int)(versionBefore & 1) : -1 );
							if ( problem != null ) lock ( failures ) failures.Add( problem );

							// The corridor may be stale by now. These must work or fail cleanly, never throw.
							query.FindCorners( path.Polys, from, to, corners );
							var poly = path.Polys[0];
							var position = path.Start;
							query.Move( ref poly, ref position, new Vector3( 30, 10, 0 ), default, visited, out _ );
							query.FindWalls( path.Polys[^1], path.End, 100, 50, default, walls );
							query.IsValid( path.Polys[path.Polys.Length / 2], default );
							var corridor = path.Polys.ToArray();
							int count = corridor.Length;
							query.Repair( mesh.State, path.Polys[0], path.Start, path.End, default, ref corridor, ref count, 32 );
						}
						query.FindRandomPoint( default, float.PositiveInfinity, default, ref randomSeed, out _, out _ );
						query.FindNearest( to, new Vector3( float.PositiveInfinity ), default, out _, out _ );
					}
				}
				catch ( Exception e )
				{
					lock ( failures ) failures.Add( e.ToString() );
				}
			} );
			threads[t].Start();
		}

		// The owner: flip the middle column, remove and restore tiles, move the link.
		var batch = new List<Tile>();
		int commits = 0;
		var watch = System.Diagnostics.Stopwatch.StartNew();
		while ( watch.ElapsedMilliseconds < 1500 )
		{
			int layout = (int)((mesh.Version + 1) & 1);
			batch.Clear();
			for ( int y = 0; y < Size; y++ ) batch.Add( Build( 2, y, layout ) );
			if ( commits % 3 == 0 ) mesh.RemoveTile( 0, commits % Size );
			if ( commits % 3 == 1 ) batch.Add( Build( 0, (commits - 1) % Size, 0 ) );
			if ( commits % 5 == 0 ) mesh.SetLink( link, new NavLink { Start = new Vector3( 100 + commits % 400, 100, 0 ), End = far, Radius = 16, Bidirectional = true, Area = 1 } );
			uint before = mesh.Version;
			mesh.CommitTiles( batch.ToArray() );
			Assert.AreEqual( before + 1, mesh.Version );
			commits++;
		}
		stop.Cancel();
		foreach ( var thread in threads ) thread.Join();

		Console.WriteLine( $"{commits} commits, {queries} queries, {paths} paths" );
		Assert.AreEqual( 0, failures.Count, failures.Count > 0 ? failures[0] : "" );
		Assert.IsTrue( commits > 20 && paths > 1000 );
	}

	// The low passage of a tile is its first 80 units, the high one its last 80. A tile boundary is the
	// edge of the high passage below it and of the low passage above it.
	static bool InPassage( float y, int layout )
	{
		float local = y % 640;
		bool low = local <= 81 || local >= 639, high = local >= 559 || local <= 1;
		return layout == 0 ? low : layout == 1 ? high : low || high;
	}

	// A corner inside the middle column must lie in a passage, and when the layout the path was found
	// on is known, in that layout's passage. -1 means a commit happened during the query.
	static string? Check( NavPath path, int layout )
	{
		foreach ( var corner in path.Corners )
		{
			if ( corner.X < 2 * 640 + 0.5f || corner.X > 3 * 640 - 0.5f ) continue;
			if ( !InPassage( corner.Y, -1 ) ) return $"A corner at {corner} is inside a wall of the middle column";
			if ( !InPassage( corner.Y, layout ) ) return $"A corner at {corner} uses the passage of the other layout, not layout {layout}";
		}

		// Walking the corners, the middle column is only ever crossed inside one passage.
		var corners = path.Corners;
		for ( int i = 1; i < corners.Length; i++ )
		{
			Vector3 a = corners[i - 1], b = corners[i];
			if ( MathF.Min( a.X, b.X ) > 3 * 640 || MathF.Max( a.X, b.X ) < 2 * 640 || MathF.Abs( a.X - b.X ) < 1 ) continue;
			// A link hop from one end of the mesh to the other.
			if ( Vector3.Distance( a, b ) > 2500 ) continue;
			for ( float x = MathF.Max( MathF.Min( a.X, b.X ), 2 * 640 + 1 ); x < MathF.Min( MathF.Max( a.X, b.X ), 3 * 640 - 1 ); x += 20 )
			{
				float y = a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
				if ( !InPassage( y, -1 ) ) return $"The path from {a} to {b} goes through a wall of the middle column";
				if ( !InPassage( y, layout ) ) return $"The path from {a} to {b} uses the passage of the other layout, not layout {layout}";
			}
		}
		return null;
	}
}
