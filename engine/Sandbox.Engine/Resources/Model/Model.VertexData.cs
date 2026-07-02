using NativeEngine;

namespace Sandbox;

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

	public int GetIndexCount( int drawcall ) => MeshGlue.GetModelIndexCount( native, drawcall );
	public int GetIndexStart( int drawcall ) => MeshGlue.GetModelIndexStart( native, drawcall );
	public int GetBaseVertex( int drawcall ) => MeshGlue.GetModelBaseVertex( native, drawcall );

	/// <summary>
	/// Index count of the mesh drawn at the given LOD.
	/// </summary>
	public int GetIndexCountForLod( int lod ) => MeshGlue.GetModelLodIndexCount( native, lod );
}
