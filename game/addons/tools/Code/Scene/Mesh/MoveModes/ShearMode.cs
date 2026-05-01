namespace Editor.MeshEditor;

/// <summary>
/// Shear the selection by sliding vertices along one axis relative to another.
/// </summary>
[Title( "Shear" )]
[Icon( "content_cut" )]
[Alias( "mesh.shear.mode" )]
[Order( 5 )]
public sealed class ShearMode : MoveMode
{
	private BBox _startBox;
	private Dictionary<string, float> _axisDelta = new();

	public override void OnBegin( SelectionTool tool )
	{
		_startBox = tool.CalculateSelectionBounds();
		_axisDelta.Clear();
	}

	protected override void OnUpdate( SelectionTool tool )
	{
		using ( Gizmo.Scope( "shear" ) )
		{
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Hitbox.DepthBias = 0.01f;
			Gizmo.Hitbox.CanInteract = CanUseGizmo;
			Gizmo.Transform = new Transform( tool.Pivot, Rotation.Identity );

			using var scaler = Gizmo.GizmoControls.PushFixedScale();

			DrawShearAxis( tool, "shear_x", Vector3.Forward, Vector3.Left, Vector3.Up, Gizmo.Colors.Forward );
			DrawShearAxis( tool, "shear_y", Vector3.Left, Vector3.Forward, Vector3.Up, Gizmo.Colors.Left );
			DrawShearAxis( tool, "shear_z", Vector3.Up, Vector3.Left, Vector3.Forward, Gizmo.Colors.Up );
		}
	}

	private void DrawShearAxis( SelectionTool tool, string id, Vector3 shearAxis, Vector3 constraint1, Vector3 constraint2, Color color )
	{
		using ( Gizmo.Scope( id ) )
		{
			var arrowLength = 24.0f;
			var crossLength = 16.0f;
			var crossThickness = 1.5f;

			Gizmo.Draw.Color = color;
			Gizmo.Draw.LineThickness = 3.0f;
			Gizmo.Draw.Line( Vector3.Zero, shearAxis * arrowLength );

			var endPosition = shearAxis * arrowLength;

			DrawConstraintCross( tool, $"{id}_primary", endPosition, shearAxis, constraint1, color, crossLength, crossThickness );
			DrawConstraintCross( tool, $"{id}_secondary", endPosition, shearAxis, constraint2, color.Darken( 0.3f ), crossLength, crossThickness );
		}
	}

	private void DrawConstraintCross( SelectionTool tool, string id, Vector3 position, Vector3 shearAxis, Vector3 constraintAxis, Color color, float length, float thickness )
	{
		using ( Gizmo.Scope( id ) )
		{
			var halfLength = length * 0.5f;
			var halfThickness = thickness * 0.5f;

			var mins = -constraintAxis * halfLength - shearAxis * halfThickness - Vector3.Cross( shearAxis, constraintAxis ).Normal * halfThickness;
			var maxs = constraintAxis * halfLength + shearAxis * halfThickness + Vector3.Cross( shearAxis, constraintAxis ).Normal * halfThickness;
			var bbox = new BBox( position + mins, position + maxs );

			Gizmo.Hitbox.BBox( bbox );

			Gizmo.Draw.Color = Gizmo.IsHovered ? color : color.Darken( 0.3f );
			Gizmo.Draw.SolidBox( bbox );

			if ( (Gizmo.IsHovered || Gizmo.Pressed.This) && _axisDelta.ContainsKey( id ) && _axisDelta[id] != 0f )
			{
				var shearAmount = _axisDelta[id];
				var snappedShear = SnapShear( shearAmount );

				using ( Gizmo.Scope( "shear_text" ) )
				{
					DrawText( tool.Pivot, $"{snappedShear:0.##}", color );
				}
			}

			if ( Gizmo.Pressed.This )
			{
				if ( !_axisDelta.ContainsKey( id ) )
				{
					_axisDelta[id] = 0f;
				}

				if ( !tool.DragStarted )
				{
					tool.StartDrag();
				}

				var delta = Gizmo.GetMouseDelta( position, constraintAxis );
				var distance = Vector3.Dot( delta, shearAxis );

				if ( distance != 0.0f )
				{
					_axisDelta[id] += distance / 0.1f;

					var snappedShear = SnapShear( _axisDelta[id] );

					ApplyShear( tool, constraintAxis, shearAxis, snappedShear );
					tool.UpdateDrag();
					tool.Pivot = tool.CalculateSelectionOrigin();
				}
			}
		}
	}

	void DrawText( Vector3 origin, string text, Color color )
	{
		var textSize = 14 * Gizmo.Settings.GizmoScale * Application.DpiScale;
		var cameraDistance = Gizmo.Camera.Position.Distance( origin );
		var scaledTextSize = textSize * (cameraDistance / 50.0f).Clamp( 0.5f, 1.0f );

		var textPosition = origin + Vector3.Up * 2;

		Gizmo.Draw.Color = color;

		var textScope = new TextRendering.Scope
		{
			Text = text,
			TextColor = Color.White,
			FontSize = scaledTextSize,
			FontName = "Roboto Mono",
			FontWeight = 400,
			LineHeight = 1,
			Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
		};

		Gizmo.Draw.ScreenText( textScope, textPosition, 0 );
	}

	private float SnapShear( float delta )
	{
		if ( Gizmo.Settings.SnapToGrid == Gizmo.IsCtrlPressed )
			return delta;

		var spacing = Gizmo.Settings.GridSpacing;
		return MathF.Round( delta / spacing ) * spacing;
	}

	private void ApplyShear( SelectionTool tool, Vector3 shearAxis, Vector3 constraintAxis, float shearAmount )
	{
		var center = _startBox.Center;
		var size = _startBox.Size;

		var constraintSize = MathF.Abs( Vector3.Dot( size, constraintAxis ) );

		if ( constraintSize < 0.01f )
		{
			var perpAxis = Vector3.Cross( shearAxis, Vector3.Up );
			if ( perpAxis.LengthSquared < 0.01f )
				perpAxis = Vector3.Cross( shearAxis, Vector3.Left );

			constraintAxis = perpAxis.Normal;
			constraintSize = MathF.Abs( Vector3.Dot( size, constraintAxis ) );
		}

		var normalizedShear = shearAmount / MathF.Max( constraintSize, 1.0f );

		tool.Shear( center, Rotation.Identity, shearAxis, constraintAxis, normalizedShear );
	}
}
