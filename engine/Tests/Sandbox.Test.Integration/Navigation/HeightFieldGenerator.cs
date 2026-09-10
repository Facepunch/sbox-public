using Sandbox.Navigation.Generation;
using System;

namespace NavigationTests
{
	[TestClass]
	public class HeightFieldGeneratorTest
	{
		private static Config MakeConfig( BBox bounds )
		{
			// Re-use parameters similar to existing Navigation tests.
			return Config.CreateValidatedConfig(
				new Vector2Int( 0, 0 ),
				bounds,
				cellSize: 4.0f,
				cellHeight: 2.0f,
				agentHeight: 64.0f,
				agentRadius: 16.0f,
				agentStepSize: 18.0f,
				agentMaxSlope: 40.0f
			);
		}

		[TestMethod]
		public void InputFilter_MarkWalkableTriangles_Basic()
		{
			// Triangle lying on y=0 plane with upward normal -> walkable for slope 45 degrees
			var verts = new Vector3[]
			{
				new(0,0,0),
				new(1,0,0),
				new(0,0,-1)
			};
			var walkableTri = new int[] { 0, 1, 2 };      // Winding producing (0,1,0) normal
			var unwalkableTri = new int[] { 0, 2, 1 };    // Reverse winding -> downward normal

			var areas = new int[1];

			InputFilter.MarkWalkableTriangles( 45.0f, verts, walkableTri, areas );
			Assert.AreEqual( Constants.WALKABLE_AREA, areas[0], "Expected triangle to be walkable" );

			InputFilter.MarkWalkableTriangles( 45.0f, verts, unwalkableTri, areas );
			Assert.AreEqual( Constants.NULL_AREA, areas[0], "Expected triangle to be unwalkable" );

			// NOTE: Implementation resets all areas each call, so non-walkable triangles overwrite previous values.
			areas[0] = 1337;
			InputFilter.MarkWalkableTriangles( 45.0f, verts, unwalkableTri, areas );
			Assert.AreEqual( Constants.NULL_AREA, areas[0], "Implementation clears areas for non-walkable triangles" );

			// Slope angle 0 => strictly normal.y > 1.0 required (impossible) so unwalkable.
			InputFilter.MarkWalkableTriangles( 0.0f, verts, walkableTri, areas );
			Assert.AreEqual( Constants.NULL_AREA, areas[0], "Slope equal to 0 should treat flat surfaces as unwalkable due to strict > comparison" );
		}

		[TestMethod]
		public void Heightfield_AddOrMergeSpan_MergesAdjacent()
		{
			using var hf = new Heightfield(
				sizeX: 1,
				sizeZ: 1,
				minBounds: new Vector3( 0, 0, 0 ),
				maxBounds: new Vector3( 10, 10, 10 ),
				cellSize: 1.0f,
				cellHeight: 1.0f
			);

			// First span
			hf.AddOrMergeSpan( 0, 0, sMin: 0, sMax: 10, areaId: 1, flagMergeThreshold: 0 );
			Assert.AreEqual( 1, hf.TotalSpanCount );

			// Adjacent touching span (10..20) should merge into single (0..20)
			hf.AddOrMergeSpan( 0, 0, sMin: 10, sMax: 20, areaId: 2, flagMergeThreshold: 0 );
			Assert.AreEqual( 1, hf.TotalSpanCount, "Spans should have merged (still one span)" );

			hf.EnsureCompressed();
			var col = hf.GetColumn( 0 );
			Assert.AreEqual( 1, col.Length );
			Assert.AreEqual( (ushort)0, col[0].MinY );
			Assert.AreEqual( (ushort)20, col[0].MaxY );
			// Area merge rule used flagMergeThreshold; here difference large so area stays new (2)
			Assert.AreEqual( 2, col[0].Area );
		}

		[TestMethod]
		public void Heightfield_GrowColumns_DoesNotFail()
		{
			using var hf = new Heightfield(
				sizeX: 1,
				sizeZ: 1,
				minBounds: new Vector3( 0, 0, 0 ),
				maxBounds: new Vector3( 10, 10, 10 ),
				cellSize: 1.0f,
				cellHeight: 1.0f
			);

			// Fill more than initial capacity (64) with non-overlapping spans to force growth.
			for ( int i = 0; i < 65; i++ )
			{
				ushort min = (ushort)(i * 3);
				ushort max = (ushort)(min + 2);
				hf.AddOrMergeSpan( 0, 0, min, max, Constants.WALKABLE_AREA, flagMergeThreshold: 0 );
			}

			Assert.AreEqual( 65, hf.TotalSpanCount );
			hf.EnsureCompressed();
			var col = hf.GetColumn( 0 );
			Assert.AreEqual( 65, col.Length, "All spans should remain distinct (no overlap)" );
		}

