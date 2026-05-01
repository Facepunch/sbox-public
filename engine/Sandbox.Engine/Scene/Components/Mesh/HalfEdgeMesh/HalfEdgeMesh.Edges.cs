
namespace HalfEdgeMesh;

partial class Mesh
{
	public VertexHandle GetEndVertexConnectedToEdge( HalfEdgeHandle hHalfEdge )
	{
		if ( !hHalfEdge.IsValid )
			return VertexHandle.Invalid;

		return hHalfEdge.Vertex;
	}

	public void GetVerticesConnectedToHalfEdge( HalfEdgeHandle hEdge, out VertexHandle hVertexA, out VertexHandle hVertexB )
	{
		if ( !hEdge.IsValid )
		{
			hVertexA = VertexHandle.Invalid;
			hVertexB = VertexHandle.Invalid;
			return;
		}

		hVertexA = GetOppositeHalfEdge( hEdge ).Vertex;
		hVertexB = hEdge.Vertex;
	}

	public void GetVerticesConnectedToFullEdge( HalfEdgeHandle hEdge, out VertexHandle hVertexA, out VertexHandle hVertexB )
	{
		GetVerticesConnectedToHalfEdge( hEdge, out hVertexA, out hVertexB );
	}

	public void GetHalfEdgesConnectedToFullEdge( HalfEdgeHandle hFullEdge, out HalfEdgeHandle hOutHalfEdgeA, out HalfEdgeHandle hOutHalfEdgeB )
	{
		if ( hFullEdge.IsValid )
		{
			hOutHalfEdgeA = hFullEdge;
			hOutHalfEdgeB = hFullEdge.OppositeEdge;
		}
		else
		{
			hOutHalfEdgeA = HalfEdgeHandle.Invalid;
			hOutHalfEdgeB = HalfEdgeHandle.Invalid;
		}
	}

