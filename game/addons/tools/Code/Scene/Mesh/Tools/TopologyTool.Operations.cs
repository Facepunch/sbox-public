using HalfEdgeMesh;

namespace Editor.MeshEditor;

public partial class TopologyTool
{
	private record struct FacePair( FaceHandle Face, FaceHandle NeighbourFace, HalfEdgeHandle Edge )
	{
		public FaceHandle FaceA { get; } = Face;
		public FaceHandle FaceB { get; } = NeighbourFace;
		public HalfEdgeHandle SharedEdge { get; } = Edge;

		public float Shape { get; set; }
		public float Normal { get; set; }
		public float Area { get; set; }

		public float Score => Shape + Normal;
	}

	private static void QuadrangulateFaces( PolygonMesh mesh, FaceHandle[] faces, float faceAngle, float shapeAngle,
		bool uv, bool color, bool blend, bool material, bool smooth, out List<FaceHandle> processedFaces,
		out List<Line> processedEdges )
	{

		processedFaces = [];
		processedEdges = [];

		List<FacePair> facePairs = [];

		// Get faces that share an edge, score them based on how viable they are to quadrangulate.
		foreach ( FaceHandle face in faces )
		{
			if ( mesh.GetFaceEdges( face ).Length > 3 )
			{
				continue;
			}

			// Get neighbouring faces that were selected when opening the tool.
			List<FaceHandle> neighbourFaces = [];

			mesh.GetFacesConnectedToFace( face, out List<FaceHandle> connectedFaces );
			neighbourFaces.AddRange( connectedFaces.Where( connectedFace => faces.Contains( connectedFace ) ) );

			// Get the edge shared between the faces.
			foreach ( FaceHandle neighbourFace in neighbourFaces )
			{
				if ( mesh.GetFaceEdges( neighbourFace ).Length > 3 )
				{
					continue;
				}

				// Skip checks if processed two faces in an earlier loop.
				if ( facePairs.Exists( x => x.FaceB == face && x.FaceA == neighbourFace ) )
				{
					continue;
				}

				HalfEdgeHandle[] faceEdges = mesh.GetFaceEdges( face );
				HalfEdgeHandle[] neighbourEdges = mesh.GetFaceEdges( neighbourFace );

				HalfEdgeHandle sharedEdge = faceEdges.First( edge => neighbourEdges.Contains( edge ) );
				mesh.GetEdgeVertices( sharedEdge, out VertexHandle sharedVertA, out VertexHandle sharedVertB );

				// Check if faces pass checks prior to scoring.
				{
					if ( smooth && mesh.GetEdgeSmoothing( sharedEdge ) == PolygonMesh.EdgeSmoothMode.Hard )
					{
						continue;
					}
				}

				if ( uv )
				{
					Vector2[] faceUVs = mesh.GetFaceTextureCoords( face );
					VertexHandle[] faceVerts = mesh.GetFaceVertices( face );

					int faceIndexA = Array.IndexOf( faceVerts, sharedVertA );
					int faceIndexB = Array.IndexOf( faceVerts, sharedVertB );

					Vector2[] neighbourUVs = mesh.GetFaceTextureCoords( neighbourFace );
					VertexHandle[] neighbourVerts = mesh.GetFaceVertices( neighbourFace );

					int neighbourIndexA = Array.IndexOf( neighbourVerts, sharedVertA );
					int neighbourIndexB = Array.IndexOf( neighbourVerts, sharedVertB );

					bool matchA = faceUVs[faceIndexA].AlmostEqual( neighbourUVs[neighbourIndexA] );
					bool matchB = faceUVs[faceIndexB].AlmostEqual( neighbourUVs[neighbourIndexB] );

					bool match = matchA && matchB;

					if ( !match )
					{
						continue;
					}
				}

				if ( color )
				{
					bool match = mesh.GetVertexColor( sharedEdge ) ==
								 mesh.GetVertexColor( sharedEdge.OppositeEdge.NextEdge.NextEdge ) &&
								 mesh.GetVertexColor( sharedEdge.OppositeEdge ) ==
								 mesh.GetVertexColor( sharedEdge.NextEdge.NextEdge );
					if ( !match )
					{
						continue;
					}
				}

				if ( blend )
				{
					bool match = mesh.GetVertexBlend( sharedEdge ) ==
								 mesh.GetVertexBlend( sharedEdge.OppositeEdge.NextEdge.NextEdge ) &&
								 mesh.GetVertexBlend( sharedEdge.OppositeEdge ) ==
								 mesh.GetVertexBlend( sharedEdge.NextEdge.NextEdge );
					if ( !match )
					{
						continue;
					}
				}

				{
					if ( material && mesh.GetFaceMaterial( face ) != mesh.GetFaceMaterial( neighbourFace ) )
					{
						continue;
					}
				}

				float normal;
				{
					Vector3 faceNormal = GetFaceNormal( face );
					Vector3 neighbourNormal = GetFaceNormal( neighbourFace );

					float degree = MathF.Acos( Vector3.Dot( faceNormal, neighbourNormal ) ).RadianToDegree();
					if ( degree >= faceAngle )
					{
						continue;
					}

					normal = degree / faceAngle;
				}

				float shape;
				{
					(float a, float b) = GetFaceShape( sharedEdge );

					float maxAngle = 90 + shapeAngle;
					float minAngle = 90 - shapeAngle;

					if ( a < minAngle || a > maxAngle )
					{
						continue;
					}

					if ( b < minAngle || b > maxAngle )
					{
						continue;
					}

					float limitA = Math.Min( Math.Abs( maxAngle - a ), Math.Abs( minAngle - a ) );
					float limitB = Math.Min( Math.Abs( maxAngle - b ), Math.Abs( minAngle - b ) );

					float max = Math.Min( limitA, limitB );
					shape = max / shapeAngle;
				}

				float area;
				{
					area = GetSharedFaceArea( face, neighbourFace );
				}

				facePairs.Add(
					new FacePair( face, neighbourFace, sharedEdge ) { Normal = normal, Shape = shape, Area = area } );
			}
		}

		// Order face pairs by score, start removing the highest scoring until there is no more.
		List<HalfEdgeHandle> edgesToRemove = [];
		while ( facePairs.Count > 0 )
		{
			FacePair e = facePairs.OrderBy( x => x.Score ).Last();
			edgesToRemove.Add( e.SharedEdge );

			facePairs.RemoveAll( x =>
				x.FaceA == e.FaceA || x.FaceA == e.FaceB ||
				x.FaceB == e.FaceA || x.FaceB == e.FaceB
			);

			processedFaces.Add( e.FaceA );
			facePairs.Remove( e );
		}

		processedEdges.AddRange( edgesToRemove.Select( e => mesh.GetEdgeLine( e ) ) );
		mesh.DissolveEdges( edgesToRemove, false, PolygonMesh.DissolveRemoveVertexCondition.None );

		float GetFaceArea( FaceHandle face )
		{
			VertexHandle[] vhs = mesh.GetFaceVertices( face );

			Vector3 a = mesh.GetVertexPosition( vhs[0] );
			Vector3 b = mesh.GetVertexPosition( vhs[1] );
			Vector3 c = mesh.GetVertexPosition( vhs[2] );

			float area = Vector3.Cross( b - a, c - a ).Length * 0.5f;
			return area;
		}

		float GetSharedFaceArea( FaceHandle face, FaceHandle neighbourFace )
		{
			return GetFaceArea( face ) + GetFaceArea( neighbourFace );
		}

		Vector3 GetFaceNormal( FaceHandle face )
		{
			VertexHandle[] vhs = mesh.GetFaceVertices( face );

			Vector3 a = mesh.GetVertexPosition( vhs[0] );
			Vector3 b = mesh.GetVertexPosition( vhs[1] );
			Vector3 c = mesh.GetVertexPosition( vhs[2] );

			Vector3 normal = Vector3.Cross( b - a, c - a ).Normal;
			return normal;
		}

		(float a, float b) GetFaceShape( HalfEdgeHandle edge )
		{
			mesh.GetEdgeVertices( edge, out VertexHandle hVertexA, out VertexHandle hVertexB );
			VertexHandle hVertexC = mesh.GetNextVertexInFace( edge ).Vertex;
			VertexHandle hVertexD = mesh.GetNextVertexInFace( edge.OppositeEdge ).Vertex;

			float a;
			float b;

			// Corner A
			{
				Vector3 quadADirA = (mesh.GetVertexPosition( hVertexC ) - mesh.GetVertexPosition( hVertexA )).Normal;
				Vector3 quadADirB = (mesh.GetVertexPosition( hVertexD ) - mesh.GetVertexPosition( hVertexA )).Normal;

				float dot = Vector3.Dot( quadADirA, quadADirB ).Clamp( -1.0f, 1.0f );
				float sharedVertADegrees = MathF.Acos( dot ) * 180.0f / MathF.PI;

				a = sharedVertADegrees;
			}

			// Corner B
			{
				Vector3 quadBDirA = (mesh.GetVertexPosition( hVertexC ) - mesh.GetVertexPosition( hVertexB )).Normal;
				Vector3 quadBDirB = (mesh.GetVertexPosition( hVertexD ) - mesh.GetVertexPosition( hVertexB )).Normal;

				float dot = Vector3.Dot( quadBDirA, quadBDirB ).Clamp( -1.0f, 1.0f );
				float sharedVertBDegrees = MathF.Acos( dot ) * 180.0f / MathF.PI;

				b = sharedVertBDegrees;
			}

			return (a, b);
		}
	}

