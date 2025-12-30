
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

		OnUpdate( tool );
	}

	protected virtual void OnUpdate( SelectionTool tool )
	{
	}

	public virtual void OnBegin( SelectionTool tool )
	{
	}
}
