using Editor.MeshEditor;

namespace Editor.RectEditor;

internal class EdgeAwareFaceUnwrapper
{
	private readonly MeshFace[] faces;
	private readonly Dictionary<(MeshFace, HalfEdgeMesh.VertexHandle), int> faceVertexToIndex = new();
	private readonly List<Vector3> vertexPositions = new();
	private readonly Dictionary<MeshFace, List<int>> faceToVertexIndices = new();

	public EdgeAwareFaceUnwrapper( MeshFace[] meshFaces )
	{
		faces = meshFaces;
	}

	public UnwrapResult Unwrap( MappingMode mode )
	{
		if ( faces.Length == 0 )
			return new UnwrapResult();

		bool straighten = mode == MappingMode.UnwrapSquare;

		InitializeVertexMap();

		var unwrappedUVs = new List<Vector2>( new Vector2[vertexPositions.Count] );
		var processedFaces = new HashSet<MeshFace>();
		var faceQueue = new Queue<MeshFace>();

		if ( faces.Length > 0 && faces[0].IsValid )
		{
			// Seed the unwrap with the first face
			UnwrapFirstFace( faces[0], unwrappedUVs, straighten );
			processedFaces.Add( faces[0] );

			for ( int i = 1; i < faces.Length; i++ )
			{
				if ( faces[i].IsValid )
					faceQueue.Enqueue( faces[i] );
			}
		}

		int maxAttempts = faces.Length * 3;
		int attempts = 0;

		while ( faceQueue.Count > 0 && attempts < maxAttempts )
		{
			var currentFace = faceQueue.Dequeue();
			attempts++;

			if ( processedFaces.Contains( currentFace ) )
				continue;

			if ( TryUnfoldFace( currentFace, processedFaces, unwrappedUVs, straighten ) )
			{
				processedFaces.Add( currentFace );
				attempts = 0;
			}
			else if ( attempts < maxAttempts )
			{
				faceQueue.Enqueue( currentFace );
			}
		}

		return BuildResult( unwrappedUVs );
	}

	private void InitializeVertexMap()
	{
		foreach ( var face in faces )
		{
			if ( !face.IsValid )
				continue;

			var vertices = face.Component.Mesh.GetFaceVertices( face.Handle );
			var indices = new List<int>();

			foreach ( var vertexHandle in vertices )
			{
				var key = (face, vertexHandle);
				if ( !faceVertexToIndex.TryGetValue( key, out var index ) )
				{
					index = vertexPositions.Count;
					faceVertexToIndex[key] = index;
					vertexPositions.Add( face.Component.Mesh.GetVertexPosition( vertexHandle ) );
				}
				indices.Add( index );
			}

			faceToVertexIndices[face] = indices;
		}
	}

	private void UnwrapFirstFace( MeshFace face, List<Vector2> unwrappedUVs, bool straighten )
	{
		if ( !faceToVertexIndices.TryGetValue( face, out var indices ) || indices.Count < 3 )
			return;

		if ( straighten && indices.Count == 4 )
		{
			var p0 = vertexPositions[indices[0]];
			var p1 = vertexPositions[indices[1]];
			var p3 = vertexPositions[indices[3]];

			float width = p0.Distance( p1 );
			float height = p0.Distance( p3 );

			unwrappedUVs[indices[0]] = new Vector2( 0, 0 );
			unwrappedUVs[indices[1]] = new Vector2( width, 0 );
			unwrappedUVs[indices[2]] = new Vector2( width, height );
			unwrappedUVs[indices[3]] = new Vector2( 0, height );
		}
		else
		{
			var p0 = vertexPositions[indices[0]];
			var p1 = vertexPositions[indices[1]];
			var p2 = vertexPositions[indices[2]];

			var uDir = (p1 - p0).Normal;
			var normal = uDir.Cross( (p2 - p0).Normal ).Normal;
			var vDir = normal.Cross( uDir );

			foreach ( var idx in indices )
			{
				var relative = vertexPositions[idx] - p0;
				unwrappedUVs[idx] = new Vector2( relative.Dot( uDir ), relative.Dot( vDir ) );
			}
		}
	}

	private bool TryUnfoldFace( MeshFace currentFace, HashSet<MeshFace> processedFaces, List<Vector2> unwrappedUVs, bool straighten )
	{
		if ( !faceToVertexIndices.TryGetValue( currentFace, out var currentIndices ) )
			return false;

		foreach ( var processedFace in processedFaces )
		{
			var sharedEdge = FindSharedVertices( currentFace, processedFace, unwrappedUVs );
			if ( sharedEdge.HasValue )
			{
				UnfoldFaceAlongEdge( currentFace, currentIndices, sharedEdge.Value, unwrappedUVs, straighten );
				return true;
			}
		}

		return false;
	}

	private void UnfoldFaceAlongEdge( MeshFace face, List<int> faceIndices, (int idx1, int idx2, Vector2 uv1, Vector2 uv2) edge, List<Vector2> unwrappedUVs, bool straighten )
	{
		unwrappedUVs[edge.idx1] = edge.uv1;
		unwrappedUVs[edge.idx2] = edge.uv2;

		// SQUARE MODE.
		if ( straighten && faceIndices.Count == 4 )
		{
			UnfoldQuadStraight( faceIndices, edge, unwrappedUVs );
		}
		// CONFORMING MODE.
		else
		{
			UnfoldGeometric( faceIndices, edge, unwrappedUVs );
		}
	}

