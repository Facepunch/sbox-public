using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

[TestClass]
public class LinkPageTests
{
	const int Side = CommitMeasurement.LinkSide, PerTile = CommitMeasurement.LinksPerTile;

	static Tile Build( int x, int y ) => CommitMeasurement.Build( x, y );

	static NavLink LinkIn( int x, int y, int k ) => CommitMeasurement.LinkIn( x, y, k );

	[TestMethod]
	public void OneTileCommitAmongThirtyThousandLinksCopiesLittle()
	{
		var mesh = CommitMeasurement.ManyLinks( Side, PerTile );
		int middle = Side / 2;
		var tiles = Enumerable.Range( 0, 8 ).Select( i => Build( middle + i % 4, middle ) ).ToArray();
		long most = 0;
		for ( int i = 0; i < tiles.Length; i++ )
		{
			long before = GC.GetAllocatedBytesForCurrentThread();
			mesh.CommitTiles( [tiles[i]] );
			long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
			if ( i >= 4 ) most = Math.Max( most, bytes );
		}
		long table = (long)Side * Side * PerTile * System.Runtime.CompilerServices.Unsafe.SizeOf<LinkEntry>();
		Console.WriteLine( $"{Side * Side * PerTile} links, table {table:N0} bytes, a one-tile commit allocates at most {most:N0} bytes" );
		Assert.IsTrue( most * 20 < table, $"{most:N0} bytes per commit" );

		// The links of the rebuilt tile, and only those, were resolved again by the last commit.
		var s = mesh.State;
		uint serial = s.Find( middle + 3, middle )!.Serial;
		Assert.AreEqual( PerTile, s.Links().Count( link => link.Serial == serial ) );
	}

	static readonly int[] Cuts = [0, 10, 20, 32, 44, 54, 64];

	// Rectangles with some of them left out, so a link end may find no polygon or the one beside.
	static Tile Variant( int x, int y, int v ) => GridMesh.Rects( x, y, Cuts, [0, 12, 24, 40, 52, 64], v switch
	{
		1 => ( i, j ) => (i + j) % 3 != 0,
		2 => ( i, j ) => i != 2,
		3 => ( i, j ) => j != 1 && i != 4,
		_ => null
	}, z: v == 4 ? 6 : 0 );

	static (int X, int Y, int Poly) Where( MeshState s, uint poly )
	{
		if ( poly == PolyRef.NoId ) return (int.MinValue, 0, 0);
		var tile = s.Get( poly );
		return (tile.X, tile.Y, (int)(poly & s.PolyMask));
	}

	/// <summary>
	/// The links of mesh are what a new mesh resolves for the same tiles and links in one commit: the
	/// same ends, the same polygons by tile and index, and the same attach lists in every tile.
	/// </summary>
	static void AssertResolvedAsNew( NavMesh mesh, IEnumerable<Tile> tiles, SortedDictionary<int, NavLink> live )
	{
		var fresh = GridMesh.Create();
		fresh.CommitTiles( tiles.ToArray() );
		var ids = new Dictionary<int, int>();
		foreach ( var (id, link) in live ) ids[id] = fresh.AddLink( link );
		fresh.Commit();

		MeshState s = mesh.State, f = fresh.State;
		Assert.AreEqual( live.Count, s.Links().Count(), "Links in use" );
		foreach ( var (id, link) in live )
		{
			var a = s.LinkAt( id );
			var b = f.LinkAt( ids[id] );
			Assert.AreNotEqual( 0u, a.Serial, $"link {id}" );
			Assert.AreEqual( link, a.Def, $"link {id}" );
			Assert.AreEqual( b.StartPoint, a.StartPoint, $"link {id}" );
			Assert.AreEqual( b.EndPoint, a.EndPoint, $"link {id}" );
			Assert.AreEqual( Where( f, b.StartPoly ), Where( s, a.StartPoly ), $"link {id}" );
			Assert.AreEqual( Where( f, b.EndPoly ), Where( s, a.EndPoly ), $"link {id}" );
		}

		// Every tile attaches the connected links that leave its polygons, by polygon, then link id.
		Assert.AreEqual( f.TileCount, s.TileCount );
		foreach ( var tile in tiles )
		{
			var at = s.Find( tile.X, tile.Y )!;
			var meant = new List<(int Poly, int Link)>();
			foreach ( int id in live.Keys )
			{
				var entry = s.LinkAt( id );
				if ( !entry.Connected || entry.StartPoly == entry.EndPoly ) continue;
				if ( entry.StartPoly >> s.PolyBits == at.Slot ) meant.Add( ((int)(entry.StartPoly & s.PolyMask), id) );
				if ( entry.Def.Bidirectional && entry.EndPoly >> s.PolyBits == at.Slot ) meant.Add( ((int)(entry.EndPoly & s.PolyMask), id) );
			}
			var attached = at.Attach.Select( e => ((int)e.Poly, (int)e.Link) ).ToArray();
			CollectionAssert.AreEqual( meant.Order().ToArray(), attached, $"attach of tile {tile.X}, {tile.Y}" );
		}
	}

