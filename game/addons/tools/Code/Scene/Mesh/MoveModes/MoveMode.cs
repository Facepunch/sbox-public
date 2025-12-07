
namespace Editor.MeshEditor;

/// <summary>
/// Base class for moving mesh elements (move, rotate, scale)
/// </summary>
public abstract class MoveMode
{
	protected IReadOnlyDictionary<MeshVertex, Vector3> TransformVertices => _transformVertices;

	private readonly Dictionary<MeshVertex, Vector3> _transformVertices = [];
	private List<MeshFace> _transformFaces;
	private IDisposable _undoScope;

	public void Update( SelectionTool tool )
	{
		if ( !tool.Selection.OfType<IMeshElement>().Any() )
			return;

		OnUpdate( tool );
	}

	protected virtual void OnUpdate( SelectionTool tool )
	{
	}

	protected void StartDrag( SelectionTool tool )
	{
		if ( _transformVertices.Count != 0 )
			return;

		var components = tool.Selection.OfType<IMeshElement>()
			.Select( x => x.Component )
			.Distinct();

		_undoScope ??= SceneEditorSession.Active.UndoScope( $"{(Gizmo.IsShiftPressed ? "Extrude" : "Move")} Selection" )
			.WithComponentChanges( components )
			.Push();

		if ( Gizmo.IsShiftPressed )
		{
			_transformFaces = tool.ExtrudeSelection();
		}

		foreach ( var vertex in tool.VertexSelection )
		{
			_transformVertices[vertex] = vertex.PositionWorld;
		}
	}

	protected void UpdateDrag()
	{
		if ( _transformFaces is not null )
		{
			foreach ( var group in _transformFaces.GroupBy( x => x.Component ) )
			{
				var mesh = group.Key.Mesh;
				var faces = group.Select( x => x.Handle ).ToArray();

				foreach ( var face in faces )
				{
					mesh.TextureAlignToGrid( mesh.Transform, face );
				}
			}
		}

		var meshes = TransformVertices
			.Select( x => x.Key.Component.Mesh )
			.Distinct();

		foreach ( var mesh in meshes )
		{
			mesh.ComputeFaceTextureCoordinatesFromParameters();
		}
	}

	protected void EndDrag()
	{
		_transformVertices.Clear();
		_transformFaces = null;

		_undoScope?.Dispose();
		_undoScope = null;
	}
}
