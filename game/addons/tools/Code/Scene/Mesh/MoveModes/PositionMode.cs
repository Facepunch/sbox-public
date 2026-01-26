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
	private Vector3 _startPosition;

	public override void OnBegin( SelectionTool tool )
	{
		_basis = tool.CalculateSelectionBasis();
		_origin = tool.Pivot;
		_moveDelta = default;
		_startPosition = tool.Pivot;
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
				if ( _moveDelta == Vector3.Zero )
				{
					_startPosition = _origin;
				}

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

		if ( Gizmo.Pressed.Any && _moveDelta != Vector3.Zero )
		{
			DrawMovementLine( _startPosition, tool.Pivot, _basis );
		}
	}

	private void DrawMovementLine( Vector3 start, Vector3 end, Rotation basis )
	{
		var distance = start.Distance( end );

		if ( distance < 0.01f )
			return;

		using ( Gizmo.Scope( "MovementLine" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;

			Gizmo.Draw.Color = Color.Blue.WithAlpha( 0.7f );
			Gizmo.Draw.LineThickness = 2;
			Gizmo.Draw.Line( start, end );

			Gizmo.Draw.Color = Color.White.WithAlpha( 0.7f );
			Gizmo.Draw.Sprite( start, 6, null, false );

			Gizmo.Draw.Color = Color.Cyan;
			Gizmo.Draw.Sprite( end, 8, null, false );

			var midPoint = (start + end) * 0.5f;
			var textSize = 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;

			var cameraDistance = Gizmo.Camera.Position.Distance( midPoint );
			var scaledTextSize = textSize * (cameraDistance / 50.0f).Clamp( 0.5f, 1.0f );

			string distanceText;
			//Local distance
			if ( !Gizmo.Settings.GlobalSpace )
			{
				var localDelta = (end - start) * basis.Inverse;
				var localDistance = localDelta.Length;
				distanceText = $"{localDistance:0.##}";
			}
			else
			{
				distanceText = $"{distance:0.##}";
			}

			var textScope = new TextRendering.Scope
			{
				Text = distanceText,
				TextColor = Color.White,
				FontSize = scaledTextSize,
				FontName = "Roboto Mono",
				FontWeight = 600,
				LineHeight = 1,
				Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
			};

			Gizmo.Draw.ScreenText( textScope, midPoint, new Vector2( 0, -scaledTextSize ) );

			var direction = (end - start).Normal;
			var arrowLength = Math.Min( distance * 0.2f, 5.0f );
		}
	}
}