	static NavLink RandomLink( Random random, int side )
	{
		float[] radii = [4, 8, 30, 200, 700, 3000];
		Vector3 Point() => new( random.NextSingle() * side * GridMesh.TileWorld, random.NextSingle() * side * GridMesh.TileWorld, 1 );
		return new NavLink { Start = Point(), End = Point(), Radius = radii[random.Next( radii.Length )], Bidirectional = random.Next( 2 ) == 0, Area = 1 };
	}

	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	public void LinksResolveAsInANewMeshThroughEveryKindOfChange( int seed )
	{
		// Tiles built, rebuilt differently, removed and put back, links added, set and removed, ids
		// reused, over several pages, with links of every reach, a few too wide to list per tile.
		const int Side = 6;
		var random = new Random( seed );
		var mesh = GridMesh.Create();
		var tiles = new Dictionary<(int X, int Y), Tile>();
		for ( int y = 0; y < Side; y++ )
			for ( int x = 0; x < Side; x++ )
				tiles[(x, y)] = Variant( x, y, 0 );
		mesh.CommitTiles( tiles.Values.ToArray() );
		var live = new SortedDictionary<int, NavLink>();
		for ( int i = 0; i < 150; i++ )
		{
			var link = RandomLink( random, Side );
			live[mesh.AddLink( link )] = link;
		}
		mesh.Commit();
		AssertResolvedAsNew( mesh, tiles.Values, live );

		for ( int step = 0; step < 60; step++ )
		{
			var built = new List<Tile>();
			for ( int op = random.Next( 1, 5 ); op > 0; op-- )
			{
				int x = random.Next( Side ), y = random.Next( Side );
				switch ( random.Next( 6 ) )
				{
					case 0:
						var link = RandomLink( random, Side );
						int added = mesh.AddLink( link );
						Assert.IsFalse( live.ContainsKey( added ) );
						live[added] = link;
						break;
					case 1 when live.Count > 0:
						int set = live.Keys.ElementAt( random.Next( live.Count ) );
						live[set] = RandomLink( random, Side );
						mesh.SetLink( set, live[set] );
						break;
					case 2 when live.Count > 0:
						int removed = live.Keys.ElementAt( random.Next( live.Count ) );
						mesh.RemoveLink( removed );
						live.Remove( removed );
						break;
					case 3 when built.Count == 0:
						mesh.RemoveTile( x, y );
						tiles.Remove( (x, y) );
						break;
					default:
						if ( built.Any( t => t.X == x && t.Y == y ) ) break;
						var tile = Variant( x, y, random.Next( 5 ) );
						built.Add( tile );
						tiles[(x, y)] = tile;
						break;
				}
			}
			if ( built.Count > 0 ) mesh.CommitTiles( built.ToArray() );
			else mesh.Commit();
			AssertResolvedAsNew( mesh, tiles.Values, live );
		}
		Assert.IsTrue( live.Keys.Max() >= 2 * MeshState.LinkPageSize, "The links span several pages" );
	}

