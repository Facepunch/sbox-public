
namespace Editor.MeshEditor;

/// <summary>
/// Base class for moving mesh elements (move, rotate, scale)
/// </summary>
public abstract class MoveMode
{
	public void Update( SelectionTool tool )
	{
		OnUpdate( tool );
	}

	protected virtual void OnUpdate( SelectionTool tool )
	{
	}
}
