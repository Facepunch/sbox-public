using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// Edge cases of the mesh state, the bake and the queries: bad input, ids given out again, commits overtaken.
/// </summary>
[TestClass]
public class ReviewTests
{
	static readonly Vector3 Snap = GridMesh.Snap;

	static NavMesh TwoFloors( byte area = 1 ) => GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, null, ( x, y ) => area ), new GridMesh.Layer( 200, null, ( x, y ) => area ) ) );

	static NavLink Ladder( byte area = NavFilter.DefaultArea ) => NavLink.Create( new Vector3( 325, 325, 1 ), new Vector3( 325, 325, 201 ), 24, true ) with { Area = area };

	[TestMethod]
	public void LinkAreaAboveTheMaximumOrNoAreaIsRefused()
	{
		var mesh = TwoFloors( area: 2 );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.AddLink( Ladder( 64 ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.AddLink( Ladder( 200 ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.AddLink( Ladder( NavFilter.NoArea ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.AddLink( new NavLink { Start = new Vector3( 325, 325, 1 ), End = new Vector3( 325, 325, 201 ), Radius = 24 } ), "A link that sets no area" );
		int id = mesh.AddLink( Ladder() );
		Assert.AreEqual( 0, id, "A refused link takes no id" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.SetLink( id, Ladder( 64 ) ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.SetLink( id, Ladder( NavFilter.NoArea ) ) );
		mesh.Commit();
		Assert.AreEqual( NavFilter.DefaultArea, mesh.State.LinkAt( id ).Def.Area, "NavLink.Create makes a ground link" );
		var made = NavLink.Create( Vector3.Zero, Vector3.UnitX, 1, false );
		Assert.AreEqual( (NavFilter.DefaultArea, (byte)0, 1f, false), (made.Area, made.Type, made.Radius, made.Bidirectional) );

		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 below = new( 105, 325, 0 ), above = new( 505, 325, 200 );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( below, above, Snap, Filters.All, path ) );
		// The floors are area 2. A filter that forbids ground forbids the default link and nothing else here.
		Assert.AreEqual( PathStatus.Partial, query.FindPath( below, above, Snap, Filters.All.WithForbiddenAreas( 1ul << NavFilter.DefaultArea ), path ) );

		mesh.SetLink( id, Ladder( NavFilter.MaxArea ) );
		mesh.Commit();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( below, above, Snap, Filters.All.WithForbiddenAreas( 1ul << NavFilter.DefaultArea ), path ) );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( below, above, Snap, Filters.All.WithForbiddenAreas( 1ul << NavFilter.MaxArea ), path ) );
	}

	[TestMethod]
	public void FilterRefusesACostBelowOne()
	{
		var filter = NavFilter.Create();
		foreach ( float bad in new[] { 0.5f, 0f, -1f, 0.999f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
		{
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => filter.WithAreaCost( 2, bad ), $"cost {bad}" );
		}
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => filter.WithAreaCost( 64, 2 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => filter.WithAreaCost( -1, 2 ) );

		var priced = filter.WithAreaCost( 1, 3 );
		Assert.AreEqual( 3f, priced.AreaCosts[1] );
		Assert.AreEqual( 1f, filter.AreaCosts[1], "A With method changes the copy only" );
		for ( int area = 0; area <= NavFilter.MaxArea; area++ )
		{
			Assert.AreEqual( 1f, filter.AreaCosts[area] );
			Assert.AreEqual( 1f, filter.LinkCosts[area] );
			Assert.AreEqual( 0f, filter.LinkPenalties[area] );
		}
		Assert.AreEqual( (0ul, 0ul), (filter.ForbiddenAreas, filter.ForbiddenLinks) );
		Assert.AreEqual( 5ul, filter.WithForbiddenAreas( 5 ).ForbiddenAreas );
		Assert.IsTrue( filter.IsValid( out _ ) && priced.IsValid( out _ ) );
		Assert.AreEqual( filter, NavFilter.Create() );
		Assert.AreNotEqual( filter, priced );
		Assert.AreEqual( priced, NavFilter.Create().WithAreaCost( 1, 3 ) );
	}

	[TestMethod]
	public void ADefaultFilterIsRefusedByQueriesAndTheCrowd()
	{
		var mesh = TwoFloors( area: 2 );
		var query = new NavQuery( mesh );
		var path = new NavPath();
		NavFilter none = default;
		Assert.IsFalse( none.IsValid( out string? reason ) );
		StringAssert.Contains( reason, "NavFilter.Create()" );
		Assert.AreNotEqual( none, NavFilter.Create() );

		Vector3 below = new( 105, 325, 0 ), above = new( 505, 325, 200 );
		var error = Assert.ThrowsException<ArgumentException>( () => query.FindPath( below, above, Snap, none, path ) );
		StringAssert.Contains( error.Message, "NavFilter.Create()" );
		Assert.ThrowsException<ArgumentException>( () => query.FindNearest( below, Snap, none, out _, out _ ) );
		Assert.IsTrue( query.FindNearest( below, Snap, NavFilter.Create(), out var poly, out var point ) );
		Assert.ThrowsException<ArgumentException>( () => query.FindPath( poly, point, poly, point, none, path ) );
		Assert.ThrowsException<ArgumentException>( () => query.IsValid( poly, none ) );
		Assert.ThrowsException<ArgumentException>( () => query.Move( ref poly, ref point, Vector3.UnitX, none, default, out _ ) );
		Assert.ThrowsException<ArgumentException>( () => query.FindWalls( new() { Poly = poly, Center = point, Radius = 100, Height = 64 }, none, new Vector3[8] ) );
		Assert.ThrowsException<ArgumentException>( () => query.FindNearestWall( poly, point, 100, none, out _ ) );
		uint seed = 1;
		Assert.ThrowsException<ArgumentException>( () => query.FindRandomPoint( point, 100, none, ref seed, out _, out _ ) );

		var crowd = new NavCrowd( mesh, 1 );
		var agent = AgentParams.Create( new() { Radius = 16, Height = 64, MaxSpeed = 100, Acceleration = 400 } );
		Assert.IsTrue( agent.IsValid( out _ ) );
		var unfiltered = agent with { Filter = none };
		Assert.IsFalse( unfiltered.IsValid( out reason ) );
		StringAssert.Contains( reason, "NavFilter.Create()" );
		Assert.ThrowsException<ArgumentException>( () => crowd.Add( below, unfiltered ) );
		Assert.ThrowsException<ArgumentException>( () => crowd.Add( below, new AgentParams { Radius = 16, Height = 64, MaxSpeed = 100, Acceleration = 400 } ) );
		int id = crowd.Add( below, agent );
		Assert.ThrowsException<ArgumentException>( () => crowd.SetParams( id, unfiltered ) );
		Assert.IsTrue( crowd.Params[id].Filter.IsValid( out _ ) );
	}

	[TestMethod]
	public void EverySettingThatIsValidRoundTripsThroughABake()
	{
		var basis = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 256 } );
		var variants = new List<NavSettings> { basis, GridMesh.Settings, default( NavSettings ) with { CellSize = 1, CellHeight = 1, TileSize = 32, AgentHeight = 1, MaxEdgeCells = 1 } };
		foreach ( float value in new[] { 0f, 1e-6f, 0.5f, 1e6f, float.MaxValue, float.Epsilon } )
		{
			variants.Add( basis with { CellSize = value } );
			variants.Add( basis with { CellHeight = value } );
			variants.Add( basis with { AgentHeight = value } );
			variants.Add( basis with { AgentRadius = value } );
			variants.Add( basis with { AgentClimb = value } );
			variants.Add( basis with { MaxEdgeError = value } );
			variants.Add( basis with { AgentMaxSlope = MathF.Min( value, 90 ) } );
		}
		foreach ( int value in new[] { 0, 16, 32, 48, 1024, 1040, int.MaxValue } )
		{
			variants.Add( basis with { TileSize = value } );
			variants.Add( basis with { MaxEdgeCells = value } );
			variants.Add( basis with { MinRegionCells = value } );
		}
		foreach ( float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f } )
		{
			foreach ( var broken in new[] { basis with { CellSize = bad }, basis with { CellHeight = bad }, basis with { AgentHeight = bad }, basis with { AgentRadius = bad }, basis with { AgentClimb = bad }, basis with { AgentMaxSlope = bad }, basis with { MaxEdgeError = bad } } )
			{
				Assert.IsFalse( broken.IsValid( out var reason ), $"{bad} was accepted" );
				Assert.IsNotNull( reason );
				Assert.ThrowsException<ArgumentException>( () => new NavMesh( broken, NavMeshOptions.Create( broken ) ) );
			}
		}

		int valid = 0;
		foreach ( var settings in variants )
		{
			if ( !settings.IsValid( out _ ) ) continue;
			valid++;
			var writer = new ArrayBufferWriter<byte>();
			Assert.IsTrue( new NavMesh( settings, NavMeshOptions.Create( settings ) ).Save( writer ) );
			Assert.IsTrue( NavMesh.TryReadSettings( writer.WrittenSpan, out var read ), $"A bake of valid settings was refused: cell {settings.CellSize}, error {settings.MaxEdgeError}" );
			Assert.AreEqual( settings, read );
			Assert.IsNotNull( BakeTests.Load( writer.WrittenSpan ) );
		}
		Assert.IsTrue( valid > 30, $"{valid} valid variants" );
	}

	[TestMethod]
	public void MaxEdgeCellsOfZeroIsRefusedAndNoLimitIsTheLargestInt()
	{
		var basis = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 64 } );
		var zero = basis with { MaxEdgeCells = 0 };
		Assert.IsFalse( zero.IsValid( out var reason ) );
		StringAssert.Contains( reason, "MaxEdgeCells" );
		Assert.ThrowsException<ArgumentException>( () => new NavMesh( zero, NavMeshOptions.Create( zero ) ) );

		// A bake that holds 0 is one of invalid settings: it does not load.
		var writer = new ArrayBufferWriter<byte>();
		Assert.IsTrue( new NavMesh( basis, NavMeshOptions.Create( basis ) ).Save( writer ) );
		var data = writer.WrittenSpan.ToArray();
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 36 ), 0 );
		Assert.IsFalse( NavMesh.TryReadSettings( data, out _ ) );
		Assert.IsNull( NavMesh.Load( data, NavMeshOptions.Create( basis ) ) );

		// int.MaxValue splits nothing, as a limit no edge reaches does.
		Vector3[] vertices = [new( 0, 0, 0 ), new( 256, 0, 0 ), new( 256, 256, 0 ), new( 0, 256, 0 ), new( 100, 100, 0 ), new( 150, 100, 0 ), new( 150, 150, 50 )];
		int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6];
		int Polygons( int maxEdgeCells )
		{
			var settings = basis with { MaxEdgeCells = maxEdgeCells };
			var mesh = new NavMesh( settings, NavMeshOptions.Create( settings ) );
			Assert.AreEqual( 0, mesh.Build( new NavBuilder(), vertices, indices ) );
			return mesh.GetStats().Polygons;
		}
		Assert.AreEqual( Polygons( 1 << 20 ), Polygons( int.MaxValue ) );
		Assert.IsTrue( Polygons( 4 ) > Polygons( int.MaxValue ), "A short limit splits edges" );
	}

	[TestMethod]
	public void CodecWithoutAFunctionIsRefused()
	{
		Func<ReadOnlySpan<byte>, Span<byte>, int> copy = static ( source, target ) => 0;
		Assert.ThrowsException<ArgumentNullException>( () => new NavCodec( null!, copy ) );
		Assert.ThrowsException<ArgumentNullException>( () => new NavCodec( copy, null! ) );
	}

	[TestMethod]
	public void CommitOverlappingARunningBuildNeverPublishesHalfAResult()
	{
		var settings = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 64 } );
		Vector3[] vertices = [new( 0, 0, 0 ), new( 256, 0, 0 ), new( 256, 256, 0 ), new( 0, 256, 0 )];
		int[] indices = [0, 1, 2, 0, 2, 3];
		var mesh = new NavMesh( settings, NavMeshOptions.Create( settings ) );
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), vertices, indices ) );
		var builder = new NavBuilder();
		var jobs = new TileJob[1];
		int published = 0, abandoned = 0;

		for ( int round = 0; round < 300; round++ )
		{
			mesh.MarkDirty( 0, 0 );
			Assert.AreEqual( 1, mesh.TakeJobs( jobs ) );
			var job = jobs[0];
			uint serial = mesh.State.Find( 0, 0 )!.Serial;
			using var start = new ManualResetEventSlim();
			var worker = new Thread( () =>
			{
				start.Wait();
				builder.Build( job, vertices, indices );
			} );
			worker.Start();
			start.Set();

			// The owner commits while the build runs: either it is too early and abandons the job, or it
			// sees the finished job whole.
			if ( round % 3 != 0 ) Thread.SpinWait( 200 * (round % 50) );
			Assert.AreEqual( 0, mesh.Commit( jobs ) );
			var tile = mesh.State.Find( 0, 0 );
			Assert.IsNotNull( tile, "A build seen half finished was committed as an empty tile" );
			worker.Join();

			if ( tile.Serial != serial ) published++;
			else
			{
				abandoned++;
				Assert.AreEqual( 1, mesh.PendingTiles, "An abandoned tile stays dirty" );
				Assert.AreEqual( 0, mesh.Commit( jobs ), "Its late result is ignored" );
				Assert.AreEqual( serial, mesh.State.Find( 0, 0 )!.Serial );
				Assert.AreEqual( 1, mesh.TakeJobs( jobs ) );
				builder.Build( jobs[0], vertices, indices );
				Assert.AreEqual( 0, mesh.Commit( jobs ) );
			}
			Assert.AreEqual( 0, mesh.PendingTiles );
			Assert.AreEqual( TileJobStatus.Built, job.Status );
		}
		Console.WriteLine( $"{published} commits saw the finished build, {abandoned} came too early" );
	}

	[TestMethod]
	public void SerialStartsAgainAtOneAndLeavesPublishedStatesAlone()
	{
		// The first commit stamps the tile page and the index block with serial 1. Four billion commits
		// later the serial is 1 again, and that commit has to copy both all the same.
		var mesh = GridMesh.Mesh( (0, 0), (1, 0) );
		var before = mesh.State;
		var tile = before.Find( 0, 0 )!;
		Assert.AreEqual( 1u, tile.Serial );
		typeof( NavMesh ).GetField( "commits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance )!.SetValue( mesh, uint.MaxValue );

		mesh.RemoveTile( 0, 0 );
		Assert.AreEqual( 0, mesh.CommitTiles( [GridMesh.Quad( 0, 1 )] ) );
		var after = mesh.State;
		Assert.IsNull( after.Find( 0, 0 ) );
		Assert.AreEqual( 1u, after.Find( 0, 1 )!.Serial, "0 stands for no serial and is skipped" );
		Assert.AreSame( tile, before.Find( 0, 0 ), "The commit wrote into a page or an index block of a published state" );
		Assert.IsNull( before.Find( 0, 1 ) );
		Assert.AreEqual( 2, before.TileCount );

		// And the commits after it go on as ever
		var query = new NavQuery( mesh );
		var kept = GridMesh.Nearest( query, GridMesh.TileWorld * 1.5f, GridMesh.TileWorld * 0.5f );
		mesh.CommitTiles( [GridMesh.Flat( 0, 0 )] );
		Assert.AreEqual( 2u, mesh.State.Find( 0, 0 )!.Serial );
		Assert.IsTrue( query.IsValid( kept, Filters.All ) );
		Assert.AreEqual( 1u, mesh.State.Find( 0, 1 )!.Serial );
		Assert.IsTrue( mesh.State.Find( 0, 1 )!.Ext.Length > 0 );
		Assert.AreEqual( 0, after.Find( 0, 1 )!.Ext.Length );
		Assert.IsNull( after.Find( 0, 0 ) );
	}

	[TestMethod]
	public void RandomPointInADiscMostlyOffTheMeshStillFindsOne()
	{
		// About a twentieth of the disc lies on the mesh.
		var mesh = GridMesh.Mesh( GridMesh.Flat( 0, 0 ) );
		var query = new NavQuery( mesh );
		var centre = new Vector3( -80, 320, 0 );
		uint seed = 5;
		int upper = 0;
		for ( int i = 0; i < 2000; i++ )
		{
			Assert.IsTrue( query.FindRandomPoint( centre, 100, Filters.All, ref seed, out var poly, out var point ), $"No point in try {i} although the disc reaches the mesh" );
			Assert.IsTrue( point.X >= 0 && Vector3.Distance( point, centre ) <= 100.01f, $"{point} is not under the disc" );
			Assert.IsTrue( query.IsValid( poly, Filters.All ) );
			if ( point.Y > 320 ) upper++;
		}
		Assert.IsTrue( upper > 800 && upper < 1200, $"{upper} of 2000 points in the upper half" );

		// A disc that only touches the mesh in one corner.
		Assert.IsTrue( query.FindRandomPoint( new Vector3( -60, -60, 0 ), 85, Filters.All, ref seed, out _, out var corner ) );
		Assert.IsTrue( corner.X >= 0 && corner.Y >= 0 && corner.Length() < 2 );
		Assert.IsFalse( query.FindRandomPoint( new Vector3( -60, -60, 0 ), 84, Filters.All, ref seed, out _, out _ ) );
	}

	[TestMethod]
	public void NearestOverAllOfASparseMeshCostsPerTileNotPerPlace()
	{
		// Two tiles 50,000 tiles apart: rings over the places between them would take minutes.
		var mesh = GridMesh.Mesh( GridMesh.Quad( 0, 0 ), GridMesh.Quad( 50000, 50000, area: 3 ) );
		var query = new NavQuery( mesh );
		var all = new Vector3( float.PositiveInfinity );
		var watch = Stopwatch.StartNew();
		for ( int i = 0; i < 100; i++ )
		{
			Assert.IsTrue( query.FindNearest( new Vector3( 640f * 40000, 640f * 40000, 0 ), all, Filters.All, out _, out var far ) );
			Assert.AreEqual( 640f * 50000, far.X );
			Assert.IsTrue( query.FindNearest( new Vector3( 640f * 100, 640f * 3000, 0 ), all, Filters.All, out _, out var near ) );
			Assert.AreEqual( 640f, near.Y );
			Assert.IsTrue( query.FindNearest( new Vector3( 100, 100, 50 ), all, Filters.All.WithForbiddenAreas( 1ul << 1 ), out _, out var allowed ) );
			Assert.AreEqual( 640f * 50000, allowed.X, "Only the far tile passes the filter" );
			Assert.IsFalse( query.FindNearest( new Vector3( 100, 100, 50 ), all, Filters.All.WithForbiddenAreas( 1ul << 1 | 1ul << 3 ), out _, out _ ) );
		}
		Assert.IsTrue( watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms" );

		// A path that cannot be found across the gap asks for the nearest polygon of the start island.
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Partial, query.FindPath( new Vector3( 100, 100, 0 ), new Vector3( 640f * 50000 + 100, 640f * 50000 + 100, 0 ), Snap, Filters.All, path ) );
		Assert.IsTrue( watch.ElapsedMilliseconds < 4000 );
	}

	[TestMethod]
	public void PathThroughTheHighestLinkId()
	{
		var mesh = TwoFloors();
		for ( int i = 0; i < PolyRef.MaxLinks - 1; i++ ) mesh.AddLink( new NavLink { Start = new Vector3( 9000 + i, 9000, 0 ), End = new Vector3( 9000 + i, 9100, 0 ), Radius = 4, Area = NavFilter.DefaultArea } );
		int last = mesh.AddLink( Ladder() );
		Assert.AreEqual( PolyRef.MaxLinks - 1, last );
		Assert.AreEqual( -1, mesh.AddLink( Ladder() ) );
		mesh.Commit();

		var query = new NavQuery( mesh );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 105, 325, 0 ), new Vector3( 505, 325, 200 ), Snap, Filters.All, path ) );
		var link = path.Polys.ToArray().Single( p => p.IsLink );
		Assert.AreEqual( last, link.Link );
		Assert.IsFalse( link.IsNone );
		Assert.IsTrue( query.GetLink( link, path.Start, out var entry, out var exit ) );
		Assert.AreEqual( path.Corners[1], entry );
		Assert.AreEqual( path.Corners[2], exit );
		Assert.IsTrue( mesh.State.IsValid( link, Filters.All ) );

		// The query answers for a link as it does for a polygon: valid, not allowed by a filter that
		// forbids its area, and stale once the link is set again or removed
		Assert.IsTrue( query.IsValid( link, Filters.All ) );
		Assert.IsFalse( query.IsValid( link, Filters.All.WithForbiddenAreas( 1ul << NavFilter.DefaultArea ) ) );
		Assert.IsTrue( query.IsValid( path.Polys[0], Filters.All ) );
		Assert.IsFalse( query.IsValid( default, Filters.All ) );
		Assert.IsFalse( query.IsValid( PolyRef.ForLink( link.Serial, last - 1 ), Filters.All ), "A link that found no mesh" );

		// And back down, which a one-way link at the same place would refuse after looking over the floor.
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 505, 325, 200 ), new Vector3( 105, 325, 0 ), Snap, Filters.All, path ) );
		Assert.AreEqual( last, path.Polys.ToArray().Single( p => p.IsLink ).Link );

		mesh.SetLink( last, Ladder() );
		mesh.Commit();
		Assert.IsFalse( query.IsValid( link, Filters.All ), "The link was set again" );
		var again = PolyRef.ForLink( mesh.State.LinkAt( last ).Serial, last );
		Assert.IsTrue( query.IsValid( again, Filters.All ) );
		mesh.RemoveLink( last );
		mesh.Commit();
		Assert.IsFalse( query.IsValid( again, Filters.All ), "The link is gone" );
	}

	[TestMethod]
	public void MarkDirtyRefusesABoxWithoutEnd()
	{
		var mesh = GridMesh.Create();
		Assert.ThrowsException<ArgumentException>( () => mesh.MarkDirty( new Vector3( float.NaN ), Vector3.Zero ) );
		Assert.ThrowsException<ArgumentException>( () => mesh.MarkDirty( new Vector3( -1e30f ), new Vector3( 1e30f ) ) );
		Assert.ThrowsException<ArgumentException>( () => mesh.MarkDirty( Vector3.Zero, new Vector3( 640f * 600, 640f * 600, 0 ) ) );
		Assert.AreEqual( 0, mesh.PendingTiles );
		mesh.MarkDirty( Vector3.Zero, new Vector3( 640f * 500, 640f * 500, 0 ) );
		Assert.IsTrue( mesh.PendingTiles > 250000 );
	}
}
