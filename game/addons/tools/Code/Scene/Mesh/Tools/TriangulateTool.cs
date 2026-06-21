using HalfEdgeMesh;

namespace Editor.MeshEditor;

[Alias( "tools.triangulate-tool" )]
public partial class TriangulateTool( MeshFace[] faces ) : EditorTool
{
	readonly Dictionary<MeshComponent, PolygonMesh> _originalMeshes = [];
	readonly Dictionary<MeshComponent, List<FaceHandle>> _remappedFaces = [];
	
	readonly Dictionary<MeshComponent, List<FaceHandle>> _triangulatedFaces = [];
	readonly Dictionary<MeshComponent, List<Line>> _interiorEdgeLines = [];
	
	public enum QuadMethod
	{
		Fixed,
		FixedAlternate,
		ShortestDiagonal,
		LongestDiagonal,
	}

	public enum NgonMethod
	{
		Fan,
		Outer,
	}
	
	public override void OnEnabled()
	{
		if ( faces is not { Length: > 0 } ) return;

		foreach ( var group in faces.GroupBy( f => f.Component ) )
		{
			var component = group.Key;
			if ( !component.IsValid() ) continue;
			
			var originalMesh = new PolygonMesh { Transform = component.Mesh.Transform };
			originalMesh.MergeMesh( component.Mesh, Transform.Zero, out _, out _, out var faceMap );
			
			_originalMeshes[component] = originalMesh;
			_remappedFaces[component] = group
				.Select( f => faceMap.TryGetValue( f.Handle, out var mapped ) ? mapped : default )
				.Where( f => f.IsValid )
				.ToList();
		}
	}

	public override void OnDisabled() => RestoreOriginals();
	
	public override void OnUpdate()
	{
		foreach ( var (component, lines) in _interiorEdgeLines )
		{
			if ( !component.IsValid() || lines.Count == 0 ) continue;
			
			using ( Gizmo.ObjectScope( component.GameObject, component.WorldTransform ) )
			{
				using ( Gizmo.Scope( "TriangulationPreview" ) )
				{
					Gizmo.Draw.Color = new Color( 0.3137f, 0.7843f, 1f, 0.5f );
					Gizmo.Draw.LineThickness = 2;

					foreach ( var line in lines )
					{
						Gizmo.Draw.Line( line );
					}
				}

				if ( _originalMeshes.TryGetValue( component, out var originalMesh ) && _remappedFaces.TryGetValue( component, out var remapped ) )
				{
					using ( Gizmo.Scope( "SelectionOutline" ) )
					{
						Gizmo.Draw.Color = new Color( 1f, 0.8f, 0.2f, 0.6f );
						Gizmo.Draw.LineThickness = 1;

						foreach ( var face in remapped )
						{
							if ( !face.IsValid ) continue;

							foreach ( var edge in originalMesh.GetFaceEdges( face ) )
							{
								originalMesh.GetEdgeVertexPositions( edge, Transform.Zero, out var a, out var b );
								Gizmo.Draw.Line( a, b );
							}
						}
					}
				}
			}
		}
	}

	public void UpdateTriangulation( QuadMethod quadMethod, NgonMethod ngonMethod, int minTriangles )
	{
		_triangulatedFaces.Clear();
		_interiorEdgeLines.Clear();

		foreach ( var (component, originalMesh) in _originalMeshes )
		{
			if ( !component.IsValid() ) continue;

			var mesh = new PolygonMesh { Transform = originalMesh.Transform };
			mesh.MergeMesh( originalMesh, Transform.Zero, out _, out _, out var faceMap );
			
			var facesToTriangulate = _remappedFaces[component]
				.Select( f => faceMap.TryGetValue( f, out var mapped ) ? mapped : default )
				.Where( f => f.IsValid )
				.ToArray();
			
			if ( facesToTriangulate.Length == 0 )
			{
				component.Mesh = mesh;
				continue;
			}

			List<Line> lines = [];
			List<FaceHandle> triangulateFaces = [];
			foreach ( var face in facesToTriangulate )
			{
				if ( mesh.GetFaceEdges( face ).Length < minTriangles ) 
					continue;

				if ( mesh.GetFaceEdges( face ).Length == 4 )
				{
					TriangulateQuad( TriangulateQuadMethod, mesh, face, out var newFaces, out var newEdge );
					triangulateFaces.AddRange( newFaces );
					lines.Add( mesh.GetEdgeLine( newEdge ) );
				}
				else
				{
					TriangulateNgon( TriangulateNgonMethod, mesh, face, out var newFaces, out var newEdges );
					triangulateFaces.AddRange( newFaces );
					lines.AddRange( newEdges.Select( newEdge => mesh.GetEdgeLine( newEdge ) ) );
				}
			}

			_triangulatedFaces[component] = triangulateFaces;
			_interiorEdgeLines[component] = lines;
			
			component.Mesh = mesh;
		}
	}
	
