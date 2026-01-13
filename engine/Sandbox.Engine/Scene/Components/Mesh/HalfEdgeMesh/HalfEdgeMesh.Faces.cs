
namespace HalfEdgeMesh;

partial class Mesh
{
	public int ComputeNumEdgesInFace( FaceHandle hFace )
	{
		var nNumEdges = 0;

		if ( hFace.IsValid )
		{
			var hCurrentEdge = hFace.Edge;
			do
			{
				++nNumEdges;
				hCurrentEdge = hCurrentEdge.NextEdge;
			}
			while ( hCurrentEdge != hFace.Edge );
		}

		return nNumEdges;
	}

	public HalfEdgeHandle GetFirstEdgeInFaceLoop( FaceHandle hFace )
	{
		if ( !hFace.IsValid )
			return HalfEdgeHandle.Invalid;

		return hFace.Edge;
	}

	public HalfEdgeHandle GetNextEdgeInFaceLoop( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		return hEdge.NextEdge;
	}

	public HalfEdgeHandle FindPreviousEdgeInFaceLoop( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		var hCurrentEdge = hEdge;
		do
		{
			if ( hCurrentEdge.NextEdge == hEdge )
				return hCurrentEdge;

			hCurrentEdge = hCurrentEdge.NextEdge;
		}
		while ( hCurrentEdge != hEdge );

		return HalfEdgeHandle.Invalid;
	}

	public HalfEdgeHandle FindEdgeConnectedToFaceEndingAtVertex( FaceHandle hFace, VertexHandle hVertex )
	{
		if ( !hVertex.IsValid )
			return HalfEdgeHandle.Invalid;

		var hFirstEdge = GetFirstEdgeInVertexLoop( hVertex );
		var hOutgoingEdge = hFirstEdge;

		do
		{
			var hIncomingEdge = hOutgoingEdge.OppositeEdge;
			if ( hIncomingEdge.Face == hFace )
				return hIncomingEdge;

			hOutgoingEdge = hIncomingEdge.NextEdge;
		}
		while ( hOutgoingEdge != hFirstEdge );

		return HalfEdgeHandle.Invalid;
	}

	public HalfEdgeHandle FindEdgeConnectingFaces( FaceHandle hFaceA, FaceHandle hFaceB )
	{
		if ( !hFaceA.IsValid )
			return HalfEdgeHandle.Invalid;

		var hCurrentEdge = hFaceA.Edge;
		do
		{
			if ( hCurrentEdge.OppositeEdge.Face == hFaceB )
				return GetFullEdgeForHalfEdge( hCurrentEdge );

			hCurrentEdge = hCurrentEdge.NextEdge;
		}
		while ( hCurrentEdge != hFaceA.Edge );

		return HalfEdgeHandle.Invalid;
	}

	public bool GetHalfEdgesConnectedToFace( FaceHandle hFace, out HalfEdgeHandle[] hEdges )
	{
		hEdges = null;

		if ( !hFace.IsValid )
			return false;

		var nNumEdges = ComputeNumEdgesInFace( hFace );
		if ( nNumEdges <= 0 )
			return false;

		hEdges = new HalfEdgeHandle[nNumEdges];

		int i = 0;
		var hEdge = hFace.Edge;
		do
		{
			hEdges[i++] = hEdge;
			hEdge = hEdge.NextEdge;
		}
		while ( hEdge != hFace.Edge );

		return hEdges.Length == nNumEdges;
	}

	public bool GetFullEdgesConnectedToFace( FaceHandle hFace, out List<HalfEdgeHandle> edges )
	{
		edges = new List<HalfEdgeHandle>();

		if ( !hFace.IsValid )
			return false;

		int nNumEdges = ComputeNumEdgesInFace( hFace );
		if ( nNumEdges <= 0 )
			return false;

		edges.EnsureCapacity( nNumEdges );

		var hEdge = hFace.Edge;
		do
		{
			edges.Add( GetFullEdgeForHalfEdge( hEdge ) );
			hEdge = hEdge.NextEdge;
		}
		while ( hEdge != hFace.Edge );

		Assert.True( edges.Count == nNumEdges );
		return edges.Count == nNumEdges;
	}

	public bool GetVerticesConnectedToFace( FaceHandle hFace, out VertexHandle[] vertices )
	{
		vertices = null;

		if ( !hFace.IsValid )
			return false;

		int nNumVertices = ComputeNumEdgesInFace( hFace );
		if ( nNumVertices <= 0 )
			return false;

		vertices = new VertexHandle[nNumVertices];

		var i = 0;
		var hCurrentEdge = hFace.Edge;
		do
		{
			vertices[i++] = hCurrentEdge.Vertex;
			hCurrentEdge = hCurrentEdge.NextEdge;
		}
		while ( hCurrentEdge != hFace.Edge );

		return i == nNumVertices;
	}

