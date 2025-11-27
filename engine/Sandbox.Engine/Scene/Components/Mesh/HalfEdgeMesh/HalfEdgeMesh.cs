
namespace HalfEdgeMesh;

internal struct Vertex
{
	public int Edge { get; set; } // Half edge emanating from the vertex

	public static Vertex Invalid => new() { Edge = -1 };
}

internal struct Face
{
	public int Edge { get; set; } // One of the edges opposite to the face

	public static Face Invalid => new() { Edge = -1 };
}

internal struct HalfEdge
{
	public int Vertex { get; set; } // Vertex at the end of the edge
	public int OppositeEdge { get; set; } // Half edge which runs the opposite direction from this edge
	public int NextEdge { get; set; } // Next half edge in the edge loop around the face to which this edge belongs
	public int Face { get; set; } // Face to which the half edge belongs

	public static HalfEdge Invalid => new()
	{
		Vertex = -1,
		OppositeEdge = -1,
		NextEdge = -1,
		Face = -1,
	};
}

internal interface IHandle
{
	internal int Index { get; }
	internal bool IsValid { get; }
}

internal enum EdgeConnectivityType
{
	Open,   // Edge is open (connected to 1 face)
	Closed, // Edge is closed (connected to 2 faces)
	Any,    // Edge is open or closed (connected to 1 or 2 faces)
}

internal enum ComponentConnectivityType
{
	None,   // None of the edges in the set are connected to any other edges
	Mixed,  // Some of the edges are connected but not all edges are connected to a single group
	List,   // All of the edges are connected in a single list
	Loop,   // All of the edges are connected in a single closed loop
	Tree,   // All of the edges are connected in a single group, but there a branches in the connection
}

public sealed record VertexHandle : IHandle
{
	public int Index { get; private init; }
	internal Mesh Mesh { get; private init; }

	internal VertexHandle( int index, Mesh mesh )
	{
		Index = index;
		Mesh = Index >= 0 ? mesh : null;
	}

	public bool IsValid => Index >= 0 && Mesh is not null && Mesh.IsVertexAllocated( this );
	public static VertexHandle Invalid => new( -1, null );

	public HalfEdgeHandle Edge
	{
		get => new( Mesh is null ? -1 : Mesh[this].Edge, Mesh );
		set => Mesh?.SetVertexEdge( this, value );
	}
}

public sealed record FaceHandle : IHandle
{
	public int Index { get; private init; }
	internal Mesh Mesh { get; private init; }

	internal FaceHandle( int index, Mesh mesh )
	{
		Index = index;
		Mesh = Index >= 0 ? mesh : null;
	}

	public bool IsValid => Index >= 0 && Mesh is not null && Mesh.IsFaceAllocated( this );
	public static FaceHandle Invalid => new( -1, null );

	public HalfEdgeHandle Edge
	{
		get => new( Mesh is null ? -1 : Mesh[this].Edge, Mesh );
		set => Mesh?.SetFaceEdge( this, value );
	}
}

public sealed record HalfEdgeHandle : IHandle
{
	public int Index { get; private init; }
	internal Mesh Mesh { get; private init; }

	internal HalfEdgeHandle( int index, Mesh mesh )
	{
		Index = index;
		Mesh = Index >= 0 ? mesh : null;
	}

	public bool IsValid => Index >= 0 && Mesh is not null && Mesh.IsHalfEdgeAllocated( this );
	public static HalfEdgeHandle Invalid => new( -1, null );

	public VertexHandle Vertex
	{
		get => new( Mesh is null ? -1 : Mesh[this].Vertex, Mesh );
		set => Mesh?.SetEdgeVertex( this, value );
	}

	public HalfEdgeHandle OppositeEdge
	{
		get => new( Mesh is null ? -1 : Mesh[this].OppositeEdge, Mesh );
		set => Mesh?.SetEdgeOpposite( this, value );
	}

	public HalfEdgeHandle NextEdge
	{
		get => new( Mesh is null ? -1 : Mesh[this].NextEdge, Mesh );
		set => Mesh?.SetEdgeNext( this, value );
	}

	public FaceHandle Face
	{
		get => new( Mesh is null ? -1 : Mesh[this].Face, Mesh );
		set => Mesh?.SetEdgeFace( this, value );
	}
}

internal sealed partial class Mesh
{
	private ComponentList<Vertex> VertexList { get; set; } = new();
	private ComponentList<Face> FaceList { get; set; } = new();
	private ComponentList<HalfEdge> HalfEdgeList { get; set; } = new();

	private int VertexCount => VertexList.Count;
	private int FaceCount => FaceList.Count;
	private int HalfEdgeCount => HalfEdgeList.Count;

	private bool IsVertexInMesh( VertexHandle hVertex ) => hVertex is not null && hVertex.IsValid;

	private VertexHandle AllocateVertex( Vertex vertex, VertexHandle hSource = default ) => new( VertexList.Allocate( vertex, hSource ), this );
	private FaceHandle AllocateFace( Face face, FaceHandle hSource = default ) => new( FaceList.Allocate( face, hSource ), this );
	private HalfEdgeHandle AllocateHalfEdge( HalfEdge halfEdge, HalfEdgeHandle hSource = default ) => new( HalfEdgeList.Allocate( halfEdge, hSource ), this );

	public bool IsVertexAllocated( VertexHandle hVertex ) => VertexList.IsAllocated( hVertex );
	public bool IsFaceAllocated( FaceHandle hFace ) => FaceList.IsAllocated( hFace );
	public bool IsHalfEdgeAllocated( HalfEdgeHandle hHalfEdge ) => HalfEdgeList.IsAllocated( hHalfEdge );

	public IEnumerable<VertexHandle> VertexHandles => VertexList.ActiveList.Select( i => new VertexHandle( i, this ) );
	public IEnumerable<FaceHandle> FaceHandles => FaceList.ActiveList.Select( i => new FaceHandle( i, this ) );
	public IEnumerable<HalfEdgeHandle> HalfEdgeHandles => HalfEdgeList.ActiveList.Select( i => new HalfEdgeHandle( i, this ) );

	public VertexHandle AddVertex() => AllocateVertex( Vertex.Invalid );

	public void AppendComponentsFromMesh( Mesh sourceMesh,
		out Dictionary<VertexHandle, VertexHandle> newVertices,
		out Dictionary<HalfEdgeHandle, HalfEdgeHandle> newHalfEdges,
		out Dictionary<FaceHandle, FaceHandle> newFaces )
	{
		newVertices = new();
		newHalfEdges = new();
		newFaces = new();

		foreach ( var hVertex in sourceMesh.VertexHandles )
		{
			var hNewVertex = AllocateVertex( Vertex.Invalid );
			newVertices.Add( hVertex, hNewVertex );
		}

		foreach ( var hFace in sourceMesh.FaceHandles )
		{
			var hNewFace = AllocateFace( Face.Invalid );
			newFaces.Add( hFace, hNewFace );
		}

		foreach ( var hHalfEdge in sourceMesh.HalfEdgeHandles )
		{
			var hNewHalfEdge = AllocateHalfEdge( HalfEdge.Invalid );
			newHalfEdges.Add( hHalfEdge, hNewHalfEdge );
		}

		foreach ( var pair in newVertices )
		{
			var hVertex = pair.Key;
			var hNewVertex = pair.Value;

			if ( newHalfEdges.TryGetValue( hVertex.Edge, out var hEdge ) )
				hNewVertex.Edge = hEdge;
		}

		foreach ( var pair in newFaces )
		{
			var hFace = pair.Key;
			var hNewFace = pair.Value;

			if ( newHalfEdges.TryGetValue( hFace.Edge, out var hEdge ) )
				hNewFace.Edge = hEdge;
		}

		foreach ( var pair in newHalfEdges )
		{
			var hHalfEdge = pair.Key;
			var hNewHalfEdge = pair.Value;

			if ( newVertices.TryGetValue( hHalfEdge.Vertex, out var hVertex ) )
				hNewHalfEdge.Vertex = hVertex;

			if ( newHalfEdges.TryGetValue( hHalfEdge.OppositeEdge, out var hOppositeEdge ) )
				hNewHalfEdge.OppositeEdge = hOppositeEdge;

			if ( newHalfEdges.TryGetValue( hHalfEdge.NextEdge, out var hNextEdge ) )
				hNewHalfEdge.NextEdge = hNextEdge;

			if ( newFaces.TryGetValue( hHalfEdge.Face, out var hFace ) )
				hNewHalfEdge.Face = hFace;
		}
	}

	public void FlipAllFaces()
	{
		foreach ( var hVertex in VertexHandles )
		{
			var hEdge = hVertex.Edge;
			if ( hEdge.IsValid )
			{
				hVertex.Edge = hEdge.OppositeEdge;
			}
		}

		foreach ( var hEdge in HalfEdgeHandles )
		{
			var hOpposite = hEdge.OppositeEdge;
			if ( hOpposite.IsValid && hEdge.Index < hOpposite.Index )
			{
				(hEdge.Vertex, hOpposite.Vertex) = (hOpposite.Vertex, hEdge.Vertex);
			}
		}

		foreach ( var hFace in FaceHandles )
		{
			ReverseFaceLoop( hFace );
		}
	}

	private void ReverseFaceLoop( FaceHandle hFace )
	{
		if ( !hFace.IsValid )
			return;

		var hStartEdge = hFace.Edge;
		if ( !hStartEdge.IsValid )
			return;

		var edges = new List<HalfEdgeHandle>();
		var hCurrent = hStartEdge;
		do
		{
			edges.Add( hCurrent );
			hCurrent = hCurrent.NextEdge;
		}
		while ( hCurrent.IsValid && hCurrent != hStartEdge );

		for ( int i = 0; i < edges.Count; i++ )
		{
			edges[i].NextEdge = edges[(i - 1 + edges.Count) % edges.Count];
		}
	}

	public IEnumerable<VertexHandle> AddVertices( int count )
	{
		int vertexCount = VertexCount;
		VertexList.AllocateMultiple( count, Vertex.Invalid );

		for ( int i = 0; i < count; i++ )
			yield return new( vertexCount + i, this );
	}

	public FaceHandle AddFace( params VertexHandle[] hVertices )
	{
		if ( !AddFace( hVertices, out var hFace ) )
			return FaceHandle.Invalid;

		return hFace;
	}

	public bool AddFace( out FaceHandle hOutFace, params VertexHandle[] hVertices )
	{
		if ( !AddFace( hVertices, out hOutFace ) )
			return false;

		return true;
	}

	public VertexHandle[] GetFaceVertices( FaceHandle hFace )
	{
		GetHalfEdgesConnectedToFace( hFace, out var hEdges );
		var hVertices = new VertexHandle[hEdges.Length];

		int i = 0;
		foreach ( var hEdge in hEdges )
			hVertices[i++] = hEdge.Vertex;

		return hVertices;
	}

	public HalfEdgeHandle GetOpenHalfEdgeFromFullEdge( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		if ( hEdge.Face == FaceHandle.Invalid )
			return hEdge;

		var hOpposite = hEdge.OppositeEdge;
		if ( hOpposite.Face == FaceHandle.Invalid )
			return hOpposite;

		return HalfEdgeHandle.Invalid;
	}

	public bool BridgeEdges( HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB, out FaceHandle hOutNewFace )
	{
		var hOpenHalfEdgeA = GetOpenHalfEdgeFromFullEdge( hEdgeA );
		var hOpenHalfEdgeB = GetOpenHalfEdgeFromFullEdge( hEdgeB );

		hOutNewFace = FaceHandle.Invalid;

		if ( !hOpenHalfEdgeA.IsValid )
			return false;

		if ( !hOpenHalfEdgeB.IsValid )
			return false;

		GetVerticesConnectedToHalfEdge( hOpenHalfEdgeA, out var hVertexA1, out var hVertexA2 );
		GetVerticesConnectedToHalfEdge( hOpenHalfEdgeB, out var hVertexB1, out var hVertexB2 );

		if ( (hVertexA1 == hVertexB1) || (hVertexA1 == hVertexB2) ||
			 (hVertexA2 == hVertexB1) || (hVertexA2 == hVertexB2) )
			return false;

		return AddFace( out hOutNewFace, hVertexA1, hVertexA2, hVertexB1, hVertexB2 );
	}

