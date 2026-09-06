using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class TextEntryScrollbarTests
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

	static string Lines( int count ) => string.Join( "\n", Enumerable.Range( 1, count ).Select( i => $"line {i}" ) );

	/// <summary>
	/// The base sheet's text entry rules, then the editor's on top
	/// </summary>
	const string Sheet = """
		.textentry { align-items: center; justify-content: center; white-space: nowrap; overflow: hidden; flex-direction: row; position: relative; padding: 10px; }
		.textentry.is-multiline { align-items: flex-start; white-space: normal; overflow-y: scroll; }
		.textentry .content-label { flex-grow: 1; overflow: hidden; }
		.textentry.is-multiline { scrollbar-width: thin; }
		.textentry.is-multiline .content-label { align-self: flex-start; width: 100%; }
		""";

	/// <summary>
	/// A short entry holding far more lines than fit.
	/// </summary>
	static (RootPanel Root, TextEntry Entry) CreateTallEntry( string style = "" )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		root.StyleSheet.Parse( Sheet );

		var entry = root.AddChild<TextEntry>();
		entry.Multiline = true;
		entry.Style.Set( $"font-size: 16px; width: 200px; height: 100px; {style}" );
		entry.Text = Lines( 40 );

		for ( int i = 0; i < 3; i++ ) root.Layout();

		return (root, entry);
	}

	static Label LabelOf( TextEntry entry ) => entry.Children.OfType<Label>().First();

	[TestMethod]
	public void LabelGrowsAndTheEntryScrollsIt()
	{
		var (_, entry) = CreateTallEntry();
		var label = LabelOf( entry );

		Assert.IsTrue( label.Box.Rect.Height > entry.Box.Rect.Height, "the label is as tall as its text, not the entry" );
		Assert.IsTrue( entry.HasScrollY );
		Assert.IsTrue( entry.ScrollSize.y > 0 );
		Assert.AreEqual( 0, entry.ScrollOffset.y, 0.001f, "starts at the top" );
	}

	[TestMethod]
	public void WheelOverTheTextScrollsTheEntry()
	{
		var (_, entry) = CreateTallEntry();

		// The wheel lands on the label under the mouse and bubbles up to the entry
		LabelOf( entry ).OnMouseWheel( new Vector2( 0, 1 ) );

		Assert.AreEqual( 20, entry.ScrollVelocity.y, 0.001f );
	}

	[TestMethod]
	public void DraggingSelectsRatherThanScrolls()
	{
		var (_, entry) = CreateTallEntry();

		Assert.IsFalse( entry.CanDragScroll );
		Assert.IsFalse( entry.WantsDrag );
	}

	[TestMethod]
	public void ScrollbarWidthGivesItABar()
	{
		// The editor sheet gives multiline entries a thin bar
		var (_, entry) = CreateTallEntry();
		var bar = entry.ScrollbarY;

		Assert.IsNotNull( bar );
		Assert.IsTrue( bar.IsVisible );
		Assert.AreEqual( entry.Box.Rect.Right - ScrollBar.ThinThickness, bar.Box.Rect.Left, 0.001f );

		var (_, none) = CreateTallEntry( "scrollbar-width: none;" );
		Assert.IsNull( none.ScrollbarY, "and the entry can opt out" );
	}

	static ButtonEvent Key( string button ) => new ButtonEvent( button, true, 0, default );

	[TestMethod]
	public void TypingAtTheEndKeepsTheCaretInView()
	{
		var (root, entry) = CreateTallEntry();
		var label = LabelOf( entry );

		entry.CaretPosition = entry.Text.Length;
		for ( int i = 0; i < 3; i++ ) root.Layout();

		// Enter on its own first - the empty line it opens has to count, before any letter lands on it
		foreach ( var key in "\nx\n\n\nx\n" )
		{
			entry.OnKeyTyped( key );
			for ( int i = 0; i < 2; i++ ) root.Layout();

			var caret = label.GetCaretRect( label.CaretPosition );
			var view = entry.Box.RectInner;

			Assert.IsTrue( caret.Bottom <= view.Bottom + 1, $"after '{key}': caret {caret.Bottom} is below the entry {view.Bottom}" );
			Assert.IsTrue( caret.Top >= view.Top - 1, $"after '{key}': caret {caret.Top} is above the entry {view.Top}" );
		}
	}

	[TestMethod]
	public void TrailingNewlineIsALine()
	{
		var (root, entry) = CreateTallEntry();
		var label = LabelOf( entry );

		entry.Text = "one";
		for ( int i = 0; i < 2; i++ ) root.Layout();
		var oneLine = label.Box.Rect.Height;

		entry.Text = "one\n";
		for ( int i = 0; i < 2; i++ ) root.Layout();

		Assert.IsTrue( label.Box.Rect.Height > oneLine + 5, $"an empty last line still takes a line: {label.Box.Rect.Height} vs {oneLine}" );

		label.SetCaretPosition( label.TextLength );
		var caret = label.GetCaretRect( label.CaretPosition );
		Assert.IsTrue( caret.Bottom <= label.Box.Rect.Bottom + 1, $"caret {caret.Bottom} hangs below the label {label.Box.Rect.Bottom}" );
	}

	[TestMethod]
	public void UpAndDownStepWholeLinesThroughBlankOnes()
	{
		var (root, entry) = CreateTallEntry();
		var label = LabelOf( entry );

		// "one" / "" / "two" - the blank line is index 4, "two" starts at 5
		entry.Text = "one\n\ntwo";
		for ( int i = 0; i < 2; i++ ) root.Layout();

		label.SetCaretPosition( 7 );        // "tw|o"
		label.MoveCaretLine( -1, false );
		Assert.AreEqual( 4, label.CaretPosition, "up from the last line lands on the blank one" );

		label.MoveCaretLine( -1, false );
		Assert.IsTrue( label.CaretPosition <= 3 && label.CaretPosition > 0, $"up from the blank line lands on the first, near the same column - got {label.CaretPosition}" );

		label.MoveCaretLine( 1, false );
		Assert.AreEqual( 4, label.CaretPosition, "and back down onto the blank line" );

		label.MoveCaretLine( 1, false );
		Assert.IsTrue( label.CaretPosition >= 5, $"down from the blank line lands on the last - got {label.CaretPosition}" );
	}

	[TestMethod]
	public void ArrowKeysKeepTheCaretInView()
	{
		var (root, entry) = CreateTallEntry();
		var label = LabelOf( entry );

		entry.CaretPosition = 0;
		for ( int i = 0; i < 3; i++ ) root.Layout();

		for ( int line = 0; line < 30; line++ )
		{
			entry.OnButtonTyped( Key( "down" ) );
			for ( int i = 0; i < 2; i++ ) root.Layout();

			var caret = label.GetCaretRect( label.CaretPosition );
			var view = entry.Box.RectInner;

			Assert.IsTrue( caret.Bottom <= view.Bottom + 1, $"line {line}: caret {caret.Bottom} is below the entry {view.Bottom}" );
			Assert.IsTrue( caret.Top >= view.Top - 1, $"line {line}: caret {caret.Top} is above the entry {view.Top}" );
		}

		Assert.IsTrue( entry.ScrollOffset.y > 0, "walking down scrolled the entry" );
	}

	/// <summary>
	/// Panel input fed by hand - the cursor and how far it moved come from the test, not the mouse
	/// </summary>
	class HandInput : PanelInput
	{
		public Vector2 Position;
		public Vector2 Delta;

		internal override Vector2 CursorPosition => Position;
		internal override Vector2 CursorDelta => Delta;

		// There's no input context to hand a cursor to here
		public override void SetCursor( string name ) { }
	}

	[TestMethod]
	public void TheBarCanBeDraggedWithoutSelectingText()
	{
		var (root, entry) = CreateTallEntry( "pointer-events: all;" );
		var bar = entry.ScrollbarY;
		var thumb = bar.Children.Single();
		var input = new HandInput();

		// Press on the thumb. The entry sees the press too, and has to leave it to the bar.
		var grab = thumb.Box.Rect.Center;
		input.Position = grab;
		input.Delta = 0;
		input.UpdateMouse( root, new InputData { MousePos = grab, Mouse0 = true } );
		root.Layout();

		Assert.AreEqual( 0, entry.ScrollOffset.y, 0.001f );

		// Drag down past the threshold that turns a press into a drag
		var to = grab + new Vector2( 0, 40 );
		input.Position = to;
		input.Delta = new Vector2( 0, 40 );
		input.UpdateMouse( root, new InputData { MousePos = to, Mouse0 = true } );
		for ( int i = 0; i < 2; i++ ) root.Layout();

		Assert.IsTrue( entry.ScrollOffset.y > 0, "dragging the thumb scrolled the entry" );
		Assert.IsFalse( LabelOf( entry ).HasSelection(), "and swept no selection across the text underneath" );

		input.Delta = 0;
		input.UpdateMouse( root, new InputData { MousePos = to, Mouse0 = false } );
		root.Layout();
	}

	[TestMethod]
	public void TheBarKeepsTheArrowCursor()
	{
		var (_, entry) = CreateTallEntry( "cursor: text;" );
		var bar = entry.ScrollbarY;

		Assert.AreEqual( "text", entry.ComputedStyle.Cursor );
		Assert.AreEqual( "default", bar.ComputedStyle.Cursor, "the bar doesn't inherit the entry's I-beam" );
		Assert.AreEqual( "default", bar.Children.Single().ComputedStyle.Cursor, "and neither does its thumb" );
	}

	[TestMethod]
	public void CaretScrollsTheEntryIntoView()
	{
		var (root, entry) = CreateTallEntry();
		var label = LabelOf( entry );

		label.SetCaretPosition( label.TextLength );
		for ( int i = 0; i < 3; i++ ) root.Layout();

		// Into view means into the content box, with the padding around it - at the end, the bottom
		// padding is on screen below the last line, like a textarea
		var view = entry.Box.RectInner;
		var caret = label.GetCaretRect( label.CaretPosition );

		Assert.IsTrue( entry.ScrollOffset.y > 0, "the entry scrolled down to the caret" );
		Assert.IsTrue( caret.Top >= view.Top - 1, $"caret {caret.Top} is above the entry {view.Top}" );
		Assert.IsTrue( caret.Bottom <= view.Bottom + 1, $"caret {caret.Bottom} is below the entry {view.Bottom}" );

		label.SetCaretPosition( 0 );
		for ( int i = 0; i < 3; i++ ) root.Layout();

		caret = label.GetCaretRect( 0 );

		Assert.AreEqual( 0, entry.ScrollOffset.y, 0.001f, "and back up for the start" );
		Assert.IsTrue( caret.Top >= view.Top - 1 );
	}
}
