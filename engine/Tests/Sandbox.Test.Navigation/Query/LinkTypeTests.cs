using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// Link types: what a filter forbids, and what it adds to and multiplies the cost of a link by.
/// </summary>
[TestClass]
public class LinkTypeTests
{
	static readonly Vector3 Snap = GridMesh.Snap;

	const byte Jump = 3;

	// A ridge across the tile: the ground climbs 3 in 4 from both sides to x = 320. Along a row the
	// search's cost of a walk is exact, every portal midpoint lies on it: from West to East it is
	// 537.5 over the top. A link straight through is 430 long, so it saves 107.5.
	static readonly Vector3 West = new( 105, 325, 3.75f ), East = new( 535, 325, 3.75f );
	const float Walk = 537.5f, Through = 430, Saved = Walk - Through;

	static NavLink Tunnel( Vector3? start = null ) => new() { Start = start ?? West, End = East, Radius = 4, Bidirectional = true, Area = 1, Type = Jump };

	static NavMesh Ridge( NavLink link, out int id )
	{
		var mesh = GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0, Height: ( x, y ) => 165 - 0.75f * MathF.Abs( x - 320 ) ) ) );
		id = mesh.AddLink( link );
		mesh.Commit();
		return mesh;
	}

	static NavFilter Barred( byte type ) => new( 0, forbiddenLinks: 1ul << type );

	static NavFilter Priced( float cost = 1, float penalty = 0, float[]? areaCosts = null )
	{
		var costs = new float[Jump + 1];
		Array.Fill( costs, 1f );
		costs[Jump] = cost;
		var penalties = new float[Jump + 1];
		penalties[Jump] = penalty;
		return new NavFilter( 0, areaCosts, 0, costs, penalties );
	}

	static bool TakesLink( NavQuery query, in NavFilter filter )
	{
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( West, East, Snap, filter, path ) );
		return path.Polys.ToArray().Any( p => p.IsLink );
	}

	[TestMethod]
	public void ForbiddenLinkTypeIsWalkedAround()
	{
		var mesh = Ridge( Tunnel(), out int id );
		var query = new NavQuery( mesh );
		Assert.IsTrue( TakesLink( query, default ) );
		Assert.IsFalse( TakesLink( query, Barred( Jump ) ) );
		Assert.IsTrue( TakesLink( query, new NavFilter( 0, forbiddenLinks: ~(1ul << Jump) ) ), "Every other type is forbidden, this one is not" );

		// The type is part of the link: changing it takes effect at the next commit.
		mesh.SetLink( id, Tunnel() with { Type = 0 } );
		Assert.IsFalse( TakesLink( query, Barred( Jump ) ) );
		mesh.Commit();
		Assert.IsTrue( TakesLink( query, Barred( Jump ) ) );
		Assert.IsFalse( TakesLink( query, Barred( 0 ) ), "A link that names no type has type 0" );
	}

	[TestMethod]
	public void ForbiddenLinkTypeThatIsTheOnlyWayEndsOnTheStartSide()
	{
		// Two floors, 200 apart, joined by nothing but the link.
		var mesh = GridMesh.Mesh( GridMesh.Tile( 0, 0, new GridMesh.Layer( 0 ), new GridMesh.Layer( 200 ) ) );
		mesh.AddLink( new NavLink { Start = new Vector3( 325, 325, 1 ), End = new Vector3( 325, 325, 201 ), Radius = 24, Bidirectional = true, Type = Jump } );
		mesh.Commit();
		var query = new NavQuery( mesh );
		var path = new NavPath();
		var barred = Barred( Jump );
		Vector3 below = new( 105, 325, 0 ), above = new( 505, 325, 200 );

		Assert.AreEqual( PathStatus.Partial, query.FindPath( below, above, Snap, barred, path ) );
		Assert.IsFalse( path.Polys.ToArray().Any( p => p.IsLink ) );
		Assert.AreEqual( 0f, path.End.Z, "The partial path stays on the lower floor" );
		Assert.IsTrue( new Vector2( path.End.X - 505, path.End.Y - 325 ).Length() < 1, $"Partial route ended at {path.End}, not below the target" );
		int polygons = mesh.GetStats().Polygons;
		Assert.IsTrue( query.SearchExpansions < polygons / 8, $"Unreachable search expanded {query.SearchExpansions} of {polygons} polygons" );

		// The island map is kept per link type mask as well: under this one the floors are apart.
		var s = mesh.State;
		uint from = GridMesh.Nearest( query, 105, 325 ).Id, to = GridMesh.Nearest( query, 505, 325, 201 ).Id;
		var open = NavQuery.GetIslands( s, default );
		var closed = NavQuery.GetIslands( s, barred );
		Assert.AreNotSame( open, closed );
		Assert.AreSame( closed, NavQuery.GetIslands( s, Barred( Jump ) ) );
		Assert.AreEqual( open.Of( from ), open.Of( to ) );
		Assert.AreNotEqual( closed.Of( from ), closed.Of( to ) );

		Assert.AreEqual( PathStatus.Complete, query.FindPath( below, above, Snap, Barred( Jump + 1 ), path ) );
		Assert.IsTrue( path.Polys.ToArray().Any( p => p.IsLink ) );
	}

	[TestMethod]
	public void LinkPenaltyIsWeighedAgainstTheWalkTheLinkSaves()
	{
		var query = new NavQuery( Ridge( Tunnel(), out _ ) );
		Assert.IsTrue( TakesLink( query, Priced( penalty: 0 ) ) );
		Assert.IsTrue( TakesLink( query, Priced( penalty: Saved - 1 ) ) );
		Assert.IsFalse( TakesLink( query, Priced( penalty: Saved + 1 ) ) );

		// A penalty for another type, and one the array does not reach, change nothing.
		Assert.IsTrue( TakesLink( query, new NavFilter( 0, linkPenalties: [1000, 1000, 1000] ) ) );
		Assert.IsFalse( TakesLink( query, new NavFilter( 0, linkPenalties: [0, 0, 0, 1000] ) ) );
	}

	[TestMethod]
	public void LinkCostMultipliesTheLengthOfTheLink()
	{
		var query = new NavQuery( Ridge( Tunnel(), out _ ) );
		float even = Walk / Through;
		Assert.IsTrue( TakesLink( query, Priced( cost: even - 0.01f ) ) );
		Assert.IsFalse( TakesLink( query, Priced( cost: even + 0.01f ) ) );
		Assert.IsTrue( TakesLink( query, new NavFilter( 0, linkCosts: [9, 9, 9] ) ), "A cost for another type changes nothing" );

		// Multiplier and penalty together: 430 * 1.2 = 516, which leaves 21.5 of the walk.
		Assert.IsTrue( TakesLink( query, Priced( cost: 1.2f, penalty: 20 ) ) );
		Assert.IsFalse( TakesLink( query, Priced( cost: 1.2f, penalty: 23 ) ) );
	}

	[TestMethod]
	public void LinkCostComesOnTopOfTheCostOfTheLinksArea()
	{
		// Only the link has area 5. 430 * 1.1 * 1.1 is below the walk, 430 * 1.2 * 1.1 above it.
		var query = new NavQuery( Ridge( Tunnel() with { Area = 5 }, out _ ) );
		Assert.IsTrue( TakesLink( query, Priced( cost: 1.1f, areaCosts: [1, 1, 1, 1, 1, 1.1f] ) ) );
		Assert.IsTrue( TakesLink( query, Priced( cost: 1, areaCosts: [1, 1, 1, 1, 1, 1.2f] ) ) );
		Assert.IsFalse( TakesLink( query, Priced( cost: 1.1f, areaCosts: [1, 1, 1, 1, 1, 1.2f] ) ) );
	}

	[TestMethod]
	public void IsValidOnALinkRespectsTheTypeMask()
	{
		var query = new NavQuery( Ridge( Tunnel(), out _ ) );
		var path = new NavPath();
		query.FindPath( West, East, Snap, default, path );
		var link = path.Polys.ToArray().Single( p => p.IsLink );
		Assert.IsTrue( query.IsValid( link, default ) );
		Assert.IsFalse( query.IsValid( link, Barred( Jump ) ) );
		Assert.IsTrue( query.IsValid( link, Barred( Jump + 1 ) ) );
		Assert.IsTrue( query.IsValid( link, Priced( cost: 50, penalty: 5000 ) ), "A price forbids nothing" );
		Assert.IsTrue( query.IsValid( path.Polys[0], Barred( Jump ) ), "The mask is about links only" );
	}

	[TestMethod]
	public void RepairDoesNotTakeAForbiddenLinkType()
	{
		// The link starts two rows north of the route, where the traveller is pushed to.
		var moved = new Vector3( 105, 345, 3.75f );
		var mesh = Ridge( Tunnel( moved ), out _ );
		var query = new NavQuery( mesh );
		var path = new NavPath();
		var barred = Barred( Jump );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( West, East, Snap, barred, path ) );
		var walked = path.Polys.ToArray();
		Assert.IsFalse( walked.Any( p => p.IsLink ) );
		var poly = GridMesh.Nearest( query, moved.X, moved.Y, moved.Z );
		Assert.IsFalse( walked.Contains( poly ) );

		var corridor = (PolyRef[])walked.Clone();
		int count = corridor.Length;
		Assert.IsTrue( query.Repair( mesh.State, poly, moved, East, barred, ref corridor, ref count, 64 ) );
		Assert.AreEqual( poly, corridor[0] );
		Assert.AreEqual( walked[^1], corridor[count - 1] );
		Assert.IsFalse( corridor.AsSpan( 0, count ).ToArray().Any( p => p.IsLink ) );

		// Allowed, the same repair goes straight through.
		corridor = (PolyRef[])walked.Clone();
		count = corridor.Length;
		Assert.IsTrue( query.Repair( mesh.State, poly, moved, East, default, ref corridor, ref count, 64 ) );
		Assert.AreEqual( 3, count );
		Assert.IsTrue( corridor[1].IsLink );
		Assert.AreEqual( walked[^1], corridor[2] );
	}

	[TestMethod]
	public void FilterRefusesALinkCostBelowOneAndAPenaltyBelowZero()
	{
		foreach ( float bad in new[] { 0.5f, 0f, -1f, 0.999f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
		{
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => new NavFilter( 0, linkCosts: [1, bad, 4] ), $"cost {bad}" );
		}
		foreach ( float bad in new[] { -0.001f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity } )
		{
			Assert.ThrowsException<ArgumentOutOfRangeException>( () => new NavFilter( 0, linkPenalties: [0, bad, 4] ), $"penalty {bad}" );
		}

		var filter = new NavFilter( 0, null, 1ul << 2, [1, 3], [0, 0.5f] );
		Assert.AreEqual( 3f, filter.LinkCost( 1 ) );
		Assert.AreEqual( 1f, filter.LinkCost( 7 ), "A type the array does not reach costs 1" );
		Assert.AreEqual( 0.5f, filter.LinkPenalty( 1 ) );
		Assert.AreEqual( 0f, filter.LinkPenalty( 7 ), "A type the array does not reach has no penalty" );
		Assert.IsFalse( filter.AllowsLink( 2 ) );
		Assert.IsTrue( filter.AllowsLink( 1 ) && filter.Allows( 2 ) );

		// Filters made the old way know no link rules.
		foreach ( var plain in new[] { default, new NavFilter( 5 ), new NavFilter( 0, [1, 2] ) } )
		{
			Assert.AreEqual( 0ul, plain.ForbiddenLinks );
			Assert.IsNull( plain.LinkCosts );
			Assert.IsNull( plain.LinkPenalties );
			Assert.AreEqual( 1f, plain.LinkCost( 0 ) );
			Assert.AreEqual( 0f, plain.LinkPenalty( 0 ) );
		}
	}

	[TestMethod]
	public void LinkTypeAboveTheMaximumIsRefused()
	{
		var mesh = GridMesh.Mesh( (0, 0) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.AddLink( Tunnel() with { Type = 64 } ) );
		int id = mesh.AddLink( Tunnel() with { Type = NavFilter.MaxLinkType } );
		Assert.AreEqual( 0, id, "A refused link takes no id" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.SetLink( id, Tunnel() with { Type = 200 } ) );
		mesh.Commit();
		Assert.AreEqual( NavFilter.MaxLinkType, mesh.State.Links[id].Def.Type );
		Assert.AreNotEqual( Tunnel(), Tunnel() with { Type = 0 }, "Links compare by type too" );
	}
}
