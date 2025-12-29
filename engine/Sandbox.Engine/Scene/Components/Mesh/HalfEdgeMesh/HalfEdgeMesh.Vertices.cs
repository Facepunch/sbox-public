
namespace HalfEdgeMesh;

partial class Mesh
{
	public int ComputeNumEdgesConnectedToVertex( VertexHandle hVertex )
	{
		if ( !hVertex.IsValid )
			return 0;

		if ( hVertex.Edge == HalfEdgeHandle.Invalid )
			return 0;

		var nEdgeCount = 0;
		var hCurrentEdge = hVertex.Edge;
		do
		{
			hCurrentEdge = hCurrentEdge.OppositeEdge.NextEdge;
			++nEdgeCount;
		}
		while ( hCurrentEdge != hVertex.Edge );

		return nEdgeCount;
	}

	public int ComputeNumFacesConnectedToVertex( VertexHandle hVertex )
	{
		if ( !hVertex.IsValid )
			return 0;

		if ( hVertex.Edge == HalfEdgeHandle.Invalid )
			return 0;

		var nFaceCount = 0;
		var hCurrentEdge = hVertex.Edge;
		do
		{
			var hAdjEdge = hCurrentEdge.OppositeEdge;
			if ( (hCurrentEdge.Face != FaceHandle.Invalid) && (hCurrentEdge.Face != hAdjEdge.Face) )
				++nFaceCount;

			hCurrentEdge = hAdjEdge.NextEdge;
		}
		while ( hCurrentEdge != hVertex.Edge );

		if ( nFaceCount == 0 )
		{
			var pEdge = hVertex.Edge;
			if ( pEdge.Face != FaceHandle.Invalid )
				nFaceCount = 1;

			Assert.True( pEdge.OppositeEdge.Face == pEdge.Face );
		}

		return nFaceCount;
	}

	public bool IsVertexInternal( VertexHandle hVertex )
	{
		if ( !hVertex.IsValid )
			return false;

		if ( hVertex.Edge == HalfEdgeHandle.Invalid )
			return false;

		var hCurrentEdge = hVertex.Edge;
		do
		{
			var hOppositeEdge = hCurrentEdge.OppositeEdge;
			if ( hCurrentEdge.Face == hOppositeEdge.Face )
				return true;

			hCurrentEdge = hOppositeEdge.NextEdge;
		}
		while ( hCurrentEdge != hVertex.Edge );

		return false;
	}

	public HalfEdgeHandle GetFirstEdgeInVertexLoop( VertexHandle hVertex )
	{
		if ( !hVertex.IsValid )
			return HalfEdgeHandle.Invalid;

		return hVertex.Edge;
	}

	public HalfEdgeHandle GetNextEdgeInVertexLoop( HalfEdgeHandle hHalfEdge )
	{
		if ( hHalfEdge.IsValid )
			return hHalfEdge.OppositeEdge.NextEdge;

		return HalfEdgeHandle.Invalid;
	}

	public HalfEdgeHandle FindPreviousEdgeInVertexLoop( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		var hCurrentEdge = hEdge;
		HalfEdgeHandle hPreviousEdge;
		do
		{
			hPreviousEdge = hCurrentEdge;
			hCurrentEdge = hCurrentEdge.OppositeEdge.NextEdge;
		}
		while ( hCurrentEdge != hEdge );

		return hPreviousEdge;
	}

	public bool GetFullEdgesConnectedToVertex( VertexHandle hVertex, out List<HalfEdgeHandle> edges, EdgeConnectivityType nEdgeType = EdgeConnectivityType.Any )
	{
		edges = null;

		if ( !hVertex.IsValid )
			return false;

		var nNumEdges = ComputeNumEdgesConnectedToVertex( hVertex );
		if ( nNumEdges <= 0 )
			return false;

		edges = new List<HalfEdgeHandle>( nNumEdges );
		var hEdge = hVertex.Edge;
		do
		{
			var hFullEdge = GetFullEdgeForHalfEdge( hEdge );
			if ( (nEdgeType == EdgeConnectivityType.Any) ||
				 ((nEdgeType == EdgeConnectivityType.Open) && IsFullEdgeOpen( hFullEdge )) ||
				 ((nEdgeType == EdgeConnectivityType.Closed) && !IsFullEdgeOpen( hFullEdge )) )
			{
				edges.Add( hFullEdge );
			}
			hEdge = hEdge.OppositeEdge.NextEdge;
		}
		while ( hEdge != hVertex.Edge );

		return edges.Count > 0;
	}