		[TestMethod]
		public void Rasterization_RasterizeTriangle_Basic()
		{
			using var hf = new Heightfield(
				sizeX: 2,
				sizeZ: 2,
				minBounds: new Vector3( 0, 0, -1 ),
				maxBounds: new Vector3( 2, 1, 1 ),
				cellSize: 1.0f,
				cellHeight: 1.0f
			);

			// Triangle covers some cells on plane y=0
			Span<Vector3> verts = stackalloc Vector3[]
			{
				new(0,0,0),
				new(1,0,0),
				new(0,0,-1)
			};
			Span<int> indices = stackalloc int[] { 0, 1, 2 };
			Span<int> areas = stackalloc int[] { Constants.WALKABLE_AREA };

			Rasterization.RasterizeTriangles( verts, indices, areas, hf, flagMergeThreshold: 1 );
			hf.EnsureCompressed();

			Assert.IsTrue( hf.TotalSpanCount > 0, "Expected some spans after rasterization" );
		}

		[TestMethod]
		public void Rasterization_TriangleBoundingBoxOverlapsButTriangleOutside_NoSpans()
		{
			using var hf = new Heightfield(
				sizeX: 10,
				sizeZ: 10,
				minBounds: new Vector3( 0, 0, 0 ),
				maxBounds: new Vector3( 10, 10, 10 ),
				cellSize: 1.0f,
				cellHeight: 1.0f
			);

			Span<Vector3> verts = stackalloc Vector3[]
			{
				new(-10,5.5f,-10),
				new(-10,5.5f,3),
				new(3,5.5f,-10)
			};
			Span<int> indices = stackalloc int[] { 0, 1, 2 };
			Span<int> areas = stackalloc int[] { 42 };

			Rasterization.RasterizeTriangles( verts, indices, areas, hf, flagMergeThreshold: 1 );
			hf.EnsureCompressed();

			Assert.AreEqual( 0, hf.TotalSpanCount, "No spans should be created for triangle outside the heightfield footprint." );
		}

		[TestMethod]
		public void SpanFilter_LedgeRemoval_RemovesIsolatedSpan()
		{
			using var hf = new Heightfield(
				sizeX: 2,
				sizeZ: 2,
				minBounds: new Vector3( 0, 0, 0 ),
				maxBounds: new Vector3( 2, 4, 2 ),
				cellSize: 1.0f,
				cellHeight: 1.0f
			);

			// Single walkable span at (0,0). All neighbors partially/fully empty -> ledge.
			hf.AddOrMergeSpan( 0, 0, 0, 2, Constants.WALKABLE_AREA, flagMergeThreshold: 0 );
			hf.EnsureCompressed();

			SpanFilter.Filter( walkableHeight: 2, walkableClimb: 1, hf );

			var col = hf.GetColumn( 0 );
			Assert.AreEqual( Constants.NULL_AREA, col[0].Area, "Isolated span should be filtered as a ledge." );
		}

		[TestMethod]
		public void HeightFieldGenerator_Generate_ReturnsNull_EmptyGeometry()
		{
			using var gen = new HeightFieldGenerator();
			var cfg = MakeConfig( BBox.FromPositionAndSize( Vector3.Zero, 100 ) );
			gen.Init( cfg );
			var result = gen.Generate();
			Assert.IsNull( result, "Generator should return null when no geometry has been added." );
		}

		[TestMethod]
		public void HeightFieldGenerator_Generate_WithPhysicsShape()
		{
			// Mirrors existing Navigation test but adds more assertions.
			var world = new PhysicsWorld();
			var body = new PhysicsBody( world );
			var shape = body.AddBoxShape( BBox.FromPositionAndSize( 0, 200 ), Rotation.Identity );

			using var gen = new HeightFieldGenerator();
			var cfg = MakeConfig( BBox.FromPositionAndSize( Vector3.Zero, 400 ) );
			gen.Init( cfg );

			gen.AddGeometryFromPhysicsShape( shape );

			Assert.AreEqual( 8, gen.inputGeoVerticesCount, "Expected box triangulation vertex count." );
			Assert.AreEqual( 36, gen.inputGeoIndicesCount, "Expected box triangulation index count (12 triangles)." );

			using var chf = gen.Generate();
			Assert.IsNotNull( chf, "Compact heightfield should be generated." );
			Assert.IsTrue( chf.SpanCount > 0, "Span count should be > 0." );

			// Verify at least one walkable area remains after erosion/filtering.
			bool anyWalkable = false;
			for ( int i = 0; i < chf.SpanCount; i++ )
			{
				if ( chf.Areas[i] == Constants.WALKABLE_AREA )
				{
					anyWalkable = true;
					break;
				}
			}
			Assert.IsTrue( anyWalkable, "Expected at least one remaining walkable area." );

			world.Delete();
		}
	}
}
