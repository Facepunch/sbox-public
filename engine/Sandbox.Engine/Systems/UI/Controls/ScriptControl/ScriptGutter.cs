using System;

namespace Sandbox.UI;

internal sealed class ScriptGutter : Panel
{
	readonly ScriptControl _editor;

	public ScriptGutter( ScriptControl editor )
	{
		_editor = editor;
		AddClass( "line-gutter" );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.Button != "mouseleft" )
		{
			return;
		}
		var y = PanelPositionToScreenPosition( MousePosition ).y;
		for ( var line = 0; line < _editor.Document.Lines.Length; line++ )
		{
			var start = _editor.Document.Lines[line];
			var rect = _editor.SourceCaretRect( start );
			if ( y < rect.Top || y >= rect.Bottom )
			{
				continue;
			}
			_editor.Select( start, _editor.Document.LineEnd( line, includeNewline: true ) - start );
			_editor.FocusEditor();
			e.StopPropagation();
			return;
		}
	}

	public override void OnMouseWheel( Vector2 value ) => _editor.Entry.OnMouseWheel( value );

	public override void OnDraw( Painter painter )
	{
		using var scope = painter.Scope();
		painter.Clip( new Rect( Vector2.Zero, Box.Rect.Size ) );
		var style = TextStyle( this, TextFlag.RightTop, _editor.Theme.LineNumber );
		for ( var line = 0; line < _editor.Document.Lines.Length; line++ )
		{
			var caret = _editor.SourceCaretRect( _editor.Document.Lines[line] );
			if ( caret.Bottom < Box.Rect.Top || caret.Top > Box.Rect.Bottom )
			{
				continue;
			}
			painter.TextStyle = style with
			{
				Color = line == _editor.CaretLine ? _editor.Theme.ActiveLineNumber : _editor.Theme.LineNumber
			};
			painter.Text( (line + 1).ToString(), new Rect( 0, caret.Top - Box.Rect.Top, Box.Rect.Width - 14 * ScaleToScreen, caret.Height ) );
		}
	}

	static TextStyle TextStyle( Panel panel, TextFlag alignment, Color color )
	{
		var style = panel.ComputedStyle;
		// Match the label's font-size rounding before converting to painter units.
		var size = MathF.Round( (style.FontSize?.GetPixels( 100 ) ?? 13f) * 32 ) / 32 / panel.ScaleToScreen;
		return new TextStyle( style.FontFamily ?? "Cascadia Mono", size, color )
		{
			FontWeight = style.FontWeight ?? 400,
			Alignment = alignment | TextFlag.DontClip | TextFlag.SingleLine
		};
	}
}
