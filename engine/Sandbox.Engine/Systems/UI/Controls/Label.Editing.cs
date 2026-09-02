using System.Globalization;

namespace Sandbox.UI;

public partial class Label
{
	private bool _multiline = true;

	/// <summary>
	/// Enables multi-line support for editing purposes.
	/// </summary>
	// Sol: TODO: unlink this from wrapping, add css text-wrap or something
	public bool Multiline
	{
		get => _multiline;
		set
		{
			if ( _multiline == value ) return;

			_multiline = value;

			// This decides whether the text wraps, which is measured during layout - without
			// this the block keeps wrapping until something else happens to dirty it
			YogaNode?.MarkDirty();
			SetNeedsPreLayout();
		}
	}

	private Vector2 caretScroll;

	/// <summary>
	/// Where the caret is aiming for while it moves up and down - an x in text space, not a
	/// character index, because the same index sits at a different place on every line. Passing
	/// through a short line shouldn't drag the caret left for good, so this outlives the lines
	/// in between. Null when nothing is aiming anywhere - zero is a real position.
	/// </summary>
	private float? _desiredCaretX;

	/// <summary>
	/// Set while moving between lines, so the move doesn't throw away the column it's using.
	/// </summary>
	private bool _movingLine;

	/// <summary>
	/// The visible size the scroll offset was last worked out against - see FinalLayout.
	/// </summary>
	private Vector2 _scrolledSize;

	/// <summary>
	/// Replace the currently selected text with given text. The caret ends up after the
	/// replacement.
	/// </summary>
	public void ReplaceSelection( string str )
	{
		var s = Math.Min( SelectionStart, SelectionEnd );
		var e = Math.Max( SelectionStart, SelectionEnd );

		InsertText( str, s, e );
		CaretPosition = s + new StringInfo( str ).LengthInTextElements;

		SelectionStart = 0;
		SelectionEnd = 0;
	}

	/// <summary>
	/// Sets the text selection.
	/// </summary>
	public void SetSelection( int start, int end )
	{
		var s = Math.Min( start, end ).Clamp( 0, TextLength );
		var e = Math.Max( start, end ).Clamp( 0, TextLength );

		if ( s == e )
		{
			s = 0;
			e = 0;
		}

		SelectionStart = s;
		SelectionEnd = e;
	}

	/// <summary>
	/// Set the text caret position to the given index.
	/// </summary>
	/// <param name="pos">Where to move the text caret to within the text.</param>
	/// <param name="select">Whether to also add the characters we passed by to the selection.</param>
	public void SetCaretPosition( int pos, bool select = false )
	{
		if ( SelectionEnd == 0 && SelectionStart == 0 && select )
		{
			SelectionStart = CaretPosition.Clamp( 0, TextLength );
		}

		CaretPosition = pos.Clamp( 0, TextLength );

		if ( select )
		{
			SelectionEnd = CaretPosition;
		}
		else
		{
			SelectionEnd = 0;
			SelectionStart = 0;
		}

		ScrollToCaret();
	}

	/// <summary>
	/// Put the caret within the visible region.
	/// </summary>
	public void ScrollToCaret()
	{
		if ( _textBlock is null ) return;

		var before = caretScroll;
		_textBlock.ScrollToCaret( CaretPosition, ref caretScroll, Box.RectInner.Size );

		ScrollChanged( before );
	}

	/// <summary>
	/// Keep the scroll offset inside the text - editing can leave it pointing past the end.
	/// </summary>
	internal void ClampScroll()
	{
		if ( _textBlock is null ) return;

		var before = caretScroll;
		_textBlock.ClampScroll( ref caretScroll, Box.RectInner.Size );

		ScrollChanged( before );
	}

	/// <summary>
	/// The text is drawn at an offset, so moving that offset has to redraw it. Nothing else
	/// marks this dirty - moving the caret with the arrow keys changes nothing but the scroll.
	/// </summary>
	private void ScrollChanged( Vector2 before )
	{
		if ( before == caretScroll ) return;

		MarkRenderDirty();
	}

