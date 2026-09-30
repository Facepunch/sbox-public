using System;

namespace Sandbox.UI;

/// <summary>
/// Supplies syntax styles to Label and draws diagnostic decorations using its caret geometry.
/// </summary>
internal sealed partial class ScriptCodeLabel : Label
{
	readonly ScriptTextEntry _entry;
	readonly Dictionary<Color, Styles> _styles = new();

	public ScriptCodeLabel( ScriptTextEntry entry )
	{
		_entry = entry;
	}

	internal void Rebuild( bool resetStyles = false )
	{
		ClearStyleSpans();
		if ( resetStyles )
		{
			_styles.Clear();
		}

		foreach ( var token in _entry.Editor.Analysis.Tokens )
		{
			SetStyleSpan( token.Span.Start, token.Span.Start + token.Span.Length, StyleFor( TokenColor( token ) ) );
		}
	}

	Color TokenColor( Script.Token token )
	{
		var editor = _entry.Editor;
		if ( token.Kind == Script.TokenKind.Identifier )
		{
			return editor.Analysis.GetSymbol( token.Span.Start )?.Kind switch
			{
				Script.SymbolKind.Type => _entry.Editor.Theme.Type,
				Script.SymbolKind.Member => _entry.Editor.Theme.Member,
				_ => _entry.Editor.Theme.Identifier
			};
		}

		if ( token.Kind == Script.TokenKind.Keyword )
		{
			var text = editor.Source.AsSpan( token.Span.Start, token.Span.Length );
			return text is "return" or "if" or "else" or "for" or "foreach" or "while" or "break" or "continue"
				? _entry.Editor.Theme.ControlFlow
				: _entry.Editor.Theme.Keyword;
		}

		return token.Kind switch
		{
			Script.TokenKind.Number => _entry.Editor.Theme.Number,
			Script.TokenKind.String => _entry.Editor.Theme.String,
			Script.TokenKind.Comment => _entry.Editor.Theme.Comment,
			_ => _entry.Editor.Theme.Foreground
		};
	}

	Styles StyleFor( Color color )
	{
		if ( !_styles.TryGetValue( color, out var style ) )
		{
			style = new Styles { FontColor = color };
			_styles.Add( color, style );
		}

		return style;
	}

	public override void OnDraw( Painter painter )
	{
		// Label ignores this property until its text layout exists, so it cannot be set in the constructor.
		SelectionColor = _entry.Editor.Theme.Selection;
		base.OnDraw( painter );

		using var scope = painter.Scope();
		var origin = Box.Rect.Position;
		painter.Clip( new Rect( _entry.Box.Rect.Position - origin, _entry.Box.Rect.Size ) );
		DrawDiagnostics( painter, origin );
	}

	bool IsVisibleLine( Rect caret ) => caret.Bottom >= _entry.Box.Rect.Top && caret.Top <= _entry.Box.Rect.Bottom;
}
