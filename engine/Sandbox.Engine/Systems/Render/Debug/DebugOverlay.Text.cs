namespace Sandbox;

internal static partial class DebugOverlay
{
	internal static Rect DrawText( Painter painter, in TextRendering.Scope scope, Vector2 point, TextFlag flags = TextFlag.LeftTop )
		=> DrawText( painter, scope, new Rect( point, 1 ), flags );

	internal static Rect DrawText( Painter painter, string text, float size, Color color, Vector2 point, TextFlag flags = TextFlag.LeftTop )
		=> DrawText( painter, new TextRendering.Scope( text, color, size ), point, flags );

	internal static Rect DrawText( Painter painter, in TextRendering.Scope scope, Rect rect, TextFlag flags = TextFlag.LeftTop )
	{
		painter.TextStyle = new TextStyle( scope.FontName, scope.FontSize, scope.TextColor )
		{
			FontWeight = scope.FontWeight,
			Italic = scope.FontItalic,
			LineHeight = scope.LineHeight,
			LetterSpacing = scope.LetterSpacing,
			WordSpacing = scope.WordSpacing,
			Outline = scope.Outline,
			Shadow = scope.Shadow,
			Alignment = flags
		};

		// Overlay columns align text without wrapping it to the column width.
		if ( string.IsNullOrEmpty( scope.Text ) ) return rect;
		rect = rect.Align( painter.MeasureText( scope.Text ), flags ).SnapToGrid();
		painter.Text( scope.Text, rect );
		return rect;
	}
}
