using System;
using System.Buffers;

namespace SceneTests.Components;

/// <summary>
/// Exercises terrain's pooled material conversion against native heightfield collision.
/// </summary>
[TestClass]
public class TerrainHeightfieldTests
{
	/// <summary>
	/// Non-power-of-two storage must collide with the same heights, surfaces and holes as
	/// power-of-two storage, even when the material pool supplies an oversized dirty array.
	/// </summary>
	[TestMethod]
	[DataRow( 64 )]
	[DataRow( 128 )]
	[DataRow( 129 )]
	[DataRow( 256 )]
	public void PooledTerrainMaterialsReachNativeCollision( int resolution )
	{
		var scene = new Scene();
		var first = new TestSurface();
		var second = new TestSurface();
		try
		{
			using var scope = scene.Push();
			var storage = new TerrainStorage();
			storage.SetResolution( resolution );
			storage.TerrainSize = resolution * 10;
			storage.TerrainHeight = 100;
			storage.Materials.Add( new TerrainMaterial { Surface = first } );
			storage.Materials.Add( new TerrainMaterial { Surface = second } );
			Array.Fill( storage.HeightMap, ushort.MaxValue );
			Array.Fill( storage.ControlMap, new CompactTerrainMaterial( 1, 3, 200, false ).Packed );
			storage.ControlMap[2 * resolution + 3] = new CompactTerrainMaterial( 0, 1, 100, false ).Packed;
			storage.ControlMap[5 * resolution + 7] = new CompactTerrainMaterial( 1, 0, 0, true ).Packed;

			var terrain = scene.CreateObject().Components.Create<Terrain>( false );
			terrain.Storage = storage;

			var rented = ArrayPool<byte>.Shared.Rent( storage.ControlMap.Length );
			try
			{
				if ( resolution == 129 )
					Assert.IsTrue( rented.Length > storage.ControlMap.Length, "Exercise excess pool capacity" );

				Array.Fill( rented, byte.MaxValue );
			}
			finally
			{
				ArrayPool<byte>.Shared.Return( rented );
			}

			terrain.Enabled = true;
			Assert.AreEqual( 1, terrain.Shapes.Count );

			// Reuse and overwrite the pool bucket after construction. Physics must own its copy.
			rented = ArrayPool<byte>.Shared.Rent( storage.ControlMap.Length );
			try
			{
				Array.Fill( rented, byte.MaxValue );
				Array.Clear( storage.HeightMap );
				Array.Clear( storage.ControlMap );

				SceneTraceResult Drop( int x, int y ) => scene.Trace.Ray(
					new Vector3( (x + 0.75f) * 10, (y + 0.75f) * 10, 200 ),
					new Vector3( (x + 0.75f) * 10, (y + 0.75f) * 10, -100 ) ).Run();

				var hit = Drop( 3, 2 );
				Assert.IsTrue( hit.Hit );
				Assert.AreEqual( 100f, hit.HitPosition.z, 0.1f );
				Assert.AreSame( first, hit.Surface, "Only the packed base material reaches physics" );
				Assert.IsFalse( Drop( 7, 5 ).Hit, "The hole bit becomes the native hole material" );

				var lastCell = Drop( resolution - 2, resolution - 2 );
				Assert.IsTrue( lastCell.Hit, "The last cell is solid despite the dirty pool tail" );
				Assert.AreSame( second, lastCell.Surface );
				Assert.AreEqual( 100f, lastCell.HitPosition.z, 0.1f );

				terrain.UpdateCollision( Terrain.SyncFlags.Height | Terrain.SyncFlags.Control,
					new RectInt( 0, 0, resolution, resolution ) );
				var updated = Drop( 7, 5 );
				Assert.IsTrue( updated.Hit, "Updating materials fills the hole" );
				Assert.AreSame( first, updated.Surface );
				Assert.AreEqual( 0f, updated.HitPosition.z, 0.1f, "Updating heights reaches physics" );
			}
			finally
			{
				ArrayPool<byte>.Shared.Return( rented );
			}
		}
		finally
		{
			scene.Destroy();
			Surface.All.Remove( first.Index );
			Surface.All.Remove( second.Index );
			Game.Resources.Unregister( first );
			Game.Resources.Unregister( second );
		}
	}

	/// <summary>
	/// Registers a unique surface so native trace results can verify material selection.
	/// </summary>
	sealed class TestSurface : Surface
	{
		/// <summary>
		/// Creates and registers a surface used only by this test.
		/// </summary>
		public TestSurface()
		{
			RegisterWeakResourceId( $"surfaces/{Guid.NewGuid():N}.surface" );
			PostLoad();
		}
	}
}
