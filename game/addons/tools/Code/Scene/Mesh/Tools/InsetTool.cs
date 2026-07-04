using HalfEdgeMesh;

namespace Editor.MeshEditor;

[Alias( "tools.inset-tool" )]
public partial class InsetTool( MeshFace[] faces ) : EditorTool
{
	readonly Dictionary<MeshComponent, PolygonMesh> _originalMeshes = [];
	readonly Dictionary<MeshComponent, List<FaceHandle>> _remappedFaces = [];
	readonly Dictionary<MeshComponent, List<FaceHandle>> _insetFaces = [];
	readonly Dictionary<MeshComponent, List<(Vector3 A, Vector3 B)>> _insetEdgeLines = [];

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
		foreach ( var (component, lines) in _insetEdgeLines )
		{
			if ( !component.IsValid() || lines.Count == 0 ) continue;

			using ( Gizmo.ObjectScope( component.GameObject, component.WorldTransform ) )
			{
				using ( Gizmo.Scope( "InsetPreview" ) )
				{
					Gizmo.Draw.Color = new Color( 0.3137f, 0.7843f, 1f, 0.5f );
					Gizmo.Draw.LineThickness = 2;

					foreach ( var (a, b) in lines )
						Gizmo.Draw.Line( a, b );
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

	public void UpdateInset( float amount, int steps )
	{
		_insetFaces.Clear();
		_insetEdgeLines.Clear();

		foreach ( var (component, originalMesh) in _originalMeshes )
		{
			if ( !component.IsValid() ) continue;

			var mesh = new PolygonMesh { Transform = originalMesh.Transform };
			mesh.MergeMesh( originalMesh, Transform.Zero, out _, out _, out var faceMap );

			var facesToInset = _remappedFaces[component]
				.Select( f => faceMap.TryGetValue( f, out var mapped ) ? mapped : default )
				.Where( f => f.IsValid )
				.ToArray();

			if ( facesToInset.Length == 0 )
			{
				component.Mesh = mesh;
				continue;
			}

			if ( mesh.InsetFaces( facesToInset, amount, steps, out var result ) )
			{
				_insetFaces[component] = result.InsetFaces;

				var lines = new List<(Vector3 A, Vector3 B)>();

				foreach ( var face in result.InsetFaces.Concat( result.ConnectingFaces ) )
				{
					if ( !face.IsValid ) continue;

					foreach ( var edge in mesh.GetFaceEdges( face ) )
					{
						mesh.GetEdgeVertexPositions( edge, Transform.Zero, out var a, out var b );
						lines.Add( (a, b) );
					}
				}

				_insetEdgeLines[component] = lines;
			}

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

		using ( SceneEditorSession.Active.UndoScope( "Inset Faces" )
			.WithComponentChanges( components )
			.Push() )
		{
			var selection = SceneEditorSession.Active.Selection;
			selection.Clear();

			foreach ( var component in components )
			{
				component.Mesh = resultMeshes[component];

				if ( !_insetFaces.TryGetValue( component, out var insetFaces ) ) continue;

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
		_insetFaces.Clear();
		_insetEdgeLines.Clear();
	}
}
