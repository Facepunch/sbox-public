using NativeEngine;

namespace Sandbox;

/// <summary>
/// The vertex attributes a model's geometry carries. Mirrors SboxVertexStream_t.
/// </summary>
[System.Flags]
internal enum VertexStreams
{
	Position = 1,
	Normal = 2,
	Tangent = 4,
	TexCoord0 = 8,
	TexCoord1 = 16,
	Color = 32,
}

public partial class Model
{
	/// <summary>
	/// Experimental!
	/// </summary>
	public unsafe Vertex[] GetVertices()
	{
		int numVertices = MeshGlue.GetModelNumVertices( native );
		if ( numVertices == 0 )
			return null;

		var vertices = new Vertex[numVertices];

		fixed ( Vertex* vmem = &vertices[0] )
		{
			MeshGlue.GetModelVertices( native, (IntPtr)vmem, (uint)numVertices );
		}

		return vertices;
	}

	/// <summary>
	/// Experimental!
	/// </summary>
	public unsafe uint[] GetIndices()
	{
		int numIndices = MeshGlue.GetModelNumIndices( native );
		if ( numIndices == 0 )
			return null;

		var indices = new uint[numIndices];

		fixed ( uint* vmem = &indices[0] )
		{
			MeshGlue.GetModelIndices( native, (IntPtr)vmem, (uint)numIndices );
		}

		return indices;
	}

	/// <summary>
	/// Read back the geometry of a single draw call, with its indices rebased to the vertices we
	/// return. Unlike <see cref="GetVertices"/> this reads the buffers the draw call itself points
	/// at, so it holds no matter how the model packs its meshes. <paramref name="streams"/> says
	/// which attributes the model actually carries - the rest come back as defaults.
	/// </summary>
	internal unsafe bool TryReadDrawCall( int mesh, int drawCall, out Vertex[] vertices, out uint[] indices, out VertexStreams streams )
	{
		vertices = default;
		indices = default;
		streams = default;

		int vertexCount = MeshGlue.GetDrawCallVertexCount( native, mesh, drawCall );
		int indexCount = MeshGlue.GetDrawCallIndexCount( native, mesh, drawCall );

		if ( vertexCount <= 0 || indexCount <= 0 )
			return false;

		var v = new Vertex[vertexCount];
		var i = new uint[indexCount];
		uint used;

		fixed ( Vertex* vmem = &v[0] )
		fixed ( uint* imem = &i[0] )
		{
			if ( !MeshGlue.ReadDrawCall( native, mesh, drawCall, (IntPtr)vmem, (uint)vertexCount, (IntPtr)imem, (uint)indexCount, out used ) )
				return false;
		}

		// Native readback compacts the referenced vertices, including gaps in shared buffers.
		vertices = used < (uint)vertexCount ? v[..(int)used] : v;
		indices = i;
		streams = (VertexStreams)MeshGlue.GetDrawCallStreams( native, mesh, drawCall );

		return true;
	}

	public int GetIndexCount( int drawcall ) => MeshGlue.GetModelIndexCount( native, drawcall );
	public int GetIndexStart( int drawcall ) => MeshGlue.GetModelIndexStart( native, drawcall );
	public int GetBaseVertex( int drawcall ) => MeshGlue.GetModelBaseVertex( native, drawcall );

	/// <summary>
	/// Index count of the mesh drawn at the given LOD.
	/// </summary>
	public int GetIndexCountForLod( int lod ) => MeshGlue.GetModelLodIndexCount( native, lod );

	/// <summary>
	/// Number of draw calls (materials) in the render mesh used at the given LOD.
	/// </summary>
	internal int GetLodDrawCallCount( int lod )
	{
		var lodMask = 1 << lod;

		for ( int i = 0; i < MeshCount; i++ )
		{
			native.GetMeshInfo( i, out _, out _, out int drawCallCount, out int meshLodMask, out _, out _, out _ );
			if ( (meshLodMask & lodMask) != 0 )
				return drawCallCount;
		}

		return 0;
	}

	/// <summary>
	/// Index range of a draw call (material) within the render mesh used at the given LOD.
	/// </summary>
	internal void GetLodDrawCallRange( int lod, int drawCall, out int startIndex, out int indexCount, out int baseVertex )
	{
		var lodMask = 1 << lod;

		for ( int i = 0; i < MeshCount; i++ )
		{
			native.GetMeshInfo( i, out _, out _, out _, out int meshLodMask, out _, out _, out _ );
			if ( (meshLodMask & lodMask) == 0 )
				continue;

			native.GetDrawCallInfo( i, drawCall, out _, out indexCount, out _, out _, out _, out _, out startIndex, out baseVertex );
			return;
		}

		startIndex = 0;
		indexCount = 0;
		baseVertex = 0;
	}
}
