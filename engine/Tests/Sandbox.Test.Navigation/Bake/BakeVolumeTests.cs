using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// A baked mesh and volumes together: the volume-set hash a tile was built with travels in the bake,
/// the rebuild cache does not.
/// </summary>
[TestClass]
public class BakeVolumeTests
{
	// Four tiles of 256 units under one floor.
	static readonly NavSettings Settings = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 64 } );
	static readonly Vector3[] Vertices = [new( 0, 0, 0 ), new( 512, 0, 0 ), new( 512, 512, 0 ), new( 0, 512, 0 )];
	static readonly int[] Indices = [0, 1, 2, 0, 2, 3];

	static NavVolume Blocker( float x, float y, float half = 40 ) => new()
	{
		Shape = VolumeShape.Box,
		Area = 0,
		LocalToWorld = Matrix4x4.CreateTranslation( x, y, 0 ),
		A = new Vector3( -half, -half, -50 ),
		B = new Vector3( half, half, 50 )
	};

	static TileJob[] Take( NavMesh mesh )
	{
		var jobs = new TileJob[16];
		return jobs[..mesh.TakeJobs( jobs )];
	}

	static float Area( NavMesh mesh, int x, int y ) => mesh.State.Find( x, y )?.Area ?? 0;

	static NavMesh BakedAndLoaded( out NavVolume volume )
	{
		var mesh = new NavMesh( Settings, NavMeshOptions.Create( Settings ) );
		volume = Blocker( 128, 128 );
		mesh.AddVolume( volume );
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), Vertices, Indices ) );
		Assert.AreEqual( 4, mesh.GetStats().Tiles );
		Assert.IsTrue( Area( mesh, 0, 0 ) < Area( mesh, 1, 1 ) - 80 * 80 / 2, "The blocker cut the first tile" );
		Assert.AreNotEqual( 0ul, mesh.VolumeHashOf( 0, 0 ) );
		Assert.AreEqual( 0ul, mesh.VolumeHashOf( 1, 1 ) );

		var writer = new ArrayBufferWriter<byte>();
		Assert.IsTrue( mesh.Save( writer ) );
		var loaded = BakeTests.Load( writer.WrittenSpan )!;
		Assert.IsNotNull( loaded );
		Assert.AreEqual( 0, loaded.GetStats().CacheBytes, "A bake carries no rebuild cache" );
		Assert.AreEqual( 0, loaded.PendingTiles );
		for ( int y = 0; y < 2; y++ )
			for ( int x = 0; x < 2; x++ )
			{
				Assert.AreEqual( Area( mesh, x, y ), Area( loaded, x, y ) );
				Assert.AreEqual( mesh.VolumeHashOf( x, y ), loaded.VolumeHashOf( x, y ) );
			}
		return loaded;
	}

	[TestMethod]
	public void SameVolumesAfterLoadProduceNoJob()
	{
		var loaded = BakedAndLoaded( out var volume );
		uint version = loaded.Version;
		loaded.AddVolume( volume );
		Assert.AreEqual( 0, Take( loaded ).Length, "The tile was baked with this volume set" );
		Assert.AreEqual( 0, loaded.PendingTiles );
		Assert.AreEqual( 0, loaded.Commit() );
		Assert.AreEqual( version, loaded.Version );
	}

	[TestMethod]
	public void LoadedTileWithoutItsBakedVolumeAsksForGeometry()
	{
		// The scene no longer has the volume the tile was baked with: the first take finds that out and
		// hands the tile out to be rebuilt from geometry.
		var loaded = BakedAndLoaded( out _ );
		var jobs = Take( loaded );
		Assert.AreEqual( 1, jobs.Length );
		Assert.AreEqual( (0, 0), (jobs[0].X, jobs[0].Y) );
		Assert.IsTrue( jobs[0].NeedsGeometry );
		new NavBuilder().Build( jobs[0], Vertices, Indices );
		Assert.AreEqual( 0, loaded.Commit( jobs ) );
		Assert.AreEqual( Area( loaded, 1, 1 ), Area( loaded, 0, 0 ), 1f, "Without the blocker the tile is whole again" );
		Assert.AreEqual( 0ul, loaded.VolumeHashOf( 0, 0 ) );
	}

	[TestMethod]
	public void NewVolumeOnALoadedTileNeedsGeometryOnceThenRebuildsFromItsField()
	{
		var loaded = BakedAndLoaded( out var volume );
		loaded.AddVolume( volume );
		var builder = new NavBuilder();
		float whole = Area( loaded, 1, 1 );
		uint serial = loaded.State.Find( 1, 1 )!.Serial;

		// A volume the bake did not know, over a tile that has no cached field after the load.
		int added = loaded.AddVolume( Blocker( 384, 384 ) );
		var jobs = Take( loaded );
		Assert.AreEqual( 1, jobs.Length, "Only the tile under the new volume" );
		Assert.AreEqual( (1, 1), (jobs[0].X, jobs[0].Y) );
		Assert.IsTrue( jobs[0].NeedsGeometry, "No cached field exists after a load" );

		// Built without the geometry it asked for, the job fails and the old tile stays.
		builder.Build( jobs[0] );
		Assert.AreEqual( TileJobStatus.Failed, jobs[0].Status );
		Assert.AreEqual( 1, loaded.Commit( jobs ) );
		Assert.AreEqual( serial, loaded.State.Find( 1, 1 )!.Serial );
		Assert.AreEqual( 0, loaded.PendingTiles, "A failed tile waits for the next mark" );

		// Marked again and given the triangles, it rebuilds and keeps its field from now on.
		loaded.SetVolume( added, Blocker( 384, 384 ) );
		jobs = Take( loaded );
		Assert.AreEqual( 1, jobs.Length );
		Assert.IsTrue( jobs[0].NeedsGeometry );
		builder.Build( jobs[0], Vertices, Indices );
		Assert.AreEqual( TileJobStatus.Built, jobs[0].Status, jobs[0].Error );
		Assert.AreEqual( 0, loaded.Commit( jobs ) );
		float cut = Area( loaded, 1, 1 );
		Assert.IsTrue( cut < whole - 80 * 80 / 2, $"The new blocker cut the tile: {whole} to {cut}" );
		Assert.IsTrue( loaded.GetStats().CacheBytes > 0, "The tile has a cached field now" );
		Assert.AreNotEqual( 0ul, loaded.VolumeHashOf( 1, 1 ) );

		// A later change to the volume rebuilds from that field, without geometry.
		loaded.SetVolume( added, Blocker( 400, 400, 20 ) );
		jobs = Take( loaded );
		Assert.AreEqual( 1, jobs.Length );
		Assert.IsFalse( jobs[0].NeedsGeometry, "The cached field serves a volume change" );
		builder.Build( jobs[0] );
		Assert.AreEqual( TileJobStatus.Built, jobs[0].Status, jobs[0].Error );
		Assert.AreEqual( 0, loaded.Commit( jobs ) );
		Assert.IsTrue( Area( loaded, 1, 1 ) > cut + 40 * 40 && Area( loaded, 1, 1 ) < whole - 40 * 40 / 2, $"A smaller blocker: {cut} to {Area( loaded, 1, 1 )} of {whole}" );

		// Removing it gives the whole tile back, again from the field, and the other tiles never moved.
		loaded.RemoveVolume( added );
		jobs = Take( loaded );
		Assert.AreEqual( 1, jobs.Length );
		Assert.IsFalse( jobs[0].NeedsGeometry );
		builder.Build( jobs[0] );
		Assert.AreEqual( 0, loaded.Commit( jobs ) );
		Assert.AreEqual( whole, Area( loaded, 1, 1 ), 1f );
		Assert.AreEqual( 0, loaded.PendingTiles );

		// The loaded mesh bakes again, with the hashes of what its tiles are built with now.
		var writer = new ArrayBufferWriter<byte>();
		Assert.IsTrue( loaded.Save( writer ) );
		var again = BakeTests.Load( writer.WrittenSpan )!;
		again.AddVolume( volume );
		Assert.AreEqual( 0, Take( again ).Length );
	}

	static void BuildAll( NavMesh mesh, TileJob[] jobs )
	{
		var builder = new NavBuilder();
		foreach ( var job in jobs ) builder.Build( job, Vertices, Indices );
		Assert.AreEqual( 0, mesh.Commit( jobs ) );
	}

	[TestMethod]
	public void TileEmptiedByABlockerComesBackAfterLoad()
	{
		// The blocker takes all ground of tile 0,0: the tile has no polygons, only the volume set it was built with.
		var mesh = new NavMesh( Settings, NavMeshOptions.Create( Settings ) );
		var blocker = Blocker( 128, 128, 170 );
		mesh.AddVolume( blocker );
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), Vertices, Indices ) );
		Assert.IsNull( mesh.State.Find( 0, 0 ) );
		Assert.AreEqual( 3, mesh.GetStats().Tiles );
		ulong hash = mesh.VolumeHashOf( 0, 0 );
		Assert.AreNotEqual( 0ul, hash );
		var writer = new ArrayBufferWriter<byte>();
		Assert.IsTrue( mesh.Save( writer ) );
		var bake = writer.WrittenSpan.ToArray();

		// Four records: first the place without polygons, at 0,0, as its hash alone. One with a hash of 0 is refused.
		const int Header = 56;
		Assert.AreEqual( 4, BinaryPrimitives.ReadInt32LittleEndian( bake.AsSpan( Header - 4 ) ) );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 1, 0 }, bake[Header..(Header + 4)] );
		Assert.AreEqual( hash, BinaryPrimitives.ReadUInt64LittleEndian( bake.AsSpan( Header + 4 ) ) );
		var broken = bake.ToArray();
		broken.AsSpan( Header + 4, 8 ).Clear();
		Assert.IsNull( BakeTests.Load( broken ) );

		// Loaded under the same blocker, nothing is built.
		var same = BakeTests.Load( bake )!;
		Assert.AreEqual( hash, same.VolumeHashOf( 0, 0 ) );
		Assert.AreEqual( 3, same.GetStats().Tiles );
		same.AddVolume( blocker );
		Assert.AreEqual( 0, Take( same ).Length );
		var again = new ArrayBufferWriter<byte>();
		Assert.IsTrue( same.Save( again ) );
		CollectionAssert.AreEqual( bake, again.WrittenSpan.ToArray(), "The loaded mesh bakes the same bytes" );

		// Without the blocker the first take rebuilds the tile from geometry, and it is whole again.
		var gone = BakeTests.Load( bake )!;
		var jobs = Take( gone );
		Assert.IsTrue( jobs.Any( j => (j.X, j.Y) == (0, 0) && j.NeedsGeometry ), "The emptied tile is rebuilt" );
		BuildAll( gone, jobs );
		Assert.IsNotNull( gone.State.Find( 0, 0 ) );
		Assert.AreEqual( 0ul, gone.VolumeHashOf( 0, 0 ) );
		Assert.AreEqual( 0, gone.PendingTiles );

		// A smaller blocker after the load gives part of the tile back.
		var shrunk = BakeTests.Load( bake )!;
		shrunk.AddVolume( Blocker( 128, 128, 40 ) );
		jobs = Take( shrunk );
		Assert.IsTrue( jobs.Any( j => (j.X, j.Y) == (0, 0) ), "The emptied tile is rebuilt under the smaller blocker" );
		BuildAll( shrunk, jobs );
		Assert.IsTrue( Area( shrunk, 0, 0 ) > 0 && Area( shrunk, 0, 0 ) < Area( shrunk, 1, 1 ) );
	}

	static (ulong Hash, int Polygons) Place( NavMesh mesh, int x, int y ) => (mesh.VolumeHashOf( x, y ), mesh.State.Find( x, y )?.Polys.Length ?? 0);

	[TestMethod]
	public void SaveDuringCommitsPairsEveryTileWithItsOwnVolumeSet()
	{
		// An area over everything, so every tile has a volume set, and a blocker toggled on tile 0,0 while
		// another thread saves: every bake holds tile 0,0 open with the open set or blocked with the blocked set.
		var mesh = new NavMesh( Settings, NavMeshOptions.Create( Settings ) );
		mesh.AddVolume( new NavVolume { Shape = VolumeShape.Box, Area = 5, LocalToWorld = Matrix4x4.Identity, A = new Vector3( -100, -100, -50 ), B = new Vector3( 600, 600, 50 ) } );
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), Vertices, Indices ) );
		var open = Place( mesh, 0, 0 );
		int id = mesh.AddVolume( Blocker( 128, 128 ) );
		BuildAll( mesh, Take( mesh ) );
		var blocked = Place( mesh, 0, 0 );
		Assert.AreNotEqual( open.Hash, blocked.Hash );
		Assert.AreNotEqual( open.Polygons, blocked.Polygons );

		bool stop = false;
		int saves = 0;
		string? wrong = null;
		var saver = new Thread( () =>
		{
			var writer = new ArrayBufferWriter<byte>();
			while ( !Volatile.Read( ref stop ) )
			{
				writer.Clear();
				if ( !mesh.Save( writer ) ) wrong ??= "Save refused";
				var place = Place( BakeTests.Load( writer.WrittenSpan )!, 0, 0 );
				if ( place != open && place != blocked ) wrong ??= $"{place} is neither {open} nor {blocked}";
				saves++;
			}
		} );
		saver.Start();
		var watch = Stopwatch.StartNew();
		int toggles = 0;
		for ( ; watch.ElapsedMilliseconds < 500 || saves < 50; toggles++ )
		{
			if ( id >= 0 )
			{
				mesh.RemoveVolume( id );
				id = -1;
			}
			else id = mesh.AddVolume( Blocker( 128, 128 ) );
			BuildAll( mesh, Take( mesh ) );
		}
		Volatile.Write( ref stop, true );
		saver.Join();
		Assert.IsNull( wrong, $"{wrong}: {toggles} toggles, {saves} saves" );
	}

	[TestMethod]
	public void VolumeHashIsTheOneBakesFromBeforeDecision77Have()
	{
		// FNV-1a as Fnv.Hash makes it over shape, area, the matrix, A, B, radius and a margin of 0 (98 bytes),
		// worked out by hand: an area volume, or a blocker that had no margin, keeps the hash its old bakes hold.
		var mesh = new NavMesh( Settings, NavMeshOptions.Create( Settings ) );
		mesh.AddVolume( new NavVolume { Shape = VolumeShape.Box, Area = 3, LocalToWorld = Matrix4x4.CreateTranslation( 100, 200, 0 ), A = new Vector3( -10, -20, -30 ), B = new Vector3( 10, 20, 30 ) } );
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), Vertices, Indices ) );
		Assert.AreEqual( 0xFE421582EAC58527ul, mesh.VolumeHashOf( 0, 0 ) );
	}

	[TestMethod]
	public void MarginIsHashedWhereItWasBeforeDecision77()
	{
		// A margin of 0 set on purpose is the hash above. A margin of 16 is the hash worked out by hand over the same
		// bytes with 16 as the last float, as bakes from before decision 77 hold it
		var volume = new NavVolume { Shape = VolumeShape.Box, Area = 3, LocalToWorld = Matrix4x4.CreateTranslation( 100, 200, 0 ), A = new Vector3( -10, -20, -30 ), B = new Vector3( 10, 20, 30 ) };
		static ulong HashOf( NavVolume v )
		{
			var mesh = new NavMesh( Settings, NavMeshOptions.Create( Settings ) );
			mesh.AddVolume( v );
			Assert.AreEqual( 0, mesh.Build( new NavBuilder(), Vertices, Indices ) );
			return mesh.VolumeHashOf( 0, 0 );
		}
		Assert.AreEqual( 0xFE421582EAC58527ul, HashOf( volume with { Margin = 0 } ) );
		Assert.AreEqual( 0xFC8F5482E9545BB4ul, HashOf( volume with { Margin = 16 } ) );
	}

	[TestMethod]
	public void BlockerWithAMarginKeepsItsTilesAfterLoad()
	{
		var mesh = new NavMesh( Settings, NavMeshOptions.Create( Settings ) );
		var blocker = Blocker( 128, 128 ) with { Margin = 16 };
		mesh.AddVolume( blocker );
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), Vertices, Indices ) );
		Assert.IsTrue( Area( mesh, 0, 0 ) < Area( mesh, 1, 1 ) - 112 * 112 + 80 * 80 / 2, "The margin cut more of the tile" );
		var writer = new ArrayBufferWriter<byte>();
		Assert.IsTrue( mesh.Save( writer ) );

		// Under the same blocker nothing is built; without its margin the tile is built again
		var same = BakeTests.Load( writer.WrittenSpan )!;
		same.AddVolume( blocker );
		Assert.AreEqual( 0, Take( same ).Length );
		Assert.AreEqual( Area( mesh, 0, 0 ), Area( same, 0, 0 ) );

		var smaller = BakeTests.Load( writer.WrittenSpan )!;
		smaller.AddVolume( blocker with { Margin = 0 } );
		var jobs = Take( smaller );
		Assert.AreEqual( 1, jobs.Length );
		Assert.AreEqual( (0, 0), (jobs[0].X, jobs[0].Y) );
		BuildAll( smaller, jobs );
		Assert.IsTrue( Area( smaller, 0, 0 ) > Area( mesh, 0, 0 ) + 100 * 100 - 80 * 80, $"{Area( smaller, 0, 0 )} against {Area( mesh, 0, 0 )}" );
	}
}
