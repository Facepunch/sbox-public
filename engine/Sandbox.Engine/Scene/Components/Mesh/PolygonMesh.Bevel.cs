using HalfEdgeMesh;

namespace Sandbox;

public partial class PolygonMesh
{
	public enum BevelEdgesMode
	{
		LeaveOriginalEdges,     // Leave the original edges, do not remove any edges
		RemoveOriginalEdges,    // Remove all of the original edges
		RemoveClosedEdges,      // Remove all the original edges that are closed, but leave open edges
	}

	public bool BevelEdges( IReadOnlyList<HalfEdgeHandle> edges,
		BevelEdgesMode edgeRemoveMode,
		int numSegments,
		float distance,
		float shape,
		List<HalfEdgeHandle> outNewOuterEdges = null,
		List<HalfEdgeHandle> outNewInnerEdges = null,
		List<FaceHandle> outNewFaces = null,
		List<FaceHandle> outFacesNeedingUVs = null )
	{
		const float flMaxParam = 15.0f / 16.0f;

		var nNumEdges = edges.Count;
		if ( nNumEdges <= 0 )
			return false;

		// Get all of the vertices connected to the edges and then all the faces connected to
		// those vertices, these are all of the faces that can be modified by the bevel.
		FindVerticesConnectedToEdges( edges, out var connectedVertices );

		FindFacesConnectedToVertices( connectedVertices, connectedVertices.Length, out var connectedFaces, out _ );

		// Compute the texture parameterization for each of the connected faces
		ComputeTextureParametersForFaces( connectedFaces, out var connectedFaceTextureParameters );

		// Initialize the uvs from the parameter, while this shouldn't be visually different it may
		// result is offset values, so to make the values consistent we need to compute them here before
		// saving them off in CBevelOriginalVertexData.
		UpdateUVsForFacesFromTextureParameters( connectedFaces, connectedFaceTextureParameters );

		// Build a table mapping faces to texture parameters
		var faceTextureParameterTable = new Dictionary<FaceHandle, FaceTextureParameters>( connectedFaces.Length * 2 );
		for ( var iFace = 0; iFace < connectedFaces.Length; ++iFace )
		{
			faceTextureParameterTable.Add( connectedFaces[iFace], connectedFaceTextureParameters[iFace] );
		}

		// Construct the list of half edges to be beveled
		var edgesToBevel = new List<HalfEdgeHandle>( nNumEdges * 2 );

		for ( var iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			var hEdge = edges[iEdge];
			Topology.GetHalfEdgesConnectedToFullEdge( hEdge, out var hHalfEdgeA, out var hHalfEdgeB );
			edgesToBevel.Add( hHalfEdgeA );
			edgesToBevel.Add( hHalfEdgeB );
		}

		var originalEdgesToBevel = new List<HalfEdgeHandle>( edgesToBevel );
		var originalEdgeFaceNormals = new Vector3[originalEdgesToBevel.Count];
		var originalEdgeMaxInsetDistances = new float[originalEdgesToBevel.Count];

		for ( var iEdge = 0; iEdge < originalEdgesToBevel.Count; ++iEdge )
		{
			var hOriginalEdge = originalEdgesToBevel[iEdge];

			originalEdgeFaceNormals[iEdge] = new Vector3( 0.0f, 0.0f, 1.0f );
			var hFace = Topology.GetFaceConnectedToHalfEdge( hOriginalEdge );
			if ( hFace != FaceHandle.Invalid )
			{
				ComputeFaceNormal( hFace, out originalEdgeFaceNormals[iEdge] );
			}

			originalEdgeMaxInsetDistances[iEdge] = BevelComputeMaximumInsetDistanceForEdge( hOriginalEdge, distance, flMaxParam );
		}

		// Store the positions and uvs for each of the vertices
		// connected to the edges before applying any modification.
		var orginalVertexData = new BevelOriginalVertexData();
		orginalVertexData.InitializePreBevel( this, edges );

		var newOuterEdges = new List<HalfEdgeHandle>( nNumEdges * 6 );
		var loopEdges = new List<HalfEdgeHandle>( nNumEdges );
		var loopVertices = new List<VertexHandle>( nNumEdges * 6 );

		while ( edgesToBevel.Count > 0 )
		{
			loopVertices.Clear();
			loopEdges.Clear();

			// Find the first closed edge in the list of remaining edges to bevel
			var hFirstBevelEdge = HalfEdgeHandle.Invalid;
			int nNumEdgesToBevel = edgesToBevel.Count;
			for ( int iEdge = 0; iEdge < nNumEdgesToBevel; ++iEdge )
			{
				var hEdge = edgesToBevel[iEdge];
				var hFace = Topology.GetFaceConnectedToHalfEdge( hEdge );
				if ( hFace != FaceHandle.Invalid )
				{
					hFirstBevelEdge = hEdge;
					break;
				}
			}

			// If no closed edges were found there is nothing left to do.
			if ( hFirstBevelEdge == HalfEdgeHandle.Invalid )
				break;

			var hStartEdge = HalfEdgeHandle.Invalid;
			var hFinalEdge = HalfEdgeHandle.Invalid;

			// Mark the first edge as being processed
			loopEdges.Add( hFirstBevelEdge );

			{
				var hPrevEdge = Topology.FindPreviousEdgeInFaceLoop( hFirstBevelEdge );
				var hPrevEdgeOpposite = Topology.GetOppositeHalfEdge( hPrevEdge );
				var hNextEdge = Topology.GetNextEdgeInFaceLoop( hFirstBevelEdge );

				var hVertexA = Topology.GetEndVertexConnectedToEdge( hPrevEdge );
				var hVertexB = Topology.GetEndVertexConnectedToEdge( hFirstBevelEdge );
				var hPrevVertex = Topology.GetEndVertexConnectedToEdge( hPrevEdgeOpposite );
				var hNextVertex = Topology.GetEndVertexConnectedToEdge( hNextEdge );

				// First add vertex to the previous edge in the face loop
				Assert.True( AddVertexToEdgeAtDistance( hVertexA, hPrevVertex, distance, flMaxParam, out var hNewPrevVertex ) );
				Assert.True( Topology.FindHalfEdgeConnectingVertices( hPrevVertex, hNewPrevVertex ) == hPrevEdge );

				// Next add a vertex to the next edge in the face loop
				Assert.True( AddVertexToEdgeAtDistance( hVertexB, hNextVertex, distance, flMaxParam, out var hNewNextVertex ) );
				Assert.True( Topology.FindHalfEdgeConnectingVertices( hNewNextVertex, hNextVertex ) == hNextEdge );


				// Add the new edge to the face
				var hEdgeToNewNext = Topology.FindHalfEdgeConnectingVertices( hVertexB, hNewNextVertex );
				Assert.True( Topology.AddEdgeToFace( hEdgeToNewNext, hPrevEdge, out _ ) );
				Assert.True( Topology.FindHalfEdgeConnectingVertices( hNewNextVertex, hNewPrevVertex ) != HalfEdgeHandle.Invalid );

				// If the previous edge is in the bevel set merge the vertices of the new previous edge
				if ( edgesToBevel.Contains( hPrevEdge ) )
				{
					Assert.True( AddVertexToEdgeAtDistance( hNewPrevVertex, hNewNextVertex, distance, flMaxParam, out var hNewVertex ) );
					MergeVertices( hVertexA, hNewPrevVertex, 0.0f, out hVertexA );
					hNewPrevVertex = hNewVertex;
				}

				// If the next edge is in the bevel set merge the vertices of the new next edge
				if ( edgesToBevel.Contains( hNextEdge ) )
				{
					Assert.True( AddVertexToEdgeAtDistance( hNewNextVertex, hNewPrevVertex, distance, flMaxParam, out var hNewVertex ) );
					MergeVertices( hVertexB, hNewNextVertex, 0.0f, out hVertexB );
					hNewNextVertex = hNewVertex;
				}

				// Add the two vertices to the loop
				loopVertices.Add( hNewPrevVertex );
				loopVertices.Add( hNewNextVertex );

				// Find the edge to start with
				hStartEdge = Topology.FindHalfEdgeConnectingVertices( hNewNextVertex, hVertexB );

				// Find the edge which will serve as the terminator
				hFinalEdge = Topology.FindHalfEdgeConnectingVertices( hVertexA, hNewPrevVertex );
			}

			// Walk all of the edges in the list of edges to be beveled which are connected to the first edge.
			var hCurrentEdge = hStartEdge;

			while ( hCurrentEdge != hFinalEdge )
			{
				var hStartVertex = Topology.GetEndVertexConnectedToEdge( hCurrentEdge );
				bool bLastEdgeInSet = edgesToBevel.Contains( hCurrentEdge );
				hCurrentEdge = Topology.GetNextEdgeInFaceLoop( hCurrentEdge );

				// Add the edges around the vertex until another edge from the set is found
				while ( hCurrentEdge != hFinalEdge )
				{
					var hFace = Topology.GetFaceConnectedToHalfEdge( hCurrentEdge );
					bool bEdgeInSet = edgesToBevel.Contains( hCurrentEdge );

					if ( bEdgeInSet && (!bLastEdgeInSet || (hFace == FaceHandle.Invalid)) )
						break;

					var hNextEdgeInVertexLoop = Topology.GetNextEdgeInVertexLoop( hCurrentEdge );
					var hEndVertex = Topology.GetEndVertexConnectedToEdge( hCurrentEdge );

					var hIncomingEdge = FindProceedingHalfEdgeEndingAtVertex( hCurrentEdge, loopVertices.LastOrDefault() );
					Assert.True( hIncomingEdge != HalfEdgeHandle.Invalid );

					Assert.True( AddVertexToEdgeAtDistance( hStartVertex, hEndVertex, distance, flMaxParam, out var hNewVertex ) );
					Assert.True( Topology.FindHalfEdgeConnectingVertices( hNewVertex, hEndVertex ) == hCurrentEdge );

					if ( hFace != FaceHandle.Invalid )
					{
						var hOutgoingEdge = Topology.FindHalfEdgeConnectingVertices( hStartVertex, hNewVertex );
						Assert.True( hOutgoingEdge != HalfEdgeHandle.Invalid );

						Assert.True( Topology.GetEndVertexConnectedToEdge( hIncomingEdge ) == loopVertices.LastOrDefault() );
						Assert.True( Topology.GetEndVertexConnectedToEdge( hOutgoingEdge ) == hNewVertex );

						Assert.True( Topology.AddEdgeToFace( hOutgoingEdge, hIncomingEdge, out _ ) );
						Assert.True( Topology.FindHalfEdgeConnectingVertices( loopVertices.LastOrDefault(), hNewVertex ) != HalfEdgeHandle.Invalid );
					}

					if ( bEdgeInSet )
					{
						Assert.True( AddVertexToEdgeAtDistance( hNewVertex, loopVertices.LastOrDefault(), distance, flMaxParam, out var hExtraVertex ) );
						MergeVertices( hStartVertex, hNewVertex, 0.0f, out _ );
						loopVertices.Add( hExtraVertex );
						break;
					}

					loopVertices.Add( hNewVertex );
					hCurrentEdge = hNextEdgeInVertexLoop;
					bLastEdgeInSet = false;
				}

				// Add the edge to the list of edges that were processed as a part of this loop.
				if ( hCurrentEdge != hFinalEdge )
				{
					loopEdges.Add( hCurrentEdge );
				}
			}

			// Complete the loop
			if ( loopVertices.Count > 2 )
			{
				Assert.True( Topology.GetEndVertexConnectedToEdge( hCurrentEdge ) == loopVertices[0] );

				var hIncomingEdge = FindProceedingHalfEdgeEndingAtVertex( hCurrentEdge, loopVertices.LastOrDefault() );
				Assert.True( hIncomingEdge != HalfEdgeHandle.Invalid );

				bool bAddedEdge = Topology.AddEdgeToFace( hCurrentEdge, hIncomingEdge, out _ );
				Assert.True( (!bAddedEdge) || Topology.FindHalfEdgeConnectingVertices( loopVertices[0], loopVertices.LastOrDefault() ) != HalfEdgeHandle.Invalid );
			}

			// Remove the edges from the set of edges which still need to be beveled
			int nNumEdgesBeveledInLoop = loopEdges.Count;
			for ( int iEdge = 0; iEdge < nNumEdgesBeveledInLoop; ++iEdge )
			{
				edgesToBevel.Remove( loopEdges[iEdge] );
			}

			// Update the total list of new edges
			int nNumLoopVertices = loopVertices.Count;
			for ( int iVertex = 0, iPrevVertex = nNumLoopVertices - 1; iVertex < nNumLoopVertices; iPrevVertex = iVertex++ )
			{
				var hHalfEdge = Topology.FindHalfEdgeConnectingVertices( loopVertices[iVertex], loopVertices[iPrevVertex] );
				if ( hHalfEdge != HalfEdgeHandle.Invalid )
				{
					newOuterEdges.Add( hHalfEdge );
				}
			}
		}

		// Clear any sub-division on the faces connected to the bevel edges
		{
			Topology.FindFacesConnectedToHalfEdges( newOuterEdges, newOuterEdges.Count, out var newBevelFaces, out _ );
			foreach ( var hFace in newBevelFaces )
			{
				//SetFaceSubdivisionLevel( hFace, POLYMESH_SUBDIVISION_LEVEL_0 );
			}
		}

		// Adjust the offset of the new edges to fix up issues where the default position isn't the desired position
		FixBevelInsetPositions( originalEdgesToBevel, originalEdgeFaceNormals, originalEdgeMaxInsetDistances, distance );

		// Recompute the UVs of the modified faces from the texture parameters that were saved off before
		// the bevel was applied. This ensures that if uvs were distorted due to either vertex merging in
		// the actual bevel or position adjustment of the vertices in FixBevelInsetPositions() that they
		// will be recomputed using the original parameterization of the face.
		{
			// Get all of the full edges for each of the half edges of the outer loop created by the bevel
			var outerEdgesFull = new List<HalfEdgeHandle>( newOuterEdges.Count );

			foreach ( var hHalfEdge in newOuterEdges )
			{
				var hEdge = Topology.GetFullEdgeForHalfEdge( hHalfEdge );
				outerEdgesFull.Add( hEdge );
			}

			// Update the face -> texture parameter table adding all of the faces connected to the outer bevel 
			// edges so that each of the new faces gets the texture parameters of the original face it came from.
			while ( UpdateFaceTextureParameterTable( outerEdgesFull, faceTextureParameterTable ) ) ;

			// Re-compute the UVs for the original faces from their uvs
			UpdateUVsForFacesFromTextureParameters( faceTextureParameterTable );
		}

		// List of all of the new faces which were generate by the bevel
		List<FaceHandle> newFaces;

		// If requested remove all of the original edges		
		if ( edgeRemoveMode != BevelEdgesMode.LeaveOriginalEdges )
		{
			// The original vertex data was initialized before the bevel was applied. The bevel may have
			// split the faces it referenced. Update it to ensure that the faces it references are the
			// faces on the outside of the bevel edge loop and not the ones inside the bevel edge loop
			// that will be further modified.
			{
				var interiorFaces = new List<FaceHandle>( newOuterEdges.Count );
				var exteriorFaces = new List<FaceHandle>( newOuterEdges.Count );
				foreach ( var hOuterEdge in newOuterEdges )
				{
					interiorFaces.Add( Topology.GetFaceConnectedToHalfEdge( hOuterEdge ) );
					exteriorFaces.Add( Topology.GetFaceConnectedToHalfEdge( Topology.GetOppositeHalfEdge( hOuterEdge ) ) );
				}

				orginalVertexData.UpdatePostBevel( this, edges, interiorFaces, exteriorFaces );
			}

			var originalEdgesToRemove = new List<HalfEdgeHandle>();

			if ( edgeRemoveMode == BevelEdgesMode.RemoveClosedEdges )
			{
				// Build a list of all of the closed edges
				originalEdgesToRemove.EnsureCapacity( nNumEdges );
				for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
				{
					if ( Topology.IsFullEdgeOpen( edges[iEdge] ) == false )
					{
						originalEdgesToRemove.Add( edges[iEdge] );
					}
				}
			}
			else
			{
				originalEdgesToRemove.AddRange( edges );
			}

			// Remove the original edges 
			BevelEdgesRemoveOriginalEdges( originalEdgesToRemove, newOuterEdges, orginalVertexData, out var edgeFaceInfo, out var vertexFaceInfo );

			// Add edges to the faces generated by the bevel in order to create the requested number of segments
			CreateSegmentedBevelFaces( edgeFaceInfo, vertexFaceInfo, numSegments, shape, out newFaces, outFacesNeedingUVs );

			// Remove all of the faces that are actually part of the bevel from the face texture parameter 
			// table, leaving only the faces neighboring the beveled faces, then update the uvs for those 
			// faces again. This is needed to fix up the uvs on the neighboring faces when the bevel adds
			// vertices to them and then moves those vertices depending on the shape factor.
			foreach ( var hFace in newFaces )
			{
				faceTextureParameterTable.Remove( hFace );
			}
			UpdateUVsForFacesFromTextureParameters( faceTextureParameterTable );

			// The list of edges is no longer complete since we added cuts along some of the outer edges
			newOuterEdges.Clear();
		}
		else
		{
			// Get all of the faces connected to the new edges
			Topology.FindFacesConnectedToHalfEdges( newOuterEdges, newOuterEdges.Count, out newFaces, out _ );
			if ( outFacesNeedingUVs is not null )
			{
				outFacesNeedingUVs.Clear();
				outFacesNeedingUVs.AddRange( newFaces );
			}
		}

		GetEdgesConnectedToFaces( newFaces, outNewInnerEdges, outNewOuterEdges );

		if ( outNewFaces is not null )
		{
			outNewFaces.Clear();
			outNewFaces.AddRange( newFaces );
		}

		IsDirty = true;

		return true;
	}

