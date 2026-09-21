using System;

namespace Sandbox.UI;

internal sealed partial class ScriptTextEntry
{
	public override void OnPaste( string text )
	{
		if ( !CanEdit )
		{
			return;
		}

		Editor.DismissIntelliSense();
		var normalized = ScriptControl.NormalizeNewlines( text );
		base.OnPaste( normalized );
	}

	public override void OnKeyTyped( char character, KeyboardModifiers modifiers )
	{
		if ( character == ' ' && modifiers.Contains( KeyboardModifiers.Ctrl ) && !modifiers.Contains( KeyboardModifiers.Alt ) )
			return;

		base.OnKeyTyped( character, modifiers );
	}

	public override void OnKeyTyped( char character )
	{
		Editor.CloseHover();
		if ( !CanEdit )
		{
			return;
		}

		if ( character is '\n' or '\r' )
		{
			NewLine();
			return;
		}

		if ( character == '\t' )
		{
			Indent( false );
			return;
		}

		if ( Editor.CompletionVisible && character is '.' or '(' or ';' )
		{
			Editor.AcceptCompletion();
		}

		var source = Editor.Source;
		var offset = Editor.CaretOffset;
		var token = Editor.Analysis.GetToken( Math.Max( 0, offset - 1 ) );
		var inLiteral = Editor.InLiteral( Math.Max( 0, offset - 1 ) );

		if ( !inLiteral && "([{\"'".Contains( character ) )
		{
			InsertPair( character );
			Editor.UpdateSignature( explicitRequest: true );
			return;
		}

		var escaped = IsEscaped( source, offset );
		var closingQuote = token?.Kind == Script.TokenKind.String && character is '"' or '\'' && !escaped;
		var canSkipClosing = !inLiteral || closingQuote;
		if ( canSkipClosing && !Selected && offset < source.Length && source[offset] == character && ")]}\"'".Contains( character ) )
		{
			Label.SetCaretPosition( CaretPosition + 1 );
			Editor.CloseCompletion();
			Editor.UpdateSignature( explicitRequest: true );
			return;
		}

		base.OnKeyTyped( character );
		if ( char.IsLetterOrDigit( character ) || character is '_' or '.' )
		{
			Editor.ShowCompletions( false );
		}
		else
		{
			Editor.CloseCompletion();
		}
		Editor.UpdateSignature( explicitRequest: true );
	}

	static bool IsEscaped( string source, int offset )
	{
		var escaped = false;
		for ( var index = offset - 1; index >= 0 && source[index] == '\\'; index-- )
		{
			escaped = !escaped;
		}
		return escaped;
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( HandleKey( e.Button, e.HasCtrl, e.HasShift, e.HasAlt ) )
		{
			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
		if ( e.HasCtrl && e.Button is "z" or "y" )
		{
			Editor.DismissIntelliSense();
		}
		else if ( e.Button is "backspace" or "delete" && Editor.CompletionVisible )
		{
			Editor.ShowCompletions( false );
		}
		else if ( e.Button is "left" or "right" or "home" or "end" )
		{
			Editor.CloseCompletion();
		}
	}

	internal bool HandleKey( string key, bool ctrl = false, bool shift = false, bool alt = false )
	{
		Editor.CloseHover();

		if ( key == "escape" )
		{
			Editor.DismissIntelliSense();
			return true;
		}

		if ( key == "f8" )
		{
			Editor.GoToDiagnostic( shift );
			return true;
		}

		if ( ctrl && !alt && shift && key == "space" )
		{
			Editor.UpdateSignature( explicitRequest: true );
			return true;
		}

		if ( ctrl && !alt && key == "space" )
		{
			Editor.ShowCompletions();
			return true;
		}

		if ( Editor.CompletionVisible && HandleCompletionKey( key ) )
		{
			return true;
		}

		if ( alt && Editor.SignatureVisible && key is "up" or "down" )
		{
			Editor.MoveOverload( key == "up" ? -1 : 1 );
			return true;
		}

		if ( key is "left" or "right" or "up" or "down" or "home" or "end" or "pageup" or "pagedown" )
		{
			Editor.CloseSignature();
			Editor.CloseCompletion();
		}

		if ( ctrl && key == "slash" )
		{
			if ( CanEdit )
				ToggleComment();
			return true;
		}

		if ( key == "tab" )
		{
			if ( CanEdit )
				Indent( shift );
			return true;
		}

		if ( key is "enter" or "pad_enter" )
		{
			if ( CanEdit )
				NewLine();
			return true;
		}

		if ( key is "pageup" or "pagedown" )
		{
			MovePage( key == "pageup" ? -1 : 1, shift );
			return true;
		}

		if ( key == "home" && !ctrl )
		{
			MoveToIndent( shift );
			return true;
		}

		if ( key == "backspace" && !ctrl && !Selected && CanEdit )
		{
			return DeleteEmptyPair();
		}

		return false;
	}

	bool HandleCompletionKey( string key )
	{
		switch ( key )
		{
			case "up":
			case "down":
				Editor.MoveCompletion( key == "up" ? -1 : 1 );
				return true;
			case "pageup":
			case "pagedown":
				Editor.MoveCompletion( key == "pageup" ? -ScriptControl.CompletionPageSize : ScriptControl.CompletionPageSize );
				return true;
			case "tab":
			case "enter":
			case "pad_enter":
				Editor.AcceptCompletion();
				return true;
			default:
				return false;
		}
	}

	void MovePage( int direction, bool select )
	{
		var lineHeight = Math.Max( 1, ElementRect( CaretPosition ).Height );
		var linesPerPage = Math.Max( 1, (int)(Box.Rect.Height / lineHeight) - 1 );
		for ( var line = 0; line < linesPerPage; line++ )
		{
			Label.MoveCaretLine( direction, select );
		}
	}

	void MoveToIndent( bool select )
	{
		Editor.CloseCompletion();
		var lineStart = Editor.Document.Lines[Editor.CaretLine];
		var firstCharacter = lineStart + IndentLength( Editor.Source.AsSpan( lineStart ) );

		var destination = Editor.CaretOffset == firstCharacter ? lineStart : firstCharacter;
		Label.SetCaretPosition( Editor.Document.ToElement( destination ), select );
	}
}