	public VertexHandle SplitVertex( HalfEdgeHandle hIncomingEdge, HalfEdgeHandle hOutgoingEdge, out HalfEdgeHandle pOutNewIncomingEdge, out HalfEdgeHandle pOutNewOutGoingEdge )
	{
		pOutNewIncomingEdge = HalfEdgeHandle.Invalid;
		pOutNewOutGoingEdge = HalfEdgeHandle.Invalid;

		var hIncomingFace = GetFaceConnectedToHalfEdge( hIncomingEdge );
		var hOutgoingFace = GetFaceConnectedToHalfEdge( hOutgoingEdge );
		if ( (hIncomingFace == FaceHandle.Invalid) || (hOutgoingFace == FaceHandle.Invalid) )
			return VertexHandle.Invalid;

		// Get the vertex at the end of the incoming edge which is the target of the operation 
		var hVertex = GetEndVertexConnectedToEdge( hIncomingEdge );

		var hIncomingOpposite = GetOppositeHalfEdge( hIncomingEdge );
		var hOutgoingOpposite = GetOppositeHalfEdge( hOutgoingEdge );

		// Verify that the outgoing edge originates at the same vertex as the incoming edge terminates at.
		Assert.True( GetEndVertexConnectedToEdge( hOutgoingOpposite ) == hVertex );
		if ( GetEndVertexConnectedToEdge( hOutgoingOpposite ) != hVertex )
			return VertexHandle.Invalid;

		// If the opposite edge of the outgoing edge is connected directly to the opposite edge of the 
		// incoming edge and neither is connected to a face, there is no work to be done the vertex is
		// already completely detached.
		if ( GetNextEdgeInFaceLoop( hOutgoingOpposite ) == hIncomingOpposite )
		{
			if ( (GetFaceConnectedToHalfEdge( hOutgoingOpposite ) == FaceHandle.Invalid) &&
				 (GetFaceConnectedToHalfEdge( hIncomingOpposite ) == FaceHandle.Invalid) )
				return VertexHandle.Invalid;
		}

		// Find the proceeding and following edges and vertices
		var hOutgoingNextEdge = GetNextEdgeInFaceLoop( hOutgoingEdge );
		var hOutgoingPrevEdge = FindPreviousEdgeInFaceLoop( hOutgoingEdge );

		var hIncomingNextEdge = GetNextEdgeInFaceLoop( hIncomingEdge );
		var hIncomingPrevEdge = FindPreviousEdgeInFaceLoop( hIncomingEdge );

		var hNextVertex = GetEndVertexConnectedToEdge( hOutgoingEdge );
		var hPrevVertex = GetEndVertexConnectedToEdge( hIncomingPrevEdge );

		// Create the new vertex 
		var hNewVertex = AllocateVertex( Vertex.Invalid, hVertex );

		// Create the new edges
		var hNewIncomingEdge = ConstructHalfEdgePair( hPrevVertex, hNewVertex );
		var hNewIncomingOpposite = GetOppositeHalfEdge( hNewIncomingEdge );

		var hNewOutgoingEdge = ConstructHalfEdgePair( hNewVertex, hNextVertex );
		var hNewOutgoingOpposite = GetOppositeHalfEdge( hNewOutgoingEdge );


		// Make sure all of the vertices refer to one of the edges 
		// still in the face that we are definitely not going to remove.
		var pVertex = hVertex;
		var pNewVertex = hNewVertex;
		var pNextVertex = hNextVertex;
		var pPrevVertex = hPrevVertex;

		pVertex.Edge = hOutgoingEdge;
		pNewVertex.Edge = hNewOutgoingEdge;
		pNextVertex.Edge = hOutgoingNextEdge;
		pPrevVertex.Edge = hNewIncomingEdge;

		// Fixup the two edge loops
		var pIncomingEdge = hIncomingEdge;
		var pIncomingPrevEdge = hIncomingPrevEdge;

		var pOutgoingEdge = hOutgoingEdge;
		var pOutgoingPrevEdge = hOutgoingPrevEdge;

		var pNewIncomingEdge = hNewIncomingEdge;
		var pNewIncomingOppsite = hNewIncomingOpposite;

		var pNewOutgoingEdge = hNewOutgoingEdge;
		var pNewOutgoingOpposite = hNewOutgoingOpposite;

		var pIncomingFace = hIncomingFace;
		var pOutgoingFace = hOutgoingFace;

		// Edge loop attached to the new vertex
		pIncomingFace.Edge = hNewIncomingEdge;
		pOutgoingFace.Edge = hNewOutgoingEdge;

		pNewIncomingEdge.Face = hIncomingFace;
		pNewOutgoingEdge.Face = hOutgoingFace;

		pIncomingPrevEdge.NextEdge = hNewIncomingEdge;
		pNewOutgoingEdge.NextEdge = hOutgoingNextEdge;

		if ( hIncomingNextEdge != hOutgoingEdge )
		{
			pNewIncomingEdge.NextEdge = hIncomingNextEdge;
			pOutgoingPrevEdge.NextEdge = hNewOutgoingEdge;
		}
		else
		{
			pNewIncomingEdge.NextEdge = hNewOutgoingEdge;
		}

		// New open edge loop
		pIncomingEdge.Face = FaceHandle.Invalid;
		pIncomingEdge.NextEdge = hOutgoingEdge;
		pOutgoingEdge.Face = FaceHandle.Invalid;
		pOutgoingEdge.NextEdge = hNewOutgoingOpposite;
		pNewOutgoingOpposite.Face = FaceHandle.Invalid;
		pNewOutgoingOpposite.NextEdge = hNewIncomingOpposite;
		pNewIncomingOppsite.Face = FaceHandle.Invalid;
		pNewIncomingOppsite.NextEdge = hIncomingEdge;

		// Redirect all of the edges between the incoming edge and the outgoing edge to the new vertex
		var hCurrentEdge = hNewIncomingEdge;
		while ( hCurrentEdge != hNewOutgoingOpposite )
		{
			var pCurrentEdge = hCurrentEdge;
			pCurrentEdge.Vertex = hNewVertex;
			hCurrentEdge = GetOppositeHalfEdge( pCurrentEdge.NextEdge );
		}

		// If there are no longer any faces attached to the original incoming or outgoing edges remove them
		if ( GetFaceConnectedToHalfEdge( GetOppositeHalfEdge( hIncomingEdge ) ) == FaceHandle.Invalid )
		{
			RemoveEdge( GetFullEdgeForHalfEdge( hIncomingEdge ), true );
		}

		if ( GetFaceConnectedToHalfEdge( GetOppositeHalfEdge( hOutgoingEdge ) ) == FaceHandle.Invalid )
		{
			RemoveEdge( GetFullEdgeForHalfEdge( hOutgoingEdge ), true );
		}

		pOutNewIncomingEdge = hNewIncomingEdge;
		pOutNewOutGoingEdge = hNewOutgoingEdge;

		return hNewVertex;
	}

	private bool SplitEdgeList( IReadOnlyList<HalfEdgeHandle> pEdges, int nNumEdges, out HalfEdgeHandle[] pOutEdgesA, out HalfEdgeHandle[] pOutEdgesB )
	{
		pOutEdgesA = null;
		pOutEdgesB = null;

		if ( nNumEdges <= 0 )
			return false;

		// Build the list of half edges to split
		var edgeListA = new HalfEdgeHandle[nNumEdges + 2];
		edgeListA[0] = FindPreviousEdgeInFaceLoop( pEdges[0] );
		for ( int i = 0; i < nNumEdges; i++ )
			edgeListA[i + 1] = pEdges[i];
		edgeListA[nNumEdges + 1] = GetNextEdgeInFaceLoop( pEdges[nNumEdges - 1] );

		// Build the list of the opposite edges in reverse
		var edgeListB = new HalfEdgeHandle[nNumEdges + 2];
		edgeListB[0] = FindPreviousEdgeInFaceLoop( GetOppositeHalfEdge( pEdges[nNumEdges - 1] ) );
		for ( int i = 0; i < nNumEdges; i++ )
			edgeListB[nNumEdges - i] = GetOppositeHalfEdge( pEdges[i] );
		edgeListB[nNumEdges + 1] = GetNextEdgeInFaceLoop( edgeListB[nNumEdges] );

		// Split the list of edges
		for ( int iEdge = 0; iEdge < (edgeListA.Length - 1); ++iEdge )
		{
			SplitVertex( edgeListA[iEdge], edgeListA[iEdge + 1], out edgeListA[iEdge], out edgeListA[iEdge + 1] );
		}

		// Split the list of opposite edges
		for ( int iEdge = 0; iEdge < (edgeListB.Length - 1); ++iEdge )
		{
			SplitVertex( edgeListB[iEdge], edgeListB[iEdge + 1], out edgeListB[iEdge], out edgeListB[iEdge + 1] );
		}

		pOutEdgesA = new HalfEdgeHandle[nNumEdges];
		for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			pOutEdgesA[iEdge] = edgeListA[iEdge + 1];
		}

