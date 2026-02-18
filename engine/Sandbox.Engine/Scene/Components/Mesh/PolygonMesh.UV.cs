using HalfEdgeMesh;
using NativeEngine;
using System.Runtime.InteropServices;

namespace Sandbox;

partial class PolygonMesh
{
	public void GenerateUVsForFaces(
		ReadOnlySpan<FaceHandle> faces,
		int generationMode,
		int edgeAlignMode,
		HalfEdgeHandle alignEdgeVertexA,
		HalfEdgeHandle alignEdgeVertexB,
		out List<HalfEdgeHandle> outFaceVertices,
		out List<Vector2> outFaceVertexUVs )
	{
		GetIndexedTrianglesForFaces( faces, out var triangleFaceHandles, out var triangleFaceVertices );

		var triangleFaceIds = new int[triangleFaceHandles.Count];
		for ( int i = 0; i < triangleFaceHandles.Count; ++i )
			triangleFaceIds[i] = triangleFaceHandles[i].Index;

		var vertexPositions = new List<Vector3>();
		var triangleVertexIndices = new List<uint>();
		var faceVertexToVertex = new Dictionary<HalfEdgeHandle, int>();
		outFaceVertices = null;
		outFaceVertexUVs = null;

		if ( !BuildVertexListForTriangulatedFaces( faces, triangleFaceVertices, vertexPositions, triangleVertexIndices, faceVertexToVertex ) )
			return;

		int alignIndexA = faceVertexToVertex.TryGetValue( alignEdgeVertexA, out var a ) ? a : -1;
		int alignIndexB = faceVertexToVertex.TryGetValue( alignEdgeVertexB, out var b ) ? b : -1;

		var vertexUVs = MeshUtils.GenerateUVsForTriangles(
			CollectionsMarshal.AsSpan( vertexPositions ),
			CollectionsMarshal.AsSpan( triangleVertexIndices ),
			triangleFaceIds,
			(GenerateUVsMode_t)generationMode,
			(AlignEdgeUV_t)edgeAlignMode,
			alignIndexA,
			alignIndexB );

		outFaceVertices = new List<HalfEdgeHandle>( faceVertexToVertex.Count );
		outFaceVertexUVs = new List<Vector2>( faceVertexToVertex.Count );

		foreach ( var kv in faceVertexToVertex )
		{
			outFaceVertices.Add( kv.Key );
			outFaceVertexUVs.Add( vertexUVs[kv.Value] );
		}
	}

	bool BuildVertexListForTriangulatedFaces( ReadOnlySpan<FaceHandle> faces, List<HalfEdgeHandle> triangleFaceVertices, List<Vector3> outVertexPositions, List<uint> outTriangleVertexIndices, Dictionary<HalfEdgeHandle, int> outFaceVertexToVertex )
	{
		var faceWalker = new FaceWalker( this, faces );
		if ( !faceWalker.GenerateFaceVertexToVertexMapping( outFaceVertexToVertex, outVertexPositions ) )
			return false;

		var numTriangleVertices = triangleFaceVertices.Count;
		outTriangleVertexIndices.Clear();
		outTriangleVertexIndices.EnsureCapacity( numTriangleVertices );

		for ( int i = 0; i < numTriangleVertices; ++i )
		{
			var hFaceVertex = triangleFaceVertices[i];
			var vertexIndex = outFaceVertexToVertex.TryGetValue( hFaceVertex, out var index ) ? index : -1;
			outTriangleVertexIndices.Add( (uint)vertexIndex );
		}

		return true;
	}

