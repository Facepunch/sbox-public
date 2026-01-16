using NativeEngine;
using System.Buffers;
using System.Runtime.InteropServices;

namespace Sandbox.Navigation.Generation;

[SkipHotload]
class HeightFieldGenerator : IDisposable
{
	internal Vector3[] inputGeoVertices = null;
	internal int[] inputGeoIndices = null;
	internal int inputGeoVerticesCount = 0;
	internal int inputGeoIndicesCount = 0;

	float inputGeometryMaxZ;
	float inputGeometryMinZ;

	Config cfg;

	Heightfield cachedHeightField;

	CUtlVectorVector triangulationVertArrCache = CUtlVectorVector.Create( 0, 512 );
	CUtlVectorUInt32 triangulationIndexArrCache = CUtlVectorUInt32.Create( 0, 1024 );

	public void Dispose()
	{
		cachedHeightField?.Dispose();
		cachedHeightField = null;

		triangulationVertArrCache.DeleteThis();
		triangulationIndexArrCache.DeleteThis();

		if ( inputGeoVertices != null )
		{
			ArrayPool<Vector3>.Shared.Return( inputGeoVertices );
			inputGeoVertices = null;
		}

		if ( inputGeoIndices != null )
		{
			ArrayPool<int>.Shared.Return( inputGeoIndices );
			inputGeoIndices = null;
		}
	}

	public void Init( Config config )
	{
		inputGeoVertices ??= ArrayPool<Vector3>.Shared.Rent( 2048 );
		inputGeoIndices ??= ArrayPool<int>.Shared.Rent( 4096 );
		inputGeometryMaxZ = float.MinValue;
		inputGeometryMinZ = float.MaxValue;
		cfg = config;
	}

	public void CollectGeometry( NavMesh navMesh, PhysicsWorld world, BBox tileBoundsWorld )
	{
		ThreadSafe.AssertIsMainThread();

		if ( !world.IsValid() )
			return;

		var results = CQueryResult.Create();
		world.native.Query( results, tileBoundsWorld, 0x07 );

		// clear arrays
		inputGeoVerticesCount = 0;
		inputGeoIndicesCount = 0;

		for ( int i = 0; i < results.Count(); i++ )
		{
			var shape = results.Element( i );
			if ( !shape.IsValid() )
				continue;

			var body = shape.Body;
			if ( !body.IsValid() )
				continue;

			if ( !navMesh.IsBodyRelevantForNavmesh( body ) )
				continue;

			AddGeometryFromPhysicsShape( shape );

		}

		results.DeleteThis();
	}

	internal void AddGeometryFromPhysicsShape( PhysicsShape shape )
	{
		triangulationVertArrCache.SetCount( 0 );
		triangulationIndexArrCache.SetCount( 0 );

		shape.native.GetTriangulationForNavmesh( triangulationVertArrCache, triangulationIndexArrCache, cfg.Bounds );

		if ( inputGeoVerticesCount + triangulationVertArrCache.Count() > inputGeoVertices.Length )
		{
			var newVerts = ArrayPool<Vector3>.Shared.Rent( (inputGeoVerticesCount + triangulationVertArrCache.Count()) * 2 );
			Array.Copy( inputGeoVertices, newVerts, inputGeoVerticesCount );
			ArrayPool<Vector3>.Shared.Return( inputGeoVertices );
			inputGeoVertices = newVerts;
		}
		if ( inputGeoIndicesCount + triangulationIndexArrCache.Count() > inputGeoIndices.Length )
		{
			var newIndices = ArrayPool<int>.Shared.Rent( (inputGeoIndicesCount + triangulationIndexArrCache.Count()) * 2 );
			Array.Copy( inputGeoIndices, newIndices, inputGeoIndicesCount );
			ArrayPool<int>.Shared.Return( inputGeoIndices );
			inputGeoIndices = newIndices;
		}

		int startV = inputGeoVerticesCount;

		for ( int i = 0; i < triangulationIndexArrCache.Count(); i += 3 )
		{
			// invert winding and increment by startV
			var idx0 = triangulationIndexArrCache.Element( i + 0 );
			var idx1 = triangulationIndexArrCache.Element( i + 1 );
			var idx2 = triangulationIndexArrCache.Element( i + 2 );

			inputGeoIndices[inputGeoIndicesCount++] = (int)idx0 + startV;
			inputGeoIndices[inputGeoIndicesCount++] = (int)idx2 + startV;
			inputGeoIndices[inputGeoIndicesCount++] = (int)idx1 + startV;
		}

		// Transform positions added to the vertex data
		var bodyTransform = shape.Body.Transform;
		for ( int v = 0; v < triangulationVertArrCache.Count(); ++v )
		{
			var vertexWorldPos = bodyTransform.PointToWorld( triangulationVertArrCache.Element( v ) );

			if ( vertexWorldPos.z > inputGeometryMaxZ ) inputGeometryMaxZ = vertexWorldPos.z;
			if ( vertexWorldPos.z < inputGeometryMinZ ) inputGeometryMinZ = vertexWorldPos.z;

			inputGeoVertices[inputGeoVerticesCount++] = NavMesh.ToNav( vertexWorldPos );
		}
	}

