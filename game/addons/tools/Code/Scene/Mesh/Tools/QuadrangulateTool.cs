using HalfEdgeMesh;

namespace Editor.MeshEditor;

public partial class QuadrangulateTool( MeshFace[] faces ) : EditorTool
{
	readonly Dictionary<MeshComponent, PolygonMesh> _originalMeshes = [];
	readonly Dictionary<MeshComponent, List<FaceHandle>> _remappedFaces = [];
	
	readonly Dictionary<MeshComponent, List<FaceHandle>> _quadFaces = [];
	readonly Dictionary<MeshComponent, List<Line>> _removedEdgeLines = [];
	
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
		foreach ( var (component, lines) in _removedEdgeLines )
		{
			if ( !component.IsValid() || lines.Count == 0 ) continue;
			
			using ( Gizmo.ObjectScope( component.GameObject, component.WorldTransform ) )
			{
				using ( Gizmo.Scope( "QuadrangulatePreview" ) )
				{
					Gizmo.Draw.Color = new Color( 1.0f, 0.0f, 0f, 0.5f );
					Gizmo.Draw.LineThickness = 2;

					foreach ( var line in lines )
						Gizmo.Draw.Line( line );
				}
			}
		}
	}
	
	public void UpdateQuadrangulate( float faceAngle, float shapeAngle, bool uv, bool color, bool blend, bool material, bool smooth )
	{
		_quadFaces.Clear();
		_removedEdgeLines.Clear();

		foreach ( var (component, originalMesh) in _originalMeshes )
		{
			if ( !component.IsValid() ) continue;

			var mesh = new PolygonMesh { Transform = originalMesh.Transform };
			mesh.MergeMesh( originalMesh, Transform.Zero, out _, out _, out var faceMap );

			var facesToQuad = _remappedFaces[component]
				.Select( f => faceMap.GetValueOrDefault(f) )
				.Where( f => f.IsValid )
				.ToArray();

			if ( facesToQuad.Length == 0 )
			{
				component.Mesh = mesh;
				continue;
			}
			
			QuadrangulateFaces( mesh, facesToQuad, faceAngle, shapeAngle, uv, color, blend, material, smooth, out var newFaces, out var removedEdges);

			_quadFaces[component] = newFaces;
			_removedEdgeLines[component] = removedEdges;
			
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

		using ( SceneEditorSession.Active.UndoScope( "Quadrangulate Faces" )
			       .WithComponentChanges( components )
			       .Push() )
		{
			var selection = SceneEditorSession.Active.Selection;
			selection.Clear();

			foreach ( var component in components )
			{
				component.Mesh = resultMeshes[component];

				if ( !_quadFaces.TryGetValue( component, out var quadFaces ) ) continue;

				foreach ( var face in quadFaces.Where( f => f.IsValid ) )
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
		_quadFaces.Clear();
		_removedEdgeLines.Clear();
	}

	static void QuadrangulateFaces( PolygonMesh mesh, FaceHandle[] faces, float faceAngle, float shapeAngle, bool uv, bool color, bool blend, bool material, bool smooth, out List<FaceHandle> newFaces, out List<Line> removedEdges )
	{
		newFaces = [];
		removedEdges = [];

		List<FacePair> facePairs = [];

		// Get faces that share an edge, score them based on how viable they are to quadrangulate.
		foreach ( var face in faces )
		{
			if (mesh.GetFaceEdges( face ).Length > 3) continue;
			
			// Get neighbouring faces that were selected when opening the tool.
			List<FaceHandle> neighbourFaces = [];
			
			mesh.GetFacesConnectedToFace( face, out var connectedFaces );
			neighbourFaces.AddRange( connectedFaces.Where( connectedFace => faces.Contains( connectedFace ) ) );

			// Get the edge shared between the faces.
			foreach ( var neighbourFace in neighbourFaces )
			{
				if (mesh.GetFaceEdges( neighbourFace ).Length > 3) continue;
				
				// Skip checks if processed two faces in an earlier loop.
				if (facePairs.Exists( x => x.FaceB == face && x.FaceA == neighbourFace )) continue;
				
				var faceEdges = mesh.GetFaceEdges( face );
				var neighbourEdges = mesh.GetFaceEdges( neighbourFace );

				var sharedEdge = faceEdges.First( edge => neighbourEdges.Contains( edge ) );
				mesh.GetEdgeVertices( sharedEdge, out var sharedVertA, out var sharedVertB );
				
				// Check if faces pass checks prior to scoring.
				{
					if ( smooth && mesh.GetEdgeSmoothing( sharedEdge ) == PolygonMesh.EdgeSmoothMode.Hard ) continue;
				}
				
				if ( uv )
				{
					var faceUVs = mesh.GetFaceTextureCoords( face );
					var faceVerts = mesh.GetFaceVertices( face );
					
					int faceIndexA = Array.IndexOf( faceVerts, sharedVertA );
					int faceIndexB = Array.IndexOf( faceVerts, sharedVertB );
					
					var neighbourUVs = mesh.GetFaceTextureCoords( neighbourFace );
					var neighbourVerts = mesh.GetFaceVertices( neighbourFace );
					
					int neighbourIndexA = Array.IndexOf( neighbourVerts, sharedVertA );
					int neighbourIndexB = Array.IndexOf( neighbourVerts, sharedVertB );
					
					const float margin = 0.0001f;
					
					bool matchA = faceUVs[faceIndexA].AlmostEqual( neighbourUVs[neighbourIndexA], margin );
					bool matchB = faceUVs[faceIndexB].AlmostEqual( neighbourUVs[neighbourIndexB], margin );

					bool match = matchA && matchB;
					
					if ( !match ) continue;
				}

				if ( color )
				{
					bool match = mesh.GetVertexColor( sharedEdge ) ==
					             mesh.GetVertexColor( sharedEdge.OppositeEdge.NextEdge.NextEdge ) &&
					             mesh.GetVertexColor( sharedEdge.OppositeEdge ) ==
					             mesh.GetVertexColor( sharedEdge.NextEdge.NextEdge );
					if ( !match ) continue;
				}

				if ( blend )
				{
					bool match = mesh.GetVertexBlend( sharedEdge ) ==
					             mesh.GetVertexBlend( sharedEdge.OppositeEdge.NextEdge.NextEdge ) &&
					             mesh.GetVertexBlend( sharedEdge.OppositeEdge ) ==
					             mesh.GetVertexBlend( sharedEdge.NextEdge.NextEdge );
					if ( !match ) continue;
				}

				{
					if ( material && mesh.GetFaceMaterial( face ) != mesh.GetFaceMaterial( neighbourFace ) ) continue;
				}

				float normal;
				{
					var faceNormal = GetFaceNormal( face );
					var neighbourNormal = GetFaceNormal( neighbourFace );

					float degree = MathF.Acos( Vector3.Dot( faceNormal, neighbourNormal ) ).RadianToDegree();
					if ( degree >= faceAngle ) continue;
					
					normal = ( degree / faceAngle );
				}

				float shape;
				{
					(float a, float b) = GetFaceShape( sharedEdge );

					var maxAngle = 90 + shapeAngle;
					var minAngle = 90 - shapeAngle;

					if ( a < minAngle || a > maxAngle ) continue;
					if ( b < minAngle || b > maxAngle ) continue;

					var limitA = Math.Min( Math.Abs( maxAngle - a ), Math.Abs( minAngle - a ) );
					var limitB = Math.Min( Math.Abs( maxAngle - b ), Math.Abs( minAngle - b ) );

					var max = Math.Min( limitA, limitB );
					shape = ( max / shapeAngle );
				}

				float area;
				{
					area = GetSharedFaceArea( face, neighbourFace );
				}
				
				facePairs.Add( new FacePair( face, neighbourFace, sharedEdge ) { Normal = normal, Shape = shape, Area = area } );
			}
		}

		// Order face pairs by score, start removing the highest scoring until there is no more.
		List<HalfEdgeHandle> edgesToRemove = [];
		while ( facePairs.Count > 0 )
		{
			var e = facePairs.OrderBy( x => x.Score ).Last();
			edgesToRemove.Add( e.SharedEdge );

			facePairs.RemoveAll(x =>
				x.FaceA == e.FaceA || x.FaceA == e.FaceB ||
				x.FaceB == e.FaceA || x.FaceB == e.FaceB
			);
			
			facePairs.Remove( e );
		}

		removedEdges.AddRange( edgesToRemove.Select( e => mesh.GetEdgeLine( e ) ) );
		mesh.DissolveEdges( edgesToRemove, false, PolygonMesh.DissolveRemoveVertexCondition.None );

		float GetFaceArea( FaceHandle face )
		{
			var vhs = mesh.GetFaceVertices( face );
			
			Vector3 a = mesh.GetVertexPosition( vhs[0] );
			Vector3 b = mesh.GetVertexPosition( vhs[1] );
			Vector3 c = mesh.GetVertexPosition( vhs[2] );

			float area = Vector3.Cross( b - a, c - a ).Length * 0.5f;
			return area;
		}

		float GetSharedFaceArea( FaceHandle face, FaceHandle neighbourFace ) =>
			GetFaceArea( face ) + GetFaceArea( neighbourFace );
		
		Vector3 GetFaceNormal( FaceHandle face )
		{
			var vhs = mesh.GetFaceVertices( face );
			
			Vector3 a = mesh.GetVertexPosition( vhs[0] );
			Vector3 b = mesh.GetVertexPosition( vhs[1] );
			Vector3 c = mesh.GetVertexPosition( vhs[2] );

			var normal = Vector3.Cross( b - a, c - a ).Normal;
			return normal;
		}
		
		(float a, float b) GetFaceShape ( HalfEdgeHandle edge )
		{
			mesh.GetEdgeVertices( edge, out var hVertexA, out var hVertexB );
			var hVertexC = mesh.GetNextVertexInFace( edge ).Vertex;
			var hVertexD = mesh.GetNextVertexInFace( edge.OppositeEdge ).Vertex;

			float a;
			float b;

			// Corner A
			{
				var quadADirA = ( mesh.GetVertexPosition( hVertexC ) - mesh.GetVertexPosition( hVertexA )).Normal;
				var quadADirB = ( mesh.GetVertexPosition( hVertexD ) - mesh.GetVertexPosition( hVertexA )).Normal;
				
				var dot = Vector3.Dot(  quadADirA, quadADirB ).Clamp( -1.0f, 1.0f );
				var sharedVertADegrees = MathF.Acos( dot) * 180.0f / MathF.PI;
				
				a = sharedVertADegrees;
			}
			
			// Corner B
			{
				var quadBDirA = ( mesh.GetVertexPosition( hVertexC ) - mesh.GetVertexPosition( hVertexB )).Normal;
				var quadBDirB = ( mesh.GetVertexPosition( hVertexD ) - mesh.GetVertexPosition( hVertexB )).Normal;
				
				var dot = Vector3.Dot(  quadBDirA, quadBDirB ).Clamp( -1.0f, 1.0f );
				var sharedVertBDegrees = MathF.Acos( dot) * 180.0f / MathF.PI;
				
				b = sharedVertBDegrees;
			}

			return (a, b);
		}
	}
	
	private struct FacePair( FaceHandle face, FaceHandle neighbourFace, HalfEdgeHandle edge )
	{
		public FaceHandle FaceA { get; } = face;
		public FaceHandle FaceB { get; } = neighbourFace;
		public HalfEdgeHandle SharedEdge { get; } = edge;
		
		public float Shape { get; set; }
		public float Normal { get; set; }
		public float Area { get; set; }
		
		public float Score => (Shape + Normal);
	}
}
