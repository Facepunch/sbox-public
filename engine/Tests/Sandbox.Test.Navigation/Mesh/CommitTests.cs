using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

[TestClass]
public class CommitTests
{
	static readonly Vector3 Snap = GridMesh.Snap;

	static PathStatus Path( NavMesh mesh, Vector3 from, Vector3 to, NavPath? path = null )
	{
		return new NavQuery( mesh ).FindPath( from, to, Snap, default, path ?? new NavPath() );
	}

	[TestMethod]
	public void NeighbourTilesConnectAcrossTheBorder()
	{
		var mesh = GridMesh.Mesh( (0, 0), (1, 0), (0, 1), (-1, 0), (0, -1) );
		var centre = mesh.State.Find( 0, 0 )!;
		Assert.AreEqual( GridMesh.Cells * 4, centre.Ext.Length, "Every border cell has one neighbour across" );
		Assert.AreEqual( GridMesh.Cells, mesh.State.Find( 1, 0 )!.Ext.Length );

		foreach ( var target in new[] { new Vector3( 1200, 320, 0 ), new Vector3( 320, 1200, 0 ), new Vector3( -500, 320, 0 ), new Vector3( 320, -500, 0 ) } )
		{
			var path = new NavPath();
			Assert.AreEqual( PathStatus.Complete, Path( mesh, new Vector3( 320, 320, 0 ), target, path ) );
			Assert.AreEqual( Vector3.Distance( new Vector3( 320, 320, 0 ), target ), path.Length, 0.01f );
		}
		// Diagonal tiles are not neighbours of each other here, but both are reached through the centre.
		Assert.AreEqual( PathStatus.Complete, Path( mesh, new Vector3( 1200, 320, 0 ), new Vector3( 320, 1200, 0 ) ) );
	}

	[TestMethod]
	[DataRow( 8f, true )]
	[DataRow( 9f, false )]
	[DataRow( -8f, true )]
	[DataRow( -9f, false )]
	public void PortalsConnectWithinTheClimbThreshold( float step, bool connected )
	{
		var mesh = GridMesh.Create();
		mesh.CommitTiles( [GridMesh.Flat( 0, 0 ), GridMesh.Flat( 1, 0, step )] );
		Assert.AreEqual( connected ? GridMesh.Cells : 0, mesh.State.Find( 0, 0 )!.Ext.Length );
		Assert.AreEqual( connected ? GridMesh.Cells : 0, mesh.State.Find( 1, 0 )!.Ext.Length );
		Assert.AreEqual( connected ? PathStatus.Complete : PathStatus.Partial, Path( mesh, new Vector3( 320, 320, 0 ), new Vector3( 900, 320, step ) ) );
	}

	[TestMethod]
	public void EdgesThatCrossInHeightConnect()
	{
		// The east tile's border edges rise from 0 to 40 over each cell, the west tile sits at 20:
		// both ends are 20 apart, more than the climb, but the edges cross.
		var mesh = GridMesh.Mesh( GridMesh.Flat( 0, 0, 20 ), GridMesh.Tile( 1, 0, new GridMesh.Layer( 0, Height: ( x, y ) => y % 20 == 0 ? 0 : 40 ) ) );
		Assert.AreEqual( GridMesh.Cells, mesh.State.Find( 0, 0 )!.Ext.Length );
	}

