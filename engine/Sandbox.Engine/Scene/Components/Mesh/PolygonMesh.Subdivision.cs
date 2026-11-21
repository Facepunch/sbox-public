using HalfEdgeMesh;

namespace Sandbox;

public partial class PolygonMesh
{
	private VertexHandle SubdivideFace( FaceHandle hFace )
	{
		Topology.GetVerticesConnectedToFace( hFace, out var vertices );

		SubdivideFace( hFace, 1, 0, vertices, vertices.Length, out var hCenterVertex );
		return hCenterVertex;
	}

	internal void SubdivideFace( FaceHandle hFace, int nNumDivisions, int nCurrentLevel, IReadOnlyList<VertexHandle> pOriginalVertices, int nNumOriginalVertices, out VertexHandle pOutCenterVertex,
		Action<FaceHandle, IReadOnlyList<VertexHandle>, int, int, int> visitQuad = null, int patch = 0, int x = 0, int y = 0 )
	{
		pOutCenterVertex = VertexHandle.Invalid;

		if ( nNumOriginalVertices < 3 ) return;
		if ( nCurrentLevel >= nNumDivisions )
		{
			visitQuad?.Invoke( hFace, pOriginalVertices, patch, x, y );
			return;
		}

		Assert.True( (nCurrentLevel == 0) || (nNumOriginalVertices == 4) );
		if ( (nCurrentLevel != 0) && (nNumOriginalVertices != 4) )
			return;

		if ( IsFaceInMesh( hFace ) == false )
			return;

		// Validate the original vertices
		for ( int iVertex = 0; iVertex < nNumOriginalVertices; ++iVertex )
		{
			if ( IsVertexInMesh( pOriginalVertices[iVertex] ) == false )
				return;
		}

		// Add the new edge vertices or find the existing vertices that are in the correct location
		var pMidpointVertices = new VertexHandle[nNumOriginalVertices];
		for ( int nCurrentVertex = nNumOriginalVertices - 1, nNextVertex = 0; nNextVertex < nNumOriginalVertices; nCurrentVertex = nNextVertex++ )
		{
			var hVertexA = pOriginalVertices[nCurrentVertex];
			var hVertexB = pOriginalVertices[nNextVertex];
			AddVertexToFaceEdgeBetweenVertices( hFace, hVertexA, hVertexB, 0.5f, out pMidpointVertices[nCurrentVertex] );
		}

		var hSharedFace = FaceHandle.Invalid;

		// Create the edges connecting the midpoint vertices
		var pCenterVertices = new VertexHandle[nNumOriginalVertices];
		for ( int nCurrentVertex = nNumOriginalVertices - 1, nNextVertex = 0; nNextVertex < nNumOriginalVertices; nCurrentVertex = nNextVertex++ )
		{
			var hVertexA = pMidpointVertices[nCurrentVertex];
			var hVertexB = pMidpointVertices[nNextVertex];
			hSharedFace = Topology.FindFaceSharedByVertices( hVertexA, hVertexB );
			if ( hSharedFace == FaceHandle.Invalid )
				continue;

			AddEdgeToFace( hSharedFace, hVertexA, hVertexB, out _ );
			AddVertexToEdge( hVertexA, hVertexB, 0.5f, out pCenterVertices[nNextVertex] );
		}

		// Create the edges connecting the center vertices
		var pCenterEdges = new HalfEdgeHandle[nNumOriginalVertices];
		for ( int nCurrentVertex = nNumOriginalVertices - 1, nNextVertex = 0; nNextVertex < nNumOriginalVertices; nCurrentVertex = nNextVertex++ )
		{
			var hVertexA = pCenterVertices[nCurrentVertex];
			var hVertexB = pCenterVertices[nNextVertex];

			hSharedFace = Topology.FindFaceSharedByVertices( hVertexA, hVertexB );
			if ( hSharedFace == FaceHandle.Invalid )
				continue;

			AddEdgeToFace( hSharedFace, hVertexA, hVertexB, out pCenterEdges[nNextVertex] );
		}

		// Find the center face by finding the face common to the first two center edges
		Topology.GetFacesConnectedToFullEdge( pCenterEdges[0], out var hFaceA1, out var hFaceA2 );
		Topology.GetFacesConnectedToFullEdge( pCenterEdges[1], out var hFaceB1, out var hFaceB2 );

		hSharedFace = FaceHandle.Invalid;
		if ( (hFaceA1 == hFaceB1) || (hFaceA1 == hFaceB2) )
		{
			hSharedFace = hFaceA1;
		}
		else if ( (hFaceA2 == hFaceB1) || (hFaceA2 == hFaceB2) )
		{
			hSharedFace = hFaceA2;
		}

		// Collapse the center face, turning it into a single vertex
		if ( CollapseFace( hSharedFace, out var hCenterVertex ) == false )
			return;

		pOutCenterVertex = hCenterVertex;

		// Recursively subdivide the resulting faces

		if ( nCurrentLevel > 0 )
		{
			Assert.True( nNumOriginalVertices == 4 );

			int half = 1 << (nNumDivisions - nCurrentLevel - 1);
			void Quad( int corner, int dx, int dy, VertexHandle a, VertexHandle b, VertexHandle c, VertexHandle d )
			{
				var child = Topology.FindFaceSharedByVertices( hCenterVertex, pOriginalVertices[corner] );
				SubdivideFace( child, nNumDivisions, nCurrentLevel + 1, [a, b, c, d], 4, out _, visitQuad, patch, x + dx, y + dy );
			}
			Quad( 0, 0, 0, pOriginalVertices[0], pMidpointVertices[0], hCenterVertex, pMidpointVertices[3] );
			Quad( 1, half, 0, pMidpointVertices[0], pOriginalVertices[1], pMidpointVertices[1], hCenterVertex );
			Quad( 3, 0, half, pMidpointVertices[3], hCenterVertex, pMidpointVertices[2], pOriginalVertices[3] );
			Quad( 2, half, half, hCenterVertex, pMidpointVertices[1], pOriginalVertices[2], pMidpointVertices[2] );
		}
		else
		{
			var childFaceOriginalVertices = new VertexHandle[4];

			for ( int iVertex = 0, iPrevVertex = nNumOriginalVertices - 1; iVertex < nNumOriginalVertices; iPrevVertex = iVertex++ )
			{
				childFaceOriginalVertices[0] = pOriginalVertices[iVertex];
				childFaceOriginalVertices[1] = pMidpointVertices[iVertex];
				childFaceOriginalVertices[2] = hCenterVertex;
				childFaceOriginalVertices[3] = pMidpointVertices[iPrevVertex];

				var hChildFace = Topology.FindFaceSharedByVertices( hCenterVertex, pOriginalVertices[iVertex] );

				SubdivideFace( hChildFace, nNumDivisions, nCurrentLevel + 1, childFaceOriginalVertices, 4, out _, visitQuad, iVertex );

			}
		}
	}

