namespace Editor.MeshEditor;

public abstract class PrimitiveEditor : IDisposable
{
	private readonly TypeDescription _type;

	protected PrimitiveTool Tool { get; private init; }

	public string Title => _type.Title;
	public string Icon => _type.Icon;

	public virtual bool CanBuild => false;
	public virtual bool InProgress => false;

	protected PrimitiveEditor( PrimitiveTool tool )
	{
		Tool = tool;
		_type = EditorTypeLibrary.GetType( GetType() );
	}

	public abstract void OnUpdate( SceneTrace trace );
	public abstract void OnCancel();
	public abstract PolygonMesh Build();

	protected Transform ConstructionFrame { get; set; } = Transform.Zero;
	public virtual Transform BuildTransform => ConstructionFrame;
	protected Ray LocalRay => Gizmo.CurrentRay.ToLocal( ConstructionFrame );

	protected void UpdateConstructionFrame( SceneTrace trace, bool alignSurface = false )
	{
		if ( InProgress ) return;

		ConstructionFrame = WorkPlane.Current;
		if ( alignSurface && Gizmo.IsShiftPressed && !Gizmo.Camera.Ortho &&
			WorkPlane.TryPickSurface( trace.Run(), out var surface ) )
		{
			ConstructionFrame = surface;
		}
		else if ( Gizmo.Camera.Ortho )
		{
			ConstructionFrame = new Transform( ConstructionFrame.Position,
				WorkPlane.AlignNormal( -Gizmo.Camera.Rotation.Forward, ConstructionFrame.Rotation ) );
		}
	}

	protected bool TryGetPlacement( SceneTrace trace, out Vector3 point, out Vector3 normal )
	{
		if ( !WorkPlane.TryGetPlacement( trace, ConstructionFrame, ConstructionFrame != Transform.Zero, out point, out normal ) )
			return false;

		point = ConstructionFrame.PointToLocal( point );
		normal = ConstructionFrame.NormalToLocal( normal );
		return true;
	}

	protected float GetPointSize( Vector3 point, float size )
	{
		return SceneEditorExtensions.GetGizmoSize( ConstructionFrame.PointToWorld( point ), size );
	}

	public virtual void OnCreated( MeshComponent component )
	{
	}

	public virtual Widget CreateWidget() => null;

	public virtual void Dispose()
	{
	}

	readonly ToolUndoStack _undo = new();

	protected void PushUndo( string title, Action undo, Action redo = null )
	{
		var frame = ConstructionFrame;
		_undo.Push( title,
			() =>
			{
				ConstructionFrame = frame;
				undo();
			},
			redo is null ? null : () =>
			{
				ConstructionFrame = frame;
				redo();
			} );
	}

	protected void PopUndo()
	{
		_undo.Clear();
	}
}
