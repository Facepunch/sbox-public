
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

	public void Update( SelectionTool tool )
	{
		OnUpdate( tool );
	}

	protected virtual void OnUpdate( SelectionTool tool )
	{
	}
}
