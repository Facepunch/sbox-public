using HalfEdgeMesh;

namespace Editor.MeshEditor;

[Alias( "tools.topology-tool" )]
public partial class TopologyTool( MeshFace[] faces ) : EditorTool
{
	public enum TopologyOperationType
	{
		Triangulate,
		Quadrangulate
	}

	public enum NgonMethod
	{
		Fan,
		Outer
	}

	public enum QuadMethod
	{
		Fixed,
		FixedAlternate,
		ShortestDiagonal,
		LongestDiagonal
	}

	private readonly Dictionary<MeshComponent, PolygonMesh> _originalMeshes = [];
	private readonly Dictionary<MeshComponent, List<FaceHandle>> _remappedFaces = [];
	private readonly Dictionary<MeshComponent, List<FaceHandle>> _processedFaces = [];
	private readonly Dictionary<MeshComponent, List<Line>> _processedEdges = [];

	public override void OnEnabled()
	{
		if ( faces is not { Length: > 0 } )
		{
			return;
		}

		foreach ( IGrouping<MeshComponent, MeshFace> group in faces.GroupBy( f => f.Component ) )
		{
			MeshComponent component = group.Key;
			if ( !component.IsValid() )
			{
				continue;
			}

			PolygonMesh originalMesh = new() { Transform = component.Mesh.Transform };
			originalMesh.MergeMesh( component.Mesh, Transform.Zero, out _, out _,
				out Dictionary<FaceHandle, FaceHandle> faceMap );

			_originalMeshes[component] = originalMesh;
			_remappedFaces[component] = group
				.Select( f => faceMap.TryGetValue( f.Handle, out FaceHandle mapped ) ? mapped : default )
				.Where( f => f.IsValid )
				.ToList();
		}
	}

	public override void OnDisabled()
	{
		RestoreOriginals();
	}

	public override void OnUpdate()
	{
		foreach ( (MeshComponent component, List<Line> lines) in _processedEdges )
		{
			if ( !component.IsValid() || lines.Count == 0 )
			{
				continue;
			}

			using ( Gizmo.ObjectScope( component.GameObject, component.WorldTransform ) )
			{
				using ( Gizmo.Scope( "TopologyPreview" ) )
				{
					switch ( ToolOperationType )
					{
						case TopologyOperationType.Triangulate:
							Gizmo.Draw.Color = new Color( 0.3137f, 0.7843f, 1f, 0.5f );
							break;
						case TopologyOperationType.Quadrangulate:
							Gizmo.Draw.Color = new Color( 1.0f, 0.0f, 0.0f, 0.5f );
							break;
					}

					Gizmo.Draw.LineThickness = 2;

					foreach ( Line line in lines )
					{
						Gizmo.Draw.Line( line );
					}
				}

				if ( ToolOperationType == TopologyOperationType.Triangulate )
				{
					if ( _originalMeshes.TryGetValue( component, out PolygonMesh originalMesh ) &&
					     _remappedFaces.TryGetValue( component, out List<FaceHandle> remapped ) )
					{
						using ( Gizmo.Scope( "SelectionOutline" ) )
						{
							Gizmo.Draw.Color = new Color( 1f, 0.8f, 0.2f, 0.6f );
							Gizmo.Draw.LineThickness = 1;

							foreach ( FaceHandle face in remapped )
							{
								if ( !face.IsValid )
								{
									continue;
								}

								foreach ( HalfEdgeHandle edge in originalMesh.GetFaceEdges( face ) )
								{
									originalMesh.GetEdgeVertexPositions( edge, Transform.Zero, out Vector3 a,
										out Vector3 b );
									Gizmo.Draw.Line( a, b );
								}
							}
						}
					}
				}

				if ( ToolOperationType == TopologyOperationType.Quadrangulate )
				{
					using ( Gizmo.Scope( "SelectionOutline" ) )
					{
						Gizmo.Draw.Color = new Color( 1f, 0.8f, 0.2f, 0.6f );
						Gizmo.Draw.LineThickness = 1;

						foreach ( (MeshComponent comp, PolygonMesh mesh) in _originalMeshes )
						{
							if ( !component.IsValid() ) continue;

							foreach ( var edge in comp.Mesh.HalfEdgeHandles )
							{
								comp.Mesh.GetEdgeVertexPositions( edge, Transform.Zero, out Vector3 a,
									out Vector3 b );
								Gizmo.Draw.Line( a, b );
							}
						}
					}
				}
			}
		}
	}

