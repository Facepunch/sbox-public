using Sandbox.UI;

namespace UITests.Controls;

/// <summary>
/// The horizontal scroll a single line text entry does to keep the caret visible. The caret
/// rect, the hit test and the drawn text all have to agree about where the text sits.
/// </summary>
[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class TextEntryScrollTests
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

	const string LongText = "the quick brown fox jumps over the lazy dog and keeps on running";

	/// <summary>
	/// A narrow entry, so the text is much wider than the box and has to scroll.
	/// </summary>
	static TextEntry CreateNarrowEntry( string text )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );

		var entry = root.AddChild<TextEntry>();
		entry.Style.Set( "font-size: 16px; width: 120px; height: 24px;" );
		entry.Text = text;

		// Tick is what normally pushes this down to the label - a single line entry doesn't wrap
		entry.Children.OfType<Label>().First().Multiline = false;

		root.Layout();

		return entry;
	}

	static Label LabelOf( TextEntry entry ) => entry.Children.OfType<Label>().First();

	[TestMethod]
	public void CaretAtStartIsVisible()
	{
		var entry = CreateNarrowEntry( LongText );
		var label = LabelOf( entry );

		label.SetCaretPosition( 0 );

		var caret = label.GetCaretRect( 0 );
		var box = label.Box.RectInner;

		Assert.IsTrue( caret.Left >= box.Left - 1, $"caret {caret.Left} is left of the box {box.Left}" );
		Assert.IsTrue( caret.Right <= box.Right + 1, $"caret {caret.Right} is right of the box {box.Right}" );
	}

	[TestMethod]
	public void CaretAtEndIsVisible()
	{
		var entry = CreateNarrowEntry( LongText );
		var label = LabelOf( entry );

		label.SetCaretPosition( label.TextLength );

		var caret = label.GetCaretRect( label.CaretPosition );
		var box = label.Box.RectInner;

		Assert.IsTrue( caret.Left >= box.Left - 1, $"caret {caret.Left} is left of the box {box.Left}" );
		Assert.IsTrue( caret.Right <= box.Right + 1, $"caret {caret.Right} is right of the box {box.Right}" );
	}

	/// <summary>
	/// Walking the caret through the whole text keeps it inside the box the entire way.
	/// </summary>
	[TestMethod]
	public void CaretStaysVisibleWalkingRight()
	{
		var entry = CreateNarrowEntry( LongText );
		var label = LabelOf( entry );

		label.SetCaretPosition( 0 );

		for ( int i = 0; i <= label.TextLength; i++ )
		{
			label.SetCaretPosition( i );

			var caret = label.GetCaretRect( i );
			var box = label.Box.RectInner;

			Assert.IsTrue( caret.Left >= box.Left - 1, $"caret {i} at {caret.Left} is left of the box {box.Left}" );
			Assert.IsTrue( caret.Right <= box.Right + 1, $"caret {i} at {caret.Right} is right of the box {box.Right}" );
		}
	}

	/// <summary>
	/// The caret rect and the hit test are inverses - clicking where the caret is drawn puts
	/// the caret back in the same place, scrolled or not.
	/// </summary>
	[TestMethod]
	public void CaretRectAndHitTestAgreeWhileScrolled()
	{
		var entry = CreateNarrowEntry( LongText );
		var label = LabelOf( entry );

		label.SetCaretPosition( label.TextLength );

		for ( int i = label.TextLength; i > label.TextLength - 5; i-- )
		{
			var caret = label.GetCaretRect( i );
			var hit = label.GetLetterAtScreenPosition( new Vector2( caret.Left + 1, caret.Center.y ) );

			Assert.AreEqual( i, hit, $"clicking the caret drawn for {i} should return {i}" );
		}
	}

	/// <summary>
	/// Clearing the text scrolls back to the start instead of leaving the old offset behind.
	/// </summary>
	[TestMethod]
	public void ClearingTextResetsScroll()
	{
		var entry = CreateNarrowEntry( LongText );
		var label = LabelOf( entry );

		label.SetCaretPosition( label.TextLength );

		entry.Text = "ab";
		label.SetCaretPosition( 0 );

		var caret = label.GetCaretRect( 0 );
		var box = label.Box.RectInner;

		Assert.IsTrue( caret.Left >= box.Left - 1, $"caret {caret.Left} is left of the box {box.Left}" );
		Assert.IsTrue( caret.Left <= box.Left + 2, $"caret {caret.Left} should sit at the start of the box {box.Left}" );
	}

	/// <summary>
	/// Text that fits in the box never scrolls - the first character stays at the left edge.
	/// </summary>
	[TestMethod]
	public void ShortTextNeverScrolls()
	{
		var entry = CreateNarrowEntry( "ab" );
		var label = LabelOf( entry );

		label.SetCaretPosition( 2 );

		var caret = label.GetCaretRect( 0 );
		var box = label.Box.RectInner;

		Assert.IsTrue( caret.Left <= box.Left + 2, $"caret {caret.Left} should sit at the start of the box {box.Left}" );
	}

	/// <summary>
	/// A multiline entry with more text than height scrolls vertically to follow the caret,
	/// which a single line entry never does.
	/// </summary>
	[TestMethod]
	public void MultilineScrollsVerticallyToTheCaret()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );

		var entry = root.AddChild<TextEntry>();
		entry.Multiline = true;
		entry.Style.Set( "font-size: 16px; width: 200px; height: 50px;" );
		entry.Text = "one\ntwo\nthree\nfour\nfive\nsix";

		var label = LabelOf( entry );
		label.Multiline = true;

		root.Layout();

		var box = label.Box.RectInner;

		label.SetCaretPosition( 0 );
		var top = label.GetCaretRect( 0 );
		Assert.IsTrue( top.Top >= box.Top - 1, $"first line caret {top.Top} is above the box {box.Top}" );

		label.SetCaretPosition( label.TextLength );
		var bottom = label.GetCaretRect( label.CaretPosition );
		Assert.IsTrue( bottom.Bottom <= box.Bottom + 1, $"last line caret {bottom.Bottom} is below the box {box.Bottom}" );

		// It really did scroll - the first line is now above the visible area
		var first = label.GetCaretRect( 0 );
		Assert.IsTrue( first.Top < box.Top, $"first line {first.Top} should have scrolled above the box {box.Top}" );
	}

	/// <summary>
	/// The label inside an entry is laid out the way the editor stylesheet does it - a row with
	/// centered items, so the label is sized by its content rather than by the entry. The entry
	/// is what clips, so that is what the scrolling has to fit the caret into.
	/// </summary>
	[TestMethod]
	public void ScrollsWhenTheLabelIsWiderThanTheEntry()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );

		var entry = root.AddChild<TextEntry>();
		entry.Style.Set( "font-size: 16px; width: 120px; height: 34px; flex-direction: row; align-items: center; overflow: hidden;" );
		entry.Text = LongText;

		var label = LabelOf( entry );
		label.Multiline = false;
		label.Style.Set( "align-self: center;" );

		root.Layout();

		label.SetCaretPosition( label.TextLength );

		var caret = label.GetCaretRect( label.CaretPosition );
		var box = entry.Box.RectInner;

		Assert.IsTrue( caret.Right <= box.Right + 1, $"caret {caret.Right} is outside the entry {box.Right}" );
	}

	/// <summary>
	/// Typing past the right edge scrolls to follow the caret. This is the path a user actually
	/// takes - the text grows under the caret rather than the caret moving through fixed text.
	/// </summary>
	[TestMethod]
	public void TypingPastTheEdgeScrolls()
	{
		var entry = CreateNarrowEntry( "" );
		var label = LabelOf( entry );

		label.SetCaretPosition( 0 );

		foreach ( var c in "the quick brown fox jumps over the lazy dog" )
		{
			entry.OnKeyTyped( c );

			var caret = label.GetCaretRect( label.CaretPosition );
			var box = label.Box.RectInner;

			Assert.IsTrue( caret.Left + 2 <= box.Right + 1, $"after typing '{c}' the caret {caret.Left} is outside the box {box.Right}" );
		}
	}

	/// <summary>
	/// The text is drawn at the scroll offset, so moving that offset has to redraw it. Moving the
	/// caret with the arrow keys changes nothing else, so without this the text stays put on
	/// screen while the caret walks off it.
	/// </summary>
	[TestMethod]
	public void ScrollingMarksTheTextForRedraw()
	{
		var entry = CreateNarrowEntry( LongText );
		var label = LabelOf( entry );

		label.SetCaretPosition( 0 );

		// Pretend it has been drawn since
		label.IsRenderDirty = false;

		label.SetCaretPosition( label.TextLength );

		Assert.IsTrue( label.IsRenderDirty, "scrolling to the caret should redraw the text" );
	}
}