	public HalfEdgeHandle GetHalfEdgeForFaceEdge( FaceHandle hFace, HalfEdgeHandle hFullEdge )
	{
		if ( !hFullEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		if ( hFullEdge.Face == hFace )
			return hFullEdge;

		if ( hFullEdge.OppositeEdge.Face == hFace )
			return hFullEdge.OppositeEdge;

		return HalfEdgeHandle.Invalid;
	}

	public HalfEdgeHandle GetFullEdgeForHalfEdge( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		if ( hEdge.Index < this[hEdge].OppositeEdge )
		{
			return hEdge;
		}
		else
		{
			return GetOppositeHalfEdge( hEdge );
		}
	}

	public HalfEdgeHandle GetOppositeHalfEdge( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		return hEdge.OppositeEdge;
	}

	public bool IsFullEdgeOpen( HalfEdgeHandle hFullEdge )
	{
		if ( !hFullEdge.IsValid )
			return false;

		if ( hFullEdge.Face == FaceHandle.Invalid )
			return true;

		if ( hFullEdge.OppositeEdge.Face == FaceHandle.Invalid )
			return true;

		return false;
	}

	public void GetFacesConnectedToFullEdge( HalfEdgeHandle hFullEdge, out FaceHandle hOutFaceA, out FaceHandle hOutFaceB )
	{
		hOutFaceA = FaceHandle.Invalid;
		hOutFaceB = FaceHandle.Invalid;

		if ( !hFullEdge.IsValid )
			return;

		hOutFaceA = hFullEdge.Face;
		hOutFaceB = hFullEdge.OppositeEdge.Face;
	}

	public FaceHandle GetOppositeFaceConnectedToFullEdge( HalfEdgeHandle hFullEdge, FaceHandle hFace )
	{
		GetHalfEdgesConnectedToFullEdge( hFullEdge, out var hHalfEdgeA, out var hHalfEdgeB );

		if ( GetFaceConnectedToHalfEdge( hHalfEdgeA ) == hFace )
		{
			return GetFaceConnectedToHalfEdge( hHalfEdgeB );
		}
		else if ( GetFaceConnectedToHalfEdge( hHalfEdgeB ) == hFace )
		{
			return GetFaceConnectedToHalfEdge( hHalfEdgeA );
		}

		return FaceHandle.Invalid;
	}

	public FaceHandle GetFaceConnectedToHalfEdge( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return FaceHandle.Invalid;

		return hEdge.Face;
	}

	public FaceHandle GetFaceConnectedToFullEdge( HalfEdgeHandle hFullEdge )
	{
		if ( !hFullEdge.IsValid )
			return FaceHandle.Invalid;

		if ( hFullEdge.Face.IsValid )
			return hFullEdge.Face;

		return hFullEdge.OppositeEdge.Face;
	}

	public FaceHandle FindFaceConnectingFullEdges( HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB )
	{
		GetFacesConnectedToFullEdge( hEdgeA, out var hFaceA1, out var hFaceA2 );
		GetFacesConnectedToFullEdge( hEdgeB, out var hFaceB1, out var hFaceB2 );

		if ( hFaceA1.IsValid && ((hFaceA1 == hFaceB1) || (hFaceA1 == hFaceB2)) )
			return hFaceA1;

		if ( hFaceA2.IsValid && ((hFaceA2 == hFaceB1) || (hFaceA2 == hFaceB2)) )
			return hFaceA2;

		return FaceHandle.Invalid;
	}

	public VertexHandle FindVertexConnectingFullEdges( HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB )
	{
		GetVerticesConnectedToHalfEdge( hEdgeA, out var hVertexA1, out var hVertexA2 );
		GetVerticesConnectedToHalfEdge( hEdgeB, out var hVertexB1, out var hVertexB2 );

		if ( (hVertexA1 == hVertexB1) || (hVertexA1 == hVertexB2) )
			return hVertexA1;

		if ( (hVertexA2 == hVertexB1) || (hVertexA2 == hVertexB2) )
			return hVertexA2;

		return VertexHandle.Invalid;
	}

	public void FindVerticesConnectedToFullEdges( IReadOnlyList<HalfEdgeHandle> edgeList, out VertexHandle[] outVertices )
	{
		var nNumEdges = edgeList.Count;
		var uniqueVertices = new Dictionary<VertexHandle, int>();
		var i = 0;

		for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			GetVerticesConnectedToFullEdge( edgeList[iEdge], out var hVertexA, out var hVertexB );

			if ( !uniqueVertices.ContainsKey( hVertexA ) )
				uniqueVertices.Add( hVertexA, i++ );

			if ( !uniqueVertices.ContainsKey( hVertexB ) )
				uniqueVertices.Add( hVertexB, i++ );
		}

		outVertices = new VertexHandle[uniqueVertices.Count];
		foreach ( var hVertex in uniqueVertices )
		{
			outVertices[hVertex.Value] = hVertex.Key;
		}
	}