	public void Apply()
	{
		var components = _originalMeshes.Keys.Where( c => c.IsValid() ).ToArray();
		if ( components.Length == 0 ) return;
		
		var resultMeshes = components.ToDictionary( c => c, c => c.Mesh );
		RestoreOriginals();
		
		using var scope = SceneEditorSession.Scope();

		using ( SceneEditorSession.Active.UndoScope( "Triangulate Faces" )
			       .WithComponentChanges( components )
			       .Push() )
		{
			var selection = SceneEditorSession.Active.Selection;
			selection.Clear();
			
			foreach ( var component in components )
			{
				component.Mesh = resultMeshes[component];

				if ( !_triangulatedFaces.TryGetValue( component, out var triangulatedFaces ) ) continue;

				foreach ( var face in triangulatedFaces.Where( f => f.IsValid ) )
					selection.Add( new MeshFace( component, face ) );
			}
		}
		
		Cleanup();
		EditorToolManager.SetSubTool( nameof( FaceTool ) );
	}

	public void Cancel()
	{
		RestoreOriginals();
		Cleanup();
		EditorToolManager.SetSubTool( nameof( FaceTool ) );
	}
	
	void RestoreOriginals()
	{
		foreach ( var (component, originalMesh) in _originalMeshes )
		{
			if ( component.IsValid() )
				component.Mesh = originalMesh;
		}
	}

	void Cleanup()
	{
		_originalMeshes.Clear();
		_remappedFaces.Clear();
		
		_triangulatedFaces.Clear();
		_interiorEdgeLines.Clear();
	}

	static void TriangulateQuad( QuadMethod method, PolygonMesh mesh, FaceHandle face, out List<FaceHandle> newFaces, out HalfEdgeHandle newEdge )
	{
		var mat = mesh.GetFaceMaterial( face );
		var hVertices = mesh.GetFaceVertices( face );

		List<Color32> vb = [];
		List<Color32> vc = [];
		
		// Walk the edge, getting the VertexBlend and VertexColor at each edge.
		var startEdge = face.Edge;
		var currentEdge = startEdge;
		do
		{
			vb.Add( mesh.GetVertexBlend( currentEdge ) );
			vc.Add( mesh.GetVertexColor( currentEdge ) );
			currentEdge = currentEdge.NextEdge;
			
		} while ( currentEdge != startEdge );
		
		Vector2[] UVs = mesh.GetFaceTextureCoords( face );
		mesh.GetFaceTextureParameters( face, out Vector4 axisU, out Vector4 axisV, out Vector2 scale );

		var vPos = new List<Vector3>();
		foreach ( var hVertex in hVertices )
		{
			vPos.Add( mesh.GetVertexPosition( hVertex ) );
		}
		
		mesh.RemoveFaces( [ face ] );

		var newVerts = mesh.AddVertices( [..vPos] );

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
		
		var mergeVerts = hVertices.Concat( newVerts ).ToArray();
		
		mesh.MergeVerticesWithinDistance( mergeVerts, 0.0001f, true, true, out _ );
		mesh.ComputeFaceTextureCoordinatesFromParameters();
		
		var edges1 = mesh.GetFaceEdges( tri1 );
		var edges2 = mesh.GetFaceEdges( tri2 );

		newFaces = [ tri1, tri2 ];
		newEdge = edges1.Intersect( edges2 ).FirstOrDefault();
	}
	
