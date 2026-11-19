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
	/// The meshes a model made in code (<see cref="ModelBuilder"/>) was built from, in the order Source 2's model has them;
	/// they keep its vertices and indices, which are read back from them. Null for other models.
	/// </summary>
	internal Mesh[] BuilderMeshes { get; set; }

	/// <summary>
	/// Experimental!
	/// </summary>
	public unsafe Vertex[] GetVertices()
	{
		if ( BuilderMeshes is { } built ) return BuilderVertices( built );

		// On Borealis.Gpu Source 2's buffers hold nothing to read back: the compiled file's geometry
		if ( FileGeometry() is { } file ) return ModelFileGeometry.Vertices( file );

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
		if ( BuilderMeshes is { } built ) return BuilderIndices( built );
		if ( FileGeometry() is { } file ) return ModelFileGeometry.Indices( file );

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

		if ( BuilderMeshes is { } built ) return TryReadBuilderDrawCall( built, mesh, drawCall, out vertices, out indices, out streams );

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

	/// <summary>
	/// <see cref="TryReadDrawCall"/> for a model made in code, from its meshes' own buffers, as native's <c>ReadDrawCall</c>
	/// reads them: indexed triangle lists only, every index inside the draw's vertices, those it uses compacted in order.
	/// </summary>
	bool TryReadBuilderDrawCall( Mesh[] built, int mesh, int drawCall, out Vertex[] vertices, out uint[] indices, out VertexStreams streams )
	{
		vertices = default;
		indices = default;
		streams = default;

		if ( (uint)mesh >= (uint)built.Length || drawCall < 0 || mesh >= MeshCount ) return false;
		native.GetDrawCallInfo( mesh, drawCall, out var vertexCount, out var indexCount, out var primitiveType, out _, out _, out _, out var startIndex, out var baseVertex );

		var capacity = Math.Min( vertexCount, indexCount );
		if ( capacity <= 0 ) return false;
		if ( primitiveType != (int)RenderPrimitiveType.RENDER_PRIM_TRIANGLES || baseVertex < 0 || startIndex < 0 || indexCount < 3 || indexCount % 3 != 0 ) return false;

		var source = built[mesh];
		var ib = source.IndexData;
		var vb = source.VertexData;
		if ( ib is null || vb is null || (long)startIndex + indexCount > ib.Length ) return false;

		var read = new uint[indexCount];
		for ( int i = 0; i < indexCount; i++ )
		{
			var index = (uint)ib[startIndex + i];
			if ( index >= (uint)vertexCount ) return false;
			read[i] = index;
		}

		// The vertices it uses, in order, and its indices rebased to them
		var used = (uint[])read.Clone();
		Array.Sort( used );
		var usedCount = 0;
		for ( int i = 0; i < used.Length; i++ )
		{
			if ( usedCount == 0 || used[usedCount - 1] != used[i] ) used[usedCount++] = used[i];
		}

		if ( usedCount > capacity || (long)baseVertex + used[usedCount - 1] >= vb.Count ) return false;
		for ( int i = 0; i < read.Length; i++ ) read[i] = (uint)Array.BinarySearch( used, 0, usedCount, read[i] );

		vertices = new Vertex[usedCount];
		ModelFileGeometry.Read( vb, vertices, baseVertex, used.AsSpan( 0, usedCount ) );
		indices = read;
		streams = ModelFileGeometry.Streams( vb );
		return true;
	}

	// LOD 0's meshes, as native's GetModelVertices reads them
	IEnumerable<Mesh> BuilderLod0( Mesh[] built )
	{
		var meshes = MeshInfo.Meshes;
		for ( int i = 0; i < built.Length && i < meshes.Length; i++ )
		{
			if ( (meshes[i].LodMask & 1) != 0 ) yield return built[i];
		}
	}

	Vertex[] BuilderVertices( Mesh[] built )
	{
		var count = BuilderLod0( built ).Sum( m => m.VertexCount );
		if ( count == 0 ) return null;

		var vertices = new Vertex[count];
		var at = 0;
		foreach ( var mesh in BuilderLod0( built ) )
		{
			if ( mesh.VertexData is not { } buffer ) continue;
			ModelFileGeometry.Read( buffer, vertices.AsSpan( at, buffer.Count ) );
			at += buffer.Count;
		}

		return vertices;
	}

	uint[] BuilderIndices( Mesh[] built )
	{
		var count = BuilderLod0( built ).Sum( m => m.IndexCount );
		if ( count == 0 ) return null;

		// As native: each mesh's indices offset by the vertices of the meshes before it
		var indices = new uint[count];
		var at = 0;
		uint startVertex = 0;
		foreach ( var mesh in BuilderLod0( built ) )
		{
			if ( mesh.IndexData is not { } buffer ) continue;
			for ( int i = 0; i < buffer.Length; i++ ) indices[at++] = startVertex + (uint)buffer[i];
			startVertex += (uint)mesh.VertexCount;
		}

		return indices;
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
