
namespace Editor.MeshEditor;

/// <summary>
/// Set the location of the gizmo for the current selection.
/// </summary>
[Title( "Pivot Tool" )]
[Icon( "adjust" )]
[Alias( "mesh.pivot.mode" )]
[Order( 3 )]
public sealed class PivotMode : MoveMode
{
	private Vector3 _pivot;
	private Rotation _basis;

	protected override void OnUpdate( SelectionTool tool )
	{
		var origin = tool.Pivot;

		if ( !Gizmo.Pressed.Any && Gizmo.HasMouseFocus )
		{
			_pivot = origin;
			_basis = tool.CalculateSelectionBasis();
		}

		using ( Gizmo.Scope( "Tool", new Transform( origin ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;

			if ( Gizmo.Control.Position( "position", Vector3.Zero, out var delta, _basis ) )
			{
				_pivot += delta;
				tool.Pivot = Gizmo.Snap( _pivot * _basis.Inverse, delta * _basis.Inverse ) * _basis;
			}
		}
	}
}
