using Nav;
using NativeEngine;
using System.Buffers;
using NVector3 = System.Numerics.Vector3;

namespace Sandbox.Navigation;

public sealed partial class NavMesh
{
	private const float TileHeightWorldSpace = 1048576f;

	internal readonly List<NVector3> geometryVertices = new();
	internal readonly List<int> geometryIndices = new();

	// World space, wound as physics hands the triangles out. No triangles make the tile empty.
	internal void CollectGeometry( PhysicsWorld world, TileJob job, out NVector3[] vertices, out int[] indices )
	{
		ThreadSafe.AssertIsMainThread();

		geometryVertices.Clear();
		geometryIndices.Clear();

		if ( world.IsValid() && world._world is PhysicsWorld3d world3d )
		{
			var bounds = new BBox(
				new Vector3( job.BoundsMin.X, job.BoundsMin.Y, MathF.Max( job.MinZ, -0.5f * TileHeightWorldSpace ) ),
				new Vector3( job.BoundsMax.X, job.BoundsMax.Y, MathF.Min( job.MaxZ, 0.5f * TileHeightWorldSpace ) ) );

			var results = CQueryResult.Create();
			var shapeVertices = CUtlVectorVector.Create( 0, 512 );
			var shapeIndices = CUtlVectorUInt32.Create( 0, 1024 );

			world3d.native.Query( results, bounds, 0x07 );

			for ( int i = 0; i < results.Count(); i++ )
			{
				var shape = results.Element( i );
				if ( !shape.IsValid() ) continue;

				var body = shape.Body;
				if ( !body.IsValid() || !IsBodyRelevantForNavmesh( body ) ) continue;

				shapeVertices.SetCount( 0 );
				shapeIndices.SetCount( 0 );
				shape.native.GetTriangulationForNavmesh( shapeVertices, shapeIndices, bounds );

				int first = geometryVertices.Count;
				var bodyTransform = body.Transform;
				for ( int v = 0; v < shapeVertices.Count(); v++ ) geometryVertices.Add( bodyTransform.PointToWorld( shapeVertices.Element( v ) ) );
				for ( int t = 0; t < shapeIndices.Count(); t++ ) geometryIndices.Add( (int)shapeIndices.Element( t ) + first );
			}

			shapeIndices.DeleteThis();
			shapeVertices.DeleteThis();
			results.DeleteThis();
		}

		// Returned to the pool when the job is committed
		vertices = ArrayPool<NVector3>.Shared.Rent( geometryVertices.Count );
		indices = ArrayPool<int>.Shared.Rent( geometryIndices.Count );
		geometryVertices.CopyTo( vertices );
		geometryIndices.CopyTo( indices );
		job.SetGeometry( vertices.AsMemory( 0, geometryVertices.Count ), indices.AsMemory( 0, geometryIndices.Count ) );
	}
}