	private static void TriangulateFaces( PolygonMesh mesh, FaceHandle[] faces, QuadMethod quadMethod, NgonMethod ngonMethod,
		int minEdges, out List<FaceHandle> processedFaces, out List<Line> processedEdges )
	{
		processedFaces = [];
		processedEdges = [];

		foreach ( var face in faces )
		{
			var edges = mesh.GetFaceEdges( face );
			if ( edges.Length < minEdges )
			{
				continue;
			}
			if ( edges.Length == 4 )
			{
				TriangulateQuad( quadMethod, mesh, face, out var newFaces, out var newEdges );
				processedFaces.AddRange( newFaces );
				processedEdges.Add( newEdges );
			}
			else
			{
				TriangulateNgon( ngonMethod, mesh, face, out var newFaces, out var newEdges );
				processedFaces.AddRange( newFaces );
				processedEdges.AddRange( newEdges );
			}
		}

		static void TriangulateQuad( QuadMethod method, PolygonMesh mesh, FaceHandle face,
			out List<FaceHandle> newFaces,
			out Line newEdge )
		{
			Material mat = mesh.GetFaceMaterial( face );
			VertexHandle[] hVertices = mesh.GetFaceVertices( face );

			List<Color32> vb = [];
			List<Color32> vc = [];

			// Walk the edge, getting the VertexBlend and VertexColor at each edge.
			HalfEdgeHandle startEdge = face.Edge;
			HalfEdgeHandle currentEdge = startEdge;
			do
			{
				vb.Add( mesh.GetVertexBlend( currentEdge ) );
				vc.Add( mesh.GetVertexColor( currentEdge ) );
				currentEdge = currentEdge.NextEdge;
			} while ( currentEdge != startEdge );

			Vector2[] UVs = mesh.GetFaceTextureCoords( face );
			mesh.GetFaceTextureParameters( face, out Vector4 axisU, out Vector4 axisV, out Vector2 scale );

			List<Vector3> vPos = new();
			foreach ( VertexHandle hVertex in hVertices )
			{
				vPos.Add( mesh.GetVertexPosition( hVertex ) );
			}

			mesh.RemoveFaces( [face] );

			VertexHandle[] newVerts = mesh.AddVertices( [.. vPos] );

			FaceHandle tri1;
			FaceHandle tri2;

			// 0, 1, 2, 3 triangulation order.
			void QuadTriangulateFixed()
			{
				tri1 = mesh.AddFace( newVerts[0], newVerts[1], newVerts[2] );
				mesh.SetFaceTextureCoords( tri1, [UVs[0], UVs[1], UVs[2]] );
				mesh.SetFaceTextureParameters( tri1, axisU, axisV, scale );

				mesh.SetVertexColor( tri1.Edge, vc[0] );
				mesh.SetVertexColor( tri1.Edge.NextEdge, vc[1] );
				mesh.SetVertexColor( tri1.Edge.NextEdge.NextEdge, vc[2] );
				mesh.SetVertexBlend( tri1.Edge, vb[0] );
				mesh.SetVertexBlend( tri1.Edge.NextEdge, vb[1] );
				mesh.SetVertexBlend( tri1.Edge.NextEdge.NextEdge, vb[2] );

				tri2 = mesh.AddFace( newVerts[2], newVerts[3], newVerts[0] );
				mesh.SetFaceTextureCoords( tri2, [UVs[2], UVs[3], UVs[0]] );
				mesh.SetFaceTextureParameters( tri2, axisU, axisV, scale );

				mesh.SetVertexColor( tri2.Edge, vc[2] );
				mesh.SetVertexColor( tri2.Edge.NextEdge, vc[3] );
				mesh.SetVertexColor( tri2.Edge.NextEdge.NextEdge, vc[0] );
				mesh.SetVertexBlend( tri2.Edge, vb[2] );
				mesh.SetVertexBlend( tri2.Edge.NextEdge, vb[3] );
				mesh.SetVertexBlend( tri2.Edge.NextEdge.NextEdge, vb[0] );
			}

			// 1, 2, 3, 0 triangulation order.
			void QuadTriangulateAlternate()
			{
				tri1 = mesh.AddFace( newVerts[1], newVerts[2], newVerts[3] );
				mesh.SetFaceTextureCoords( tri1, [UVs[1], UVs[2], UVs[3]] );
				mesh.SetFaceTextureParameters( tri1, axisU, axisV, scale );

				mesh.SetVertexColor( tri1.Edge, vc[1] );
				mesh.SetVertexColor( tri1.Edge.NextEdge, vc[2] );
				mesh.SetVertexColor( tri1.Edge.NextEdge.NextEdge, vc[3] );
				mesh.SetVertexBlend( tri1.Edge, vb[1] );
				mesh.SetVertexBlend( tri1.Edge.NextEdge, vb[2] );
				mesh.SetVertexBlend( tri1.Edge.NextEdge.NextEdge, vb[3] );

				tri2 = mesh.AddFace( newVerts[3], newVerts[0], newVerts[1] );
				mesh.SetFaceTextureCoords( tri2, [UVs[3], UVs[0], UVs[1]] );
				mesh.SetFaceTextureParameters( tri2, axisU, axisV, scale );

				mesh.SetVertexColor( tri2.Edge, vc[3] );
				mesh.SetVertexColor( tri2.Edge.NextEdge, vc[0] );
				mesh.SetVertexColor( tri2.Edge.NextEdge.NextEdge, vc[1] );
				mesh.SetVertexBlend( tri2.Edge, vb[3] );
				mesh.SetVertexBlend( tri2.Edge.NextEdge, vb[0] );
				mesh.SetVertexBlend( tri2.Edge.NextEdge.NextEdge, vb[1] );
			}

			switch ( method )
			{
				case QuadMethod.Fixed:
					{
						QuadTriangulateFixed();
						break;
					}
				case QuadMethod.FixedAlternate:
					{
						QuadTriangulateAlternate();
						break;
					}
				case QuadMethod.ShortestDiagonal:
					{
						float diag0 = (vPos[0] - vPos[2]).Length;
						float diag1 = (vPos[1] - vPos[3]).Length;

						if ( diag0 < diag1 )
						{
							QuadTriangulateFixed();
						}
						else
						{
							QuadTriangulateAlternate();
						}

						break;
					}
				case QuadMethod.LongestDiagonal:
					{
						float diag0 = (vPos[0] - vPos[2]).Length;
						float diag1 = (vPos[1] - vPos[3]).Length;

						if ( diag0 > diag1 )
						{
							QuadTriangulateFixed();
						}
						else
						{
							QuadTriangulateAlternate();
						}

						break;
					}
				default:
					{
						QuadTriangulateFixed();
						break;
					}
			}

			mesh.SetFaceMaterial( tri1, mat );
			mesh.SetFaceMaterial( tri2, mat );

			VertexHandle[] mergeVerts = hVertices.Concat( newVerts ).ToArray();

			mesh.MergeVerticesWithinDistance( mergeVerts, 0.0001f, true, true, out _ );
			mesh.ComputeFaceTextureCoordinatesFromParameters();

			HalfEdgeHandle[] edges1 = mesh.GetFaceEdges( tri1 );
			HalfEdgeHandle[] edges2 = mesh.GetFaceEdges( tri2 );

			newFaces = [tri1, tri2];
			newEdge = mesh.GetEdgeLine( edges1.Intersect( edges2 ).FirstOrDefault() );
		}

		static void TriangulateNgon( NgonMethod method, PolygonMesh mesh, FaceHandle face,
			out List<FaceHandle> newFaces,
			out List<Line> newEdges )
		{
			newFaces = [];
			newEdges = [];

			Material mat = mesh.GetFaceMaterial( face );

			VertexHandle[] hVertices = mesh.GetFaceVertices( face );

			List<Color32> vb = [];
			List<Color32> vc = [];

			// Walk the edge, getting the VertexBlend and VertexColor at each edge.
			HalfEdgeHandle startEdge = face.Edge;
			HalfEdgeHandle currentEdge = startEdge;

			do
			{
				vb.Add( mesh.GetVertexBlend( currentEdge ) );
				vc.Add( mesh.GetVertexColor( currentEdge ) );
				currentEdge = currentEdge.NextEdge;
			} while ( currentEdge != startEdge );

			List<Vector3> vPos = new();
			foreach ( VertexHandle hVertex in hVertices )
			{
				vPos.Add( mesh.GetVertexPosition( hVertex ) );
			}

			Vector2[] vUv = mesh.GetFaceTextureCoords( face );
			mesh.GetFaceTextureParameters( face, out Vector4 axisU, out Vector4 axisV, out Vector2 scale );

			// Get face normal, tangent and bitangent for 2D projection.
			mesh.ComputeFaceNormal( face, out Vector3 normal );
			Vector3 reference = MathF.Abs( normal.z ) < 0.99f ? Vector3.Up : Vector3.Right;

			Vector3 tangent = normal.Cross( reference ).Normal;
			Vector3 bitangent = normal.Cross( tangent ).Normal;

			// Returns 2D projected vertex.
			Vector2 Project( Vector3 p ) { return new Vector2( p.Dot( tangent ), p.Dot( bitangent ) ); }

			mesh.RemoveFaces( [face] );

			VertexHandle[] newVerts = mesh.AddVertices( [.. vPos] );

			// Projected 2D position of the vertices.
			List<Vector2> projPos = new();
			foreach ( Vector3 pos in newVerts.Select( mesh.GetVertexPosition ) )
			{
				projPos.Add( Project( pos ) );
			}

			// VertexHandle indices - clipped when a triangle is made.
			List<int> polygon = new();
			for ( int i = 0; i < newVerts.Length; i++ )
			{
				polygon.Add( i );
			}

			switch ( method )
			{
				case NgonMethod.Fan:
					{
						// Loop all verts
						while ( polygon.Count > 3 )
						{
							bool foundEar = false;

							for ( int i = 0; i < polygon.Count; i++ )
							{
								if ( !IsEar( i ) )
								{
									continue;
								}

								int prev = (i - 1 + polygon.Count) % polygon.Count;
								int next = (i + 1) % polygon.Count;

								int a = polygon[prev];
								int b = polygon[i];
								int c = polygon[next];

								// Create triangle and set UVs.
								FaceHandle newFace = mesh.AddFace( newVerts[a], newVerts[b], newVerts[c] );
								newFaces.Add( newFace );

								mesh.SetFaceTextureCoords( newFace, [vUv[a], vUv[b], vUv[c]] );
								mesh.SetFaceTextureParameters( newFace, axisU, axisV, scale );
								mesh.SetFaceMaterial( newFace, mat );

								// Vertex colors/blends
								HalfEdgeHandle edge = newFace.Edge;

								mesh.SetVertexBlend( edge, vb[a] );
								mesh.SetVertexColor( edge, vc[a] );
								edge = edge.NextEdge;

								mesh.SetVertexBlend( edge, vb[b] );
								mesh.SetVertexColor( edge, vc[b] );
								edge = edge.NextEdge;

								mesh.SetVertexBlend( edge, vb[c] );
								mesh.SetVertexColor( edge, vc[c] );

								// Remove ear tip.
								polygon.RemoveAt( i );

								foundEar = true;
								break;
							}

							// Invalid/self-intersecting polygon.
							if ( !foundEar )
							{
								Log.Warning( "Ear found." );
								return;
							}
						}

						break;
					}
				case NgonMethod.Outer:
					{
						int startIndex = 0;

						// Loop all verts.
						while ( polygon.Count > 3 )
						{
							bool foundEar = false;

							for ( int offset = 0; offset < polygon.Count; offset++ )
							{
								int i = (startIndex + offset) % polygon.Count;

								if ( !IsEar( i ) )
								{
									continue;
								}

								int prev = (i - 1 + polygon.Count) % polygon.Count;
								int next = (i + 1) % polygon.Count;

								int a = polygon[prev];
								int b = polygon[i];
								int c = polygon[next];

								// Create triangle.
								FaceHandle newFace = mesh.AddFace( newVerts[a], newVerts[b], newVerts[c] );
								newFaces.Add( newFace );

								mesh.SetFaceTextureCoords( newFace, [vUv[a], vUv[b], vUv[c]] );
								mesh.SetFaceTextureParameters( newFace, axisU, axisV, scale );
								mesh.SetFaceMaterial( newFace, mat );

								// Vertex colors/blends.
								HalfEdgeHandle edge = newFace.Edge;

								mesh.SetVertexBlend( edge, vb[a] );
								mesh.SetVertexColor( edge, vc[a] );
								edge = edge.NextEdge;

								mesh.SetVertexBlend( edge, vb[b] );
								mesh.SetVertexColor( edge, vc[b] );
								edge = edge.NextEdge;

								mesh.SetVertexBlend( edge, vb[c] );
								mesh.SetVertexColor( edge, vc[c] );

								// Remove ear tip.
								polygon.RemoveAt( i );

								// Continue from the next vertex next iteration.
								startIndex = i % polygon.Count;

								foundEar = true;
								break;
							}

							if ( !foundEar )
							{
								Log.Warning( "Ear found." );
								return;
							}
						}

						break;
					}
			}

			// Final triangle.
			FaceHandle finalFace = mesh.AddFace( newVerts[polygon[0]], newVerts[polygon[1]], newVerts[polygon[2]] );
			{
				mesh.SetFaceTextureCoords( finalFace, [vUv[polygon[0]], vUv[polygon[1]], vUv[polygon[2]]] );
				mesh.SetFaceTextureParameters( finalFace, axisU, axisV, scale );
				mesh.SetFaceMaterial( finalFace, mat );
				newFaces.Add( finalFace );
			}

			// Get all the new internal edges.
			foreach ( FaceHandle newFace in newFaces )
			{
				HalfEdgeHandle[] edges = mesh.GetFaceEdges( newFace );
				foreach ( HalfEdgeHandle edge in edges )
				{
					if ( !mesh.IsEdgeOpen( edge ) )
					{
						newEdges.Add( mesh.GetEdgeLine( edge ) );
					}
				}
			}

			// Vertex colors/blends
			HalfEdgeHandle finalEdge = finalFace.Edge;

			{
				mesh.SetVertexBlend( finalEdge, vb[polygon[0]] );
				mesh.SetVertexColor( finalEdge, vc[polygon[0]] );
				finalEdge = finalEdge.NextEdge;

				mesh.SetVertexBlend( finalEdge, vb[polygon[1]] );
				mesh.SetVertexColor( finalEdge, vc[polygon[1]] );
				finalEdge = finalEdge.NextEdge;

				mesh.SetVertexBlend( finalEdge, vb[polygon[2]] );
				mesh.SetVertexColor( finalEdge, vc[polygon[2]] );
			}

			// Merge the triangulated face into the mesh.
			VertexHandle[] mergeVerts = hVertices.Concat( newVerts ).ToArray();
			{
				mesh.MergeVerticesWithinDistance( mergeVerts, 0.0001f, true, true, out _ );
				mesh.ComputeFaceTextureCoordinatesFromParameters();
			}
			return;

			bool IsEar( int current )
			{
				int prev = (current - 1 + polygon.Count) % polygon.Count;
				int next = (current + 1) % polygon.Count;

				// Must be convex.
				if ( !IsConvex( prev, current, next ) )
				{
					return false;
				}

				Vector2 a = projPos[polygon[prev]];
				Vector2 b = projPos[polygon[current]];
				Vector2 c = projPos[polygon[next]];

				// No other vertex may lie inside.
				for ( int i = 0; i < polygon.Count; i++ )
				{
					if ( i == prev || i == current || i == next )
					{
						continue;
					}

					Vector2 p = projPos[polygon[i]];

					if ( PointInTriangle( p, a, b, c ) )
					{
						return false;
					}
				}

				return true;
			}

			bool IsConvex( int prev, int current, int next )
			{
				Vector2 a = projPos[polygon[prev]];
				Vector2 b = projPos[polygon[current]];
				Vector2 c = projPos[polygon[next]];

				float cross = ((b.x - a.x) * (c.y - b.y)) - ((b.y - a.y) * (c.x - b.x));
				return cross > 0.0001f;
			}

			bool PointInTriangle( Vector2 p, Vector2 a, Vector2 b, Vector2 c )
			{
				float Sign( Vector2 p1, Vector2 p2, Vector2 p3 )
				{
					return ((p1.x - p3.x) * (p2.y - p3.y)) -
						   ((p2.x - p3.x) * (p1.y - p3.y));
				}

				bool b1 = Sign( p, a, b ) < 0.0f;
				bool b2 = Sign( p, b, c ) < 0.0f;
				bool b3 = Sign( p, c, a ) < 0.0f;

				return b1 == b2 && b2 == b3;
			}
		}
	}
}
