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

	public UnwrapResult UnwrapToSquare()
	{
		if ( faces.Length == 0 )
			return new UnwrapResult();

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

		var unwrappedUVs = new List<Vector2>( new Vector2[vertexPositions.Count] );
		var processedFaces = new HashSet<MeshFace>();
		var faceQueue = new Queue<MeshFace>();

		if ( faces.Length > 0 && faces[0].IsValid )
		{
			UnwrapFirstFace( faces[0], unwrappedUVs );
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

			if ( TryUnfoldFace( currentFace, processedFaces, unwrappedUVs ) )
			{
				processedFaces.Add( currentFace );
				attempts = 0;
			}
			else if ( attempts < maxAttempts )
			{
				faceQueue.Enqueue( currentFace );
			}
		}

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

	private void UnwrapFirstFace( MeshFace face, List<Vector2> unwrappedUVs )
	{
		if ( !faceToVertexIndices.TryGetValue( face, out var indices ) || indices.Count < 3 )
			return;

		var pos0 = vertexPositions[indices[0]];
		var pos1 = vertexPositions[indices[1]];
		var pos2 = vertexPositions[indices[2]];

		var u = (pos1 - pos0).Normal;
		var normal = u.Cross( (pos2 - pos0).Normal ).Normal;
		var v = normal.Cross( u );

		foreach ( var vertexIndex in indices )
		{
			var pos = vertexPositions[vertexIndex];
			var relative = pos - pos0;
			unwrappedUVs[vertexIndex] = new Vector2( relative.Dot( u ), relative.Dot( v ) );
		}
	}

	private bool TryUnfoldFace( MeshFace currentFace, HashSet<MeshFace> processedFaces, List<Vector2> unwrappedUVs )
	{
		if ( !faceToVertexIndices.TryGetValue( currentFace, out var currentIndices ) )
			return false;

		foreach ( var processedFace in processedFaces )
		{
			var sharedVertices = FindSharedVertices( currentFace, processedFace, unwrappedUVs );
			if ( sharedVertices.HasValue )
			{
				UnfoldFaceAlongEdge( currentFace, currentIndices, sharedVertices.Value, unwrappedUVs );
				return true;
			}
		}

		return false;
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

			for ( int j = 0; j < indices2.Count; j++ )
			{
				var idx2a = indices2[j];
				var idx2b = indices2[(j + 1) % indices2.Count];

				var pos1a = vertexPositions[idx1a];
				var pos1b = vertexPositions[idx1b];
				var pos2a = vertexPositions[idx2a];
				var pos2b = vertexPositions[idx2b];

				const float tolerance = 0.001f;
				bool matchForward = pos1a.Distance( pos2a ) < tolerance && pos1b.Distance( pos2b ) < tolerance;
				bool matchReverse = pos1a.Distance( pos2b ) < tolerance && pos1b.Distance( pos2a ) < tolerance;

				if ( matchForward || matchReverse )
				{
					return matchForward
						? (idx1a, idx1b, unwrappedUVs[idx2a], unwrappedUVs[idx2b])
						: (idx1a, idx1b, unwrappedUVs[idx2b], unwrappedUVs[idx2a]);
				}
			}
		}

		return null;
	}

	private void UnfoldFaceAlongEdge( MeshFace face, List<int> faceIndices, (int idx1, int idx2, Vector2 uv1, Vector2 uv2) sharedEdge, List<Vector2> unwrappedUVs )
	{
		unwrappedUVs[sharedEdge.idx1] = sharedEdge.uv1;
		unwrappedUVs[sharedEdge.idx2] = sharedEdge.uv2;

		var edge3DA = vertexPositions[sharedEdge.idx1];
		var edge3DB = vertexPositions[sharedEdge.idx2];
		var edge3D = edge3DB - edge3DA;
		var edge2D = sharedEdge.uv2 - sharedEdge.uv1;

		Vector3 thirdVertex = Vector3.Zero;
		foreach ( var idx in faceIndices )
		{
			if ( idx != sharedEdge.idx1 && idx != sharedEdge.idx2 )
			{
				thirdVertex = vertexPositions[idx];
				break;
			}
		}

		var faceNormal = edge3D.Cross( thirdVertex - edge3DA ).Normal;
		var localU = edge3D.Normal;
		var localV = faceNormal.Cross( localU );

		foreach ( var idx in faceIndices )
		{
			if ( idx == sharedEdge.idx1 || idx == sharedEdge.idx2 )
				continue;

			var pos3D = vertexPositions[idx];
			var relative3D = pos3D - edge3DA;
			var localPos = new Vector2( relative3D.Dot( localU ), relative3D.Dot( localV ) );

			var edgeLength2D = edge2D.Length;
			var edgeLength3D = edge3D.Length;
			var scale = edgeLength3D > 0 ? edgeLength2D / edgeLength3D : 1.0f;

			var edge2DDir = edge2D.Normal;
			var edge2DPerp = new Vector2( -edge2DDir.y, edge2DDir.x );

			unwrappedUVs[idx] = sharedEdge.uv1 + edge2DDir * localPos.x * scale + edge2DPerp * localPos.y * scale;
		}
	}

	public class UnwrapResult
	{
		public List<Vector2> VertexPositions { get; set; } = new();
		public List<List<int>> FaceIndices { get; set; } = new();
		public List<Vector3> OriginalPositions { get; set; } = new();
	}
}
