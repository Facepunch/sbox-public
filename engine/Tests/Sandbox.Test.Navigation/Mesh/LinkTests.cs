using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

[TestClass]
public class LinkTests
{
	static readonly Vector3 Snap = GridMesh.Snap;

	// Two floors, 200 apart, not connected by polygons.
	static NavMesh TwoFloors() => GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0 ), new GridMesh.Layer( 200 ) ) );

	static NavLink Ladder( bool bidirectional = true ) => new() { Start = new Vector3( 320, 320, 1 ), End = new Vector3( 320, 320, 201 ), Radius = 24, Bidirectional = bidirectional, Area = 1 };

	[TestMethod]
	public void LinkNeedsACommitButNoJob()
	{
		// Review m2: AddLink on a clean mesh takes effect with the parameterless Commit.
		var mesh = TwoFloors();
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 below = new( 100, 320, 0 ), above = new( 500, 320, 200 );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( below, above, Snap, default, path ) );

		int id = mesh.AddLink( Ladder() );
		Assert.AreEqual( 0, id );
		Assert.IsFalse( mesh.GetLinkEnds( id, out _, out _, out _, out _ ), "Not resolved before the commit" );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( below, above, Snap, default, path ) );

		uint version = mesh.Version;
		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.AreEqual( 0, mesh.Commit() );
		Assert.AreEqual( version + 1, mesh.Version );
		Assert.IsTrue( mesh.GetLinkEnds( id, out var start, out var end, out bool startConnected, out bool endConnected ) );
		Assert.IsTrue( startConnected && endConnected );
		Assert.AreEqual( new Vector3( 320, 320, 0 ), start );
		Assert.AreEqual( new Vector3( 320, 320, 200 ), end );

		Assert.AreEqual( PathStatus.Complete, query.FindPath( below, above, Snap, default, path ) );
		Assert.AreEqual( 220 + 200 + 180, path.Length, 0.01f );
		Assert.AreEqual( 4, path.Corners.Length );
		Assert.AreEqual( start, path.Corners[1] );
		Assert.AreEqual( end, path.Corners[2] );

		// The link is one corridor entry between the polygons it joins.
		int at = -1;
		for ( int i = 0; i < path.Polys.Length; i++ ) if ( path.Polys[i].IsLink ) at = i;
		Assert.IsTrue( at > 0 && at < path.Polys.Length - 1 );
		Assert.AreEqual( id, path.Polys[at].Link );
		Assert.IsTrue( query.IsValid( path.Polys[at - 1], default ) && query.IsValid( path.Polys[at + 1], default ) );
		Assert.IsTrue( query.IsValid( path.Polys[at], default ), "A link in a corridor is as valid as the polygons around it" );
	}

	[TestMethod]
	public void BidirectionalLinkIsWalkedBothWaysAndOneWayOnlyForward()
	{
		foreach ( bool bidirectional in new[] { true, false } )
		{
			var mesh = TwoFloors();
			mesh.AddLink( Ladder( bidirectional ) );
			mesh.Commit();
			var query = new NavQuery( mesh );
			var path = new NavPath();
			Vector3 below = new( 100, 320, 0 ), above = new( 500, 320, 200 );
			Assert.AreEqual( PathStatus.Complete, query.FindPath( below, above, Snap, default, path ) );
			Assert.AreEqual( bidirectional ? PathStatus.Complete : PathStatus.Partial, query.FindPath( above, below, Snap, default, path ) );
			if ( bidirectional ) Assert.AreEqual( new Vector3( 320, 320, 200 ), path.Corners[1], "Walking back enters the link at its end" );
		}
	}

	[TestMethod]
	public void OneWayLinkThatIsTheOnlyConnectionIsStillTaken()
	{
		// Review m4 proposed that one-way links do not join islands. Then the goal behind such a link
		// counts as unreachable from the right side as well, so they do join. From the wrong side the
		// search finds no way, after looking through the far island.
		var mesh = TwoFloors();
		mesh.AddLink( Ladder( bidirectional: false ) );
		mesh.Commit();
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 105, 325, 0 ), new Vector3( 505, 325, 200 ), Snap, default, path ) );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( new Vector3( 505, 325, 200 ), new Vector3( 105, 325, 0 ), Snap, default, path ) );
		Assert.AreEqual( 200f, path.End.Z, "The partial path stays on the upper floor" );
		Assert.IsFalse( path.Polys.ToArray().Any( p => p.IsLink ) );
	}

	[TestMethod]
	public void LinkAreaIsFilteredAndCosted()
	{
		var mesh = TwoFloors();
		var link = Ladder();
		link.Area = 5;
		mesh.AddLink( link );
		mesh.Commit();
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 below = new( 100, 320, 0 ), above = new( 500, 320, 200 );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( below, above, Snap, new NavFilter( 1ul << 5 ), path ) );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( below, above, Snap, default, path ) );
	}

	[TestMethod]
	public void SetLinkAndRemoveLinkTakeEffectAtCommit()
	{
		var mesh = TwoFloors();
		int id = mesh.AddLink( Ladder() );
		mesh.Commit();
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Vector3 below = new( 100, 320, 0 ), above = new( 500, 320, 200 );
		query.FindPath( below, above, Snap, default, path );
		var linkRef = path.Polys.ToArray().Single( p => p.IsLink );
		Assert.IsTrue( query.GetLink( linkRef, below, out var entry, out var exit ) );
		Assert.AreEqual( 0f, entry.Z );
		Assert.AreEqual( 200f, exit.Z );
		Assert.IsTrue( query.GetLink( linkRef, above, out entry, out _ ) );
		Assert.AreEqual( 200f, entry.Z, "Seen from above the link is entered at the top" );

		var moved = Ladder();
		moved.Start = new Vector3( 120, 320, 1 );
		moved.End = new Vector3( 120, 320, 201 );
		mesh.SetLink( id, moved );
		Assert.IsTrue( query.GetLink( linkRef, below, out _, out _ ), "Nothing changes before the commit" );
		mesh.Commit();
		Assert.IsFalse( query.GetLink( linkRef, below, out _, out _ ), "A changed link invalidates references to it" );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( below, above, Snap, default, path ) );
		Assert.AreEqual( 20 + 200 + 380, path.Length, 0.01f );

		mesh.RemoveLink( id );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( below, above, Snap, default, path ) );
		mesh.Commit();
		Assert.AreEqual( PathStatus.Partial, query.FindPath( below, above, Snap, default, path ) );
		Assert.IsFalse( mesh.GetLinkEnds( id, out _, out _, out _, out _ ) );
		Assert.AreEqual( 0, mesh.State.Find( 0, 0 )!.Attach.Length );
		Assert.AreEqual( id, mesh.AddLink( Ladder() ), "A freed id is reused" );
		Assert.ThrowsException<ArgumentException>( () => mesh.SetLink( 77, Ladder() ) );
	}

	[TestMethod]
	public void LinkEndOffTheMeshStaysUnconnected()
	{
		var mesh = TwoFloors();
		var link = Ladder();
		link.End = new Vector3( 320, 320, 400 );
		int id = mesh.AddLink( link );
		mesh.Commit();
		Assert.IsTrue( mesh.GetLinkEnds( id, out var start, out var end, out bool startConnected, out bool endConnected ) );
		Assert.IsTrue( startConnected );
		Assert.IsFalse( endConnected );
		Assert.AreEqual( new Vector3( 320, 320, 0 ), start );
		Assert.AreEqual( link.End, end );
		Assert.AreEqual( 0, mesh.State.Find( 0, 0 )!.Attach.Length );

		// It connects once the mesh grows under it.
		mesh.CommitTiles( [GridMesh.Tile( 0, 0, new GridMesh.Layer( 0 ), new GridMesh.Layer( 400 ) )] );
		Assert.IsTrue( mesh.GetLinkEnds( id, out _, out end, out _, out endConnected ) && endConnected );
		Assert.AreEqual( 400f, end.Z );
	}

	[TestMethod]
	public void LinkIsResolvedAgainWhenItsPolygonsTileIsRebuilt()
	{
		// Review B1: the link end lies 2 units inside tile A, but the nearest polygon is across the
		// border in B, because A has no polygons there. B is rebuilt: the link must not keep an id into
		// B's old polygon array.
		var a = GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, ( x, y ) => x < 60 ) );
		var mesh = GridMesh.Mesh( a, GridMesh.Flat( 1, 0 ), GridMesh.Tile( 0, 1, new GridMesh.Layer( 200 ) ) );
		int id = mesh.AddLink( new NavLink { Start = new Vector3( 638, 320, 0 ), End = new Vector3( 320, 960, 200 ), Radius = 16, Bidirectional = true, Area = 1 } );
		mesh.Commit();
		Assert.IsTrue( mesh.GetLinkEnds( id, out var start, out _, out bool connected, out _ ) && connected );
		Assert.AreEqual( 640f, start.X, "The start snapped across the border into the east tile" );
		var before = mesh.State.Links[id];
		Assert.AreEqual( mesh.State.Find( 1, 0 )!.Slot, (int)(before.StartPoly >> PolyRef.PolyBits) );

		// The rebuilt tile has other polygons: the old polygon index means something else, or nothing.
		mesh.CommitTiles( [GridMesh.Tile( 1, 0, new GridMesh.Layer( 0, ( x, y ) => y is >= 30 and < 34 ) )] );
		var after = mesh.State.Links[id];
		var east = mesh.State.Find( 1, 0 )!;
		Assert.IsTrue( after.Connected );
		Assert.AreNotEqual( before.Serial, after.Serial, "The link changed with its tile" );
		Assert.IsTrue( (int)(after.StartPoly & PolyRef.PolyMask) < east.Polys.Length );
		Assert.AreEqual( 1, east.Attach.Length );
		Assert.AreEqual( (int)(after.StartPoly & PolyRef.PolyMask), east.Attach[0].Poly );

		var query = new NavQuery( mesh );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 900, 320, 0 ), new Vector3( 320, 1000, 200 ), GridMesh.Snap, default, path ) );

		// Removing the tile disconnects the link, bringing it back connects it again.
		mesh.RemoveTile( 1, 0 );
		mesh.Commit();
		Assert.IsFalse( mesh.State.Links[id].Connected );
		Assert.AreEqual( 0, mesh.State.Find( 0, 1 )!.Attach.Length );
		mesh.CommitTiles( [GridMesh.Flat( 1, 0 )] );
		Assert.IsTrue( mesh.State.Links[id].Connected );
		Assert.AreEqual( 1, mesh.State.Find( 0, 1 )!.Attach.Length );
	}

	[TestMethod]
	public void LinkFarFromACommitKeepsItsSerial()
	{
		var mesh = GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0 ), new GridMesh.Layer( 200 ) ), GridMesh.Flat( 1, 0 ), GridMesh.Flat( 2, 0 ) );
		int id = mesh.AddLink( Ladder() );
		mesh.Commit();
		var query = new NavQuery( mesh );
		var path = new NavPath();
		query.FindPath( new Vector3( 100, 320, 0 ), new Vector3( 500, 320, 200 ), Snap, default, path );
		var linkRef = path.Polys.ToArray().Single( p => p.IsLink );
		uint serial = mesh.State.Links[id].Serial;

		// The neighbour changes: the link's tile gets a new object, the link stays as it is.
		mesh.CommitTiles( [GridMesh.Flat( 1, 0 )] );
		Assert.AreEqual( serial, mesh.State.Links[id].Serial );
		Assert.AreEqual( 2, mesh.State.Find( 0, 0 )!.Attach.Length, "The attach table survives the relink" );
		Assert.IsTrue( query.GetLink( linkRef, Vector3.Zero, out _, out _ ) );

		mesh.CommitTiles( [GridMesh.Flat( 2, 0 )] );
		Assert.AreEqual( serial, mesh.State.Links[id].Serial );
	}

	[TestMethod]
	public void LinksSpanAnyNumberOfTiles()
	{
		var mesh = GridMesh.Mesh( (0, 0), (5, 0) );
		mesh.AddLink( new NavLink { Start = new Vector3( 600, 320, 0 ), End = new Vector3( 5 * 640 + 40, 320, 0 ), Radius = 16, Bidirectional = false, Area = 1 } );
		mesh.Commit();
		var query = new NavQuery( mesh );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 100, 320, 0 ), new Vector3( 5 * 640 + 300, 320, 0 ), Snap, default, path ) );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( new Vector3( 5 * 640 + 300, 320, 0 ), new Vector3( 100, 320, 0 ), Snap, default, path ) );
	}

	[TestMethod]
	public void AddLinkReturnsMinusOneWhenNoIdIsFree()
	{
		var mesh = GridMesh.Create();
		for ( int i = 0; i < PolyRef.MaxLinks; i++ ) Assert.AreEqual( i, mesh.AddLink( Ladder() ) );
		Assert.AreEqual( -1, mesh.AddLink( Ladder() ) );
		mesh.RemoveLink( 100 );
		Assert.AreEqual( 100, mesh.AddLink( Ladder() ) );
		Assert.AreEqual( -1, mesh.AddLink( Ladder() ) );
		Assert.AreEqual( 0, mesh.Commit() );
	}

	[TestMethod]
	public void LinksCompareByValue()
	{
		Assert.AreEqual( Ladder(), Ladder() );
		Assert.IsTrue( Ladder() == Ladder() );
		Assert.IsTrue( Ladder() != Ladder( false ) );
		Assert.AreNotEqual( Ladder(), Ladder() with { Radius = 25 } );
		Assert.AreNotEqual( Ladder(), Ladder() with { Area = 2 } );
		Assert.AreNotEqual( Ladder(), Ladder() with { Start = new Vector3( 320, 320, 2 ) } );
		Assert.AreNotEqual( Ladder(), Ladder() with { End = new Vector3( 320, 321, 201 ) } );
	}
}