	int GetIndexedTrianglesForFaces( ReadOnlySpan<FaceHandle> faces, out List<FaceHandle> outTriangleFaceHandles, out List<HalfEdgeHandle> outTriangleFaceVertexHandles )
	{
		outTriangleFaceHandles = [];
		outTriangleFaceVertexHandles = [];

		var triFaceVerts = new List<HalfEdgeHandle>( 32 );
		var triPositions = new List<Vector3>( 32 );

		for ( int iFace = 0; iFace < faces.Length; ++iFace )
		{
			var face = faces[iFace];

			triFaceVerts.Clear();
			triPositions.Clear();

			var start = GetFirstVertexInFace( face );
			var fv = start;

			do
			{
				var v = GetVertexConnectedToFaceVertex( fv );
				triPositions.Add( Positions[v] );
				triFaceVerts.Add( fv );

				fv = GetNextVertexInFace( fv );
			}
			while ( !fv.Equals( start ) );

			if ( triPositions.Count < 3 )
				continue;

			var triIndices = Mesh.TriangulatePolygon( CollectionsMarshal.AsSpan( triPositions ) );

			for ( int i = 0; i < triIndices.Length; i += 3 )
			{
				int i0 = triIndices[i + 0];
				int i1 = triIndices[i + 1];
				int i2 = triIndices[i + 2];

				outTriangleFaceVertexHandles.Add( triFaceVerts[i0] );
				outTriangleFaceVertexHandles.Add( triFaceVerts[i1] );
				outTriangleFaceVertexHandles.Add( triFaceVerts[i2] );

				outTriangleFaceHandles.Add( face );
			}
		}

		return outTriangleFaceHandles.Count;
	}

	class FaceWalker
	{
		enum FaceState
		{
			NotInSet,
			NeedsMapped,
			FaceMapped,
		};

		struct FaceEdgePair
		{
			public FaceHandle Face;
			public HalfEdgeHandle Edge;
		};

		readonly PolygonMesh _mesh;
		readonly List<FaceEdgePair> _smoothEdgeFaceQueue = [];
		readonly List<FaceEdgePair> _sharpEdgeFaceQueue = [];
		readonly Dictionary<FaceHandle, FaceState> _faceMappingState;
		int _smoothEdgeQueuePos = 0;
		int _sharpEdgeQueuePos = 0;

		public FaceWalker( PolygonMesh mesh, ReadOnlySpan<FaceHandle> faces )
		{
			_mesh = mesh;
			_faceMappingState = new Dictionary<FaceHandle, FaceState>( faces.Length );

			foreach ( var face in faces )
			{
				_faceMappingState.Add( face, FaceState.NeedsMapped );
			}
		}

		FaceState GetFaceState( FaceHandle hFace )
		{
			return _faceMappingState.TryGetValue( hFace, out var hFaceEntry ) ? hFaceEntry : FaceState.NotInSet;
		}

		void AddFaceToQueue( FaceHandle hFace, HalfEdgeHandle hEdge, bool isEdgeSmooth )
		{
			var pair = new FaceEdgePair { Face = hFace, Edge = hEdge };

			if ( isEdgeSmooth )
			{
				_smoothEdgeFaceQueue.Add( pair );
			}
			else
			{
				_sharpEdgeFaceQueue.Add( pair );
			}
		}

		bool GetNextFaceInQueue( out FaceHandle outFace, out HalfEdgeHandle outEdge )
		{
			if ( _smoothEdgeFaceQueue.Count > _smoothEdgeQueuePos )
			{
				outFace = _smoothEdgeFaceQueue[_smoothEdgeQueuePos].Face;
				outEdge = _smoothEdgeFaceQueue[_smoothEdgeQueuePos].Edge;
				++_smoothEdgeQueuePos;
				return true;
			}

			if ( _sharpEdgeFaceQueue.Count > _sharpEdgeQueuePos )
			{
				outFace = _sharpEdgeFaceQueue[_sharpEdgeQueuePos].Face;
				outEdge = _sharpEdgeFaceQueue[_sharpEdgeQueuePos].Edge;
				++_sharpEdgeQueuePos;
				return true;
			}

			outFace = default;
			outEdge = default;

			return false;
		}

		bool GetNextFaceToProcess( out FaceHandle outFace, out HalfEdgeHandle outEdge )
		{
			while ( GetNextFaceInQueue( out var hFaceToProcess, out var hConnectingEdge ) )
			{
				if ( !_faceMappingState.TryGetValue( hFaceToProcess, out var hFaceEntry ) )
					continue;

				if ( hFaceEntry == FaceState.NeedsMapped )
				{
					outFace = hFaceToProcess;
					outEdge = hConnectingEdge;
					_faceMappingState[hFaceToProcess] = FaceState.FaceMapped;
					return true;
				}
			}

			foreach ( var (hFace, hFaceEntry) in _faceMappingState )
			{
				if ( hFaceEntry == FaceState.NeedsMapped )
				{
					outFace = hFace;
					outEdge = HalfEdgeHandle.Invalid;
					_faceMappingState[hFace] = FaceState.FaceMapped;
					return true;
				}
			}

			outFace = default;
			outEdge = default;

			return false;
		}

