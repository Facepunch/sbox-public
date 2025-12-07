
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

	protected override void OnUpdate( SelectionTool tool )
	{
		if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
		{
			EndDrag();

			_moveDelta = default;
			_basis = tool.CalculateSelectionBasis();
			_origin = tool.Pivot;
		}

		using ( Gizmo.Scope( "Tool", new Transform( _origin, _basis ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Rotate( "rotation", out var angleDelta ) )
			{
				_moveDelta += angleDelta;

				StartDrag( tool );

				var snapDelta = Gizmo.Snap( _moveDelta, _moveDelta );

				foreach ( var entry in TransformVertices )
				{
					var rotation = _basis * snapDelta * _basis.Inverse;
					var position = entry.Value - _origin;
					position *= rotation;
					position += _origin;

					var transform = entry.Key.Transform;
					entry.Key.Component.Mesh.SetVertexPosition( entry.Key.Handle, transform.PointToLocal( position ) );
				}

				UpdateDrag();
			}
		}
	}
}