	/// <summary>
	/// Move the text caret to the closest word start or end to the left of current position.<br/>
	/// This simulates holding Control key while pressing left arrow key.
	/// </summary>
	/// <param name="select">Whether to also add the characters we passed by to the selection.</param>
	public void MoveToWordBoundaryLeft( bool select )
	{
		var boundaries = GetWordBoundaryIndices();
		var left = boundaries.LastOrDefault( x => x < CaretPosition );

		MoveCaretPos( left - CaretPosition, select );
	}

	/// <summary>
	/// Move the text caret to the closest word start or end to the right of current position.<br/>
	/// This simulates holding Control key while pressing right arrow key.
	/// </summary>
	/// <param name="select">Whether to also add the characters we passed by to the selection.</param>
	public void MoveToWordBoundaryRight( bool select )
	{
		var boundaries = GetWordBoundaryIndices();
		var right = boundaries.FirstOrDefault( x => x > CaretPosition, TextLength );

		MoveCaretPos( right - CaretPosition, select );
	}

	/// <summary>
	/// Move the text caret by given amount.
	/// </summary>
	/// <param name="delta">How many characters to the right to move. Negative values move left.</param>
	/// <param name="select">Whether to also add the characters we passed by to the selection.</param>
	public void MoveCaretPos( int delta, bool select = false )
	{
		SetCaretPosition( CaretPosition + delta, select );
	}

	/// <summary>
	/// Insert given text at given position.
	/// </summary>
	/// <param name="text">Text to insert.</param>
	/// <param name="pos">Position to insert the text at.</param>
	/// <param name="endpos">If set, the end position in the current <see cref="Text"/>,
	/// which will be used to replace portion of the existing text with the given <paramref name="text"/>.</param>
	public void InsertText( string text, int pos, int? endpos = null )
	{
		CaretSantity();

		pos = Math.Clamp( pos, 0, TextLength );
		if ( endpos.HasValue ) endpos = Math.Clamp( endpos.Value, 0, TextLength );

		var a = pos > 0 ? StringInfo.SubstringByTextElements( 0, pos ) : "";
		var b = "";

		if ( endpos.HasValue )
		{
			if ( endpos < TextLength ) b = StringInfo.SubstringByTextElements( endpos.Value );
		}
		else
		{
			if ( pos < TextLength ) b = StringInfo.SubstringByTextElements( pos );
		}

		Text = $"{a}{text}{b}";
	}

	/// <summary>
	/// Remove given amount of characters from the label at given <paramref name="start"/> position.
	/// </summary>
	public virtual void RemoveText( int start, int count )
	{
		var a = start > 0 ? StringInfo.SubstringByTextElements( 0, start ) : "";
		var b = (start + count < TextLength) ? StringInfo.SubstringByTextElements( start + count ) : "";

		Text = a + b;
	}

	/// <summary>
	/// Move the text caret to the start of the current line.
	/// </summary>
	/// <param name="select">Whether to also add the characters we passed by to the selection.</param>
	public void MoveToLineStart( bool select = false )
	{
		if ( !Multiline )
		{
			SetCaretPosition( 0, select );
			return;
		}

		int iNewline = 0;
		var e = StringInfo.GetTextElementEnumerator( Text );
		while ( e.MoveNext() )
		{
			if ( e.ElementIndex >= CaretPosition )
				break;

			if ( IsNewline( e.GetTextElement() ) )
				iNewline = e.ElementIndex + 1;
		}

		SetCaretPosition( iNewline, select );
	}

