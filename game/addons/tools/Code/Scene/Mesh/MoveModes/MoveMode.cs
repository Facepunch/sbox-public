
namespace Editor.MeshEditor;

/// <summary>
/// Base class for moving mesh elements (move, rotate, scale)
/// </summary>
public abstract class MoveMode
{
	/// <summary>
	/// If false, the standard Gizmo.Select() (scene object selection) will be skipped
	/// while this mode is active.
	/// </summary>
	public virtual bool AllowSceneSelection => true;

	bool _dirty = true;
	bool _globalSpace;

	protected bool CanUseGizmo = true;
	private Vector2 _lastCursorPos;

	public void Update( SelectionTool tool )
	{
		if ( tool.DragStarted )
		{
			if ( Gizmo.Pressed.Any == false )
			{
				tool.EndDrag();

				_dirty = true;
			}
		}
		else if ( _globalSpace != tool.GlobalSpace )
		{
			_dirty = true;
		}

		_globalSpace = tool.GlobalSpace;

		if ( _dirty )
		{
			OnBegin( tool );

			_dirty = false;
		}

		UpdateGizmoFromCursor();
		OnUpdate( tool );
	}

	protected void UpdateGizmoFromCursor()
	{
		if ( Gizmo.IsLeftMouseDown )
		{
			CanUseGizmo = false;
			_lastCursorPos = Gizmo.CursorPosition;
		}
		else if ( !CanUseGizmo && Gizmo.CursorPosition.DistanceSquared( _lastCursorPos ) > 4f )
		{
			CanUseGizmo = true;
		}
	}

	protected virtual void OnUpdate( SelectionTool tool )
	{
	}

	public virtual void OnBegin( SelectionTool tool )
	{
	}
}