	private bool AddVertexToFaceEdgeBetweenVertices( FaceHandle hFace, VertexHandle hEdgeVertexA, VertexHandle hEdgeVertexB, float flParam, out VertexHandle pOutNewVertex )
	{
		const float flTolerance = 0.01f;

		pOutNewVertex = VertexHandle.Invalid;
		var hNewVertex = VertexHandle.Invalid;

		// Find the face vertices connected to the specified vertices in the specified face
		var hFaceVertexA = FindFaceVertexConnectedToVertex( hEdgeVertexA, hFace );
		var hFaceVertexB = FindFaceVertexConnectedToVertex( hEdgeVertexB, hFace );
		if ( (hFaceVertexA == HalfEdgeHandle.Invalid) || (hFaceVertexB == HalfEdgeHandle.Invalid) )
			return false;

		// Compute the length of the entire edge
		int nNumSegments = 0;
		float flEdgeLength = 0;
		var hCurrentFaceVertex = hFaceVertexA;
		while ( hCurrentFaceVertex != hFaceVertexB )
		{
			var hNextFaceVertex = GetNextVertexInFace( hCurrentFaceVertex );
			var hSegmentVertexA = GetVertexConnectedToFaceVertex( hCurrentFaceVertex );
			var hSegmentVertexB = GetVertexConnectedToFaceVertex( hNextFaceVertex );
			var vPositionA = GetVertexPosition( hSegmentVertexA );
			var vPositionB = GetVertexPosition( hSegmentVertexB );

			float flSegmentLength = vPositionA.Distance( vPositionB );
			flEdgeLength += flSegmentLength;
			++nNumSegments;

			hCurrentFaceVertex = hNextFaceVertex;
		}

		// If the edge is zero length, we will just say each segment is 1 unit 
		// so the total length equals the number of units is 1.0f * nNumSegmets;
		bool bZeroLengthEdge = false;
		if ( flEdgeLength <= 0.0f )
		{
			flEdgeLength = (float)nNumSegments;
			bZeroLengthEdge = true;
		}

		// Compute the distance of the new vertex along the edge
		float flNewVertexDistance = flEdgeLength * flParam;

		// Find the segment to which the new vertex should be added
		float flCurrentLength = 0.0f;
		hCurrentFaceVertex = hFaceVertexA;
		while ( hCurrentFaceVertex != hFaceVertexB )
		{
			var hNextFaceVertex = GetNextVertexInFace( hCurrentFaceVertex );
			var hCurrentVertex = GetVertexConnectedToFaceVertex( hCurrentFaceVertex );
			var hNextVertex = GetVertexConnectedToFaceVertex( hNextFaceVertex );
			var vPositionA = GetVertexPosition( hCurrentVertex );
			var vPositionB = GetVertexPosition( hNextVertex );

			float flSegmentLength = vPositionA.Distance( vPositionB );
			if ( bZeroLengthEdge )
			{
				flSegmentLength = 1.0f;
			}

			if ( flSegmentLength > 0.0f )
			{
				float flSegmentParameter = (flNewVertexDistance - flCurrentLength) / flSegmentLength;
				Assert.True( flSegmentParameter >= 0.0f );

				// If the segment parameter is less than or equal to 1.0 this is the segment the vertex is on
				if ( flSegmentParameter <= 1.0f )
				{
					if ( flSegmentParameter < flTolerance )
					{
						// The new vertex position is the position of the first vertex of the current segment
						hNewVertex = hCurrentVertex;
					}
					else if ( flSegmentParameter > (1.0f - flTolerance) )
					{
						// The new vertex position is the position of the second vertex of the current segment
						hNewVertex = hNextVertex;
					}
					else
					{
						// Add the new vertex to the segment
						if ( AddVertexToEdge( hCurrentVertex, hNextVertex, flSegmentParameter, out hNewVertex ) == false )
							return false;
					}

					// The new vertex was found or added to this segment no need to check the others.
					break;
				}
			}

			flCurrentLength += flSegmentLength;
			hCurrentFaceVertex = hNextFaceVertex;
		}

		pOutNewVertex = hNewVertex;

		return hNewVertex != VertexHandle.Invalid;
	}
}
