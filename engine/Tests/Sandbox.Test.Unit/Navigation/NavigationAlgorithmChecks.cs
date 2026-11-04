using System;
using System.Collections.Generic;
using Sandbox.Navigation.Pathfinding;
using Sandbox.Navigation.Generation;

namespace NavigationTests;

static class NavigationAlgorithmChecks
{
	public static void BoundsAndSearch()
	{
		var mesh = SyntheticNavMesh.Create( new() { Obstacles = true } );
		var data = mesh.GetTile( 0 ).data;
		var tree = data.bvTree;
		var random = new Random( 917 );
		for ( int i = 0; i < 500; i++ )
		{
			var center = new Vector3( random.NextSingle() * 640, 1, random.NextSingle() * 640 );
			var size = new Vector3( random.NextSingle() * 80 + 1 );
			var candidates = new HashSet<int>();
			foreach ( int polygon in BoundingVolumeTree.Query( data, center - size, center + size ) ) candidates.Add( polygon );
			data.bvTree = null;
			foreach ( int polygon in BoundingVolumeTree.Query( data, center - size, center + size ) )
				Check( candidates.Contains( polygon ), "Bounds tree missed a polygon overlapping the query" );
			data.bvTree = tree;
		}
		var query = new MeshQuery( mesh );
		var filter = TraversalFilter.Unrestricted;
		query.FindNearestPoly( new Vector3( 50, 1, 320 ), new Vector3( 20 ), filter, out var start, out var from, out _ );
		query.FindNearestPoly( new Vector3( 590, 1, 320 ), new Vector3( 20 ), filter, out var end, out var to, out _ );
		var complete = new List<long>();
		Check( query.FindPath( start, end, from, to, filter, complete ).Succeeded(), "Synchronous search failed" );
		var status = query.BeginPathSearch( start, end, from, to, filter );
		int iterations = 0;
		while ( status.InProgress() && iterations++ < 10000 ) status = query.AdvancePathSearch( 1 );
		var incremental = new List<long>();
		Check( status.Succeeded() && query.FinishPathSearch( incremental ).Succeeded(), "Incremental search failed" );
		Check( complete.SequenceEqual( incremental ) && complete[^1] == end, "Incremental path differs from synchronous path" );
		Span<long> visited = stackalloc long[32];
		for ( int i = 0; i < 100; i++ )
		{
			Check( query.FindDistanceToWall( start, from, 100, filter, out var distance, out _, out _ ).Succeeded() && float.IsFinite( distance ), "Wall query failed after scratch reuse" );
			Check( query.MoveAlongSurface( start, from, from, filter, out var position, visited, out int count, visited.Length ).Succeeded()
				&& count > 0 && position.Distance( from ) < 0.01f, "Movement query failed after scratch reuse" );
		}
		long oldReference = mesh.GetTileRef( mesh.GetTile( 0 ) );
		mesh.RemoveTile( oldReference );
		Check( !mesh.IsValidPolyRef( start ), "Removed tile reference remained valid" );
		Check( mesh.AddTile( data, 0, 0, out long replacement ).Succeeded(), "Tile slot reuse failed" );
		Check( replacement != oldReference && mesh.GetMaxTiles() == 1 && !mesh.IsValidPolyRef( start ), "Reused tile accepted a stale reference" );
		Console.WriteLine( "PASS: bounds tree coverage (500 boxes), incremental search, stale tile references" );
	}

	public static void Run()
	{
		var random = new Random( 1729 );
		for ( int test = 0; test < 200; test++ )
		{
			using var field = new Heightfield( 8, 8, Vector3.Zero, new Vector3( 8, 4096, 8 ), 1, 1 );
			for ( int z = 0; z < 8; z++ )
				for ( int x = 0; x < 8; x++ )
				{
					int y = 0;
					for ( int layer = 0, count = random.Next( 80 ); layer < count; layer++ )
					{
						y += random.Next( 2, 32 );
						field.AddOrMergeSpan( x, z, (ushort)y, (ushort)(y + 1), random.Next( 4 ) == 0 ? 0 : Constants.WALKABLE_AREA, 0 );
					}
				}
			using var compact = field.BuildCompactHeightfield( random.Next( 1, 20 ), random.Next( 1, 80 ) );
			// Independent reference: original four-direction, first-match search.
			for ( int z = 0; z < 8; z++ )
				for ( int x = 0; x < 8; x++ )
				{
					var cell = compact.Cells[x + z * 8];
					for ( int i = cell.Index; i < cell.Index + cell.Count; i++ )
						for ( int direction = 0; direction < 4; direction++ )
						{
							var span = compact.Spans[i];
							int nx = x + Utils.GetDirOffsetX( direction ), nz = z + Utils.GetDirOffsetZ( direction );
							int expected = Constants.NOT_CONNECTED;
							if ( nx >= 0 && nx < 8 && nz >= 0 && nz < 8 )
							{
								var neighbor = compact.Cells[nx + nz * 8];
								for ( int k = 0; k < Math.Min( neighbor.Count, Constants.NOT_CONNECTED ); k++ )
								{
									var next = compact.Spans[neighbor.Index + k];
									if ( Math.Min( span.StartY + span.Height, next.StartY + next.Height ) - Math.Max( span.StartY, next.StartY ) >= compact.WalkableHeight && Math.Abs( span.StartY - next.StartY ) <= compact.WalkableClimb ) { expected = k; break; }
								}
							}
							Check( Utils.GetCon( span, direction ) == expected, "Span connectivity differs from reference" );
						}
				}
		}
		var queue = new SearchQueue();
		var nodes = Enumerable.Range( 0, 256 ).Select( i => new SearchNode( i ) { total = random.NextSingle() } ).ToArray();
		foreach ( var node in nodes ) queue.Push( node );
		for ( int i = 0; i < 10000; i++ )
		{
			var node = nodes[random.Next( nodes.Length )];
			node.total = random.NextSingle();
			queue.Modify( node );
			Check( queue.Peek().total == nodes.Min( n => n.total ), "Priority update" );
		}
		float previous = float.NegativeInfinity;
		while ( !queue.IsEmpty() ) { float next = queue.Pop().total; Check( next >= previous, "Heap ordering" ); previous = next; }
		Console.WriteLine( "PASS: randomized span parity (200 fields), heap increases/decreases (10000 updates)" );
	}

	internal static void Check( bool condition, string message )
	{
		if ( !condition ) throw new Exception( message );
	}
}