		void FindOrAddPositionForVertexSharedBetweenFaces( VertexHandle hVertex, FaceHandle hFaceA, FaceHandle hFaceB, Dictionary<HalfEdgeHandle, int> outFaceVertexToVertexTable, List<Vector3> outVertexPositions )
		{
			var hFaceVertexA = _mesh.FindFaceVertexConnectedToVertex( hVertex, hFaceA );
			var hFaceVertexB = _mesh.FindFaceVertexConnectedToVertex( hVertex, hFaceB );
			var indexA = outFaceVertexToVertexTable.TryGetValue( hFaceVertexA, out var a ) ? a : -1;
			var indexB = outFaceVertexToVertexTable.TryGetValue( hFaceVertexB, out var b ) ? b : -1;

			if ( (indexA < 0) && (indexB < 0) )
			{
				var vertexPosition = _mesh.GetVertexPosition( hVertex );
				var newIndex = outVertexPositions.Count;
				outVertexPositions.Add( vertexPosition );
				outFaceVertexToVertexTable.Add( hFaceVertexA, newIndex );
				outFaceVertexToVertexTable.Add( hFaceVertexB, newIndex );
			}
			else if ( indexA < 0 )
			{
				outFaceVertexToVertexTable.Add( hFaceVertexA, indexB );
			}
			else if ( indexB < 0 )
			{
				outFaceVertexToVertexTable.Add( hFaceVertexB, indexA );
			}
			else
			{
				Assert.True( indexA == indexB );
			}
		}

		public bool GenerateFaceVertexToVertexMapping( Dictionary<HalfEdgeHandle, int> pOutFaceVertexToVertexTable, List<Vector3> pOutVertexPositions )
		{
			while ( GetNextFaceToProcess( out var hCurrentFace, out var hConnectingEdge ) )
			{
				_mesh.GetEdgesConnectedToFace( hCurrentFace, out var edgesConnectedToFace );
				var nNumEdges = edgesConnectedToFace.Count;

				for ( var i = 0; i < nNumEdges; ++i )
				{
					var hEdge = edgesConnectedToFace[i];
					var hOppositeFace = _mesh.GetOppositeFaceConnectedToEdge( hEdge, hCurrentFace );
					var oppositeFaceState = GetFaceState( hOppositeFace );

					if ( oppositeFaceState == FaceState.NotInSet )
						continue;

					var isSmoothEdge = _mesh.IsEdgeSmooth( hEdge );

					if ( oppositeFaceState == FaceState.NeedsMapped )
					{
						Assert.True( hEdge != hConnectingEdge );
						AddFaceToQueue( hOppositeFace, hEdge, isSmoothEdge );
					}
					else if ( (hEdge == hConnectingEdge) || isSmoothEdge )
					{
						_mesh.GetVerticesConnectedToEdge( hEdge, hCurrentFace, out var hVertexA, out var hVertexB );
						FindOrAddPositionForVertexSharedBetweenFaces( hVertexA, hCurrentFace, hOppositeFace, pOutFaceVertexToVertexTable, pOutVertexPositions );
						FindOrAddPositionForVertexSharedBetweenFaces( hVertexB, hCurrentFace, hOppositeFace, pOutFaceVertexToVertexTable, pOutVertexPositions );
					}
				}
			}

			foreach ( var kv in _faceMappingState )
			{
				_mesh.GetFaceVerticesConnectedToFace( kv.Key, out var faceVerticesConnectedToFace );

				foreach ( var hFaceVertex in faceVerticesConnectedToFace )
				{
					if ( pOutFaceVertexToVertexTable.ContainsKey( hFaceVertex ) ) continue;

					var hVertex = _mesh.GetVertexConnectedToFaceVertex( hFaceVertex );
					var vVertexPosition = _mesh.GetVertexPosition( hVertex );
					var nVertexIndex = pOutVertexPositions.Count;
					pOutVertexPositions.Add( vVertexPosition );
					pOutFaceVertexToVertexTable.Add( hFaceVertex, nVertexIndex );
				}
			}

			return true;
		}
	}

