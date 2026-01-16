
namespace Editor.MeshEditor;

/// <summary>
/// Rotate selected Mesh Elements.<br/> <br/> 
/// <b>Ctrl</b> - toggle snap to grid
/// <b>Shift</b> - extrude selection
/// </summary>
[Title( "Rotate" )]
[Icon( "360" )]
[Alias( "mesh.rotate.mode" )]
[Order( 1 )]
public sealed class RotateMode : MoveMode
{
	private Angles _moveDelta;
	private Vector3 _origin;
	private Rotation _basis;

	public override void OnBegin( SelectionTool tool )
	{
		_moveDelta = default;
		_basis = tool.CalculateSelectionBasis();
		_origin = tool.Pivot;
	}

	protected override void OnUpdate( SelectionTool tool )
	{
		using ( Gizmo.Scope( "Tool", new Transform( _origin, _basis ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.CanInteract = CanUseGizmo;

			if ( Gizmo.Control.Rotate( "rotation", out var angleDelta ) )
			{
				_moveDelta += angleDelta;

				var snapDelta = Gizmo.Snap( _moveDelta, _moveDelta );

				tool.StartDrag();
				tool.Rotate( _origin, _basis, snapDelta );
				tool.UpdateDrag();
			}
		}
	}
}
