using HalfEdgeMesh;

namespace Sandbox;

partial class PolygonMesh
{
	struct FaceCutterEdgeResult( HalfEdgeHandle edge, bool isNew )
	{
		public HalfEdgeHandle Edge = edge;
		public bool IsNew = isNew;
	}

	enum SideType
	{
		Front,
		Back,
		On
	};

	class FaceCutter( PolygonMesh pMesh, FaceHandle face, Plane planeInMeshSpace, List<FaceCutterEdgeResult> outNewEdges, List<FaceHandle> outResultFaces )
	{
		public PolygonMesh Mesh = pMesh;
		public FaceHandle FaceToCut = face;
		public int VertexCount;

		public Plane CutPlane = planeInMeshSpace;
		public Plane FacePlane;

		public struct CrossingPoint
		{
			public VertexHandle Vertex;
			public float DistanceFromSortPlane;
		}

		public List<CrossingPoint> CrossingPoints = [];

		public VertexHandle[] FaceVertices;
		public Vector3[] VertexPositions;

		public float[] Distances;
		public SideType[] Sides;
		public int[] VertexSideCounts = new int[3];

		public List<FaceCutterEdgeResult> NewEdges = outNewEdges;
		public List<FaceHandle> ResultFaces = outResultFaces;

		public SideType Cut( bool removeFacesBehindPlane )
		{
			InitData();
			ClassifyPoints();

			SideType nResultSide;
			if ( VertexSideCounts[(int)SideType.Front] == 0 && VertexSideCounts[(int)SideType.Back] == 0 )
			{
				nResultSide = VertexCount == 0 ? SideType.Front : FacePlane.Normal.Dot( CutPlane.Normal ) > 0 ? SideType.Back : SideType.Front;
			}
			else if ( VertexSideCounts[(int)SideType.Front] == 0 )
			{
				nResultSide = SideType.Back;
			}
			else if ( VertexSideCounts[(int)SideType.Back] == 0 )
			{
				nResultSide = SideType.Front;
			}
			else
			{
				GenerateCrossingPoints();
				SortCrossingPoints();
				BuildNewEdges( removeFacesBehindPlane );
				nResultSide = SideType.On;
			}

			if ( nResultSide != SideType.On )
			{
				if ( (nResultSide == SideType.Back) && removeFacesBehindPlane )
				{
					Mesh.Topology.RemoveFace( FaceToCut, true );
				}
				else
				{
					ResultFaces?.Add( FaceToCut );
				}
			}

			return nResultSide;
		}

		void InitData()
		{
			Mesh.GetFacePlane( FaceToCut, Transform.Zero, out FacePlane );
			Mesh.GetVerticesConnectedToFace( FaceToCut, out FaceVertices );

			VertexCount = FaceVertices.Length;
			VertexPositions = new Vector3[VertexCount];

			for ( var i = 0; i < VertexCount; ++i )
			{
				Mesh.GetVertexPosition( FaceVertices[i], Transform.Zero, out VertexPositions[i] );
			}
		}

		void ClassifyPoints()
		{
			Distances = new float[VertexCount];
			Sides = new SideType[VertexCount];
			VertexSideCounts[(int)SideType.Front] = 0;
			VertexSideCounts[(int)SideType.Back] = 0;
			VertexSideCounts[(int)SideType.On] = 0;

			const float eps = 0.001f;

			for ( var i = 0; i < VertexCount; i++ )
			{
				var distance = CutPlane.GetDistance( VertexPositions[i] );
				Distances[i] = distance;

				if ( distance > eps )
				{
					Sides[i] = SideType.Front;
					VertexSideCounts[(int)SideType.Front]++;
				}
				else if ( distance < -eps )
				{
					Sides[i] = SideType.Back;
					VertexSideCounts[(int)SideType.Back]++;
				}
				else
				{
					Sides[i] = SideType.On;
					VertexSideCounts[(int)SideType.On]++;
				}
			}
		}

		void GenerateCrossingPoints()
		{
			Plane sortPlane = default;

			Assert.True( CrossingPoints.Count == 0 );
			CrossingPoints.Clear();
			CrossingPoints.EnsureCapacity( VertexCount );

			for ( var i = 0; i < VertexCount; ++i )
			{
				var splitVert = VertexHandle.Invalid;

				var curSide = Sides[i];
				var nextSide = Sides[(i + 1) % VertexCount];

				if ( curSide == SideType.On )
				{
					splitVert = FaceVertices[i];
				}
				else if ( (curSide != nextSide) && (nextSide != SideType.On) )
				{
					var curVert = FaceVertices[i];
					var nextVert = FaceVertices[(i + 1) % VertexCount];
					var fraction = Distances[i] / (Distances[i] - Distances[(i + 1) % VertexCount]);

					if ( Mesh.AddVertexToEdge( curVert, nextVert, fraction, out splitVert ) == false )
					{
						var crossingPointCount = CrossingPoints.Count;
						for ( int pointIndex = 0; pointIndex < crossingPointCount; ++pointIndex )
						{
							var vertex = CrossingPoints[pointIndex].Vertex;
							if ( (Mesh.FindEdgeConnectingVertices( vertex, curVert ) != HalfEdgeHandle.Invalid) &&
								 (Mesh.FindEdgeConnectingVertices( vertex, nextVert ) != HalfEdgeHandle.Invalid) )
							{
								splitVert = vertex;
								break;
							}
						}
					}

					Assert.True( splitVert != VertexHandle.Invalid );
				}

				if ( splitVert != VertexHandle.Invalid )
				{
					Mesh.GetVertexPosition( splitVert, Transform.Zero, out var vertexPosition );

					var crossingPointIndex = CrossingPoints.Count;
					CrossingPoints.Add( new CrossingPoint { Vertex = splitVert, DistanceFromSortPlane = 0 } );

					if ( crossingPointIndex == 0 )
					{
						var sortNormal = CutPlane.Normal.Cross( FacePlane.Normal );
						sortPlane = new Plane( vertexPosition, sortNormal );
					}
					else
					{
						var point = CrossingPoints[crossingPointIndex];
						point.DistanceFromSortPlane = sortPlane.GetDistance( vertexPosition );
						CrossingPoints[crossingPointIndex] = point;
					}
				}
			}
		}

