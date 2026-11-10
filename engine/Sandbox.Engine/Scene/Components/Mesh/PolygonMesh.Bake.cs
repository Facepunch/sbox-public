using HalfEdgeMesh;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Sandbox;

partial class PolygonMesh
{
	/// <summary>
	/// Triangulated geometry pulled out of a <see cref="PolygonMesh"/>, one entry per material.
	/// </summary>
	internal readonly record struct BakedSubmesh( Material Material, MeshVertex[] Vertices, int[] Indices );

	/// <summary>
	/// Triangulate this mesh into standalone geometry in local space. Unlike <see cref="Rebuild"/>
	/// this owns everything it produces and leaves the mesh's own render data, submeshes and dirty
	/// state alone, so it's safe to bake from a live mesh. Hidden faces are included - hiding is an
	/// editor convenience that isn't saved, so leaving them out would compile a scene that doesn't
	/// match the one you'd get by loading the source.
	/// </summary>
	internal List<BakedSubmesh> Triangulate()
	{
		var groups = new Dictionary<Material, (List<MeshVertex> Vertices, List<int> Indices)>();

		try
		{
			// ComputeFaceVertexNormal recomputes the plane of every face it touches, so prime the
			// same cache Rebuild uses, and leave it as we found it.
			foreach ( var hFace in Topology.FaceHandles )
			{
				PlaneEquation( hFace, out var normal, out _ );
				_faceNormalCache[hFace] = normal;
			}

			foreach ( var hFace in Topology.FaceHandles )
			{
				var material = GetFaceMaterial( hFace ) ?? DefaultMaterial;

				if ( !groups.TryGetValue( material, out var group ) )
				{
					group = (new List<MeshVertex>(), new List<int>());
					groups.Add( material, group );
				}

				TriangulateFaceInto( hFace, group.Vertices, group.Indices );
			}
		}
		finally
		{
			_faceNormalCache.Clear();
		}

		var baked = new List<BakedSubmesh>( groups.Count );

		foreach ( var (material, group) in groups )
		{
			if ( group.Indices.Count < 3 )
				continue;

			var indices = group.Indices.ToArray();
			baked.Add( new BakedSubmesh( material, Weld( group.Vertices, indices ), indices ) );
		}

		return baked;
	}

	/// <summary>
	/// Faces are triangulated independently, so wherever two of them meet smoothly they produce
	/// identical vertices. Fold those together - hard edges differ in their normals or texcoords
	/// and are left alone. Rewrites <paramref name="indices"/> in place to point at the result.
	/// </summary>
	static MeshVertex[] Weld( List<MeshVertex> vertices, Span<int> indices )
	{
		var unique = new Dictionary<MeshVertex, int>( vertices.Count, BitwiseVertexComparer.Instance );
		var remap = new int[vertices.Count];

		for ( int i = 0; i < vertices.Count; i++ )
		{
			if ( !unique.TryGetValue( vertices[i], out var index ) )
			{
				index = unique.Count;
				unique.Add( vertices[i], index );
			}

			remap[i] = index;
		}

		for ( int i = 0; i < indices.Length; i++ )
		{
			indices[i] = remap[indices[i]];
		}

		var welded = new MeshVertex[unique.Count];

		foreach ( var (vertex, index) in unique )
		{
			welded[index] = vertex;
		}

		return welded;
	}

	/// <summary>
	/// Compares vertices by their bytes. Only vertices built from the exact same inputs weld, which
	/// is what we want - anything looser would smooth over creases.
	/// </summary>
	sealed class BitwiseVertexComparer : IEqualityComparer<MeshVertex>
	{
		public static readonly BitwiseVertexComparer Instance = new();

		static ReadOnlySpan<byte> Bytes( in MeshVertex vertex ) => MemoryMarshal.AsBytes( new ReadOnlySpan<MeshVertex>( in vertex ) );

		public bool Equals( MeshVertex a, MeshVertex b ) => Bytes( a ).SequenceEqual( Bytes( b ) );

		public int GetHashCode( MeshVertex vertex )
		{
			var hash = new HashCode();
			hash.AddBytes( Bytes( vertex ) );
			return hash.ToHashCode();
		}
	}

	void TriangulateFaceInto( FaceHandle hFace, List<MeshVertex> vertices, List<int> indices )
	{
		var positions = Topology.GetFaceVertices( hFace )
			.Select( x => Positions[x] )
			.ToArray();

		var faceIndices = Mesh.TriangulatePolygon( positions );

		if ( faceIndices.Length < 3 || faceIndices.Length % 3 != 0 )
			return;

		if ( !GetFaceVerticesConnectedToFace( hFace, out var faceEdges ) || faceEdges.Length != positions.Length )
			return;

		var startVertex = vertices.Count;

		for ( int i = 0; i < faceEdges.Length; i++ )
		{
			var faceEdge = faceEdges[i];
			var normal = ComputeFaceVertexNormal( faceEdge );
			ComputeTangentSpaceForFaceVertex( faceEdge, out var u, out var v );
			CalcTangentAndFlipFromBasis( u, v, normal, out var tangent );

			vertices.Add( new MeshVertex
			{
				Position = positions[i],
				Normal = normal,
				Tangent = tangent,
				Texcoord = TextureCoord[faceEdge],
				Blend = Blends[faceEdge],
				Color = Colors[faceEdge],
			} );
		}

		for ( int i = 0; i < faceIndices.Length; i += 3 )
		{
			var a = startVertex + faceIndices[i];
			var b = startVertex + faceIndices[i + 1];
			var c = startVertex + faceIndices[i + 2];

			var ab = vertices[b].Position - vertices[a].Position;
			var ac = vertices[c].Position - vertices[a].Position;

			// Nothing to draw, and they'd only confuse the normals of anything that shares them
			if ( Vector3.Cross( ab, ac ).Length.AlmostEqual( 0.0f ) )
				continue;

			indices.Add( a );
			indices.Add( b );
			indices.Add( c );
		}
	}
}