	public bool GetOutgoingHalfEdgesConnectedToVertex( VertexHandle hVertex, out List<HalfEdgeHandle> edges )
	{
		edges = null;

		if ( !hVertex.IsValid )
			return false;

		var nNumEdges = ComputeNumEdgesConnectedToVertex( hVertex );
		if ( nNumEdges <= 0 )
			return false;

		edges = new List<HalfEdgeHandle>( nNumEdges );

		var hEdge = hVertex.Edge;
		do
		{
			edges.Add( hEdge );
			hEdge = hEdge.OppositeEdge.NextEdge;
		}
		while ( hEdge != hVertex.Edge );

		Assert.True( edges.Count == nNumEdges );
		return (edges.Count == nNumEdges);
	}

	public bool GetIncomingHalfEdgesConnectedToVertex( VertexHandle hVertex, out List<HalfEdgeHandle> edges )
	{
		edges = null;

		if ( !hVertex.IsValid )
			return false;

		var nNumEdges = ComputeNumEdgesConnectedToVertex( hVertex );
		if ( nNumEdges <= 0 )
			return false;

		edges = new List<HalfEdgeHandle>( nNumEdges );

		var hEdge = hVertex.Edge;
		do
		{
			var hAdjEdge = hEdge.OppositeEdge;
			edges.Add( hAdjEdge );
			hEdge = hAdjEdge.NextEdge;
		}
		while ( hEdge != hVertex.Edge );

		Assert.True( edges.Count == nNumEdges );
		return edges.Count == nNumEdges;
	}

	public bool GetVerticesConnectedToVertexByEdge( VertexHandle hVertex, out List<VertexHandle> vertices )
	{
		vertices = null;

		if ( !hVertex.IsValid )
			return false;

		var nNumVertices = ComputeNumEdgesConnectedToVertex( hVertex );
		if ( nNumVertices <= 0 )
			return false;

		vertices = new List<VertexHandle>( nNumVertices );

		var hEdge = hVertex.Edge;
		do
		{
			vertices.Add( hEdge.Vertex );
			hEdge = hEdge.OppositeEdge.NextEdge;
		}
		while ( hEdge != hVertex.Edge );

		Assert.True( vertices.Count == nNumVertices );
		return vertices.Count == nNumVertices;
	}

	public bool GetFacesConnectedToVertex( VertexHandle hVertex, out List<FaceHandle> faces )
	{
		faces = null;

		if ( !hVertex.IsValid )
			return false;

		int nNumFaces = ComputeNumFacesConnectedToVertex( hVertex );
		if ( nNumFaces <= 0 )
			return false;

		faces = new List<FaceHandle>( nNumFaces );

		var hCurrentEdge = hVertex.Edge;
		do
		{
			var hAdjEdge = hCurrentEdge.OppositeEdge;
			if ( (hCurrentEdge.Face != FaceHandle.Invalid) && (hCurrentEdge.Face != hAdjEdge.Face) )
				faces.Add( hCurrentEdge.Face );

			hCurrentEdge = hAdjEdge.NextEdge;
		}
		while ( hCurrentEdge != hVertex.Edge );

		if ( faces.Count == 0 )
		{
			var pEdge = hVertex.Edge;
			if ( pEdge.Face != FaceHandle.Invalid )
				faces.Add( pEdge.Face );

			Assert.True( pEdge.OppositeEdge.Face == pEdge.Face );
		}

		Assert.True( faces.Count == nNumFaces );
		return faces.Count == nNumFaces;
	}

	public HalfEdgeHandle FindHalfEdgeConnectingVertices( VertexHandle hVertexA, VertexHandle hVertexB )
	{
		if ( !hVertexA.IsValid )
			return HalfEdgeHandle.Invalid;

		var hEdge = hVertexA.Edge;
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		do
		{
			if ( hEdge.Vertex == hVertexB )
				return hEdge;

			hEdge = GetOppositeHalfEdge( hEdge ).NextEdge;
		}
		while ( hEdge != hVertexA.Edge );

		return HalfEdgeHandle.Invalid;
	}

	public HalfEdgeHandle FindFullEdgeConnectingVertices( VertexHandle hVertexA, VertexHandle hVertexB )
	{
		return GetFullEdgeForHalfEdge( FindHalfEdgeConnectingVertices( hVertexA, hVertexB ) );
	}

	public FaceHandle FindFaceWithEdgeConnectingVertices( VertexHandle hVertexA, VertexHandle hVertexB )
	{
		return GetFaceConnectedToHalfEdge( FindHalfEdgeConnectingVertices( hVertexA, hVertexB ) );
	}