	public void FindVerticesConnectedToFaces( IReadOnlyList<FaceHandle> pFaceList, int nNumFaces, out VertexHandle[] outVertices )
	{
		var uniqueVertices = new Dictionary<VertexHandle, int>();
		var i = 0;
		for ( var iFace = 0; iFace < nNumFaces; ++iFace )
		{
			var hFace = pFaceList[iFace];
			var hStartEdge = GetFirstEdgeInFaceLoop( hFace );

			if ( !hStartEdge.IsValid )
				continue;

			var hCurrentEdge = hStartEdge;
			do
			{
				if ( !uniqueVertices.ContainsKey( hCurrentEdge.Vertex ) )
					uniqueVertices.Add( hCurrentEdge.Vertex, i++ );

				hCurrentEdge = GetNextEdgeInFaceLoop( hCurrentEdge );
			}
			while ( hCurrentEdge != hStartEdge );
		}

		outVertices = new VertexHandle[uniqueVertices.Count];
		foreach ( var hVertex in uniqueVertices )
			outVertices[hVertex.Value] = hVertex.Key;
	}

	public void FindFullEdgesConnectedToFaces( IReadOnlyList<FaceHandle> pFaceList, int nNumFaces, out HalfEdgeHandle[] pOutEdgeList, out int[] pOutEdgeFaceCounts )
	{
		var nNumTotalFaceEdges = 0;
		for ( var iFace = 0; iFace < nNumFaces; ++iFace )
		{
			nNumTotalFaceEdges += ComputeNumEdgesInFace( pFaceList[iFace] );
		}

		var uniqueEdges = new Dictionary<HalfEdgeHandle, int>( nNumTotalFaceEdges );

		for ( var iFace = 0; iFace < nNumFaces; ++iFace )
		{
			var hFace = pFaceList[iFace];
			var hStartEdge = GetFirstEdgeInFaceLoop( hFace );
			if ( hStartEdge == HalfEdgeHandle.Invalid )
				continue;

			var hCurrentEdge = hStartEdge;
			do
			{
				var hFullEdge = GetFullEdgeForHalfEdge( hCurrentEdge );
				if ( !uniqueEdges.ContainsKey( hFullEdge ) )
					uniqueEdges.Add( hFullEdge, uniqueEdges.Count );

				hCurrentEdge = GetNextEdgeInFaceLoop( hCurrentEdge );
			}
			while ( hCurrentEdge != hStartEdge );
		}

		var nNumUniqueEdges = uniqueEdges.Count;
		pOutEdgeList = new HalfEdgeHandle[nNumUniqueEdges];

		foreach ( var pair in uniqueEdges )
		{
			pOutEdgeList[pair.Value] = pair.Key;
		}

		pOutEdgeFaceCounts = new int[nNumUniqueEdges];

		for ( var iFace = 0; iFace < nNumFaces; ++iFace )
		{
			var hFace = pFaceList[iFace];
			var hStartEdge = GetFirstEdgeInFaceLoop( hFace );
			if ( hStartEdge == HalfEdgeHandle.Invalid )
				continue;

			var hCurrentEdge = hStartEdge;
			do
			{
				var hFullEdge = GetFullEdgeForHalfEdge( hCurrentEdge );
				if ( uniqueEdges.TryGetValue( hFullEdge, out var nIndex ) )
				{
					pOutEdgeFaceCounts[nIndex]++;
				}

				hCurrentEdge = GetNextEdgeInFaceLoop( hCurrentEdge );
			}
			while ( hCurrentEdge != hStartEdge );
		}
	}

	public bool GetFacesConnectedToFace( FaceHandle hFace, out List<FaceHandle> faces )
	{
		faces = [];

		if ( !hFace.IsValid )
			return false;

		int edgeCount = ComputeNumEdgesInFace( hFace );
		if ( edgeCount <= 0 )
			return false;

		faces.EnsureCapacity( edgeCount );

		var hEdge = hFace.Edge;
		do
		{
			var hOppositeEdge = hEdge.OppositeEdge;
			if ( hOppositeEdge.Face != FaceHandle.Invalid )
			{
				faces.Add( hOppositeEdge.Face );
			}

			hEdge = hEdge.NextEdge;
		}
		while ( hEdge != hFace.Edge );

		return true;
	}
}