	static void TriangulateNgon( NgonMethod method, PolygonMesh mesh, FaceHandle face, out List<FaceHandle> newFaces, out List<HalfEdgeHandle> newEdges )
	{
		newFaces = [];
		newEdges = [];
		
		var mat = mesh.GetFaceMaterial( face );
		
		var hVertices = mesh.GetFaceVertices( face );
		
		List<Color32> vb = [];
		List<Color32> vc = [];
		
		// Walk the edge, getting the VertexBlend and VertexColor at each edge.
		var startEdge = face.Edge;
		var currentEdge = startEdge;

		do
		{
			vb.Add( mesh.GetVertexBlend( currentEdge ) );
			vc.Add( mesh.GetVertexColor( currentEdge ) );
			currentEdge = currentEdge.NextEdge;
			
		} while ( currentEdge != startEdge );

		var vPos = new List<Vector3>();
		foreach ( var hVertex in hVertices )
		{
			vPos.Add( mesh.GetVertexPosition( hVertex ) );
		}
		
		var vUv = mesh.GetFaceTextureCoords( face );
		mesh.GetFaceTextureParameters( face, out Vector4 axisU, out Vector4 axisV, out Vector2 scale );
		
		// Get face normal, tangent and bitangent for 2D projection.
		mesh.ComputeFaceNormal( face, out var normal );
		Vector3 reference = MathF.Abs( normal.z ) < 0.99f ? Vector3.Up : Vector3.Right;
		
		var tangent = normal.Cross( reference ).Normal;
		var bitangent = normal.Cross( tangent ).Normal;
		
		// Returns 2D projected vertex.
		Vector2 Project( Vector3 p ) { return new Vector2( p.Dot( tangent ), p.Dot( bitangent ) ); }
		
		mesh.RemoveFaces( [ face ] );
		
		var newVerts = mesh.AddVertices( [..vPos] );
		
		// Projected 2D position of the vertices.
		var projPos = new List<Vector2>();
		foreach ( var pos in newVerts.Select( mesh.GetVertexPosition ) )
		{
			projPos.Add( Project( pos ) );
		}

		// VertexHandle indices - clipped when a triangle is made.
		var polygon = new List<int>();
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
							continue;

						int prev = (i - 1 + polygon.Count) % polygon.Count;
						int next = (i + 1) % polygon.Count;

						int a = polygon[prev];
						int b = polygon[i];
						int c = polygon[next];

						// Create triangle and set UVs.
						var newFace = mesh.AddFace( newVerts[a], newVerts[b], newVerts[c] );
						newFaces.Add( newFace );
						
						mesh.SetFaceTextureCoords( newFace, [vUv[a], vUv[b], vUv[c]] );
						mesh.SetFaceTextureParameters( newFace, axisU, axisV, scale );
						mesh.SetFaceMaterial( newFace, mat );
				
						// Vertex colors/blends
						var edge = newFace.Edge;

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
								continue;

							int prev = (i - 1 + polygon.Count) % polygon.Count;
							int next = (i + 1) % polygon.Count;

							int a = polygon[prev];
							int b = polygon[i];
							int c = polygon[next];

							// Create triangle.
							var newFace = mesh.AddFace( newVerts[a], newVerts[b], newVerts[c] );
							newFaces.Add( newFace );
							
							mesh.SetFaceTextureCoords( newFace, [ vUv[a], vUv[b], vUv[c] ] );
							mesh.SetFaceTextureParameters( newFace, axisU, axisV, scale );
							mesh.SetFaceMaterial( newFace, mat );

							// Vertex colors/blends.
							var edge = newFace.Edge;

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
			default:
				{
					break;
				}
		}
		
		// Final triangle.
		var finalFace = mesh.AddFace( newVerts[ polygon[ 0 ]], newVerts[ polygon[ 1 ]], newVerts[ polygon[ 2 ] ] );
		{
			mesh.SetFaceTextureCoords( finalFace, [vUv[polygon[0]], vUv[polygon[1]], vUv[polygon[2]]] );
			mesh.SetFaceTextureParameters( finalFace, axisU, axisV, scale );
			mesh.SetFaceMaterial( finalFace, mat );
			newFaces.Add( finalFace );
		}
		
		// Get all the new internal edges.
		foreach ( var newFace in newFaces )
		{
			var edges = mesh.GetFaceEdges( newFace );
			foreach ( var edge in edges )
			{
				if ( !mesh.IsEdgeOpen( edge ) ) newEdges.Add( edge );
			}
		}
		
		// Vertex colors/blends
		var finalEdge = finalFace.Edge;

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
		var mergeVerts = hVertices.Concat( newVerts ).ToArray();
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
				return false;

			var a = projPos[ polygon[prev] ];
			var b = projPos[ polygon[current] ];
			var c = projPos[ polygon[next] ];
			
			// No other vertex may lie inside.
			for ( int i = 0; i < polygon.Count; i++ )
			{
				if ( i == prev || i == current || i == next )
					continue;

				var p = projPos[ polygon[i] ];

				if ( PointInTriangle( p, a, b, c ) )
					return false;
			}

			return true;
		}
		
		bool IsConvex( int prev, int current, int next )
		{
			var a = projPos[ polygon[ prev ] ];
			var b = projPos[ polygon[ current ] ];
			var c = projPos[ polygon[ next ] ];
			
			float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
			return cross > 0.0001f;
		}

		bool PointInTriangle( Vector2 p, Vector2 a, Vector2 b, Vector2 c )
		{
			float Sign( Vector2 p1, Vector2 p2, Vector2 p3 )
			{
				return (p1.x - p3.x) * (p2.y - p3.y) -
				       (p2.x - p3.x) * (p1.y - p3.y);
			}

			bool b1 = Sign( p, a, b ) < 0.0f;
			bool b2 = Sign( p, b, c ) < 0.0f;
			bool b3 = Sign( p, c, a ) < 0.0f;

			return (b1 == b2) && (b2 == b3);
		}
	}
}