	private bool FixBevelInsetPositions( List<HalfEdgeHandle> beveledEdges, IReadOnlyList<Vector3> originalFaceNormals, IReadOnlyList<float> edgeMaxInsetDistances, float insetDistance )
	{
		var nNumBeveledEdges = beveledEdges.Count;
		var beveledEdgesNext = new int[nNumBeveledEdges];

		for ( var iEdge = 0; iEdge < nNumBeveledEdges; ++iEdge )
		{
			var hBeveledEdge = beveledEdges[iEdge];
			beveledEdgesNext[iEdge] = -1;

			// If there is no face connected to the half edge, skip it since
			// that means the bevel operation would not have modified it.
			var hFace = Topology.GetFaceConnectedToHalfEdge( hBeveledEdge );
			if ( hFace == FaceHandle.Invalid )
				continue;

			// After the bevel operation is complete, each of the beveled edges should now belong to a 
			// quad, we want to find the three edges that along with the beveled edge make up that quad. 
			var hNextEdgeInFace = Topology.GetNextEdgeInFaceLoop( hBeveledEdge );
			var hInsetEdge = Topology.GetNextEdgeInFaceLoop( hNextEdgeInFace );
			var hPrevEdgeInFace = Topology.GetNextEdgeInFaceLoop( hInsetEdge );

			// The next edge in the face following the previous edge should be the beveled edge, if 
			// this is not the case the bevel did not produce a quad for this edge (it might have been 
			// a triangle for an edge at the end of a set of edges), so we don't want to try to fix its
			// position.
			if ( Topology.GetNextEdgeInFaceLoop( hPrevEdgeInFace ) != hBeveledEdge )
				continue;

			var hNextOppositeEdge = Topology.GetOppositeHalfEdge( hNextEdgeInFace );
			var hNextBeveledEdge = Topology.GetNextEdgeInFaceLoop( hNextOppositeEdge );

			// If the next edge is the opposite edge the edge is at the end of connected set of 
			// edges being beveled, the position of the shared vertex should not be modified.
			if ( Topology.GetOppositeHalfEdge( hBeveledEdge ) == hNextBeveledEdge )
				continue;

			if ( Topology.GetFaceConnectedToHalfEdge( hNextBeveledEdge ) != FaceHandle.Invalid )
			{
				var nextBeveledEdgeIndex = beveledEdges.IndexOf( hNextBeveledEdge );
				if ( nextBeveledEdgeIndex != -1 )
				{
					beveledEdgesNext[iEdge] = nextBeveledEdgeIndex;
				}
			}
		}

		FixBevelInsetPositions3D( beveledEdges, beveledEdgesNext, originalFaceNormals, edgeMaxInsetDistances, insetDistance );

		return true;
	}

