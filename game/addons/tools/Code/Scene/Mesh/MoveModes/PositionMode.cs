
namespace Editor.MeshEditor;

/// <summary>
/// Move selected Mesh Elements.<br/> <br/> 
/// <b>Ctrl</b> - toggle snap to grid<br/>
/// <b>Shift</b> - extrude selection
/// </summary>
[Title( "Move/Position" )]
[Icon( "control_camera" )]
[Alias( "mesh.position.mode" )]
[Order( 0 )]
public sealed class PositionMode : MoveMode
{
	private Vector3 _moveDelta;
	private Vector3 _origin;
	private Rotation _basis;

	public override void OnBegin( SelectionTool tool )
	{
		_basis = tool.CalculateSelectionBasis();
		_origin = tool.Pivot;
		_moveDelta = default;
	}

	protected override void OnUpdate( SelectionTool tool )
	{
		var origin = tool.Pivot;

		using ( Gizmo.Scope( "Tool", new Transform( origin ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.CanInteract = CanUseGizmo;

			if ( Gizmo.Control.Position( "position", Vector3.Zero, out var delta, _basis ) )
			{
				_moveDelta += delta;

				var moveDelta = (_moveDelta + _origin) * _basis.Inverse;
				moveDelta = Gizmo.Snap( moveDelta, _moveDelta * _basis.Inverse );
				moveDelta *= _basis;

				tool.Pivot = moveDelta;

				moveDelta -= _origin;

				tool.StartDrag();
				tool.Translate( moveDelta );
				tool.UpdateDrag();
			}
		}
	}
}
