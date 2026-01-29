
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

		if ( Gizmo.Pressed.Any && (_moveDelta.pitch != 0 || _moveDelta.yaw != 0 || _moveDelta.roll != 0) )
		{
			var snapDelta = Gizmo.Snap( _moveDelta, _moveDelta );
			DrawRotationAngle( _origin, snapDelta );
		}
	}

	private void DrawRotationAngle( Vector3 origin, Angles rotation )
	{
		using ( Gizmo.Scope( "RotationAngle" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			var textSize = 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;
			var cameraDistance = Gizmo.Camera.Position.Distance( origin );
			var scaledTextSize = textSize * (cameraDistance / 50.0f).Clamp( 0.5f, 1.0f );

			var angleText = "";
			if ( rotation.pitch != 0 ) angleText += $"P:{rotation.pitch:0.##}° ";
			if ( rotation.yaw != 0 ) angleText += $"Y:{rotation.yaw:0.##}° ";
			if ( rotation.roll != 0 ) angleText += $"R:{rotation.roll:0.##}° ";

			angleText = angleText.Trim();

			if ( string.IsNullOrEmpty( angleText ) )
				return;

			var textPosition = origin + Vector3.Up * 2;

			Gizmo.Draw.Color = Color.White;

			var textScope = new TextRendering.Scope
			{
				Text = angleText,
				TextColor = Color.White,
				FontSize = scaledTextSize,
				FontName = "Roboto Mono",
				FontWeight = 400,
				LineHeight = 1,
				Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
			};

			Gizmo.Draw.ScreenText( textScope, textPosition, 0 );
		}
	}
}