	private void FixBevelInsetPositions3D( IReadOnlyList<HalfEdgeHandle> beveledEdges, IReadOnlyList<int> beveledEdgesNext, IReadOnlyList<Vector3> orginalFaceNormals, IReadOnlyList<float> edgeMaxInsetDistances, float flInsetDistance )
	{
		var flFaceNormalThreshold = MathF.Cos( 1.0f.DegreeToRadian() );
		var flLineIntersectThreshold = MathF.Cos( 5.0f.DegreeToRadian() );

		var nNumBeveledEdges = beveledEdges.Count;
		for ( var iEdge = 0; iEdge < nNumBeveledEdges; ++iEdge )
		{
			int nNextBeveledEdgeIndex = beveledEdgesNext[iEdge];
			if ( nNextBeveledEdgeIndex < 0 )
				continue;

			var hBeveledEdge = beveledEdges[iEdge];
			var hNextBeveledEdge = beveledEdges[nNextBeveledEdgeIndex];
			Assert.True( hBeveledEdge != HalfEdgeHandle.Invalid );
			Assert.True( hNextBeveledEdge != HalfEdgeHandle.Invalid );

			// Get the vertices at the end of the beveled edge. 
			// VertexA is the start of the edge and vertexB is the end.
			Topology.GetVerticesConnectedToHalfEdge( hBeveledEdge, out var hVertexA, out var hVertexB );
			Topology.GetVerticesConnectedToHalfEdge( hNextBeveledEdge, out var hVertexC, out var hVertexD );
			var vPositionA = GetVertexPosition( hVertexA );
			var vPositionB = GetVertexPosition( hVertexB );
			var vPositionC = GetVertexPosition( hVertexC );
			var vPositionD = GetVertexPosition( hVertexD );
			Assert.True( hVertexC == hVertexB );

			// Compute the inset vertex positions based on the first edge
			var vNormal = orginalFaceNormals[iEdge];
			Vector3 vInsetPositionA;
			Vector3 vInsetPositionB;
			{
				var flEdgeInsetDistance = MathF.Min( flInsetDistance, edgeMaxInsetDistances[iEdge] );
				var vAB = vPositionB - vPositionA;
				var vInsetDirAB = Vector3.Cross( vNormal, vAB ).Normal;
				vInsetPositionA = vPositionA + vInsetDirAB * flEdgeInsetDistance;
				vInsetPositionB = vPositionB + vInsetDirAB * flEdgeInsetDistance;
			}

			// Compute the inset vertex positions based on the next edge
			var vNextNormal = orginalFaceNormals[nNextBeveledEdgeIndex];
			Vector3 vInsetPositionD;
			Vector3 vInsetPositionC;
			{
				var flEdgeInsetDistance = MathF.Min( flInsetDistance, edgeMaxInsetDistances[nNextBeveledEdgeIndex] );
				var vCD = vPositionD - vPositionC;
				var vInsetDirCD = Vector3.Cross( vNextNormal, vCD ).Normal;
				vInsetPositionC = vPositionC + vInsetDirCD * flEdgeInsetDistance;
				vInsetPositionD = vPositionD + vInsetDirCD * flEdgeInsetDistance;
			}

			// We really only need to re-compute the position in the case where the two edges are from
			// the same face, but the result should be valid as long as both faces we essentially
			// co-planar, however if they faces are not co-planar the intersections may not be 
			// precise so prefer to use leave the original positions.
			if ( vNormal.Dot( vNextNormal ) < flFaceNormalThreshold )
				continue;

			var hNextEdgeInFace = Topology.GetNextEdgeInFaceLoop( hBeveledEdge );
			var hInsetVertex = Topology.GetEndVertexConnectedToEdge( hNextEdgeInFace );

			// Find the intersection of the lines going through the inset vertex positions of each edge
			var vABInsertDir = (vInsetPositionB - vInsetPositionA).Normal;
			var vCDInsertDir = (vInsetPositionD - vInsetPositionC).Normal;
			var flInsetLinesDot = MathF.Abs( vABInsertDir.Dot( vCDInsertDir ) );

			if ( (flInsetLinesDot < flLineIntersectThreshold) &&
				 CalcLineToLineIntersectionSegment( vInsetPositionA, vInsetPositionB, vInsetPositionC, vInsetPositionD, out var vPointOnAB, out var vPointOnCD ) )
			{
				var vIntersectionPoint = (vPointOnAB + vPointOnCD) * 0.5f;
				SetVertexPosition( hInsetVertex, vIntersectionPoint );
			}
			else
			{
				SetVertexPosition( hInsetVertex, vInsetPositionB );
			}
		}
	}

	private void BevelEdgesRemoveOriginalEdges( IReadOnlyList<HalfEdgeHandle> pEdges,
												IReadOnlyList<HalfEdgeHandle> totalNewEdges,
												BevelOriginalVertexData originalVertexData,
												out List<BevelEdgeFaceInfo> pOutEdgeFaceInfo,
												out List<BevelVertexFaceInfo> pOutVertexFaceInfo )
	{
		FindVerticesConnectedToEdges( pEdges, out var interiorVertices );

		var nNumEdges = pEdges.Count;

		pOutEdgeFaceInfo = new List<BevelEdgeFaceInfo>( nNumEdges );
		pOutVertexFaceInfo = new List<BevelVertexFaceInfo>( nNumEdges );

		var interiorFaces = new List<FaceHandle>( nNumEdges );

		for ( var iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			var hEdge = pEdges[iEdge];

			Topology.GetFacesConnectedToFullEdge( hEdge, out var hFaceA, out var hFaceB );

			var verticesA = new VertexHandle[4];
			var verticesDataA = new BevelVertexData[4];
			var bQuadValidA = GetVertexDataForQuad( originalVertexData, hFaceA, hEdge, verticesA, verticesDataA );

			var verticesB = new VertexHandle[4];
			var verticesDataB = new BevelVertexData[4];
			var bQuadValidB = GetVertexDataForQuad( originalVertexData, hFaceB, hEdge, verticesB, verticesDataB );

			DissolveEdge( hEdge, out var hNewFace );
			if ( (hNewFace != FaceHandle.Invalid) && bQuadValidA && bQuadValidB )
			{
				pOutEdgeFaceInfo.Add( new BevelEdgeFaceInfo
				{
					Face = hNewFace,
					Vertices = new List<VertexHandle> { verticesA[0], verticesA[1], verticesB[0], verticesB[1] },
					QuadA = verticesDataA.ToList(),
					QuadB = verticesDataB.ToList()
				} );

				interiorFaces.Add( hNewFace );
			}
		}

		var nNumVertices = interiorVertices.Length;
		for ( var iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			var hCenterVertex = interiorVertices[iVertex];
			if ( IsVertexInMesh( hCenterVertex ) == false )
				continue;

			// Get the position of the vertex and list of faces the were connected to the vertex before removing it
			GetFacesConnectedToVertex( hCenterVertex, out var facesConnectedToVertex );
			var vCenterVertexPosition = GetVertexPosition( hCenterVertex );

			RemoveVertex( hCenterVertex, true );

			// Find the open edge loop that resulted from removing the vertex by looking at the faces
			// that were connected to the vertex that were generated by the bevel.
			var hOpenEdgeLoopStart = BevelFindOpenEdgeLoopFromVertexRemoval( totalNewEdges, interiorFaces, facesConnectedToVertex );
			if ( hOpenEdgeLoopStart == HalfEdgeHandle.Invalid )
				continue;

			// Create a face to fill the open edge loop (hole) left by removing the vertex
			AddFace( Topology.GetFullEdgeForHalfEdge( hOpenEdgeLoopStart ), out var hFaceForVertex );
			if ( hFaceForVertex == FaceHandle.Invalid )
				continue;

			var pFaceInfo = new BevelVertexFaceInfo();
			pFaceInfo.Face = hFaceForVertex;
			pFaceInfo.IsEndTriangle = false;

			GetFaceVerticesConnectedToFace( hFaceForVertex, out var faceVertices );
			var nNumVerticesInFace = faceVertices.Length;
			pFaceInfo.Vertices = new List<VertexHandle>( nNumVerticesInFace );
			pFaceInfo.VertexData = new List<BevelVertexQuadData>( nNumVerticesInFace );

			// Store the position and uv for each of the vertices of the newly generated face. The
			// uvs will actually come from the outside faces (the ones pre-existing the bevel). 
			for ( int iFaceVertex = 0; iFaceVertex < nNumVerticesInFace; ++iFaceVertex )
			{
				var hFaceVertex = faceVertices[iFaceVertex];
				var hVertexInFace = GetVertexConnectedToFaceVertex( hFaceVertex );

				pFaceInfo.Vertices.Add( hVertexInFace );

				var vertexData = new BevelVertexQuadData();
				var outerVertex = new BevelVertexData
				{
					Position = GetVertexPosition( hVertexInFace ),
					UV = Vector2.Zero,
					OutgoingUV = Vector2.Zero
				};

				BevelFindUVSourceVerticesForCorner( hFaceVertex, interiorFaces, out var hUVSourceFaceVertexIncoming, out var hUVSourceFaceVertexOutgoing );

				if ( hUVSourceFaceVertexIncoming != HalfEdgeHandle.Invalid )
				{
					outerVertex.UV = TextureCoord[hUVSourceFaceVertexIncoming];
				}

				if ( hUVSourceFaceVertexOutgoing != HalfEdgeHandle.Invalid )
				{
					outerVertex.OutgoingUV = TextureCoord[hUVSourceFaceVertexOutgoing];
				}

				var hOuterFace = Topology.GetFaceConnectedToHalfEdge( hUVSourceFaceVertexIncoming );
				var centerVertex = new BevelVertexData
				{
					Position = vCenterVertexPosition,
					UV = originalVertexData.GetUVForVertexAndFace( hCenterVertex, hOuterFace )
				};

				vertexData.OuterVertex = outerVertex;
				vertexData.CenterVertex = centerVertex;
				pFaceInfo.VertexData.Add( vertexData );
			}

			// Compute the target positions for a vertex at the midpoint of each edge
			for ( var iFaceVertex = 0; iFaceVertex < nNumVerticesInFace; ++iFaceVertex )
			{
				var iPrevFaceVertex = (iFaceVertex + nNumVerticesInFace - 1) % nNumVerticesInFace;
				var iNextFaceVertex = (iFaceVertex + 1) % nNumVerticesInFace;

				var hCurrentVertex = pFaceInfo.Vertices[iFaceVertex];
				var hIncomingEdge = faceVertices[iFaceVertex];
				var hOutgoingEdge = faceVertices[iNextFaceVertex];

				// Get the vertex positions of the edge, then compute the center
				var vertexDataA = pFaceInfo.VertexData[iPrevFaceVertex].OuterVertex;
				var vertexDataB = pFaceInfo.VertexData[iFaceVertex].OuterVertex;
				var vertexDataC = pFaceInfo.VertexData[iNextFaceVertex].OuterVertex;

				var vertexData = pFaceInfo.VertexData[iFaceVertex];
				var prevMidpointVertex = vertexData.PrevMidpointVertex;
				prevMidpointVertex.Position = (vertexDataA.Position + vertexDataB.Position) * 0.5f;
				prevMidpointVertex.UV = (vertexDataA.UV + vertexDataB.UV) * 0.5f;

				var nextMidpointVertex = vertexData.NextMidpointVertex;
				nextMidpointVertex.Position = (vertexDataB.Position + vertexDataC.Position) * 0.5f;
				nextMidpointVertex.UV = (vertexDataB.UV + vertexDataC.UV) * 0.5f;

				// Update the mid-point vertex position and uv targets to be on the original edge
				UpdateMidpointEdgeVertexData( hFaceForVertex, hCurrentVertex, Topology.GetFullEdgeForHalfEdge( hIncomingEdge ), interiorFaces, vertexDataA.Position, vertexDataB.Position, pOutEdgeFaceInfo, ref prevMidpointVertex );
				UpdateMidpointEdgeVertexData( hFaceForVertex, hCurrentVertex, Topology.GetFullEdgeForHalfEdge( hOutgoingEdge ), interiorFaces, vertexDataB.Position, vertexDataC.Position, pOutEdgeFaceInfo, ref nextMidpointVertex );

				vertexData.PrevMidpointVertex = prevMidpointVertex;
				vertexData.NextMidpointVertex = nextMidpointVertex;

				pFaceInfo.VertexData[iFaceVertex] = vertexData;
			}

			// If this face is a triangle, see if it is only connected to a single face generated by the 
			// bevel. If so we will triangulate it instead of sub-dividing it.
			if ( facesConnectedToVertex.Count == 3 )
			{
				var hInteriorFace = FaceHandle.Invalid;

				for ( var i = 0; i < 3; ++i )
				{
					if ( interiorFaces.Contains( facesConnectedToVertex[i] ) )
					{
						if ( hInteriorFace == FaceHandle.Invalid )
						{
							hInteriorFace = facesConnectedToVertex[i];
						}
						else
						{
							hInteriorFace = FaceHandle.Invalid;
							break;
						}
					}
				}

				if ( hInteriorFace != FaceHandle.Invalid )
				{
					var hEndEdge = FindEdgeConnectingFaces( pFaceInfo.Face, hInteriorFace );

					pFaceInfo.IsEndTriangle = true;
					GetVerticesConnectedToEdge( hEndEdge, pFaceInfo.Face, out pFaceInfo.EndTriangleVertexA, out pFaceInfo.EndTriangleVertexB );
				}
			}

			pOutVertexFaceInfo.Add( pFaceInfo );
		}
	}