		void SortCrossingPoints()
		{
			CrossingPoints.Sort( ( a, b ) => a.DistanceFromSortPlane.CompareTo( b.DistanceFromSortPlane ) );
		}

		void BuildNewEdges( bool bRemoveFacesBehindPlane )
		{
			List<FaceHandle> facesToConsider = [];
			List<bool> facesToConsiderKeepFlag = [];

			int nCrossings = CrossingPoints.Count;

			facesToConsider.Add( FaceToCut );
			facesToConsiderKeepFlag.Add( true );

			for ( int i = 0; i < (nCrossings - 1); ++i )
			{
				var vertexA = CrossingPoints[i].Vertex;
				var vertexB = CrossingPoints[i + 1].Vertex;

				if ( vertexA == vertexB ) continue;

				var index = Mesh.Topology.FindFaceInSetSharedByVertices( vertexA, vertexB, facesToConsider );
				if ( index == -1 ) continue;

				var faceToSplit = facesToConsider[index];
				Assert.True( faceToSplit != FaceHandle.Invalid );

				var newEdge = HalfEdgeHandle.Invalid;
				var wasAdded = false;

				if ( Mesh.IsLineBetweenVerticesInsideFace( faceToSplit, vertexA, vertexB ) )
				{
					if ( Mesh.AddEdgeToFace( faceToSplit, vertexA, vertexB, out newEdge ) )
					{
						wasAdded = true;
					}
				}

				if ( !wasAdded )
				{
					newEdge = Mesh.FindEdgeConnectingVertices( vertexA, vertexB );
					if ( newEdge != HalfEdgeHandle.Invalid )
					{
						NewEdges?.Add( new FaceCutterEdgeResult( newEdge, false ) );
					}
					continue;
				}

				Mesh.GetFacesConnectedToEdge( newEdge, out var faceA, out var faceB );

				FaceHandle otherFace = faceA == faceToSplit ? faceB : faceA;
				facesToConsiderKeepFlag[index] = true;
				facesToConsider.Add( otherFace );
				facesToConsiderKeepFlag.Add( false );

				NewEdges?.Add( new FaceCutterEdgeResult( newEdge, true ) );
			}

			Assert.True( facesToConsider.Count == facesToConsiderKeepFlag.Count );

			var totalFaceCount = facesToConsider.Count;
			var resultFaceCount = facesToConsider.Count;

			if ( bRemoveFacesBehindPlane )
			{
				resultFaceCount = 0;
				for ( int i = 0; i < totalFaceCount; ++i )
				{
					if ( !facesToConsiderKeepFlag[i] )
					{
						Mesh.Topology.RemoveFace( facesToConsider[i], true );
						facesToConsider[i] = FaceHandle.Invalid;
					}
					else
					{
						++resultFaceCount;
					}
				}
			}

			if ( ResultFaces is not null )
			{
				ResultFaces.EnsureCapacity( ResultFaces.Count + resultFaceCount );
				for ( int faceIndex = 0; faceIndex < totalFaceCount; ++faceIndex )
				{
					var face = facesToConsider[faceIndex];
					if ( face != FaceHandle.Invalid )
					{
						ResultFaces.Add( face );
					}
				}
			}
		}
	}

	public void ClipFacesByPlaneAndCap( IReadOnlyList<FaceHandle> faces, Plane planeInMeshSpace, bool removeFacesBehindPlane, bool attemptToCap, List<HalfEdgeHandle> outNewEdges = null, List<FaceHandle> outCapFaces = null )
	{
		int faceCount = faces.Count;

		List<FaceCutterEdgeResult> resultEdges = [];

		for ( int faceIndex = 0; faceIndex < faceCount; ++faceIndex )
		{
			if ( IsFaceInMesh( faces[faceIndex] ) == false ) continue;

			var cutter = new FaceCutter( this, faces[faceIndex], planeInMeshSpace, resultEdges, null );
			cutter.Cut( removeFacesBehindPlane );
		}

		var edgeCount = resultEdges.Count;

		if ( attemptToCap )
		{
			for ( int i = 0; i < edgeCount; ++i )
			{
				CreateFaceInEdgeLoop( resultEdges[i].Edge, out var newFace );
				if ( (newFace != FaceHandle.Invalid) && (outCapFaces is not null) )
				{
					outCapFaces.Add( newFace );
				}
			}
		}

		if ( outNewEdges is not null )
		{
			outNewEdges.Clear();
			outNewEdges.EnsureCapacity( edgeCount );

			for ( int edgeIndex = 0; edgeIndex < edgeCount; ++edgeIndex )
			{
				if ( resultEdges[edgeIndex].IsNew )
				{
					outNewEdges.Add( resultEdges[edgeIndex].Edge );
				}
			}
		}

		IsDirty = true;
	}
}
