using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nav.Build;

namespace Nav.Lib.Tests;

/// <summary>
/// Generated links on small scenes of boxes: agent radius 16, height 64, climb 18, cells of 4, tiles of
/// 512. Drops are type 1, jumps type 2.
/// </summary>
[TestClass]
public partial class GeneratedLinkTests
{
	internal const byte Drop = 1, Jump = 2;
	static readonly NavSettings Settings = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 128 } );
	internal static readonly NavLinkSettings Links = NavLinkSettings.Create( Settings ) with { MaxDrop = 300, MaxJumpDistance = 200, MaxJumpUp = 40, DropType = Drop, JumpType = Jump, Insert = true };
	internal static readonly Vector3 Snap = new( 34, 34, 97 );

	internal sealed class Scene
	{
		public readonly List<Vector3> Vertices = [];
		public readonly List<int> Indices = [];
		public NavMesh Mesh = null!;
		public int Asked, PeakHeld;
		public readonly List<(int X, int Y)> Order = [];
		readonly NavBuilder builder = new();

		void Quad( Vector3 a, Vector3 b, Vector3 c, Vector3 d )
		{
			int first = Vertices.Count;
			Vertices.AddRange( [a, b, c, d] );
			Indices.AddRange( [first, first + 1, first + 2, first, first + 2, first + 3] );
		}

		public Scene Floor( float x0, float y0, float x1, float y1, float z )
		{
			Quad( new( x0, y0, z ), new( x1, y0, z ), new( x1, y1, z ), new( x0, y1, z ) );
			return this;
		}

		// A top to stand on and four sides.
		public Scene Box( float x0, float y0, float x1, float y1, float z0, float z1 )
		{
			Floor( x0, y0, x1, y1, z1 );
			Quad( new( x0, y0, z0 ), new( x1, y0, z0 ), new( x1, y0, z1 ), new( x0, y0, z1 ) );
			Quad( new( x1, y1, z0 ), new( x0, y1, z0 ), new( x0, y1, z1 ), new( x1, y1, z1 ) );
			Quad( new( x0, y1, z0 ), new( x0, y0, z0 ), new( x0, y0, z1 ), new( x0, y1, z1 ) );
			Quad( new( x1, y0, z0 ), new( x1, y1, z0 ), new( x1, y1, z1 ), new( x1, y0, z1 ) );
			return this;
		}

		public Scene Start( NavLinkSettings links )
		{
			Mesh = new NavMesh( Settings, NavMeshOptions.Create( Settings ) with { Links = links } );
			return this;
		}

		// The job loop of an engine that hands every job all its triangles, batch jobs to a commit.
		public Scene Pump( int batch = 64 )
		{
			var jobs = new TileJob[batch];
			Vector3[] vertices = Vertices.ToArray();
			int[] indices = Indices.ToArray();
			Asked = 0;
			for ( int count; (count = Mesh.TakeJobs( jobs )) > 0; )
			{
				for ( int i = 0; i < count; i++ )
				{
					// A job that only judges links across borders builds no tile.
					if ( !jobs[i].SeamsOnly ) Order.Add( (jobs[i].X, jobs[i].Y) );
					if ( jobs[i].NeedsGeometry )
					{
						Asked++;
						jobs[i].SetGeometry( vertices, indices );
					}
					builder.Build( jobs[i] );
				}
				Mesh.Commit( jobs.AsSpan( 0, count ) );
				PeakHeld = Math.Max( PeakHeld, Mesh.HeldFields );
			}
			return this;
		}

		public Scene Generate( NavLinkSettings links ) => Start( links ).Build();

		// Every tile the scene reaches.
		public Scene Build( int batch = 64 )
		{
			for ( int y = 0; y * 512 < Vertices.Max( v => v.Y ); y++ )
			{
				for ( int x = 0; x * 512 < Vertices.Max( v => v.X ); x++ ) Mesh.MarkDirty( x, y );
			}
			return Pump( batch );
		}

		public List<NavLink> Generated()
		{
			var ids = new List<int>();
			Mesh.GetGeneratedLinks( ids );
			var found = new List<NavLink>();
			foreach ( int id in ids )
			{
				Assert.IsTrue( Mesh.GetLink( id, out var link, out bool generated ) && generated );
				Assert.IsTrue( Mesh.GetLinkEnds( id, out _, out _, out bool startOn, out bool endOn ) && startOn && endOn, $"link {id} is not on the mesh" );
				found.Add( link );
			}
			return found;
		}
	}

	// Every link of a has one in b of its type with both ends within the spacing, either way round when
	// both can be taken both ways.
	static void AssertSame( List<NavLink> a, List<NavLink> b, string what )
	{
		foreach ( var link in a )
		{
			bool matched = b.Any( o => o.Type == link.Type && (Vector3.Distance( o.Start, link.Start ) < 64 && Vector3.Distance( o.End, link.End ) < 64
				|| o.Bidirectional && link.Bidirectional && Vector3.Distance( o.Start, link.End ) < 64 && Vector3.Distance( o.End, link.Start ) < 64) );
			Assert.IsTrue( matched, $"{what}: no match for type {link.Type} {link.Start} to {link.End}" );
		}
	}

	internal static Scene Platform() => new Scene().Floor( 40, 40, 470, 470, 0 ).Box( 160, 160, 320, 320, 0, 100 );

	// Two platforms with the tile border at 512 in the gap between them.
	internal static Scene Gap() => new Scene().Floor( 40, 40, 900, 470, 0 ).Box( 300, 160, 460, 320, 0, 100 ).Box( 600, 160, 760, 320, 0, 100 );

	internal static bool Crosses( NavLink l ) => l.Type == Jump && l.Start.Z > 90 && l.End.Z > 90 && (l.Start.X < 512) != (l.End.X < 512);

	[TestMethod]
	public void NothingIsGeneratedOrHeldWithoutLinkSettings()
	{
		var scene = Gap().Start( default );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Mesh.MarkDirty( 1, 0 );
		var jobs = new TileJob[2];
		Assert.AreEqual( 2, scene.Mesh.TakeJobs( jobs ) );
		foreach ( var job in jobs )
		{
			Assert.IsFalse( job.FindLinks || job.KeepSolid || job.Apron );
			Assert.AreEqual( 512 + 28, job.BoundsMax.X - job.X * 512 );
			job.SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
			new NavBuilder().Build( job );
			Assert.IsNull( job.Found );
			Assert.IsNull( job.Solid );
		}
		scene.Mesh.Commit( jobs );
		Assert.AreEqual( 0, scene.Generated().Count );
		Assert.AreEqual( 0, scene.Mesh.HeldPeak );
	}

	[TestMethod]
	public void SettingsThatDoNotFitATileAreRefused()
	{
		Assert.ThrowsException<ArgumentException>( () => new NavMesh( Settings, NavMeshOptions.Create( Settings ) with { Links = Links with { MaxJumpDistance = 500 } } ) );
		Assert.ThrowsException<ArgumentException>( () => new NavMesh( Settings, NavMeshOptions.Create( Settings ) with { Links = Links with { JumpType = 64 } } ) );
		Assert.ThrowsException<ArgumentException>( () => new NavMesh( Settings, NavMeshOptions.Create( Settings ) with { Links = Links with { MaxDrop = float.NaN } } ) );
	}

	[TestMethod]
	public void SpacingIsUsedAsSetAndMustBeACellAtLeast()
	{
		var made = NavLinkSettings.Create( Settings );
		Assert.AreEqual( (0f, 0f, 0f, 4 * Settings.AgentRadius, (byte)0, (byte)0, false), (made.MaxDrop, made.MaxJumpDistance, made.MaxJumpUp, made.Spacing, made.DropType, made.JumpType, made.Insert) );
		Assert.IsFalse( made.Enabled );
		Assert.IsTrue( made.IsValid( Settings, out _ ) );
		Assert.IsTrue( (made with { Spacing = 0 }).IsValid( Settings, out _ ), "Nothing generated: no spacing needed" );

		foreach ( float spacing in new[] { 0f, Settings.CellSize * 0.5f, -1f, float.NaN } )
		{
			var links = Links with { Spacing = spacing };
			Assert.IsFalse( links.IsValid( Settings, out var reason ), $"spacing {spacing}" );
			Assert.IsNotNull( reason );
			Assert.ThrowsException<ArgumentException>( () => new NavMesh( Settings, NavMeshOptions.Create( Settings ) with { Links = links } ) );
		}
		Assert.IsTrue( (Links with { Spacing = Settings.CellSize }).IsValid( Settings, out _ ) );

		// Wider spacing, fewer links along the same ledge: the value is used, not raised or replaced.
		static int Count( float spacing ) => Platform().Generate( Links with { Spacing = spacing } ).Generated().Count;
		Assert.IsTrue( Count( 256 ) < Count( 64 ), $"{Count( 256 )} against {Count( 64 )}" );
	}

	[TestMethod]
	public void PlatformGivesDropsThatPathsTakeAndFiltersForbid()
	{
		var scene = Platform().Generate( Links );
		var links = scene.Generated();
		Assert.IsTrue( links.Count >= 4, $"{links.Count} links" );
		foreach ( var link in links )
		{
			Assert.AreEqual( Drop, link.Type );
			Assert.AreEqual( 100, link.Start.Z, 4.01f );
			Assert.AreEqual( 0, link.End.Z, 4.01f );
			Assert.IsFalse( link.Bidirectional );
		}
		Assert.IsTrue( links.Any( l => l.End.X > 320 ) && links.Any( l => l.End.X < 160 ) && links.Any( l => l.End.Y > 320 ) && links.Any( l => l.End.Y < 160 ) );

		var query = new NavQuery( scene.Mesh );
		var path = new NavPath();
		Vector3 top = new( 240, 240, 100 ), floor = new( 420, 240, 0 );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( top, floor, Snap, Filters.All, path ) );
		Assert.IsTrue( path.Polys.ToArray().Any( p => p.IsLink ) );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( top, floor, Snap, Filters.All.WithForbiddenLinks( 1ul << Drop ), path ) );
		// One way: there is no path up.
		Assert.AreEqual( PathStatus.Partial, query.FindPath( floor, top, Snap, Filters.All, path ) );
	}

	[TestMethod]
	public void GapGivesAJumpBothWaysAndABeamAcrossItNone()
	{
		var scene = new Scene().Floor( 40, 40, 470, 470, 0 ).Box( 80, 160, 200, 320, 0, 100 ).Box( 300, 160, 420, 320, 0, 100 );
		Assert.IsTrue( scene.Generate( Links ).Generated().Any( l => l.Type == Jump && l.Bidirectional && MathF.Abs( l.Start.Z - 100 ) < 5 && MathF.Abs( l.End.Z - 100 ) < 5 ) );

		// A beam along the gap at head height, in the way of every jump.
		scene.Box( 246, 100, 254, 380, 130, 170 );
		Assert.IsFalse( scene.Generate( Links ).Generated().Any( l => l.Type == Jump ) );
	}

	// A ledge low enough to jump onto: a drop down and a jump up, each one way, so a filter tells them apart.
	[TestMethod]
	public void LowLedgeGivesADropDownAndAJumpUpEachOneWay()
	{
		static Scene Ledge() => new Scene().Floor( 40, 40, 470, 470, 0 ).Box( 160, 160, 320, 320, 0, 30 );
		var links = Ledge().Generate( Links ).Generated();
		var drops = links.Where( l => l.Type == Drop ).ToList();
		var ups = links.Where( l => l.Type == Jump ).ToList();
		Assert.IsTrue( drops.Count >= 4 && ups.Count >= 4, $"{drops.Count} drops, {ups.Count} jumps" );
		// Thinning keeps or drops the two together: every drop has its jump beside it.
		Assert.AreEqual( drops.Count, ups.Count );
		Assert.IsFalse( links.Any( l => l.Bidirectional ) );
		foreach ( var drop in drops )
		{
			Assert.AreEqual( 30, drop.Start.Z, 4.01f );
			Assert.AreEqual( 0, drop.End.Z, 4.01f );
		}
		foreach ( var up in ups )
		{
			Assert.AreEqual( 0, up.Start.Z, 4.01f );
			Assert.AreEqual( 30, up.End.Z, 4.01f );
			Assert.IsTrue( drops.Any( d => Vector3.Distance( d.Start, up.End ) < 16 && Vector3.Distance( d.End, up.Start ) < 16 ), $"no drop back along the jump {up.Start} to {up.End}" );
		}

		var scene = Ledge().Generate( Links );
		var query = new NavQuery( scene.Mesh );
		var path = new NavPath();
		Vector3 top = new( 240, 240, 30 ), floor = new( 420, 240, 0 );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( floor, top, Snap, Filters.All, path ) );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( floor, top, Snap, Filters.All.WithForbiddenLinks( 1ul << Jump ), path ), "up is a jump" );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( floor, top, Snap, Filters.All.WithForbiddenLinks( 1ul << Drop ), path ) );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( top, floor, Snap, Filters.All.WithForbiddenLinks( 1ul << Jump ), path ), "down is a drop" );
		Assert.AreEqual( PathStatus.Partial, query.FindPath( top, floor, Snap, Filters.All.WithForbiddenLinks( 1ul << Drop ), path ) );

		// Reported the same way, and with MaxJumpUp 0 the drops alone.
		var reported = Ledge().Generate( Links with { Insert = false } );
		var told = new List<NavLink>();
		reported.Mesh.GetGeneratedLinks( 0, 0, told );
		Assert.AreEqual( drops.Count, told.Count( l => l.Type == Drop && !l.Bidirectional ) );
		Assert.AreEqual( ups.Count, told.Count( l => l.Type == Jump && !l.Bidirectional ) );
		var flat = Ledge().Generate( Links with { MaxJumpUp = 0 } ).Generated();
		Assert.AreEqual( drops.Count, flat.Count );
		Assert.IsTrue( flat.All( l => l.Type == Drop && !l.Bidirectional ) );
	}

	[TestMethod]
	public void HandPlacedLinkIsToldApartAndHidesNothing()
	{
		var plain = Platform().Generate( Links );
		var scene = Platform().Start( Links );
		int placed = scene.Mesh.AddLink( new NavLink { Start = new Vector3( 300, 240, 100 ), End = new Vector3( 360, 240, 0 ), Radius = 16, Area = NavFilter.DefaultArea } );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Pump();

		Assert.IsTrue( scene.Mesh.GetLink( placed, out var link, out bool generated ) );
		Assert.IsFalse( generated );
		Assert.AreEqual( 300, link.Start.X );
		Assert.AreEqual( plain.Generated().Count, scene.Generated().Count );
		var ids = new List<int>();
		scene.Mesh.GetGeneratedLinks( ids );
		Assert.IsFalse( ids.Contains( placed ) );

		// The caller's calls leave generated links alone.
		scene.Mesh.RemoveLink( ids[0] );
		Assert.ThrowsException<ArgumentException>( () => scene.Mesh.SetLink( ids[0], link ) );
		scene.Mesh.Commit();
		Assert.AreEqual( plain.Generated().Count, scene.Generated().Count );
		Assert.IsFalse( scene.Mesh.GetLink( 60000, out _, out _ ) );
	}

	[TestMethod]
	public void JumpAcrossATileBorderIsJudgedFromTheHeldSpansOfBothTilesWhichAreThenLetGo()
	{
		var scene = Gap().Generate( Links );
		Assert.IsTrue( scene.Mesh.HeldPeak > 0 );
		Assert.AreEqual( 0, scene.Mesh.HeldFields );
		Assert.AreEqual( 0, scene.Mesh.HeldBytes );
		// One of each jump and the same jump back.
		int crossing = scene.Generated().Count( Crosses );
		Assert.IsTrue( crossing is 1 or 2, $"{crossing} jumps across" );
		Assert.AreEqual( crossing, scene.Generated().Count( l => Crosses( l ) && l.Bidirectional ) );

		// A wall in the far tile, beyond the field of the near one.
		Assert.IsFalse( Gap().Box( 560, 100, 568, 380, 0, 300 ).Generate( Links ).Generated().Any( Crosses ) );
	}

	[TestMethod]
	public void TileRebuiltAloneLooksOverAnApronAndFindsTheLinksBothWays()
	{
		var scene = Gap().Generate( Links );
		var before = scene.Generated();
		var ids = new List<int>();
		scene.Mesh.GetGeneratedLinks( ids );

		var jobs = new TileJob[1];
		scene.Mesh.MarkDirty( 1, 0 );
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		Assert.IsTrue( jobs[0].Apron );
		Assert.IsFalse( jobs[0].KeepSolid );
		int apron = BuildConfig.ApronCells( Settings, Links ) * 4;
		Assert.IsTrue( apron >= 200 );
		Assert.AreEqual( 512 - 28 - apron, jobs[0].BoundsMin.X );
		Assert.AreEqual( 1024 + 28 + apron, jobs[0].BoundsMax.X );
		jobs[0].SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
		new NavBuilder().Build( jobs[0] );
		Assert.AreEqual( 0, scene.Mesh.Commit( jobs ) );

		var after = scene.Generated();
		Assert.AreEqual( before.Count( Crosses ), after.Count( Crosses ) );
		Assert.IsTrue( after.Any( Crosses ) );
		AssertSame( before, after, "after the rebuild" );
		AssertSame( after, before, "before the rebuild" );
		Assert.AreEqual( 0, scene.Mesh.HeldFields );

		// With a wall in the way now, none.
		scene.Box( 560, 100, 568, 380, 0, 300 );
		scene.Mesh.MarkDirty( 1, 0 );
		scene.Pump();
		Assert.IsFalse( scene.Generated().Any( Crosses ) );
	}

	[TestMethod]
	public void RebuildThatFindsTheSameLinksKeepsTheirIds()
	{
		var scene = Platform().Generate( Links );
		var ids = new List<int>();
		scene.Mesh.GetGeneratedLinks( ids );
		// A volume beside the platform that touches no link: the tile is built again, the links stay.
		int id = scene.Mesh.AddVolume( new NavVolume { Shape = VolumeShape.Box, Area = 5, LocalToWorld = Matrix4x4.Identity, A = new Vector3( 50, 50, -10 ), B = new Vector3( 90, 90, 10 ) } );
		scene.Pump();
		var again = new List<int>();
		scene.Mesh.GetGeneratedLinks( again );
		CollectionAssert.AreEqual( ids, again );
		Assert.IsTrue( ids.Count >= 4 );
	}

	// C1: a tile does not hold its spans for a neighbour that fails, is removed or is never built.
	[TestMethod]
	public void HeldSpansAreLetGoWhenTheNeighbourFailsOrIsRemoved()
	{
		foreach ( bool remove in new[] { false, true } )
		{
			var scene = Gap().Start( Links );
			scene.Mesh.MarkDirty( 0, 0 );
			scene.Mesh.MarkDirty( 1, 0 );
			var jobs = new TileJob[2];
			Assert.AreEqual( 2, scene.Mesh.TakeJobs( jobs ) );
			Assert.IsTrue( jobs[0].KeepSolid && jobs[1].KeepSolid );
			jobs[0].SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
			new NavBuilder().Build( jobs[0] );
			scene.Mesh.Commit( jobs.AsSpan( 0, 1 ) );
			Assert.AreEqual( 1, scene.Mesh.HeldFields );

			if ( remove )
			{
				scene.Mesh.RemoveTile( 1, 0 );
				scene.Mesh.Commit();
			}
			else
			{
				// Built without its triangles: the job fails.
				new NavBuilder().Build( jobs[1] );
				Assert.AreEqual( 1, scene.Mesh.Commit( jobs.AsSpan( 1, 1 ) ) );
			}
			// Nothing is left to wait for: the edge points of tile 0 are judged by a job, and it lets go.
			Assert.IsTrue( scene.Mesh.IsPending( 0, 0 ) );
			scene.Pump();
			Assert.AreEqual( 0, scene.Asked );
			Assert.IsFalse( scene.Mesh.IsPending( 0, 0 ) );
			Assert.AreEqual( 0, scene.Mesh.HeldFields );
			Assert.AreEqual( 0, scene.Mesh.HeldBytes );
			Assert.IsTrue( scene.Generated().Count > 0 );
			Assert.IsFalse( scene.Generated().Any( Crosses ) );
		}
	}

	[TestMethod]
	public void HeldSpansAreLetGoForANeighbourThatIsRebuiltOverAndOver()
	{
		var scene = Gap().Start( Links );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Mesh.MarkDirty( 1, 0 );
		var jobs = new TileJob[2];
		Assert.AreEqual( 2, scene.Mesh.TakeJobs( jobs ) );
		foreach ( var job in jobs )
		{
			job.SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
			new NavBuilder().Build( job );
		}
		// Tile 1 changes again while its job is in flight: it stays dirty.
		scene.Mesh.MarkDirty( 1, 0 );
		scene.Mesh.Commit( jobs );
		Assert.IsTrue( scene.Mesh.IsPending( 1, 0 ) );
		Assert.IsFalse( scene.Mesh.GetStats().Tiles < 2 );
		// Tile 0 is judged against the build of tile 1 that came back, then against the next, and lets go.
		scene.Pump();
		Assert.AreEqual( 0, scene.Mesh.PendingTiles );
		Assert.AreEqual( 0, scene.Mesh.HeldFields );
		int crossing = scene.Generated().Count( Crosses );
		Assert.IsTrue( crossing > 0 );
		Assert.AreEqual( Gap().Generate( Links ).Generated().Count( Crosses ), crossing );
	}

	[TestMethod]
	public void HeldSpansOverTheLimitAreLetGoAndTheLateNeighbourLooksOverAnApron()
	{
		var scene = Gap().Start( Links );
		scene.Mesh.MaxHeldFields = 0;
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Mesh.MarkDirty( 1, 0 );
		var jobs = new TileJob[1];
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		jobs[0].SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
		new NavBuilder().Build( jobs[0] );
		Assert.IsNotNull( jobs[0].Solid );
		scene.Mesh.Commit( jobs );

		// Over the limit it waits for no tile: the next job judges what it can, and it lets go.
		scene.Mesh.RemoveTile( 1, 0 );
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		Assert.IsTrue( jobs[0].SeamsOnly && !jobs[0].NeedsGeometry );
		new NavBuilder().Build( jobs[0] );
		Assert.AreEqual( TileJobStatus.Unchanged, jobs[0].Status );
		scene.Mesh.Commit( jobs );
		Assert.AreEqual( 0, scene.Mesh.HeldFields, "held although the limit is 0" );
		Assert.IsFalse( scene.Generated().Any( Crosses ) );

		// Tile 1, whenever it comes: tile 0 is built and holds nothing, so it looks that far itself.
		scene.Mesh.MarkDirty( 1, 0 );
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		Assert.IsTrue( jobs[0].Apron && !jobs[0].KeepSolid && jobs[0].Seams == null );
		jobs[0].SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
		new NavBuilder().Build( jobs[0] );
		scene.Mesh.Commit( jobs );
		var whole = Gap().Generate( Links ).Generated();
		Assert.AreEqual( whole.Count( Crosses ), scene.Generated().Count( Crosses ) );
		AssertSame( whole, scene.Generated(), "against a generation" );
	}

	// Platforms in a row along x and one along y on every tile, with gaps of 120 that tile borders run through.
	static Scene Field( int tiles )
	{
		float size = tiles * 512;
		var scene = new Scene().Floor( 8, 8, size - 8, size - 8, 0 );
		for ( float y = 96; y + 160 < size; y += 280 )
		{
			for ( float x = 72; x + 160 < size; x += 280 ) scene.Box( x, y, x + 160, y + 160, 0, 100 );
		}
		return scene;
	}

	// Both tiles of the gap built and committed together: their links across the border are still to judge.
	static Scene GapBuilt( out TileJob[] jobs )
	{
		var scene = Gap().Start( Links );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Mesh.MarkDirty( 1, 0 );
		jobs = new TileJob[2];
		Assert.AreEqual( 2, scene.Mesh.TakeJobs( jobs ) );
		foreach ( var job in jobs )
		{
			Assert.IsNull( job.Seams );
			job.SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
			new NavBuilder().Build( job );
		}
		scene.Mesh.Commit( jobs );
		return scene;
	}

	[TestMethod]
	public void LastJudgingIsAJobOfItsOwnThatCountsAsPending()
	{
		var scene = GapBuilt( out var jobs );
		Assert.AreEqual( 2, scene.Mesh.PendingTiles );
		Assert.IsTrue( scene.Mesh.IsPending( 0, 0 ) && scene.Mesh.IsPending( 1, 0 ) );
		Assert.AreEqual( 2, scene.Mesh.HeldFields );
		Assert.AreEqual( 0, scene.Generated().Count );

		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		Assert.IsTrue( jobs[0].SeamsOnly && !jobs[0].NeedsGeometry && jobs[0].Seams!.Length == 2 );
		Assert.AreEqual( 0, scene.Mesh.TakeJobs( jobs.AsSpan( 1 ) ), "one at a time" );
		Assert.AreEqual( 2, scene.Mesh.PendingTiles );
		new NavBuilder().Build( jobs[0] );
		Assert.AreEqual( TileJobStatus.Unchanged, jobs[0].Status );
		Assert.AreEqual( 0, scene.Mesh.Commit( jobs.AsSpan( 0, 1 ) ) );

		Assert.AreEqual( 0, scene.Mesh.PendingTiles );
		Assert.AreEqual( 0, scene.Mesh.HeldFields );
		Assert.IsTrue( scene.Generated().Any( Crosses ) );
		// Committed once more it counts for nothing.
		scene.Mesh.Commit( jobs.AsSpan( 0, 1 ) );
		AssertSame( Gap().Generate( Links ).Generated(), scene.Generated(), "against a generation" );
	}

	[TestMethod]
	public void JudgingRidesOnTheFirstJobWhenATileIsDirty()
	{
		// Two rows away: the ways out of the gap's tiles, diagonal ones too, do not come over it.
		var scene = GapBuilt( out var jobs );
		scene.Floor( 40, 1100, 470, 1400, 0 );
		scene.Mesh.MarkDirty( 0, 2 );
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		Assert.IsTrue( jobs[0].Seams != null && !jobs[0].SeamsOnly && jobs[0].NeedsGeometry && (jobs[0].X, jobs[0].Y) == (0, 2) );
		jobs[0].SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
		new NavBuilder().Build( jobs[0] );
		Assert.AreEqual( TileJobStatus.Built, jobs[0].Status );
		scene.Mesh.Commit( jobs.AsSpan( 0, 1 ) );
		Assert.IsTrue( scene.Generated().Any( Crosses ) );
		scene.Pump();
		Assert.AreEqual( 0, scene.Mesh.PendingTiles );
		Assert.AreEqual( 0, scene.Mesh.HeldFields );
	}

	[TestMethod]
	public void JudgingThatIsAbandonedOrOvertakenByARebuildIsDoneAgain()
	{
		var whole = Gap().Generate( Links ).Generated();

		// Committed before it was built: abandoned, and handed out again.
		var scene = GapBuilt( out var jobs );
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		var abandoned = jobs[0];
		scene.Mesh.Commit( jobs.AsSpan( 0, 1 ) );
		Assert.AreEqual( 2, scene.Mesh.PendingTiles );
		Assert.AreEqual( 0, scene.Generated().Count );
		scene.Pump();
		new NavBuilder().Build( abandoned );
		scene.Mesh.Commit( [abandoned] );
		Assert.AreEqual( 0, scene.Mesh.PendingTiles );
		Assert.AreEqual( 0, scene.Mesh.HeldFields );
		AssertSame( whole, scene.Generated(), "after an abandoned job" );
		Assert.AreEqual( whole.Count, scene.Generated().Count );

		// A tile is built again, without its second platform, while the job that judges against it is out.
		scene = GapBuilt( out jobs );
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		var late = jobs[0];
		new NavBuilder().Build( late );
		var without = new Scene().Floor( 40, 40, 900, 470, 0 ).Box( 300, 160, 460, 320, 0, 100 );
		scene.Mesh.MarkDirty( 1, 0 );
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		Assert.IsTrue( jobs[0].Seams == null && jobs[0].KeepSolid );
		jobs[0].SetGeometry( without.Vertices.ToArray(), without.Indices.ToArray() );
		new NavBuilder().Build( jobs[0] );
		scene.Mesh.Commit( jobs.AsSpan( 0, 1 ) );
		scene.Mesh.Commit( [late] );
		Assert.IsFalse( scene.Generated().Any( Crosses ), "a jump onto a platform that is gone" );
		scene.Pump();
		Assert.AreEqual( 0, scene.Mesh.PendingTiles );
		Assert.AreEqual( 0, scene.Mesh.HeldFields );
		Assert.IsFalse( scene.Generated().Any( Crosses ) );
		Assert.IsTrue( scene.Generated().Count > 0 );
	}

	[TestMethod]
	public void TileAJudgingJobIsNamedAfterWaitsForIt()
	{
		var scene = GapBuilt( out var jobs );
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		var judging = jobs[0];
		scene.Mesh.MarkDirty( judging.X, judging.Y );
		Assert.AreEqual( 0, scene.Mesh.TakeJobs( jobs ), "one job in flight per tile" );
		new NavBuilder().Build( judging );
		scene.Mesh.Commit( [judging] );
		Assert.AreEqual( 1, scene.Mesh.TakeJobs( jobs ) );
		Assert.IsTrue( jobs[0].NeedsGeometry && (jobs[0].X, jobs[0].Y) == (judging.X, judging.Y) );
		jobs[0].SetGeometry( scene.Vertices.ToArray(), scene.Indices.ToArray() );
		new NavBuilder().Build( jobs[0] );
		scene.Mesh.Commit( jobs.AsSpan( 0, 1 ) );
		scene.Pump();
		Assert.AreEqual( 0, scene.Mesh.PendingTiles );
		AssertSame( Gap().Generate( Links ).Generated(), scene.Generated(), "against a generation" );
	}

	// C2: whatever order tiles are marked in, a generation is handed out row by row and holds two rows.
	[TestMethod]
	public void GenerationIsHandedOutRowByRowAndHoldsTwoRowsOfSpans()
	{
		const int tiles = 8;
		var scene = Field( tiles ).Start( Links );
		var order = new List<(int, int)>();
		for ( int y = 0; y < tiles; y++ )
		{
			for ( int x = 0; x < tiles; x++ ) order.Add( (x, y) );
		}
		var random = new Random( 3 );
		foreach ( var (x, y) in order.OrderBy( _ => random.Next() ) ) scene.Mesh.MarkDirty( x, y );

		var jobs = new TileJob[2];
		scene.Pump( 4 );
		CollectionAssert.AreEqual( order, scene.Order );
		Assert.AreEqual( 0, scene.Mesh.HeldFields );
		Assert.IsTrue( scene.PeakHeld <= 2 * tiles + 4 + 2, $"{scene.PeakHeld} fields held at once" );
		Assert.IsTrue( scene.Generated().Count > tiles * tiles );

		// Without links the order is the order of the marks, as before.
		var plain = new NavMesh( Settings, NavMeshOptions.Create( Settings ) );
		plain.MarkDirty( 3, 3 );
		plain.MarkDirty( 0, 0 );
		Assert.AreEqual( 2, plain.TakeJobs( jobs ) );
		Assert.AreEqual( (3, 3), (jobs[0].X, jobs[0].Y) );
		var sorted = new NavMesh( Settings, NavMeshOptions.Create( Settings ) with { Links = Links } );
		sorted.MarkDirty( 3, 3 );
		sorted.MarkDirty( 0, 0 );
		Assert.AreEqual( 2, sorted.TakeJobs( jobs ) );
		Assert.AreEqual( (0, 0), (jobs[0].X, jobs[0].Y) );
	}

	// C3: a box of tiles rebuilt at runtime has the links of a generation, in it and around it.
	[TestMethod]
	public void BoxOfTilesRebuiltAtRuntimeHasTheLinksOfAGeneration()
	{
		var reference = Field( 5 ).Generate( Links ).Generated();
		Assert.IsTrue( reference.Count( l => l.Type == Jump && ((int)(l.Start.X / 512) != (int)(l.End.X / 512) || (int)(l.Start.Y / 512) != (int)(l.End.Y / 512)) ) >= 8, "the scene has too few jumps across borders" );

		foreach ( var (x0, y0, x1, y1, batch) in new[] { (1, 1, 2, 2, 64), (1, 1, 3, 2, 1), (0, 0, 1, 1, 2), (2, 2, 2, 2, 1) } )
		{
			var scene = Field( 5 ).Generate( Links );
			for ( int y = y1; y >= y0; y-- )
			{
				for ( int x = x0; x <= x1; x++ ) scene.Mesh.MarkDirty( x, y );
			}
			scene.Pump( batch );
			Assert.IsTrue( scene.Asked == (x1 - x0 + 1) * (y1 - y0 + 1) );
			Assert.AreEqual( 0, scene.Mesh.HeldFields );
			var links = scene.Generated();
			AssertSame( reference, links, $"box {x0},{y0} to {x1},{y1}, missing" );
			AssertSame( links, reference, $"box {x0},{y0} to {x1},{y1}, not in a generation" );
			Assert.AreEqual( reference.Count, links.Count, $"box {x0},{y0} to {x1},{y1}" );
		}
	}

	internal static NavVolume Blocker( float x0, float y0, float x1, float y1, float z0 = -50, float z1 = 50 ) => new() { Shape = VolumeShape.Box, Area = NavFilter.NoArea, LocalToWorld = Matrix4x4.Identity, A = new Vector3( x0, y0, z0 ), B = new Vector3( x1, y1, z1 ) };

	// Rule 5: a changed volume keeps the links and takes out those it blocks, without triangles.
	[TestMethod]
	public void VolumeTakesOutTheLinksItBlocksAndGivesThemBackWithoutGeometry()
	{
		var scene = Platform().Generate( Links );
		var all = scene.Generated();
		static bool East( NavLink l ) => l.End.X > 320;
		static bool North( NavLink l ) => l.End.Y > 320;
		Assert.IsTrue( all.Any( East ) && all.Any( North ) );

		// Over the ground east of the platform. The first volume over a tile asks for its triangles once.
		int id = scene.Mesh.AddVolume( Blocker( 330, 100, 420, 380 ) );
		scene.Pump();
		Assert.AreEqual( 1, scene.Asked );
		Assert.IsFalse( scene.Generated().Any( East ), "a drop onto blocked ground" );
		Assert.IsTrue( scene.Generated().Any( North ) );

		scene.Mesh.SetVolume( id, Blocker( 100, 330, 380, 420 ) );
		scene.Pump();
		Assert.AreEqual( 0, scene.Asked );
		Assert.IsTrue( scene.Generated().Any( East ) );
		Assert.IsFalse( scene.Generated().Any( North ) );

		scene.Mesh.RemoveVolume( id );
		scene.Pump();
		Assert.AreEqual( 0, scene.Asked );
		AssertSame( all, scene.Generated(), "after the volume left" );
		Assert.AreEqual( all.Count, scene.Generated().Count );
	}

	// C4: a tile built from changed triangles under a blocker has all its links once the blocker leaves.
	[TestMethod]
	public void TileBuiltUnderABlockerHasAllItsLinksWhenTheBlockerLeaves()
	{
		var all = Platform().Generate( Links ).Generated();

		// The blocker covers the east half of the platform top and the ground east of it.
		var scene = Platform().Start( Links );
		int id = scene.Mesh.AddVolume( Blocker( 240, 100, 420, 380, -50, 150 ) );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Pump();
		Assert.AreEqual( 1, scene.Asked );
		Assert.IsFalse( scene.Generated().Any( l => l.End.X > 320 ) );
		Assert.IsTrue( scene.Generated().Any( l => l.End.X < 160 ) );

		scene.Mesh.RemoveVolume( id );
		scene.Pump();
		Assert.AreEqual( 0, scene.Asked, "the triangles were asked for again" );
		AssertSame( all, scene.Generated(), "after the blocker left" );
		Assert.AreEqual( all.Count, scene.Generated().Count );
	}

	// C6: a blocker that floats is judged the same inside a tile and across a border.
	[TestMethod]
	public void FloatingBlockerIsJudgedTheSameInsideATileAndAcrossABorder()
	{
		// Platforms with the gap inside tile 0, and with the border in the gap.
		static Scene Inside() => new Scene().Floor( 40, 40, 900, 470, 0 ).Box( 60, 160, 220, 320, 0, 100 ).Box( 340, 160, 500, 320, 0, 100 );
		static bool Jumps( NavLink l ) => l.Type == Jump && l.Start.Z > 90 && l.End.Z > 90;
		foreach ( var (make, middle) in new (Func<Scene>, float)[] { (Inside, 280), (Gap, 530) } )
		{
			Assert.IsTrue( make().Generate( Links ).Generated().Any( Jumps ) );

			// In the gap, clear of all ground, where the body of a jumping agent passes.
			var inTheWay = make().Start( Links );
			inTheWay.Mesh.AddVolume( Blocker( middle - 10, 100, middle + 10, 380, 110, 170 ) );
			inTheWay.Build();
			Assert.IsFalse( inTheWay.Generated().Any( Jumps ), $"a jump through the blocker at {middle}" );

			// More than the agent's height above the highest point of the jump.
			var above = make().Start( Links );
			above.Mesh.AddVolume( Blocker( middle - 10, 100, middle + 10, 380, 300, 340 ) );
			above.Build();
			Assert.IsTrue( above.Generated().Any( Jumps ), $"no jump under the blocker at {middle}" );
		}
	}

	// C7: link ids.
	[TestMethod]
	public void LinkIdsAreHandedOutLowestFirstUpToTheLimitAndAHighOneWorksInAPath()
	{
		var scene = new Scene().Floor( 40, 40, 470, 470, 0 ).Floor( 600, 40, 900, 470, 0 ).Start( default );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Mesh.MarkDirty( 1, 0 );
		scene.Pump();

		var nowhere = new NavLink { Start = new Vector3( 0, 0, 9000 ), End = new Vector3( 10, 0, 9000 ), Radius = 1, Area = NavFilter.DefaultArea };
		for ( int i = 0; i < 65534; i++ ) Assert.AreEqual( i, scene.Mesh.AddLink( nowhere ) );
		int last = scene.Mesh.AddLink( new NavLink { Start = new Vector3( 440, 240, 0 ), End = new Vector3( 630, 240, 0 ), Radius = 16, Bidirectional = true, Area = NavFilter.DefaultArea } );
		Assert.AreEqual( 65534, last );
		Assert.AreEqual( -1, scene.Mesh.AddLink( nowhere ) );

		scene.Mesh.RemoveLink( 70 );
		scene.Mesh.RemoveLink( 7 );
		scene.Mesh.RemoveLink( 7 );
		Assert.AreEqual( 7, scene.Mesh.AddLink( nowhere ) );
		Assert.AreEqual( 70, scene.Mesh.AddLink( nowhere ) );
		Assert.AreEqual( -1, scene.Mesh.AddLink( nowhere ) );
		scene.Mesh.Commit();

		var query = new NavQuery( scene.Mesh );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 100, 240, 0 ), new Vector3( 800, 240, 0 ), Snap, Filters.All, path ) );
		var link = path.Polys.ToArray().Single( p => p.IsLink );
		Assert.AreEqual( 65534, link.Link );
		Assert.IsTrue( query.IsValid( link, Filters.All ) );
		Assert.IsTrue( query.GetLink( link, new Vector3( 100, 240, 0 ), out var entry, out var exit ) );
		Assert.IsTrue( entry.X < 512 && exit.X > 512 );
		Assert.IsFalse( path.Polys.ToArray().Any( p => !p.IsLink && p.Link >= 0 ) );
	}

	[TestMethod]
	public void GeneratedLinksThatFindNoFreeIdAreCounted()
	{
		var scene = Platform().Start( Links );
		var nowhere = new NavLink { Start = new Vector3( 0, 0, 9000 ), End = new Vector3( 10, 0, 9000 ), Radius = 1, Area = NavFilter.DefaultArea };
		for ( int i = 0; i < 65533; i++ ) scene.Mesh.AddLink( nowhere );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Pump();
		int made = scene.Generated().Count, wanted = Platform().Generate( Links ).Generated().Count;
		Assert.AreEqual( 2, made );
		Assert.AreEqual( wanted - 2, scene.Mesh.LinksRefused );

		// With room again, the next build of the tile makes them all.
		for ( int i = 0; i < 100; i++ ) scene.Mesh.RemoveLink( i );
		scene.Mesh.MarkDirty( 0, 0 );
		scene.Pump();
		Assert.AreEqual( wanted, scene.Generated().Count );
		Assert.AreEqual( 0, scene.Mesh.LinksRefused );
	}

	[TestMethod]
	public void JumpHeightStartsAndEndsOnTheGroundAndHopsNoHigherThanMaxJumpUp()
	{
		Vector3 start = new( 0, 0, 100 ), end = new( 200, 0, 20 );
		Assert.AreEqual( 100, Links.JumpHeight( Settings, start, end, 0 ) );
		Assert.AreEqual( 20, Links.JumpHeight( Settings, start, end, 1 ), 0.001f );
		for ( float t = 0; t <= 1; t += 0.05f ) Assert.IsTrue( Links.JumpHeight( Settings, start, end, t ) <= 100 + Links.MaxJumpUp );
		// Level: a quarter of the distance, capped.
		Assert.AreEqual( 40, Links.JumpHeight( Settings, start, new Vector3( 200, 0, 100 ), 0.5f ) - 100, 0.001f );
		Assert.AreEqual( 20, Links.JumpHeight( Settings, start, new Vector3( 80, 0, 100 ), 0.5f ) - 100, 0.001f );
	}
}