	private void UpdateMidpointEdgeVertexData( FaceHandle hFace,
								   VertexHandle hVertex, HalfEdgeHandle hEdge,
								   List<FaceHandle> interiorFaces,
								   Vector3 vPositionA, Vector3 vPositionB,
								   List<BevelEdgeFaceInfo> pEdgeFaceInfo,
								   ref BevelVertexData pTargetMidpointVertex )
	{
		// If the opposite face is one that was generated from an edge, move
		// the target position to the closest point on the original edge.
		var hOppositeFace = GetOppositeFaceConnectedToEdge( hEdge, hFace );
		var nEdgeFaceIndex = interiorFaces.IndexOf( hOppositeFace );
		if ( nEdgeFaceIndex == -1 )
			return;

		// Get the neighboring edge face. Note that in addition to updating the midpoint of the vertex face
		// we will also update the corresponding target vertex data in the edge face, since we have identified
		// that the vertex face and edge face are connected by an edge and we want them to have the same
		// vertex position and uv coordinate so that there isn't a discontinuity across the edge.
		var edgeFaceInfo = pEdgeFaceInfo[nEdgeFaceIndex];

		// Determine which of the two quads we need to use based on which ones include the specified vertex
		// While the positions of vertices along the center edge of the quads are the same, the uvs are not.
		if ( (edgeFaceInfo.Vertices[0] == hVertex) || (edgeFaceInfo.Vertices[1] == hVertex) )
		{
			if ( edgeFaceInfo.QuadA.Count == 4 )
			{
				ComputeVertexDataOnTargetEdge( vPositionA, vPositionB, edgeFaceInfo.QuadA[2], edgeFaceInfo.QuadA[3], out var vertexOnEdge );

				pTargetMidpointVertex = vertexOnEdge;

				if ( edgeFaceInfo.Vertices[0] == hVertex )
				{
					edgeFaceInfo.QuadA[3] = vertexOnEdge;
				}
				else if ( edgeFaceInfo.Vertices[1] == hVertex )
				{
					edgeFaceInfo.QuadA[2] = vertexOnEdge;
				}
			}
		}
		else if ( (edgeFaceInfo.Vertices[2] == hVertex) || (edgeFaceInfo.Vertices[3] == hVertex) )
		{
			if ( edgeFaceInfo.QuadB.Count == 4 )
			{
				ComputeVertexDataOnTargetEdge( vPositionA, vPositionB, edgeFaceInfo.QuadB[2], edgeFaceInfo.QuadB[3], out var vertexOnEdge );

				pTargetMidpointVertex = vertexOnEdge;

				if ( edgeFaceInfo.Vertices[2] == hVertex )
				{
					edgeFaceInfo.QuadB[3] = vertexOnEdge;
				}
				else if ( edgeFaceInfo.Vertices[3] == hVertex )
				{
					edgeFaceInfo.QuadB[2] = vertexOnEdge;
				}
			}
		}
	}

	private void BevelFindUVSourceVerticesForCorner( HalfEdgeHandle hFaceVertex, IReadOnlyList<FaceHandle> interiorFaces, out HalfEdgeHandle pOutIncomingUVFaceVertex, out HalfEdgeHandle pOutOutgoingUVFaceVertex )
	{
		pOutIncomingUVFaceVertex = HalfEdgeHandle.Invalid;
		pOutOutgoingUVFaceVertex = HalfEdgeHandle.Invalid;

		var hVertex = GetVertexConnectedToFaceVertex( hFaceVertex );

		{
			var hPrevFaceVertex = Topology.FindPreviousEdgeInFaceLoop( hFaceVertex );
			var hPrevVertex = GetVertexConnectedToFaceVertex( hPrevFaceVertex );
			var hOppositeFacePrev = Topology.FindFaceWithEdgeConnectingVertices( hVertex, hPrevVertex );

			if ( (hOppositeFacePrev != FaceHandle.Invalid) && (interiorFaces.Contains( hOppositeFacePrev ) == false) )
			{
				pOutIncomingUVFaceVertex = FindFaceVertexConnectedToVertex( hVertex, hOppositeFacePrev );
			}
		}

		{
			var hNextFaceVertex = GetNextVertexInFace( hFaceVertex );
			var hNextVertex = GetVertexConnectedToFaceVertex( hNextFaceVertex );
			var hOppositeFaceNext = Topology.FindFaceWithEdgeConnectingVertices( hNextVertex, hVertex );

			if ( (hOppositeFaceNext != FaceHandle.Invalid) && (interiorFaces.Contains( hOppositeFaceNext ) == false) )
			{
				pOutOutgoingUVFaceVertex = FindFaceVertexConnectedToVertex( hVertex, hOppositeFaceNext );
			}
		}

		if ( pOutIncomingUVFaceVertex == HalfEdgeHandle.Invalid )
		{
			pOutIncomingUVFaceVertex = pOutOutgoingUVFaceVertex;
		}

		if ( pOutOutgoingUVFaceVertex == HalfEdgeHandle.Invalid )
		{
			pOutOutgoingUVFaceVertex = pOutIncomingUVFaceVertex;
		}

		// If both the incoming and outgoing edges are attached to interior faces, we will need to look
		// for another face connected to the vertex to get the uvs from.
		if ( pOutIncomingUVFaceVertex == HalfEdgeHandle.Invalid )
		{
			Topology.GetIncomingHalfEdgesConnectedToVertex( hVertex, out var faceVerticesConnectedToVertex );

			// Find the first face which is not part one of the interior faces, there 
			// may be multiple, if so it doesn't matter which one is used.
			foreach ( var hConnectedFaceVertex in faceVerticesConnectedToVertex )
			{
				if ( hConnectedFaceVertex == hFaceVertex )
					continue;

				var hConnectedFace = Topology.GetFaceConnectedToHalfEdge( hConnectedFaceVertex );

				if ( interiorFaces.Contains( hConnectedFace ) == false )
				{
					pOutIncomingUVFaceVertex = hConnectedFaceVertex;
					pOutOutgoingUVFaceVertex = hConnectedFaceVertex;
				}
			}
		}
	}

	private void ComputeVertexDataOnTargetEdge( Vector3 basePosA, Vector3 basePosB, BevelVertexData edgeA, BevelVertexData edgeB, out BevelVertexData outVertex )
	{
		CalcClosestPointOnLineSegment( basePosA, edgeA.Position, edgeB.Position, out var pointA, out var tA );
		CalcClosestPointOnLineSegment( basePosB, edgeA.Position, edgeB.Position, out var pointB, out var tB );

		var closerToA = MathF.Abs( tA - 0.5f ) < MathF.Abs( tB - 0.5f );
		outVertex = new BevelVertexData
		{
			Position = closerToA ? pointA : pointB,
			UV = edgeA.UV.LerpTo( edgeB.UV, closerToA ? tA : tB )
		};
	}

	private HalfEdgeHandle BevelFindOpenEdgeLoopFromVertexRemoval( IReadOnlyList<HalfEdgeHandle> totalNewEdges, IReadOnlyList<FaceHandle> interiorFaces, IReadOnlyList<FaceHandle> pFacesConnectedToVertex )
	{
		for ( var iFace = 0; iFace < pFacesConnectedToVertex.Count; ++iFace )
		{
			var hFace = pFacesConnectedToVertex[iFace];

			// Skip any faces that weren't generated by the bevel operation
			if ( !interiorFaces.Contains( hFace ) )
				continue;

			var hFaceStartEdge = Topology.GetFirstEdgeInFaceLoop( hFace );
			var hFaceCurrentEdge = hFaceStartEdge;

			do
			{
				var hStartOpenEdge = Topology.GetOppositeHalfEdge( hFaceCurrentEdge );
				hFaceCurrentEdge = Topology.GetNextEdgeInFaceLoop( hFaceCurrentEdge );
				if ( Topology.GetFaceConnectedToHalfEdge( hStartOpenEdge ) != FaceHandle.Invalid )
					continue;

				// All of the edges in the loop must belong to interior 
				// faces or to the set of edges created by the bevel operation.
				var hCurrentOpenEdge = hStartOpenEdge;
				do
				{
					if ( !totalNewEdges.Contains( hCurrentOpenEdge ) )
					{
						var hOppositeEdge = Topology.GetOppositeHalfEdge( hCurrentOpenEdge );
						var hOppositeFace = Topology.GetFaceConnectedToHalfEdge( hOppositeEdge );
						if ( !interiorFaces.Contains( hOppositeFace ) )
							break;
					}
					hCurrentOpenEdge = Topology.GetNextEdgeInFaceLoop( hCurrentOpenEdge );
				}
				while ( hCurrentOpenEdge != hStartOpenEdge );

				// If all of the edges belonged to the bevel, return the edge loop
				if ( hCurrentOpenEdge == hStartOpenEdge )
					return hStartOpenEdge;
			}
			while ( hFaceCurrentEdge != hFaceStartEdge );
		}

		return HalfEdgeHandle.Invalid;
	}

	private bool GetVertexDataForQuad( BevelOriginalVertexData originalVertexData,
									   FaceHandle hFace,
									   HalfEdgeHandle hCenterEdge,
									   VertexHandle[] pOutVertices,
									   BevelVertexData[] pOutVertexData )
	{
		if ( Topology.ComputeNumEdgesInFace( hFace ) != 4 )
			return false;

		var hOuterEdge = FindOppositeEdgeInFace( hFace, hCenterEdge );
		if ( hOuterEdge == HalfEdgeHandle.Invalid )
			return false;

		GetVerticesConnectedToEdge( hOuterEdge, hFace, out pOutVertices[0], out pOutVertices[1] );
		GetVerticesConnectedToEdge( hCenterEdge, hFace, out pOutVertices[2], out pOutVertices[3] );

		for ( var i = 0; i < 4; ++i )
		{
			var v = pOutVertexData[i];
			v.Position = GetVertexPosition( pOutVertices[i] );
			v.UV = Vector2.Zero;
			pOutVertexData[i] = v;
		}

		{
			// For the vertices on the outer edge, if possible use the opposite face to get the uvs since
			// the face we are operating on is one generated by the bevel and doesn't always have good uvs
			var hOppositeFace = GetOppositeFaceConnectedToEdge( hOuterEdge, hFace );

			var hFaceForOuterUVs = (hOppositeFace != FaceHandle.Invalid) ? hOppositeFace : hFace;
			var hFaceVertex0 = FindFaceVertexConnectedToVertex( pOutVertices[0], hFaceForOuterUVs );
			var hFaceVertex1 = FindFaceVertexConnectedToVertex( pOutVertices[1], hFaceForOuterUVs );

			var v = pOutVertexData[0];
			v.UV = TextureCoord[hFaceVertex0];
			pOutVertexData[0] = v;

			v = pOutVertexData[1];
			v.UV = TextureCoord[hFaceVertex1];
			pOutVertexData[1] = v;

			// For vertices on the original edge, look up the uvs in the table of original uvs using the vertex
			// and the opposite face, which should be one of the faces originally connected to the vertex.
			v = pOutVertexData[2];
			v.UV = originalVertexData.GetUVForVertexAndFace( pOutVertices[2], hOppositeFace );
			pOutVertexData[2] = v;

			v = pOutVertexData[3];
			v.UV = originalVertexData.GetUVForVertexAndFace( pOutVertices[3], hOppositeFace );
			pOutVertexData[3] = v;
		}

		return true;
	}