	public void SplitFacesIntoIslandsForUVMapping( IReadOnlyList<FaceHandle> faces, out List<List<FaceHandle>> faceIslands )
	{
		FindFaceIslands( faces, out faceIslands );

		for ( var i = faceIslands.Count - 1; i >= 0; --i )
		{
			var island = faceIslands[i];

			FindBoundaryEdgesConnectedToFaces( island, out var boundaryEdges );
			FindEdgeIslands( boundaryEdges, out var boundaryEdgeIslands );

			if ( boundaryEdgeIslands.Count == 1 && !DoesFaceSetContainSharpEdges( island ) )
				continue;

			var boundedIslands = new List<List<FaceHandle>>();
			GroupFacesIntoBoundedIslandsByAxis( island, boundedIslands );

			faceIslands.RemoveAt( i );
			faceIslands.InsertRange( i, boundedIslands );
		}
	}

	bool DoesFaceSetContainSharpEdges( IReadOnlyList<FaceHandle> faces )
	{
		if ( faces == null || faces.Count == 0 ) return false;

		var faceSet = new HashSet<FaceHandle>( faces );

		for ( var i = 0; i < faces.Count; ++i )
		{
			var face = faces[i];
			GetEdgesConnectedToFace( face, out var edges );

			for ( var e = 0; e < edges.Count; ++e )
			{
				var edge = edges[e];
				var otherFace = GetOppositeFaceConnectedToEdge( edge, face );

				if ( !faceSet.Contains( otherFace ) ) continue;
				if ( IsEdgeSmooth( edge ) ) continue;

				return true;
			}
		}

		return false;
	}

	void GroupFacesIntoBoundedIslandsByAxis( IReadOnlyList<FaceHandle> faces, List<List<FaceHandle>> outIslands )
	{
		var grouping = new FaceGrouping( this, faces );
		var queue = new List<FaceHandle>();

		for ( var start = grouping.FindNextStartingFace(); start is not null && start.IsValid; start = grouping.FindNextStartingFace() )
		{
			queue.Clear();
			var q = 0;

			var groupIndex = grouping.AddFaceGroup( start );

			for ( var face = start; face is not null && face.IsValid; face = q < queue.Count ? queue[q++] : default )
			{
				GetEdgesConnectedToFace( face, out var edges );

				for ( var i = 0; i < edges.Count; ++i )
				{
					var edge = edges[i];
					var other = GetOppositeFaceConnectedToEdge( edge, face );

					if ( grouping.AddFaceToGroup( other, edge, groupIndex ) )
						queue.Add( other );
				}
			}
		}

		MergeCompatibleGroups( grouping );

		grouping.GetFaceGroups( outIslands );
	}

	static void MergeCompatibleGroups( FaceGrouping grouping )
	{
		while ( grouping.FindBestGroupsToMerge( out var a, out var b ) )
			grouping.MergeFaceGroups( a, b );
	}

	sealed class FaceGrouping
	{
		enum CardinalDirection
		{
			Invalid = -1,
			Px = 0, Py, Pz,
			Nx, Ny, Nz,
		}

		struct FaceGroup
		{
			public CardinalDirection Direction;
			public Vector3 AverageNormal;
			public List<FaceHandle> Faces;
		}

		readonly PolygonMesh _mesh;
		readonly Dictionary<FaceHandle, int> _faceToGroup;
		readonly List<FaceGroup> _groups;

		public FaceGrouping( PolygonMesh mesh, IReadOnlyList<FaceHandle> faces )
		{
			this._mesh = mesh;

			_faceToGroup = new Dictionary<FaceHandle, int>( faces.Count );
			for ( var i = 0; i < faces.Count; ++i )
				_faceToGroup[faces[i]] = -1;

			_groups = new List<FaceGroup>( faces.Count );
		}

		public FaceHandle FindNextStartingFace()
		{
			foreach ( var kv in _faceToGroup )
				if ( kv.Value == -1 )
					return kv.Key;

			return default;
		}

		public int AddFaceGroup( FaceHandle start )
		{
			if ( !_faceToGroup.TryGetValue( start, out var assigned ) || assigned != -1 )
				return -1;

			var index = _groups.Count;

			_groups.Add( new FaceGroup
			{
				Direction = CalcCardinalDirectionForFace( start ),
				AverageNormal = Vector3.Zero,
				Faces = new List<FaceHandle>( 32 )
			} );

			AssignFaceToGroup( start, index );
			return index;
		}

