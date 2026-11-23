using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// Island maps in blocks of 8 x 8 tiles: what a map says against a union over every polygon, and which
/// blocks the map after a commit shares with the one before.
/// </summary>
[TestClass]
public class IslandBlockTests
{
	static readonly NavFilter Open = Filters.All;

	static NavMesh Grid( int x0, int y0, int x1, int y1, Func<int, int, Tile>? make = null )
	{
		var tiles = new List<Tile>();
		for ( int y = y0; y < y1; y++ )
			for ( int x = x0; x < x1; x++ )
				tiles.Add( make?.Invoke( x, y ) ?? GridMesh.Quad( x, y ) );
		return GridMesh.Mesh( tiles.ToArray() );
	}

	// The polygon at the middle of a tile.
	static uint At( NavMesh mesh, int x, int y )
	{
		var tile = mesh.State.Find( x, y )!;
		return mesh.State.IdOf( tile, 0 );
	}

	static Vector3 Middle( int x, int y ) => new( (x + 0.5f) * GridMesh.TileWorld, (y + 0.5f) * GridMesh.TileWorld, 0 );

	static IslandMap Map( NavMesh mesh, in NavFilter filter )
	{
		var map = NavQuery.GetIslands( mesh.State, filter );
		AssertMatchesFlood( mesh.State, filter, map );
		return map;
	}

	/// <summary>
	/// The reference: one union-find over every polygon, through the edges inside tiles, the connections
	/// between tiles and the links the filter lets pass.
	/// </summary>
	static void AssertMatchesFlood( MeshState s, in NavFilter filter, IslandMap map )
	{
		var ids = new List<uint>();
		var at = new Dictionary<uint, int>();
		foreach ( var page in s.Pages )
		{
			if ( page == null ) continue;
			foreach ( var tile in page )
				for ( int p = 0; tile != null && p < tile.Polys.Length; p++ )
				{
					uint id = s.IdOf( tile, p );
					at[id] = ids.Count;
					ids.Add( id );
				}
		}
		var parent = Enumerable.Range( 0, ids.Count ).ToArray();
		int Root( int i )
		{
			while ( parent[i] != i ) i = parent[i] = parent[parent[i]];
			return i;
		}
		var f = filter;
		bool Allowed( uint id ) => f.Allows( s.Get( id ).Polys[id & s.PolyMask].Area );
		void Join( uint a, uint b )
		{
			if ( Allowed( a ) && Allowed( b ) ) parent[Root( at[a] )] = Root( at[b] );
		}
		foreach ( uint id in ids )
		{
			var tile = s.Get( id );
			ref readonly var poly = ref tile.Polys[id & s.PolyMask];
			for ( int j = 0; j < poly.VertCount; j++ )
				if ( poly.Neis[j] < Poly.Border ) Join( id, id & ~s.PolyMask | poly.Neis[j] );
			foreach ( var link in tile.Ext )
				if ( link.Poly == (id & s.PolyMask) ) Join( id, link.Target );
		}
		foreach ( var link in s.Links() )
			if ( link.Serial != 0 && link.Connected && filter.Allows( link.Def.Area ) && filter.AllowsLink( link.Def.Type ) ) Join( link.StartPoly, link.EndPoly );

		var labelOfRoot = new Dictionary<int, int>();
		var rootOfLabel = new Dictionary<int, int>();
		foreach ( uint id in ids )
		{
			int label = map.Of( id );
			if ( !Allowed( id ) )
			{
				Assert.AreEqual( -1, label, "A forbidden polygon has a label" );
				continue;
			}
			int root = Root( at[id] );
			if ( labelOfRoot.TryGetValue( root, out int known ) ) Assert.AreEqual( known, label, "Two polygons of one island have different labels" );
			else labelOfRoot[root] = label;
			if ( rootOfLabel.TryGetValue( label, out int knownRoot ) ) Assert.AreEqual( knownRoot, root, "Two islands share a label" );
			else rootOfLabel[label] = root;
		}
	}

