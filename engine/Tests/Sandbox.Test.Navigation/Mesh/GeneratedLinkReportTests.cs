using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Nav.Lib.Tests.GeneratedLinkTests;

namespace Nav.Lib.Tests;

/// <summary>
/// Generated links reported and not inserted, for a caller that keeps its links as objects of its own:
/// the sets per tile, which tiles changed, and the way back in as ordinary links. The scenes are those
/// of GeneratedLinkTests.
/// </summary>
[TestClass]
public class GeneratedLinkReportTests
{
	static readonly NavLinkSettings Report = Links with { Insert = false };

	static List<NavLink> Reported( NavMesh mesh, int x, int y )
	{
		var links = new List<NavLink>();
		Assert.AreEqual( mesh.GetGeneratedLinks( x, y, links ), links.Count );
		return links;
	}

	static List<(int X, int Y)> Changes( NavMesh mesh )
	{
		var tiles = new List<(int X, int Y)>();
		Assert.AreEqual( mesh.TakeLinkChanges( tiles ), tiles.Count );
		tiles.Sort();
		return tiles;
	}

	// What an engine does with a changed tile: its link objects that are no longer reported go, those
	// newly reported are added, the rest are left alone.
	sealed class Engine
	{
		public readonly Dictionary<(int X, int Y), List<(NavLink Link, int Id)>> Objects = new();
		public int Created, Destroyed;

		public void Sync( NavMesh mesh )
		{
			foreach ( var at in Changes( mesh ) )
			{
				var now = Reported( mesh, at.X, at.Y );
				if ( !Objects.TryGetValue( at, out var had ) ) Objects[at] = had = [];
				foreach ( var (link, id) in had.Where( o => !now.Contains( o.Link ) ).ToArray() )
				{
					mesh.RemoveLink( id );
					had.Remove( (link, id) );
					Destroyed++;
				}
				foreach ( var link in now.Where( l => !had.Any( o => o.Link == l ) ) )
				{
					had.Add( (link, mesh.AddLink( link )) );
					Created++;
				}
			}
			mesh.Commit();
		}

		public IEnumerable<(NavLink Link, int Id)> All => Objects.Values.SelectMany( o => o );
	}

	[TestMethod]
	public void ReportingInsertsNothingAndReportsWhatInsertingMakes()
	{
		var scene = Gap().Generate( Report );
		var twin = Gap().Generate( Links );

		Assert.AreEqual( 0, scene.Mesh.GetGeneratedLinks( new List<int>() ) );
		Assert.AreEqual( 0, scene.Mesh.LinksRefused );
		Assert.AreEqual( 0, scene.Mesh.State.Links().Count( l => l.Serial != 0 ) );

		var reported = Reported( scene.Mesh, 0, 0 ).Concat( Reported( scene.Mesh, 1, 0 ) ).ToList();
		var ids = new List<int>();
		twin.Mesh.GetGeneratedLinks( ids );
		Assert.AreEqual( ids.Count, reported.Count );
		Assert.IsTrue( reported.Count >= 8 && reported.Any( Crosses ), $"{reported.Count} links" );

		// Each is one of the twin's, with its ends where the twin's link stands on the mesh.
		foreach ( var link in reported )
		{
			Assert.AreEqual( 4, link.Radius );
			Assert.IsTrue( ids.Any( id =>
			{
				twin.Mesh.GetLink( id, out var other, out _ );
				twin.Mesh.GetLinkEnds( id, out var start, out var end, out _, out _ );
				return other.Type == link.Type && other.Bidirectional == link.Bidirectional && start == link.Start && end == link.End;
			} ), $"no inserted link like {link}" );
		}

		// The report does not depend on whether the mesh inserts.
		CollectionAssert.AreEqual( Reported( twin.Mesh, 0, 0 ), Reported( scene.Mesh, 0, 0 ) );
		CollectionAssert.AreEqual( Reported( twin.Mesh, 1, 0 ), Reported( scene.Mesh, 1, 0 ) );
	}

	[TestMethod]
	public void ChangedTilesAreToldOnceAndARebuildWithTheSameLinksIsNoChange()
	{
		var scene = Gap().Generate( Report );
		CollectionAssert.AreEqual( new[] { (0, 0), (1, 0) }, Changes( scene.Mesh ) );
		Assert.AreEqual( 0, Changes( scene.Mesh ).Count );
		scene.Mesh.Commit();
		Assert.AreEqual( 0, Changes( scene.Mesh ).Count );

		var before = (Reported( scene.Mesh, 0, 0 ), Reported( scene.Mesh, 1, 0 ));
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Pump();
		Assert.AreEqual( 0, Changes( scene.Mesh ).Count, "one tile built again" );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Mesh.MarkDirty( 1, 0 );
		scene.Pump( 1 );
		Assert.AreEqual( 0, Changes( scene.Mesh ).Count, "both built again, one to a commit" );
		CollectionAssert.AreEqual( before.Item1, Reported( scene.Mesh, 0, 0 ) );
		CollectionAssert.AreEqual( before.Item2, Reported( scene.Mesh, 1, 0 ) );
	}

