using System.Buffers;
using System.Buffers.Binary;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// A mesh of two agent types, humans and vehicles, over one street: a wall across it with a narrow gap
/// only humans fit through and a wide one both do, and a narrow ledge only humans stand on.
/// </summary>
[TestClass]
public class AgentTypeTests
{
	public static readonly NavSettings Humans = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 256 } );
	public static readonly NavSettings Vehicles = NavSettings.Create( new() { AgentHeight = 96, AgentRadius = 48, AgentClimb = 8, AgentMaxSlope = 20, CellSize = 8, CellHeight = 4, TileSize = 64 } );

	static NavAgentTypeDesc Desc( NavSettings settings ) => new() { Settings = settings, Options = NavMeshOptions.Create( settings ) };

	static readonly Vector3 West = new( 500, 430, 0 ), East = new( 1500, 430, 0 ), OnLedge = new( 1650, 1230, 100 ), BelowLedge = new( 1650, 1150, 0 );

	public static (Vector3[] Vertices, int[] Indices) Street()
	{
		var mesh = new MeshBuilder();
		mesh.Slab( new Vector3( 0, 0, -32 ), new Vector3( 2048, 2048, 0 ), 256 );
		// The wall at x 1000 to 1040: a gap 60 wide at y 400 and one 300 wide at y 1600.
		mesh.Box( new Vector3( 1000, 0, 0 ), new Vector3( 1040, 400, 150 ) );
		mesh.Box( new Vector3( 1000, 460, 0 ), new Vector3( 1040, 1600, 150 ) );
		mesh.Box( new Vector3( 1000, 1900, 0 ), new Vector3( 1040, 2048, 150 ) );
		// The ledge: 60 wide, 100 high.
		mesh.Box( new Vector3( 1600, 1200, 0 ), new Vector3( 1700, 1260, 100 ) );
		return (mesh.Vertices.ToArray(), mesh.Indices.ToArray());
	}

	public static NavMesh Both( NavLinkSettings? humanLinks = null, NavLinkSettings? vehicleLinks = null )
	{
		var (vertices, indices) = Street();
		var mesh = new NavMesh( [
			new NavAgentTypeDesc { Settings = Humans, Options = NavMeshOptions.Create( Humans ) with { Links = humanLinks ?? NavLinkSettings.Create( Humans ) } },
			new NavAgentTypeDesc { Settings = Vehicles, Options = NavMeshOptions.Create( Vehicles ) with { Links = vehicleLinks ?? NavLinkSettings.Create( Vehicles ) } }
		] );
		var failures = new List<(int AgentType, int X, int Y, string Error)>();
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), vertices, indices, failures ) );
		Assert.AreEqual( 0, failures.Count );
		return mesh;
	}

	static NavMesh One( NavSettings settings )
	{
		var (vertices, indices) = Street();
		var mesh = new NavMesh( settings, NavMeshOptions.Create( settings ) );
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), vertices, indices ) );
		return mesh;
	}

	static Vector3 Snap( NavSettings settings ) => new( 2.1f * settings.AgentRadius, 2.1f * settings.AgentRadius, 1.51f * settings.AgentHeight );

	[TestMethod]
	public void DeclaringTypes()
	{
		Assert.ThrowsException<ArgumentException>( () => new NavMesh( ReadOnlySpan<NavAgentTypeDesc>.Empty ) );
		var many = Enumerable.Repeat( Desc( Humans ), NavMesh.MaxAgentTypes + 1 ).ToArray();
		Assert.ThrowsException<ArgumentException>( () => new NavMesh( many ) );
		var bad = Vehicles with { TileSize = 30 };
		var error = Assert.ThrowsException<ArgumentException>( () => new NavMesh( [Desc( Humans ), new NavAgentTypeDesc { Settings = bad, Options = NavMeshOptions.Create( Humans ) }] ) );
		StringAssert.Contains( error.Message, "Agent type 1" );

		var mesh = new NavMesh( many.AsSpan( 0, NavMesh.MaxAgentTypes ) );
		Assert.AreEqual( NavMesh.MaxAgentTypes, mesh.AgentTypeCount );
		var two = new NavMesh( [Desc( Humans ), Desc( Vehicles )] );
		Assert.AreEqual( 2, two.AgentTypeCount );
		Assert.AreEqual( Vehicles, two.GetAgentType( 1 ).Settings );
		Assert.AreEqual( NavLinkSettings.Create( Vehicles ), two.GetAgentType( 1 ).Options.Links );
		Assert.AreEqual( Humans, new NavMesh( Humans, NavMeshOptions.Create( Humans ) ).GetAgentType( 0 ).Settings );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => two.GetAgentType( 2 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => two.GetAgentType( -1 ) );
	}

	[TestMethod]
	public void CallsWithoutATypeThrowOnSeveral()
	{
		var mesh = new NavMesh( [Desc( Humans ), Desc( Vehicles )] );
		var tiles = new (int, int)[4];
		Assert.ThrowsException<InvalidOperationException>( () => mesh.Settings );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.LinkSettings );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.GetTileAt( default ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.GetTiles( tiles ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.GetPolygons( 0, 0, [], [], [] ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.IsPending( 0, 0 ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.MarkDirty( 0, 0 ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.RemoveTile( 0, 0 ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.GetLinkEnds( 0, out _, out _, out _, out _ ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.GetLink( 0, out _, out _ ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.GetGeneratedLinks( new List<int>() ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.GetGeneratedLinks( 0, 0, new List<NavLink>() ) );
		Assert.ThrowsException<InvalidOperationException>( () => mesh.TakeLinkChanges( [] ) );
		Assert.ThrowsException<InvalidOperationException>( () => new NavQuery( mesh ) );
		Assert.ThrowsException<ArgumentException>( () => mesh.Build( new NavBuilder(), [], [], new List<(int, int, string)>() ) );

		Assert.ThrowsException<ArgumentOutOfRangeException>( () => new NavQuery( mesh, 2 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.GetTiles( 2, tiles ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.MarkDirty( -1, 0, 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => mesh.AddLink( 2, NavLink.Create( default, Vector3.One, 10, true ) ) );

		// The calls about the whole scene work.
		Assert.AreEqual( 0, mesh.GetStats().Tiles );
		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.AreEqual( 0, mesh.Commit() );
		Assert.AreEqual( 1, new NavQuery( mesh, 1 ).AgentType );
	}

	[TestMethod]
	public void EachTypeIsTheMeshOfItsOwn()
	{
		var both = Both();
		Assert.AreEqual( 0, both.PendingTiles );
		for ( int type = 0; type < 2; type++ )
		{
			var settings = type == 0 ? Humans : Vehicles;
			var alone = One( settings );
			Assert.AreEqual( alone.GetStats().Polygons, both.GetStats( type ).Polygons, $"Type {type}" );
			Assert.AreEqual( alone.GetStats().Tiles, both.GetStats( type ).Tiles );
			var tiles = new (int X, int Y)[alone.GetStats().Tiles];
			alone.GetTiles( tiles );
			foreach ( var (x, y) in tiles )
			{
				List<Vector3> va = [], vb = [];
				List<int> ca = [], cb = [];
				List<byte> aa = [], ab = [];
				alone.GetPolygons( x, y, va, ca, aa );
				both.GetPolygons( type, x, y, vb, cb, ab );
				CollectionAssert.AreEqual( va, vb, $"Type {type} tile {x},{y}" );
				CollectionAssert.AreEqual( ca, cb );
				CollectionAssert.AreEqual( aa, ab );
			}

			var path = new NavPath();
			var other = new NavPath();
			new NavQuery( alone ).FindPath( West, East, Snap( settings ), Filters.All, path );
			new NavQuery( both, type ).FindPath( West, East, Snap( settings ), Filters.All, other );
			Assert.AreEqual( path.Status, other.Status );
			CollectionAssert.AreEqual( path.Corners.ToArray(), other.Corners.ToArray() );
		}
		var sum = both.GetStats();
		Assert.AreEqual( both.GetStats( 0 ).Polygons + both.GetStats( 1 ).Polygons, sum.Polygons );
		Assert.AreEqual( both.GetStats( 0 ).RuntimeBytes + both.GetStats( 1 ).RuntimeBytes, sum.RuntimeBytes );
		Assert.AreNotEqual( both.GetTileAt( 0, East ), both.GetTileAt( 1, East ), "Each type has its own tile grid" );
	}

	[TestMethod]
	public void HumansTakeTheNarrowGapAndVehiclesGoRound()
	{
		var mesh = Both();
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, new NavQuery( mesh, 0 ).FindPath( West, East, Snap( Humans ), Filters.All, path ) );
		float walk = path.Length;
		Assert.IsTrue( walk < 1100, $"Humans walk {walk}" );
		Assert.AreEqual( PathStatus.Complete, new NavQuery( mesh, 1 ).FindPath( West, East, Snap( Vehicles ), Filters.All, path ) );
		Assert.IsTrue( path.Length > walk + 1500, $"Vehicles drive {path.Length}" );
	}

	[TestMethod]
	public void JobsOfBothTypesCommitTogether()
	{
		var (vertices, indices) = Street();
		var mesh = new NavMesh( [Desc( Humans ), Desc( Vehicles )] );
		mesh.MarkDirty( new Vector3( 0, 0, -50 ), new Vector3( 2048, 2048, 200 ) );
		Assert.IsTrue( mesh.IsPending( 0, 0, 0 ) && mesh.IsPending( 1, 3, 3 ) );
		var builder = new NavBuilder();
		var jobs = new TileJob[4];
		uint version = mesh.Version;
		int rounds = 0, both = 0;
		while ( true )
		{
			int count = mesh.TakeJobs( jobs );
			if ( count == 0 ) break;
			if ( rounds == 0 ) Assert.AreEqual( 2, jobs.Take( count ).Select( job => job.AgentType ).Distinct().Count(), "Both types get jobs" );
			for ( int i = 0; i < count; i++ )
			{
				var job = jobs[i];
				Assert.AreEqual( job.AgentType == 0 ? 1024 : 512, job.BoundsMax.X - job.BoundsMin.X - 2 * (job.AgentType == 0 ? 7 * 4 : 9 * 8), 0.01f );
				job.SetGeometry( vertices, indices );
				builder.Build( job );
			}
			int humans = mesh.GetStats( 0 ).Tiles, vehicles = mesh.GetStats( 1 ).Tiles;
			Assert.AreEqual( 0, mesh.Commit( jobs.AsSpan( 0, count ) ) );
			Assert.IsTrue( mesh.Version - version <= 1, "A commit is at most one version" );
			if ( mesh.GetStats( 0 ).Tiles != humans && mesh.GetStats( 1 ).Tiles != vehicles )
			{
				Assert.AreEqual( version + 1, mesh.Version, "One commit of both types is one version" );
				both++;
			}
			version = mesh.Version;
			rounds++;
		}
		Assert.AreEqual( 0, mesh.PendingTiles );
		Assert.IsTrue( both > 0 );
		Assert.AreEqual( One( Humans ).GetStats().Polygons, mesh.GetStats( 0 ).Polygons );
		Assert.AreEqual( One( Vehicles ).GetStats().Polygons, mesh.GetStats( 1 ).Polygons );

		mesh.RemoveTile( 1, 0, 0 );
		Assert.AreEqual( 0, mesh.Commit() );
		Assert.AreEqual( version + 1, mesh.Version );
		Assert.AreEqual( One( Vehicles ).GetStats().Tiles - 1, mesh.GetStats( 1 ).Tiles );
		Assert.AreEqual( One( Humans ).GetStats().Tiles, mesh.GetStats( 0 ).Tiles );
	}

	[TestMethod]
	public void ARefOfOneTypeIsNotValidInAnother()
	{
		var mesh = Both();
		var humans = new NavQuery( mesh, 0 );
		var vehicles = new NavQuery( mesh, 1 );
		Assert.IsTrue( humans.FindNearest( West, Snap( Humans ), Filters.All, out var human, out var at ) );
		Assert.IsTrue( vehicles.FindNearest( West, Snap( Vehicles ), Filters.All, out var vehicle, out _ ) );
		Assert.IsTrue( humans.IsValid( human, Filters.All ) );
		Assert.IsTrue( vehicles.IsValid( vehicle, Filters.All ) );
		Assert.IsFalse( vehicles.IsValid( human, Filters.All ) );
		Assert.IsFalse( humans.IsValid( vehicle, Filters.All ) );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.NotFound, vehicles.FindPath( human, at, human, at, Filters.All, path ) );
		Assert.IsFalse( vehicles.ClosestPoint( human, at, out _ ) );
	}

	[TestMethod]
	public void ALinkFitsOneTypeOnly()
	{
		var mesh = Both();
		var ladder = new NavLink { Start = BelowLedge, End = OnLedge, Radius = 16, Bidirectional = true, Area = 1 };
		int id = mesh.AddLink( ladder );
		Assert.AreEqual( 0, mesh.Commit() );
		Assert.IsTrue( mesh.GetLinkEnds( 0, id, out _, out _, out bool startConnected, out bool endConnected ) );
		Assert.IsTrue( startConnected && endConnected );
		Assert.IsTrue( mesh.GetLinkEnds( 1, id, out _, out _, out _, out endConnected ) );
		Assert.IsFalse( endConnected, "No vehicle polygon on the ledge" );
		Assert.IsTrue( mesh.GetLink( 1, id, out var link, out bool generated ) );
		Assert.AreEqual( ladder, link );
		Assert.IsFalse( generated );

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, new NavQuery( mesh, 0 ).FindPath( West, OnLedge, Snap( Humans ), Filters.All, path ) );
		Assert.IsTrue( path.Polys.ToArray().Any( poly => poly.Link == id ) );
		Assert.AreNotEqual( PathStatus.Complete, new NavQuery( mesh, 1 ).FindPath( West, OnLedge, new Vector3( 32 ), Filters.All, path ) );

		// A link of one type is not in the other, and its id is not handed out again.
		int only = mesh.AddLink( 0, ladder with { Start = BelowLedge + new Vector3( 30, 0, 0 ), End = OnLedge + new Vector3( 30, 0, 0 ) } );
		Assert.AreNotEqual( id, only );
		Assert.AreEqual( 0, mesh.Commit() );
		Assert.IsTrue( mesh.GetLink( 0, only, out _, out _ ) );
		Assert.IsFalse( mesh.GetLink( 1, only, out _, out _ ) );
		Assert.IsFalse( mesh.GetLinkEnds( 1, only, out _, out _, out _, out _ ) );

		// Set keeps its types, Remove takes it out of both.
		mesh.SetLink( only, ladder with { Radius = 20 } );
		mesh.RemoveLink( id );
		Assert.AreEqual( 0, mesh.Commit() );
		Assert.IsFalse( mesh.GetLink( 0, id, out _, out _ ) || mesh.GetLink( 1, id, out _, out _ ) );
		Assert.IsTrue( mesh.GetLink( 0, only, out link, out _ ) );
		Assert.AreEqual( 20, link.Radius );
		Assert.IsFalse( mesh.GetLink( 1, only, out _, out _ ) );
		Assert.ThrowsException<ArgumentException>( () => mesh.SetLink( id, ladder ) );
	}

	[TestMethod]
	public void AVolumeOverBothTypes()
	{
		var (vertices, indices) = Street();
		var mesh = new NavMesh( [Desc( Humans ), Desc( Vehicles )] );
		mesh.Build( new NavBuilder(), vertices, indices, new List<(int, int, int, string)>() );
		var humans = new NavQuery( mesh, 0 );
		var vehicles = new NavQuery( mesh, 1 );
		var path = new NavPath();

		// A blocker over the wide gap: vehicles cannot cross, humans still can.
		var blocker = new NavVolume { Shape = VolumeShape.Box, Area = NavFilter.NoArea, LocalToWorld = Matrix4x4.Identity, A = new Vector3( 950, 1550, -50 ), B = new Vector3( 1090, 1950, 300 ) };
		int id = mesh.AddVolume( blocker );
		var types = Pump( mesh, vertices, indices, out bool geometry );
		CollectionAssert.AreEquivalent( new[] { 0, 1 }, types, "Both types rebuild" );
		Assert.IsTrue( geometry, "The first volume over a tile asks for its triangles" );
		Assert.AreEqual( PathStatus.Complete, humans.FindPath( West, East, Snap( Humans ), Filters.All, path ) );
		Assert.AreEqual( PathStatus.Partial, vehicles.FindPath( West, East, Snap( Vehicles ), Filters.All, path ) );

		// Moved half off the gap, over the same tiles: rebuilt from the kept fields of both types.
		mesh.SetVolume( id, blocker with { LocalToWorld = Matrix4x4.CreateTranslation( 0, 250, 0 ) } );
		types = Pump( mesh, vertices, indices, out geometry );
		CollectionAssert.AreEquivalent( new[] { 0, 1 }, types );
		Assert.IsFalse( geometry );
		Assert.AreEqual( PathStatus.Complete, vehicles.FindPath( West, East, Snap( Vehicles ), Filters.All, path ) );

		// An area the vehicles' filter forbids, over the wide gap.
		mesh.SetVolume( id, blocker with { Area = 5 } );
		Pump( mesh, vertices, indices, out _ );
		var noFive = NavFilter.Create().WithForbiddenAreas( 1ul << 5 );
		Assert.AreEqual( PathStatus.Partial, vehicles.FindPath( West, East, Snap( Vehicles ), noFive, path ) );
		Assert.AreEqual( PathStatus.Complete, vehicles.FindPath( West, East, Snap( Vehicles ), Filters.All, path ) );
		Assert.AreEqual( PathStatus.Complete, humans.FindPath( West, East, Snap( Humans ), noFive, path ) );

		mesh.RemoveVolume( id );
		Pump( mesh, vertices, indices, out _ );
		Assert.AreEqual( PathStatus.Complete, vehicles.FindPath( West, East, Snap( Vehicles ), noFive, path ) );
		Assert.AreEqual( One( Vehicles ).GetStats().Polygons, mesh.GetStats( 1 ).Polygons );
		Assert.AreEqual( One( Humans ).GetStats().Polygons, mesh.GetStats( 0 ).Polygons );
	}

	// Runs the job loop until nothing is pending. Returns the types that had jobs.
	static List<int> Pump( NavMesh mesh, Vector3[] vertices, int[] indices, out bool geometry )
	{
		var builder = new NavBuilder();
		var jobs = new TileJob[16];
		var types = new List<int>();
		geometry = false;
		while ( true )
		{
			int count = mesh.TakeJobs( jobs );
			if ( count == 0 ) break;
			for ( int i = 0; i < count; i++ )
			{
				if ( !types.Contains( jobs[i].AgentType ) ) types.Add( jobs[i].AgentType );
				if ( jobs[i].NeedsGeometry )
				{
					geometry = true;
					jobs[i].SetGeometry( vertices, indices );
				}
				builder.Build( jobs[i] );
			}
			Assert.AreEqual( 0, mesh.Commit( jobs.AsSpan( 0, count ) ) );
		}
		return types;
	}

	[TestMethod]
	public void GeneratedLinksBelongToTheirType()
	{
		// Drops off the ledge's top are too narrow for vehicles; the street's are made for both off a dock.
		var mesh = new MeshBuilder();
		mesh.Slab( new Vector3( 0, 0, -32 ), new Vector3( 2048, 1024, 0 ), 256 );
		mesh.Box( new Vector3( 0, 0, 0 ), new Vector3( 800, 1024, 60 ) );
		var humanLinks = NavLinkSettings.Create( Humans ) with { MaxDrop = 300, DropType = 1, Insert = true };
		var vehicleLinks = NavLinkSettings.Create( Vehicles ) with { MaxDrop = 300, DropType = 2, Insert = true };
		var both = new NavMesh( [
			new NavAgentTypeDesc { Settings = Humans, Options = NavMeshOptions.Create( Humans ) with { Links = humanLinks } },
			new NavAgentTypeDesc { Settings = Vehicles, Options = NavMeshOptions.Create( Vehicles ) with { Links = vehicleLinks } }
		] );
		Assert.AreEqual( 0, both.Build( new NavBuilder(), mesh.Vertices.ToArray(), mesh.Indices.ToArray(), new List<(int, int, int, string)>() ) );
		Assert.AreEqual( 0, both.PendingTiles );

		List<int> human = [], vehicle = [];
		both.GetGeneratedLinks( 0, human );
		both.GetGeneratedLinks( 1, vehicle );
		Assert.IsTrue( human.Count > 0 && vehicle.Count > 0, $"{human.Count} and {vehicle.Count}" );
		Assert.AreEqual( 0, human.Intersect( vehicle ).Count(), "One id space" );
		foreach ( int id in human )
		{
			Assert.IsTrue( both.GetLink( 0, id, out var link, out bool generated ) && generated && link.Type == 1 );
			Assert.IsFalse( both.GetLink( 1, id, out _, out _ ) );
		}
		foreach ( int id in vehicle )
		{
			Assert.IsTrue( both.GetLink( 1, id, out var link, out _ ) && link.Type == 2 );
			Assert.IsFalse( both.GetLink( 0, id, out _, out _ ) );
		}
		var path = new NavPath();
		Vector3 top = new( 400, 512, 60 ), street = new( 1500, 512, 0 );
		Assert.AreEqual( PathStatus.Complete, new NavQuery( both, 0 ).FindPath( top, street, Snap( Humans ), Filters.All, path ) );
		Assert.AreEqual( PathStatus.Complete, new NavQuery( both, 1 ).FindPath( top, street, Snap( Vehicles ), Filters.All, path ) );

		// Reported instead of inserted: per type, added back to that type by the caller.
		var reported = new NavMesh( [
			new NavAgentTypeDesc { Settings = Humans, Options = NavMeshOptions.Create( Humans ) with { Links = humanLinks with { Insert = false } } },
			new NavAgentTypeDesc { Settings = Vehicles, Options = NavMeshOptions.Create( Vehicles ) with { Links = vehicleLinks with { Insert = false } } }
		] );
		reported.Build( new NavBuilder(), mesh.Vertices.ToArray(), mesh.Indices.ToArray(), new List<(int, int, int, string)>() );
		var changed = new List<(int X, int Y)>();
		Assert.IsTrue( reported.TakeLinkChanges( 1, changed ) > 0 );
		var links = new List<NavLink>();
		foreach ( var (x, y) in changed ) reported.GetGeneratedLinks( 1, x, y, links );
		Assert.IsTrue( links.All( link => link.Type == 2 ) );
		foreach ( var link in links ) reported.AddLink( 1, link );
		reported.Commit();
		Assert.AreEqual( PathStatus.Complete, new NavQuery( reported, 1 ).FindPath( top, street, Snap( Vehicles ), Filters.All, path ) );
		Assert.AreEqual( PathStatus.Partial, new NavQuery( reported, 0 ).FindPath( top, street, Snap( Humans ), Filters.All, path ), "The links added to the vehicles are not the humans'" );
	}

	[TestMethod]
	public void BakeOfTwoTypes()
	{
		var mesh = Both();
		var writer = new ArrayBufferWriter<byte>();
		Assert.IsTrue( mesh.Save( writer ) );
		var data = writer.WrittenSpan.ToArray();
		Assert.AreEqual( 4u, BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 4 ) ) );
		Assert.AreEqual( 2, BinaryPrimitives.ReadInt32LittleEndian( data.AsSpan( 8 ) ) );

		Assert.IsFalse( NavMesh.TryReadSettings( data, out _ ) );
		var settings = new List<NavSettings>();
		Assert.IsTrue( NavMesh.TryReadSettings( data, settings ) );
		CollectionAssert.AreEqual( new[] { Humans, Vehicles }, settings );
		Assert.IsNull( NavMesh.Load( data, NavMeshOptions.Create( Humans ) ) );
		Assert.IsNull( NavMesh.Load( data, [NavMeshOptions.Create( Humans )] ) );

		var loaded = NavMesh.Load( data, [NavMeshOptions.Create( Humans ), NavMeshOptions.Create( Vehicles )] );
		Assert.IsNotNull( loaded );
		Assert.AreEqual( 2, loaded.AgentTypeCount );
		var again = new ArrayBufferWriter<byte>();
		Assert.IsTrue( loaded.Save( again ) );
		CollectionAssert.AreEqual( data, again.WrittenSpan.ToArray() );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, new NavQuery( loaded, 1 ).FindPath( West, East, Snap( Vehicles ), Filters.All, path ) );

		// Each type's part is the bake that type's mesh alone writes: its settings and tile count, then its tiles.
		int at = 12 + 48 * 2;
		for ( int type = 0; type < 2; type++ )
		{
			var one = new ArrayBufferWriter<byte>();
			One( type == 0 ? Humans : Vehicles ).Save( one );
			var alone = one.WrittenSpan;
			Assert.AreEqual( 3u, BinaryPrimitives.ReadUInt32LittleEndian( alone[4..] ), "A mesh of one type writes version 3" );
			CollectionAssert.AreEqual( alone[8..56].ToArray(), data.AsSpan( 12 + 48 * type, 48 ).ToArray() );
			CollectionAssert.AreEqual( alone[56..].ToArray(), data.AsSpan( at, alone.Length - 56 ).ToArray() );
			at += alone.Length - 56;
		}
		Assert.AreEqual( data.Length, at );

		// A bake of one type loads through the list as well.
		var single = new ArrayBufferWriter<byte>();
		One( Humans ).Save( single );
		settings.Clear();
		Assert.IsTrue( NavMesh.TryReadSettings( single.WrittenSpan, settings ) );
		CollectionAssert.AreEqual( new[] { Humans }, settings );
		Assert.AreEqual( 1, NavMesh.Load( single.WrittenSpan, [NavMeshOptions.Create( Humans )] )!.AgentTypeCount );

		// Cut, padded, miscounted.
		Assert.IsNull( NavMesh.Load( data.AsSpan( 0, data.Length - 1 ), [NavMeshOptions.Create( Humans ), NavMeshOptions.Create( Vehicles )] ) );
		Assert.IsNull( NavMesh.Load( [.. data, 0], [NavMeshOptions.Create( Humans ), NavMeshOptions.Create( Vehicles )] ) );
		var miscounted = data.ToArray();
		BinaryPrimitives.WriteInt32LittleEndian( miscounted.AsSpan( 8 ), 1 );
		Assert.IsFalse( NavMesh.TryReadSettings( miscounted, settings ) );
		BinaryPrimitives.WriteInt32LittleEndian( miscounted.AsSpan( 8 ), 3 );
		Assert.IsNull( NavMesh.Load( miscounted, [NavMeshOptions.Create( Humans ), NavMeshOptions.Create( Vehicles ), NavMeshOptions.Create( Vehicles )] ) );
		var moved = data.ToArray();
		BinaryPrimitives.WriteInt32LittleEndian( moved.AsSpan( 12 + 44 ), BinaryPrimitives.ReadInt32LittleEndian( moved.AsSpan( 12 + 44 ) ) + 1 );
		Assert.IsNull( NavMesh.Load( moved, [NavMeshOptions.Create( Humans ), NavMeshOptions.Create( Vehicles )] ) );
	}

	[TestMethod]
	public void LoadedVersionIsTheOneOfALoadedMeshOfOneType()
	{
		var options = new[] { NavMeshOptions.Create( Humans ), NavMeshOptions.Create( Vehicles ) };
		var writer = new ArrayBufferWriter<byte>();
		Assert.IsTrue( Both().Save( writer ) );
		var both = NavMesh.Load( writer.WrittenSpan, options )!;
		writer.Clear();
		Assert.IsTrue( One( Humans ).Save( writer ) );
		var one = NavMesh.Load( writer.WrittenSpan, options[0] )!;
		Assert.AreEqual( 1u, one.Version, "A load publishes once" );
		Assert.AreEqual( one.Version, both.Version );

		// A bake without tiles publishes nothing.
		writer.Clear();
		Assert.IsTrue( new NavMesh( [Desc( Humans ), Desc( Vehicles )] ).Save( writer ) );
		var none = NavMesh.Load( writer.WrittenSpan, options )!;
		writer.Clear();
		Assert.IsTrue( new NavMesh( Humans, options[0] ).Save( writer ) );
		Assert.AreEqual( NavMesh.Load( writer.WrittenSpan, options[0] )!.Version, none.Version );
	}

	[TestMethod]
	public void BakedVolumesPerType()
	{
		var (vertices, indices) = Street();
		var mesh = new NavMesh( [Desc( Humans ), Desc( Vehicles )] );
		mesh.Build( new NavBuilder(), vertices, indices, new List<(int, int, int, string)>() );
		var blocker = new NavVolume { Shape = VolumeShape.Box, Area = NavFilter.NoArea, LocalToWorld = Matrix4x4.Identity, A = new Vector3( 950, 1550, -50 ), B = new Vector3( 1090, 1950, 300 ) };
		mesh.AddVolume( blocker );
		Pump( mesh, vertices, indices, out _ );
		var writer = new ArrayBufferWriter<byte>();
		mesh.Save( writer );
		var options = new[] { NavMeshOptions.Create( Humans ), NavMeshOptions.Create( Vehicles ) };

		// The same volume at load: nothing to rebuild.
		var loaded = NavMesh.Load( writer.WrittenSpan, options )!;
		loaded.AddVolume( blocker );
		Assert.AreEqual( 0, Pump( loaded, vertices, indices, out _ ).Count );

		// None: every tile of both types the volume was over is rebuilt.
		loaded = NavMesh.Load( writer.WrittenSpan, options )!;
		CollectionAssert.AreEquivalent( new[] { 0, 1 }, Pump( loaded, vertices, indices, out bool geometry ) );
		Assert.IsTrue( geometry );
		Assert.AreEqual( One( Vehicles ).GetStats().Polygons, loaded.GetStats( 1 ).Polygons );
	}

	[TestMethod]
	public void ASaveIsOneCommit()
	{
		var (vertices, indices) = Street();
		var mesh = Both();
		int humans = mesh.GetStats( 0 ).Tiles, vehicles = mesh.GetStats( 1 ).Tiles;
		using var stop = new CancellationTokenSource();
		string? problem = null;
		int saves = 0;
		var saver = new Thread( () =>
		{
			var writer = new ArrayBufferWriter<byte>();
			while ( !stop.IsCancellationRequested )
			{
				writer.Clear();
				mesh.Save( writer );
				int a = BinaryPrimitives.ReadInt32LittleEndian( writer.WrittenSpan[(12 + 44)..] ), b = BinaryPrimitives.ReadInt32LittleEndian( writer.WrittenSpan[(12 + 48 + 44)..] );
				if ( humans - a != vehicles - b ) problem ??= $"{a} human tiles and {b} vehicle tiles";
				saves++;
			}
		} );
		saver.Start();
		var builder = new NavBuilder();
		var jobs = new TileJob[4];
		for ( int round = 0; round < 200 && (round < 20 || saves < 50); round++ )
		{
			mesh.RemoveTile( 0, 1, 1 );
			mesh.RemoveTile( 1, 2, 2 );
			mesh.Commit();
			mesh.MarkDirty( 0, 1, 1 );
			mesh.MarkDirty( 1, 2, 2 );
			int count = mesh.TakeJobs( jobs );
			Assert.AreEqual( 2, count );
			for ( int i = 0; i < count; i++ )
			{
				jobs[i].SetGeometry( vertices, indices );
				builder.Build( jobs[i] );
			}
			mesh.Commit( jobs.AsSpan( 0, count ) );
		}
		stop.Cancel();
		saver.Join();
		Assert.IsNull( problem );
	}

	[TestMethod]
	public void HumansAndVehiclesInOneCrowd()
	{
		var mesh = Both();
		float[] Run( int workers )
		{
			var crowd = new NavCrowd( mesh, workers ) { Share = 1 };
			var human = AgentParams.Create( new AgentDesc { Radius = 16, Height = 64, MaxSpeed = 200, Acceleration = 800 } );
			var vehicle = AgentParams.Create( new AgentDesc { Radius = 48, Height = 96, MaxSpeed = 400, Acceleration = 800 } ) with { AgentType = 1 };
			var ids = new List<int>();
			for ( int i = 0; i < 12; i++ )
			{
				int id = crowd.Add( new Vector3( 300, 200 + i * 60, 0 ), human );
				crowd.MoveTo( id, new Vector3( 1700, 200 + i * 60, 0 ) );
				ids.Add( id );
			}
			for ( int i = 0; i < 4; i++ )
			{
				int id = crowd.Add( new Vector3( 1800, 300 + i * 200, 0 ), vehicle );
				crowd.MoveTo( id, new Vector3( 200, 300 + i * 200, 0 ) );
				ids.Add( id );
			}
			Action<int, Action<int>> run = ( count, body ) => Parallel.For( 0, count, body );
			for ( int frame = 0; frame < 60 * 40; frame++ )
			{
				crowd.Step( 1 / 60f, run );
				for ( int a = 0; a < crowd.States.Length; a++ )
				{
					for ( int b = 0; b < crowd.States.Length; b++ )
					{
						if ( crowd.Params[a].AgentType == crowd.Params[b].AgentType || a >= b ) continue;
						float gap = Vector2.Distance( new( crowd.States[a].Position.X, crowd.States[a].Position.Y ), new( crowd.States[b].Position.X, crowd.States[b].Position.Y ) );
						Assert.IsTrue( gap > 0.5f * (crowd.Params[a].Radius + crowd.Params[b].Radius), $"Agents {a} and {b} overlap: {gap}" );
					}
				}
				bool busy = false;
				foreach ( var state in crowd.States ) busy |= state.Status is AgentStatus.Pending or AgentStatus.Moving;
				if ( !busy ) break;
			}
			var result = new List<float>();
			foreach ( var state in crowd.States )
			{
				Assert.AreEqual( AgentStatus.Arrived, state.Status );
				result.Add( state.Position.X );
				result.Add( state.Position.Y );
			}
			return result.ToArray();
		}
		CollectionAssert.AreEqual( Run( 1 ), Run( 4 ) );
	}

	[TestMethod]
	public void CrowdRefusesATypeTheMeshDoesNotHave()
	{
		var mesh = Both();
		var crowd = new NavCrowd( mesh, 2 );
		var human = AgentParams.Create( new AgentDesc { Radius = 16, Height = 64, MaxSpeed = 200, Acceleration = 800 } );
		Assert.ThrowsException<ArgumentException>( () => crowd.Add( West, human with { AgentType = 2 } ) );
		int id = crowd.Add( West, human );
		Assert.ThrowsException<ArgumentException>( () => crowd.SetParams( id, human with { AgentType = 2 } ) );

		// Another type: planned again on the vehicles' tiles.
		crowd.MoveTo( id, East );
		crowd.Step( 1 / 60f );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, crowd.GetPath( id, path ) );
		float walk = path.Length;
		crowd.SetParams( id, human with { AgentType = 1, Radius = 48, Height = 96 } );
		crowd.Step( 1 / 60f );
		Assert.AreEqual( PathStatus.Complete, crowd.GetPath( id, path ) );
		Assert.IsTrue( path.Length > walk + 1000, $"{walk} then {path.Length}" );
		Assert.IsTrue( new NavQuery( mesh, 1 ).IsValid( crowd.States[id].Poly, Filters.All ) );

		// A mesh of one type does not have type 1.
		Assert.ThrowsException<ArgumentException>( () => crowd.SetMesh( One( Humans ) ) );
		Assert.IsTrue( new NavQuery( mesh, 1 ).IsValid( crowd.States[id].Poly, Filters.All ), "Nothing changed" );
		crowd.SetParams( id, human );
		crowd.SetMesh( One( Humans ) );
	}
}
