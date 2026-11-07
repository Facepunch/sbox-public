using System.IO;
using System.Runtime.InteropServices;
using Sandbox;

namespace HalfEdgeMesh;

partial class Mesh
{
	/// <summary>
	/// Size in bytes of what <see cref="Serialize"/> writes.
	/// </summary>
	internal int SerializedSize => 12 + MemoryMarshal.AsBytes( VertexList.AsSpan() ).Length + MemoryMarshal.AsBytes( HalfEdgeList.AsSpan() ).Length + MemoryMarshal.AsBytes( FaceList.AsSpan() ).Length;

	/// <summary>
	/// The counts, then every vertex's edge, every half edge's vertex, opposite, next and face, and every face's edge,
	/// as little-endian ints. The structs are those ints in that order, so each list is written as it is in memory.
	/// </summary>
	internal void Serialize( ref ByteStream stream )
	{
		stream.Write( VertexCount );
		stream.Write( HalfEdgeCount );
		stream.Write( FaceCount );
		stream.Write( MemoryMarshal.AsBytes( VertexList.AsSpan() ) );
		stream.Write( MemoryMarshal.AsBytes( HalfEdgeList.AsSpan() ) );
		stream.Write( MemoryMarshal.AsBytes( FaceList.AsSpan() ) );
	}

	public void Deserialize( BinaryReader reader )
	{
		var vertexCount = reader.ReadInt32();
		var edgeCount = reader.ReadInt32();
		var faceCount = reader.ReadInt32();

		for ( int i = 0; i < vertexCount; ++i )
		{
			var hVertex = AllocateVertex( new Vertex()
			{
				Edge = reader.ReadInt32(),
			} );

			if ( this[hVertex].Edge < 0 )
			{
				FreeVertex( hVertex );
			}
		}

		for ( int i = 0; i < edgeCount; ++i )
		{
			var hEdge = AllocateHalfEdge( new HalfEdge()
			{
				Vertex = reader.ReadInt32(),
				OppositeEdge = reader.ReadInt32(),
				NextEdge = reader.ReadInt32(),
				Face = reader.ReadInt32(),
			} );

			if ( this[hEdge].Vertex < 0 )
			{
				FreeHalfEdge( hEdge );
			}
		}

		for ( int i = 0; i < faceCount; ++i )
		{
			var hFace = AllocateFace( new Face()
			{
				Edge = reader.ReadInt32(),
			} );

			if ( this[hFace].Edge < 0 )
			{
				FreeFace( hFace );
			}
		}
	}
}