	public CompactHeightfield Generate()
	{
		var nverts = inputGeoVerticesCount;
		var ntris = inputGeoIndicesCount / 3;

		// Nothing todo bail
		if ( nverts < 3 || ntris == 0 )
		{
			return null;
		}

		// Ideally we should also never arrive here
		if ( cfg.TileSizeXY == 0 )
		{
			Log.Warning( "buildNavigation: Empty tile provided.\n" );
			// Bail nothing more todo
			return null;
		}

		// The geometry may not fully cover the vertical space of the tile.
		// By shrinking the bounds we can avoid precissions issues due to large tile height.
		// Make sure we are at least couple voxels high though
		cfg.Bounds.Maxs.z = inputGeometryMaxZ + cfg.CellHeight * 2; // add alittle bit extra to avoid precission issues
		cfg.Bounds.Mins.z = inputGeometryMinZ - cfg.CellHeight * 2; // add alittle bit extra to avoid precission issues

		// Set the area where the navigation will be build.
		var minBoundsNavSpace = NavMesh.ToNav( cfg.Bounds.Mins );
		var maxBoundsNavSpace = NavMesh.ToNav( cfg.Bounds.Maxs );

		//
		// Step 2. Rasterize input polygon soup.
		//

		// Allocate voxel heightfield where we rasterize our input data to.
		if ( cachedHeightField == null )
		{
			cachedHeightField = new( cfg.TileSizeXY, cfg.TileSizeXY, minBoundsNavSpace, maxBoundsNavSpace, cfg.CellSize, cfg.CellHeight );
		}
		else
		{
			// Reuse memory
			cachedHeightField.Init( cfg.TileSizeXY, cfg.TileSizeXY, minBoundsNavSpace, maxBoundsNavSpace, cfg.CellSize, cfg.CellHeight );
		}


		using var pooledTriAreas = new PooledSpan<int>( ntris );
		var triAreas = pooledTriAreas.Span;
		InputFilter.MarkWalkableTriangles( cfg.WalkableSlopeAngle, inputGeoVertices.AsSpan( 0, inputGeoVerticesCount ), inputGeoIndices.AsSpan( 0, inputGeoIndicesCount ), triAreas );

		Rasterization.RasterizeTriangles( inputGeoVertices.AsSpan( 0, inputGeoVerticesCount ), inputGeoIndices.AsSpan( 0, inputGeoIndicesCount ), triAreas, cachedHeightField, cfg.WalkableClimb );

		cachedHeightField.EnsureCompressed();

		if ( cachedHeightField.TotalSpanCount == 0 )
		{
			return null;
		}

		//
		// Step 3. Filter walkables surfaces.
		//

		// Once all geometry is rasterized, we do initial pass of filtering to
		// remove unwanted overhangs caused by the conservative rasterization
		// as well as filter spans where the character cannot possibly stand.
		SpanFilter.Filter( cfg.WalkableHeight, cfg.WalkableClimb, cachedHeightField );

		//
		// Step 4. Partition walkable surface to simple regions.
		//

		// Compact the heightfield so that it is faster to handle from now on.
		// This will result more cache coherent data as well as the neighbours
		// between walkable cells will be calculated.
#pragma warning disable CA2000 // Dispose objects before losing scope
		// chf is returned by this function, caller takes ownership
		var chf = cachedHeightField.BuildCompactHeightfield( cfg.WalkableHeight, cfg.WalkableClimb );
#pragma warning restore CA2000 // Dispose objects before losing scope

		// Erode the walkable area by agent radius.
		AreaFilter.ErodeWalkableArea( cfg.WalkableRadius, chf );

		return chf;
	}
}
