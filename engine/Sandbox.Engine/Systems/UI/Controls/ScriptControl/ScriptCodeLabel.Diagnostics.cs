using System;

namespace Sandbox.UI;

internal sealed partial class ScriptCodeLabel
{
	void DrawDiagnostics( Painter painter, Vector2 origin )
	{
		var editor = _entry.Editor;
		foreach ( var diagnostic in editor.VisibleDiagnostics )
		{
			var endOffset = Math.Min( Text.Length, ScriptControl.DiagnosticEnd( diagnostic.Span ) );
			for ( var line = editor.Document.LineAt( diagnostic.Span.Start ); line <= editor.Document.LineAt( endOffset ); line++ )
			{
				var start = GetCaretRect( editor.Document.ToElement( Math.Max( diagnostic.Span.Start, editor.Document.Lines[line] ) ) );
				var end = GetCaretRect( editor.Document.ToElement( Math.Min( endOffset, editor.Document.LineEnd( line ) ) ) );
				if ( !IsVisibleLine( start ) )
				{
					continue;
				}
				var left = Math.Max( start.Left, _entry.Box.Rect.Left ) - origin.x;
				var right = Math.Min( Math.Max( start.Left + 6, end.Left ), _entry.Box.Rect.Right ) - origin.x;
				var y = start.Bottom - origin.y - 2;
				var color = diagnostic.Severity == Script.DiagnosticSeverity.Error ? editor.Theme.Error : editor.Theme.Warning;
				DrawSquiggle( painter, left, right, y, color );
			}
		}
	}

	static void DrawSquiggle( Painter painter, float left, float right, float y, Color color )
	{
		painter.Stroke = Stroke.Solid( color, 1 );
		for ( var x = left; x < right; x += 4 )
		{
			var peak = new Vector2( Math.Min( x + 2, right ), y - 2 );
			painter.Line( new Vector2( x, y ), peak );
			painter.Line( peak, new Vector2( Math.Min( x + 4, right ), y ) );
		}
	}
}
