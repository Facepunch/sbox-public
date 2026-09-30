using System;

namespace Sandbox.UI;

internal sealed partial class ScriptTextEntry
{
	void InsertPair( char opening )
	{
		var closing = opening switch
		{
			'(' => ')',
			'[' => ']',
			'{' => '}',
			_ => opening
		};
		var start = Selected ? SelectionStart : Editor.CaretOffset;
		var selected = Selected ? Editor.Source[SelectionStart..SelectionEnd] : "";
		var insertion = $"{opening}{selected}{closing}";
		Editor.Replace( start, selected.Length, insertion, start + 1 + selected.Length );
	}

	bool DeleteEmptyPair()
	{
		var position = Editor.CaretOffset;
		var source = Editor.Source;
		if ( position <= 0 || position >= source.Length )
		{
			return false;
		}

		var pair = (source[position - 1], source[position]);
		var matched = pair is ('(', ')' ) or ('[', ']' ) or ('{', '}' ) or ('"', '"' ) or ('\'', '\'' );
		if ( !matched )
		{
			return false;
		}

		Editor.Replace( position - 1, 2, "" );
		return true;
	}

	void NewLine()
	{
		var position = Selected ? SelectionStart : Editor.CaretOffset;
		var lineStart = Editor.Document.Lines[Editor.Document.LineAt( position )];
		var beforeCaret = Editor.Source[lineStart..position];
		var indent = beforeCaret[..IndentLength( beforeCaret )];
		var afterOpeningBrace = beforeCaret.TrimEnd().EndsWith( '{' );
		var innerIndent = indent;
		if ( afterOpeningBrace )
		{
			innerIndent += new string( ' ', Editor.TabSize );
		}

		var insertion = "\n" + innerIndent;
		var caret = position + insertion.Length;
		if ( afterOpeningBrace && position < Editor.Source.Length && Editor.Source[position] == '}' )
		{
			insertion += "\n" + indent;
		}

		var replacedLength = Selected ? SelectionEnd - position : 0;
		Editor.Replace( position, replacedLength, insertion, caret );
	}

	void Indent( bool reverse )
	{
		var tabSize = Editor.TabSize;
		if ( !Selected && !reverse )
		{
			var spaces = tabSize - (Editor.Column - 1) % tabSize;
			OnPaste( new string( ' ', spaces ) );
			return;
		}

		EditLines( line =>
		{
			if ( !reverse )
			{
				return new string( ' ', tabSize ) + line;
			}

			if ( line.StartsWith( '\t' ) )
			{
				return line[1..];
			}

			var spaces = line.TakeWhile( c => c == ' ' ).Count();
			return line[Math.Min( tabSize, spaces )..];
		} );
	}

	void ToggleComment()
	{
		var (start, end) = SelectedLines();
		var lines = Editor.Source[start..end].Split( '\n' );
		var nonEmptyLines = lines.Where( line => !string.IsNullOrWhiteSpace( line ) );
		var uncomment = nonEmptyLines.All( line => line.TrimStart().StartsWith( "//" ) );

		EditLines( start, end, lines, line =>
		{
			var indentLength = IndentLength( line );
			if ( !uncomment )
			{
				return line.Insert( indentLength, "// " );
			}

			var content = line[indentLength..];
			if ( !content.StartsWith( "//" ) )
			{
				return line;
			}

			var commentLength = content.StartsWith( "// " ) ? 3 : 2;
			return line.Remove( indentLength, commentLength );
		} );
	}

	(int Start, int End) SelectedLines()
	{
		var startOffset = Editor.CaretOffset;
		var endOffset = startOffset;
		if ( Selected )
		{
			startOffset = SelectionStart;
			// A selection ending at the next line's start doesn't include that line.
			endOffset = Math.Max( SelectionStart, SelectionEnd - 1 );
		}

		var startLine = Editor.Document.LineAt( startOffset );
		var endLine = Editor.Document.LineAt( endOffset );
		return (Editor.Document.Lines[startLine], Editor.Document.LineEnd( endLine ));
	}

	void EditLines( Func<string, string> edit )
	{
		var (start, end) = SelectedLines();
		var lines = Editor.Source[start..end].Split( '\n' );
		EditLines( start, end, lines, edit );
	}

	void EditLines( int start, int end, string[] lines, Func<string, string> edit )
	{
		var updated = string.Join( "\n", lines.Select( edit ) );
		Editor.Replace( start, end - start, updated );
		Editor.Select( start, updated.Length );
	}

	static int IndentLength( ReadOnlySpan<char> line )
	{
		var length = 0;
		while ( length < line.Length && line[length] is ' ' or '\t' )
		{
			length++;
		}

		return length;
	}
}
