using System.Buffers;
using System.Numerics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Nav.Lib.Tests;

/// <summary>
/// The split of a polygon id into tile slot and polygon, chosen per mesh by NavSettings.MaxTilePolygons.
/// </summary>
[TestClass]
public class SplitTests
{
	static NavSettings Limited( int polygons ) => GridMesh.Settings with { MaxTilePolygons = polygons };

	static NavMesh Mesh( in NavSettings settings ) => new( settings, NavMeshOptions.Create( settings ) );

	static NavMesh? Load( byte[] data ) => NavMesh.TryReadSettings( data, out var settings ) ? NavMesh.Load( data, NavMeshOptions.Create( settings ) ) : null;

	// The next new tile goes into this slot, as if the ones below were taken.
	static void SkipSlots( NavMesh mesh, int slot ) => typeof( NavMesh ).GetField( "slotCount", BindingFlags.NonPublic | BindingFlags.Instance )!.SetValue( mesh, slot );

	[TestMethod]
	public void SboxSettingsKeepTodaysLimits()
	{
		var settings = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = 256 } );
		Assert.AreEqual( 256, settings.TileSize );
		Assert.AreEqual( 16383, settings.MaxTilePolygons );
		Assert.AreEqual( 14, settings.PolyBits );

		var mesh = Mesh( settings );
		Assert.AreEqual( 262140, mesh.MaxTiles );
		Assert.AreEqual( 14, mesh.State.PolyBits );
		Assert.AreEqual( 0x3FFFu, mesh.State.PolyMask );
	}

	[TestMethod]
	public void CreateSetsTheLimitsFromTheTileSize()
	{
		// A polygon per four cells, so every tile size covers the same number of cells.
		(int Size, int Polygons, int Tiles)[] expected =
		[
			(32, 255, 16776960), (48, 575, 4194240), (64, 1023, 4194240), (128, 4095, 1048560),
			(256, 16383, 262140), (512, 65520, 65535), (1024, 65520, 65535)
		];
		foreach ( var (size, polygons, tiles) in expected )
		{
			var settings = NavSettings.Create( new() { AgentHeight = 64, AgentRadius = 16, AgentClimb = 18, AgentMaxSlope = 40, CellSize = 4, CellHeight = 4, TileSize = size } );
			Assert.AreEqual( polygons, settings.MaxTilePolygons, $"tile size {size}" );
			Assert.AreEqual( tiles, Mesh( settings ).MaxTiles, $"tile size {size}" );
		}
	}

	[TestMethod]
	public void TheSettingIsTheExactPolygonLimit()
	{
		(int Polygons, int Bits, int Tiles)[] expected = [(1, 1, 2147450880), (255, 8, 16776960), (256, 9, 8388480), (300, 9, 8388480), (16383, 14, 262140), (16384, 15, 131070), (65520, 16, 65535)];
		foreach ( var (polygons, bits, tiles) in expected )
		{
			var settings = Limited( polygons );
			Assert.IsTrue( settings.IsValid( out _ ) );
			Assert.AreEqual( polygons, settings.MaxTilePolygons );
			Assert.AreEqual( bits, settings.PolyBits );
			Assert.AreEqual( tiles, Mesh( settings ).MaxTiles );
		}
		Assert.IsFalse( Limited( -1 ).IsValid( out _ ) );
		Assert.IsFalse( Limited( 0 ).IsValid( out _ ) );
		Assert.IsFalse( Limited( 65521 ).IsValid( out var reason ) );
		StringAssert.Contains( reason, "MaxTilePolygons" );
	}

	[TestMethod]
	public void IdsRoundTripAtTheExtremesOfEverySplit()
	{
		for ( int bits = 1; bits <= 16; bits++ )
		{
			uint mask = (1u << bits) - 1;
			int maxTiles = PolyRef.MaxTiles( bits );
			// The link ids start right above the last tile slot.
			Assert.AreEqual( PolyRef.LinkIds, (uint)maxTiles << bits, $"bits {bits}" );

			foreach ( uint slot in new[] { 0u, 1u, (uint)maxTiles - 1 } )
			{
				foreach ( uint poly in new[] { 0u, mask - 1, mask } )
				{
					var reference = new PolyRef( uint.MaxValue, slot << bits | poly );
					Assert.IsFalse( reference.IsLink, $"bits {bits} slot {slot} poly {poly}" );
					Assert.AreEqual( -1, reference.Link );
					Assert.AreEqual( slot, reference.Id >> bits );
					Assert.AreEqual( poly, reference.Id & mask );
					Assert.AreEqual( uint.MaxValue, reference.Serial );
				}
			}

			foreach ( int link in new[] { 0, 1, PolyRef.MaxLinks - 1 } )
			{
				var reference = PolyRef.ForLink( 5, link );
				Assert.IsTrue( reference.IsLink );
				Assert.AreEqual( link, reference.Link );
				Assert.IsTrue( reference.Id >> bits >= (uint)maxTiles, "A link id is no tile slot" );
			}
		}
	}

	// A tile of exactly count polygons on floors 100 apart, at most 4096 to a floor.
	static Tile Floors( int x, int y, int count )
	{
		int floors = Math.Max( 2, (count + 4095) / 4096 );
		var layers = new GridMesh.Layer[floors];
		for ( int k = 0; k < floors; k++ )
		{
			int cells = count / floors + (k < count % floors ? 1 : 0);
			layers[k] = new GridMesh.Layer( k * 100, ( cx, cy ) => cy * GridMesh.Cells + cx < cells );
		}
		var tile = GridMesh.Tile( x, y, layers );
		Assert.AreEqual( count, tile.Polys.Length );
		return tile;
	}

	static Vector3 Centre( Tile tile, int poly )
	{
		ref readonly var p = ref tile.Polys[poly];
		var sum = Vector3.Zero;
		for ( int j = 0; j < p.VertCount; j++ ) sum += tile.Verts[p.Verts[j]];
		return sum / p.VertCount;
	}

	[TestMethod]
	[DataRow( 255 )]
	[DataRow( 1023 )]
	[DataRow( 16383 )]
	public void FullTileInTheLastSlotWorksWithLinksAndRejectsStaleRefs( int limit )
	{
		var mesh = Mesh( Limited( limit ) );
		int last = mesh.MaxTiles - 1;
		SkipSlots( mesh, last );
		Assert.AreEqual( 0, mesh.CommitTiles( [Floors( 0, 0, limit )] ) );
		var s = mesh.State;
		var tile = s.Find( 0, 0 )!;
		Assert.AreEqual( last, tile.Slot );

		// The last polygon, and a polygon on another floor that a link joins it to.
		int top = limit - 1;
		Vector3 end = Centre( tile, top ) + new Vector3( 0, 0, 1 );
		int other = 0;
		while ( MathF.Abs( Centre( tile, other ).Z - Centre( tile, top ).Z ) < 50 ) other++;
		Vector3 start = Centre( tile, other ) + new Vector3( 0, 0, 1 );
		int link = mesh.AddLink( new NavLink { Start = start, End = end, Radius = 4, Bidirectional = true, Area = 1 } );
		mesh.Commit();
		s = mesh.State;

		var query = new NavQuery( mesh );
		Assert.IsTrue( query.FindNearest( end, new Vector3( 2, 2, 2 ), Filters.All, out var topRef, out _ ) );
		Assert.IsFalse( topRef.IsLink );
		Assert.AreEqual( (uint)last, topRef.Id >> s.PolyBits );
		Assert.AreEqual( (uint)top, topRef.Id & s.PolyMask );

		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( start, end, GridMesh.Snap, Filters.All, path ) );
		Assert.AreEqual( topRef, path.Polys[^1] );
		var linkRef = default( PolyRef );
		foreach ( var entry in path.Polys )
		{
			if ( entry.IsLink ) linkRef = entry;
			else Assert.AreEqual( (uint)last, entry.Id >> s.PolyBits );
		}
		Assert.AreEqual( link, linkRef.Link );
		Assert.IsTrue( query.IsValid( linkRef, Filters.All ) );

		// Islands pack the slot into their edges
		var islands = NavQuery.GetIslands( s, Filters.All );
		Assert.AreEqual( islands.Of( topRef.Id ), islands.Of( path.Polys[0].Id ) );

		// The same tile built again: the old references name nothing, the new ones resolve.
		Assert.AreEqual( 0, mesh.CommitTiles( [Floors( 0, 0, limit )] ) );
		Assert.IsFalse( query.IsValid( topRef, Filters.All ) );
		Assert.IsFalse( query.IsValid( linkRef, Filters.All ) );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( start, end, GridMesh.Snap, Filters.All, path ) );
		Assert.AreNotEqual( topRef, path.Polys[^1] );
		Assert.AreEqual( topRef.Id, path.Polys[^1].Id, "Same slot and polygon, new serial" );
		Assert.IsTrue( query.IsValid( path.Polys[^1], Filters.All ) );

		// One polygon more does not build under this limit.
		Assert.IsNull( new TileAssembler().Create( 0, 0, tile.Verts, new Poly[limit + 1], Limited( limit ), out var error ) );
		StringAssert.Contains( error, $"the limit is {limit}" );
	}

	[TestMethod]
	public void SmallTilesGoPastTodaysTileLimit()
	{
		var mesh = Mesh( Limited( 255 ) );
		Assert.AreEqual( 16776960, mesh.MaxTiles );
		SkipSlots( mesh, 262139 );
		Assert.AreEqual( 0, mesh.CommitTiles( [GridMesh.Quad( 0, 0 ), GridMesh.Quad( 1, 0 ), GridMesh.Quad( 2, 0 )] ) );
		var s = mesh.State;
		Assert.AreEqual( 262141, s.Find( 2, 0 )!.Slot );

		var query = new NavQuery( mesh );
		var path = new NavPath();
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 10, 300, 0 ), new Vector3( 3 * GridMesh.TileWorld - 10, 300, 0 ), GridMesh.Snap, Filters.All, path ) );
		Assert.AreEqual( 262141u, path.Polys[^1].Id >> s.PolyBits );

		// The last slot is taken, after it the table is full.
		SkipSlots( mesh, mesh.MaxTiles - 1 );
		Assert.AreEqual( 1, mesh.CommitTiles( [GridMesh.Quad( 3, 0 ), GridMesh.Quad( 4, 0 )] ) );
		Assert.AreEqual( mesh.MaxTiles - 1, mesh.State.Find( 3, 0 )!.Slot );
		Assert.IsNull( mesh.State.Find( 4, 0 ) );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 10, 300, 0 ), new Vector3( 4 * GridMesh.TileWorld - 10, 300, 0 ), GridMesh.Snap, Filters.All, path ) );
	}

	[TestMethod]
	public void TheTileTableEndsWhereTheSplitSaysAtTheWidestPolygonSplit()
	{
		var mesh = Mesh( Limited( 65520 ) );
		Assert.AreEqual( 65535, mesh.MaxTiles );
		SkipSlots( mesh, 65534 );
		Assert.AreEqual( 0, mesh.CommitTiles( [GridMesh.Quad( 0, 0 )] ) );
		mesh.MarkDirty( 1, 0 );
		var jobs = new TileJob[1];
		Assert.AreEqual( 1, mesh.TakeJobs( jobs ) );
		jobs[0].Complete( TileJobStatus.Built, GridMesh.Quad( 1, 0 ) );
		Assert.AreEqual( 1, mesh.Commit( jobs ) );
		Assert.AreEqual( TileJobStatus.Failed, jobs[0].Status );
		StringAssert.Contains( jobs[0].Error, "65535 tiles" );

		// A box over more tiles than the mesh holds is a mistake at this split, not at the default one.
		Vector3 min = new( 0, 0, 0 ), max = new( 300 * GridMesh.TileWorld, 300 * GridMesh.TileWorld, 0 );
		Assert.ThrowsException<ArgumentException>( () => mesh.MarkDirty( min, max ) );
		GridMesh.Create().MarkDirty( min, max );
	}

	static byte[] Bake( NavMesh mesh )
	{
		var writer = new ArrayBufferWriter<byte>();
		Assert.IsTrue( mesh.Save( writer ) );
		return writer.WrittenSpan.ToArray();
	}

	[TestMethod]
	public void BakeKeepsTheSplit()
	{
		var mesh = Mesh( Limited( 255 ) );
		mesh.CommitTiles( [Floors( 0, 0, 255 ), GridMesh.Quad( 1, 0 )] );
		var data = Bake( mesh );
		Assert.IsTrue( NavMesh.TryReadSettings( data, out var settings ) );
		Assert.AreEqual( 255, settings.MaxTilePolygons );

		var loaded = Load( data )!;
		Assert.AreEqual( 8, loaded.State.PolyBits );
		Assert.AreEqual( mesh.MaxTiles, loaded.MaxTiles );
		CollectionAssert.AreEqual( data, Bake( loaded ) );
		var query = new NavQuery( loaded );
		Assert.AreEqual( PathStatus.Complete, query.FindPath( new Vector3( 5, 5, 1 ), new Vector3( 1.5f * GridMesh.TileWorld, 300, 0 ), GridMesh.Snap, Filters.All, new NavPath() ) );

		// A tile over the limit the bake's settings name is damage.
		var lowered = (byte[])data.Clone();
		BitConverter.TryWriteBytes( lowered.AsSpan( 48 ), 254 );
		Assert.IsTrue( NavMesh.TryReadSettings( lowered, out _ ) );
		Assert.IsNull( Load( lowered ) );
		var raised = (byte[])data.Clone();
		BitConverter.TryWriteBytes( raised.AsSpan( 48 ), 16383 );
		Assert.AreEqual( 14, Load( raised )!.State.PolyBits, "Bakes hold no ids: a wider polygon split loads the same tiles" );
	}

	[TestMethod]
	public void SmallSplitHoldsMoreTilesThanTheDefault()
	{
		var mesh = Mesh( Limited( 255 ) );
		var batch = new Tile[4096];
		int committed = 0, wanted = 262140 + 100;
		while ( committed < wanted )
		{
			int count = Math.Min( batch.Length, wanted - committed );
			for ( int i = 0; i < count; i++ ) batch[i] = GridMesh.Quad( (committed + i) % 1024, (committed + i) / 1024 );
			Assert.AreEqual( 0, mesh.CommitTiles( batch.AsSpan( 0, count ) ) );
			committed += count;
		}
		Assert.AreEqual( wanted, mesh.GetStats().Tiles );
		var query = new NavQuery( mesh );
		Assert.IsTrue( query.FindNearest( new Vector3( 50.5f * GridMesh.TileWorld, 256.5f * GridMesh.TileWorld, 0 ), GridMesh.Snap, Filters.All, out var poly, out _ ) );
		Assert.AreEqual( (uint)(256 * 1024 + 50), poly.Id >> mesh.State.PolyBits );
	}
}