	[TestMethod]
	public void OlderStatesKeepReadingTheirOwnLinks()
	{
		var mesh = GridMesh.Mesh( Build( 0, 0 ), Build( 1, 0 ), Build( 2, 0 ) );
		for ( int i = 0; i < 3 * MeshState.LinkPageSize; i++ ) mesh.AddLink( LinkIn( i % 3, 0, i / 3 % 4 ) );
		mesh.Commit();
		var first = mesh.State;
		var kept = Enumerable.Range( 0, 3 * MeshState.LinkPageSize ).Select( id => first.LinkAt( id ) ).ToArray();

		// Set a link on the first page, remove one on the last, rebuild the tile of most of the links.
		mesh.SetLink( 1, LinkIn( 2, 0, 3 ) );
		mesh.RemoveLink( 3 * MeshState.LinkPageSize - 1 );
		mesh.Commit();
		var second = mesh.State;
		Assert.AreEqual( 0u, second.LinkAt( 3 * MeshState.LinkPageSize - 1 ).Serial );
		Assert.AreSame( first.LinkPages[1], second.LinkPages[1], "A page no link of the commit is on is shared" );
		Assert.AreNotSame( first.LinkPages[0], second.LinkPages[0] );
		mesh.CommitTiles( [Build( 0, 0 )] );
		mesh.AddLink( LinkIn( 1, 0, 0 ) );
		mesh.Commit();
		Assert.AreNotSame( second.LinkPages[1], mesh.State.LinkPages[1], "Links of the rebuilt tile are on every page" );

		for ( int id = 0; id < kept.Length; id++ )
		{
			var entry = first.LinkAt( id );
			Assert.AreEqual( kept[id].Serial, entry.Serial );
			Assert.AreEqual( kept[id].Def, entry.Def );
			Assert.AreEqual( kept[id].StartPoly, entry.StartPoly );
			Assert.AreEqual( kept[id].EndPoly, entry.EndPoly );
			Assert.AreEqual( kept[id].StartPoint, entry.StartPoint );
		}
		Assert.AreEqual( 0u, first.LinkAt( 3 * MeshState.LinkPageSize ).Serial, "An id past the pages of a state is free there" );
		Assert.AreNotEqual( 0u, mesh.State.LinkAt( 3 * MeshState.LinkPageSize - 1 ).Serial, "The freed id was taken again" );
	}