	[TestMethod]
	public void SeamLinksAreReportedOnceTheTilesAroundAreIn()
	{
		var scene = Gap().Start( Report );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Mesh.MarkDirty( 1, 0 );
		var jobs = new TileJob[1];
		var builder = new NavBuilder();
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		jobs[0].SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
		builder.Build( jobs[0] );
		scene.Mesh.Commit( jobs );

		// The first tile holds its spans and has no links yet.
		Assert.AreEqual( 0, Changes( scene.Mesh ).Count );
		Assert.AreEqual( 0, Reported( scene.Mesh, 0, 0 ).Count );

		scene.Pump( 1 );
		CollectionAssert.AreEqual( new[] { (0, 0), (1, 0) }, Changes( scene.Mesh ) );
		Assert.IsTrue( Reported( scene.Mesh, 0, 0 ).Concat( Reported( scene.Mesh, 1, 0 ) ).Any( Crosses ) );
	}

	[TestMethod]
	public void ReportedLinksAddedAsOrdinaryLinksJoinThePolygonsTheGeneratorMeantAndPathsTakeThem()
	{
		var scene = Gap().Generate( Report );
		var twin = Gap().Generate( Links );
		var query = new NavQuery( scene.Mesh );
		var path = new NavPath();
		Vector3 west = new( 380, 240, 100 ), east = new( 680, 240, 100 ), floor = new( 100, 100, 0 );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( west, east, Snap, Filters.All, path ), "nothing is inserted" );

		var engine = new Engine();
		engine.Sync( scene.Mesh );
		Assert.IsTrue( engine.Created >= 8 );

		// Each stands where it was reported, on the polygons the inserted link of the twin joins.
		var ids = new List<int>();
		twin.Mesh.GetGeneratedLinks( ids );
		var meant = ids.Select( id => twin.Mesh.State.LinkAt( id ) ).ToList();
		foreach ( var (link, id) in engine.All )
		{
			Assert.IsTrue( scene.Mesh.GetLink( id, out _, out bool generated ) && !generated );
			var entry = scene.Mesh.State.LinkAt( id );
			Assert.IsTrue( entry.Connected, $"{link} is not on the mesh" );
			Assert.AreEqual( link.Start, entry.StartPoint );
			Assert.AreEqual( link.End, entry.EndPoint );
			Assert.IsTrue( meant.Any( m => m.StartPoly == entry.StartPoly && m.EndPoly == entry.EndPoly && m.StartPoint == entry.StartPoint && m.EndPoint == entry.EndPoint ), $"{link} joins other polygons" );
		}

		Assert.AreEqual( PathStatus.Complete, query.FindPath( west, east, Snap, Filters.All, path ) );
		Assert.IsTrue( path.Polys.ToArray().Count( p => p.IsLink ) == 1 && path.Length < 400 );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( west, floor, Snap, Filters.All, path ) );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( west, east, Snap, Filters.All.WithForbiddenLinks( 1ul << Jump ), path ) );

		// The links the engine added hide nothing from the generator: built again, every tile reports
		// what it did, and the engine has nothing to do.
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Pump();
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Mesh.MarkDirty( 1, 0 );
		scene.Pump();
		int created = engine.Created;
		engine.Sync( scene.Mesh );
		Assert.AreEqual( (created, 0), (engine.Created, engine.Destroyed) );
		CollectionAssert.AreEqual( Reported( twin.Mesh, 0, 0 ), Reported( scene.Mesh, 0, 0 ) );
		CollectionAssert.AreEqual( Reported( twin.Mesh, 1, 0 ), Reported( scene.Mesh, 1, 0 ) );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( west, east, Snap, Filters.All, path ) );
	}

	[TestMethod]
	public void BlockerAndGeometryChangeOnlyTheLinkObjectsTheyTouch()
	{
		var scene = Platform().Generate( Report );
		var engine = new Engine();
		engine.Sync( scene.Mesh );
		var all = Reported( scene.Mesh, 0, 0 );
		static bool East( NavLink l ) => l.End.X > 320;
		int east = all.Count( East );
		Assert.IsTrue( east > 0 && east < all.Count && engine.Created == all.Count );

		// A blocker over the ground east of the platform: the drops onto it go, the others stay as they are.
		int id = scene.Mesh.AddVolume( Blocker( 330, 100, 420, 380 ) );
		scene.Pump();
		engine.Sync( scene.Mesh );
		Assert.AreEqual( (all.Count, east), (engine.Created, engine.Destroyed) );
		CollectionAssert.AreEqual( all.Where( l => !East( l ) ).ToList(), Reported( scene.Mesh, 0, 0 ) );
		Assert.IsTrue( engine.All.All( o => scene.Mesh.State.LinkAt( o.Id ).Connected ) );

		// It leaves: they come back, the same as before.
		scene.Mesh.RemoveVolume( id );
		scene.Pump();
		engine.Sync( scene.Mesh );
		Assert.AreEqual( (all.Count + east, east), (engine.Created, engine.Destroyed) );
		CollectionAssert.AreEqual( all, Reported( scene.Mesh, 0, 0 ) );

		// The platform grows a step to the east: the tile is built again from other triangles.
		scene.Box( 320, 160, 400, 320, 0, 100 );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Pump();
		CollectionAssert.AreEqual( new[] { (0, 0) }, Changes( scene.Mesh ) );
		var grown = Reported( scene.Mesh, 0, 0 );
		Assert.IsTrue( grown.Any( l => l.Start.X > 330 ) && !all.Any( l => l.Start.X > 330 ) );

		// The tile goes: so do its links.
		scene.Mesh.RemoveTile( 0, 0 );
		scene.Mesh.Commit();
		CollectionAssert.AreEqual( new[] { (0, 0) }, Changes( scene.Mesh ) );
		Assert.AreEqual( 0, Reported( scene.Mesh, 0, 0 ).Count );
	}
}