	public FaceHandle FindFaceSharedByVertices( VertexHandle hVertexA, VertexHandle hVertexB )
	{
		var hFirstEdgeA = GetFirstEdgeInVertexLoop( hVertexA );
		if ( hFirstEdgeA == HalfEdgeHandle.Invalid )
			return FaceHandle.Invalid;

		var hFirstEdgeB = GetFirstEdgeInVertexLoop( hVertexB );
		if ( hFirstEdgeB == HalfEdgeHandle.Invalid )
			return FaceHandle.Invalid;

		var hCurrentEdgeA = hFirstEdgeA;

		do
		{
			var hFaceA1 = hCurrentEdgeA.Face;
			var hFaceA2 = hCurrentEdgeA.OppositeEdge.Face;

			var hCurrentEdgeB = hFirstEdgeB;

			do
			{
				var hFaceB1 = hCurrentEdgeB.Face;
				var hFaceB2 = hCurrentEdgeB.OppositeEdge.Face;

				if ( (hFaceA1 != FaceHandle.Invalid) && ((hFaceA1 == hFaceB1) || (hFaceA1 == hFaceB2)) )
					return hFaceA1;

				if ( (hFaceA2 != FaceHandle.Invalid) && ((hFaceA2 == hFaceB1) || (hFaceA2 == hFaceB2)) )
					return hFaceA2;

				hCurrentEdgeB = GetNextEdgeInVertexLoop( hCurrentEdgeB );
			}
			while ( hCurrentEdgeB != hFirstEdgeB );

			hCurrentEdgeA = GetNextEdgeInVertexLoop( hCurrentEdgeA );
		}
		while ( hCurrentEdgeA != hFirstEdgeA );

		return FaceHandle.Invalid;
	}

	public bool FindFacesSharedByVertices( VertexHandle hVertexA, VertexHandle hVertexB, out List<FaceHandle> faces )
	{
		GetFacesConnectedToVertex( hVertexA, out var connectedFacesA );
		GetFacesConnectedToVertex( hVertexB, out var connectedFacesB );

		var numConnectedA = connectedFacesA.Count;
		var numConnectedB = connectedFacesB.Count;

		faces = new List<FaceHandle>( Math.Min( numConnectedA, numConnectedB ) );

		for ( var i = 0; i < numConnectedA; ++i )
		{
			var hFace = connectedFacesA[i];
			if ( !hFace.IsValid )
				continue;

			if ( connectedFacesB.Contains( hFace ) )
				faces.Add( hFace );
		}

		return faces.Count > 0;
	}

	internal int FindFaceInSetSharedByVertices( VertexHandle hVertexA, VertexHandle hVertexB, List<FaceHandle> faceList )
	{
		if ( (IsVertexInMesh( hVertexA ) == false) || (IsVertexInMesh( hVertexB ) == false) )
			return -1;

		var hFirstEdgeA = GetFirstEdgeInVertexLoop( hVertexA );
		var hCurrentEdgeA = hFirstEdgeA;

		do
		{
			var hFaceA1 = hCurrentEdgeA.Face;
			var hFaceA2 = hCurrentEdgeA.OppositeEdge.Face;

			var hFirstEdgeB = GetFirstEdgeInVertexLoop( hVertexB );
			var hCurrentEdgeB = hFirstEdgeB;

			do
			{
				var hFaceB1 = hCurrentEdgeB.Face;
				var hFaceB2 = hCurrentEdgeB.OppositeEdge.Face;

				if ( (hFaceA1 != FaceHandle.Invalid) && ((hFaceA1 == hFaceB1) || (hFaceA1 == hFaceB2)) )
				{
					int nIndex = faceList.IndexOf( hFaceA1 );
					if ( nIndex != -1 )
						return nIndex;
				}

				if ( (hFaceA2 != FaceHandle.Invalid) && ((hFaceA2 == hFaceB1) || (hFaceA2 == hFaceB2)) )
				{
					int nIndex = faceList.IndexOf( hFaceA2 );
					if ( nIndex != -1 )
						return nIndex;
				}

				hCurrentEdgeB = GetNextEdgeInVertexLoop( hCurrentEdgeB );
			}
			while ( hCurrentEdgeB != hFirstEdgeB );

			hCurrentEdgeA = GetNextEdgeInVertexLoop( hCurrentEdgeA );
		}
		while ( hCurrentEdgeA != hFirstEdgeA );

		return -1;
	}

