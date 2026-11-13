using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// Query tests on hand-made tiles: nearest points, paths, corners, surface moves, walls, random points.
/// </summary>
[TestClass]
public class QueryTests
{
	static readonly Vector3 Snap = GridMesh.Snap;

	sealed record Layout( bool UpperFloor = false, bool Obstacles = false, bool Doorway = false, bool Corner = false, bool AreaStrip = false, int MinY = 0 );

	static Tile Synthetic( Layout layout, int tileX = 0 )
	{
		bool Walkable( int x, int y )
		{
			if ( y < layout.MinY ) return false;
			if ( layout.Obstacles && x % 12 == 6 && y > 8 && y < 56 ) return false;
			if ( layout.Corner && x >= 28 && y < 36 ) return false;
			if ( layout.Doorway && x >= 30 && x < 34 && (y < 28 || y >= 36) ) return false;
			return true;
		}
		var ground = new GridMesh.Layer( 0, Walkable, ( x, y ) => (byte)(layout.AreaStrip && x >= 30 && x < 34 ? 2 : 1) );
		return layout.UpperFloor ? GridMesh.Tile( tileX, 0, ground, new GridMesh.Layer( 200, Walkable ) ) : GridMesh.Tile( tileX, 0, ground );
	}

	static NavMesh Mesh( Layout layout ) => GridMesh.Mesh( Synthetic( layout ) );

	[TestMethod]
	public void NearestMatchesABruteForceOverEveryPolygon()
	{
		var mesh = GridMesh.Mesh( Synthetic( new Layout( UpperFloor: true, Obstacles: true ) ), Synthetic( new Layout( Obstacles: true, MinY: 20 ), 1 ) );
		var query = new NavQuery( mesh );
		var random = new Random( 917 );
		int found = 0;
		for ( int i = 0; i < 2000; i++ )
		{
			var point = new Vector3( random.NextSingle() * 1400 - 60, random.NextSingle() * 760 - 60, random.NextSingle() * 320 - 60 );
			var half = i % 5 == 0 ? new Vector3( float.PositiveInfinity ) : new Vector3( random.NextSingle() * 80 + 1, random.NextSingle() * 80 + 1, random.NextSingle() * 120 + 1 );
			float expected = BruteForce( mesh, point, half );
			bool hit = query.FindNearest( point, half, default, out var poly, out var nearest );
			Assert.AreEqual( expected < float.MaxValue, hit, $"{point} within {half}" );
			if ( !hit ) continue;
			found++;
			Assert.IsTrue( query.IsValid( poly, default ) );
			Assert.AreEqual( expected, Metric( point, nearest ), 0.01f + expected * 1e-4f, $"{point} within {half}: got {nearest}" );
		}
		Assert.IsTrue( found > 1000 );
	}

	// The nearest metric: directly above or below within the climb counts as touching.
	static float Metric( Vector3 point, Vector3 nearest )
	{
		if ( nearest.X == point.X && nearest.Y == point.Y )
		{
			float gap = MathF.Abs( point.Z - nearest.Z ) - GridMesh.Settings.AgentClimb;
			return gap > 0 ? gap * gap : 0;
		}
		return Vector3.DistanceSquared( point, nearest );
	}

	static float BruteForce( NavMesh mesh, Vector3 point, Vector3 half )
	{
		float best = float.MaxValue;
		foreach ( var page in mesh.State.Pages )
		{
			foreach ( var tile in page! )
			{
				if ( tile == null ) continue;
				foreach ( ref readonly var poly in tile.Polys.AsSpan() )
				{
					// Every polygon here is an axis-aligned square.
					Vector3 min = tile.Verts[poly.Verts[0]], max = tile.Verts[poly.Verts[2]];
					if ( point.X - half.X > max.X || point.X + half.X < min.X || point.Y - half.Y > max.Y || point.Y + half.Y < min.Y || point.Z - half.Z > max.Z || point.Z + half.Z < min.Z ) continue;
					bool over = point.X > min.X && point.X < max.X && point.Y > min.Y && point.Y < max.Y;
					var closest = over ? new Vector3( point.X, point.Y, min.Z ) : Vector3.Clamp( point, min, max );
					best = MathF.Min( best, Metric( point, closest ) );
				}
			}
		}
		return best;
	}

