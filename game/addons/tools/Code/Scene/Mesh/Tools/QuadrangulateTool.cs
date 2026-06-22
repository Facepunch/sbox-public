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
	
	public void UpdateQuadrangulate( float faceAngle, float shapeAngle, float topology, bool uv, bool color, bool blend, bool material, bool smooth )
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
			
			QuadrangulateFaces( mesh, facesToQuad, faceAngle, shapeAngle, topology, uv, color, blend, material, smooth, out var newFaces, out var removedEdges );

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

				if ( !_quadFaces.TryGetValue( component, out var insetFaces ) ) continue;

				foreach ( var face in insetFaces.Where( f => f.IsValid ) )
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

	static void QuadrangulateFaces( PolygonMesh mesh, FaceHandle[] faces, float faceAngle, float shapeAngle,
		float topology, bool uv, bool color, bool blend, bool material, bool smooth, out List<FaceHandle> newFaces, out List<Line> removedEdges )
	{
		newFaces = [];
		removedEdges = [];

		// Get faces that share an edge, score them based on how viable they are to quadrangulate.
		foreach ( var face in faces )
		{
			// Get neighbouring faces that were selected during the operation.
			List<FaceHandle> neighbourFaces = [];
			
			mesh.GetFacesConnectedToFace( face, out var connectedFaces );
			neighbourFaces.AddRange( connectedFaces.Where( connectedFace => faces.Contains( connectedFace ) ) );

			foreach ( var neighbourFace in neighbourFaces )
			{
				mesh.GetEdgesConnectedToFace( face, out var faceEdges );
				mesh.GetEdgesConnectedToFace( neighbourFace, out var neighbourEdges );

				var sharedEdge = faceEdges.First( edge => neighbourEdges.Contains( edge ) );
				
				// Check if faces are valid for scoring according to properties.
				if ( smooth && mesh.GetEdgeSmoothing( sharedEdge ) == PolygonMesh.EdgeSmoothMode.Hard ) continue;
				
				if ( uv )
				{
					mesh.GetEdgeVertices( sharedEdge, out var sharedVertA, out var sharedVertB );
					
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
				
				if ( material && mesh.GetFaceMaterial( face ) != mesh.GetFaceMaterial( neighbourFace ) ) continue;

				var faceNormal = GetFaceNormal( face );
				var neighbourNormal = GetFaceNormal( neighbourFace );

				float degree = MathF.Acos( Vector3.Dot( faceNormal, neighbourNormal ) ).RadianToDegree();
				if ( degree >= QuadrangulateMaxFaceAngle ) continue;
				
				removedEdges.Add( mesh.GetEdgeLine( sharedEdge ) );
			}
		}

		Vector3 GetFaceNormal( FaceHandle face )
		{
			var vhs = mesh.GetFaceVertices( face );
			
			Vector3 a = mesh.GetVertexPosition( vhs[0] );
			Vector3 b = mesh.GetVertexPosition( vhs[1] );
			Vector3 c = mesh.GetVertexPosition( vhs[2] );

			Vector3 normal = Vector3.Cross( b - a, c - a ).Normal;
			return normal;
		}
	}
}
