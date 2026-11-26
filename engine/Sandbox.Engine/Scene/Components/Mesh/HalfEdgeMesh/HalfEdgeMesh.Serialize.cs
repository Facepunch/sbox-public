using System.IO;

namespace HalfEdgeMesh;

partial class Mesh
{
	public byte[] Serialize()
	{
		using var ms = new MemoryStream();
		using ( var bw = new BinaryWriter( ms ) )
		{
			Serialize( bw );
		}

		return ms.ToArray();
	}

	public void Serialize( BinaryWriter writer )
	{
		writer.Write( VertexCount );
		writer.Write( HalfEdgeCount );
		writer.Write( FaceCount );

		foreach ( var v in VertexList )
		{
			writer.Write( v.Edge );
		}

		foreach ( var he in HalfEdgeList )
		{
			writer.Write( he.Vertex );
			writer.Write( he.OppositeEdge );
			writer.Write( he.NextEdge );
			writer.Write( he.Face );
		}

		foreach ( var f in FaceList )
		{
			writer.Write( f.Edge );
		}
	}

	public void Deserialize( BinaryReader reader )
	{
		var vertexCount = reader.ReadInt32();
		var edgeCount = reader.ReadInt32();
		var faceCount = reader.ReadInt32();

		VertexList.Reserve( vertexCount );
		HalfEdgeList.Reserve( edgeCount );
		FaceList.Reserve( faceCount );

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