	public void UpdateTopology( TopologyProperties properties )
	{
		_processedFaces.Clear();
		_processedEdges.Clear();

		foreach ( (MeshComponent component, PolygonMesh originalMesh) in _originalMeshes )
		{
			if ( !component.IsValid() )
			{
				continue;
			}

			PolygonMesh mesh = new() { Transform = originalMesh.Transform };
			mesh.MergeMesh( originalMesh, Transform.Zero, out _, out _,
				out Dictionary<FaceHandle, FaceHandle> faceMap );

			FaceHandle[] facesToProcess = _remappedFaces[component]
				.Select( f => faceMap.GetValueOrDefault( f ) )
				.Where( f => f.IsValid )
				.ToArray();

			if ( facesToProcess.Length == 0 )
			{
				component.Mesh = mesh;
				continue;
			}

			switch ( properties.OperationType )
			{
				case TopologyOperationType.Triangulate:
					{
						TriangulateFaces( mesh, facesToProcess, properties.QuadMethod,
							properties.NgonMethod, properties.MinimumVertices,
							out List<FaceHandle> faces, out List<Line> edges );

						_processedFaces[component] = faces;
						_processedEdges[component] = edges;
						break;
					}
				case TopologyOperationType.Quadrangulate:
					{
						QuadrangulateFaces( mesh, facesToProcess, properties.MaxFaceAngle,
							properties.MaxShapeAngle, properties.CompareUVs,
							properties.CompareVertexColor, properties.CompareVertexBlend,
							properties.CompareFaceMaterial, properties.CompareSmoothing,
							out List<FaceHandle> faces, out List<Line> edges );

						_processedFaces[component] = faces;
						_processedEdges[component] = edges;
						break;
					}
			}

			component.Mesh = mesh;
		}
	}

	public void Apply()
	{
		MeshComponent[] components = _originalMeshes.Keys.Where( c => c.IsValid() ).ToArray();
		if ( components.Length == 0 )
		{
			return;
		}

		Dictionary<MeshComponent, PolygonMesh> resultMeshes = components.ToDictionary( c => c, c => c.Mesh );
		RestoreOriginals();

		using IDisposable scope = SceneEditorSession.Scope();

		string name = "Topology Operation";
		switch ( ToolOperationType )
		{
			case TopologyOperationType.Triangulate: name = "Triangulate"; break;
			case TopologyOperationType.Quadrangulate: name = "Quadrangulate"; break;
		}

		using ( SceneEditorSession.Active.UndoScope( name )
			       .WithComponentChanges( components )
			       .Push() )
		{
			SelectionSystem selection = SceneEditorSession.Active.Selection;
			selection.Clear();

			foreach ( MeshComponent component in components )
			{
				component.Mesh = resultMeshes[component];

				if ( !_processedFaces.TryGetValue( component, out List<FaceHandle> processedFaces ) )
				{
					continue;
				}

				foreach ( FaceHandle face in processedFaces.Where( f => f.IsValid ) )
				{
					selection.Add( new MeshFace( component, face ) );
				}
			}
		}

		Cleanup();
		EditorToolManager.SetSubTool( nameof(FaceTool) );
	}

	public void Cancel()
	{
		RestoreOriginals();
		Cleanup();
		EditorToolManager.SetSubTool( nameof(FaceTool) );
	}

	private void RestoreOriginals()
	{
		foreach ( (MeshComponent component, PolygonMesh originalMesh) in _originalMeshes )
		{
			if ( component.IsValid() )
			{
				component.Mesh = originalMesh;
			}
		}
	}

	private void Cleanup()
	{
		_originalMeshes.Clear();
		_remappedFaces.Clear();

		_processedFaces.Clear();
		_processedEdges.Clear();
	}
}