	private void CreateSegmentedBevelFaces( IReadOnlyList<BevelEdgeFaceInfo> edgeFaces,
											IReadOnlyList<BevelVertexFaceInfo> vertexFaces,
											int nNumSegments, float flShape,
											out List<FaceHandle> pOutNewFaces,
											List<FaceHandle> pOutFacesNeedingUVs )
	{
		pOutNewFaces = new List<FaceHandle>();
		pOutFacesNeedingUVs?.Clear();

		if ( nNumSegments < 2 )
		{
			// No subdivision, return the initial faces
			pOutNewFaces.EnsureCapacity( vertexFaces.Count + edgeFaces.Count );
			foreach ( var edgeFace in edgeFaces )
			{
				pOutNewFaces.Add( edgeFace.Face );
			}

			foreach ( var vertexFace in vertexFaces )
			{
				pOutNewFaces.Add( vertexFace.Face );
			}

			pOutFacesNeedingUVs?.AddRange( pOutNewFaces );

			return;
		}

		bool bRemoveCenterEdge = false;
		if ( nNumSegments % 2 != 0 )
		{
			nNumSegments += 1;
			bRemoveCenterEdge = true;
		}

		var nNumSubFaceCuts = (nNumSegments - 1) / 2;

		var centerEdges = new List<HalfEdgeHandle>();

		// Perform a single level of sub-division on the faces generated from vertices 
		// so that we get quads, then add edges to each of those quads.
		var vertexSubFaceQuads = new List<BevelQuadFace>();
		{
			var nNumVertexFaces = vertexFaces.Count;
			vertexSubFaceQuads.EnsureCapacity( nNumVertexFaces * 4 );

			for ( int iFace = 0; iFace < nNumVertexFaces; ++iFace )
			{
				var vertexFaceInfo = vertexFaces[iFace];
				if ( vertexFaceInfo.IsEndTriangle )
					continue;

				var hCenterVertex = SubdivideFace( vertexFaceInfo.Face );

				GetFacesConnectedToVertex( hCenterVertex, out var subFaces );
				var nNumSubFaces = subFaces.Count;

				for ( int iSubFace = 0; iSubFace < nNumSubFaces; ++iSubFace )
				{
					var hSubFace = subFaces[iSubFace];

					// Get the vertices connected to the sub-face such that 
					// the center vertex is the first vertex in the list.
					var hFirstFaceFaceVertex = FindFaceVertexConnectedToVertex( hCenterVertex, hSubFace );
					var hCurrentFaceVertex = hFirstFaceFaceVertex;
					var subFaceVertices = new List<VertexHandle>();

					do
					{
						subFaceVertices.Add( GetVertexConnectedToFaceVertex( hCurrentFaceVertex ) );
						hCurrentFaceVertex = GetNextVertexInFace( hCurrentFaceVertex );
					}
					while ( hCurrentFaceVertex != hFirstFaceFaceVertex );

					// Subdivided faces should always be quads
					if ( subFaceVertices.Count == 4 )
					{
						var hOuterVertex = subFaceVertices[2];
						var hPrevMidpointVertex = subFaceVertices[1];
						var hNextMidpointVertex = subFaceVertices[3];

						var vOuterBasePosition = GetVertexPosition( hOuterVertex );
						var vPrevBasePosition = GetVertexPosition( hPrevMidpointVertex );
						var vCenterBasePosition = GetVertexPosition( hCenterVertex );
						var vNextBasePosition = GetVertexPosition( hNextMidpointVertex );

						var nVertexIndex = vertexFaceInfo.Vertices.IndexOf( hOuterVertex );

						if ( nVertexIndex != -1 )
						{
							var outerVertex = vertexFaceInfo.VertexData[nVertexIndex].OuterVertex;
							var nextVertex = vertexFaceInfo.VertexData[nVertexIndex].NextMidpointVertex;
							var centerVertex = vertexFaceInfo.VertexData[nVertexIndex].CenterVertex;
							var prevVertex = vertexFaceInfo.VertexData[nVertexIndex].PrevMidpointVertex;

							var pSubFaceQuad = new BevelQuadFace
							{
								FaceSource = BevelFaceSource.Vertex,
								FaceType = BevelFaceType.Quad,
								Face = hSubFace
							};

							pSubFaceQuad.SetVertex( 0, hOuterVertex, vOuterBasePosition, outerVertex.Position, outerVertex.UV );
							pSubFaceQuad.SetVertex( 1, hNextMidpointVertex, vNextBasePosition, nextVertex.Position, nextVertex.UV );
							pSubFaceQuad.SetVertex( 2, hCenterVertex, vCenterBasePosition, centerVertex.Position, centerVertex.UV );
							pSubFaceQuad.SetVertex( 3, hPrevMidpointVertex, vPrevBasePosition, prevVertex.Position, prevVertex.UV );
							vertexSubFaceQuads.Add( pSubFaceQuad );
						}
					}
				}

				if ( nNumSubFaceCuts == 0 )
				{
					Topology.GetFullEdgesConnectedToVertex( hCenterVertex, out var edgesConnectedToCenterVertex );
					centerEdges.AddRange( edgesConnectedToCenterVertex );
				}
			}
		}

		// Add a single center edge cut to each of the edge quads
		var edgeSubFaceQuads = new List<BevelQuadFace>();
		{
			var nNumEdgeFaces = edgeFaces.Count;
			edgeSubFaceQuads.EnsureCapacity( nNumEdgeFaces * 2 );

			for ( int iEdgeFace = 0; iEdgeFace < nNumEdgeFaces; ++iEdgeFace )
			{
				var edgeFace = edgeFaces[iEdgeFace];

				AddVertexToFaceEdgeBetweenVertices( edgeFace.Face, edgeFace.Vertices[3], edgeFace.Vertices[0], 0.5f, out var hVertexA );
				AddVertexToFaceEdgeBetweenVertices( edgeFace.Face, edgeFace.Vertices[1], edgeFace.Vertices[2], 0.5f, out var hVertexB );

				AddEdgeToFace( edgeFace.Face, hVertexA, hVertexB, out var hNewEdge );
				centerEdges.Add( hNewEdge );

				var hFaceA = Topology.FindFaceWithEdgeConnectingVertices( hVertexB, hVertexA );
				var hFaceB = GetOppositeFaceConnectedToEdge( hNewEdge, hFaceA );

				var vBasePositionA = GetVertexPosition( hVertexA );
				var vBasePositionB = GetVertexPosition( hVertexB );

				if ( edgeFace.QuadA.Count == 4 )
				{
					var pSubFaceQuadA = new BevelQuadFace
					{
						FaceSource = BevelFaceSource.Edge,
						FaceType = BevelFaceType.Quad,
						Face = hFaceA
					};

					pSubFaceQuadA.SetVertex( 0, edgeFace.Vertices[0], edgeFace.QuadA[0].Position, edgeFace.QuadA[0].Position, edgeFace.QuadA[0].UV );
					pSubFaceQuadA.SetVertex( 1, edgeFace.Vertices[1], edgeFace.QuadA[1].Position, edgeFace.QuadA[1].Position, edgeFace.QuadA[1].UV );
					pSubFaceQuadA.SetVertex( 2, hVertexB, vBasePositionB, edgeFace.QuadA[2].Position, edgeFace.QuadA[2].UV );
					pSubFaceQuadA.SetVertex( 3, hVertexA, vBasePositionA, edgeFace.QuadA[3].Position, edgeFace.QuadA[3].UV );
					edgeSubFaceQuads.Add( pSubFaceQuadA );
				}

				if ( edgeFace.QuadB.Count == 4 )
				{
					var pSubFaceQuadB = new BevelQuadFace
					{
						FaceSource = BevelFaceSource.Edge,
						FaceType = BevelFaceType.Quad,
						Face = hFaceB
					};

					pSubFaceQuadB.SetVertex( 0, edgeFace.Vertices[2], edgeFace.QuadB[0].Position, edgeFace.QuadB[0].Position, edgeFace.QuadB[0].UV );
					pSubFaceQuadB.SetVertex( 1, edgeFace.Vertices[3], edgeFace.QuadB[1].Position, edgeFace.QuadB[1].Position, edgeFace.QuadB[1].UV );
					pSubFaceQuadB.SetVertex( 2, hVertexA, vBasePositionA, edgeFace.QuadB[2].Position, edgeFace.QuadB[2].UV );
					pSubFaceQuadB.SetVertex( 3, hVertexB, vBasePositionB, edgeFace.QuadB[3].Position, edgeFace.QuadB[3].UV );
					edgeSubFaceQuads.Add( pSubFaceQuadB );
				}
			}
		}

		// Special case for triangle vertex faces at the end of a single edge
		{
			var nNumVertexFaces = vertexFaces.Count;
			for ( int iFace = 0; iFace < nNumVertexFaces; ++iFace )
			{
				var vertexFaceInfo = vertexFaces[iFace];

				if ( vertexFaceInfo.IsEndTriangle == false )
					continue;

				if ( Topology.ComputeNumEdgesInFace( vertexFaceInfo.Face ) != 4 )
					continue;

				var hVertexA = vertexFaceInfo.EndTriangleVertexA;
				var hVertexB = vertexFaceInfo.EndTriangleVertexB;
				var hFaceVertexA = FindFaceVertexConnectedToVertex( hVertexA, vertexFaceInfo.Face );
				var hFaceVertexB = FindFaceVertexConnectedToVertex( hVertexB, vertexFaceInfo.Face );

				var hMidpointFaceVertex = GetNextVertexInFace( hFaceVertexA );
				var hMidpointVertex = GetVertexConnectedToFaceVertex( hMidpointFaceVertex );

				var hOppositeFaceVertex = GetNextVertexInFace( hFaceVertexB );
				var hOppositeVertex = GetVertexConnectedToFaceVertex( hOppositeFaceVertex );

				AddEdgeToFace( vertexFaceInfo.Face, hOppositeVertex, hMidpointVertex, out var hNewEdge );

				Topology.GetFacesConnectedToFullEdge( hNewEdge, out var hSubFaceA, out var hSubFaceB );

				if ( FindFaceVertexConnectedToVertex( hVertexA, hSubFaceA ) == HalfEdgeHandle.Invalid )
				{
					(hSubFaceA, hSubFaceB) = (hSubFaceB, hSubFaceA);
				}

				var vBasePositionVertexA = GetVertexPosition( hVertexA );
				var vBasePositionVertexB = GetVertexPosition( hVertexB );
				var vBasePositionOpposite = GetVertexPosition( hOppositeVertex );
				var vBasePositionMidPoint = GetVertexPosition( hMidpointVertex );

				var nVertexIndexA = vertexFaceInfo.Vertices.IndexOf( hVertexA );
				var vertexDataA = vertexFaceInfo.VertexData[nVertexIndexA].OuterVertex;
				var vertexDataMidpointA = vertexFaceInfo.VertexData[nVertexIndexA].NextMidpointVertex;

				var nVertexIndexB = vertexFaceInfo.Vertices.IndexOf( hVertexB );
				var vertexDataB = vertexFaceInfo.VertexData[nVertexIndexB].OuterVertex;
				var vertexDataMidpointB = vertexFaceInfo.VertexData[nVertexIndexB].PrevMidpointVertex;

				var nVertexIndexOpposite = vertexFaceInfo.Vertices.IndexOf( hOppositeVertex );
				var vertexDataOpposite = vertexFaceInfo.VertexData[nVertexIndexOpposite].OuterVertex;

				{
					var pSubFaceA = new BevelQuadFace
					{
						FaceSource = BevelFaceSource.Vertex,
						FaceType = BevelFaceType.Triangle,
						Face = hSubFaceA
					};

					pSubFaceA.SetVertex( 0, hVertexA, vBasePositionVertexA, vertexDataA.Position, vertexDataA.UV );
					pSubFaceA.SetVertex( 1, hMidpointVertex, vBasePositionMidPoint, vertexDataMidpointA.Position, vertexDataMidpointA.UV );
					pSubFaceA.SetVertex( 2, hOppositeVertex, vBasePositionOpposite, vertexDataOpposite.Position, vertexDataOpposite.OutgoingUV );
					pSubFaceA.SetVertex( 3, hOppositeVertex, vBasePositionOpposite, vertexDataOpposite.Position, vertexDataOpposite.OutgoingUV );
					vertexSubFaceQuads.Add( pSubFaceA );
				}

				{
					var pSubFaceB = new BevelQuadFace
					{
						FaceSource = BevelFaceSource.Vertex,
						FaceType = BevelFaceType.TriangleFlipped,
						Face = hSubFaceB
					};

					pSubFaceB.SetVertex( 0, hVertexB, vBasePositionVertexB, vertexDataB.Position, vertexDataB.UV );
					pSubFaceB.SetVertex( 1, hMidpointVertex, vBasePositionMidPoint, vertexDataMidpointB.Position, vertexDataMidpointB.UV );
					pSubFaceB.SetVertex( 2, hOppositeVertex, vBasePositionOpposite, vertexDataOpposite.Position, vertexDataOpposite.UV );
					pSubFaceB.SetVertex( 3, hOppositeVertex, vBasePositionOpposite, vertexDataOpposite.Position, vertexDataOpposite.UV );
					vertexSubFaceQuads.Add( pSubFaceB );
				}
			}
		}

		// Assign the appropriate materials to the quad faces based on their neighboring faces before further sub-dividing them.
		BevelAssignMaterialsToQuadFaces( edgeSubFaceQuads, vertexSubFaceQuads );

		// Now that we have done a single level of subdivision on the initial faces and have entirely
		// quads, add the additional edges needed to get the requested number of segments.
		var subdividedFaces = new List<BevelSubdividedFace>();
		BevelSubdivideQuadFaces( edgeSubFaceQuads, 0, nNumSubFaceCuts, subdividedFaces, null ); // Already added center edges for edge faces
		BevelSubdivideQuadFaces( vertexSubFaceQuads, nNumSubFaceCuts, nNumSubFaceCuts, subdividedFaces, centerEdges );

		// If an odd number of sub-divisions were requested, we must remove the center edges
		var centerEdgeFaces = new List<FaceHandle>();

		if ( bRemoveCenterEdge )
		{
			FindVerticesConnectedToEdges( centerEdges, out var verticesToRemove );

			foreach ( var hEdge in centerEdges )
			{
				DissolveEdge( hEdge, out var hCenterFace );

				centerEdgeFaces.Add( hCenterFace );
			}

			foreach ( var hVertex in verticesToRemove )
			{
				RemoveVertex( hVertex, true );
			}

			BevelRemoveCenterEdgeVerticesFromSubdividedFaces( subdividedFaces, nNumSubFaceCuts + 1 );
		}

		// Compute the vertex positions for the vertices that were created by the subdivision.
		BevelComputeInterpolatedVertexPositions( subdividedFaces, flShape );

		// Its possible for the position interpolation to result in co-located vertices.
		// Merge these vertices and remove any faces that are invalid as a result.
		BevelMergeVerticesAndCleanupFaces( subdividedFaces );

		if ( pOutNewFaces is not null )
		{
			foreach ( var subdividedFace in subdividedFaces )
			{
				foreach ( var hFace in subdividedFace.Faces )
				{
					if ( IsFaceInMesh( hFace ) )
					{
						pOutNewFaces.Add( hFace );
					}
				}
			}
		}

		if ( pOutFacesNeedingUVs is not null )
		{
			pOutFacesNeedingUVs.EnsureCapacity( centerEdgeFaces.Count );

			foreach ( var hFace in centerEdgeFaces )
			{
				if ( IsFaceInMesh( hFace ) )
				{
					pOutFacesNeedingUVs.Add( hFace );
				}
			}
		}
	}