	public void FindFacesConnectedToHalfEdges( IReadOnlyList<HalfEdgeHandle> pHalfEdgeList, int nNumEdges, out List<FaceHandle> pOutFaceList, out List<HalfEdgeHandle> pOutEdgeForFaces )
	{
		pOutFaceList = new List<FaceHandle>( nNumEdges );
		pOutEdgeForFaces = new List<HalfEdgeHandle>( nNumEdges );

		var faceTable = new HashSet<FaceHandle>( nNumEdges );

		for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			var hEdge = pHalfEdgeList[iEdge];
			if ( !hEdge.IsValid )
				continue;

			if ( faceTable.Add( hEdge.Face ) )
			{
				pOutFaceList.Add( hEdge.Face );
				pOutEdgeForFaces.Add( hEdge );
			}
		}
	}

	public HalfEdgeHandle FindOppositeHalfEdgeInFace( HalfEdgeHandle hEdge, int nMaxFaceSides = -1 )
	{
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		if ( hEdge.Face == FaceHandle.Invalid )
			return HalfEdgeHandle.Invalid;

		var nNumEdges = ComputeNumEdgesInFace( hEdge.Face );
		if ( ((nNumEdges % 2) != 0) || (nNumEdges < 3) )
			return HalfEdgeHandle.Invalid;

		if ( (nMaxFaceSides > 0) && (nNumEdges > nMaxFaceSides) )
			return HalfEdgeHandle.Invalid;

		var nStepCount = nNumEdges / 2;
		var hCurrentEdge = hEdge;
		for ( int iStep = 0; iStep < nStepCount; ++iStep )
		{
			hCurrentEdge = hCurrentEdge.NextEdge;
		}

		return hCurrentEdge;
	}

	public HalfEdgeHandle FindNextHalfEdgeInLoop( HalfEdgeHandle hEdge, int nMaxVertexEdges )
	{
		var hVertex = GetEndVertexConnectedToEdge( hEdge );
		var nNumEdges = ComputeNumEdgesConnectedToVertex( hVertex );

		if ( IsHalfEdgeInMesh( hEdge ) == false )
			return HalfEdgeHandle.Invalid;

		if ( (nNumEdges <= 1) || ((nNumEdges % 2) != 0) || ((nMaxVertexEdges > 0) && (nNumEdges > nMaxVertexEdges)) )
			return HalfEdgeHandle.Invalid;

		int nOppositeEdge = nNumEdges / 2;
		var hNextEdge = GetNextEdgeInFaceLoop( hEdge );
		for ( int i = 1; i < nOppositeEdge; ++i )
		{
			hNextEdge = GetNextEdgeInVertexLoop( hNextEdge );
		}

		if ( !hNextEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		if ( (hNextEdge.Face == FaceHandle.Invalid) || (hNextEdge.OppositeEdge.Face == FaceHandle.Invalid) )
			return HalfEdgeHandle.Invalid;

		Assert.True( hNextEdge != hEdge );
		if ( hNextEdge == hEdge )
			return HalfEdgeHandle.Invalid;

		return hNextEdge;
	}

	public void FindEdgeLoopStartingAtEdge( HalfEdgeHandle hStartEdge, int nMaxVertexEdges, out HalfEdgeHandle[] pOutEdgeList )
	{
		pOutEdgeList = null;

		if ( !IsHalfEdgeInMesh( hStartEdge ) )
			return;

		int nEdgeCount = 0;
		var hCurrentEdge = hStartEdge;
		do
		{
			hCurrentEdge = FindNextHalfEdgeInLoop( hCurrentEdge, nMaxVertexEdges );
			++nEdgeCount;
		}
		while ( (hCurrentEdge != HalfEdgeHandle.Invalid) && (hCurrentEdge != hStartEdge) );

		pOutEdgeList = new HalfEdgeHandle[nEdgeCount];

		nEdgeCount = 0;
		hCurrentEdge = hStartEdge;
		do
		{
			pOutEdgeList[nEdgeCount++] = hCurrentEdge;
			hCurrentEdge = FindNextHalfEdgeInLoop( hCurrentEdge, nMaxVertexEdges );
		}
		while ( (hCurrentEdge != HalfEdgeHandle.Invalid) && (hCurrentEdge != hStartEdge) );
	}

	public void FindEdgeLoop( HalfEdgeHandle hEdge, int nMaxVertices, out List<HalfEdgeHandle> pOutEdgeList )
	{
		GetHalfEdgesConnectedToFullEdge( hEdge, out var hHalfEdgeA, out var hHalfEdgeB );

		FindEdgeLoopStartingAtEdge( hHalfEdgeA, nMaxVertices, out var edgeLoopA );

		HalfEdgeHandle[] edgeLoopB = null;
		if ( (edgeLoopA.Length < 2) || (FindNextHalfEdgeInLoop( edgeLoopA.LastOrDefault(), nMaxVertices ) != edgeLoopA.FirstOrDefault()) )
		{
			FindEdgeLoopStartingAtEdge( hHalfEdgeB, nMaxVertices, out edgeLoopB );
		}

		var nNumEdgesA = edgeLoopA is null ? 0 : edgeLoopA.Length;
		var nNumEdgesB = edgeLoopB is null ? 0 : edgeLoopB.Length;
		pOutEdgeList = new List<HalfEdgeHandle>( nNumEdgesA + nNumEdgesB );

		for ( int iEdge = (nNumEdgesB - 1); iEdge > 0; --iEdge )
		{
			pOutEdgeList.Add( GetFullEdgeForHalfEdge( edgeLoopB[iEdge] ) );
		}
		for ( int iEdge = 0; iEdge < nNumEdgesA; ++iEdge )
		{
			pOutEdgeList.Add( GetFullEdgeForHalfEdge( edgeLoopA[iEdge] ) );
		}
	}

	public void FindEdgeRing( HalfEdgeHandle hEdge, out List<HalfEdgeHandle> outEdgeList )
	{
		GetHalfEdgesConnectedToFullEdge( hEdge, out var hStartHalfEdgeA, out var hStartHalfEdgeB );

		var edgesA = new List<HalfEdgeHandle>();
		var edgesB = new List<HalfEdgeHandle>();

		for ( int i = 0; i < 2; ++i )
		{
			var hCurrentEdge = hStartHalfEdgeA;
			int nNumEdgesA = 0;
			do
			{
				if ( i == 1 )
				{
					edgesA.Add( hCurrentEdge );
				}
				++nNumEdgesA;
				var hFaceOppositeEdge = FindOppositeHalfEdgeInFace( hCurrentEdge );
				if ( !hFaceOppositeEdge.IsValid )
					break;

				hCurrentEdge = hFaceOppositeEdge.OppositeEdge;
			}
			while ( hCurrentEdge != hStartHalfEdgeA );
			edgesA.EnsureCapacity( nNumEdgesA );

			if ( (hCurrentEdge != hStartHalfEdgeA) || (nNumEdgesA < 2) )
			{
				int nNumEdgesB = 0;
				var hLastEdge = hCurrentEdge;
				hCurrentEdge = hStartHalfEdgeB;
				do
				{
					var hFaceOppositeEdge = FindOppositeHalfEdgeInFace( hCurrentEdge );
					if ( !hFaceOppositeEdge.IsValid || (hFaceOppositeEdge == hLastEdge) )
						break;

					hCurrentEdge = hFaceOppositeEdge.OppositeEdge;

					if ( i == 1 )
					{
						edgesB.Add( hCurrentEdge );
					}
					++nNumEdgesB;
				}
				while ( hCurrentEdge != hStartHalfEdgeB );

				edgesB.EnsureCapacity( nNumEdgesB );
			}
		}

		outEdgeList = new List<HalfEdgeHandle>( edgesA.Count + edgesB.Count );

		for ( int iEdge = edgesB.Count - 1; iEdge >= 0; --iEdge )
		{
			outEdgeList.Add( GetFullEdgeForHalfEdge( edgesB[iEdge] ) );
		}

		for ( int iEdge = 0; iEdge < edgesA.Count; ++iEdge )
		{
			outEdgeList.Add( GetFullEdgeForHalfEdge( edgesA[iEdge] ) );
		}
	}

	public void FindEdgeIslands( IReadOnlyList<HalfEdgeHandle> pEdgeList, out List<List<HalfEdgeHandle>> pOutEdgeList )
	{
		pOutEdgeList = new();

		var edgeSearchList = new List<HalfEdgeHandle>( pEdgeList );

		while ( edgeSearchList.Count > 0 )
		{
			var hStartEdge = edgeSearchList[0];
			edgeSearchList.RemoveAt( 0 );
			if ( !hStartEdge.IsValid )
				continue;

			var islandEdgeList = new List<HalfEdgeHandle>( 32 );
			pOutEdgeList.Add( islandEdgeList );
			islandEdgeList.Add( hStartEdge );

			var nIslandEdgeIndex = 0;

			while ( nIslandEdgeIndex < islandEdgeList.Count )
			{
				var hCurrentEdge = islandEdgeList[nIslandEdgeIndex];

				GetVerticesConnectedToFullEdge( hCurrentEdge, out var hVertexA, out var hVertexB );
				GetFullEdgesConnectedToVertex( hVertexA, out var edgesConnectedToEdge, EdgeConnectivityType.Any );
				GetFullEdgesConnectedToVertex( hVertexB, out var edgesConnectedToVertex, EdgeConnectivityType.Any );
				edgesConnectedToEdge.AddRange( edgesConnectedToVertex );

				var nNumConnectedEdges = edgesConnectedToEdge.Count;
				for ( int iEdge = 0; iEdge < nNumConnectedEdges; ++iEdge )
				{
					var hConnectedEdge = edgesConnectedToEdge[iEdge];
					if ( hConnectedEdge == hCurrentEdge )
						continue;

					int nIndexInSearchList = edgeSearchList.IndexOf( hConnectedEdge );
					if ( nIndexInSearchList != -1 )
					{
						islandEdgeList.Add( hConnectedEdge );
						edgeSearchList.RemoveAt( nIndexInSearchList );
					}
				}

				++nIslandEdgeIndex;
			}
		}
	}

	public ComponentConnectivityType ClassifyEdgeListConnectivity( IReadOnlyList<HalfEdgeHandle> pEdgeList, int nNumEdges, out List<HalfEdgeHandle> pOutSortedHalfEdgeList )
	{
		pOutSortedHalfEdgeList = new List<HalfEdgeHandle>();

		if ( (pEdgeList is null) || (nNumEdges <= 0) )
			return ComponentConnectivityType.None;

		int nNumHalfEdges = nNumEdges * 2;
		var halfEdges = new HalfEdgeHandle[nNumHalfEdges];
		for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			GetHalfEdgesConnectedToFullEdge( pEdgeList[iEdge], out halfEdges[iEdge * 2], out halfEdges[iEdge * 2 + 1] );
		}

		var connectivityType = ComponentConnectivityType.None;
		var hFirstEdgeInList = HalfEdgeHandle.Invalid;

		if ( nNumEdges > 1 )
		{
			for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
			{
				var hStartEdge = halfEdges[iEdge * 2];
				var hStartEdgeOpposite = halfEdges[iEdge * 2 + 1];
				var hCurrentEdge = hStartEdge;
				int nNumConnectedHalfEdges = 0;
				bool bVisitedOpposite = false;
				bool bFoundSplit = false;

				do
				{
					++nNumConnectedHalfEdges;
					if ( hCurrentEdge == hStartEdgeOpposite )
					{
						bVisitedOpposite = true;
					}

					var hOppositeEdge = hCurrentEdge.OppositeEdge;
					var hNextEdgeInList = FindConnectedHalfEdgeInSet( hCurrentEdge, halfEdges, halfEdges.Length );

					if ( hNextEdgeInList == hOppositeEdge )
					{
						if ( (hFirstEdgeInList != halfEdges[0]) && (hFirstEdgeInList != halfEdges[1]) )
						{
							hFirstEdgeInList = hNextEdgeInList;
						}
					}
					else
					{
						var hNextOpposite = hNextEdgeInList.OppositeEdge;
						if ( FindConnectedHalfEdgeInSet( hNextOpposite, halfEdges, halfEdges.Length ) != hOppositeEdge )
						{
							bFoundSplit = true;
						}
					}

					hCurrentEdge = hNextEdgeInList;
				}
				while ( hCurrentEdge != hStartEdge );

				if ( nNumConnectedHalfEdges <= 2 )
					continue;

				if ( iEdge == 0 )
				{
					if ( (bVisitedOpposite == false) && (nNumConnectedHalfEdges == nNumEdges) )
					{
						connectivityType = ComponentConnectivityType.Loop;
						break;
					}

					if ( nNumConnectedHalfEdges == nNumHalfEdges )
					{
						connectivityType = bFoundSplit ? ComponentConnectivityType.Tree : ComponentConnectivityType.List;
						break;
					}
				}

				connectivityType = ComponentConnectivityType.Mixed;
				break;
			}
		}
		else
		{
			connectivityType = ComponentConnectivityType.List;
			hFirstEdgeInList = halfEdges[0];
		}

		{
			var hStartEdge = HalfEdgeHandle.Invalid;

			if ( connectivityType == ComponentConnectivityType.Loop )
			{
				hStartEdge = halfEdges[0];
				Assert.True( hFirstEdgeInList == HalfEdgeHandle.Invalid );
			}
			else if ( connectivityType == ComponentConnectivityType.List )
			{
				hStartEdge = hFirstEdgeInList;
				Assert.True( hFirstEdgeInList != HalfEdgeHandle.Invalid );
			}

			if ( hStartEdge != HalfEdgeHandle.Invalid )
			{
				pOutSortedHalfEdgeList.EnsureCapacity( nNumEdges );

				var hCurrentEdge = hStartEdge;
				do
				{
					pOutSortedHalfEdgeList.Add( hCurrentEdge );
					var hNextEdge = FindConnectedHalfEdgeInSet( hCurrentEdge, halfEdges, halfEdges.Length );

					if ( hNextEdge == hCurrentEdge.OppositeEdge )
						break;

					hCurrentEdge = hNextEdge;
				}
				while ( hCurrentEdge != hStartEdge );
			}

			if ( connectivityType == ComponentConnectivityType.Loop )
			{
				var nNumSortedEdges = pOutSortedHalfEdgeList.Count;
				var sortedListOpposite = new HalfEdgeHandle[nNumSortedEdges];

				var facesA = new HashSet<FaceHandle>( nNumSortedEdges );
				var facesB = new HashSet<FaceHandle>( nNumSortedEdges );

				for ( int iEdge = 0; iEdge < nNumSortedEdges; ++iEdge )
				{
					var hHalfEdgeA = pOutSortedHalfEdgeList[iEdge];
					var hHalfEdgeB = GetOppositeHalfEdge( hHalfEdgeA );
					var hFaceA = GetFaceConnectedToHalfEdge( hHalfEdgeA );
					var hFaceB = GetFaceConnectedToHalfEdge( hHalfEdgeB );
					facesA.Add( hFaceA );
					facesB.Add( hFaceB );

					sortedListOpposite[nNumSortedEdges - iEdge - 1] = hHalfEdgeB;
				}

				if ( facesB.Count < facesA.Count )
				{
					pOutSortedHalfEdgeList = sortedListOpposite.ToList();
				}
			}
		}

		return connectivityType;
	}

	public int FindEdgeRibs( IReadOnlyList<HalfEdgeHandle> edges, int numEdges,
		out List<List<HalfEdgeHandle>> leftRibs, out List<List<HalfEdgeHandle>> rightRibs,
		out List<HalfEdgeHandle> spineEdges )
	{
		const int maxFaceEdges = 4;

		leftRibs = new List<List<HalfEdgeHandle>>();
		rightRibs = new List<List<HalfEdgeHandle>>();

		var connectivityType = ClassifyEdgeListConnectivity( edges, numEdges, out spineEdges );
		if ( (connectivityType != ComponentConnectivityType.Loop) && (connectivityType != ComponentConnectivityType.List) )
			return 0;

		var numSpineEdges = spineEdges.Count;
		var numRibs = numSpineEdges * 2;
		var ribCount = 0;

		for ( var i = 0; i < numRibs; i++ )
		{
			leftRibs.Add( new List<HalfEdgeHandle>() );
			rightRibs.Add( new List<HalfEdgeHandle>() );
		}

		for ( var i = 0; i < numSpineEdges; ++i )
		{
			var leftRibEdgesTop = leftRibs[ribCount + 0];
			var leftRibEdgesBottom = leftRibs[ribCount + 1];
			var rightRibEdgesTop = rightRibs[ribCount + 0];
			var rightRibEdgesBottom = rightRibs[ribCount + 1];
			ribCount += 2;

			var hStartEdge = spineEdges[i];
			var hCurrentEdge = hStartEdge;
			do
			{
				var hFaceOppositeEdge = FindOppositeHalfEdgeInFace( hCurrentEdge, maxFaceEdges );
				if ( !hFaceOppositeEdge.IsValid )
					break;

				leftRibEdgesTop.Add( FindPreviousEdgeInFaceLoop( hCurrentEdge ) );
				leftRibEdgesBottom.Add( GetNextEdgeInFaceLoop( hCurrentEdge ) );
				hCurrentEdge = hFaceOppositeEdge.OppositeEdge;
			}
			while ( hCurrentEdge != hStartEdge );

			hStartEdge = spineEdges[i].OppositeEdge;
			hCurrentEdge = hStartEdge;
			do
			{
				var hFaceOppositeEdge = FindOppositeHalfEdgeInFace( hCurrentEdge, maxFaceEdges );
				if ( !hFaceOppositeEdge.IsValid )
					break;

				rightRibEdgesTop.Add( GetNextEdgeInFaceLoop( hCurrentEdge ) );
				rightRibEdgesBottom.Add( FindPreviousEdgeInFaceLoop( hCurrentEdge ) );
				hCurrentEdge = hFaceOppositeEdge.OppositeEdge;
			}
			while ( hCurrentEdge != hStartEdge );
		}

		Assert.True( ribCount == numRibs );

		return numRibs;
	}

	public void FindFacesConnectedToFullEdges( IReadOnlyList<HalfEdgeHandle> edgeList, List<FaceHandle> outFaces, List<int> outFaceEdgeCounts )
	{
		outFaces.Clear();
		outFaceEdgeCounts?.Clear();

		var uniqueFaces = new Dictionary<FaceHandle, int>();

		for ( int i = 0; i < edgeList.Count; i++ )
		{
			GetFacesConnectedToFullEdge( edgeList[i], out var faceA, out var faceB );

			if ( faceA.IsValid && !uniqueFaces.ContainsKey( faceA ) )
				uniqueFaces.Add( faceA, uniqueFaces.Count );

			if ( faceB.IsValid && !uniqueFaces.ContainsKey( faceB ) )
				uniqueFaces.Add( faceB, uniqueFaces.Count );
		}

		foreach ( var kv in uniqueFaces )
			outFaces.Add( kv.Key );

		if ( outFaceEdgeCounts != null )
		{
			outFaceEdgeCounts.AddRange( new int[outFaces.Count] );

			for ( int i = 0; i < edgeList.Count; i++ )
			{
				GetFacesConnectedToFullEdge( edgeList[i], out var faceA, out var faceB );

				if ( faceA.IsValid && uniqueFaces.TryGetValue( faceA, out var indexA ) )
					outFaceEdgeCounts[indexA]++;

				if ( faceB.IsValid && uniqueFaces.TryGetValue( faceB, out var indexB ) )
					outFaceEdgeCounts[indexB]++;
			}
		}
	}

	public void FindOpenEdgeIslands( IReadOnlyList<HalfEdgeHandle> edges, out List<List<HalfEdgeHandle>> outHalfEdgeIslandList, out List<List<HalfEdgeHandle>> outFullEdgeIslandList )
	{
		outHalfEdgeIslandList = new();
		outFullEdgeIslandList = new();

		var edgeSearchList = new List<HalfEdgeHandle>( edges.Count );

		for ( int iEdge = 0; iEdge < edges.Count; ++iEdge )
		{
			GetHalfEdgesConnectedToFullEdge( edges[iEdge], out var hHalfEdgeA, out var hHalfEdgeB );

			var hFaceA = GetFaceConnectedToHalfEdge( hHalfEdgeA );
			var hFaceB = GetFaceConnectedToHalfEdge( hHalfEdgeB );

			if ( hFaceA == FaceHandle.Invalid && hFaceB != FaceHandle.Invalid )
			{
				edgeSearchList.Add( hHalfEdgeA );
			}
			else if ( hFaceB == FaceHandle.Invalid && hFaceA != FaceHandle.Invalid )
			{
				edgeSearchList.Add( hHalfEdgeB );
			}
		}

		while ( edgeSearchList.Count > 0 )
		{
			var hLoopStartEdge = edgeSearchList[0];
			var hCurrentEdge = hLoopStartEdge;
			int nCurrentEdgeIndex = 0;

			do
			{
				int nPrevEdgeIndex = nCurrentEdgeIndex;
				hCurrentEdge = hCurrentEdge.NextEdge;
				nCurrentEdgeIndex = edgeSearchList.IndexOf( hCurrentEdge );

				if ( nPrevEdgeIndex == -1 && nCurrentEdgeIndex != -1 )
					break;
			}
			while ( hCurrentEdge != hLoopStartEdge );

			Assert.True( nCurrentEdgeIndex != -1 );
			if ( nCurrentEdgeIndex == -1 )
				break;

			var islandHalfEdgeList = new List<HalfEdgeHandle>();
			var islandFullEdgeList = new List<HalfEdgeHandle>();

			outHalfEdgeIslandList.Add( islandHalfEdgeList );
			outFullEdgeIslandList.Add( islandFullEdgeList );

			while ( nCurrentEdgeIndex != -1 )
			{
				islandHalfEdgeList.Add( hCurrentEdge );
				islandFullEdgeList.Add( GetFullEdgeForHalfEdge( hCurrentEdge ) );
				edgeSearchList.RemoveAt( nCurrentEdgeIndex );

				hCurrentEdge = hCurrentEdge.NextEdge;
				nCurrentEdgeIndex = edgeSearchList.IndexOf( hCurrentEdge );
			}
		}
	}
}
