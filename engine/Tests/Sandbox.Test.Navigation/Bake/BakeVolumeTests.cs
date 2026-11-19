using System.Buffers;
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
	static readonly NavSettings Settings = NavSettings.Create( 64, 16, 18, 40, 4, 4, 64 );
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
		var mesh = new NavMesh( Settings );
		volume = Blocker( 128, 128 );
		mesh.AddVolume( volume );
		Assert.AreEqual( 0, mesh.Build( new NavBuilder(), Vertices, Indices ) );
		Assert.AreEqual( 4, mesh.GetStats().Tiles );
		Assert.IsTrue( Area( mesh, 0, 0 ) < Area( mesh, 1, 1 ) - 80 * 80 / 2, "The blocker cut the first tile" );
		Assert.AreNotEqual( 0ul, mesh.VolumeHashOf( 0, 0 ) );
		Assert.AreEqual( 0ul, mesh.VolumeHashOf( 1, 1 ) );

		var writer = new ArrayBufferWriter<byte>();
		Assert.IsTrue( mesh.Save( writer ) );
		var loaded = NavMesh.Load( writer.WrittenSpan )!;
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
		var again = NavMesh.Load( writer.WrittenSpan )!;
		again.AddVolume( volume );
		Assert.AreEqual( 0, Take( again ).Length );
	}

	[TestMethod]
	public void VolumesCompareByValue()
	{
		var volume = new NavVolume { Shape = VolumeShape.Capsule, Area = 3, LocalToWorld = Matrix4x4.CreateRotationZ( 0.5f ), A = new Vector3( 1, 2, 3 ), B = new Vector3( 4, 5, 6 ), Radius = 7, Margin = 8 };
		var same = volume;
		Assert.AreEqual( volume, same );
		Assert.IsTrue( volume == same );
		Assert.IsTrue( volume != default );
		Assert.AreNotEqual( volume, volume with { Shape = VolumeShape.Sphere } );
		Assert.AreNotEqual( volume, volume with { Area = 4 } );
		Assert.AreNotEqual( volume, volume with { LocalToWorld = Matrix4x4.CreateRotationZ( 0.6f ) } );
		Assert.AreNotEqual( volume, volume with { A = new Vector3( 1, 2, 4 ) } );
		Assert.AreNotEqual( volume, volume with { B = new Vector3( 4, 6, 6 ) } );
		Assert.AreNotEqual( volume, volume with { Radius = 7.5f } );
		Assert.AreNotEqual( volume, volume with { Margin = 0 } );
	}
}