	private void BevelRemoveCenterEdgeVerticesFromSubdividedFaces( IReadOnlyList<BevelSubdividedFace> subdividedQuads, int nNumSteps )
	{
		var flStep = 1.0f / nNumSteps;
		var flMaxValue = 1.0f - flStep;
		var flTargetValue = 1.0f - (1.0f / (nNumSteps * 2 - 1));

		foreach ( var subdividedQuad in subdividedQuads )
		{
			var nNumVertices = subdividedQuad.Vertices.Count;
			for ( int iVertex = nNumVertices - 1; iVertex >= 0; --iVertex )
			{
				var hVertex = subdividedQuad.Vertices[iVertex];

				if ( IsVertexInMesh( hVertex ) )
				{
					// Re-parameterize the remaining vertices so that they are evenly spaced
					var vParam = subdividedQuad.VertexParameters[iVertex];
					vParam.x = MathF.Min( 1.0f, (vParam.x / flMaxValue) * flTargetValue );
					vParam.y = MathF.Min( 1.0f, (vParam.y / flMaxValue) * flTargetValue );
					subdividedQuad.VertexParameters[iVertex] = vParam;
				}
				else
				{
					// Remove vertices that belonged to center edges and were removed from the mesh
					subdividedQuad.Vertices.RemoveAt( iVertex );
					subdividedQuad.VertexParameters.RemoveAt( iVertex );
				}
			}
		}
	}

	private void BevelAssignMaterialsToQuadFaces( IReadOnlyList<BevelQuadFace> edgeSubFaceQuads, IReadOnlyList<BevelQuadFace> vertexSubFaceQuads )
	{
		foreach ( var edgeQuad in edgeSubFaceQuads )
		{
			var hOppositeFace = Topology.FindFaceWithEdgeConnectingVertices( edgeQuad.Vertices[1], edgeQuad.Vertices[0] );
			Assert.True( hOppositeFace != edgeQuad.Face );
			if ( hOppositeFace != FaceHandle.Invalid )
			{
				MaterialIndex[edgeQuad.Face] = MaterialIndex[hOppositeFace];
			}
		}

		// The opposite faces for vertex faces will be edges faces, so its important that the vertex face
		// material assignments are handled after the edges face material assignments so that the the edge
		// faces have the correct materials we when copy them to the vertex faces.
		foreach ( var vertexQuad in vertexSubFaceQuads )
		{
			var hEdge = FindEdgeConnectingVertices( vertexQuad.Vertices[0], vertexQuad.Vertices[1] );

			Topology.GetFacesConnectedToFullEdge( hEdge, out var hFaceA, out var hFaceB );

			var hOppositeFace = (hFaceA != vertexQuad.Face) ? hFaceA : hFaceB;

			if ( hOppositeFace != FaceHandle.Invalid )
			{
				MaterialIndex[vertexQuad.Face] = MaterialIndex[hOppositeFace];
			}
		}
	}

	private void BevelMergeVerticesAndCleanupFaces( IReadOnlyList<FaceHandle> faces, bool bDissolveEdges )
	{
		const float bevelVertexMergeDistance = 0.01f;

		var nNumFaces = faces.Count;

		// Get all the vertices connected to the faces
		Topology.FindVerticesConnectedToFaces( faces, nNumFaces, out var connectedVertices );

		const int nMaxIterations = 8;
		for ( int i = 0; i < nMaxIterations; ++i )
		{
			bool bModifications = false;

			// Merge co-located vertices
			if ( MergeVerticesWithinDistance( connectedVertices, bevelVertexMergeDistance, true, true, out _ ) > 0 )
			{
				bModifications = true;
			}

			// Remove any faces which cannot be triangulated (probably zero area). Note its important to
			// do a merge before this, otherwise there may be faces with co-located vertices within the
			// face that would be invalid, but the merge will fix.
			for ( int iFace = 0; iFace < nNumFaces; ++iFace )
			{
				var hFace = faces[iFace];

				// Ignore faces that have been removed
				if ( IsFaceInMesh( hFace ) == false )
					continue;

				if ( IsFaceShapeValid( hFace ) == false )
				{
					Topology.RemoveFace( hFace, true );
					bModifications = true;
				}
			}

			// Stop if no modifications were made
			if ( bModifications == false )
				break;
		}
	}

	void BevelMergeVerticesAndCleanupFaces( IReadOnlyList<BevelSubdividedFace> subdividedQuads )
	{
		foreach ( var subdividedQuad in subdividedQuads )
		{
			var bDissolveEdges = subdividedQuad.SourceQuad.FaceSource == BevelFaceSource.Vertex;
			BevelMergeVerticesAndCleanupFaces( subdividedQuad.Faces, bDissolveEdges );

			// Remove any faces from the results list the were removed (either because invalid or as a result of the vertex merge).
			for ( int iFace = subdividedQuad.Faces.Count - 1; iFace >= 0; --iFace )
			{
				if ( IsFaceInMesh( subdividedQuad.Faces[iFace] ) == false )
				{
					subdividedQuad.Faces.RemoveAt( iFace );
				}
			}
		}
	}

	void BevelSubdivideQuadFaces( IReadOnlyList<BevelQuadFace> quadFaces, int nNumCutsX, int nNumCutsY, List<BevelSubdividedFace> pOutResults, List<HalfEdgeHandle> pOutCenterEdges )
	{
		var edgeSpansY = new List<EdgeSpan>();

		var nNumFaces = quadFaces.Count;
		pOutResults.EnsureCapacity( pOutResults.Count + nNumFaces );

		for ( int i = 0; i < nNumFaces; ++i )
		{
			var quadFace = quadFaces[i];

			var pResults = new BevelSubdividedFace();
			pResults.SourceQuad = quadFace;

			if ( quadFace.FaceType == BevelFaceType.Quad )
			{
				// Add the requested number of cuts to the face
				QuadSliceFace( quadFace.Face, quadFace.Vertices, nNumCutsX, nNumCutsY, pResults.Faces, null, edgeSpansY, pResults.Vertices, pResults.VertexParameters );

				if ( pOutCenterEdges is not null && (edgeSpansY.Count > 0) )
				{
					edgeSpansY[^1].GetAllEdges( pOutCenterEdges );
				}
			}
			else
			{
				if ( quadFace.FaceType == BevelFaceType.Triangle )
				{
					BevelAddCutsToTriangle( quadFace, nNumCutsX, false, pResults );
				}
				else if ( quadFace.FaceType == BevelFaceType.TriangleFlipped )
				{
					BevelAddCutsToTriangle( quadFace, nNumCutsX, true, pResults );
				}
			}

			pOutResults.Add( pResults );
		}
	}

