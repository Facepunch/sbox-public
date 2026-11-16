using V3 = System.Numerics.Vector3;

namespace Borealis.Backend.Meshlets;

/// <summary>
/// Cuts an index range into meshlets with meshoptimizer (<c>meshopt_buildMeshlets</c>, spatial, no cone weight: normal
/// cones cull almost nothing of s&amp;box's world geometry), and bounds each with the box of its vertices.
/// </summary>
internal static unsafe class MeshletBuilder
{
	/// <summary>
	/// The meshlets of <paramref name="indices"/> (index values as stored, vertex <c>baseVertex + index</c> at
	/// <paramref name="position"/>), or null when the range is empty or the library isn't there.
	/// </summary>
	public static MeshletSet Build( ReadOnlySpan<uint> indices, Func<int, V3> position, int baseVertex, MeshletConfig config )
	{
		// meshoptimizer is built without its asserts: what it would assert is checked here
		if ( config.MaxVertices is < 3 or > 256 || config.MaxTriangles is < 4 or > 252 || config.MaxTriangles % 4 != 0 )
			throw new ArgumentOutOfRangeException( nameof( config ), config, "3-256 vertices and 4-252 triangles, a multiple of 4: meshoptimizer's limits and a meshlet record's" );
		if ( indices.Length % 3 != 0 ) throw new ArgumentException( "Not whole triangles", nameof( indices ) );
		var count = indices.Length;
		if ( count == 0 || !MeshOptimizer.Available ) return null;

		// The range's vertices, from the lowest index it uses
		uint min = uint.MaxValue, max = 0;
		for ( int k = 0; k < count; k++ )
		{
			min = Math.Min( min, indices[k] );
			max = Math.Max( max, indices[k] );
		}

		var vertexCount = (int)(max - min + 1);
		var positions = new V3[vertexCount];
		for ( int v = 0; v < vertexCount; v++ ) positions[v] = position( baseVertex + (int)min + v );

		var local = new uint[count];
		for ( int k = 0; k < count; k++ ) local[k] = indices[k] - min;

		var bound = (int)MeshOptimizer.BuildMeshletsBound( (nuint)count, (nuint)config.MaxVertices, (nuint)config.MaxTriangles );
		var meshlets = new MeshOptimizer.Meshlet[bound];
		var meshletVertices = new uint[count];
		var meshletTriangles = new byte[count];
		int built;
		fixed ( MeshOptimizer.Meshlet* m = meshlets )
		fixed ( uint* mv = meshletVertices )
		fixed ( byte* mt = meshletTriangles )
		fixed ( uint* ix = local )
		fixed ( V3* p = positions )
			built = (int)MeshOptimizer.BuildMeshlets( m, mv, mt, ix, (nuint)count, (float*)p, (nuint)vertexCount, 12, (nuint)config.MaxVertices, (nuint)config.MaxTriangles, 0 );

		// Each meshlet's triangles, corners in their order, and the box of its vertices
		var ordered = new uint[count];
		var records = new MeshletRecord[built];
		var next = 0;
		for ( int i = 0; i < built; i++ )
		{
			ref readonly var meshlet = ref meshlets[i];
			var lo = new V3( float.MaxValue );
			var hi = new V3( float.MinValue );
			for ( int k = 0; k < meshlet.VertexCount; k++ )
			{
				var p = positions[meshletVertices[meshlet.VertexOffset + k]];
				lo = V3.Min( lo, p );
				hi = V3.Max( hi, p );
			}

			for ( int k = 0; k < meshlet.TriangleCount * 3; k++ ) ordered[next * 3 + k] = meshletVertices[meshlet.VertexOffset + meshletTriangles[meshlet.TriangleOffset + k]] + min;
			records[i] = new MeshletRecord { Center = (lo + hi) * 0.5f, Extents = (hi - lo) * 0.5f, FirstTriangle = (uint)next, TriangleCount = meshlet.TriangleCount };
			next += (int)meshlet.TriangleCount;
		}

		// Every triangle in exactly one meshlet, or the range keeps its order and draws whole
		return next * 3 == count && count == indices.Length ? new MeshletSet { Indices = ordered, Meshlets = records } : null;
	}
}