	[TestMethod]
	public void IslandsReachAcrossBlockBorders()
	{
		// Three blocks wide, from -8 so that negative block coordinates take part.
		var mesh = Grid( -8, 0, 12, 3 );
		var map = Map( mesh, Open );
		Assert.AreEqual( map.Of( At( mesh, -8, 0 ) ), map.Of( At( mesh, 11, 2 ) ) );
		Assert.AreNotSame( map.BlockOf( -1, 0 ), map.BlockOf( 0, 0 ) );
		Assert.AreSame( map.BlockOf( 0, 0 ), map.BlockOf( 7, 2 ) );

		// A column that does not reach its east border splits the grid where two blocks meet.
		var cut = Grid( -8, 0, 12, 3, ( x, y ) => x == 7 ? GridMesh.Quad( x, y, x1: 60 ) : GridMesh.Quad( x, y ) );
		map = Map( cut, Open );
		Assert.AreEqual( map.Of( At( cut, -8, 0 ) ), map.Of( At( cut, 7, 2 ) ) );
		Assert.AreNotEqual( map.Of( At( cut, 7, 0 ) ), map.Of( At( cut, 8, 0 ) ) );
	}

	[TestMethod]
	public void ACommitAtABlockBorderSplitsAndJoinsIslands()
	{
		var mesh = Grid( 0, 0, 16, 16 );
		var query = new NavQuery( mesh );
		var path = new NavPath();
		var map = Map( mesh, Open );
		Assert.AreEqual( map.Of( At( mesh, 0, 0 ) ), map.Of( At( mesh, 0, 15 ) ) );

		// The row below the block border no longer reaches north.
		var row = Enumerable.Range( 0, 16 ).Select( x => GridMesh.Quad( x, 7, y1: 60 ) ).ToArray();
		mesh.CommitTiles( row );
		map = Map( mesh, Open );
		Assert.AreNotEqual( map.Of( At( mesh, 0, 0 ) ), map.Of( At( mesh, 0, 15 ) ) );
		Assert.AreEqual( map.Of( At( mesh, 0, 0 ) ), map.Of( At( mesh, 15, 7 ) ) );
		Assert.AreEqual( map.Of( At( mesh, 0, 8 ) ), map.Of( At( mesh, 15, 15 ) ) );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( Middle( 0, 0 ), Middle( 15, 15 ), GridMesh.Snap, Open, path ) );