	private void BevelAddCutsToTriangle( BevelQuadFace quadFace, int nNumCuts, bool bFlipped, BevelSubdividedFace pResult )
	{
		var edgeSpan = new EdgeSpan();

		var hFaceVertex0 = FindFaceVertexConnectedToVertex( quadFace.Vertices[0], quadFace.Face );
		var hFaceVertex1 = FindFaceVertexConnectedToVertex( quadFace.Vertices[1], quadFace.Face );

		if ( bFlipped )
		{
			edgeSpan.InitializeFromFace( this, quadFace.Face, hFaceVertex1, hFaceVertex0 );
			edgeSpan.Reverse();
		}
		else
		{
			edgeSpan.InitializeFromFace( this, quadFace.Face, hFaceVertex0, hFaceVertex1 );
		}

		if ( edgeSpan.NumVertices < 2 )
			return;

		edgeSpan.AddVertices( nNumCuts, out var newVerticesOnSpan );

		var hCurrentFace = quadFace.Face;
		pResult.Faces.Add( hCurrentFace );

		for ( int iCut = 0; iCut < nNumCuts; ++iCut )
		{
			var nNewVertexIndex = newVerticesOnSpan[iCut];
			if ( nNewVertexIndex < 0 )
				continue;

			var hNewVertex = edgeSpan.GetVertex( nNewVertexIndex );

			// Add the new edge connecting the new vertex to the opposite vertex
			AddEdgeToFace( hCurrentFace, hNewVertex, quadFace.Vertices[2], out var hNewEdge );

			Topology.GetFacesConnectedToFullEdge( hNewEdge, out var hFaceA, out var hFaceB );

			if ( hFaceA != hCurrentFace )
			{
				pResult.Faces.Add( hFaceA );
			}

			if ( hFaceB != hCurrentFace )
			{
				pResult.Faces.Add( hFaceB );
			}

			// If there are more cuts to make, we need to update the current face, since adding the
			// edge generated a new face, we need to determine which of the two faces the next to add
			// the next edge to.
			if ( (iCut + 1) < nNumCuts )
			{
				var nNextVertexIndex = newVerticesOnSpan[iCut + 1];
				if ( nNewVertexIndex > 0 )
				{
					// Which ever face contains the next vertex will become the current face
					var hNextVertex = edgeSpan.GetVertex( nNextVertexIndex );
					if ( FindFaceVertexConnectedToVertex( hNextVertex, hFaceA ) != HalfEdgeHandle.Invalid )
					{
						hCurrentFace = hFaceA;
					}
					else
					{
						hCurrentFace = hFaceB;
					}
				}
			}
		}

		var nNumVertices = edgeSpan.NumVertices;
		for ( int iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			var flParam = iVertex / (float)(nNumVertices - 1);
			var hVertex = edgeSpan.GetVertex( iVertex );

			if ( hVertex != VertexHandle.Invalid )
			{
				pResult.Vertices.Add( hVertex );
				pResult.VertexParameters.Add( new Vector2( flParam, 0.0f ) );
			}
		}

		pResult.Vertices.Add( quadFace.Vertices[2] );
		pResult.VertexParameters.Add( new Vector2( 0.0f, 1.0f ) );
	}

	private enum BevelFaceSource
	{
		Invalid,
		Edge,
		Vertex,
	}

	private enum BevelFaceType
	{
		Quad,
		Triangle,
		TriangleFlipped,
	}

	private class BevelQuadFace
	{
		public BevelFaceSource FaceSource = BevelFaceSource.Invalid;
		public BevelFaceType FaceType = BevelFaceType.Quad;
		public FaceHandle Face = FaceHandle.Invalid;
		public VertexHandle[] Vertices = new VertexHandle[4];
		public Vector3[] BasePositions = new Vector3[4];
		public Vector3[] TargetPositions = new Vector3[4];
		public Vector2[] VertexUVs = new Vector2[4];

		public void SetVertex( int nIndex, VertexHandle hVertex, Vector3 vBasePosition, Vector3 vTargetPosition, Vector2 vUV )
		{
			Vertices[nIndex] = hVertex;
			BasePositions[nIndex] = vBasePosition;
			TargetPositions[nIndex] = vTargetPosition;
			VertexUVs[nIndex] = vUV;
		}
	}

	private class BevelSubdividedFace
	{
		public BevelQuadFace SourceQuad = new();
		public List<FaceHandle> Faces = new();
		public List<VertexHandle> Vertices = new();
		public List<Vector2> VertexParameters = new();
	}

	private void BevelComputeInterpolatedVertexPositions( IReadOnlyList<BevelSubdividedFace> subdividedQuads, float flShape )
	{
		var faceTable = new HashSet<FaceHandle>();

		foreach ( var subdividedQuad in subdividedQuads )
		{
			var bFromVertex = subdividedQuad.SourceQuad.FaceSource == BevelFaceSource.Vertex;
			var pBasePositions = subdividedQuad.SourceQuad.BasePositions;
			var pTargetPositions = subdividedQuad.SourceQuad.TargetPositions;

			// Build the hash table of all the result faces
			faceTable.Clear();
			faceTable.EnsureCapacity( subdividedQuad.Faces.Count );
			foreach ( var hFace in subdividedQuad.Faces )
			{
				faceTable.Add( hFace );
			}

			var nNumVertices = subdividedQuad.Vertices.Count;
			for ( int iVertex = 0; iVertex < nNumVertices; ++iVertex )
			{
				var hVertex = subdividedQuad.Vertices[iVertex];
				if ( IsVertexInMesh( hVertex ) == false )
					continue;

				var vParam = subdividedQuad.VertexParameters[iVertex];

				var vBasePosX1 = pBasePositions[0].LerpTo( pBasePositions[1], vParam.x );
				var vBasePosX2 = pBasePositions[3].LerpTo( pBasePositions[2], vParam.x );
				var vBasePosition = vBasePosX1.LerpTo( vBasePosX2, vParam.y );

				var vTargetPosX1 = pTargetPositions[0].LerpTo( pTargetPositions[1], vParam.x );
				var vTargetPosX2 = pTargetPositions[3].LerpTo( pTargetPositions[2], vParam.x );
				var vTargetPosition = vTargetPosX1.LerpTo( vTargetPosX2, vParam.y );

				{
					var vUVX1 = subdividedQuad.SourceQuad.VertexUVs[0].LerpTo( subdividedQuad.SourceQuad.VertexUVs[1], vParam.x );
					var vUVX2 = subdividedQuad.SourceQuad.VertexUVs[3].LerpTo( subdividedQuad.SourceQuad.VertexUVs[2], vParam.x );
					var vUV = vUVX1.LerpTo( vUVX2, vParam.y );

					Topology.GetIncomingHalfEdgesConnectedToVertex( hVertex, out var faceVertices );

					foreach ( var hFaceVertex in faceVertices )
					{
						// Only update UVs on faces that were generated by the bevel operation
						var hFace = Topology.GetFaceConnectedToHalfEdge( hFaceVertex );
						if ( faceTable.Contains( hFace ) == false )
							continue;

						TextureCoord[hFaceVertex] = vUV;
					}
				}

				if ( flShape > 0.99f )
				{
					// Use exact target for all vertices
					SetVertexPosition( hVertex, vTargetPosition );
				}
				else if ( flShape < 0.01f )
				{
					// Use exact base for all vertices
					SetVertexPosition( hVertex, vBasePosition );
				}
				else
				{
					// If the face is from and edge, we can just use the [ 0, 1 ] value of the y parameter of 
					// the vertex. However, when the face is from a vertex we need to use both the x and y
					// parameter values of the vertex, but we need the edges to exactly match the neighboring
					// edge faces, so we use the length of the parameter which will be [ 0, 1 ] on the edges,
					// however the full range is [ 0, sqrt(2) ], we cannot just divide by sqrt(2), as that
					// would effect the edges, so instead to determine the value we divide by, we use the
					// Min( vParam.x, vParam.y ) as a lerp parameter so that if either x or y  is 0 (on the edges)
					// we will divide by 1.0 so they will still match the neighboring faces. 
					var flVertexParam = MathX.Clamp( bFromVertex ? (vParam.Length / MathX.Lerp( 1.0f, 1.414213562f, MathF.Min( vParam.x, vParam.y ) )) : vParam.y, 0.0f, 1.0f );

					// Superellipse method: This is complicated by the fact that we are using the output in a
					// non-linear space (applying it as an interpolation parameter). Because of this to get it 
					// to behave the way we want, we make the input values non-linear. Additionally the input
					// values are restricted to the [ 0.0, 0.5 ] range because we are effectively mirroring the 
					// results across the beveled edge.
					//
					// r : [ 0.0, 4.0 ], this controls the range shapes, 4.0 is nearly rectangular. We
					// modify the value of flShape such that for flShape = 0.5, r = 1.0f. Above 0.5 we
					// square and multiply by 4 to get a sharp corner, below we offset by 0.25f because 
					// everything below 0.25f is effectively flat.
					var r = (flShape < 0.5f) ? (flShape * 1.5f + 0.25f) : (flShape * flShape * 4.0f);
					var x = MathF.Pow( flVertexParam, r ) * 0.5f; // Non-linear input, range [ 0.0, 0.5 ]
					var flInterpParam = MathF.Pow( (1.0f - MathF.Pow( x, r )), 1.0f / r ); // Superellipse equation
					SetVertexPosition( hVertex, vBasePosition.LerpTo( vTargetPosition, flInterpParam ) );
				}
			}
		}
	}

	private struct BevelVertexData
	{
		public Vector3 Position;
		public Vector2 UV;
		public Vector2 OutgoingUV;
	}

	private struct BevelVertexQuadData
	{
		public BevelVertexData OuterVertex;
		public BevelVertexData NextMidpointVertex;
		public BevelVertexData CenterVertex;
		public BevelVertexData PrevMidpointVertex;
	}

	private struct BevelEdgeFaceInfo
	{
		public FaceHandle Face;
		public List<VertexHandle> Vertices;
		public List<BevelVertexData> QuadA;
		public List<BevelVertexData> QuadB;
	}

	private struct BevelVertexFaceInfo
	{
		public FaceHandle Face;
		public List<VertexHandle> Vertices;
		public List<BevelVertexQuadData> VertexData;
		public bool IsEndTriangle;
		public VertexHandle EndTriangleVertexA;
		public VertexHandle EndTriangleVertexB;
	}

	private struct FaceTextureParameters
	{
		public Vector4 AxisU;
		public Vector4 AxisV;
		public Vector2 Scale;
	}

	private void ComputeTextureParametersForFaces( IReadOnlyList<FaceHandle> faces, out FaceTextureParameters[] outFaceTetureParameters )
	{
		var numFaces = faces.Count;
		outFaceTetureParameters = new FaceTextureParameters[numFaces];

		for ( var faceIndex = 0; faceIndex < numFaces; ++faceIndex )
		{
			var hFace = faces[faceIndex];
			if ( IsFaceInMesh( hFace ) == false )
				continue;

			// Get the positions and texture coordinates for all of the vertices of the face
			GetFaceVerticesConnectedToFace( hFace, out var faceVertices );
			var numVertices = faceVertices.Length;

			var facePositions = new Vector3[numVertices];
			var faceTexCoords = new Vector2[numVertices];

			for ( var vertexIndex = 0; vertexIndex < numVertices; ++vertexIndex )
			{
				var hFaceVertex = faceVertices[vertexIndex];
				var hVertex = GetVertexConnectedToFaceVertex( hFaceVertex );

				facePositions[vertexIndex] = GetVertexPosition( hVertex );
				faceTexCoords[vertexIndex] = TextureCoord[hFaceVertex];
			}

			// Get the position and texture coordinate of the three best vertices to use to compute the face texture parameters
			var bestPositions = new Vector3[3];
			var bestTexCoords = new Vector2[3];
			GetTextureBasisVertices( facePositions, faceTexCoords, bestPositions, bestTexCoords );
			ComputeFaceTextureParametersFromUVs( bestPositions, bestTexCoords, new Vector2( 1.0f, 1.0f ), out var vAxisU, out var vAxisV, out var vScale );
			outFaceTetureParameters[faceIndex] = new FaceTextureParameters() { AxisU = vAxisU, AxisV = vAxisV, Scale = vScale };
		}
	}