	[TestMethod]
	public void RampDoesNotConnectToAPlatformAboveIt()
	{
		// Review M5: a ramp along the border from 0 to 160, continued in the next tile, which also has a
		// flat platform at 80 over the first 160 units, where the ramp is at 0 to 40.
		static float Ramp( float x, float y ) => y * 0.25f;
		var mesh = GridMesh.Mesh(
			GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, Height: Ramp ) ),
			GridMesh.Tile( 1, 0, new GridMesh.Layer( 0, Height: Ramp ), new GridMesh.Layer( 80, ( x, y ) => y < 16 ) ) );

		var west = mesh.State.Find( 0, 0 )!;
		var east = mesh.State.Find( 1, 0 )!;
		Assert.AreEqual( GridMesh.Cells, west.Ext.Length, "Each ramp edge connects to its continuation only" );
		foreach ( var link in west.Ext )
		{
			ref readonly var target = ref east.Polys[link.Target & PolyRef.PolyMask];
			var v = east.Verts[target.Verts[0]];
			Assert.AreEqual( Ramp( v.X, v.Y ), v.Z, 0.001f, "A ramp edge connected to the platform" );
		}
	}

	[TestMethod]
	public void PartlyOpenPortalKeepsItsOpenPart()
	{
		// One polygon per tile. The east one only covers the upper half of the shared border.
		var mesh = GridMesh.Mesh( GridMesh.Quad( 0, 0 ), GridMesh.Quad( 1, 0, y0: 32 ) );
		var west = mesh.State.Find( 0, 0 )!;
		var east = mesh.State.Find( 1, 0 )!;
		Assert.AreEqual( 1, west.Ext.Length );
		Assert.AreEqual( 1, east.Ext.Length );

		// The west polygon's east edge runs from y = 640 down to 0, so the open half is its first half.
		Assert.AreEqual( 0, west.Ext[0].TMin );
		Assert.IsTrue( west.Ext[0].TMax is 127 or 128 );
		Assert.AreEqual( 0, east.Ext[0].TMin );
		Assert.AreEqual( 255, east.Ext[0].TMax );

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, Path( mesh, new Vector3( 600, 100, 0 ), new Vector3( 700, 400, 0 ), path ) );
		foreach ( var corner in path.Corners )
			Assert.IsTrue( MathF.Abs( corner.X - 640 ) > 0.01f || corner.Y >= 318, $"The path crosses the closed part of the border at {corner}" );
		Assert.IsTrue( path.Length > Vector3.Distance( new Vector3( 600, 100, 0 ), new Vector3( 700, 400, 0 ) ) );
	}

	[TestMethod]
	public void RemovingATileDisconnectsItsNeighbours()
	{
		var mesh = GridMesh.Mesh( (0, 0), (1, 0), (2, 0) );
		uint version = mesh.Version;
		Assert.AreEqual( PathStatus.Complete, Path( mesh, new Vector3( 100, 100, 0 ), new Vector3( 1500, 100, 0 ) ) );

		mesh.RemoveTile( 1, 0 );
		Assert.AreEqual( version, mesh.Version, "RemoveTile waits for the commit" );
		Assert.AreEqual( 0, mesh.Commit() );
		Assert.AreEqual( version + 1, mesh.Version );
		Assert.AreEqual( 0, mesh.State.Find( 0, 0 )!.Ext.Length );
		Assert.AreEqual( 0, mesh.State.Find( 2, 0 )!.Ext.Length );
		Assert.IsNull( mesh.State.Find( 1, 0 ) );
		Assert.AreEqual( PathStatus.EndNotFound, Path( mesh, new Vector3( 100, 100, 0 ), new Vector3( 900, 100, 0 ) ) );
		Assert.AreEqual( PathStatus.Partial, Path( mesh, new Vector3( 100, 100, 0 ), new Vector3( 1500, 100, 0 ) ) );

		mesh.CommitTiles( [GridMesh.Flat( 1, 0 )] );
		Assert.AreEqual( PathStatus.Complete, Path( mesh, new Vector3( 100, 100, 0 ), new Vector3( 1500, 100, 0 ) ) );
	}

	[TestMethod]
	public void CommitWithNothingToDoKeepsTheState()
	{
		var mesh = GridMesh.Mesh( (0, 0) );
		var state = mesh.State;
		Assert.AreEqual( 0, mesh.Commit() );
		mesh.RemoveTile( 5, 5 );
		Assert.AreEqual( 0, mesh.Commit() );
		Assert.AreSame( state, mesh.State );
	}

	[TestMethod]
	public void RebuiltTileInvalidatesItsReferences()
	{
		var mesh = GridMesh.Mesh( (0, 0), (1, 0) );
		var query = new NavQuery( mesh );
		var start = GridMesh.Nearest( query, 50, 320 );
		var end = GridMesh.Nearest( query, 590, 320 );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( start, new Vector3( 50, 320, 0 ), end, new Vector3( 590, 320, 0 ), default, path ) );

		mesh.CommitTiles( [GridMesh.Flat( 0, 0 )] );
		Assert.IsFalse( query.IsValid( start, default ), "A reference into a rebuilt tile stayed valid" );
		Assert.IsFalse( query.GetHeight( start, new Vector3( 50, 320, 0 ), out _ ) );
		Assert.AreEqual( PathStatus.NotFound, query.FindPath( start, new Vector3( 50, 320, 0 ), end, new Vector3( 590, 320, 0 ), default, path ) );
		Assert.IsFalse( path.IsValid );
		Assert.AreEqual( 0, path.Polys.Length );
		Span<Vector3> corners = stackalloc Vector3[8];
		Assert.AreEqual( 0, query.FindCorners( [start, end], new Vector3( 50, 320, 0 ), new Vector3( 590, 320, 0 ), corners ) );

		var again = GridMesh.Nearest( query, 50, 320 );
		Assert.AreNotEqual( start, again );
		Assert.AreEqual( start.Id, again.Id, "Same slot and polygon, different serial" );
	}

	[TestMethod]
	public void ReusedSlotDoesNotValidateAnOldReference()
	{
		// Review B1 (b): with per-tile serials counting from 1 the new tile in the old slot would
		// validate a reference to the removed one.
		var mesh = GridMesh.Mesh( (3, 3) );
		var query = new NavQuery( mesh );
		var old = GridMesh.Nearest( query, 3 * 640 + 50, 3 * 640 + 50 );
		int slot = mesh.State.Find( 3, 3 )!.Slot;

		mesh.RemoveTile( 3, 3 );
		mesh.Commit();
		Assert.IsFalse( query.IsValid( old, default ), "Removed tile reference remained valid" );

		mesh.CommitTiles( [GridMesh.Flat( 9, 9 )] );
		Assert.AreEqual( slot, mesh.State.Find( 9, 9 )!.Slot, "The freed slot is reused" );
		Assert.AreEqual( 1, mesh.GetStats().Tiles );
		Assert.IsFalse( query.IsValid( old, default ), "Reused slot accepted a stale reference" );
		Assert.IsTrue( query.IsValid( GridMesh.Nearest( query, 9 * 640 + 50, 9 * 640 + 50 ), default ) );
	}

	[TestMethod]
	public void NeighbourRelinkKeepsReferencesValid()
	{
		// Review B1 (a): a crate lands in tile (1, 0); corridors through (0, 0) must stay valid.
		var mesh = GridMesh.Mesh( (0, 0), (1, 0), (2, 0) );
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 50, 320, 0 ), new Vector3( 600, 320, 0 ), Snap, default, path ) );
		var before = mesh.State.Find( 0, 0 )!;
		var untouched = mesh.State.Find( 2, 0 )!;

		mesh.CommitTiles( [GridMesh.Tile( 1, 0, new GridMesh.Layer( 0, ( x, y ) => x > 2 || y > 40 ) )] );
		var after = mesh.State.Find( 0, 0 )!;
		Assert.AreNotSame( before, after, "The neighbour gets a new tile object for its new connections" );
		Assert.AreSame( before.Polys, after.Polys );
		Assert.AreSame( before.Tree, after.Tree );
		Assert.AreEqual( before.Serial, after.Serial );
		Assert.AreNotEqual( before.Ext.Length, after.Ext.Length );
		Assert.AreEqual( untouched.Serial, mesh.State.Find( 2, 0 )!.Serial );
		foreach ( var poly in path.Polys ) Assert.IsTrue( query.IsValid( poly, default ) );

		Span<Vector3> corners = stackalloc Vector3[8];
		Assert.AreEqual( 2, query.FindCorners( path.Polys, path.Start, path.End, corners ) );
	}

	[TestMethod]
	public void SerialsNeverRepeatAcrossTheMesh()
	{
		var mesh = GridMesh.Create();
		var seen = new HashSet<uint>();
		for ( int round = 0; round < 20; round++ )
		{
			mesh.CommitTiles( [GridMesh.Quad( round % 3, 0 ), GridMesh.Quad( round % 3, 1 )] );
			uint serial = mesh.State.Find( round % 3, 0 )!.Serial;
			Assert.AreEqual( serial, mesh.State.Find( round % 3, 1 )!.Serial, "Tiles of one commit share its serial" );
			Assert.IsTrue( seen.Add( serial ) && serial != 0 );
			if ( round % 4 == 3 )
			{
				mesh.RemoveTile( round % 3, 1 );
				mesh.Commit();
			}
		}
	}

	[TestMethod]
	public void CommittingTheSameTileObjectAgainPublishesANewBuild()
	{
		var mesh = GridMesh.Create();
		var tile = GridMesh.Flat( 0, 0 );
		mesh.CommitTiles( [tile] );
		var first = mesh.State.Find( 0, 0 )!;
		mesh.CommitTiles( [tile] );
		var second = mesh.State.Find( 0, 0 )!;
		Assert.AreNotSame( first, second );
		Assert.AreNotEqual( first.Serial, second.Serial );
		Assert.AreSame( tile, first );
	}

	[TestMethod]
	public void StatsTilesAndPolygons()
	{
		var mesh = GridMesh.Mesh( GridMesh.Flat( 0, 0 ), GridMesh.Quad( -1, 0 ), GridMesh.Quad( 2, -2, z: 50, area: 7 ) );
		var stats = mesh.GetStats();
		Assert.AreEqual( 3, stats.Tiles );
		Assert.AreEqual( GridMesh.Cells * GridMesh.Cells + 2, stats.Polygons );
		Assert.AreEqual( (GridMesh.Cells + 1) * (GridMesh.Cells + 1) + 8, stats.Vertices );
		long polygonBytes = (long)stats.Polygons * 28 + (long)stats.Vertices * 12;
		Assert.IsTrue( stats.RuntimeBytes > polygonBytes && stats.RuntimeBytes < polygonBytes * 2, $"{stats.RuntimeBytes} bytes for {polygonBytes} bytes of polygons and vertices" );

		var coordinates = new (int X, int Y)[8];
		Assert.AreEqual( 3, mesh.GetTiles( coordinates ) );
		CollectionAssert.AreEquivalent( new[] { (0, 0), (-1, 0), (2, -2) }, coordinates[..3] );
		Assert.AreEqual( 2, mesh.GetTiles( coordinates.AsSpan( 0, 2 ) ) );

		var vertices = new List<Vector3>();
		var counts = new List<int>();
		var areas = new List<byte>();
		mesh.GetPolygons( 2, -2, vertices, counts, areas );
		mesh.GetPolygons( 40, 40, vertices, counts, areas );
		CollectionAssert.AreEqual( new[] { 4 }, counts );
		CollectionAssert.AreEqual( new byte[] { 7 }, areas );
		float twice = 0;
		for ( int i = 0; i < 4; i++ ) twice += vertices[i].X * vertices[(i + 1) % 4].Y - vertices[(i + 1) % 4].X * vertices[i].Y;
		Assert.AreEqual( 2 * 640f * 640f, twice, 1f, "Polygons come out counter-clockwise seen from above" );
		Assert.AreEqual( 50f, vertices[0].Z );

		Assert.AreEqual( (0, 0), mesh.GetTileAt( new Vector3( 1, 639, 0 ) ) );
		Assert.AreEqual( (-1, -2), mesh.GetTileAt( new Vector3( -0.5f, -641, 0 ) ) );
		Assert.AreEqual( (3, 1), mesh.GetTileAt( new Vector3( 1920, 640, 0 ) ) );
	}

	[TestMethod]
	public void CommitCopiesOnlyTheTablePagesItTouches()
	{
		var mesh = GridMesh.Create();
		var tiles = new Tile[MeshState.PageSize * 3];
		for ( int i = 0; i < tiles.Length; i++ ) tiles[i] = GridMesh.Quad( i % 40 * 3, i / 40 * 3 );
		mesh.CommitTiles( tiles );
		var before = mesh.State;
		Assert.AreEqual( 3, before.Pages.Length );

		// Rebuild a tile whose slot is in the second page. Its neighbours are missing, so nothing else changes.
		var target = before.Pages[1]![7]!;
		mesh.CommitTiles( [GridMesh.Quad( target.X, target.Y )] );
		var after = mesh.State;
		Assert.AreNotSame( before.Pages, after.Pages );
		Assert.AreSame( before.Pages[0], after.Pages[0] );
		Assert.AreNotSame( before.Pages[1], after.Pages[1] );
		Assert.AreSame( before.Pages[2], after.Pages[2] );
		Assert.AreSame( before.Index, after.Index, "The coordinate index is shared while the set of tiles stays the same" );
		Assert.AreSame( before.Pages[1]![8], after.Pages[1]![8] );
		Assert.AreEqual( tiles.Length, after.TileCount );

		// Adding a tile changes the index, by one block.
		mesh.CommitTiles( [GridMesh.Quad( 500, 500 )] );
		Assert.AreNotSame( after.Index, mesh.State.Index );
		Assert.AreEqual( after.Index.Find( target.X, target.Y ), mesh.State.Index.Find( target.X, target.Y ) );
		Assert.AreEqual( -1, after.Index.Find( 500, 500 ), "A published index never changes" );
		Assert.AreEqual( tiles.Length, mesh.State.Index.Find( 500, 500 ) );
	}

	[TestMethod]
	public void IndexHandlesNegativeCoordinatesAndEmptiedBlocks()
	{
		var mesh = GridMesh.Create();
		var random = new Random( 5 );
		var live = new Dictionary<(int, int), bool>();
		for ( int round = 0; round < 60; round++ )
		{
			var batch = new List<Tile>();
			var changed = new HashSet<(int, int)>();
			for ( int i = 0; i < 20; i++ )
			{
				var at = (random.Next( -70, 70 ), random.Next( -70, 70 ));
				if ( !changed.Add( at ) ) continue;
				if ( live.Remove( at ) ) mesh.RemoveTile( at.Item1, at.Item2 );
				else if ( live.TryAdd( at, true ) ) batch.Add( GridMesh.Quad( at.Item1, at.Item2 ) );
			}
			Assert.AreEqual( 0, mesh.CommitTiles( batch.ToArray() ) );
			var state = mesh.State;
			Assert.AreEqual( live.Count, state.TileCount );
			foreach ( var at in live.Keys )
			{
				var tile = state.Find( at.Item1, at.Item2 );
				Assert.IsTrue( tile != null && tile.X == at.Item1 && tile.Y == at.Item2 );
				Assert.IsTrue( at.Item1 >= state.Index.MinX && at.Item1 <= state.Index.MaxX && at.Item2 >= state.Index.MinY && at.Item2 <= state.Index.MaxY );
			}
			Assert.IsNull( state.Find( 1000, 1000 ) );
		}
		foreach ( var at in live.Keys ) mesh.RemoveTile( at.Item1, at.Item2 );
		mesh.Commit();
		Assert.AreEqual( 0, mesh.GetStats().Tiles );
		Assert.IsTrue( mesh.State.Index.MaxX < mesh.State.Index.MinX, "An empty index has empty bounds" );
	}

	[TestMethod, TestCategory( "Slow" )]
	public void FullTileTableFailsTheJobInsteadOfThrowing()
	{
		var mesh = GridMesh.Create();
		var batch = new Tile[4096];
		int committed = 0;
		while ( committed < PolyRef.MaxTiles )
		{
			int count = Math.Min( batch.Length, PolyRef.MaxTiles - committed );
			for ( int i = 0; i < count; i++ ) batch[i] = GridMesh.Quad( (committed + i) % 512 * 2, (committed + i) / 512 * 2 );
			Assert.AreEqual( 0, mesh.CommitTiles( batch.AsSpan( 0, count ) ) );
			committed += count;
		}
		Assert.AreEqual( PolyRef.MaxTiles, mesh.GetStats().Tiles );

		mesh.MarkDirty( 5000, 5000 );
		var jobs = new TileJob[1];
		Assert.AreEqual( 1, mesh.TakeJobs( jobs ) );
		jobs[0].Complete( TileJobStatus.Built, GridMesh.Quad( 5000, 5000 ) );
		jobs[0].TrianglesUsed = 7;
		Assert.AreEqual( 1, mesh.Commit( jobs ) );
		Assert.AreEqual( 0, mesh.GetStats().TrianglesUsed, "A tile that did not get in leaves nothing behind" );
		Assert.AreEqual( 0, mesh.GetStats().CacheBytes );
		Assert.AreEqual( TileJobStatus.Failed, jobs[0].Status );
		StringAssert.Contains( jobs[0].Error, "tiles" );
		Assert.AreEqual( 0, mesh.PendingTiles, "A failed tile is not handed out again" );
		Assert.AreEqual( PolyRef.MaxTiles, mesh.GetStats().Tiles );

		// Replacing an existing tile still works, and a freed slot can be taken.
		Assert.AreEqual( 0, mesh.CommitTiles( [GridMesh.Quad( 0, 0 )] ) );
		mesh.RemoveTile( 2, 0 );
		Assert.AreEqual( 0, mesh.CommitTiles( [GridMesh.Quad( 5000, 5000 )] ) );
		var query = new NavQuery( mesh );
		Assert.IsTrue( query.FindNearest( new Vector3( 5000 * 640 + 320, 5000 * 640 + 320, 0 ), GridMesh.Snap, default, out var poly, out _ ) );
		Assert.IsFalse( poly.IsLink, "The last tile slot must not read as a link" );
	}
}
