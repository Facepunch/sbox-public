
namespace Editor.MeshEditor;

/// <summary>
/// Helper methods for drawing dimension labels that are always camera-aligned
/// </summary>
public static class DimensionDisplay
{
	/// <summary>
	/// Draw dimension labels for a bounding box that are always aligned to face the camera
	/// Positions each label on the edge closest to the camera for that axis
	/// </summary>
	public static void DrawBounds( BBox box )
	{
		var textSize = 22 * Gizmo.Settings.GizmoScale * Application.DpiScale;

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = Gizmo.Colors.Active.WithAlpha( 0.5f );
		Gizmo.Draw.LineBBox( box );

		Gizmo.Draw.LineThickness = 2;

		var cameraPos = Gizmo.Camera.Position;
		var boxCenter = box.Center;

		if ( box.Size.x > 0.01f )
		{
			var useMaxY = (cameraPos.y - boxCenter.y) > 0;
			var useMaxZ = (cameraPos.z - boxCenter.z) > 0;

			var yPos = useMaxY ? box.Maxs.y : box.Mins.y;
			var zPos = useMaxZ ? box.Maxs.z : box.Mins.z;

			var lineStart = new Vector3( box.Mins.x, yPos, zPos );
			var lineEnd = new Vector3( box.Maxs.x, yPos, zPos );
			var midPoint = new Vector3( box.Center.x, yPos, zPos );

			DrawDimensionLabel( midPoint, $"W:{box.Size.x:0.#}", Gizmo.Colors.Forward, textSize );
			Gizmo.Draw.Color = Gizmo.Colors.Forward;
			Gizmo.Draw.Line( lineStart, lineEnd );
		}

		if ( box.Size.y > 0.01f )
		{
			var useMaxX = (cameraPos.x - boxCenter.x) > 0;
			var useMaxZ = (cameraPos.z - boxCenter.z) > 0;

			var xPos = useMaxX ? box.Maxs.x : box.Mins.x;
			var zPos = useMaxZ ? box.Maxs.z : box.Mins.z;

			var lineStart = new Vector3( xPos, box.Mins.y, zPos );
			var lineEnd = new Vector3( xPos, box.Maxs.y, zPos );
			var midPoint = new Vector3( xPos, box.Center.y, zPos );

			DrawDimensionLabel( midPoint, $"L:{box.Size.y:0.#}", Gizmo.Colors.Left, textSize );
			Gizmo.Draw.Color = Gizmo.Colors.Left;
			Gizmo.Draw.Line( lineStart, lineEnd );
		}

		if ( box.Size.z > 0.01f )
		{
			var useMaxX = (cameraPos.x - boxCenter.x) > 0;
			var useMaxY = (cameraPos.y - boxCenter.y) > 0;

			var xPos = useMaxX ? box.Maxs.x : box.Mins.x;
			var yPos = useMaxY ? box.Maxs.y : box.Mins.y;

			var lineStart = new Vector3( xPos, yPos, box.Mins.z );
			var lineEnd = new Vector3( xPos, yPos, box.Maxs.z );
			var midPoint = new Vector3( xPos, yPos, box.Center.z );

			DrawDimensionLabel( midPoint, $"H:{box.Size.z:0.#}", Gizmo.Colors.Up, textSize );
			Gizmo.Draw.Color = Gizmo.Colors.Up;
			Gizmo.Draw.Line( lineStart, lineEnd );
		}
	}

	/// <summary>
	/// Draw a dimension label at a world position that always faces the camera
	/// </summary>
	private static void DrawDimensionLabel( Vector3 worldPosition, string text, Color color, float textSize )
	{
		// ScreenText is already camera-aligned, so we just need to position it correctly
		var textOffset = Vector2.Up * 32; // Offset upward in screen space

		Gizmo.Draw.Color = color;
		var textScope = new TextRendering.Scope
		{
			Text = text,
			TextColor = color,
			FontSize = textSize,
			FontName = "Roboto Mono",
			FontWeight = 400,
			LineHeight = 1,
			Outline = new TextRendering.Outline() { Color = Color.Black, Enabled = true, Size = 3 }
		};

		Gizmo.Draw.ScreenText( textScope, worldPosition, textOffset );
	}
}