	private void UpdateUVsForFacesFromTextureParameters( IReadOnlyList<FaceHandle> faces, IReadOnlyList<FaceTextureParameters> faceTextureParameters )
	{
		var numFaces = faces.Count;
		for ( var faceIndex = 0; faceIndex < numFaces; ++faceIndex )
		{
			var hFace = faces[faceIndex];
			if ( IsFaceInMesh( hFace ) == false )
				continue;

			var parameters = faceTextureParameters[faceIndex];

			// Get all of the vertices in the face
			GetFaceVerticesConnectedToFace( hFace, out var faceVertices );
			var numVertices = faceVertices.Length;

			for ( var vertexIndex = 0; vertexIndex < numVertices; ++vertexIndex )
			{
				var hFaceVertex = faceVertices[vertexIndex];
				var hVertex = GetVertexConnectedToFaceVertex( hFaceVertex );

				// Get the local space position of the vertex
				var position = GetVertexPosition( hVertex );

				// Compute the projected texture coordinates
				var texCoord = TextureCoord[hFaceVertex];
				texCoord.x = Vector3.Dot( (Vector3)parameters.AxisU, position ) / parameters.Scale.x + parameters.AxisU.w;
				texCoord.y = Vector3.Dot( (Vector3)parameters.AxisV, position ) / parameters.Scale.y + parameters.AxisV.w;
				TextureCoord[hFaceVertex] = texCoord;
			}
		}
	}

	private void UpdateUVsForFacesFromTextureParameters( Dictionary<FaceHandle, FaceTextureParameters> faceTextureParameterTable )
	{
		UpdateUVsForFacesFromTextureParameters( faceTextureParameterTable.Keys.ToList(), faceTextureParameterTable.Values.ToList() );
	}

	private void GetEdgesConnectedToFaces( IReadOnlyList<FaceHandle> faces, List<HalfEdgeHandle> outInteriorEdges, List<HalfEdgeHandle> outExteriorEdges )
	{
		if ( (outInteriorEdges is null) && (outExteriorEdges is null) )
			return;

		FindEdgesConnectedToFaces( faces, faces.Count, out var connectedEdges, out var numFacesConnectedToEdge );
		var numEdges = connectedEdges.Length;

		if ( outInteriorEdges is not null )
		{
			outInteriorEdges.Clear();
			outInteriorEdges.EnsureCapacity( numEdges );

			for ( var edgeIndex = 0; edgeIndex < numEdges; ++edgeIndex )
			{
				if ( numFacesConnectedToEdge[edgeIndex] == 2 )
				{
					outInteriorEdges.Add( connectedEdges[edgeIndex] );
				}
			}
		}

		if ( outExteriorEdges is not null )
		{
			outExteriorEdges.Clear();
			outExteriorEdges.EnsureCapacity( numEdges );

			for ( var edgeIndex = 0; edgeIndex < numEdges; ++edgeIndex )
			{
				if ( numFacesConnectedToEdge[edgeIndex] == 1 )
				{
					outExteriorEdges.Add( connectedEdges[edgeIndex] );
				}
			}
		}
	}

	private float BevelComputeMaximumInsetDistanceForEdge( HalfEdgeHandle hEdge, float insetDistance, float maxParam )
	{
		if ( IsHalfEdgeInMesh( hEdge ) == false )
			return 0.0f;

		var hFace = Topology.GetFaceConnectedToHalfEdge( hEdge );
		if ( hFace == FaceHandle.Invalid )
			return insetDistance;

		var numVertices = Topology.ComputeNumEdgesInFace( hFace );
		if ( numVertices <= 0 )
			return 0.0f;

		// Get all of the vertices of the face, starting with the faces vertex at the end of the
		// specified edge. This way the specified edge goes from the last vertex to the first 
		// vertex returned, preventing the need to separately compute the positions of the edge.
		var vertexPositions = new Vector3[numVertices];
		GetFaceVertexPositionsStartingAtVertex( hEdge, vertexPositions );

		var edgeStartPos = vertexPositions[numVertices - 1];
		var edgeEndPos = vertexPositions[0];
		var edgeMidPoint = (edgeStartPos + edgeEndPos) * 0.5f;

		// Construct an edge segment going from the mid-point of the edge
		// and extending to the inside direction of the face.
		PlaneEquation( vertexPositions, out var normal, out _ );
		var edgeDir = edgeEndPos - edgeStartPos;
		var insetDir = Vector3.Cross( normal, edgeDir ).Normal;

		// Note that we only offset by the inset distance so that if the inset distance is small we
		// won't actually get any clipping of the segment. However, we divide the inset distance by
		// the max param because we always multiply the final segment length by the max param below.
		// This way if there is no clipping of the segment the final distance is equal to 
		// flInsetDistance, not flInsetDistance * flMaxParam.
		var insetPos = edgeMidPoint + insetDir * (insetDistance / maxParam);

		// Clip the line segment to the face, getting the list of segments that are inside the face
		Mesh.ClipPolygon( vertexPositions, edgeMidPoint, insetPos, out var insideSegmentPoints );
		if ( insideSegmentPoints.Length < 2 )
			return insetDistance;

		// Take the first segment (the one that should have the start point as one of its end points)
		// and compute its length. This is the distance we can move along the inset direction before
		// intersecting another edge. Finally apply the maximum parameter, which limits the inset 
		// distance to a fraction of the distance.
		var segmentLength = insideSegmentPoints[0].Distance( insideSegmentPoints[1] );
		var maxDistance = segmentLength * maxParam;

		return maxDistance;
	}

	private bool UpdateFaceTextureParameterTable( IReadOnlyList<HalfEdgeHandle> edges, Dictionary<FaceHandle, FaceTextureParameters> faceTextureParameterTable )
	{
		var addedAny = false;
		var numEdges = edges.Count;

		for ( var i = 0; i < numEdges; ++i )
		{
			Topology.GetFacesConnectedToFullEdge( edges[i], out var hFaceA, out var hFaceB );

			bool hasEntryA = faceTextureParameterTable.TryGetValue( hFaceA, out var parametersA );
			bool hasEntryB = faceTextureParameterTable.TryGetValue( hFaceB, out var parametersB );

			if ( !hasEntryA && !hasEntryB )
				continue;

			if ( !hasEntryA )
			{
				faceTextureParameterTable[hFaceA] = parametersB;
				addedAny = true;
			}
			else if ( !hasEntryB )
			{
				faceTextureParameterTable[hFaceB] = parametersA;
				addedAny = true;
			}
		}

		return addedAny;
	}

	private class BevelOriginalVertexData
	{
		private struct PerFaceVertexData
		{
			public FaceHandle Face;
			public Vector2 UV;
		}

		private struct VertexData
		{
			public VertexHandle Vertex;
			public Vector3 Position;
			public List<PerFaceVertexData> PerFaceData;
		}

		private record VertexFacePair( VertexHandle Vertex, FaceHandle Face );

		private List<VertexData> Vertices = new();
		private Dictionary<VertexFacePair, Vector2> OriginalUVTable = new();

		public bool InitializePreBevel( PolygonMesh mesh, IReadOnlyList<HalfEdgeHandle> edges )
		{
			mesh.FindVerticesConnectedToEdges( edges, out var connectedVertices );
			Vertices = new List<VertexData>( connectedVertices.Length );

			foreach ( var vertex in connectedVertices )
			{
				var vertexData = new VertexData
				{
					Vertex = vertex,
					Position = mesh.GetVertexPosition( vertex ),
					PerFaceData = new List<PerFaceVertexData>()
				};

				mesh.Topology.GetIncomingHalfEdgesConnectedToVertex( vertex, out var faceVertices );
				foreach ( var faceVertex in faceVertices )
				{
					vertexData.PerFaceData.Add( new PerFaceVertexData
					{
						Face = mesh.Topology.GetFaceConnectedToHalfEdge( faceVertex ),
						UV = mesh.TextureCoord[faceVertex]
					} );
				}

				Vertices.Add( vertexData );
			}

			return true;
		}

		public void UpdatePostBevel( PolygonMesh mesh, IReadOnlyList<HalfEdgeHandle> edges, IReadOnlyList<FaceHandle> interiorFaces, IReadOnlyList<FaceHandle> exteriorFaces )
		{
			if ( interiorFaces.Count != exteriorFaces.Count )
				throw new InvalidOperationException( "Mismatched face counts" );

			for ( int i = 0; i < interiorFaces.Count; i++ )
			{
				ReplaceFaceReferences( interiorFaces[i], exteriorFaces[i] );
			}

			BuildOriginalUVTable( mesh, edges );
		}

		public Vector2 GetUVForVertexAndFace( VertexHandle vertex, FaceHandle face )
		{
			if ( OriginalUVTable.Count == 0 )
				throw new InvalidOperationException( "UpdatePostBevel() was not called" );

			if ( !OriginalUVTable.TryGetValue( new VertexFacePair( vertex, face ), out var uv ) )
				throw new KeyNotFoundException( "Vertex-face pair not found" );

			return uv;
		}

		private void ReplaceFaceReferences( FaceHandle findFace, FaceHandle replaceFace )
		{
			for ( var v = 0; v < Vertices.Count; v++ )
			{
				var vertex = Vertices[v];

				for ( var i = 0; i < vertex.PerFaceData.Count; i++ )
				{
					if ( vertex.PerFaceData[i].Face == findFace )
					{
						vertex.PerFaceData[i] = new PerFaceVertexData
						{
							Face = replaceFace,
							UV = vertex.PerFaceData[i].UV
						};
					}
				}

				Vertices[v] = vertex;
			}
		}

		private void BuildOriginalUVTable( PolygonMesh mesh, IReadOnlyList<HalfEdgeHandle> edges )
		{
			mesh.FindVerticesConnectedToEdges( edges, out var connectedVertices );
			OriginalUVTable = new Dictionary<VertexFacePair, Vector2>( connectedVertices.Length * 4 );

			foreach ( var vertex in connectedVertices )
			{
				var position = mesh.GetVertexPosition( vertex );
				var index = FindVertexEntry( vertex, position );
				if ( index < 0 )
					continue;

				foreach ( var perFaceData in Vertices[index].PerFaceData )
				{
					OriginalUVTable[new VertexFacePair( vertex, perFaceData.Face )] = perFaceData.UV;
				}
			}
		}

		private int FindVertexEntry( VertexHandle hVertex, Vector3 position )
		{
			var closestIndex = -1;
			var minDistance = 1.0f;

			for ( var i = 0; i < Vertices.Count; i++ )
			{
				if ( Vertices[i].Vertex == hVertex )
				{
					return i;
				}

				var distance = position.Distance( Vertices[i].Position );
				if ( distance < minDistance )
				{
					minDistance = distance;
					closestIndex = i;
				}
			}

			return closestIndex;
		}
	}
}