		pOutEdgesB = new HalfEdgeHandle[nNumEdges];
		for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			pOutEdgesB[iEdge] = edgeListB[edgeListB.Length - 2 - iEdge];
		}

		return true;
	}

	private bool SplitEdgeLoop( IReadOnlyList<HalfEdgeHandle> pEdges, int nNumEdges, out HalfEdgeHandle[] pOutEdgesA )
	{
		pOutEdgesA = null;

		if ( nNumEdges <= 0 )
			return false;

		// Build the list of half edges to split
		var edgeListA = new HalfEdgeHandle[nNumEdges];
		for ( int i = 0; i < nNumEdges; i++ )
			edgeListA[i] = pEdges[i];

		// Split the list of edges
		for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			int nEdgeA = iEdge;
			int nEdgeB = (iEdge + 1) % nNumEdges;
			SplitVertex( edgeListA[nEdgeA], edgeListA[nEdgeB], out edgeListA[nEdgeA], out edgeListA[nEdgeB] );
		}

		pOutEdgesA = new HalfEdgeHandle[nNumEdges];
		for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			pOutEdgesA[iEdge] = edgeListA[iEdge];
		}

		return true;
	}

	public bool SplitEdges( IReadOnlyList<HalfEdgeHandle> pEdges, out HalfEdgeHandle[] pOutNewEdgesA, out HalfEdgeHandle[] pOutNewEdgesB )
	{
		bool bEdgesSplit = false;

		var nNumEdges = pEdges.Count;

		pOutNewEdgesA = new HalfEdgeHandle[nNumEdges];
		pOutNewEdgesB = new HalfEdgeHandle[nNumEdges];

		for ( int i = 0; i < nNumEdges; ++i )
		{
			pOutNewEdgesA[i] = HalfEdgeHandle.Invalid;
			pOutNewEdgesB[i] = HalfEdgeHandle.Invalid;
		}

		// First group the edges into a set of islands
		FindEdgeIslands( pEdges, out var edgeIslands );

		var nNumIslands = edgeIslands.Count;
		for ( int iIsland = 0; iIsland < nNumIslands; ++iIsland )
		{
			var edgeList = edgeIslands[iIsland];

			HalfEdgeHandle[] newEdgesA = null;
			HalfEdgeHandle[] newEdgesB = null;

			// Classify the connectivity of the island, it must be a loop or list
			var nConnectivity = ClassifyEdgeListConnectivity( edgeList, edgeList.Count, out var sortedEdgeList );
			bool bIslandEdgesSplit = false;

			var sortedFullEdgeList = new HalfEdgeHandle[sortedEdgeList.Count];
			for ( int iEdge = 0; iEdge < sortedEdgeList.Count; ++iEdge )
			{
				sortedFullEdgeList[iEdge] = GetFullEdgeForHalfEdge( sortedEdgeList[iEdge] );
			}

			if ( nConnectivity == ComponentConnectivityType.List )
			{
				if ( SplitEdgeList( sortedEdgeList, sortedEdgeList.Count, out newEdgesA, out newEdgesB ) )
				{
					bIslandEdgesSplit = true;
				}
			}
			else if ( nConnectivity == ComponentConnectivityType.Loop )
			{
				if ( SplitEdgeLoop( sortedEdgeList, sortedEdgeList.Count, out newEdgesA ) )
				{
					bIslandEdgesSplit = true;
				}
			}

			if ( bIslandEdgesSplit )
			{
				for ( int iSortedEdge = 0; iSortedEdge < sortedEdgeList.Count; ++iSortedEdge )
				{
					var hSortedEdge = sortedFullEdgeList[iSortedEdge];

					for ( int iInputEdge = 0; iInputEdge < nNumEdges; ++iInputEdge )
					{
						if ( hSortedEdge == pEdges[iInputEdge] )
						{
							pOutNewEdgesA[iInputEdge] = GetFullEdgeForHalfEdge( newEdgesA[iSortedEdge] );

							if ( nConnectivity == ComponentConnectivityType.Loop )
							{
								// Splitting a loop only generates one new set of edges, the other set is the original edges
								pOutNewEdgesB[iInputEdge] = pEdges[iInputEdge];
							}
							else
							{
								pOutNewEdgesB[iInputEdge] = GetFullEdgeForHalfEdge( newEdgesB[iSortedEdge] );
							}

							break;
						}
					}
				}

				bEdgesSplit = true;
			}
		}

		return bEdgesSplit;
	}

	public bool ExtendEdges( IReadOnlyList<HalfEdgeHandle> pEdges, int nNumOriginalEdges, out List<HalfEdgeHandle> pOutNewEdges, out List<HalfEdgeHandle> pOutOriginalEdges, out List<VertexHandle> pOutNewVertices, out List<VertexHandle> pOutOriginalVertices )
	{
		pOutNewEdges = new List<HalfEdgeHandle>();
		pOutNewEdges.EnsureCapacity( nNumOriginalEdges );

		pOutOriginalEdges = new List<HalfEdgeHandle>();
		pOutOriginalEdges.EnsureCapacity( nNumOriginalEdges );

		pOutNewVertices = new List<VertexHandle>();
		pOutNewVertices.EnsureCapacity( nNumOriginalEdges );

		pOutOriginalVertices = new List<VertexHandle>();
		pOutOriginalVertices.EnsureCapacity( nNumOriginalEdges );

		// Build a list of the open half edges which are to be extended.
		var edgesToExtend = new List<HalfEdgeHandle>( nNumOriginalEdges );
		for ( var iEdge = 0; iEdge < nNumOriginalEdges; ++iEdge )
		{
			GetHalfEdgesConnectedToFullEdge( pEdges[iEdge], out var hHalfEdgeA, out var hHalfEdgeB );
			if ( GetFaceConnectedToHalfEdge( hHalfEdgeA ) == FaceHandle.Invalid )
			{
				edgesToExtend.Add( hHalfEdgeA );
			}
			else if ( GetFaceConnectedToHalfEdge( hHalfEdgeB ) == FaceHandle.Invalid )
			{
				edgesToExtend.Add( hHalfEdgeB );
			}
		}

		if ( edgesToExtend.Count <= 0 )
			return false;

		var connectedEdgeSet = new List<HalfEdgeHandle>( edgesToExtend.Count );

		while ( edgesToExtend.Count > 0 )
		{
			// Find all of the edges in the set to extend that are connected to the first edge
			connectedEdgeSet.Clear();
			var hCurrentEdge = HalfEdgeHandle.Invalid;
			var hStartEdge = edgesToExtend.First();
			var hPrevEdge = hStartEdge;

			do
			{
				hCurrentEdge = hPrevEdge;
				hPrevEdge = FindPreviousEdgeInFaceLoop( hCurrentEdge );
			}
			while ( (hPrevEdge != hStartEdge) && edgesToExtend.Contains( hPrevEdge ) );

			hStartEdge = hCurrentEdge;
			do
			{
				connectedEdgeSet.Add( hCurrentEdge );
				hCurrentEdge = GetNextEdgeInFaceLoop( hCurrentEdge );
			}
			while ( (hCurrentEdge != hStartEdge) && edgesToExtend.Contains( hCurrentEdge ) );

			// Create the vertices and faces for each edge
			var nNumConnectedEdges = connectedEdgeSet.Count;
			if ( nNumConnectedEdges > 0 )
			{
				var hPrevNewVertex = VertexHandle.Invalid;
				var hFirstVertex = VertexHandle.Invalid;
				var hFirstOriginalVertex = VertexHandle.Invalid;

				for ( var iEdge = 0; iEdge < nNumConnectedEdges; ++iEdge )
				{
					var hOriginalHalfEdge = connectedEdgeSet[iEdge];

					var hVertexA = hOriginalHalfEdge.OppositeEdge.Vertex;
					var hVertexB = hOriginalHalfEdge.Vertex;
					var hNewVertex = VertexHandle.Invalid;

					if ( hVertexB == hFirstOriginalVertex )
					{
						hNewVertex = hFirstVertex;
					}

					if ( hNewVertex == VertexHandle.Invalid )
					{
						hNewVertex = AllocateVertex( Vertex.Invalid, hVertexB );

						pOutNewVertices.Add( hNewVertex );
						pOutOriginalVertices.Add( hVertexB );
					}

					if ( hPrevNewVertex == VertexHandle.Invalid )
					{
						hPrevNewVertex = AllocateVertex( Vertex.Invalid, hVertexA );
						hFirstVertex = hPrevNewVertex;
						hFirstOriginalVertex = hVertexA;

						pOutNewVertices.Add( hPrevNewVertex );
						pOutOriginalVertices.Add( hVertexA );
					}

					AddFace( hVertexA, hVertexB, hNewVertex, hPrevNewVertex );

					var hNewEdge = FindFullEdgeConnectingVertices( hNewVertex, hPrevNewVertex );
					Assert.True( hNewEdge != HalfEdgeHandle.Invalid );
					if ( hNewEdge != HalfEdgeHandle.Invalid )
					{
						pOutNewEdges.Add( hNewEdge );
					}

					hPrevNewVertex = hNewVertex;

					var hOriginalEdge = GetFullEdgeForHalfEdge( hOriginalHalfEdge );
					pOutOriginalEdges.Add( hOriginalEdge );

					// Remove the edge from the list of edges which still need to be extended.
					edgesToExtend.Remove( hOriginalHalfEdge );
				}
			}
		}

		return true;
	}

	public int ComputeNumOpenEdgesInVertexLoop( VertexHandle hVertex )
	{
		if ( !hVertex.IsValid )
			return 0;

		var nNumOpenEdges = 0;

		// Iterate over all of the edges emanating from the vertex and determine 
		// if they are connected to a face. If not increment the open edge count.
		var hEdge = hVertex.Edge;
		if ( hVertex.Edge == HalfEdgeHandle.Invalid )
			return 0;

		do
		{
			if ( hEdge.Face == FaceHandle.Invalid )
				++nNumOpenEdges;

			hEdge = GetOppositeHalfEdge( hEdge ).NextEdge;
		}
		while ( hEdge != hVertex.Edge );

		return nNumOpenEdges;
	}

	public HalfEdgeHandle FindOpenOppositeEdgeInVertexLoop( VertexHandle hVertex )
	{
		if ( !hVertex.IsValid )
			return HalfEdgeHandle.Invalid;

		if ( hVertex.Edge == HalfEdgeHandle.Invalid )
			return HalfEdgeHandle.Invalid;

		var hCurrentEdge = hVertex.Edge;

		do
		{
			var hOppositeEdge = GetOppositeHalfEdge( hCurrentEdge );
			if ( hOppositeEdge.Face == FaceHandle.Invalid )
				return hOppositeEdge;

			hCurrentEdge = hOppositeEdge.NextEdge;
		}
		while ( hCurrentEdge != hVertex.Edge );

		return HalfEdgeHandle.Invalid;
	}

	public HalfEdgeHandle FindOppositeEdgeWithNextEdgeInVertexLoop( VertexHandle hVertex, HalfEdgeHandle hNextEdge )
	{
		if ( !hVertex.IsValid )
			return HalfEdgeHandle.Invalid;

		if ( hVertex.Edge == HalfEdgeHandle.Invalid )
			return HalfEdgeHandle.Invalid;

		var hCurrentEdge = hVertex.Edge;

		do
		{
			var hOppositeEdge = GetOppositeHalfEdge( hCurrentEdge );
			if ( hOppositeEdge.NextEdge == hNextEdge )
				return hOppositeEdge;

			hCurrentEdge = hOppositeEdge.NextEdge;
		}
		while ( hCurrentEdge != hVertex.Edge );

		return HalfEdgeHandle.Invalid;
	}

	private HalfEdgeHandle ConstructHalfEdgePair( VertexHandle hVertexA, VertexHandle hVertexB )
	{
		// Should never be trying to add an edge which already exists
		Assert.False( FindHalfEdgeConnectingVertices( hVertexA, hVertexB ).IsValid );
		Assert.False( FindHalfEdgeConnectingVertices( hVertexB, hVertexA ).IsValid );

		// Construct both halves of the half edge pair
		if ( AllocateHalfEdgePair( out var hEdgeAB, out var hEdgeBA ) )
		{
			hEdgeAB.Vertex = hVertexB;
			hEdgeBA.Vertex = hVertexA;
		}

		return hEdgeAB;
	}

	private bool AllocateHalfEdgePair( out HalfEdgeHandle hHalfEdgeA, out HalfEdgeHandle hHalfEdgeB )
	{
		int halfEdgeCount = HalfEdgeCount;

		var edgeA = new HalfEdge
		{
			Vertex = -1,
			OppositeEdge = halfEdgeCount + 1,
			NextEdge = halfEdgeCount + 1,
			Face = -1,
		};

		var edgeB = new HalfEdge
		{
			Vertex = -1,
			OppositeEdge = halfEdgeCount,
			NextEdge = halfEdgeCount,
			Face = -1,
		};

		hHalfEdgeA = AllocateHalfEdge( edgeA );
		hHalfEdgeB = AllocateHalfEdge( edgeB );

		return true;
	}

	private void AttachEdgesToFace( FaceHandle hFace, HalfEdgeHandle[] pAllEdges, int nNumEdges )
	{
		Assert.True( hFace.IsValid );
		if ( !hFace.IsValid )
			return;

		var hEdge = pAllEdges[nNumEdges - 1];
		for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
		{
			var hNextEdge = pAllEdges[iEdge];
			var hOppositeEdge = GetOppositeHalfEdge( hEdge );
			var hNextOppositeEdge = GetOppositeHalfEdge( hNextEdge );

			Assert.True( hNextOppositeEdge.Vertex == hEdge.Vertex );

			// Assign the face to the edge. It is important this is done first
			// so that this edge doesn't turn up in the open edge search.
			hEdge.Face = hFace;

			if ( hOppositeEdge.Face == FaceHandle.Invalid )
			{
				HalfEdgeHandle hInsertAfterEdge;

				if ( hNextOppositeEdge.Face != FaceHandle.Invalid )
				{
					hInsertAfterEdge = FindOppositeEdgeWithNextEdgeInVertexLoop( hEdge.Vertex, hNextEdge );
				}
				else
				{
					hInsertAfterEdge = FindOpenOppositeEdgeInVertexLoop( hEdge.Vertex );
				}

				if ( hInsertAfterEdge != HalfEdgeHandle.Invalid )
				{
					hEdge.NextEdge = hInsertAfterEdge.NextEdge;
					hInsertAfterEdge.NextEdge = hEdge.OppositeEdge;
				}
			}

			// Check to see if the vertex has been assigned an edge yet, if not assign it the next 
			// edge, since the edge assigned to a vertex is the edge starting at the vertex.
			var hVertex = hEdge.Vertex;
			if ( hVertex.Edge == HalfEdgeHandle.Invalid )
			{
				hVertex.Edge = hNextEdge;
			}

			if ( hNextOppositeEdge.Face == FaceHandle.Invalid )
			{
				hNextOppositeEdge.NextEdge = hEdge.NextEdge;
				hEdge.NextEdge = hNextEdge;
			}

			Assert.True( hEdge.NextEdge == hNextEdge );

			hEdge = hNextEdge;
		}

		// Make the face points to the last edge so that that when a face is created 
		// the vertex ordering will match the order of the provided vertices.
		hFace.Edge = pAllEdges[nNumEdges - 1];

		Assert.True( CheckFaceIntegrity( hFace ) );
	}

	private bool CheckFaceIntegrity( FaceHandle hFace, bool bAssert = true )
	{
		Assert.True( hFace.IsValid || (bAssert == false) );
		if ( !hFace.IsValid )
			return false;

		var hFirstEdge = hFace.Edge;
		Assert.True( hFirstEdge.IsValid || (bAssert == false) );
		if ( !hFirstEdge.IsValid )
			return false;

		var hEdge = hFace.Edge;
		do
		{
			Assert.True( hEdge.IsValid || (bAssert == false) );
			if ( !hEdge.IsValid )
				return false;

			Assert.True( hEdge.Face == hFace || (bAssert == false) );
			if ( hEdge.Face != hFace )
				return false;

			hEdge = hEdge.NextEdge;
		}
		while ( hEdge != hFace.Edge );

		return true;
	}

	private bool AddFace( VertexHandle[] pVerticesA, out FaceHandle hFace )
	{
		hFace = FaceHandle.Invalid;

		var nNumVertices = pVerticesA.Length;
		if ( nNumVertices < 3 )
			return false;

		var pEdgeHandles = new HalfEdgeHandle[nNumVertices];
		var pVerticesB = new VertexHandle[nNumVertices];
		for ( int iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			pVerticesB[iVertex] = pVerticesA[(iVertex + 1) % nNumVertices];
		}

		// Find all of the existing edges and ensure they are
		// open and make sure that the new edges can be added.
		for ( int iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			pEdgeHandles[iVertex] = FindHalfEdgeConnectingVertices( pVerticesA[iVertex], pVerticesB[iVertex] );

			var pEdge = pEdgeHandles[iVertex];
			if ( pEdge.IsValid )
			{
				// Cannot construct a face using an edge which is already in use by another face
				if ( pEdge.Face != FaceHandle.Invalid )
				{
					return false;
				}
			}
			else if ( pVerticesB[iVertex].Edge != HalfEdgeHandle.Invalid )
			{
				int nNumOpenEdges = ComputeNumOpenEdgesInVertexLoop( pVerticesB[iVertex] );

				// If a new edge is being added to a vertex which already has edges attached there
				// must be at least on open edge, otherwise there is nowhere to insert the new edge.
				if ( nNumOpenEdges == 0 )
				{
					return false;
				}

				// If there are two open edges then we must ensure that the next edge being added is an
				// existing edge, otherwise it will be ambiguous as to where the face is to be added.
				if ( nNumOpenEdges >= 2 )
				{
					if ( !FindHalfEdgeConnectingVertices( pVerticesB[iVertex], pVerticesB[(iVertex + 1) % nNumVertices] ).IsValid )
					{
						return false;
					}
				}
			}
		}

		// If two neighboring edges are existing edges they must be directly 
		// connected, they cannot have additional edges between them.
		for ( int iEdge = 0; iEdge < nNumVertices; ++iEdge )
		{
			var hEdge = pEdgeHandles[iEdge];
			var hNextEdge = pEdgeHandles[(iEdge + 1) % nNumVertices];

			if ( hEdge.IsValid && hNextEdge.IsValid )
			{
				if ( hEdge.NextEdge != hNextEdge )
				{
					return false;
				}
			}
		}

		hFace = AllocateFace( Face.Invalid );

		// Create the new edges
		for ( int iVertex = 0; iVertex < nNumVertices; ++iVertex )
		{
			if ( !pEdgeHandles[iVertex].IsValid )
			{
				// Check for an existing edge connecting the vertices in the opposite direction,
				// this may occur if there is an interior edge in the face.
				for ( int iEdge = 0; iEdge < iVertex; ++iEdge )
				{
					GetVerticesConnectedToHalfEdge( pEdgeHandles[iEdge], out var hVertexA, out var hVertexB );
					if ( (hVertexA == pVerticesB[iVertex]) && (hVertexB == pVerticesA[iVertex]) )
					{
						pEdgeHandles[iVertex] = pEdgeHandles[iEdge].OppositeEdge;
					}
				}

				if ( !pEdgeHandles[iVertex].IsValid )
				{
					pEdgeHandles[iVertex] = ConstructHalfEdgePair( pVerticesA[iVertex], pVerticesB[iVertex] );
				}

				Assert.True( pEdgeHandles[iVertex].IsValid );
			}
		}

		// Attach the edges to the face
		AttachEdgesToFace( hFace, pEdgeHandles, nNumVertices );

		return true;
	}

	private void DetachEdgeFromVertex( HalfEdgeHandle hEdge, bool bRemoveFreeVerts )
	{
		if ( !hEdge.IsValid )
			return;

		if ( hEdge.Vertex == VertexHandle.Invalid )
			return;

		// Get the opposite edge and the vertex from which the edge originates
		var hOppositeEdge = hEdge.OppositeEdge;
		var hVertex = hOppositeEdge.Vertex;

		// Determine if the this is the only edge attached to the vertex. If not remove the 
		// edge from the loop of edges going around the vertex, otherwise update the vertex 
		// edge reference and remove the vertex if remove free vertices is specified.
		if ( hOppositeEdge.NextEdge != hEdge )
		{
			var hPreviousEdge = FindPreviousEdgeInVertexLoop( hEdge );
			Assert.True( hPreviousEdge.OppositeEdge.NextEdge == hEdge );

			hPreviousEdge.OppositeEdge.NextEdge = hOppositeEdge.NextEdge;

			// Update the edge the vertex refers to to ensure 
			// it is not still referring to the that was detached.
			hVertex.Edge = hOppositeEdge.NextEdge;

			// Now make the opposite edge loop back
			hOppositeEdge.NextEdge = hEdge;
		}
		else
		{
			Assert.True( ComputeNumEdgesConnectedToVertex( hVertex ) == 1 );

			// If this is the only edge connected to the
			// vertex, the vertex should refer to it.
			Assert.True( (hVertex.Edge == hEdge) || hVertex.Edge == HalfEdgeHandle.Invalid );

			// Set the vertex as being disconnected
			hVertex.Edge = HalfEdgeHandle.Invalid;

			// Remove the vertex from the mesh entirely if remove free vertices is true
			if ( bRemoveFreeVerts )
			{
				RemoveVertex( hVertex, bRemoveFreeVerts );
			}
		}
	}

	private struct FaceEdgePair
	{
		public FaceHandle Face;
		public HalfEdgeHandle IncomingEdge;
		public HalfEdgeHandle OutgoingEdge;
	};

	public bool RemoveVertex( VertexHandle hVertex, bool bRemoveFreeVerts )
	{
		if ( !hVertex.IsValid )
			return false;

		var bValidEdge = hVertex.Edge != HalfEdgeHandle.Invalid;

		if ( bValidEdge )
		{
			// Count the number of edges emanating from the vertex
			var nVertexNumEdges = 0;
			var hCurrentEdge = hVertex.Edge;
			HalfEdgeHandle hPreviousAdjEdge;
			do
			{
				++nVertexNumEdges;
				hPreviousAdjEdge = hCurrentEdge.OppositeEdge;
				hCurrentEdge = hPreviousAdjEdge.NextEdge;
			}
			while ( hCurrentEdge != hVertex.Edge );

			// Build a list of the pairs of edges going in and out of 
			// the specified vertex for each face connected to the vertex.
			var pFaceEdgePairs = new FaceEdgePair[nVertexNumEdges];
			var nNumPairs = 0;

			hCurrentEdge = hVertex.Edge;
			do
			{
				Assert.True( hPreviousAdjEdge.Vertex == hVertex );
				Assert.True( hPreviousAdjEdge.NextEdge == hCurrentEdge );
				Assert.True( hPreviousAdjEdge.Face == hCurrentEdge.Face );

				if ( hCurrentEdge.Face != FaceHandle.Invalid )
				{
					var faceEdgePair = pFaceEdgePairs[nNumPairs];
					faceEdgePair.Face = hCurrentEdge.Face;
					faceEdgePair.IncomingEdge = hPreviousAdjEdge;
					faceEdgePair.OutgoingEdge = hCurrentEdge;
					pFaceEdgePairs[nNumPairs] = faceEdgePair;
					nNumPairs++;
				}

				hPreviousAdjEdge = hCurrentEdge.OppositeEdge;
				hCurrentEdge = hPreviousAdjEdge.NextEdge;
			}
			while ( hCurrentEdge != hVertex.Edge );

			Assert.True( nNumPairs <= nVertexNumEdges );

			// If the face is a triangle removing the vertex would leave
			// it in an invalid state, so the whole face should be removed.
			for ( var iPair = 0; iPair < nNumPairs; ++iPair )
			{
				var pair = pFaceEdgePairs[iPair];

				if ( pair.OutgoingEdge.NextEdge.NextEdge == pair.IncomingEdge )
				{
					RemoveFace( pair.Face, bRemoveFreeVerts );
					pair.Face = FaceHandle.Invalid;
					pFaceEdgePairs[iPair] = pair;
				}
			}

			// Replace the incoming and outgoing edges of the vertex with a 
			// single edge connecting the proceeding and following vertices. 
			for ( var iPair = 0; iPair < nNumPairs; ++iPair )
			{
				var pair = pFaceEdgePairs[iPair];

				if ( pair.Face != FaceHandle.Invalid )
				{
					if ( ReplaceFaceEdges( pair.Face, pair.IncomingEdge, pair.OutgoingEdge, bRemoveFreeVerts ) == false )
					{
						RemoveFace( pair.Face, bRemoveFreeVerts );
						pair.Face = FaceHandle.Invalid;
						pFaceEdgePairs[iPair] = pair;
					}
				}
			}
		}

		// If remove free vertices was specified, this vertex will
		// already have been removed if it was connected to anything.
		if ( !bValidEdge || !bRemoveFreeVerts )
		{
			FreeVertex( hVertex );
		}

		return true;
	}

	private bool ReplaceFaceEdges( FaceHandle hFace, HalfEdgeHandle hIncomingEdge, HalfEdgeHandle hOutgoingEdge, bool bRemoveFreeVerts )
	{
		Assert.True( hFace.IsValid && hIncomingEdge.IsValid && hOutgoingEdge.IsValid );
		if ( !hFace.IsValid || !hIncomingEdge.IsValid || !hOutgoingEdge.IsValid )
			return false;

		// Both edges must belong to the face
		Assert.True( (hIncomingEdge.Face == hFace) && (hOutgoingEdge.Face == hFace) );
		if ( (hIncomingEdge.Face != hFace) || (hOutgoingEdge.Face != hFace) )
			return false;

		// The outgoing edge must be the next edge in the loop from the incoming edge.
		Assert.True( hIncomingEdge.NextEdge == hOutgoingEdge );
		if ( hIncomingEdge.NextEdge != hOutgoingEdge )
			return false;

		// Count the number of edges the face has, it must have more than 3 edges
		var nFaceNumEdges = ComputeNumEdgesInFace( hFace );
		Assert.True( nFaceNumEdges > 3 );
		if ( nFaceNumEdges <= 3 )
			return false;

		var hIncomingOppositeEdge = hIncomingEdge.OppositeEdge;

		// The new edge must connect two different valid vertices
		var hVertexA = hIncomingOppositeEdge.Vertex;
		var hVertexB = hOutgoingEdge.Vertex;
		Assert.True( hVertexA.IsValid && hVertexB.IsValid && (hVertexA != hVertexB) );
		if ( !hVertexA.IsValid || !hVertexB.IsValid || (hVertexA == hVertexB) )
			return false;

		// Build a list of all of the edges in the face excluding the ones that are going to be removed.
		var pEdgeList = new HalfEdgeHandle[nFaceNumEdges];
		int nNumEdges = 0;
		var hCurrentEdge = hOutgoingEdge.NextEdge;
		do
		{
			pEdgeList[nNumEdges++] = hCurrentEdge;
			hCurrentEdge = hCurrentEdge.NextEdge;
		}
		while ( hCurrentEdge != hIncomingEdge );
		Assert.True( nNumEdges == (nFaceNumEdges - 2) );

		// Check to see if there is already a connecting edge. This can happen in the case where the 
		// edges are part of an open triangle loop or in the case where both the incoming and outgoing 
		// edges are internal to the face (both half edges of the pair reference the same face) when 
		// replacing the second face edge pair.
		var hConnectingEdge = FindHalfEdgeConnectingVertices( hVertexA, hVertexB );
		if ( hConnectingEdge.IsValid )
		{
			// If both the incoming and outgoing edges both have a face attached this should be the case
			// where the edges are part of a triangle loop. If so the next edge of opposite edge of the
			// incoming edge should be the connecting edge and the next edge of the connecting should be
			// the opposite edge of the outgoing edge. This may occur if there is a face attached to one
			// or both to the vertices and is inside what appeared to be an triangle loop, making it not
			// actually a triangle loop.
			if ( (hIncomingEdge.Face != FaceHandle.Invalid) &&
				 (hOutgoingEdge.Face != FaceHandle.Invalid) )
			{
				if ( hIncomingEdge.OppositeEdge.NextEdge != hConnectingEdge )
					return false;

				if ( hConnectingEdge.NextEdge != hOutgoingEdge.OppositeEdge )
					return false;
			}
		}

		hIncomingEdge.Face = FaceHandle.Invalid;
		hOutgoingEdge.Face = FaceHandle.Invalid;

		if ( hConnectingEdge.IsValid )
		{
			// Copy the data from the face vertex at the end of the outgoing edge to the face vertex at
			// the end of the connecting edge, since that is the vertex replacing the vertex at the end
			// of the outgoing edge.
			CopyFaceVertexData( hConnectingEdge, hOutgoingEdge );
		}
		else
		{
			// If an existing connecting edge was not found, construct a new edge which 
			// will replace the two removed edges and connect vertex a to vertex b.
			pEdgeList[nNumEdges++] = ConstructHalfEdgePair( hVertexA, hVertexB );
			Assert.True( nNumEdges == (nFaceNumEdges - 1) );
		}

		if ( hIncomingEdge.OppositeEdge.Face == FaceHandle.Invalid )
		{
			RemoveHalfEdgePair( hIncomingEdge, bRemoveFreeVerts );
		}
		else
		{
			ClearEdgeData( hIncomingEdge );
		}

		if ( hOutgoingEdge.OppositeEdge.Face == FaceHandle.Invalid )
		{
			RemoveHalfEdgePair( hOutgoingEdge, bRemoveFreeVerts );
		}
		else
		{
			ClearEdgeData( hOutgoingEdge );
		}

		if ( hConnectingEdge.IsValid )
		{
			hConnectingEdge.Face = hFace;
			hFace.Edge = hConnectingEdge;
		}
		else
		{
			// Detach all of the remaining edges from the face.
			for ( var iEdge = 0; iEdge < (nNumEdges - 1); ++iEdge )
			{
				var hEdge = pEdgeList[iEdge];
				hEdge.Face = FaceHandle.Invalid;

				if ( hEdge.OppositeEdge.Face == FaceHandle.Invalid )
				{
					DetachEdgeFromVertex( pEdgeList[iEdge], false );
					DetachEdgeFromVertex( hEdge.OppositeEdge, false );
				}
			}

			hFace.Edge = HalfEdgeHandle.Invalid;

			// Attach all the edges to the face
			AttachEdgesToFace( hFace, pEdgeList, nNumEdges );

			for ( var iEdge = 0; iEdge < (nNumEdges - 1); ++iEdge )
			{
				var hEdge = pEdgeList[iEdge];
				if ( hEdge.OppositeEdge.Face == FaceHandle.Invalid )
				{
					ClearEdgeData( hEdge.OppositeEdge );
				}
			}
		}

		// Remove any edges which are now loose edges in the face
		RemoveLooseEdgesInFace( hFace );

		return true;
	}

	public bool RemoveEdge( HalfEdgeHandle hFullEdge, bool bRemoveFreeVerts )
	{
		return RemoveHalfEdgePair( hFullEdge, bRemoveFreeVerts );
	}

	public bool BevelFaces( FaceHandle[] pFaces, int nNumFaces, bool bCreateConnectingFaces, out List<FaceHandle> pNewFaces, out List<FaceHandle> pOutConnectingFaces, out List<FaceHandle> pOutConnectingTargetFaces, out List<FaceHandle> pOutConnectingOriginFaces )
	{
		pNewFaces = null;
		pOutConnectingFaces = null;
		pOutConnectingTargetFaces = null;
		pOutConnectingOriginFaces = null;

		// Verify that all of the faces are valid faces in the mesh
		for ( var iFace = 0; iFace < nNumFaces; ++iFace )
		{
			var hFace = pFaces[iFace];
			if ( !hFace.IsValid )
				return false;
		}

		// Get the unique set of vertices used by the faces
		FindVerticesConnectedToFaces( pFaces, nNumFaces, out var originalVertices );
		var nTotalVertexCount = originalVertices.Length;

		// Duplicate the vertices
		var newVertices = AddVertices( nTotalVertexCount ).ToArray();

		pNewFaces = new List<FaceHandle>( nNumFaces );

		// Create the new faces using the duplicated vertices
		var newFaceVertices = new List<VertexHandle>();
		var nNumFacesBeveled = 0;
		var nTotalNumEdges = 0;

		for ( var iFace = 0; iFace < nNumFaces; ++iFace )
		{
			pNewFaces.Add( FaceHandle.Invalid );
			var hFace = pFaces[iFace];

			// Build a list of the new vertices to be used by the new face
			newFaceVertices.Clear();

			var bFoundAllVertices = true;
			var hEdge = hFace.Edge;
			do
			{
				var hNextEdge = hEdge.NextEdge;

				// Is this vertex connected to multiple faces in the set?
				GetFacesConnectedToVertex( hEdge.Vertex, out var facesConnectedToVertex );
				var nNumFacesInSetConnectedToVertex = 0;
				for ( int iSetFace = 0; iSetFace < nNumFaces; ++iSetFace )
				{
					var hSetFace = pFaces[iSetFace];
					if ( facesConnectedToVertex.Contains( hSetFace ) )
						++nNumFacesInSetConnectedToVertex;
				}

				Assert.True( nNumFacesInSetConnectedToVertex >= 1 );

				// If the vertex is connected to multiple faces in the set check to see the current face
				// shares an edge with any of the other faces in the set that is connected to the vertex.
				var bCreateUniqueVertex = false;
				if ( nNumFacesInSetConnectedToVertex > 1 )
				{
					bCreateUniqueVertex = true;
					for ( int iSetFace = 0; iSetFace < nNumFaces; ++iSetFace )
					{
						if ( (hEdge.OppositeEdge.Face == pFaces[iSetFace]) ||
							 (hNextEdge.OppositeEdge.Face == pFaces[iSetFace]) )
						{
							bCreateUniqueVertex = false;
							break;
						}
					}
				}

				// If the face uses a vertex which is shared with other faces but does not share any 
				// edges a unique vertex should be used for this face.
				if ( bCreateUniqueVertex )
				{
					newFaceVertices.Add( AddVertex() );
				}
				else
				{
					var nVertexIndex = Array.IndexOf( originalVertices, hEdge.Vertex );
					Assert.True( nVertexIndex >= 0 );
					if ( nVertexIndex < 0 )
					{
						bFoundAllVertices = false;
						break;
					}

					newFaceVertices.Add( newVertices[nVertexIndex] );
				}

				hEdge = hEdge.NextEdge;
			}
			while ( hEdge != hFace.Edge );

			// Create a new face to replace the original using a new set of vertices
			if ( bFoundAllVertices )
			{
				// It is possible that adding the face will fail if there was a topological arrangement 
				// such as more than two faces sharing a vertex without sharing any edges that cannot
				// be duplicated by adding faces.
				if ( AddFace( out var newFace, newFaceVertices.ToArray() ) )
				{
					pNewFaces[iFace] = newFace;

					// We are assuming the vertex ordering of the face will match the ordering of the provided vertices
					Assert.True( GetFirstEdgeInFaceLoop( pNewFaces[iFace] ).Vertex == newFaceVertices[0] );

					++nNumFacesBeveled;
					nTotalNumEdges += newFaceVertices.Count;
				}
			}
		}

		// Remove all of the old faces but leave the old vertices 
		// and track which vertices belonged to which faces.
		var oldFaceVertices = new VertexHandle[nNumFaces][];

		for ( var iFace = 0; iFace < nNumFaces; ++iFace )
		{
			var hOldFace = pFaces[iFace];
			var hNewFace = pNewFaces[iFace];
			if ( !hNewFace.IsValid )
				continue;

			// Save the list of vertices connected to the original face
			GetVerticesConnectedToFace( hOldFace, out var faceVertices );
			oldFaceVertices[iFace] = faceVertices;

			// Remove the original face
			RemoveFace( hOldFace, false );
		}

		pOutConnectingFaces = new( nTotalNumEdges );
		pOutConnectingTargetFaces = new( nTotalNumEdges );
		pOutConnectingOriginFaces = new( nTotalNumEdges );

		if ( bCreateConnectingFaces )
		{
			// Add the faces that connect the edges of the old face to the edges of the new face 	
			var connectingFaceVertices = new VertexHandle[4];

			for ( var iFace = 0; iFace < nNumFaces; ++iFace )
			{
				var hNewFace = pNewFaces[iFace];
				if ( !hNewFace.IsValid )
					continue;

				// Get the list of vertices in the new face.
				GetVerticesConnectedToFace( hNewFace, out var faceVertices );

				var nVertexCount = faceVertices.Length;
				for ( int iVertexA = (nVertexCount - 1), iVertexB = 0; iVertexB < nVertexCount; iVertexA = iVertexB++ )
				{
					connectingFaceVertices[0] = oldFaceVertices[iFace][iVertexA];
					connectingFaceVertices[1] = oldFaceVertices[iFace][iVertexB];
					connectingFaceVertices[2] = faceVertices[iVertexB];
					connectingFaceVertices[3] = faceVertices[iVertexA];

					// This will fail for edges that are shared in the set of new faces
					if ( AddFace( out var hConnectingFace, connectingFaceVertices ) )
					{
						if ( hConnectingFace.IsValid )
						{
							pOutConnectingFaces.Add( hConnectingFace );
							pOutConnectingTargetFaces.Add( hNewFace );

							var hOriginFace = FindFaceWithEdgeConnectingVertices( oldFaceVertices[iFace][iVertexB], oldFaceVertices[iFace][iVertexA] );
							pOutConnectingOriginFaces.Add( hOriginFace );
						}
					}
				}
			}
		}

		// Check to see if any of the original vertices no longer have any edges attached and remove them.
		for ( var iVertex = 0; iVertex < nTotalVertexCount; ++iVertex )
		{
			var hVertex = originalVertices[iVertex];
			var nNumEdges = ComputeNumEdgesConnectedToVertex( hVertex );
			if ( nNumEdges == 0 )
			{
				RemoveVertex( hVertex, false );
			}
		}

		// Remove any of the new vertices that were not used, this can happen if there is a vertex 
		// which is shared by two or more faces that don't share any edges.
		for ( var iVertex = 0; iVertex < newVertices.Length; ++iVertex )
		{
			var hVertex = newVertices[iVertex];
			int nNumEdges = ComputeNumEdgesConnectedToVertex( hVertex );
			if ( nNumEdges == 0 )
			{
				RemoveVertex( hVertex, false );
			}
		}

		return nNumFacesBeveled == nNumFaces;
	}

	private void ClearEdgeData( HalfEdgeHandle hEdge )
	{
		if ( !hEdge.IsValid )
			return;
	}

	private void CopyFaceVertexData( HalfEdgeHandle hDstHalfEdge, HalfEdgeHandle hSrcHalfEdge )
	{
		if ( !hDstHalfEdge.IsValid )
			return;

		if ( !hSrcHalfEdge.IsValid )
			return;
	}

	public bool RemoveFace( FaceHandle hFace, bool bRemoveFreeVerts )
	{
		if ( !hFace.IsValid )
			return false;

		var hFirstEdge = hFace.Edge;
		if ( hFirstEdge.IsValid && (hFirstEdge.Face == hFace) )
		{
			// Count the number of edges around polygon
			var nNumEdges = 0;
			var hEdge = hFace.Edge;
			do
			{
				hEdge = hEdge.NextEdge;
				++nNumEdges;
			}
			while ( hEdge != hFace.Edge );

			// Build the list of edges
			var pEdgeList = new HalfEdgeHandle[nNumEdges]; ;
			var nEdge = 0;
			hEdge = hFace.Edge;
			do
			{
				pEdgeList[nEdge++] = hEdge;
				hEdge = hEdge.NextEdge;
			}
			while ( hEdge != hFace.Edge );
			Assert.True( nEdge == nNumEdges );

			// Walk all of the edges of polygon, if an edge is only attached to the face being removed 
			// (its opposite edge is not attached to a face ) the edge should be removed.
			for ( var iEdge = 0; iEdge < nNumEdges; ++iEdge )
			{
				var hCurrentEdge = pEdgeList[iEdge];

				// Remove the edge's reference to this face.
				hCurrentEdge.Face = FaceHandle.Invalid;

				// If the opposite edge is open remove the edge since after removing this face it would no 
				// longer meet the requirement of all half edge pairs being attached to at least one face.
				// Note that if there is an interior edge it will appear in the list twice, once for 
				// each half edge, the first time it will remove the face from the half edge resulting in
				// it being removed when the second half edge is encountered.
				var hOppositeEdge = hCurrentEdge.OppositeEdge;
				if ( hOppositeEdge.Face == FaceHandle.Invalid )
				{
					RemoveHalfEdgePair( hCurrentEdge.OppositeEdge, bRemoveFreeVerts );
				}
			}
		}

		FreeFace( hFace );

		return true;
	}

	private bool RemoveHalfEdgePair( HalfEdgeHandle hEdge, bool bRemoveFreeVerts )
	{
		if ( !hEdge.IsValid )
			return false;

		var hAdjEdge = hEdge.OppositeEdge;
		var hOppositeEdge = hAdjEdge;

		// Determine if the edge is a loose edge, in this case the face connected to the edge should not
		// be removed, but needs to be updated so that it doesn't refer to the edge once it is removed.
		var bLooseEdge = IsLooseEdge( GetFullEdgeForHalfEdge( hEdge ) );

		if ( (hEdge.Face.IsValid || hOppositeEdge.Face.IsValid) && (bLooseEdge == false) )
		{
			// Remove the faces attached to the edge and its opposite edge. Note this will
			// result in RemoveFace() calling RemoveEdge when no more faces are attached 
			// to the edge, so we don't actually remove the edge directly here.
			var hFace = hEdge.Face;
			var hAdjFace = hOppositeEdge.Face;
			RemoveFace( hFace, bRemoveFreeVerts );
			RemoveFace( hAdjFace, bRemoveFreeVerts );

			// Note: It is possible that the edge is corrupt and the face it refers to does not refer 
			// to it, in this case the edge may not have been removed along with the face, so free the
			// edge here if it is still in the mesh.
			RemoveHalfEdgePair( hEdge, bRemoveFreeVerts );
		}
		else
		{
			// If the edge is a loose edge which is connected to a face which will not be
			// removed make sure that face is not referring to this edge or its opposite edge
			var hFace = hEdge.Face;
			if ( bLooseEdge && hFace.IsValid )
			{
				var hNextFaceEdge = hFace.Edge;
				while ( (hNextFaceEdge == hEdge) || (hNextFaceEdge == hAdjEdge) )
				{
					hNextFaceEdge = hNextFaceEdge.NextEdge;

					// If we have come full circle there and not found an edge which is not going
					// to be removed stop. This means the face is invalid and should be removed.
					if ( hNextFaceEdge == hFace.Edge )
					{
						hNextFaceEdge = HalfEdgeHandle.Invalid;
						break;
					}
				}

				hFace.Edge = hNextFaceEdge;

				Assert.True( hFace.Edge != hEdge );
				Assert.True( hFace.Edge != hAdjEdge );

				if ( hFace.Edge == HalfEdgeHandle.Invalid )
				{
					RemoveFace( hEdge.Face, false );
				}
			}

			// Detach the edge and its opposite edge from the vertices they originate from.
			DetachEdgeFromVertex( hEdge, bRemoveFreeVerts );
			DetachEdgeFromVertex( hEdge.OppositeEdge, bRemoveFreeVerts );

			// Remove the edge and its opposite edge from the mesh. 
			// Note pEdge is invalid as soon as hEdge is removed
			FreeHalfEdgePair( hEdge );
		}

		return true;
	}

	private bool IsLooseEdge( HalfEdgeHandle hFullEdge )
	{
		GetHalfEdgesConnectedToFullEdge( hFullEdge, out var hHalfEdgeA, out var hHalfEdgeB );

		if ( (this[hHalfEdgeA].OppositeEdge == this[hHalfEdgeA].NextEdge) ||
			 (this[hHalfEdgeB].OppositeEdge == this[hHalfEdgeB].NextEdge) )
			return true;

		return false;
	}

	private void RemoveLooseEdgesInFace( FaceHandle hFace )
	{
		var hEdgeToRemove = HalfEdgeHandle.Invalid;
		{
			if ( hFace.IsValid )
			{
				hEdgeToRemove = FindFirstLooseEdgeInFaceLoop( hFace.Edge );
			}
		}

		while ( hEdgeToRemove.IsValid )
		{
			RemoveHalfEdgePair( hEdgeToRemove, true );

			// Its possible that removing the edge above will result in removing the face if it was 
			// the last edge in the face loop. If so, there are no more edges to remove and we must 
			// stop because the face pointer may be invalid.
			if ( !hFace.IsValid )
				break;

			hEdgeToRemove = FindFirstLooseEdgeInFaceLoop( hFace.Edge );
		}
	}

	private HalfEdgeHandle FindFirstLooseEdgeInFaceLoop( HalfEdgeHandle hStartEdge )
	{
		if ( hStartEdge.IsValid )
		{
			var hCurrentEdge = hStartEdge;
			do
			{
				if ( hCurrentEdge.OppositeEdge == hCurrentEdge.NextEdge )
					return hCurrentEdge;

				hCurrentEdge = hCurrentEdge.NextEdge;
			}
			while ( hCurrentEdge != hStartEdge );
		}

		return HalfEdgeHandle.Invalid;
	}

	private void FreeHalfEdgePair( HalfEdgeHandle hHalfEdge )
	{
		if ( !hHalfEdge.IsValid )
			return;

		FreeHalfEdge( hHalfEdge.OppositeEdge );
		FreeHalfEdge( hHalfEdge );
	}

	private void FreeHalfEdge( HalfEdgeHandle hHalfEdge )
	{
		if ( !hHalfEdge.IsValid )
			return;

		this[hHalfEdge] = HalfEdge.Invalid;

		HalfEdgeList.Deallocate( hHalfEdge );
	}

	private void FreeFace( FaceHandle hFace )
	{
		if ( !hFace.IsValid )
			return;

		this[hFace] = Face.Invalid;
		FaceList.Deallocate( hFace );
	}

	private void FreeVertex( VertexHandle hVertex )
	{
		if ( !hVertex.IsValid )
			return;

		this[hVertex] = Vertex.Invalid;
		VertexList.Deallocate( hVertex );
	}

	public bool AddVertexToEdge( HalfEdgeHandle hHalfEdge, out VertexHandle hOutNewVertex )
	{
		hOutNewVertex = null;

		// Get one of the half edges of the full edge. 
		var hExistingEdgeA = hHalfEdge;
		if ( !hExistingEdgeA.IsValid )
			return false;

		GetVerticesConnectedToHalfEdge( hExistingEdgeA, out var hVertexA, out var hVertexB );

		var hExistingEdgeB = hExistingEdgeA.OppositeEdge;
		Assert.True( hExistingEdgeA.Vertex == hVertexB );
		Assert.True( hExistingEdgeB.Vertex == hVertexA );

		var hPrevEdgeB = FindPreviousEdgeInFaceLoop( hExistingEdgeB );
		Assert.True( hPrevEdgeB.IsValid );

		// Create the new edge pair
		if ( !AllocateHalfEdgePair( out var hNewEdgeA, out var hNewEdgeB ) )
			return false;

		// Create the new vertex 
		var hNewVertex = AllocateVertex( Vertex.Invalid );
		if ( !hNewVertex.IsValid )
			return false;

		// Redirect the existing edge so that it 
		// connects the new vertex with vertex A.
		hExistingEdgeA.Vertex = hNewVertex;

		// The new edge will connect the new vertex with vertex B
		hNewEdgeA.Vertex = hVertexB;
		hNewEdgeA.NextEdge = hExistingEdgeA.NextEdge;
		hNewEdgeA.Face = hExistingEdgeA.Face;
		hNewVertex.Edge = hNewEdgeA;

		hNewEdgeB.Vertex = hNewVertex;
		hNewEdgeB.NextEdge = hExistingEdgeB;
		hNewEdgeB.Face = hExistingEdgeB.Face;
		hVertexB.Edge = hNewEdgeB;

		hExistingEdgeA.NextEdge = hNewEdgeA;
		hPrevEdgeB.NextEdge = hNewEdgeB;

		hOutNewVertex = hNewVertex;

		return true;
	}

	public bool AddEdgeToFace( HalfEdgeHandle hIncomingEdgeA, HalfEdgeHandle hIncomingEdgeB, out HalfEdgeHandle hOutNewEdge )
	{
		hOutNewEdge = null;

		if ( !hIncomingEdgeA.IsValid || !hIncomingEdgeB.IsValid )
			return false;

		// Both edges must be connected to the same face
		var hFace = hIncomingEdgeA.Face;
		if ( hIncomingEdgeB.Face != hFace )
			return false;

		if ( !hFace.IsValid )
			return false;

		// Both edges cannot end at the same vertex 
		var hVertexA = hIncomingEdgeA.Vertex;
		var hVertexB = hIncomingEdgeB.Vertex;
		if ( hVertexA == hVertexB )
			return false;

		// Make sure that an edge connecting the specified vertices does not already exist.
		if ( FindFullEdgeConnectingVertices( hVertexA, hVertexB ).IsValid )
			return false;

		// Create the new half edge pair
		if ( AllocateHalfEdgePair( out var hNewEdgeAB, out var hNewEdgeBA ) == false )
			return false;

		hNewEdgeAB.Vertex = hVertexB;
		hNewEdgeBA.Vertex = hVertexA;

		// Reconnect the edges
		hNewEdgeAB.NextEdge = hIncomingEdgeB.NextEdge;
		hNewEdgeBA.NextEdge = hIncomingEdgeA.NextEdge;
		hIncomingEdgeA.NextEdge = hNewEdgeAB;
		hIncomingEdgeB.NextEdge = hNewEdgeBA;

		// Assign new edge A to the existing face 
		hNewEdgeAB.Face = hFace;
		hFace.Edge = hNewEdgeAB;

		// Create the new face and assign it to all of 
		// the edges in the loop with new edge B.
		var hNewFace = AllocateFace( Face.Invalid, hFace );
		if ( hNewFace.IsValid )
		{
			hNewFace.Edge = hNewEdgeBA;
			var hNewFaceEdge = hNewFace.Edge;
			do
			{
				hNewFaceEdge.Face = hNewFace;
				hNewFaceEdge = hNewFaceEdge.NextEdge;
			}
			while ( hNewFaceEdge != hNewFace.Edge );

			Assert.True( CheckFaceIntegrity( hNewFace ) );
		}

		Assert.True( CheckFaceIntegrity( hFace ) );

		hOutNewEdge = GetFullEdgeForHalfEdge( hNewEdgeAB );

		return hOutNewEdge.IsValid;
	}

	public bool CollapseFace( FaceHandle hFace, out VertexHandle hOutNewVertex )
	{
		hOutNewVertex = null;

		if ( !hFace.IsValid )
			return false;

		int nNumFaceEdges = ComputeNumEdgesInFace( hFace );
		if ( nNumFaceEdges <= 0 )
			return false;

		// Build a list of all of the edges in the face
		var vertexList = new VertexHandle[nNumFaceEdges];
		int nVertexCount = 0;
		var hEdge = hFace.Edge;
		do
		{
			vertexList[nVertexCount++] = hEdge.Vertex;
			hEdge = hEdge.NextEdge;
		}
		while ( hEdge != hFace.Edge );
		Assert.True( nVertexCount == nNumFaceEdges );

		// Collapse all of the edges. Note that collapsing one edge may remove others
		// in the list and eventually the face itself will be removed by this process.
		var hCollapsedFaceVertex = VertexHandle.Invalid;
		var hCurrentVertex = vertexList[0];
		for ( int iVertex = 1; iVertex < nNumFaceEdges; ++iVertex )
		{
			var hFullEdge = FindFullEdgeConnectingVertices( hCurrentVertex, vertexList[iVertex] );
			if ( hFullEdge.IsValid )
			{
				CollapseEdge( hFullEdge, out hCurrentVertex, out var _ );

				if ( !hCurrentVertex.IsValid )
					break;
			}

			hCollapsedFaceVertex = hCurrentVertex;
		}

		hOutNewVertex = hCollapsedFaceVertex;

		return hCollapsedFaceVertex.IsValid;
	}

	public bool CollapseEdge( HalfEdgeHandle hFullEdge, out VertexHandle pOutNewVertex, out List<(HalfEdgeHandle, HalfEdgeHandle)> pOutEdgeReplacements )
	{
		return CollapseEdge( hFullEdge, out pOutNewVertex, false, out pOutEdgeReplacements );
	}

	public bool CollapseEdge( HalfEdgeHandle hFullEdge, out VertexHandle pOutNewVertex, bool bCheckOnly, out List<(HalfEdgeHandle, HalfEdgeHandle)> pOutEdgeReplacements )
	{
		pOutNewVertex = null;
		pOutEdgeReplacements = null;

		if ( !hFullEdge.IsValid )
			return false;

		GetVerticesConnectedToHalfEdge( hFullEdge, out var hVertexA, out var hVertexB );
		var hEdgeA = hFullEdge;
		var hEdgeB = hFullEdge.OppositeEdge;
		var hFaceA = hEdgeA.Face;
		var hFaceB = hEdgeB.Face;

		// Find the pairs of edges which will be overlapping once the specified edge is collapsed.
		var overlappingEdgeA1 = HalfEdgeHandle.Invalid;
		var overlappingEdgeA2 = HalfEdgeHandle.Invalid;
		{
			var pEdgeA = hEdgeA;
			var pNextEdge = pEdgeA.NextEdge;
			if ( pNextEdge.NextEdge.NextEdge == hEdgeA )
			{
				overlappingEdgeA1 = pEdgeA.NextEdge;
				overlappingEdgeA2 = pNextEdge.NextEdge;
			}
		}

		var overlappingEdgeB1 = HalfEdgeHandle.Invalid;
		var overlappingEdgeB2 = HalfEdgeHandle.Invalid;
		{
			var pEdgeB = hEdgeB;
			var pNextEdge = pEdgeB.NextEdge;
			if ( pNextEdge.NextEdge.NextEdge == hEdgeB )
			{
				overlappingEdgeB1 = pEdgeB.NextEdge;
				overlappingEdgeB2 = pNextEdge.NextEdge;
			}
		}

		// Check to see if there are any edges that would be overlapping once the specified edge is collapsed 
		// that are not attached to same face as one of the edges, in this case the edge cannot be collapsed.
		var hStartEdge = hVertexA.Edge;
		var hCurrentEdge = hStartEdge;
		do
		{
			var hEdgeAToN = hCurrentEdge;
			var pEdgeAToN = hEdgeAToN;
			hCurrentEdge = pEdgeAToN.OppositeEdge.NextEdge;

			var hVertexN = pEdgeAToN.Vertex;
			var hEdgeNToB = FindHalfEdgeConnectingVertices( hVertexN, hVertexB );
			if ( hEdgeNToB.IsValid )
			{
				var pEdgeNToB = hEdgeNToB;
				var hEdgeNToA = pEdgeAToN.OppositeEdge;
				var hEdgeBToN = pEdgeNToB.OppositeEdge;
				var pEdgeNToA = hEdgeNToA;
				var pEdgeBToN = hEdgeBToN;

				// If the edge pair is one of the already found overlapping 
				// edge pairs there is no need to test the face, it is allowed.
				if ( ((hEdgeAToN == overlappingEdgeA1) && (hEdgeNToB == overlappingEdgeA2)) ||
					 ((hEdgeAToN == overlappingEdgeA2) && (hEdgeNToB == overlappingEdgeA1)) )
					continue;

				if ( ((hEdgeAToN == overlappingEdgeB1) && (hEdgeNToB == overlappingEdgeB2)) ||
					 ((hEdgeAToN == overlappingEdgeB2) && (hEdgeNToB == overlappingEdgeB1)) )
					continue;

				if ( ((hEdgeBToN == overlappingEdgeA1) && (hEdgeNToA == overlappingEdgeA2)) ||
					 ((hEdgeBToN == overlappingEdgeA2) && (hEdgeNToA == overlappingEdgeA1)) )
					continue;

				if ( ((hEdgeBToN == overlappingEdgeB1) && (hEdgeNToA == overlappingEdgeB2)) ||
					 ((hEdgeBToN == overlappingEdgeB2) && (hEdgeNToA == overlappingEdgeB1)) )
					continue;


				if ( (pEdgeAToN.Face == pEdgeNToB.Face) && (pEdgeAToN.Face != FaceHandle.Invalid) )
				{
					if ( (pEdgeAToN.Face == hFaceA) && ((hEdgeAToN == overlappingEdgeA1) || (hEdgeAToN == overlappingEdgeA2)) )
						continue;

					if ( (pEdgeAToN.Face == hFaceB) && ((hEdgeAToN == overlappingEdgeB1) || (hEdgeAToN == overlappingEdgeB2)) )
						continue;
				}

				if ( (pEdgeBToN.Face == pEdgeNToA.Face) && (pEdgeBToN.Face != FaceHandle.Invalid) )
				{
					if ( (pEdgeBToN.Face == hFaceA) && ((hEdgeBToN == overlappingEdgeA1) || (hEdgeBToN == overlappingEdgeA2)) )
						continue;

					if ( (pEdgeBToN.Face == hFaceB) && ((hEdgeBToN == overlappingEdgeB1) || (hEdgeBToN == overlappingEdgeB2)) )
						continue;
				}

				// Neither the edge path connecting vertex a to b or the path connecting vertex b to a 
				// were connected to either of the faces directly connected to the edge being collapsed.
				// This means collapsing the edge could result in bad topology, the collapse is not allowed.
				return false;
			}
		}
		while ( hCurrentEdge != hStartEdge );

		if ( bCheckOnly )
			return true;

		// Create the new vertex and point all the edges that were terminating 
		// at either of the old vertices to the new vertex.
		var hNewVertex = AllocateVertex( Vertex.Invalid );
		if ( !hNewVertex.IsValid )
			return false;

		RedirectEdgesToVertex( hVertexA, hNewVertex );
		RedirectEdgesToVertex( hVertexB, hNewVertex );

		// Disconnect the edge that is being collapsed from the faces and other edges.
		Assert.True( hEdgeA.IsValid && hEdgeB.IsValid );
		if ( hEdgeA.IsValid && hEdgeA.IsValid )
		{
			var pNewVertex = hNewVertex;
			var hNextEdgeA = hEdgeA.NextEdge;
			var hPrevEdgeA = FindPreviousEdgeInFaceLoop( hEdgeA );
			var hNextEdgeB = hEdgeB.NextEdge;
			var hPrevEdgeB = FindPreviousEdgeInFaceLoop( hEdgeB );

			hPrevEdgeB.NextEdge = hNextEdgeB;
			hPrevEdgeA.NextEdge = hNextEdgeA;

			var pFaceA = hFaceA;
			if ( pFaceA.IsValid )
				pFaceA.Edge = hNextEdgeA;

			var pFaceB = hFaceB;
			if ( pFaceB.IsValid )
				pFaceB.Edge = hNextEdgeB;

			// Make sure the new vertex is not referencing the edge being collapsed
			if ( (pNewVertex.Edge == hEdgeA) || (pNewVertex.Edge == hEdgeB) )
			{
				pNewVertex.Edge = hNextEdgeA;
			}
			Assert.True( (pNewVertex.Edge != hEdgeA) && (pNewVertex.Edge != hEdgeB) );

			// Remove the old vertices
			hVertexA.Edge = HalfEdgeHandle.Invalid;
			RemoveVertex( hVertexA, false );
			hVertexB.Edge = HalfEdgeHandle.Invalid;
			RemoveVertex( hVertexB, false );

			// Remove the old edge
			hEdgeA.Face = FaceHandle.Invalid;
			hEdgeB.Face = FaceHandle.Invalid;
			hEdgeA.Vertex = VertexHandle.Invalid;
			hEdgeB.Vertex = VertexHandle.Invalid;
			RemoveHalfEdgePair( hEdgeA, false );
		}

		pOutEdgeReplacements = new();

		// Merge the edges that are now overlapping and remove the faces which have become 2-sided
		if ( MergeOverlappingEdges( overlappingEdgeA1, overlappingEdgeA2, out var mergedEdgeA ) )
		{
			pOutEdgeReplacements.Add( (overlappingEdgeA1, mergedEdgeA) );
			pOutEdgeReplacements.Add( (overlappingEdgeA2, mergedEdgeA) );
		}

		if ( MergeOverlappingEdges( overlappingEdgeB1, overlappingEdgeB2, out var mergedEdgeB ) )
		{
			pOutEdgeReplacements.Add( (overlappingEdgeB1, mergedEdgeB) );
			pOutEdgeReplacements.Add( (overlappingEdgeB2, mergedEdgeB) );
		}

		Assert.True( CheckVertexEdgeIntegrity( hNewVertex ) );

		// Remove any loose edges that were created as a result of the the edge collapse. This can
		// occur if an edge on an interior edge loop is collapsed, removing the interior face loop 
		// leaving just a series of loose interior edges.
		RemoveLooseEdgesInFace( hFaceA );
		RemoveLooseEdgesInFace( hFaceB );

		Assert.True( CheckVertexEdgeIntegrity( hNewVertex ) );
		Assert.True( !hFaceA.IsValid || CheckFaceIntegrity( hFaceA ) );
		Assert.True( !hFaceB.IsValid || CheckFaceIntegrity( hFaceB ) );

		// If the edge that was collapsed was on a triangular face which was removed as a result of the
		// edges collapse it is possible the new vertex was actually removed if the edge was not shared
		// with any other faces.
		if ( !hNewVertex.IsValid )
		{
			hNewVertex = VertexHandle.Invalid;
		}

		pOutNewVertex = hNewVertex;

		return true;
	}

	private bool CheckVertexEdgeIntegrity( VertexHandle hVertex, bool bAssert = true )
	{
		var hStartEdge = GetFirstEdgeInVertexLoop( hVertex );
		if ( !hStartEdge.IsValid )
			return true;

		var hCurrentEdge = hStartEdge;
		do
		{
			if ( !CheckEdgeIntegrity( hCurrentEdge, bAssert ) )
				return false;

			hCurrentEdge = GetNextEdgeInVertexLoop( hCurrentEdge );
		}
		while ( hCurrentEdge != hStartEdge );

		return true;
	}

	private bool MergeOverlappingEdges( HalfEdgeHandle hHalfEdgeA, HalfEdgeHandle hHalfEdgeB, out HalfEdgeHandle pOutNewEdge )
	{
		pOutNewEdge = null;

		if ( !hHalfEdgeA.IsValid || !hHalfEdgeB.IsValid )
			return false;

		// Both edges must refer to each other as the next edge
		Assert.True( hHalfEdgeA.NextEdge == hHalfEdgeB );
		Assert.True( hHalfEdgeB.NextEdge == hHalfEdgeA );
		if ( (hHalfEdgeA.NextEdge != hHalfEdgeB) || (hHalfEdgeB.NextEdge != hHalfEdgeA) )
			return false;

		// Both edges must refer to the same face
		Assert.True( hHalfEdgeA.Face == hHalfEdgeB.Face );
		if ( hHalfEdgeA.Face != hHalfEdgeB.Face )
			return false;

		// The two half edges must be opposites, but not each others opposites
		var hOppositeEdgeA = hHalfEdgeA.OppositeEdge;
		var hOppositeEdgeB = hHalfEdgeB.OppositeEdge;
		Assert.True( hOppositeEdgeA.Vertex == hHalfEdgeB.Vertex );
		Assert.True( hOppositeEdgeB.Vertex == hHalfEdgeA.Vertex );
		Assert.True( hOppositeEdgeA != hOppositeEdgeB );
		if ( (hOppositeEdgeA.Vertex != hHalfEdgeB.Vertex) ||
			 (hOppositeEdgeB.Vertex != hHalfEdgeA.Vertex) ||
			 (hOppositeEdgeA == hOppositeEdgeB) )
			return false;

		// Remove the shared face
		if ( hHalfEdgeA.Face != FaceHandle.Invalid )
		{
			var hFace = hHalfEdgeA.Face;
			DetachFaceFromEdges( hFace );
			RemoveFace( hFace, false );
		}

		// Both edge should now be open
		Assert.True( hHalfEdgeA.Face == FaceHandle.Invalid );
		Assert.True( hHalfEdgeB.Face == FaceHandle.Invalid );

		// Create a new half edge pair which will be a connected pair of the 
		// opposite edges of the open edges which are being connected.
		if ( !AllocateHalfEdgePair( out var hNewHalfEdgeA, out var hNewHalfEdgeB ) )
			return false;

		{
			hNewHalfEdgeA.NextEdge = hOppositeEdgeA.NextEdge;
			hNewHalfEdgeA.Face = hOppositeEdgeA.Face;
			hNewHalfEdgeA.Vertex = hOppositeEdgeA.Vertex;

			var hPrevEdgeA = FindPreviousEdgeInFaceLoop( hOppositeEdgeA );
			Assert.True( hPrevEdgeA.NextEdge == hOppositeEdgeA );
			hPrevEdgeA.NextEdge = hNewHalfEdgeA;

			hNewHalfEdgeA.Vertex.Edge = hNewHalfEdgeB;
			if ( hNewHalfEdgeA.Face != FaceHandle.Invalid )
			{
				if ( hNewHalfEdgeA.Face.Edge == hOppositeEdgeA )
				{
					hNewHalfEdgeA.Face.Edge = hNewHalfEdgeA;
				}
			}

			hNewHalfEdgeB.NextEdge = hOppositeEdgeB.NextEdge;
			hNewHalfEdgeB.Face = hOppositeEdgeB.Face;
			hNewHalfEdgeB.Vertex = hOppositeEdgeB.Vertex;

			var hPrevEdgeB = FindPreviousEdgeInFaceLoop( hOppositeEdgeB );
			Assert.True( hPrevEdgeB.NextEdge == hOppositeEdgeB );
			hPrevEdgeB.NextEdge = hNewHalfEdgeB;

			hNewHalfEdgeB.Vertex.Edge = hNewHalfEdgeA;
			if ( hNewHalfEdgeB.Face != FaceHandle.Invalid )
			{
				if ( hNewHalfEdgeB.Face.Edge == hOppositeEdgeB )
				{
					hNewHalfEdgeB.Face.Edge = hNewHalfEdgeB;
				}
			}
		}

		// Remove the old half edge pairs
		FreeHalfEdgePair( hHalfEdgeA );
		FreeHalfEdgePair( hHalfEdgeB );

		// If the resulting edge has no connected faces or is a loose edge remove it
		if ( ((hNewHalfEdgeA.Face == FaceHandle.Invalid) && (hNewHalfEdgeB.Face == FaceHandle.Invalid)) ||
			 (hNewHalfEdgeA.NextEdge == hNewHalfEdgeB) || (hNewHalfEdgeB.NextEdge == hNewHalfEdgeA) )
		{
			RemoveHalfEdgePair( hNewHalfEdgeA, true );
		}
		else
		{
			pOutNewEdge = hNewHalfEdgeA;
		}

		Assert.True( !IsHalfEdgeInMesh( hNewHalfEdgeA ) || CheckEdgeIntegrity( hNewHalfEdgeA ) );
		Assert.True( !IsHalfEdgeInMesh( hNewHalfEdgeB ) || CheckEdgeIntegrity( hNewHalfEdgeB ) );
		Assert.True( !IsHalfEdgeInMesh( hNewHalfEdgeA ) || (hNewHalfEdgeA.Face == FaceHandle.Invalid) || CheckFaceIntegrity( hNewHalfEdgeA.Face ) );
		Assert.True( !IsHalfEdgeInMesh( hNewHalfEdgeB ) || (hNewHalfEdgeB.Face == FaceHandle.Invalid) || CheckFaceIntegrity( hNewHalfEdgeB.Face ) );

		return true;
	}

	private bool IsHalfEdgeInMesh( HalfEdgeHandle hHalfEdge )
	{
		return hHalfEdge.IsValid;
	}

	private bool CheckEdgeIntegrity( HalfEdgeHandle hEdge, bool bAssert = true )
	{
		Assert.True( hEdge.IsValid || (bAssert == false) );
		if ( !hEdge.IsValid )
			return false;

		// 1. Every half edge must be matched with a corresponding opposite half edge to form a pair.
		var hOppositeEdge = GetOppositeHalfEdge( hEdge );
		Assert.True( hOppositeEdge.IsValid || (bAssert == false) );
		if ( !hOppositeEdge.IsValid )
			return false;

		Assert.True( (hOppositeEdge.OppositeEdge == hEdge) || (bAssert == false) );
		if ( hOppositeEdge.OppositeEdge != hEdge )
			return false;

		GetVerticesConnectedToHalfEdge( hEdge, out var hVertexA, out var hVertexB );
		GetVerticesConnectedToHalfEdge( hEdge.OppositeEdge, out var hAdjVertexA, out var hAdjVertexB );
		Assert.True( (hVertexA == hAdjVertexB) || (bAssert == false) );
		if ( hVertexA != hAdjVertexB )
			return false;

		Assert.True( (hVertexB == hAdjVertexA) || (bAssert == false) );
		if ( hVertexB != hAdjVertexA )
			return false;

		Assert.True( (hVertexA != hVertexB) || (bAssert == false) );
		if ( hVertexA == hVertexB )
			return false;


		// 2. Each half edge pair must refer to at least one face.
		Assert.True( (hEdge.Face != FaceHandle.Invalid) || (hOppositeEdge.Face != FaceHandle.Invalid) || (bAssert == false) );
		if ( (hEdge.Face == FaceHandle.Invalid) && (hOppositeEdge.Face == FaceHandle.Invalid) )
			return false;

		// If the half edge refers to a face it must be valid and must refer back to the edge
		if ( hEdge.Face != FaceHandle.Invalid )
		{
			// All valid handles within the mesh should always correspond to valid components
			Assert.True( hEdge.Face.IsValid || (bAssert == false) );
			var hFace = hEdge.Face;
			if ( !hFace.IsValid )
				return false;

			// An edge must be in the edge loop of the face it is connected to
			var hStartEdge = hFace.Edge;
			var hCurrentEdge = hStartEdge;
			while ( hCurrentEdge != hEdge )
			{
				hCurrentEdge = hCurrentEdge.NextEdge;

				// Traversed the whole face edge loop and did not find the edge
				Assert.True( (hCurrentEdge != hStartEdge) || (bAssert == false) );
				if ( hCurrentEdge == hStartEdge )
					return false;
			}
		}

		// 3. The next edge reference of an edge must always be valid.
		Assert.True( (hEdge.NextEdge != HalfEdgeHandle.Invalid) || (bAssert == false) );
		if ( hEdge.NextEdge == HalfEdgeHandle.Invalid )
			return false;

		var hNextEdge = hEdge.NextEdge;
		Assert.True( hNextEdge.IsValid || (bAssert == false) );
		if ( !hNextEdge.IsValid )
			return false;

		// 4. The edge specified by the next edge reference must refer to the same face as this edge.
		Assert.True( (hEdge.Face == hNextEdge.Face) || (bAssert == false) );
		if ( hEdge.Face != hNextEdge.Face )
			return false;

		// 5. An edge may not refer to its opposite edge as it next edge.
		Assert.True( (hNextEdge != hOppositeEdge) || (bAssert == false) );
		if ( hNextEdge == hOppositeEdge )
			return false;

		// 6. The vertex reference of and edge must always be valid
		Assert.True( (hEdge.Vertex != VertexHandle.Invalid) || (bAssert == false) );
		if ( hEdge.Vertex == VertexHandle.Invalid )
			return false;

		var hVertex = hEdge.Vertex;
		Assert.True( hVertex.IsValid || (bAssert == false) );
		if ( !hVertex.IsValid )
			return false;

		// 7. Both half edges of a pair may not specify the same vertex
		Assert.True( (hEdge.Vertex != hOppositeEdge.Vertex) || (bAssert == false) );
		if ( hEdge.Vertex == hOppositeEdge.Vertex )
			return false;

		// 8. An edge's opposite edge must originate from the end vertex specified by the edge and
		// therefore must be in the edge loop around the vertex. 
		Assert.True( (hVertex.Edge != HalfEdgeHandle.Invalid) || (bAssert == false) );
		if ( hVertex.Edge == HalfEdgeHandle.Invalid )
			return false;

		bool bFoundOpposite = false;
		{
			var hCurrentEdge = hVertex.Edge;
			do
			{
				if ( hCurrentEdge == hEdge.OppositeEdge )
				{
					bFoundOpposite = true;
					break;
				}
				hCurrentEdge = hCurrentEdge.OppositeEdge.NextEdge;
			}
			while ( hCurrentEdge != hVertex.Edge );
		}

		Assert.True( (bFoundOpposite) || (bAssert == false) );
		if ( bFoundOpposite == false )
			return false;

		// 9. There may never be two edges which start and end at the same vertex. 
		var hOverlappingEdge = FindOverlappingEdge( hEdge );
		Assert.True( !hOverlappingEdge.IsValid || (bAssert == false) );
		if ( hOverlappingEdge.IsValid )
			return false;

		return true;
	}

	private HalfEdgeHandle FindOverlappingEdge( HalfEdgeHandle hHalfEdge )
	{
		if ( !hHalfEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		// Test all of the edges originating the at start vertex of
		// this edge and check to see if they end at the same vertex.
		var hVertex = hHalfEdge.OppositeEdge.Vertex;
		var hCurrentEdge = hVertex.Edge;
		do
		{
			if ( hCurrentEdge != hHalfEdge )
			{
				if ( hCurrentEdge.Vertex == hHalfEdge.Vertex )
					return hCurrentEdge;
			}
			hCurrentEdge = hCurrentEdge.OppositeEdge.NextEdge;
		}
		while ( hCurrentEdge != hVertex.Edge );

		return HalfEdgeHandle.Invalid;
	}

	private void DetachFaceFromEdges( FaceHandle hFace )
	{
		if ( !hFace.IsValid )
			return;

		if ( hFace.Edge != HalfEdgeHandle.Invalid )
		{
			var hCurrentEdge = hFace.Edge;
			do
			{
				hCurrentEdge.Face = FaceHandle.Invalid;
				hCurrentEdge = hCurrentEdge.NextEdge;
			}
			while ( hCurrentEdge != hFace.Edge );
		}

		hFace.Edge = HalfEdgeHandle.Invalid;
	}

	private void RedirectEdgesToVertex( VertexHandle hOldVertex, VertexHandle hNewVertex )
	{
		if ( !hNewVertex.IsValid )
			return;

		// Redirect all of the edges ending at the old vertex to end at the new vertex
		var hStartEdge = hOldVertex.Edge;
		var hCurrentEdge = hStartEdge;
		do
		{
			var hOppositeEdge = hCurrentEdge.OppositeEdge;
			Assert.True( hOppositeEdge.Vertex == hOldVertex );

			hOppositeEdge.Vertex = hNewVertex;
			hNewVertex.Edge = hCurrentEdge;

			hCurrentEdge = hOppositeEdge.NextEdge;
		}
		while ( hCurrentEdge != hStartEdge );
	}

	public bool DissolveEdge( HalfEdgeHandle hFullEdge, out FaceHandle hOutFaceHandle )
	{
		hOutFaceHandle = null;

		if ( !hFullEdge.IsValid )
			return false;

		var hAdjEdge = hFullEdge.OppositeEdge;
		var hFace = hFullEdge.Face;
		var hAdjFace = hAdjEdge.Face;

		// The edge must be connected to two different faces for dissolve to be a valid operation.
		if ( !hFace.IsValid || !hAdjFace.IsValid || (hFace == hAdjFace) )
			return false;

		// Update all of the edges that used to refer to
		// the opposite face to refer to the current face.
		var nOppositeFaceNumEdges = 0;
		var hAdjFaceEdge = hAdjEdge;
		do
		{
			hAdjFaceEdge.Face = hFace;
			hAdjFaceEdge = hAdjFaceEdge.NextEdge;
			++nOppositeFaceNumEdges;
		}
		while ( hAdjFaceEdge != hAdjEdge );

		// Ensure the face is not using the edge that is going to be removed as its starting edge.
		// Always do this so that the first edge in the face is consistent relative to the edge dissolved.
		hFace.Edge = hFullEdge.NextEdge;

		// Detach the edge from the face to ensure
		// removing the edge does not destroy the face
		hFullEdge.Face = FaceHandle.Invalid;
		hAdjEdge.Face = FaceHandle.Invalid;

		// Remove the specified edge and then iteratively remove any loose edges.
		RemoveEdge( hFullEdge, true );
		RemoveLooseEdgesInFace( hFace );

		// Remove the opposite face. Its edge is cleared first so the remove does not incorrectly 
		// try to remove the edges which have been transferred to the current face.
		hAdjFace.Edge = HalfEdgeHandle.Invalid;
		RemoveFace( hAdjFace, false );

		hOutFaceHandle = hFace;

		return true;
	}

	public bool GetEdgeMergeVertexPairs( HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB,
		out VertexHandle vertexPairA1, out VertexHandle vertexPairA2,
		out VertexHandle vertexPairB1, out VertexHandle vertexPairB2 )
	{
		vertexPairA1 = VertexHandle.Invalid;
		vertexPairA2 = VertexHandle.Invalid;
		vertexPairB1 = VertexHandle.Invalid;
		vertexPairB2 = VertexHandle.Invalid;

		// Get the open half edge of each edge, both edges must have one open half edge.
		var hOpenHalfEdgeA = GetOpenHalfEdgeFromFullEdge( hEdgeA );
		var hOpenHalfEdgeB = GetOpenHalfEdgeFromFullEdge( hEdgeB );
		if ( (hOpenHalfEdgeA == HalfEdgeHandle.Invalid) || (hOpenHalfEdgeB == HalfEdgeHandle.Invalid) )
			return false;

		vertexPairA1 = hOpenHalfEdgeA.Vertex;
		vertexPairA2 = hOpenHalfEdgeB.OppositeEdge.Vertex;

		vertexPairB1 = hOpenHalfEdgeA.OppositeEdge.Vertex;
		vertexPairB2 = hOpenHalfEdgeB.Vertex;

		return true;
	}

	public bool MergeEdges( HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB, out VertexHandle hOutNewVertexA, out VertexHandle hOutNewVertexB )
	{
		hOutNewVertexA = VertexHandle.Invalid;
		hOutNewVertexB = VertexHandle.Invalid;

		// Get the open half edge of each edge, both edges must have one open half edge.
		var hOpenHalfEdgeA = GetOpenHalfEdgeFromFullEdge( hEdgeA );
		var hOpenHalfEdgeB = GetOpenHalfEdgeFromFullEdge( hEdgeB );
		if ( (hOpenHalfEdgeA == HalfEdgeHandle.Invalid) || (hOpenHalfEdgeB == HalfEdgeHandle.Invalid) )
			return false;

		// The opposite edges of the open half edges must not belong to the same face
		if ( hOpenHalfEdgeA.OppositeEdge.Face == hOpenHalfEdgeB.OppositeEdge.Face )
			return false;

		// Two edges which start or end at the same vertex may not be merged
		if ( hOpenHalfEdgeA.Vertex == hOpenHalfEdgeB.Vertex )
			return false;

		if ( hOpenHalfEdgeA.OppositeEdge.Vertex == hOpenHalfEdgeB.OppositeEdge.Vertex )
			return false;

		// Build the pairs of vertices that will need to be merged.
		if ( !GetEdgeMergeVertexPairs( hEdgeA, hEdgeB, out var vertexPairA1, out var vertexPairA2, out var vertexPairB1, out var vertexPairB2 ) )
			return false;

		// If either of the vertices are shared already, just merge the other pair of vertices.
		if ( vertexPairA1 == vertexPairA2 )
		{
			hOutNewVertexA = vertexPairA1;
			return MergeVertices( vertexPairB1, vertexPairB2, out hOutNewVertexB );
		}

		if ( vertexPairB1 == vertexPairB2 )
		{
			hOutNewVertexB = vertexPairB1;
			return MergeVertices( vertexPairA1, vertexPairA2, out hOutNewVertexA );
		}

		// Test to see if both pairs of vertices can be merged. Performing this check helps avoid the 
		// case where merging the edge results in merging a single vertex of the edge but not both.
		if ( (!MergeVertices( vertexPairA1, vertexPairA2, hOpenHalfEdgeA.NextEdge, hOpenHalfEdgeB, out _, true )) ||
			 (!MergeVertices( vertexPairB1, vertexPairB2, hOpenHalfEdgeA, hOpenHalfEdgeB.NextEdge, out _, true )) )
			return false;

		// If both pairs can be merged then merge them.
		if ( !MergeVertices( vertexPairA1, vertexPairA2, hOpenHalfEdgeA.NextEdge, hOpenHalfEdgeB, out hOutNewVertexA, false ) )
			return false;

		if ( !MergeVertices( vertexPairB1, vertexPairB2, hOpenHalfEdgeA, hOpenHalfEdgeB.NextEdge, out hOutNewVertexB, false ) )
			return false;

		return true;
	}

	public bool MergeVertices( VertexHandle hVertexA, VertexHandle hVertexB, out VertexHandle hOutNewVertex )
	{
		return MergeVertices( hVertexA, hVertexB, HalfEdgeHandle.Invalid, HalfEdgeHandle.Invalid, out hOutNewVertex, false );
	}

	public bool MergeVertices( VertexHandle hVertexA, VertexHandle hVertexB, HalfEdgeHandle hOpenEdgeA, HalfEdgeHandle hOpenEdgeB, out VertexHandle hOutNewVertex, bool bCheckOnly )
	{
		hOutNewVertex = VertexHandle.Invalid;

		// If the two specified vertices are actually the same vertex, do nothing but return true
		if ( hVertexA == hVertexB )
		{
			hOutNewVertex = hVertexA;
			return true;
		}

		// First check to see if there is an edge connecting the two vertices, if so collapse the edge
		var hFullEdge = FindFullEdgeConnectingVertices( hVertexA, hVertexB );
		if ( hFullEdge != HalfEdgeHandle.Invalid )
		{
			return CollapseEdge( hFullEdge, out hOutNewVertex, bCheckOnly, out var _ );
		}

		// If an open edge was not specified to use in merging the vertices, check to see if there is 
		// exactly one open edge starting at the vertex, if so use that one, otherwise the vertices may 
		// not be merged.
		if ( hOpenEdgeA != HalfEdgeHandle.Invalid )
		{
			Assert.True( hOpenEdgeA.Face == FaceHandle.Invalid );
			Assert.True( hOpenEdgeA.OppositeEdge.Vertex == hVertexA );
		}
		else if ( ComputeNumOpenEdgesInVertexLoop( hVertexA ) == 1 )
		{
			hOpenEdgeA = FindFirstOpenEdgeInVertexLoop( hVertexA );
		}

		if ( hOpenEdgeB != HalfEdgeHandle.Invalid )
		{
			Assert.True( hOpenEdgeB.Face == FaceHandle.Invalid );
			Assert.True( hOpenEdgeB.OppositeEdge.Vertex == hVertexB );
		}
		else if ( ComputeNumOpenEdgesInVertexLoop( hVertexB ) == 1 )
		{
			hOpenEdgeB = FindFirstOpenEdgeInVertexLoop( hVertexB );
		}

		if ( (hOpenEdgeA == HalfEdgeHandle.Invalid) || (hOpenEdgeB == HalfEdgeHandle.Invalid) )
			return false;

		// Now check to see if there is a pair of open edges connecting the two vertices. If so create
		// a triangle face and use the collapse edge function to collapse the new edge resulting in 
		// merging the vertices.
		{
			// Now see if there is an open edge connecting the vertex
			// at the end of the open edge (vertex N) to vertex B.
			var hVertexN = hOpenEdgeA.Vertex;
			var hEdgeNToB = FindHalfEdgeConnectingVertices( hVertexN, hVertexB );
			if ( hEdgeNToB != HalfEdgeHandle.Invalid )
			{
				// If there is an edge but it is not open the vertices cannot be merged
				if ( hEdgeNToB.Face != FaceHandle.Invalid )
					return false;

				if ( !AddFace( out var hNewFace, hVertexA, hVertexN, hVertexB ) )
					return false;

				hFullEdge = FindFullEdgeConnectingVertices( hVertexA, hVertexB );
				bool bSuccess = CollapseEdge( hFullEdge, out hOutNewVertex, bCheckOnly, out var _ );
				if ( bCheckOnly )
				{
					RemoveFace( hNewFace, false );
				}
				return bSuccess;
			}
		}

		// If creating a face using the open edge from A to N failed try the open edge from B to M.
		{
			var hVertexM = hOpenEdgeB.Vertex;
			var hEdgeMToA = FindHalfEdgeConnectingVertices( hVertexM, hVertexA );
			if ( hEdgeMToA != HalfEdgeHandle.Invalid )
			{
				if ( hEdgeMToA.Face != FaceHandle.Invalid )
					return false;

				if ( !AddFace( out var hNewFace, hVertexB, hVertexM, hVertexA ) )
					return false;

				hFullEdge = FindFullEdgeConnectingVertices( hVertexA, hVertexB );
				var bSuccess = CollapseEdge( hFullEdge, out hOutNewVertex, bCheckOnly, out var _ );
				if ( bCheckOnly )
				{
					RemoveFace( hNewFace, false );
				}
				return bSuccess;
			}
		}

		// If we have reached this point the vertices do not have a single edge or a pair of open edges
		// that connect them. They may be merged as long as they do not belong to the same face and there
		// is not a pair of edges connecting them.
		var hClosedEdgeA = hOpenEdgeA.OppositeEdge;
		var hClosedEdgeB = hOpenEdgeB.OppositeEdge;
		if ( hClosedEdgeA.Face == hClosedEdgeB.Face )
			return false;

		if ( AreVerticesConnectedByEdgePair( hVertexA, hVertexB ) )
			return false;

		// Find the previous edges to which refer to the open edges as their next edge. 
		// Note these edge will be open as well.
		var hPreviousOpenEdgeA = FindPreviousEdgeInFaceLoop( hOpenEdgeA );
		var hPreviousOpenEdgeB = FindPreviousEdgeInFaceLoop( hOpenEdgeB );
		var pPreviousOpenEdgeA = hPreviousOpenEdgeA;
		var pPreviousOpenEdgeB = hPreviousOpenEdgeB;
		Assert.True( pPreviousOpenEdgeA.IsValid );
		Assert.True( pPreviousOpenEdgeB.IsValid );
		if ( !pPreviousOpenEdgeA.IsValid || !pPreviousOpenEdgeB.IsValid )
			return false;

		if ( bCheckOnly )
			return true;

		// If any of these conditions are not true there is a fundamental problem with
		// the topology or a bug in the FindPreviousEdgeInVertexLoop() function.
		Assert.True( pPreviousOpenEdgeA.Vertex == hVertexA );
		Assert.True( pPreviousOpenEdgeB.Vertex == hVertexB );
		Assert.True( pPreviousOpenEdgeA.NextEdge == hOpenEdgeA );
		Assert.True( pPreviousOpenEdgeB.NextEdge == hOpenEdgeB );
		Assert.True( pPreviousOpenEdgeA.Face == FaceHandle.Invalid );
		Assert.True( pPreviousOpenEdgeB.Face == FaceHandle.Invalid );

		// Create the new vertex and point all the edges that were terminating 
		// at either of the old vertices to the new vertex.
		var hNewVertex = AllocateVertex( Vertex.Invalid );
		if ( hNewVertex == VertexHandle.Invalid )
			return false;

		RedirectEdgesToVertex( hVertexA, hNewVertex );
		RedirectEdgesToVertex( hVertexB, hNewVertex );

		// Redirect the previous open edges at the open edge of the opposite vertex
		pPreviousOpenEdgeA.NextEdge = hOpenEdgeB;
		pPreviousOpenEdgeB.NextEdge = hOpenEdgeA;

		// Remove the old vertices
		hVertexA.Edge = HalfEdgeHandle.Invalid;
		hVertexB.Edge = HalfEdgeHandle.Invalid;
		RemoveVertex( hVertexA, false );
		RemoveVertex( hVertexB, false );

		Assert.True( CheckVertexEdgeIntegrity( hNewVertex ) );

		hOutNewVertex = hNewVertex;

		return true;
	}

	private HalfEdgeHandle FindFirstOpenEdgeInVertexLoop( VertexHandle hVertex )
	{
		if ( hVertex.IsValid )
		{
			var hEdge = hVertex.Edge;
			do
			{
				if ( hEdge.Face == FaceHandle.Invalid )
					return hEdge;

				hEdge = hEdge.OppositeEdge.NextEdge;
			}
			while ( hEdge != hVertex.Edge );
		}

		return HalfEdgeHandle.Invalid;
	}

	private bool AreVerticesConnectedByEdgePair( VertexHandle hVertexA, VertexHandle hVertexB )
	{
		var hStartEdge = GetFirstEdgeInVertexLoop( hVertexA );
		var hCurrentEdge = hStartEdge;

		do
		{
			if ( FindHalfEdgeConnectingVertices( hCurrentEdge.Vertex, hVertexB ) != HalfEdgeHandle.Invalid )
				return true;

			hCurrentEdge = GetNextEdgeInVertexLoop( hCurrentEdge );
		}
		while ( hCurrentEdge != hStartEdge );
		return false;
	}

	public HalfEdgeHandle FindConnectedHalfEdgeInSet( HalfEdgeHandle hEdge, IReadOnlyList<HalfEdgeHandle> pEdges, int nNumEdges )
	{
		if ( !hEdge.IsValid )
			return HalfEdgeHandle.Invalid;

		var hStartEdge = hEdge.NextEdge;
		var hCurrentEdge = hStartEdge;

		do
		{
			// Is the edge in the provided list
			for ( int iEdge = 0; iEdge < nNumEdges; ++iEdge )
			{
				if ( hCurrentEdge == pEdges[iEdge] )
					return hCurrentEdge;
			}

			// Get the next edge connected to the vertex
			hCurrentEdge = GetNextEdgeInVertexLoop( hCurrentEdge );
		}
		while ( hCurrentEdge != hStartEdge );

		return HalfEdgeHandle.Invalid;
	}

	internal void SetEdgeVertex( HalfEdgeHandle hEdge, VertexHandle hVertex )
	{
		var halfEdge = this[hEdge];
		halfEdge.Vertex = hVertex.Index;
		this[hEdge] = halfEdge;
	}

	internal void SetEdgeOpposite( HalfEdgeHandle hEdge, HalfEdgeHandle hOpposite )
	{
		var halfEdge = this[hEdge];
		halfEdge.OppositeEdge = hOpposite.Index;
		this[hEdge] = halfEdge;
	}

	internal void SetEdgeNext( HalfEdgeHandle hEdge, HalfEdgeHandle hNext )
	{
		var halfEdge = this[hEdge];
		halfEdge.NextEdge = hNext.Index;
		this[hEdge] = halfEdge;
	}

	internal void SetEdgeFace( HalfEdgeHandle hEdge, FaceHandle hFace )
	{
		var halfEdge = this[hEdge];
		halfEdge.Face = hFace.Index;
		this[hEdge] = halfEdge;
	}

	internal void SetVertexEdge( VertexHandle hVertex, HalfEdgeHandle hEdge )
	{
		var vertex = this[hVertex];
		vertex.Edge = hEdge.Index;
		this[hVertex] = vertex;
	}

	internal void SetFaceEdge( FaceHandle hFace, HalfEdgeHandle hEdge )
	{
		var face = this[hFace];
		face.Edge = hEdge.Index;
		this[hFace] = face;
	}

	public Vertex this[VertexHandle hVertex]
	{
		get => hVertex is not null && hVertex.Index >= 0 && hVertex.Index < VertexList.Count ? VertexList[hVertex.Index] : Vertex.Invalid;
		private set
		{
			if ( hVertex is not null && hVertex.Index >= 0 && hVertex.Index < VertexList.Count )
				VertexList[hVertex.Index] = value;
		}
	}

	public Face this[FaceHandle hFace]
	{
		get => hFace is not null && hFace.Index >= 0 && hFace.Index < FaceList.Count ? FaceList[hFace.Index] : Face.Invalid;
		private set
		{
			if ( hFace is not null && hFace.Index >= 0 && hFace.Index < FaceList.Count )
				FaceList[hFace.Index] = value;
		}
	}

	public HalfEdge this[HalfEdgeHandle hEdge]
	{
		get => hEdge is not null && hEdge.Index >= 0 && hEdge.Index < HalfEdgeList.Count ? HalfEdgeList[hEdge.Index] : HalfEdge.Invalid;
		private set
		{
			if ( hEdge is not null && hEdge.Index >= 0 && hEdge.Index < HalfEdgeList.Count )
				HalfEdgeList[hEdge.Index] = value;
		}
	}
}