		public bool AddFaceToGroup( FaceHandle face, HalfEdgeHandle edge, int groupIndex )
		{
			if ( !_faceToGroup.TryGetValue( face, out var assigned ) || assigned != -1 )
				return false;

			if ( CalcCardinalDirectionForFace( face ) != _groups[groupIndex].Direction )
				return false;

			if ( !_mesh.IsEdgeSmooth( edge ) )
				return false;

			AssignFaceToGroup( face, groupIndex );
			return true;
		}

		public void MergeFaceGroups( int a, int b )
		{
			var ga = _groups[a];
			var gb = _groups[b];
			if ( gb.Faces.Count == 0 ) return;

			ga.Faces.EnsureCapacity( ga.Faces.Count + gb.Faces.Count );

			for ( var i = 0; i < gb.Faces.Count; ++i )
				AssignFaceToGroup( gb.Faces[i], a );

			gb.Faces.Clear();
			gb.Direction = CardinalDirection.Invalid;
			gb.AverageNormal = Vector3.Zero;

			_groups[a] = ga;
			_groups[b] = gb;
		}

		public bool CanMergeFaceGroups( int a, int b )
		{
			const float angleThreshold = 1f - 0.5f;

			var ga = _groups[a];
			var gb = _groups[b];

			var groupAngle = 1f - ga.AverageNormal.Dot( gb.AverageNormal );
			if ( groupAngle > angleThreshold )
				return false;

			var shared = 0;

			for ( var i = 0; i < ga.Faces.Count; ++i )
			{
				var faceA = ga.Faces[i];

				_mesh.GetEdgesConnectedToFace( faceA, out var edges );

				for ( var e = 0; e < edges.Count; ++e )
				{
					var edge = edges[e];
					var other = _mesh.GetOppositeFaceConnectedToEdge( edge, faceA );

					if ( _faceToGroup.TryGetValue( other, out var g ) && g == b )
					{
						if ( !_mesh.IsEdgeSmooth( edge ) )
							return false;

						++shared;
					}
				}
			}

			return shared > 0;
		}

		public bool FindBestGroupsToMerge( out int groupA, out int groupB )
		{
			var bestMin = int.MaxValue;
			var bestMax = int.MaxValue;
			var bestA = -1;
			var bestB = -1;

			for ( var i = 0; i < _groups.Count; ++i )
			{
				var ni = _groups[i].Faces.Count;
				if ( ni == 0 || ni > bestMax ) continue;

				for ( var j = i + 1; j < _groups.Count; ++j )
				{
					var nj = _groups[j].Faces.Count;
					if ( nj == 0 ) continue;

					var pairMin = Math.Min( ni, nj );
					var pairMax = Math.Max( ni, nj );

					if ( pairMin > bestMin || (pairMin == bestMin && pairMax >= bestMax) )
						continue;

					if ( !CanMergeFaceGroups( i, j ) )
						continue;

					bestMin = pairMin;
					bestMax = pairMax;
					bestA = i;
					bestB = j;
				}
			}

			groupA = bestA;
			groupB = bestB;
			return bestA >= 0 && bestB >= 0;
		}

		public void GetFaceGroups( List<List<FaceHandle>> outGroups )
		{
			outGroups.Clear();

			for ( var i = 0; i < _groups.Count; ++i )
			{
				if ( _groups[i].Faces.Count == 0 ) continue;
				outGroups.Add( new List<FaceHandle>( _groups[i].Faces ) );
			}
		}

		void AssignFaceToGroup( FaceHandle face, int groupIndex )
		{
			_faceToGroup[face] = groupIndex;

			var g = _groups[groupIndex];
			var n = g.Faces.Count;

			g.Faces.Add( face );

			_mesh.ComputeFaceNormal( face, out var faceNormal );

			g.AverageNormal = (g.AverageNormal * n + faceNormal).Normal;

			_groups[groupIndex] = g;
		}

		CardinalDirection CalcCardinalDirectionForFace( FaceHandle face )
		{
			_mesh.ComputeFaceNormal( face, out var n );
			var axis = LargestComponentIndex( n );

			return n[axis] >= 0f
				? (CardinalDirection)((int)CardinalDirection.Px + axis)
				: (CardinalDirection)((int)CardinalDirection.Nx + axis);
		}

		static int LargestComponentIndex( Vector3 v )
		{
			var ax = MathF.Abs( v.x );
			var ay = MathF.Abs( v.y );
			var az = MathF.Abs( v.z );
			return ax > ay ? (ax > az ? 0 : 2) : (ay > az ? 1 : 2);
		}
	}
}