	[TestMethod]
	public void NearestPrefersThePolygonUnderThePointWithinTheClimb()
	{
		// A ledge at height 6 next to the ground: standing just off the ledge, 7 above the ground, the
		// ledge is nearer in a straight line but the ground is under the point and within the climb.
		var mesh = GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, ( x, y ) => x >= 32 ), new GridMesh.Layer( 6, ( x, y ) => x < 32 ) ) );
		var query = new NavQuery( mesh );
		Assert.IsTrue( query.FindNearest( new Vector3( 321, 325, 7 ), Snap, default, out _, out var nearest ) );
		Assert.AreEqual( new Vector3( 321, 325, 0 ), nearest );

		// Beyond the climb the vertical gap counts, less the climb: a ledge at 20 is then nearer.
		mesh = GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, ( x, y ) => x >= 32 ), new GridMesh.Layer( 20, ( x, y ) => x < 32 ) ) );
		Assert.IsTrue( new NavQuery( mesh ).FindNearest( new Vector3( 321, 325, 25 ), Snap, default, out _, out nearest ) );
		Assert.AreEqual( new Vector3( 320, 325, 20 ), nearest );
	}

	[TestMethod]
	public void NearestHonoursBoxFilterAndEmptyMeshes()
	{
		var empty = new NavQuery( GridMesh.Create() );
		Assert.IsFalse( empty.FindNearest( Vector3.Zero, new Vector3( float.PositiveInfinity ), default, out var none, out _ ) );
		Assert.IsTrue( none.IsNone );

		var mesh = GridMesh.Mesh( GridMesh.Quad( 0, 0, area: 3 ), GridMesh.Quad( 40, -7 ) );
		var query = new NavQuery( mesh );
		var far = new Vector3( 30000, -3000, 500 );
		Assert.IsFalse( query.FindNearest( far, new Vector3( 500 ), default, out _, out _ ) );
		Assert.IsTrue( query.FindNearest( far, new Vector3( float.PositiveInfinity ), default, out var poly, out var nearest ) );
		Assert.AreEqual( new Vector3( 40 * 640 + 640, -6 * 640, 0 ), nearest, "The corner of the far tile is nearest" );

		Assert.IsTrue( query.FindNearest( new Vector3( 100, 100, 0 ), Snap, default, out poly, out _ ) );
		Assert.IsFalse( query.FindNearest( new Vector3( 100, 100, 0 ), Snap, new NavFilter( 1ul << 3 ), out _, out _ ) );
		Assert.IsTrue( query.IsValid( poly, default ) );
		Assert.IsFalse( query.IsValid( poly, new NavFilter( 1ul << 3 ) ) );
		Assert.IsFalse( query.FindNearest( new Vector3( float.NaN, 0, 0 ), Snap, default, out _, out _ ) );
		Assert.IsFalse( query.FindNearest( Vector3.Zero, new Vector3( float.NaN ), default, out _, out _ ) );
	}

	[TestMethod]
	public void SearchWallAndMoveQueriesShareScratchWithoutDisturbingEachOther()
	{
		// Port of the search part of BoundsSearchAndTileReuse.
		var mesh = Mesh( new Layout( Obstacles: true ) );
		var query = new NavQuery( mesh );
		Vector3 from = new( 50, 320, 0 ), to = new( 590, 320, 0 );
		var start = GridMesh.Nearest( query, 50, 320 );
		var end = GridMesh.Nearest( query, 590, 320 );
		var first = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( start, from, end, to, default, first ) );
		Assert.AreEqual( end, first.Polys[^1] );

		Span<PolyRef> visited = stackalloc PolyRef[32];
		for ( int i = 0; i < 100; i++ )
		{
			Assert.IsTrue( query.FindNearestWall( start, from, 100, default, out var wall ), "Wall query failed after scratch reuse" );
			Assert.AreEqual( 10f, Vector3.Distance( from, wall ), 0.001f, "The obstacle column at x = 60 is the nearest wall" );
			var poly = start;
			var position = from;
			Assert.IsTrue( query.Move( ref poly, ref position, Vector3.Zero, default, visited, out int count ) && count > 0, "Movement query failed after scratch reuse" );
			Assert.IsTrue( Vector3.Distance( position, from ) < 0.01f );
			Assert.AreEqual( start, poly );
		}

		var again = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( start, from, end, to, default, again ) );
		Assert.IsTrue( first.Polys.SequenceEqual( again.Polys ) );
		Assert.IsTrue( first.Corners.SequenceEqual( again.Corners ) );
	}

	[TestMethod]
	public void ResultsDoNotDependOnScratchHistory()
	{
		var mesh = GridMesh.Mesh( Synthetic( new Layout( Obstacles: true ) ), Synthetic( new Layout( Obstacles: true ), 1 ) );
		var used = new NavQuery( mesh );
		var random = new Random( 11 );
		var path = new NavPath();
		var fresh = new NavPath();
		for ( int i = 0; i < 200; i++ )
		{
			Vector3 from = new( random.NextSingle() * 1280, random.NextSingle() * 640, 0 ), to = new( random.NextSingle() * 1280, random.NextSingle() * 640, 0 );
			var status = used.FindPath( from, to, Snap, default, path );
			// A query object that has never searched before, with scratch at its initial size.
			Assert.AreEqual( status, new NavQuery( mesh ).FindPath( from, to, Snap, default, fresh ) );
			Assert.IsTrue( path.Polys.SequenceEqual( fresh.Polys ), "The corridor depends on what the query did before" );
			Assert.IsTrue( path.Corners.SequenceEqual( fresh.Corners ) );
			Assert.AreEqual( path.Length, fresh.Length );
		}
	}

	[TestMethod]
	public void UnreachableTargetDoesNotSearchTheWholeIsland_11896()
	{
		var mesh = Mesh( new Layout( UpperFloor: true, Obstacles: true ) );
		var query = new NavQuery( mesh );
		var start = GridMesh.Nearest( query, 20, 320 );
		var end = GridMesh.Nearest( query, 100, 320, 201 );
		var islands = NavQuery.GetIslands( mesh.State, default );
		Assert.AreNotEqual( islands.Of( start.Id ), islands.Of( end.Id ) );

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Partial, query.FindPath( start, new Vector3( 20, 320, 0 ), end, new Vector3( 100, 320, 200 ), default, path ) );
		Assert.IsTrue( new Vector2( path.End.X - 100, path.End.Y - 320 ).Length() < 1, $"Partial route ended at {path.End}, not below the target" );
		Assert.AreEqual( 0f, path.End.Z );
		Assert.AreEqual( path.End, path.Corners[^1] );
		int polygons = mesh.GetStats().Polygons;
		Assert.IsTrue( query.SearchExpansions < polygons / 4, $"Unreachable search expanded {query.SearchExpansions} of {polygons} polygons" );
	}

	[TestMethod]
	public void TargetBehindABlockedAreaDoesNotSearchTheWholeIsland_11896()
	{
		var mesh = Mesh( new Layout( AreaStrip: true, Obstacles: true ) );
		var query = new NavQuery( mesh );
		var filter = new NavFilter( 1ul << 2 );
		var start = GridMesh.Nearest( query, 260, 320, 1, filter );
		var end = GridMesh.Nearest( query, 600, 320, 1, filter );
		Assert.AreEqual( NavQuery.GetIslands( mesh.State, default ).Of( start.Id ), NavQuery.GetIslands( mesh.State, default ).Of( end.Id ) );
		Assert.AreNotEqual( NavQuery.GetIslands( mesh.State, filter ).Of( start.Id ), NavQuery.GetIslands( mesh.State, filter ).Of( end.Id ) );

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Partial, query.FindPath( start, new Vector3( 260, 320, 0 ), end, new Vector3( 600, 320, 0 ), filter, path ) );
		Assert.IsTrue( path.End.X > 280 && path.End.X <= 300, $"Partial route ended at {path.End}, not at the blocked strip" );
		int polygons = mesh.GetStats().Polygons;
		Assert.IsTrue( query.SearchExpansions < polygons / 8, $"Blocked search expanded {query.SearchExpansions} of {polygons} polygons" );

		// Without the filter the strip is just ground.
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 260, 320, 0 ), new Vector3( 600, 320, 0 ), Snap, default, path ) );
	}

	[TestMethod]
	public void AreaCostsSteerThePathAndIslandMapsAreKeptPerMask()
	{
		// A costly strip with a free gap at the top (the strip stops at y = 500).
		var mesh = GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, null, ( x, y ) => (byte)(x >= 30 && x < 34 && y < 50 ? 2 : 1) ) ) );
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 from = new( 205, 405, 0 ), to = new( 435, 405, 0 );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( from, to, Snap, default, path ) );
		Assert.AreEqual( 230f, path.Length, 0.01f );

		var costs = new float[64];
		Array.Fill( costs, 1f );
		costs[2] = 20;
		Assert.AreEqual( PathStatus.Complete, query.FindPath( from, to, Snap, new NavFilter( 0, costs ), path ) );
		Assert.IsTrue( path.Length > 300, "The path should go around the costly strip" );
		foreach ( var corner in path.Corners ) Assert.IsFalse( corner.X > 301 && corner.X < 339 && corner.Y < 499, $"A corner at {corner} is inside the strip" );

		Assert.AreEqual( PathStatus.Complete, query.FindPath( from, to, Snap, new NavFilter( 1ul << 2 ), path ) );
		Assert.IsTrue( path.Length > 300 );
		// Asked for, there is one island map per area mask.
		var open = NavQuery.GetIslands( mesh.State, default );
		var blocked = NavQuery.GetIslands( mesh.State, new NavFilter( 1ul << 2 ) );
		Assert.AreNotSame( open, blocked );
		Assert.AreSame( open, NavQuery.GetIslands( mesh.State, default ) );
		int maps = 0;
		for ( var map = mesh.State.Islands; map != null; map = map.Next ) maps++;
		Assert.AreEqual( 2, maps, "One island map per area mask" );
	}

	[TestMethod]
	public void IslandsAreOnlyBuiltWhenASearchDoesNotReachItsGoalSoon()
	{
		var mesh = GridMesh.Mesh( Synthetic( new Layout( UpperFloor: true, Obstacles: true ) ), Synthetic( new Layout( Obstacles: true ), 1 ) );
		var query = new NavQuery( mesh );
		var path = new NavPath();

		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 25, 325, 0 ), new Vector3( 45, 345, 0 ), Snap, default, path ) );
		Assert.IsNull( mesh.State.Islands, "A local query built an island map" );

		// A long way round the obstacle columns takes more than the first phase allows.
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 25, 325, 0 ), new Vector3( 1255, 325, 0 ), Snap, default, path ) );
		Assert.IsTrue( query.SearchExpansions > 256 );
		Assert.IsNotNull( mesh.State.Islands );

		// A commit drops the map with the state. The per-tile components of untouched tiles stay.
		var untouched = mesh.State.Find( 1, 0 )!.Islands( 0 );
		mesh.CommitTiles( [Synthetic( new Layout( UpperFloor: true, Obstacles: true ) )] );
		Assert.IsNull( mesh.State.Islands );
		Assert.AreNotSame( mesh.State.Find( 1, 0 ), null );
		Assert.AreSame( untouched, mesh.State.Find( 1, 0 )!.Islands( 0 ), "A relinked tile keeps the components of its build" );

		// An unreachable goal on the upper floor: found out after the first phase, then retargeted.
		Assert.AreEqual( PathStatus.Partial, query.FindPath( new Vector3( 25, 325, 0 ), new Vector3( 605, 325, 200 ), Snap, default, path ) );
		Assert.IsNotNull( mesh.State.Islands );

		// A bounded repair never asks.
		mesh.CommitTiles( [Synthetic( new Layout( UpperFloor: true, Obstacles: true ) )] );
		query.FindPath( new Vector3( 25, 325, 0 ), new Vector3( 45, 455, 0 ), Snap, default, path );
		var corridor = path.Polys.ToArray();
		int count = corridor.Length;
		query.Repair( mesh.State, GridMesh.Nearest( query, 35, 365 ), new Vector3( 35, 365, 0 ), path.End, default, ref corridor, ref count, 64 );
		Assert.IsNull( mesh.State.Islands );
	}

	[TestMethod]
	[DataRow( 0ul )]
	[DataRow( 1ul << 2 )]
	public void TwoLevelIslandsMatchAFloodOverEveryPolygon( ulong forbidden )
	{
		// Several tiles with walls, a strip of another area, an upper floor, a missing tile and links.
		var mesh = GridMesh.Mesh(
			Synthetic( new Layout( UpperFloor: true, Obstacles: true ) ),
			Synthetic( new Layout( Obstacles: true, AreaStrip: true ), 1 ),
			Synthetic( new Layout( Doorway: true, MinY: 20 ), 2 ),
			GridMesh.Tile( 0, 1, new GridMesh.Layer( 0, ( x, y ) => (x + y) % 9 != 0 && x != 40 ) ),
			GridMesh.Tile( 2, 1, new GridMesh.Layer( 0, ( x, y ) => y > 30 ), new GridMesh.Layer( 90, null, ( x, y ) => (byte)(x < 5 ? 2 : 1) ) ),
			GridMesh.Tile( 5, 5, new GridMesh.Layer( 0 ) ) );
		mesh.AddLink( new NavLink { Start = new Vector3( 105, 105, 0 ), End = new Vector3( 105, 105, 200 ), Radius = 16, Bidirectional = false, Area = 1 } );
		mesh.AddLink( new NavLink { Start = new Vector3( 1900, 300, 0 ), End = new Vector3( 5 * 640 + 100, 5 * 640 + 100, 0 ), Radius = 16, Bidirectional = true, Area = 2 } );
		mesh.Commit();
		var s = mesh.State;
		var filter = new NavFilter( forbidden );
		var islands = NavQuery.GetIslands( s, filter );

		// The reference: one union-find over all polygons, as the first version did it.
		var ids = new List<uint>();
		var at = new Dictionary<uint, int>();
		foreach ( var page in s.Pages )
			foreach ( var tile in page! )
				for ( int p = 0; tile != null && p < tile.Polys.Length; p++ )
				{
					uint id = (uint)tile.Slot << PolyRef.PolyBits | (uint)p;
					at[id] = ids.Count;
					ids.Add( id );
				}
		var parent = Enumerable.Range( 0, ids.Count ).ToArray();
		int Root( int i )
		{
			while ( parent[i] != i ) i = parent[i] = parent[parent[i]];
			return i;
		}
		bool Allowed( uint id ) => filter.Allows( s.Get( id ).Polys[id & PolyRef.PolyMask].Area );
		void Join( uint a, uint b )
		{
			if ( Allowed( a ) && Allowed( b ) ) parent[Root( at[a] )] = Root( at[b] );
		}
		foreach ( uint id in ids )
		{
			var tile = s.Get( id );
			ref readonly var poly = ref tile.Polys[id & PolyRef.PolyMask];
			for ( int j = 0; j < poly.VertCount; j++ )
				if ( poly.Neis[j] < Poly.Border ) Join( id, id & ~PolyRef.PolyMask | poly.Neis[j] );
			foreach ( var link in tile.Ext )
				if ( link.Poly == (id & PolyRef.PolyMask) ) Join( id, link.Target );
		}
		foreach ( var link in s.Links )
			if ( link.Connected && filter.Allows( link.Def.Area ) ) Join( link.StartPoly, link.EndPoly );

		var labelOfRoot = new Dictionary<int, int>();
		var rootOfLabel = new Dictionary<int, int>();
		int allowed = 0;
		foreach ( uint id in ids )
		{
			int label = islands.Of( id );
			if ( !Allowed( id ) )
			{
				Assert.AreEqual( -1, label );
				continue;
			}
			allowed++;
			int root = Root( at[id] );
			if ( labelOfRoot.TryGetValue( root, out int known ) ) Assert.AreEqual( known, label, "Two polygons of one island have different labels" );
			else labelOfRoot[root] = label;
			if ( rootOfLabel.TryGetValue( label, out int knownRoot ) ) Assert.AreEqual( knownRoot, root, "Two islands share a label" );
			else rootOfLabel[label] = root;
		}
		Assert.IsTrue( labelOfRoot.Count > 5 && allowed > 10000, $"{labelOfRoot.Count} islands over {allowed} polygons" );
	}

	[TestMethod]
	[DataRow( "straight", false )]
	[DataRow( "doorway", false )]
	[DataRow( "doorway", true )]
	[DataRow( "corner", false )]
	[DataRow( "corner", true )]
	public void RouteCornersHugTheGeometry( string layout, bool reverse )
	{
		// Routes round corners, on a few large rectangles like a generated mesh.
		var mesh = GridMesh.Mesh( layout switch
		{
			"doorway" => GridMesh.Rects( 0, 0, [0, 30, 34, 64], [0, 28, 36, 64], ( i, j ) => i != 1 || j == 1 ),
			"corner" => GridMesh.Rects( 0, 0, [0, 28, 64], [0, 36, 64], ( i, j ) => i == 0 || j == 1 ),
			_ => GridMesh.Rects( 0, 0, [0, 20, 64], [0, 64] )
		} );
		var query = new NavQuery( mesh );
		Vector3 start = layout == "corner" ? new Vector3( 140, 100, 0 ) : new Vector3( 100, 100, 0 );
		Vector3 target = layout == "corner" ? new Vector3( 540, 500, 0 ) : new Vector3( 540, 100, 0 );
		if ( reverse ) (start, target) = (target, start);

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( start, target, new Vector3( 8 ), default, path ) );
		Assert.AreEqual( start, path.Corners[0] );
		Assert.AreEqual( target, path.Corners[^1] );

		Vector3[] expected = layout switch
		{
			"straight" => [start, target],
			// Through the door between y = 280 and 360, around its near posts.
			"doorway" => [new( 100, 100, 0 ), new( 300, 280, 0 ), new( 340, 280, 0 ), new( 540, 100, 0 )],
			// Around the inner corner of the blocked block at x = 280, y = 360.
			_ => [new( 140, 100, 0 ), new( 280, 360, 0 ), new( 540, 500, 0 )]
		};
		if ( reverse && layout != "straight" ) Array.Reverse( expected );
		CollectionAssert.AreEqual( expected, path.Corners.ToArray(), string.Join( " ", path.Corners.ToArray() ) );

		float length = 0;
		for ( int i = 1; i < expected.Length; i++ ) length += Vector3.Distance( expected[i - 1], expected[i] );
		Assert.AreEqual( length, path.Length, 0.001f );

		// The same corridor pulled a few corners at a time gives the same first corners.
		Span<Vector3> some = stackalloc Vector3[2];
		Assert.AreEqual( Math.Min( 2, expected.Length ), query.FindCorners( path.Polys, start, target, some ) );
		Assert.AreEqual( expected[1], some[1] );
	}

	[TestMethod]
	public void CornersStopAtTheFirstLinkAndThePathHasBothEnds()
	{
		var mesh = Mesh( new Layout( UpperFloor: true ) );
		mesh.AddLink( new NavLink { Start = new Vector3( 320, 325, 1 ), End = new Vector3( 320, 325, 201 ), Radius = 24, Bidirectional = true, Area = 1 } );
		mesh.Commit();
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 from = new( 105, 105, 0 ), to = new( 505, 505, 200 );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( from, to, Snap, default, path ) );
		CollectionAssert.AreEqual( new[] { from, new Vector3( 320, 325, 0 ), new Vector3( 320, 325, 200 ), to }, path.Corners.ToArray() );

		Span<Vector3> corners = stackalloc Vector3[8];
		int count = query.FindCorners( path.Polys, from, to, corners );
		Assert.AreEqual( 2, count, "A mover gets the corners up to the link entry" );
		Assert.AreEqual( new Vector3( 320, 325, 0 ), corners[1] );

		// After the link the rest of the corridor is pulled from the exit.
		int link = 0;
		while ( !path.Polys[link].IsLink ) link++;
		Assert.IsTrue( query.GetLink( path.Polys[link], corners[1], out var entry, out var exit ) );
		Assert.AreEqual( corners[1], entry );
		count = query.FindCorners( path.Polys[(link + 1)..], exit, to, corners );
		Assert.AreEqual( 2, count );
		Assert.AreEqual( exit, corners[0] );
		Assert.AreEqual( to, corners[1] );

		// The internal form also says what each corner enters.
		Span<PolyRef> entered = stackalloc PolyRef[8];
		count = query.Funnel( mesh.State, path.Polys, from, to, corners, entered, false );
		Assert.AreEqual( 4, count );
		Assert.IsTrue( entered[1].IsLink && !entered[2].IsLink && entered[3].IsNone );
	}

	[TestMethod]
	public void HeightIsThePolygonPlane()
	{
		var mesh = GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, Height: ( x, y ) => x * 0.5f ) ) );
		var query = new NavQuery( mesh );
		Assert.IsTrue( query.FindNearest( new Vector3( 103, 205, 60 ), Snap, default, out var poly, out var nearest ) );
		Assert.AreEqual( 51.5f, nearest.Z, 0.001f );
		Assert.IsTrue( query.GetHeight( poly, new Vector3( 107, 203, -500 ), out float z ) );
		Assert.AreEqual( 53.5f, z, 0.001f );
		Assert.IsFalse( query.GetHeight( poly, new Vector3( 150, 203, 0 ), out _ ), "Not over the polygon" );
		Assert.IsFalse( query.GetHeight( default, new Vector3( 107, 203, 0 ), out _ ) );
	}

	[TestMethod]
	public void ClosestPointIsOnThePlaneOrOnTheNearestEdge()
	{
		var mesh = GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, Height: ( x, y ) => x * 0.5f ) ) );
		var query = new NavQuery( mesh );
		Assert.IsTrue( query.FindNearest( new Vector3( 103, 205, 60 ), Snap, default, out var poly, out _ ) );

		// Over the polygon: straight down or up to its plane, however far
		Assert.IsTrue( query.ClosestPoint( poly, new Vector3( 107, 203, -500 ), out var closest ) );
		Assert.AreEqual( new Vector3( 107, 203, 53.5f ), closest );
		Assert.IsTrue( query.GetHeight( poly, new Vector3( 107, 203, -500 ), out float z ) );
		Assert.AreEqual( z, closest.Z );

		// Beside it: what a nearest query that ends on this polygon answers
		var beside = new Vector3( -30, 203, 3 );
		Assert.IsTrue( query.FindNearest( beside, new Vector3( 64 ), default, out var edgePoly, out var nearest ) );
		Assert.IsFalse( query.GetHeight( edgePoly, beside, out _ ) );
		Assert.IsTrue( query.ClosestPoint( edgePoly, beside, out closest ) );
		Assert.AreEqual( nearest, closest );
		Assert.AreEqual( 203, closest.Y, 0.001f );
		Assert.IsTrue( closest.X is >= 0 and < 20, $"The mesh starts at {closest.X}" );

		Assert.IsFalse( query.ClosestPoint( default, beside, out closest ) );
		Assert.AreEqual( beside, closest, "A failed call hands the point back" );
		Assert.IsFalse( query.ClosestPoint( poly, new Vector3( float.NaN, 0, 0 ), out _ ) );

		// A polygon of a tile that is gone is stale
		mesh.RemoveTile( 0, 0 );
		mesh.Commit();
		Assert.IsFalse( query.ClosestPoint( poly, new Vector3( 107, 203, 0 ), out _ ) );
	}

	[TestMethod]
	public void MoveSlidesAlongWallsAndCrossesTiles()
	{
		var mesh = GridMesh.Mesh( Synthetic( new Layout( Doorway: true ) ), GridMesh.Flat( 1, 0, 4 ) );
		var query = new NavQuery( mesh );
		Span<PolyRef> visited = stackalloc PolyRef[32];

		// Into the wall beside the door: stops at the wall, slid along it.
		var poly = GridMesh.Nearest( query, 285, 205 );
		var position = new Vector3( 285, 205, 0 );
		Assert.IsTrue( query.Move( ref poly, ref position, new Vector3( 60, 30, 0 ), default, visited, out int count ) );
		Assert.AreEqual( new Vector3( 300, 235, 0 ), position );
		Assert.IsTrue( count >= 2 );
		Assert.AreEqual( poly, visited[count - 1] );
		Assert.AreEqual( poly, GridMesh.Nearest( query, 299, 235 ) );

		// Through the door.
		poly = GridMesh.Nearest( query, 285, 305 );
		position = new Vector3( 285, 305, 0 );
		Assert.IsTrue( query.Move( ref poly, ref position, new Vector3( 80, 10, 0 ), default, visited, out count ) );
		Assert.AreEqual( new Vector3( 365, 315, 0 ), position );
		Assert.AreEqual( 10, count );

		// Across the tile border, up the 4 unit step: the height follows the polygon.
		poly = GridMesh.Nearest( query, 625, 105 );
		position = new Vector3( 625, 105, 0 );
		Assert.IsTrue( query.Move( ref poly, ref position, new Vector3( 30, 0, 0 ), default, visited, out count ) );
		Assert.AreEqual( new Vector3( 655, 105, 4 ), position );
		Assert.AreEqual( mesh.State.Find( 1, 0 )!.Slot, (int)(poly.Id >> PolyRef.PolyBits) );

		// Off the edge of the mesh: clamped to the edge.
		position = new Vector3( 1275, 105, 4 );
		poly = GridMesh.Nearest( query, 1275, 105, 4 );
		Assert.IsTrue( query.Move( ref poly, ref position, new Vector3( 100, 0, 0 ), default, visited, out _ ) );
		Assert.AreEqual( new Vector3( 1280, 105, 4 ), position );

		// A small visited buffer keeps the first polygons.
		poly = GridMesh.Nearest( query, 285, 305 );
		position = new Vector3( 285, 305, 0 );
		Assert.IsTrue( query.Move( ref poly, ref position, new Vector3( 80, 10, 0 ), default, visited[..3], out count ) );
		Assert.AreEqual( 3, count );
		Assert.AreEqual( GridMesh.Nearest( query, 285, 305 ), visited[0] );

		var stale = default( PolyRef );
		Assert.IsFalse( query.Move( ref stale, ref position, Vector3.UnitX, default, visited, out _ ) );
	}

	[TestMethod]
	public void RaycastStopsAtWallsAndFollowsTheSurface()
	{
		var mesh = GridMesh.Mesh( Synthetic( new Layout( Doorway: true ) ), GridMesh.Flat( 1, 0 ) );
		var query = new NavQuery( mesh );
		var start = GridMesh.Nearest( query, 105, 105 );

		Assert.IsTrue( query.Raycast( mesh.State, start, new Vector3( 105, 105, 0 ), new Vector3( 505, 105, 0 ), default, out float t, out var last ) );
		Assert.AreEqual( 195f / 400f, t, 0.001f, "The wall beside the door is at x = 300" );
		Assert.AreEqual( last, GridMesh.Nearest( query, 295, 105 ) );

		start = GridMesh.Nearest( query, 105, 305 );
		Assert.IsTrue( query.Raycast( mesh.State, start, new Vector3( 105, 305, 0 ), new Vector3( 905, 335, 0 ), default, out t, out last ) );
		Assert.AreEqual( 1f, t, "Through the door and across the tile border nothing is in the way" );
		Assert.AreEqual( last, GridMesh.Nearest( query, 905, 335 ) );
		Assert.IsFalse( query.Raycast( mesh.State, default, Vector3.Zero, Vector3.One, default, out _, out _ ) );
	}

	[TestMethod]
	public void WallQueryKeepsPartialTilePortalsOpen()
	{
		// The east tile only exists from y = 320 up, so the lower half of the shared border is a wall.
		var mesh = GridMesh.Mesh( GridMesh.Flat( 0, 0 ), GridMesh.Tile( 1, 0, new GridMesh.Layer( 0, ( x, y ) => y >= 32, ( x, y ) => 2 ) ) );
		var query = new NavQuery( mesh );
		Span<Vector3> walls = stackalloc Vector3[32];
		foreach ( float y in new[] { 280f, 400f } )
		{
			var position = new Vector3( 630, y, 1 );
			var poly = GridMesh.Nearest( query, 630, y );
			int count = query.FindWalls( poly, position, 64, 32, default, walls );
			bool borderWall = false;
			for ( int i = 0; i < count; i++ )
			{
				if ( MathF.Abs( walls[i * 2].X - 640 ) > 0.01f || MathF.Abs( walls[i * 2 + 1].X - 640 ) > 0.01f ) continue;
				borderWall = true;
				Assert.IsTrue( MathF.Max( walls[i * 2].Y, walls[i * 2 + 1].Y ) <= 321, "The open part of a tile portal must not be treated as a wall" );
			}
			Assert.AreEqual( y < 320, borderWall );
		}

		// With the east tile's area forbidden the whole border is a wall again.
		var filter = new NavFilter( 1ul << 2 );
		var blocked = new Vector3( 630, 400, 1 );
		var start = GridMesh.Nearest( query, 630, 400, 1, filter );
		Assert.IsFalse( start.IsNone, "The starting tile must remain traversable" );
		int blockedCount = query.FindWalls( start, blocked, 64, 32, filter, walls );
		Assert.IsTrue( blockedCount > 0, "A portal into an excluded area must become a steering boundary" );
		Assert.AreEqual( 640f, walls[0].X, 0.01f );
		Assert.AreEqual( 640f, walls[1].X, 0.01f );

		Assert.IsTrue( query.FindNearestWall( start, blocked, 64, filter, out var wall ) );
		Assert.AreEqual( new Vector3( 640, 400, 0 ), wall );
		Assert.IsFalse( query.FindNearestWall( start, blocked, 64, default, out _ ), "Nothing but the open portal is in range" );
		Assert.IsTrue( query.FindNearestWall( start, blocked, 500, default, out wall ) );
		Assert.AreEqual( new Vector3( 640, 320, 0 ), wall, "The nearest wall is where the closed part of the border starts" );
	}

	[TestMethod]
	public void WallsComeNearestFirstAndRespectTheHeightBand()
	{
		var mesh = Mesh( new Layout( Doorway: true, UpperFloor: true ) );
		var query = new NavQuery( mesh );
		var position = new Vector3( 285, 325, 0 );
		var poly = GridMesh.Nearest( query, 285, 325 );
		Span<Vector3> walls = stackalloc Vector3[12];
		int count = query.FindWalls( poly, position, 80, 32, default, walls );
		Assert.IsTrue( count >= 2 );
		float previous = 0;
		for ( int i = 0; i < count; i++ )
		{
			float distance = Geo.DistanceToSegmentSquared( position, walls[i * 2], walls[i * 2 + 1], out _ );
			Assert.IsTrue( distance >= previous && distance <= 80 * 80 );
			Assert.AreEqual( 0f, walls[i * 2].Z, "Walls of the upper floor are outside the height band" );
			previous = distance;
		}
		Assert.AreEqual( 0, query.FindWalls( poly, position, 80, 32, default, default ) );
		Assert.AreEqual( 0, query.FindWalls( default, position, 80, 32, default, walls ) );
	}

	[TestMethod]
	public void RandomPointsAreUniformByArea()
	{
		// Two tiles: a full one and one with a quarter of its cells.
		var mesh = GridMesh.Mesh( GridMesh.Flat( 0, 0 ), GridMesh.Tile( 3, 0, new GridMesh.Layer( 50, ( x, y ) => x < 32 && y < 32 ) ) );
		var query = new NavQuery( mesh );
		uint seed = 12345;
		int small = 0, total = 20000;
		var quadrants = new int[4];
		for ( int i = 0; i < total; i++ )
		{
			Assert.IsTrue( query.FindRandomPoint( default, float.PositiveInfinity, default, ref seed, out var poly, out var point ) );
			Assert.IsTrue( query.GetHeight( poly, point, out float z ) && MathF.Abs( z - point.Z ) < 0.001f, "The point is on its polygon" );
			if ( point.X > 1000 ) small++;
			else quadrants[(point.X > 320 ? 1 : 0) + (point.Y > 320 ? 2 : 0)]++;
		}
		Assert.AreEqual( 0.2, small / (double)total, 0.015, "A fifth of the area is in the small tile" );
		foreach ( int quadrant in quadrants ) Assert.AreEqual( 0.2, quadrant / (double)total, 0.015 );

		// The same seed gives the same point.
		uint a = 99, b = 99;
		query.FindRandomPoint( default, float.PositiveInfinity, default, ref a, out _, out var first );
		query.FindRandomPoint( default, float.PositiveInfinity, default, ref b, out _, out var second );
		Assert.AreEqual( first, second );
		Assert.AreEqual( a, b );

		// Within a disc: inside the radius, on the mesh, and spread over the part of the disc on the mesh.
		var centre = new Vector3( 620, 320, 0 );
		int left = 0, hits = 0;
		for ( int i = 0; i < 2000; i++ )
		{
			if ( !query.FindRandomPoint( centre, 100, default, ref seed, out _, out var point ) ) continue;
			hits++;
			Assert.IsTrue( Vector3.Distance( point, centre ) <= 100.01f && point.X <= 640 );
			if ( point.X < 570 ) left++;
		}
		Assert.IsTrue( hits > 1990, "Half the disc is on the mesh, so 16 attempts practically always hit" );
		Assert.IsTrue( left > hits / 5 && left < hits / 2 );
		Assert.IsFalse( query.FindRandomPoint( new Vector3( 5000, 5000, 0 ), 100, default, ref seed, out _, out _ ) );
		Assert.IsFalse( query.FindRandomPoint( centre, 100, new NavFilter( 1ul << 1 ), ref seed, out _, out _ ) );
	}

	[TestMethod]
	public void RepairRejoinsTheCorridorInsteadOfReplanning()
	{
		var mesh = Mesh( new Layout( Obstacles: true ) );
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 from = new( 105, 325, 0 ), target = new( 545, 325, 0 );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( from, target, Snap, default, path ) );
		var corridor = path.Polys.ToArray();
		int count = corridor.Length;
		var end = corridor[^1];

		// The agent was moved a few cells off its corridor.
		var moved = new Vector3( 135, 365, 0 );
		var poly = GridMesh.Nearest( query, 135, 365 );
		Assert.IsFalse( corridor.Contains( poly ) );
		Assert.IsTrue( query.Repair( mesh.State, poly, moved, target, default, ref corridor, ref count, 64 ) );
		Assert.AreEqual( poly, corridor[0] );
		Assert.AreEqual( end, corridor[count - 1] );
		Assert.IsTrue( query.SearchExpansions <= 64 );

		Span<Vector3> corners = stackalloc Vector3[16];
		int pulled = query.FindCorners( corridor.AsSpan( 0, count ), moved, target, corners );
		Assert.IsTrue( pulled >= 2 );
		Assert.AreEqual( moved, corners[0] );

		// Too far away for the budget: the corridor stays as it was.
		var before = corridor.AsSpan( 0, count ).ToArray();
		var far = GridMesh.Nearest( query, 35, 35 );
		Assert.IsFalse( query.Repair( mesh.State, far, new Vector3( 35, 35, 0 ), target, default, ref corridor, ref count, 2 ) );
		Assert.IsTrue( before.AsSpan().SequenceEqual( corridor.AsSpan( 0, count ) ) );
	}

	[TestMethod]
	[DataRow( 0.0254f )]
	[DataRow( 100f )]
	public void QueriesBehaveTheSameInAnotherUnit( float scale )
	{
		// Nothing in the library is tied to inches: the same world in metres, or a hundred times larger,
		// gives the same paths, portals, link snaps, walls and moves, scaled.
		static NavMesh World( float scale )
		{
			var mesh = new NavMesh( GridMesh.Scaled( scale ) );
			mesh.CommitTiles( [
				GridMesh.Rects( 0, 0, [0, 30, 34, 64], [0, 28, 36, 64], ( i, j ) => i != 1 || j == 1, scale ),
				GridMesh.Rects( 1, 0, [0, 20, 64], [0, 32, 64], ( i, j ) => j == 1, scale, 6 ),
				GridMesh.Rects( 0, 1, [0, 64], [0, 64], null, scale, 9 ),
				GridMesh.Rects( 1, 1, [0, 64], [0, 64], null, scale, 200 )] );
			mesh.AddLink( new NavLink { Start = new Vector3( 900, 600, 7 ) * scale, End = new Vector3( 900, 700, 201 ) * scale, Radius = 24 * scale, Bidirectional = true, Area = 1 } );
			mesh.Commit();
			return mesh;
		}

		NavMesh reference = World( 1 ), scaled = World( scale );
		Assert.AreEqual( reference.State.Find( 0, 0 )!.Ext.Length, scaled.State.Find( 0, 0 )!.Ext.Length );
		Assert.AreEqual( 2, scaled.State.Find( 0, 0 )!.Ext.Length, "The 6 high step to the east connects, the 9 high one to the north does not" );
		Assert.AreEqual( 0, scaled.State.Find( 0, 1 )!.Ext.Length );
		Assert.IsTrue( scaled.State.Links[0].Connected );

		NavQuery a = new( reference ), b = new( scaled );
		NavPath pathA = new(), pathB = new();
		var random = new Random( 3 );
		float tolerance = 0.002f * scale;
		Span<PolyRef> visited = stackalloc PolyRef[16];
		Span<Vector3> wallsA = stackalloc Vector3[8], wallsB = stackalloc Vector3[8];
		int complete = 0;
		for ( int i = 0; i < 300; i++ )
		{
			// Off the lattice, so that no point sits exactly on a polygon edge, where rounding decides.
			Vector3 from = new( random.Next( 5, 1275 ) + 0.37f, random.Next( 5, 1275 ) + 0.41f, random.Next( 0, 2 ) * 200 ), to = new( random.Next( 5, 1275 ) + 0.43f, random.Next( 5, 1275 ) + 0.47f, random.Next( 0, 2 ) * 200 );
			var half = new Vector3( 20, 20, 60 );
			var status = a.FindPath( from, to, half, default, pathA );
			Assert.AreEqual( status, b.FindPath( from * scale, to * scale, half * scale, default, pathB ), $"{from} to {to}" );
			if ( status < PathStatus.Partial ) continue;
			if ( status == PathStatus.Complete ) complete++;
			Assert.AreEqual( pathA.Corners.Length, pathB.Corners.Length, $"{from} to {to}" );
			for ( int c = 0; c < pathA.Corners.Length; c++ ) Assert.IsTrue( Vector3.Distance( pathA.Corners[c] * scale, pathB.Corners[c] ) <= tolerance, $"{from} to {to}: corner {c} is {pathB.Corners[c]}, expected {pathA.Corners[c] * scale}" );
			Assert.AreEqual( pathA.Polys.Length, pathB.Polys.Length );

			PolyRef polyA = pathA.Polys[0], polyB = pathB.Polys[0];
			Vector3 positionA = pathA.Start, positionB = pathB.Start, delta = new( random.Next( -90, 90 ) + 0.13f, random.Next( -90, 90 ) + 0.17f, 0 );
			a.Move( ref polyA, ref positionA, delta, default, visited, out int countA );
			b.Move( ref polyB, ref positionB, delta * scale, default, visited, out int countB );
			Assert.AreEqual( countA, countB, $"polygons crossed moving from {pathA.Start} by {delta}" );
			Assert.IsTrue( Vector3.Distance( positionA * scale, positionB ) <= tolerance );

			int foundA = a.FindWalls( polyA, positionA, 150, 50, default, wallsA ), foundB = b.FindWalls( polyB, positionB, 150 * scale, 50 * scale, default, wallsB );
			Assert.AreEqual( foundA, foundB, $"walls around {positionA}" );
			if ( foundA > 0 ) Assert.IsTrue( Vector3.Distance( wallsA[0] * scale, wallsB[0] ) <= tolerance || Vector3.Distance( wallsA[0] * scale, wallsB[1] ) <= tolerance );
		}
		Assert.IsTrue( complete > 30, $"{complete} complete paths" );
	}

	[TestMethod]
	public void PathObjectIsReusableAndCopyable()
	{
		var mesh = Mesh( new Layout( Obstacles: true ) );
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Assert.IsFalse( path.IsValid );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 15, 15, 0 ), new Vector3( 625, 625, 0 ), Snap, default, path ) );
		var copy = new NavPath();
		copy.CopyFrom( path );
		Assert.AreEqual( PathStatus.StartNotFound, query.FindPath( new Vector3( 15, 15, 900 ), new Vector3( 625, 625, 0 ), Snap, default, path ) );
		Assert.AreEqual( 0, path.Polys.Length + path.Corners.Length );
		Assert.AreEqual( PathStatus.EndNotFound, query.FindPath( new Vector3( 15, 15, 0 ), new Vector3( 625, 625, 900 ), Snap, default, path ) );

		Assert.IsTrue( copy.IsValid && copy.Polys.Length > 10 && copy.Corners.Length >= 2 );
		Assert.AreEqual( new Vector3( 15, 15, 0 ), copy.Start );
		Assert.AreEqual( new Vector3( 625, 625, 0 ), copy.End );
		Assert.IsTrue( copy.Length >= Vector3.Distance( copy.Start, copy.End ) );
	}
}
