
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

	protected override void OnUpdate( SelectionTool tool )
	{
		var origin = tool.Pivot;

		if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
		{
			EndDrag();

			_basis = tool.CalculateSelectionBasis();
			_origin = origin;
			_moveDelta = default;
		}

		using ( Gizmo.Scope( "Tool", new Transform( origin ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Position( "position", Vector3.Zero, out var delta, _basis ) )
			{
				_moveDelta += delta;

				var moveDelta = (_moveDelta + _origin) * _basis.Inverse;
				moveDelta = Gizmo.Snap( moveDelta, _moveDelta * _basis.Inverse );
				moveDelta *= _basis;

				tool.Pivot = moveDelta;

				moveDelta -= _origin;

				StartDrag( tool );

				foreach ( var entry in TransformVertices )
				{
					var position = entry.Value + moveDelta;
					var transform = entry.Key.Transform;
					entry.Key.Component.Mesh.SetVertexPosition( entry.Key.Handle, transform.PointToLocal( position ) );
				}

				UpdateDrag();
			}
		}
	}
}