		// One tile of the row opens again and joins the halves.
		mesh.CommitTiles( [GridMesh.Quad( 9, 7 )] );
		map = Map( mesh, Open );
		Assert.AreEqual( map.Of( At( mesh, 0, 0 ) ), map.Of( At( mesh, 0, 15 ) ) );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( Middle( 0, 0 ), Middle( 15, 15 ), GridMesh.Snap, Open, path ) );
	}

	[TestMethod]
	public void LinksJoinIslandsInDifferentBlocks()
	{
		const byte Jump = 3;
		var mesh = GridMesh.Mesh( GridMesh.Quad( 0, 0 ), GridMesh.Quad( 1, 0 ), GridMesh.Quad( 20, 3 ), GridMesh.Quad( 21, 3 ) );
		var map = Map( mesh, Open );
		Assert.AreNotEqual( map.Of( At( mesh, 0, 0 ) ), map.Of( At( mesh, 21, 3 ) ) );

		// One way only, and still one island.
		int link = mesh.AddLink( new NavLink { Start = Middle( 1, 0 ), End = Middle( 20, 3 ), Radius = 16, Bidirectional = false, Area = 2, Type = Jump } );
		mesh.Commit();
		map = Map( mesh, Open );
		Assert.AreEqual( map.Of( At( mesh, 0, 0 ) ), map.Of( At( mesh, 21, 3 ) ) );
		var barred = Map( mesh, Filters.All.WithForbiddenLinks( 1ul << Jump ) );
		Assert.AreNotEqual( barred.Of( At( mesh, 0, 0 ) ), barred.Of( At( mesh, 21, 3 ) ) );
		Assert.AreSame( map.BlockOf( 0, 0 ), barred.BlockOf( 0, 0 ), "Maps of one area mask on one state share every block" );
		var noArea = Map( mesh, Filters.All.WithForbiddenAreas( 1ul << 2 ) );
		Assert.AreNotEqual( noArea.Of( At( mesh, 0, 0 ) ), noArea.Of( At( mesh, 21, 3 ) ) );

		mesh.RemoveLink( link );
		mesh.Commit();
		map = Map( mesh, Open );
		Assert.AreNotEqual( map.Of( At( mesh, 0, 0 ) ), map.Of( At( mesh, 21, 3 ) ) );
	}

	[TestMethod]
	public void RemovingTilesSplitsIslandsAndEmptiesBlocks()
	{
		var mesh = Grid( 0, 0, 24, 2 );
		var map = Map( mesh, Open );
		Assert.AreEqual( map.Of( At( mesh, 0, 0 ) ), map.Of( At( mesh, 23, 1 ) ) );

		mesh.RemoveTile( 8, 0 );
		mesh.RemoveTile( 8, 1 );
		mesh.Commit();
		map = Map( mesh, Open );
		Assert.AreNotEqual( map.Of( At( mesh, 7, 0 ) ), map.Of( At( mesh, 9, 0 ) ) );

		// The middle block loses every tile: it stays, empty, in the maps that follow.
		for ( int x = 9; x < 16; x++ )
		{
			mesh.RemoveTile( x, 0 );
			mesh.RemoveTile( x, 1 );
		}
		mesh.Commit();
		map = Map( mesh, Open );
		Assert.IsTrue( map.BlockOf( 8, 0 )!.Empty );
		Assert.AreNotEqual( map.Of( At( mesh, 7, 0 ) ), map.Of( At( mesh, 16, 0 ) ) );

		// With two of three blocks empty the next map is built whole and drops them.
		for ( int x = 16; x < 24; x++ )
		{
			mesh.RemoveTile( x, 0 );
			mesh.RemoveTile( x, 1 );
		}
		mesh.Commit();
		map = Map( mesh, Open );
		Assert.IsTrue( map.BlockOf( 16, 0 )!.Empty );
		mesh.CommitTiles( [GridMesh.Quad( 3, 0 )] );
		map = Map( mesh, Open );
		Assert.IsNull( map.BlockOf( 16, 0 ) );
		Assert.IsNull( map.BlockOf( 8, 0 ) );

		mesh.CommitTiles( Enumerable.Range( 8, 16 ).Select( x => GridMesh.Quad( x, 0 ) ).ToArray() );
		map = Map( mesh, Open );
		Assert.AreEqual( map.Of( At( mesh, 0, 0 ) ), map.Of( At( mesh, 23, 0 ) ) );
	}

	[TestMethod]
	public void EveryAreaMaskKeepsItsOwnBlocks()
	{
		// Areas 1 to 6 in diagonal stripes, so every mask cuts the grid differently.
		var mesh = Grid( 0, 0, 20, 20, ( x, y ) => GridMesh.Quad( x, y, area: (byte)(1 + (x + y) % 6) ) );
		var filters = Enumerable.Range( 0, 12 ).Select( i => Filters.All.WithForbiddenAreas( (ulong)i * 0x2A % 128 & ~1ul ) ).Distinct().ToArray();
		var before = filters.Select( filter => Map( mesh, filter ) ).ToArray();
		mesh.CommitTiles( [GridMesh.Quad( 3, 3, area: 4 ), GridMesh.Quad( 12, 12, area: 1 )] );
		for ( int i = 0; i < filters.Length; i++ )
		{
			var after = Map( mesh, filters[i] );
			Assert.AreNotSame( before[i], after );
			Assert.AreSame( before[i].BlockOf( 19, 0 ), after.BlockOf( 19, 0 ), "A block no commit touched is rebuilt" );
			Assert.AreNotSame( before[i].BlockOf( 3, 3 ), after.BlockOf( 3, 3 ) );
			Assert.AreNotSame( before[i].BlockOf( 12, 12 ), after.BlockOf( 12, 12 ) );
			if ( i > 0 ) Assert.AreNotSame( after.BlockOf( 0, 0 ), Map( mesh, filters[0] ).BlockOf( 0, 0 ), "Two area masks share a block" );
		}
	}

	[TestMethod]
	public void AnUnchangedBlockIsSharedWithTheNextMap()
	{
		var mesh = Grid( 0, 0, 24, 16 );
		var first = Map( mesh, Open );

		// Inside the first block: the tile and its four neighbours are all in it.
		mesh.CommitTiles( [GridMesh.Quad( 3, 3 )] );
		var second = Map( mesh, Open );
		Assert.AreNotSame( first.BlockOf( 3, 3 ), second.BlockOf( 3, 3 ) );
		foreach ( var (x, y) in new[] { (8, 0), (16, 0), (0, 8), (8, 8), (16, 8) } )
			Assert.AreSame( first.BlockOf( x, y ), second.BlockOf( x, y ), $"Block of tile {x}, {y}" );

		// On the block border: its east neighbour gets a new tile object, so that block is rebuilt too.
		mesh.CommitTiles( [GridMesh.Quad( 7, 3 )] );
		var third = Map( mesh, Open );
		Assert.AreNotSame( second.BlockOf( 0, 0 ), third.BlockOf( 0, 0 ) );
		Assert.AreNotSame( second.BlockOf( 8, 0 ), third.BlockOf( 8, 0 ) );
		Assert.AreSame( second.BlockOf( 16, 0 ), third.BlockOf( 16, 0 ) );
		Assert.AreSame( second.BlockOf( 0, 8 ), third.BlockOf( 0, 8 ) );

		// A state whose map is built after a newer one's starts from the newer map.
		mesh.CommitTiles( [GridMesh.Quad( 20, 12 )] );
		var older = mesh.State;
		mesh.CommitTiles( [GridMesh.Quad( 3, 12 )] );
		var newer = Map( mesh, Open );
		var map = NavQuery.GetIslands( older, Open );
		AssertMatchesFlood( older, Open, map );
		Assert.AreSame( newer.BlockOf( 20, 12 ), map.BlockOf( 20, 12 ) );
		Assert.AreSame( newer.BlockOf( 16, 0 ), map.BlockOf( 16, 0 ) );
		Assert.AreNotSame( newer.BlockOf( 3, 12 ), map.BlockOf( 3, 12 ) );
	}

	[TestMethod]
	public void MapsAfterRandomCommitsMatchAFloodOverEveryPolygon()
	{
		var random = new Random( 11 );
		Tile Variant( int x, int y ) => random.Next( 8 ) switch
		{
			0 => GridMesh.Quad( x, y, x1: 60 ),
			1 => GridMesh.Quad( x, y, y0: 4 ),
			2 => GridMesh.Rects( x, y, [0, 30, 34, 64], [0, 64], ( i, j ) => i != 1 ),
			3 => GridMesh.Rects( x, y, [0, 64], [0, 30, 34, 64], ( i, j ) => j != 1 ),
			4 => GridMesh.Quad( x, y, area: 2 ),
			5 => GridMesh.Quad( x, y, area: 4 ),
			_ => GridMesh.Quad( x, y ),
		};
		const int X0 = -6, Y0 = -5, X1 = 14, Y1 = 9;
		var mesh = Grid( X0, Y0, X1, Y1, Variant );
		var filters = new[] { Open, Filters.All.WithForbiddenAreas( 1ul << 2 ), Filters.All.WithForbiddenLinks( 1ul << 3 ), Filters.All.WithForbiddenAreas( 1ul << 2 | 1ul << 4 ).WithForbiddenLinks( 1ul << 3 ) };
		// Links between random tiles, so their ends sit in every block and their ids fill several pages.
		NavLink RandomLink( int x, int y ) => new() { Start = Middle( x, y ), End = Middle( random.Next( X0, X1 ), random.Next( Y0, Y1 ) ), Radius = 16, Bidirectional = random.Next( 2 ) == 0, Area = (byte)(random.Next( 2 ) == 0 ? 1 : 2), Type = (byte)random.Next( 2, 4 ) };
		var links = new List<int>();
		for ( int i = 0; i < 5 * MeshState.LinkPageSize; i++ ) links.Add( mesh.AddLink( RandomLink( random.Next( X0, X1 ), random.Next( Y0, Y1 ) ) ) );
		mesh.Commit();
		MeshState? pending = null;
		var pendingFilter = Open;

		for ( int round = 0; round < 80; round++ )
		{
			var batch = new List<Tile>();
			for ( int change = random.Next( 1, 6 ); change > 0; change-- )
			{
				int x = random.Next( X0 - 2, X1 + 2 ), y = random.Next( Y0 - 2, Y1 + 2 );
				int kind = random.Next( 12 );
				if ( kind < 6 ) batch.Add( Variant( x, y ) );
				else if ( kind < 8 ) mesh.RemoveTile( x, y );
				else if ( kind < 9 ) links.Add( mesh.AddLink( RandomLink( x, y ) ) );
				else if ( kind < 10 ) mesh.SetLink( links[random.Next( links.Count )], RandomLink( x, y ) );
				else
				{
					// The lowest free id goes to the next link added, often in a page no other change touches.
					int index = random.Next( links.Count );
					mesh.RemoveLink( links[index] );
					links.RemoveAt( index );
				}
			}
			if ( batch.Count > 0 ) mesh.CommitTiles( batch.ToArray() );
			else mesh.Commit();

			for ( int i = 0; i < 3; i++ ) Map( mesh, filters[(round + i) % 4] );
			// Some states get their first map only after newer states have theirs.
			if ( pending != null ) AssertMatchesFlood( pending, pendingFilter, NavQuery.GetIslands( pending, pendingFilter ) );
			pending = round % 3 == 0 ? mesh.State : null;
			pendingFilter = filters[(round + 3) % 4];
		}
	}

	[TestMethod]
	public void ALinkKeepsJoiningWhileTheBlocksOfItsEndsAreRebuilt()
	{
		// Tiles 0 and 5 of the first block are islands of their own, 5 joined to 20 far away by a link.
		var mesh = GridMesh.Mesh( GridMesh.Quad( 0, 0 ), GridMesh.Quad( 5, 0 ), GridMesh.Quad( 20, 0 ), GridMesh.Quad( 21, 0 ) );
		int link = mesh.AddLink( new NavLink { Start = Middle( 5, 0 ), End = Middle( 20, 0 ), Radius = 16, Bidirectional = false, Area = 1 } );
		mesh.Commit();
		var map = Map( mesh, Open );
		Assert.AreEqual( map.Of( At( mesh, 5, 0 ) ), map.Of( At( mesh, 21, 0 ) ) );
		var page = mesh.State.LinkPages[link >> MeshState.LinkPageBits];

		// Tile 0 splits in two, so every label after it in the block moves up by one, and tile 4 appears
		// beside 5, which gets a new tile object. The link keeps its entry.
		mesh.CommitTiles( [GridMesh.Rects( 0, 0, [0, 30, 34, 64], [0, 64], ( i, j ) => i != 1 ), GridMesh.Quad( 4, 0, x1: 60 )] );
		var after = Map( mesh, Open );
		Assert.AreSame( page, mesh.State.LinkPages[link >> MeshState.LinkPageBits], "The link was resolved again" );
		Assert.AreNotSame( map.BlockOf( 0, 0 ), after.BlockOf( 0, 0 ) );
		Assert.AreEqual( after.Of( At( mesh, 5, 0 ) ), after.Of( At( mesh, 21, 0 ) ) );
		Assert.AreNotEqual( after.Of( At( mesh, 0, 0 ) ), after.Of( At( mesh, 21, 0 ) ) );
		Assert.AreNotEqual( after.Of( At( mesh, 4, 0 ) ), after.Of( At( mesh, 21, 0 ) ) );

		// The far end's block is rebuilt now.
		mesh.CommitTiles( [GridMesh.Quad( 17, 0 ), GridMesh.Quad( 16, 0, x0: 4 )] );
		after = Map( mesh, Open );
		Assert.AreSame( page, mesh.State.LinkPages[link >> MeshState.LinkPageBits], "The link was resolved again" );
		Assert.AreEqual( after.Of( At( mesh, 5, 0 ) ), after.Of( At( mesh, 21, 0 ) ) );
		Assert.AreNotEqual( after.Of( At( mesh, 17, 0 ) ), after.Of( At( mesh, 21, 0 ) ) );
	}

	[TestMethod]
	public void IslandMapAfterAOneTileCommitAmongThirtyThousandLinksAllocatesFarLessThanAWholeOne()
	{
		var mesh = CommitMeasurement.ManyLinks( CommitMeasurement.LinkSide, CommitMeasurement.LinksPerTile, CommitMeasurement.LinkAcross );
		int at = CommitMeasurement.LinkSide / 2;
		IslandMap? previous = null;
		for ( int i = 0; i < 2; i++ )
		{
			mesh.CommitTiles( [CommitMeasurement.Build( at, at )] );
			previous = NavQuery.GetIslands( mesh.State, Open );
		}

		mesh.CommitTiles( [CommitMeasurement.Build( at, at )] );
		long before = GC.GetAllocatedBytesForCurrentThread();
		var map = NavQuery.GetIslands( mesh.State, Open );
		long after = GC.GetAllocatedBytesForCurrentThread() - before;

		// The links of the rebuilt tile moved in their block's list; every other block keeps its list.
		Assert.AreNotEqual( previous!.LinksOf( at, at ), map.LinksOf( at, at ) );
		Assert.AreEqual( previous.LinksOf( 0, 0 ), map.LinksOf( 0, 0 ) );
		Assert.AreEqual( previous.LinksOf( at + IslandBlock.Size, at ), map.LinksOf( at + IslandBlock.Size, at ) );
		Assert.AreNotEqual( 0, map.LinksOf( 0, 0 ).Count );

		before = GC.GetAllocatedBytesForCurrentThread();
		_ = new IslandMap( mesh.State, 0, 0, null );
		long whole = GC.GetAllocatedBytesForCurrentThread() - before;
		AssertMatchesFlood( mesh.State, Open, map );
		Console.WriteLine( $"{mesh.State.Links().Count()} links: the island map after a one-tile commit allocated {after:N0} bytes, a whole map {whole:N0}" );
		Assert.IsTrue( after * 10 < whole, $"The map allocated {after:N0} bytes against {whole:N0} for a whole map" );
	}

	[TestMethod]
	public void FirstLongSearchAfterAOneTileCommitAllocatesFarLessThanAFullMap()
	{
		// An isolated tile in the far corner: a search for it asks for the island map.
		const int Side = 96;
		var mesh = Grid( 0, 0, Side, Side, ( x, y ) => x == Side - 1 && y == Side - 1 ? GridMesh.Quad( x, y, 2, 2, 62, 62 ) : GridMesh.Quad( x, y ) );
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 from = Middle( 0, 0 ), to = Middle( Side - 1, Side - 1 );
		for ( int i = 0; i < 2; i++ )
		{
			mesh.CommitTiles( [GridMesh.Quad( 40, 44 )] );
			Assert.AreEqual( PathStatus.Partial, query.FindPath( from, to, GridMesh.Snap, Open, path ) );
		}

		mesh.CommitTiles( [GridMesh.Quad( 40, 44 )] );
		long before = GC.GetAllocatedBytesForCurrentThread();
		Assert.AreEqual( PathStatus.Partial, query.FindPath( from, to, GridMesh.Snap, Open, path ) );
		long search = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.IsNotNull( mesh.State.Islands, "The search did not ask for islands" );

		before = GC.GetAllocatedBytesForCurrentThread();
		var whole = new IslandMap( mesh.State, 0, 0, null );
		long full = GC.GetAllocatedBytesForCurrentThread() - before;
		AssertMatchesFlood( mesh.State, Open, whole );
		Console.WriteLine( $"first long search after a one-tile commit allocated {search} bytes, a whole map {full}" );
		Assert.IsTrue( search * 10 < full, $"The search allocated {search} bytes against {full} for a whole map" );
	}
}
