using System;
using Sandbox.Navigation.Generation;

namespace Navigation;

[TestClass]
public class HeightFieldGeneratorCoreTests
{
	private static Config MakeConfig( BBox bounds )
	{
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
		var verts = new Vector3[]
		{
			new( 0, 0, 0 ),
			new( 1, 0, 0 ),
			new( 0, 0, -1 )
		};
		var walkableTri = new[] { 0, 1, 2 };
		var unwalkableTri = new[] { 0, 2, 1 };

		var areas = new int[1];

		InputFilter.MarkWalkableTriangles( 45.0f, verts, walkableTri, areas );
		Assert.AreEqual( Constants.WALKABLE_AREA, areas[0], "Expected triangle to be walkable" );

		InputFilter.MarkWalkableTriangles( 45.0f, verts, unwalkableTri, areas );
		Assert.AreEqual( Constants.NULL_AREA, areas[0], "Expected triangle to be unwalkable" );

		areas[0] = 1337;
		InputFilter.MarkWalkableTriangles( 45.0f, verts, unwalkableTri, areas );
		Assert.AreEqual( Constants.NULL_AREA, areas[0], "Implementation clears areas for non-walkable triangles" );

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

		hf.AddOrMergeSpan( 0, 0, sMin: 0, sMax: 10, areaId: 1, flagMergeThreshold: 0 );
		Assert.AreEqual( 1, hf.TotalSpanCount );

		hf.AddOrMergeSpan( 0, 0, sMin: 10, sMax: 20, areaId: 2, flagMergeThreshold: 0 );
		Assert.AreEqual( 1, hf.TotalSpanCount, "Spans should have merged (still one span)" );

		hf.EnsureCompressed();
		var col = hf.GetColumn( 0 );
		Assert.AreEqual( 1, col.Length );
		Assert.AreEqual( (ushort)0, col[0].MinY );
		Assert.AreEqual( (ushort)20, col[0].MaxY );
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

		Span<Vector3> verts = stackalloc Vector3[]
		{
			new( 0, 0, 0 ),
			new( 1, 0, 0 ),
			new( 0, 0, -1 )
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
			new( -10, 5.5f, -10 ),
			new( -10, 5.5f, 3 ),
			new( 3, 5.5f, -10 )
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
}