	public void FindFacesConnectedToVertices( IReadOnlyList<VertexHandle> hVertices, int nNumVertices, out FaceHandle[] newFaces, out int[] faceVertexCounts )
	{
		var uniqueFaces = new Dictionary<FaceHandle, int>( nNumVertices * 5 );

		for ( var i = 0; i < nNumVertices; ++i )
		{
			if ( !GetFacesConnectedToVertex( hVertices[i], out var facesConnectedToVertex ) )
				continue;

			foreach ( var hFace in facesConnectedToVertex )
			{
				if ( !uniqueFaces.ContainsKey( hFace ) )
					uniqueFaces.Add( hFace, uniqueFaces.Count );
			}
		}

		var numUniqueFaces = uniqueFaces.Count;
		newFaces = new FaceHandle[numUniqueFaces];

		foreach ( var pair in uniqueFaces )
		{
			newFaces[pair.Value] = pair.Key;
		}

		faceVertexCounts = new int[numUniqueFaces];

		for ( var iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			if ( !GetFacesConnectedToVertex( hVertices[iVertex], out var facesConnectedToVertex ) )
				continue;

			var nNumFaces = facesConnectedToVertex.Count;
			for ( var iFace = 0; iFace < nNumFaces; ++iFace )
			{
				if ( uniqueFaces.TryGetValue( facesConnectedToVertex[iFace], out var nIndex ) )
				{
					faceVertexCounts[nIndex]++;
				}
			}
		}
	}

	public void FindFullEdgesConnectedToVertices( IReadOnlyList<VertexHandle> hVertices, int nNumVertices, out HalfEdgeHandle[] newEdges, out int[] edgeVertexCounts )
	{
		var uniqueEdges = new Dictionary<HalfEdgeHandle, int>( nNumVertices * 2 );

		for ( var iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			if ( !GetFullEdgesConnectedToVertex( hVertices[iVertex], out var edgesConnectedToVertex, EdgeConnectivityType.Any ) )
				continue;

			foreach ( var hEdge in edgesConnectedToVertex )
			{
				if ( !uniqueEdges.ContainsKey( hEdge ) )
					uniqueEdges.Add( hEdge, uniqueEdges.Count );
			}
		}

		var nNumUniqueEdges = uniqueEdges.Count;
		newEdges = new HalfEdgeHandle[nNumUniqueEdges];

		foreach ( var pair in uniqueEdges )
		{
			newEdges[pair.Value] = pair.Key;
		}

		edgeVertexCounts = new int[nNumUniqueEdges];

		for ( var iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			if ( !GetFullEdgesConnectedToVertex( hVertices[iVertex], out var edgesConnectedToVertex, EdgeConnectivityType.Any ) )
				continue;

			var nNumEdges = edgesConnectedToVertex.Count;
			for ( var iEdge = 0; iEdge < nNumEdges; ++iEdge )
			{
				if ( uniqueEdges.TryGetValue( edgesConnectedToVertex[iEdge], out var nIndex ) )
				{
					edgeVertexCounts[nIndex]++;
				}
			}
		}
	}

	public void FindVertexIslands( IReadOnlyList<VertexHandle> hVertices, int nNumVertices, out List<List<VertexHandle>> pOutVertexList )
	{
		pOutVertexList = new List<List<VertexHandle>>();
		var vertexSearchList = hVertices.Take( nNumVertices ).ToList();

		while ( vertexSearchList.Count() > 0 )
		{
			var hStartVertex = vertexSearchList[0];
			vertexSearchList.RemoveAt( 0 );

			if ( IsVertexInMesh( hStartVertex ) == false )
				continue;

			var islandVertexList = new List<VertexHandle>( 32 )
			{
				hStartVertex
			};

			pOutVertexList.Add( islandVertexList );

			var nIslandVertexIndex = 0;

			while ( nIslandVertexIndex < islandVertexList.Count )
			{
				var hCurrentVertex = islandVertexList[nIslandVertexIndex];

				GetVerticesConnectedToVertexByEdge( hCurrentVertex, out var verticesConnectedToVertex );

				var nNumConnectedVertices = verticesConnectedToVertex.Count();
				for ( var iVertex = 0; iVertex < nNumConnectedVertices; ++iVertex )
				{
					var hConnectedVertex = verticesConnectedToVertex[iVertex];
					if ( hConnectedVertex == hCurrentVertex )
						continue;

					var nIndexInSearchList = vertexSearchList.IndexOf( hConnectedVertex );
					if ( nIndexInSearchList != -1 )
					{
						islandVertexList.Add( hConnectedVertex );
						vertexSearchList.RemoveAt( nIndexInSearchList );
					}
				}

				++nIslandVertexIndex;
			}
		}
	}
}