	/// <summary>
	/// Unfolds a quad by extruding the shared edge perpendicularly by the average length of the connecting sides.
	/// This forces the UV strip to remain straight (grid-like) even if the 3D mesh curves.
	/// </summary>
	private void UnfoldQuadStraight( List<int> faceIndices, (int idx1, int idx2, Vector2 uv1, Vector2 uv2) edge, List<Vector2> unwrappedUVs )
	{
		var uvVec = edge.uv2 - edge.uv1;
		var uvLen = uvVec.Length;
		var uvNormal = uvLen > 0.000001f ? uvVec / uvLen : new Vector2( 1, 0 );
		var uvPerp = new Vector2( -uvNormal.y, uvNormal.x ); // 90 degrees Left

		int ptr1 = faceIndices.IndexOf( edge.idx1 );
		int ptr2 = faceIndices.IndexOf( edge.idx2 );

		int connectedTo1;
		int connectedTo2;

		if ( (ptr1 + 1) % 4 == ptr2 )
		{
			connectedTo2 = faceIndices[(ptr2 + 1) % 4];
			connectedTo1 = faceIndices[(ptr1 + 3) % 4];
		}
		else
		{
			connectedTo1 = faceIndices[(ptr1 + 1) % 4];
			connectedTo2 = faceIndices[(ptr2 + 3) % 4];
		}

		float len1 = vertexPositions[edge.idx1].Distance( vertexPositions[connectedTo1] );
		float len2 = vertexPositions[edge.idx2].Distance( vertexPositions[connectedTo2] );
		float avgLen = (len1 + len2) * 0.5f;

		unwrappedUVs[connectedTo1] = edge.uv1 + uvPerp * avgLen;
		unwrappedUVs[connectedTo2] = edge.uv2 + uvPerp * avgLen;
	}

	/// <summary>
	/// Unfolds a face by projecting its vertices onto a 2D plane defined by the shared edge.
	/// This preserves the original geometric angles and shapes.
	/// </summary>
	private void UnfoldGeometric( List<int> faceIndices, (int idx1, int idx2, Vector2 uv1, Vector2 uv2) edge, List<Vector2> unwrappedUVs )
	{
		var pA = vertexPositions[edge.idx1];
		var pB = vertexPositions[edge.idx2];
		var edge3D = pB - pA;
		var edge2D = edge.uv2 - edge.uv1;

		Vector3 pThird = Vector3.Zero;
		foreach ( var idx in faceIndices )
		{
			if ( idx != edge.idx1 && idx != edge.idx2 )
			{
				pThird = vertexPositions[idx];
				break;
			}
		}

		var faceNormal = edge3D.Cross( pThird - pA ).Normal;
		var localU = edge3D.Normal;
		var localV = faceNormal.Cross( localU );

		var edge2DDir = edge2D.Normal;
		var edge2DPerp = new Vector2( -edge2DDir.y, edge2DDir.x );
		var scale = edge3D.Length > 0 ? edge2D.Length / edge3D.Length : 1.0f;

		foreach ( var idx in faceIndices )
		{
			if ( idx == edge.idx1 || idx == edge.idx2 )
				continue;

			var relative3D = vertexPositions[idx] - pA;
			float u = relative3D.Dot( localU );
			float v = relative3D.Dot( localV );

			unwrappedUVs[idx] = edge.uv1 + edge2DDir * u * scale + edge2DPerp * v * scale;
		}
	}

	private (int idx1, int idx2, Vector2 uv1, Vector2 uv2)? FindSharedVertices( MeshFace face1, MeshFace face2, List<Vector2> unwrappedUVs )
	{
		if ( !faceToVertexIndices.TryGetValue( face1, out var indices1 ) ||
			 !faceToVertexIndices.TryGetValue( face2, out var indices2 ) )
			return null;

		for ( int i = 0; i < indices1.Count; i++ )
		{
			var idx1a = indices1[i];
			var idx1b = indices1[(i + 1) % indices1.Count];

			var pos1a = vertexPositions[idx1a];
			var pos1b = vertexPositions[idx1b];

			for ( int j = 0; j < indices2.Count; j++ )
			{
				var idx2a = indices2[j];
				var idx2b = indices2[(j + 1) % indices2.Count];

				var pos2a = vertexPositions[idx2a];
				var pos2b = vertexPositions[idx2b];

				const float tolerance = 0.001f;
				bool matchForward = pos1a.Distance( pos2a ) < tolerance && pos1b.Distance( pos2b ) < tolerance;
				bool matchReverse = pos1a.Distance( pos2b ) < tolerance && pos1b.Distance( pos2a ) < tolerance;

				if ( matchForward )
				{
					return (idx1a, idx1b, unwrappedUVs[idx2a], unwrappedUVs[idx2b]);
				}
				if ( matchReverse )
				{
					return (idx1a, idx1b, unwrappedUVs[idx2b], unwrappedUVs[idx2a]);
				}
			}
		}

		return null;
	}

	private UnwrapResult BuildResult( List<Vector2> unwrappedUVs )
	{
		var finalFaceIndices = new List<List<int>>();
		foreach ( var face in faces )
		{
			if ( face.IsValid && faceToVertexIndices.TryGetValue( face, out var indices ) )
			{
				finalFaceIndices.Add( indices );
			}
		}

		return new UnwrapResult
		{
			VertexPositions = unwrappedUVs,
			FaceIndices = finalFaceIndices,
			OriginalPositions = vertexPositions
		};
	}

	public class UnwrapResult
	{
		public List<Vector2> VertexPositions { get; set; } = new();
		public List<List<int>> FaceIndices { get; set; } = new();
		public List<Vector3> OriginalPositions { get; set; } = new();
	}
}