	[TestMethod]
	public void LinkIdsAtTheTopOfTheRange()
	{
		// Two floors 200 apart. Every id is taken by a link on the floor below, the last by a ladder up.
		var mesh = GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0 ), new GridMesh.Layer( 200 ) ) );
		var ground = new NavLink { Start = new Vector3( 100, 100, 1 ), End = new Vector3( 500, 100, 1 ), Radius = 8, Bidirectional = true, Area = 1 };
		var ladder = new NavLink { Start = new Vector3( 320, 320, 1 ), End = new Vector3( 320, 320, 201 ), Radius = 24, Bidirectional = true, Area = 1 };
		int top = PolyRef.MaxLinks - 1;
		for ( int id = 0; id < top; id++ ) Assert.AreEqual( id, mesh.AddLink( ground ) );
		Assert.AreEqual( top, mesh.AddLink( ladder ) );
		Assert.AreEqual( -1, mesh.AddLink( ground ) );
		mesh.Commit();

		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 below = new( 100, 320, 0 ), above = new( 500, 320, 200 );
		void Climbs()
		{
			Assert.AreEqual( PathStatus.Complete, query.FindPath( below, above, GridMesh.Snap, Filters.All, path ) );
			var link = path.Polys.ToArray().Single( p => p.IsLink );
			Assert.AreEqual( top, link.Link );
			Assert.IsTrue( query.IsValid( link, Filters.All ) );
			Assert.IsTrue( query.GetLink( link, below, out var entry, out var exit ) );
			Assert.AreEqual( new Vector3( 320, 320, 0 ), entry );
			Assert.AreEqual( new Vector3( 320, 320, 200 ), exit );
		}
		Climbs();
		Assert.IsTrue( mesh.GetLinkEnds( top, out _, out _, out bool startConnected, out bool endConnected ) && startConnected && endConnected );
		Assert.IsFalse( mesh.GetLinkEnds( top + 1, out _, out _, out _, out _ ) );
		Assert.IsFalse( mesh.GetLink( -1, out _, out _ ) );

		mesh.RemoveLink( top );
		mesh.Commit();
		Assert.IsFalse( mesh.GetLinkEnds( top, out _, out _, out _, out _ ) );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( below, above, GridMesh.Snap, Filters.All, path ) );
		Assert.AreEqual( top, mesh.AddLink( ladder ) );
		mesh.Commit();
		Climbs();
		mesh.CommitTiles( [GridMesh.Tile( 0, 0, new GridMesh.Layer( 0 ), new GridMesh.Layer( 200 ) )] );
		Climbs();
	}

	[TestMethod]
	public void LinksOnALoadedBakeResolveAsOnTheMeshBaked()
	{
		const int Side = 4;
		var random = new Random( 11 );
		var tiles = new List<Tile>();
		for ( int y = 0; y < Side; y++ )
			for ( int x = 0; x < Side; x++ )
				tiles.Add( Variant( x, y, random.Next( 5 ) ) );
		var mesh = GridMesh.Mesh( tiles.ToArray() );
		var writer = new System.Buffers.ArrayBufferWriter<byte>();
		Assert.IsTrue( mesh.Save( writer ) );
		var loaded = BakeTests.Load( writer.WrittenSpan )!;

		var live = new SortedDictionary<int, NavLink>();
		for ( int i = 0; i < 2 * MeshState.LinkPageSize + 5; i++ )
		{
			var link = RandomLink( random, Side );
			int id = loaded.AddLink( link );
			Assert.AreEqual( id, mesh.AddLink( link ) );
			live[id] = link;
		}
		mesh.Commit();
		loaded.Commit();
		AssertResolvedAsNew( loaded, tiles, live );
		AssertResolvedAsNew( mesh, tiles, live );

		// A rebuild on the loaded mesh resolves its links again like any other.
		tiles[5] = Variant( tiles[5].X, tiles[5].Y, 3 );
		loaded.CommitTiles( [tiles[5]] );
		AssertResolvedAsNew( loaded, tiles, live );
	}

	[TestMethod]
	public void ALinkEndSnapsOnlyToTheTilesOfItsSnapBox()
	{
		// What the lists of links per tile rely on. A start a radius east of the border touches the
		// edge of the west tile, but its box covers the east tile only, where it finds nothing. Half a
		// unit nearer, its box covers the west tile as well and it snaps there.
		var tiles = new[] { GridMesh.Quad( 0, 0 ), GridMesh.Quad( 1, 0, x0: 20 ) };
		var mesh = GridMesh.Mesh( tiles );
		var live = new SortedDictionary<int, NavLink>();
		foreach ( float x in new[] { GridMesh.TileWorld + 8, GridMesh.TileWorld + 7.5f } )
		{
			var link = new NavLink { Start = new Vector3( x, 320, 1 ), End = new Vector3( 1000, 320, 1 ), Radius = 8, Bidirectional = true, Area = 1 };
			live[mesh.AddLink( link )] = link;
		}
		mesh.Commit();
		var s = mesh.State;
		Assert.AreEqual( PolyRef.NoId, s.LinkAt( 0 ).StartPoly );
		Assert.AreEqual( (0, 0, 0), Where( s, s.LinkAt( 1 ).StartPoly ) );
		Assert.AreEqual( 1, s.Find( 0, 0 )!.Attach.Length );

		tiles[0] = GridMesh.Quad( 0, 0 );
		mesh.CommitTiles( [tiles[0]] );
		AssertResolvedAsNew( mesh, tiles, live );
		Assert.AreEqual( 1, mesh.State.Find( 0, 0 )!.Attach.Length );
	}
}
