using Sandbox.UI;
using System.Collections.Generic;

namespace UITests.Controls;

/// <summary>
/// Drives TextEntry the way input does - typed characters, button events, paste, IME events -
/// and checks the text, caret and selection come out right. Runs headless: layout is CPU-side
/// RichTextKit, and the text texture convar is turned off for the duration.
/// </summary>
[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class TextEntryTests
{
	bool previousRenderText;

	[TestInitialize]
	public void DisableTextTextures()
	{
		previousRenderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
	}

	[TestCleanup]
	public void RestoreTextTextures()
	{
		TextBlock.ui_rendertext = previousRenderText;
	}

	/// <summary>
	/// TextEntry with OnEvent exposed, so tests can feed it IME events directly.
	/// </summary>
	class TestEntry : TextEntry
	{
		public Label ContentLabel => Label;

		public void RaiseEvent( string name, object value = null )
		{
			OnEvent( new PanelEvent( name ) { Value = value } );
		}

		public void RaiseMouseEvent( string name, string button, KeyboardModifiers modifiers = default )
		{
			OnEvent( new MousePanelEvent( name, this, button ) { KeyboardModifiers = modifiers } );
		}

		/// <summary>
		/// Put the pretend cursor over a letter, so mouse events land somewhere meaningful.
		/// </summary>
		public void PointAt( int letter )
		{
			var caret = ContentLabel.GetCaretRect( letter );
			(FindRootPanel() as RootPanel).MousePos = caret.Position + caret.Size * 0.5f;
		}
	}

	/// <summary>
	/// An entry parented and laid out, so it has a text block and the selection API is live.
	/// </summary>
	static TestEntry CreateLaidOutEntry( string text = "" )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );

		var entry = root.AddChild<TestEntry>();
		entry.Style.Set( "font-size: 16px; width: 300px;" );
		entry.Text = text;
		entry.CaretPosition = entry.TextLength;

		root.Layout();

		entry.ContentLabel.ShouldDrawSelection = true;

		return entry;
	}

	static ButtonEvent Key( string button, KeyboardModifiers modifiers = default )
	{
		return new ButtonEvent( button, true, 0, modifiers );
	}

	static void Type( TextEntry entry, string text )
	{
		foreach ( var c in text )
		{
			entry.OnKeyTyped( c );
		}
	}

	[TestMethod]
	public void TypingInsertsAtCaret()
	{
		var entry = CreateLaidOutEntry();

		Type( entry, "helo" );
		entry.CaretPosition = 3;
		Type( entry, "l" );

		Assert.AreEqual( "hello", entry.Text );
		Assert.AreEqual( 4, entry.CaretPosition );
	}

	[TestMethod]
	public void TypedSurrogatePairInsertsOneElement()
	{
		var entry = CreateLaidOutEntry();

		Type( entry, "a\U0001F44Db" );

		Assert.AreEqual( "a\U0001F44Db", entry.Text );
		Assert.AreEqual( 3, entry.TextLength );
		Assert.AreEqual( 3, entry.CaretPosition );
	}

	[TestMethod]
	public void TypingReplacesSelection()
	{
		var entry = CreateLaidOutEntry( "hello world" );

		entry.ContentLabel.SetSelection( 0, 5 );
		Type( entry, "x" );

		Assert.AreEqual( "x world", entry.Text );
		Assert.AreEqual( 1, entry.CaretPosition );
		Assert.IsFalse( entry.ContentLabel.HasSelection() );
	}

	[TestMethod]
	public void MaxLengthBlocksTypingButAllowsReplacingSelection()
	{
		var entry = CreateLaidOutEntry( "abc" );
		entry.MaxLength = 3;

		Type( entry, "d" );
		Assert.AreEqual( "abc", entry.Text );

		entry.ContentLabel.SetSelection( 0, 3 );
		Type( entry, "d" );
		Assert.AreEqual( "d", entry.Text );
	}

	[TestMethod]
	public void NumericAllowsOneSeparatorAndSignedness()
	{
		var entry = CreateLaidOutEntry();
		entry.Numeric = true;

		Type( entry, "1.5.2" );
		Assert.AreEqual( "1.52", entry.Text );

		entry.Text = "";
		entry.CaretPosition = 0;
		entry.MinValue = 0;
		Type( entry, "-5x" );
		Assert.AreEqual( "5", entry.Text );
	}

	[TestMethod]
	public void PasteFiltersAndCountsElements()
	{
		var entry = CreateLaidOutEntry();

		entry.OnPaste( "a\U0001F44Db" );

		Assert.AreEqual( "a\U0001F44Db", entry.Text );
		Assert.AreEqual( 3, entry.CaretPosition );
	}

	[TestMethod]
	public void PasteTruncatesToMaxLengthByElements()
	{
		var entry = CreateLaidOutEntry();
		entry.MaxLength = 2;

		entry.OnPaste( "\U0001F44D\U0001F44D\U0001F44D" );

		Assert.AreEqual( "\U0001F44D\U0001F44D", entry.Text );
		Assert.AreEqual( 2, entry.TextLength );
	}

	[TestMethod]
	public void BackspaceAndDeleteRemoveAroundCaret()
	{
		var entry = CreateLaidOutEntry( "abc" );

		entry.CaretPosition = 2;
		entry.OnButtonTyped( Key( "backspace" ) );
		Assert.AreEqual( "ac", entry.Text );
		Assert.AreEqual( 1, entry.CaretPosition );

		entry.OnButtonTyped( Key( "delete" ) );
		Assert.AreEqual( "a", entry.Text );
	}

	[TestMethod]
	public void CtrlBackspaceDeletesToWordBoundary()
	{
		var entry = CreateLaidOutEntry( "hello world" );

		entry.CaretPosition = 11;
		entry.OnButtonTyped( Key( "backspace", KeyboardModifiers.Ctrl ) );

		Assert.AreEqual( "hello ", entry.Text );
	}

	[TestMethod]
	public void WordJumpRightNeverGetsStuck()
	{
		var entry = CreateLaidOutEntry( "hello \U0001F44D\U0001F44D world" );

		entry.CaretPosition = 0;

		var previous = -1;
		for ( int i = 0; i < 20 && entry.CaretPosition < entry.TextLength; i++ )
		{
			Assert.IsTrue( entry.CaretPosition > previous, "caret should always move right" );
			previous = entry.CaretPosition;

			entry.OnButtonTyped( Key( "right", KeyboardModifiers.Ctrl ) );
		}

		Assert.AreEqual( entry.TextLength, entry.CaretPosition );
	}

	[TestMethod]
	public void WordBoundariesSitAtEmojiEdges()
	{
		var label = new Label { Text = "ab \U0001F44D\U0001F44D cd" };

		// elements: a b space emoji emoji space c d
		var boundaries = label.GetWordBoundaryIndices();

		CollectionAssert.Contains( boundaries, 3, "start of the emoji run" );
		CollectionAssert.Contains( boundaries, 5, "end of the emoji run" );
	}

	[TestMethod]
	public void WordBoundariesForPlainWords()
	{
		var label = new Label { Text = "ab cd" };

		CollectionAssert.AreEqual( new List<int> { 0, 2, 3, 5 }, label.GetWordBoundaryIndices() );
	}

	[TestMethod]
	public void ShiftArrowsGrowAndShrinkSelection()
	{
		var entry = CreateLaidOutEntry( "hello" );

		entry.CaretPosition = 1;
		entry.OnButtonTyped( Key( "right", KeyboardModifiers.Shift ) );
		entry.OnButtonTyped( Key( "right", KeyboardModifiers.Shift ) );

		Assert.AreEqual( 1, entry.ContentLabel.SelectionStart );
		Assert.AreEqual( 3, entry.ContentLabel.SelectionEnd );
		Assert.AreEqual( "el", entry.ContentLabel.GetSelectedText() );

		entry.OnButtonTyped( Key( "left", KeyboardModifiers.Shift ) );
		Assert.AreEqual( "e", entry.ContentLabel.GetSelectedText() );
	}

	[TestMethod]
	public void ShiftHomeSelectsToStart()
	{
		var entry = CreateLaidOutEntry( "hello" );

		entry.CaretPosition = 3;
		entry.OnButtonTyped( Key( "home", KeyboardModifiers.Shift ) );

		Assert.AreEqual( "hel", entry.ContentLabel.GetSelectedText() );
		Assert.AreEqual( 0, entry.CaretPosition );
	}

	[TestMethod]
	public void PlainArrowCollapsesSelectionToItsEdge()
	{
		var entry = CreateLaidOutEntry( "hello" );

		entry.ContentLabel.SetSelection( 1, 4 );
		entry.OnButtonTyped( Key( "left" ) );

		Assert.IsFalse( entry.ContentLabel.HasSelection() );
		Assert.AreEqual( 1, entry.CaretPosition );
	}

	[TestMethod]
	public void CtrlASelectsEverything()
	{
		var entry = CreateLaidOutEntry( "hello" );

		entry.CaretPosition = 0;
		entry.OnButtonTyped( Key( "a", KeyboardModifiers.Ctrl ) );

		Assert.AreEqual( "hello", entry.ContentLabel.GetSelectedText() );

		// The caret ends up at the end of the selection, so what you type next replaces it
		Assert.AreEqual( entry.TextLength, entry.CaretPosition );
	}

	[TestMethod]
	public void CopyReturnsSelectionCutRemovesIt()
	{
		var entry = CreateLaidOutEntry( "hello world" );

		entry.ContentLabel.SetSelection( 6, 11 );

		Assert.AreEqual( "world", entry.GetClipboardValue( cut: false ) );
		Assert.AreEqual( "hello world", entry.Text );

		Assert.AreEqual( "world", entry.GetClipboardValue( cut: true ) );
		Assert.AreEqual( "hello ", entry.Text );
	}

	[TestMethod]
	public void ImePreviewSplicesInAndOut()
	{
		var entry = CreateLaidOutEntry( "ab" );
		entry.CaretPosition = 1;

		entry.RaiseEvent( "onimestart" );
		entry.RaiseEvent( "onime", "か" );
		Assert.AreEqual( "aかb", entry.Text );

		entry.RaiseEvent( "onime", "かな" );
		Assert.AreEqual( "aかなb", entry.Text );
		Assert.AreEqual( 3, entry.CaretPosition );

		entry.RaiseEvent( "onime", "" );
		entry.RaiseEvent( "onimeend" );
		Assert.AreEqual( "ab", entry.Text );
		Assert.AreEqual( 1, entry.CaretPosition );
	}

	/// <summary>
	/// SDL delivers the committed text as typed input while the composition preview is still
	/// spliced into the text - the preview has to come out first, not clobber the commit.
	/// </summary>
	[TestMethod]
	public void ImeCommitArrivesBeforeCompositionClears()
	{
		var entry = CreateLaidOutEntry( "ab" );
		entry.CaretPosition = 1;

		entry.RaiseEvent( "onimestart" );
		entry.RaiseEvent( "onime", "かな" );

		// The commit lands as ordinary typed text, then the composition clears
		Type( entry, "かな" );
		entry.RaiseEvent( "onime", "" );
		entry.RaiseEvent( "onimeend" );

		Assert.AreEqual( "aかなb", entry.Text );
		Assert.AreEqual( 3, entry.CaretPosition );
	}

	[TestMethod]
	public void ImeCompositionReplacesSelection()
	{
		var entry = CreateLaidOutEntry( "hello" );

		entry.ContentLabel.SetSelection( 0, 5 );
		entry.RaiseEvent( "onimestart" );
		entry.RaiseEvent( "onime", "か" );

		Assert.AreEqual( "か", entry.Text );
	}

	[TestMethod]
	public void EmojiReplaceOnlyTouchesTheCodeAtTheCaret()
	{
		var entry = CreateLaidOutEntry();
		entry.AllowEmojiReplace = true;

		Type( entry, ":fire: and :fire:" );

		Assert.AreEqual( "\U0001F525 and \U0001F525", entry.Text );
		Assert.AreEqual( entry.TextLength, entry.CaretPosition );
	}

	[TestMethod]
	public void HomeAndEndMoveTheCaret()
	{
		var entry = CreateLaidOutEntry( "hello" );

		entry.CaretPosition = 3;
		entry.OnButtonTyped( Key( "home" ) );
		Assert.AreEqual( 0, entry.CaretPosition );

		entry.OnButtonTyped( Key( "end" ) );
		Assert.AreEqual( 5, entry.CaretPosition );
	}

	[TestMethod]
	public void MultilineEnterInsertsNewline()
	{
		var entry = CreateLaidOutEntry( "ab" );
		entry.Multiline = true;
		entry.ContentLabel.Multiline = true;
		entry.CaretPosition = 1;

		entry.OnButtonTyped( Key( "enter" ) );

		Assert.AreEqual( "a\nb", entry.Text );
	}

	[TestMethod]
	public void UndoPutsBackWhatWasTyped()
	{
		var entry = CreateLaidOutEntry();

		Type( entry, "hello" );
		Assert.AreEqual( "hello", entry.Text );

		entry.Undo();
		Assert.AreEqual( "", entry.Text );
	}

	/// <summary>
	/// A run of typing is one undo step, so a word comes back out in one go rather than a
	/// letter at a time. Whitespace ends the run, so it goes word by word.
	/// </summary>
	[TestMethod]
	public void TypingUndoesAWordAtATime()
	{
		var entry = CreateLaidOutEntry();

		Type( entry, "hello world" );

		entry.Undo();
		Assert.AreEqual( "hello ", entry.Text );

		entry.Undo();
		Assert.AreEqual( "hello", entry.Text );

		entry.Undo();
		Assert.AreEqual( "", entry.Text );
	}

	[TestMethod]
	public void RedoPutsItBack()
	{
		var entry = CreateLaidOutEntry();

		Type( entry, "abc" );
		entry.Undo();
		Assert.AreEqual( "", entry.Text );

		entry.Redo();
		Assert.AreEqual( "abc", entry.Text );
	}

	/// <summary>
	/// Editing after undoing throws the redos away, the same as everywhere else.
	/// </summary>
	[TestMethod]
	public void EditingAfterUndoDropsTheRedos()
	{
		var entry = CreateLaidOutEntry();

		Type( entry, "abc" );
		entry.Undo();

		Type( entry, "x" );

		Assert.IsFalse( entry.CanRedo );
		Assert.AreEqual( "x", entry.Text );
	}

	/// <summary>
	/// Undo restores where the caret and selection were, not just the text.
	/// </summary>
	[TestMethod]
	public void UndoRestoresTheSelection()
	{
		var entry = CreateLaidOutEntry( "hello world" );

		entry.ContentLabel.SetSelection( 0, 5 );
		Type( entry, "x" );
		Assert.AreEqual( "x world", entry.Text );

		entry.Undo();

		Assert.AreEqual( "hello world", entry.Text );
		Assert.AreEqual( "hello", entry.ContentLabel.GetSelectedText() );
	}

	/// <summary>
	/// A pasted lot is one step however long it is, never joined onto the typing around it.
	/// </summary>
	[TestMethod]
	public void PasteIsItsOwnStep()
	{
		var entry = CreateLaidOutEntry();

		Type( entry, "ab" );
		entry.OnPaste( "CD" );
		Assert.AreEqual( "abCD", entry.Text );

		entry.Undo();
		Assert.AreEqual( "ab", entry.Text );
	}

	/// <summary>
	/// Deleting is its own kind of run - backspacing a few times undoes in one go, and doesn't
	/// join onto the typing before it.
	/// </summary>
	[TestMethod]
	public void DeletingIsItsOwnRun()
	{
		var entry = CreateLaidOutEntry( "abcdef" );
		entry.CaretPosition = entry.TextLength;

		entry.OnButtonTyped( Key( "backspace" ) );
		entry.OnButtonTyped( Key( "backspace" ) );
		Assert.AreEqual( "abcd", entry.Text );

		entry.Undo();
		Assert.AreEqual( "abcdef", entry.Text );
	}

	/// <summary>
	/// Ctrl+Z and Ctrl+Y arrive as ordinary button events, so the entry handles them itself.
	/// </summary>
	[TestMethod]
	public void CtrlZAndCtrlYDriveIt()
	{
		var entry = CreateLaidOutEntry();

		Type( entry, "abc" );

		entry.OnButtonTyped( Key( "z", KeyboardModifiers.Ctrl ) );
		Assert.AreEqual( "", entry.Text );

		// Ctrl+Y redoes
		entry.OnButtonTyped( Key( "y", KeyboardModifiers.Ctrl ) );
		Assert.AreEqual( "abc", entry.Text );

		// So does ctrl+shift+z
		entry.OnButtonTyped( Key( "z", KeyboardModifiers.Ctrl ) );
		Assert.AreEqual( "", entry.Text );

		entry.OnButtonTyped( Key( "z", KeyboardModifiers.Ctrl | KeyboardModifiers.Shift ) );
		Assert.AreEqual( "abc", entry.Text );
	}

	[TestMethod]
	public void NothingToUndoIsHarmless()
	{
		var entry = CreateLaidOutEntry( "abc" );

		Assert.IsFalse( entry.CanUndo );

		entry.Undo();
		entry.Redo();

		Assert.AreEqual( "abc", entry.Text );
	}

	/// <summary>
	/// A third click takes the whole line. On a single line entry that's everything.
	/// </summary>
	[TestMethod]
	public void TripleClickSelectsTheLine()
	{
		var entry = CreateLaidOutEntry( "hello world" );

		entry.RaiseMouseEvent( "ontripleclick", "mouseleft" );

		Assert.AreEqual( "hello world", entry.ContentLabel.GetSelectedText() );
	}

	[TestMethod]
	public void TripleClickTakesOneLineOfMany()
	{
		var entry = CreateLaidOutEntry( "one\ntwo\nthree" );
		entry.Multiline = true;
		entry.ContentLabel.Multiline = true;

		// Click on the middle line - the handler picks the line from where the mouse is
		var caret = entry.ContentLabel.GetCaretRect( 5 );
		(entry.FindRootPanel() as RootPanel).MousePos = caret.Position + caret.Size * 0.5f;

		entry.RaiseMouseEvent( "ontripleclick", "mouseleft" );

		Assert.AreEqual( "two", entry.ContentLabel.GetSelectedText() );
	}

	/// <summary>
	/// Shift and click grows the selection out to where you clicked, keeping the anchor - it
	/// doesn't start again from there.
	/// </summary>
	[TestMethod]
	public void ShiftClickExtendsFromTheCaret()
	{
		var entry = CreateLaidOutEntry( "hello world" );

		entry.CaretPosition = 0;
		entry.PointAt( 5 );
		entry.RaiseMouseEvent( "onmousedown", "mouseleft", KeyboardModifiers.Shift );

		Assert.AreEqual( "hello", entry.ContentLabel.GetSelectedText() );
		Assert.AreEqual( 5, entry.CaretPosition );
	}

	/// <summary>
	/// Shift clicking again moves the same end of the selection rather than starting over.
	/// </summary>
	[TestMethod]
	public void ShiftClickKeepsTheAnchor()
	{
		var entry = CreateLaidOutEntry( "hello world" );

		entry.CaretPosition = 0;
		entry.PointAt( 5 );
		entry.RaiseMouseEvent( "onmousedown", "mouseleft", KeyboardModifiers.Shift );

		entry.PointAt( 11 );
		entry.RaiseMouseEvent( "onmousedown", "mouseleft", KeyboardModifiers.Shift );

		Assert.AreEqual( "hello world", entry.ContentLabel.GetSelectedText() );
	}

	/// <summary>
	/// A plain click still starts a new selection where it landed.
	/// </summary>
	[TestMethod]
	public void PlainClickStartsAgain()
	{
		var entry = CreateLaidOutEntry( "hello world" );

		entry.ContentLabel.SetSelection( 0, 5 );

		entry.PointAt( 8 );
		entry.RaiseMouseEvent( "onmousedown", "mouseleft" );

		Assert.IsFalse( entry.ContentLabel.HasSelection() );
		Assert.AreEqual( 8, entry.CaretPosition );
	}

	/// <summary>
	/// Read only text can't be typed over, deleted, pasted into or undone.
	/// </summary>
	[TestMethod]
	public void ReadOnlyRefusesEdits()
	{
		var entry = CreateLaidOutEntry( "hello" );
		entry.ReadOnly = true;
		entry.CaretPosition = entry.TextLength;

		Type( entry, "x" );
		Assert.AreEqual( "hello", entry.Text );

		entry.OnButtonTyped( Key( "backspace" ) );
		Assert.AreEqual( "hello", entry.Text );

		entry.OnButtonTyped( Key( "delete" ) );
		Assert.AreEqual( "hello", entry.Text );

		entry.OnPaste( "nope" );
		Assert.AreEqual( "hello", entry.Text );
	}

	/// <summary>
	/// It's still text though - it selects and copies, which is the whole point of it over
	/// being disabled.
	/// </summary>
	[TestMethod]
	public void ReadOnlyStillSelectsAndCopies()
	{
		var entry = CreateLaidOutEntry( "hello world" );
		entry.ReadOnly = true;

		Assert.IsTrue( entry.AcceptsFocus );

		entry.ContentLabel.SetSelection( 0, 5 );
		Assert.AreEqual( "hello", entry.ContentLabel.GetSelectedText() );

		Assert.AreEqual( "hello", entry.GetClipboardValue( cut: false ) );

		// Cutting copies but leaves the text alone
		Assert.AreEqual( "hello", entry.GetClipboardValue( cut: true ) );
		Assert.AreEqual( "hello world", entry.Text );
	}

	/// <summary>
	/// Disabled is the stronger one - it doesn't even take focus.
	/// </summary>
	[TestMethod]
	public void DisabledRefusesFocusReadOnlyDoesNot()
	{
		var entry = CreateLaidOutEntry( "hello" );

		entry.ReadOnly = true;
		Assert.IsTrue( entry.AcceptsFocus );

		entry.Disabled = true;
		Assert.IsFalse( entry.AcceptsFocus );
	}

	/// <summary>
	/// Going down through a short line and out the other side keeps the column you started in,
	/// rather than sticking where the short line ended.
	/// </summary>
	[TestMethod]
	public void UpDownKeepsItsColumn()
	{
		var entry = CreateMultiline( "the first long line" + BS + "ab" + BS + "another long line" );
		var label = entry.ContentLabel;

		// Part way along the first line
		label.SetCaretPosition( 12 );
		var wanted = label.GetCaretRect( label.CaretPosition ).Left;

		// Down onto the short line - the caret has to give ground here, there's nowhere else
		label.MoveCaretLine( 1, false );

		// And down again onto a long one, where the original column is available again
		label.MoveCaretLine( 1, false );

		var landed = label.GetCaretRect( label.CaretPosition ).Left;

		Assert.IsTrue( System.MathF.Abs( landed - wanted ) < 4.0f,
			$"caret landed at {landed}, wanted about {wanted}" );
	}

	/// <summary>
	/// Moving sideways gives the column up, so the next vertical move starts from where the
	/// caret actually is.
	/// </summary>
	[TestMethod]
	public void MovingSidewaysGivesUpTheColumn()
	{
		var entry = CreateMultiline( "the first long line" + BS + "ab" + BS + "another long line" );
		var label = entry.ContentLabel;

		label.SetCaretPosition( 12 );

		label.MoveCaretLine( 1, false );

		// Now on the short line - a sideways move makes this the new column
		label.MoveToLineStart();

		label.MoveCaretLine( 1, false );

		var landed = label.GetCaretRect( label.CaretPosition ).Left;
		var lineStart = label.GetCaretRect( 23 ).Left;

		Assert.IsTrue( System.MathF.Abs( landed - lineStart ) < 4.0f,
			$"caret landed at {landed}, wanted the start of the line at {lineStart}" );
	}

	const string BS = "\n";

	static TestEntry CreateMultiline( string text )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );

		var entry = root.AddChild<TestEntry>();
		entry.Multiline = true;
		entry.Style.Set( "font-size: 16px; width: 400px; height: 200px;" );
		entry.Text = text;

		root.Layout();

		entry.ContentLabel.ShouldDrawSelection = true;
		return entry;
	}
}