	/// <summary>
	/// Move the text caret to the end of the current line.
	/// </summary>
	/// <param name="select">Whether to also add the characters we passed by to the selection.</param>
	public void MoveToLineEnd( bool select = false )
	{
		if ( !Multiline )
		{
			SetCaretPosition( TextLength, select );
			return;
		}

		var e = StringInfo.GetTextElementEnumerator( Text );
		while ( e.MoveNext() )
		{
			if ( e.ElementIndex < CaretPosition )
				continue;

			if ( IsNewline( e.GetTextElement() ) )
			{
				SetCaretPosition( e.ElementIndex, select );
				return;
			}
		}

		SetCaretPosition( TextLength, select );
	}

	/// <summary>
	/// Move the text caret to next or previous line.
	/// </summary>
	/// <param name="offset_line">How many lines to offset. Negative values move up.</param>
	/// <param name="select">Whether to also add the characters we passed by to the selection.</param>
	public void MoveCaretLine( int offset_line, bool select )
	{
		if ( !Multiline )
		{
			if ( offset_line < 0 ) SetCaretPosition( 0, select );
			if ( offset_line > 0 ) SetCaretPosition( TextLength, select );
			return;
		}

		if ( _textBlock is null ) return;

		var caret = GetCaretRect( CaretPosition );

		// Work in text space - the caret rect comes back in screen space, which moves with the
		// panel and the scroll
		var local = new Vector2(
			caret.Left - _textRect.Left + caretScroll.x,
			caret.Top - _textRect.Top + caretScroll.y );

		// The first move of a run fixes the x every move after it aims for
		_desiredCaretX ??= local.x;

		var y = local.y + caret.Height * 0.5f + caret.Height * offset_line * 1.2f;

		var pos = GetLetterAt( new Vector2( _desiredCaretX.Value, y ) );
		if ( pos < 0 ) return;

		_movingLine = true;

		try
		{
			SetCaretPosition( pos, select );
		}
		finally
		{
			_movingLine = false;
		}
	}

	/// <summary>
	/// Select a work at given word position.
	/// </summary>
	public void SelectWord( int wordPos )
	{
		if ( TextLength == 0 )
			return;

		var boundaries = GetWordBoundaryIndices();
		SelectionStart = boundaries.LastOrDefault( x => x < wordPos );
		SelectionEnd = boundaries.FirstOrDefault( x => x >= wordPos );

		CaretPosition = SelectionEnd;
	}

	/// <summary>
	/// Returns a list of positions in the text of each side of each word within the <see cref="Text"/>.<br/>
	/// This is used for Control + Arrow Key navigation.
	/// </summary>
	public List<int> GetWordBoundaryIndices()
	{
		var result = new List<int>() { 0 };

		var e = StringInfo.GetTextElementEnumerator( Text );
		var index = 0;
		var lastKind = -1;

		// A boundary sits wherever the kind of character changes - between words, runs of
		// symbols and runs of whitespace. An emoji counts by its whole grapheme, one element
		while ( e.MoveNext() )
		{
			var kind = ElementKind( e.GetTextElement() );

			if ( lastKind >= 0 && kind != lastKind )
				result.Add( index );

			lastKind = kind;
			index++;
		}

		if ( result[^1] != TextLength )
			result.Add( TextLength );

		return result;
	}

	/// <summary>
	/// What a text element is for word boundary purposes - part of a word, whitespace, or a
	/// symbol. Emoji are symbols, so the caret stops at each side of a run of them.
	/// </summary>
	private static int ElementKind( string element )
	{
		if ( !System.Text.Rune.TryGetRuneAt( element, 0, out var rune ) )
			return 2;

		if ( System.Text.Rune.IsWhiteSpace( rune ) ) return 0;
		if ( System.Text.Rune.IsLetterOrDigit( rune ) || rune.Value == '_' ) return 1;

		return 2;
	}

	/// <summary>
	/// Returns true if the input string is a 1 or 2 (\r\n) character newline symbol.
	/// </summary>
	private bool IsNewline( string str )
	{
		if ( str == "\n" ) return true;
		if ( str == "\r\n" ) return true;
		if ( str == "\r" ) return true;

		return false;
	}
}
