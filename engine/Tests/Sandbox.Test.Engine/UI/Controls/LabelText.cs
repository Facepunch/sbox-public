using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize] // Modifies UI System Global
public class LabelTextTest
{
	bool previousRenderText;

	/// <summary>
	/// Label layout finalization rebuilds the text texture (TextBlock.RebuildTexture), which needs
	/// the native render system that this tier never boots. Turn the convar off for the duration of
	/// each test - text measurement is pure RichTextKit (CPU) and is unaffected.
	/// </summary>
	[TestInitialize]
	public void DisableTextTextures()
	{
		previousRenderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
	}

	/// <summary>
	/// Restores the text texture convar so this class doesn't leak state into other tests.
	/// </summary>
	[TestCleanup]
	public void RestoreTextTextures()
	{
		TextBlock.ui_rendertext = previousRenderText;
	}

	/// <summary>
	/// Creates a root sized 1000x1000 whose children are content-sized in both axes - the default
	/// align-items: stretch would otherwise stretch label boxes to fill the cross axis.
	/// </summary>
	static RootPanel CreateRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		return root;
	}

	/// <summary>
	/// Text starts null, round-trips through the setter, and assigning null is coalesced to an
	/// empty string rather than stored as null. The constructor also adds the "label" class.
	/// </summary>
	[TestMethod]
	public void TextGetSetRoundTrip()
	{
		var label = new Label();

		Assert.IsTrue( label.Class.Contains( "label" ) );
		Assert.IsNull( label.Text );

		label.Text = "Hello";
		Assert.AreEqual( "Hello", label.Text );

		label.Text = null;
		Assert.AreEqual( "", label.Text );
	}

	/// <summary>
	/// The text+classname constructor sets the text and adds the extra class on top of the
	/// built-in "label" class.
	/// </summary>
	[TestMethod]
	public void ConstructorSetsTextAndClass()
	{
		var label = new Label( "Hi", "greeting" );

		Assert.AreEqual( "Hi", label.Text );
		Assert.IsTrue( label.Class.Contains( "label" ) );
		Assert.IsTrue( label.Class.Contains( "greeting" ) );
	}

	/// <summary>
	/// SetContent (markup inner text) and SetProperty( "text", ... ) (markup attribute) are both
	/// equivalent to assigning the Text property directly.
	/// </summary>
	[TestMethod]
	public void SetContentAndSetPropertyMatchTextProperty()
	{
		var viaProperty = new Label();
		viaProperty.Text = "Hello";

		var viaContent = new Label();
		viaContent.SetContent( "Hello" );

		var viaSetProperty = new Label();
		viaSetProperty.SetProperty( "text", "Hello" );

		Assert.AreEqual( viaProperty.Text, viaContent.Text );
		Assert.AreEqual( viaProperty.Text, viaSetProperty.Text );
	}

	/// <summary>
	/// With Tokenize disabled, text starting with '#' is stored verbatim instead of being run
	/// through the language phrase lookup.
	/// </summary>
	[TestMethod]
	public void TokenizeDisabledKeepsHashPrefixedText()
	{
		var label = new Label { Tokenize = false, Text = "#some.token" };

		Assert.AreEqual( "#some.token", label.Text );
	}

	/// <summary>
	/// TextLength counts text elements (graphemes), not chars - a surrogate-pair emoji is one
	/// element even though it is two chars.
	/// </summary>
	[TestMethod]
	public void TextLengthCountsGraphemes()
	{
		var label = new Label();
		label.Text = "a\U0001F44Db";

		Assert.AreEqual( 4, label.Text.Length );
		Assert.AreEqual( 3, label.TextLength );
	}

	/// <summary>
	/// A label with text and a fixed font size measures to a non-zero, content-sized box after
	/// layout - the measure function runs through RichTextKit on the CPU.
	/// </summary>
	[TestMethod]
	public void MeasuredBoxIsNonZeroAfterLayout()
	{
		var root = CreateRoot();

		var label = root.AddChild<Label>();
		label.Text = "Hello";
		label.Style.Set( "font-size: 16px; white-space: nowrap;" );

		root.Layout();

		Assert.IsTrue( label.Box.Rect.Width > 0 );
		Assert.IsTrue( label.Box.Rect.Height > 0 );
		Assert.IsTrue( label.Box.Rect.Width < 1000 );
	}

	/// <summary>
	/// Longer text measures wider than shorter text at the same font size, proving the measured
	/// size actually comes from the text content.
	/// </summary>
	[TestMethod]
	public void LongerTextMeasuresWider()
	{
		var root = CreateRoot();

		var shorter = root.AddChild<Label>();
		shorter.Text = "Hello";
		shorter.Style.Set( "font-size: 16px; white-space: nowrap;" );

		var longer = root.AddChild<Label>();
		longer.Text = "Hello Hello Hello";
		longer.Style.Set( "font-size: 16px; white-space: nowrap;" );

		root.Layout();

		Assert.IsTrue( shorter.Box.Rect.Width > 0 );
		Assert.IsTrue( longer.Box.Rect.Width > shorter.Box.Rect.Width );
	}

	/// <summary>
	/// Changing a measured control's content dirties its panel layout, so its existing box is
	/// remeasured on the next layout pass rather than retaining the first measurement.
	/// </summary>
	[TestMethod]
	public void TextMutationRemeasuresExistingLabel()
	{
		var root = CreateRoot();
		var label = root.AddChild<Label>();
		label.Text = "Hi";
		label.Style.Set( "font-size: 16px; white-space: nowrap;" );
		root.Layout();
		var initialWidth = label.Box.Rect.Width;

		label.Text = "This is substantially longer";
		root.Layout();

		Assert.IsTrue( label.Box.Rect.Width > initialWidth );
	}

	/// <summary>
	/// At a fixed width, wrapping text (the default) measures taller than the same text with
	/// white-space: nowrap, which stays on a single line.
	/// </summary>
	[TestMethod]
	public void WordWrapIncreasesHeightOverNoWrap()
	{
		const string text = "aa bb cc dd ee ff gg hh";

		var root = CreateRoot();

		var wrapped = root.AddChild<Label>();
		wrapped.Text = text;
		wrapped.Style.Set( "width: 100px; font-size: 16px;" );

		var single = root.AddChild<Label>();
		single.Text = text;
		single.Style.Set( "width: 100px; font-size: 16px; white-space: nowrap;" );

		root.Layout();

		Assert.AreEqual( 100f, wrapped.Box.Rect.Width );
		Assert.IsTrue( single.Box.Rect.Height > 0 );
		Assert.IsTrue( wrapped.Box.Rect.Height > single.Box.Rect.Height );
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void FractionalGridColumnsWrapLabels( bool rich )
	{
		var root = CreateRoot();
		var grid = root.AddChild<Panel>();
		grid.Style.Set( "display: grid; width: 240px; grid-template-columns: 1fr 1fr; align-items: start;" );
		var label = grid.AddChild<Label>();
		label.IsRich = rich;
		label.Text = rich ? "small <b>words</b> that should wrap across several lines" : "small words that should wrap across several lines";
		label.Style.Set( "font-size: 16px;" );
		var other = grid.AddChild<Label>();
		other.Text = "short";
		other.Style.Set( "font-size: 16px;" );

		root.Layout();
		Assert.AreEqual( 120f, label.Box.Rect.Width, 1f );
		Assert.AreEqual( 120f, other.Box.Rect.Width, 1f );
		Assert.IsTrue( label.Box.Rect.Height > other.Box.Rect.Height );
		var wrappedHeight = label.Box.Rect.Height;

		root.Layout();
		Assert.AreEqual( wrappedHeight, label.Box.Rect.Height );
		grid.Style.Width = 600;
		root.Layout();
		Assert.AreEqual( 300f, label.Box.Rect.Width, 1f );
		Assert.IsTrue( label.Box.Rect.Height < wrappedHeight );

		label.Text = "short";
		root.Layout();
		Assert.AreEqual( rich, label.IsRich );
		Assert.AreEqual( other._textBlock.Measure( float.NaN, float.NaN ), label._textBlock.Measure( float.NaN, float.NaN ) );
		Assert.AreEqual( other.Box.Rect.Height, label.Box.Rect.Height );
	}

	[DataTestMethod]
	[DataRow( null )]
	[DataRow( "Courier New" )]
	public void RichTextRetainsBaseFontWhenSpanFontIsUnsetOrCleared( string family )
	{
		var root = CreateRoot();
		root.Style.FontFamily = family;
		var rich = root.AddChild<Label>();
		rich.IsRich = true;
		rich.Text = "<span>iiii WWWW</span>";
		rich.Style.FontSize = 16;
		var plain = root.AddChild<Label>();
		plain.Text = "iiii WWWW";
		plain.Style.FontSize = 16;
		root.Layout();
		var expected = plain._textBlock.Measure( float.NaN, float.NaN );
		Assert.AreEqual( expected, rich._textBlock.Measure( float.NaN, float.NaN ) );

		rich.Text = "<span style=\"font-family: Courier New;\">iiii WWWW</span>";
		plain.Style.FontFamily = "Courier New";
		root.Layout();
		Assert.AreEqual( plain._textBlock.Measure( float.NaN, float.NaN ), rich._textBlock.Measure( float.NaN, float.NaN ) );

		rich.Text = "iiii WWWW";
		plain.Style.FontFamily = null;
		root.Layout();
		Assert.IsTrue( rich.IsRich );
		Assert.AreEqual( expected, rich._textBlock.Measure( float.NaN, float.NaN ) );
	}

	[DataTestMethod]
	[DataRow( "small extraordinary words", "extraordinary", "", false )]
	[DataRow( "small extraordinary words", "small extraordinary words", "white-space: nowrap;", false )]
	[DataRow( "small extraordinary words", "small extraordinary words", "white-space: pre;", false )]
	[DataRow( "bb   a", "bb ", "white-space: break-spaces;", false )]
	[DataRow( "   ", " ", "white-space: break-spaces;", false )]
	[DataRow( "small extraordinary words", "EXTRAORDINARY", "text-transform: uppercase;", false )]
	[DataRow( "extra<b>ordinary</b> words", "extra<b>ordinary</b>", "", true )]
	[DataRow( "one\u00a0two three", "one\u00a0two", "white-space: pre-wrap;", false )]
	[DataRow( "extraordinary\nsmall", "extraordinary", "white-space: pre-line;", false )]
	[DataRow( "small extraordinary words", "extraordinary", "letter-spacing: 2px;", false )]
	[DataRow( "WWWWW", "W", "word-break: break-all;", false )]
	[DataRow( "small extraordinary words", "extraordinary", "text-overflow: ellipsis;", false )]
	[DataRow( "a \u05d0\u05d1\u05d2\u05d3\u05d4 words", "words", "", false )]
	public void MinContentUsesUnbreakableShapedText( string text, string longest, string style, bool rich )
	{
		var root = CreateRoot();
		var label = root.AddChild<Label>();
		label.IsRich = rich;
		label.Text = text;
		label.Style.Set( "font-size: 16px; " + style );
		var reference = root.AddChild<Label>();
		reference.IsRich = rich;
		reference.Text = longest;
		reference.Style.Set( "font-size: 16px; " + style );
		root.Layout();

		var normal = label._textBlock.Measure( 80, float.NaN );
		var measured = label._textBlock.MeasuredSize;
		var lines = label._textBlock.LineCount;
		var intrinsic = label._textBlock.MeasureMinContent();
		Assert.AreEqual( reference._textBlock.Measure( float.NaN, float.NaN ).x, intrinsic.x, 1f );
		Assert.AreEqual( intrinsic, label._textBlock.MeasureMinContent() );
		Assert.AreEqual( measured, label._textBlock.MeasuredSize );
		Assert.AreEqual( lines, label._textBlock.LineCount );
		Assert.AreEqual( normal, label._textBlock.Measure( 80, float.NaN ) );

		label.Style.FontSize = 32;
		root.Layout();
		Assert.IsTrue( label._textBlock.MeasureMinContent().x > intrinsic.x );
		label.Style.FontSize = 1;
		root.Layout();
		Assert.IsTrue( label._textBlock.MeasureMinContent().x < intrinsic.x );
	}

	[DataTestMethod]
	[DataRow( "min-content", "words words words", "normal" )]
	[DataRow( "160px", "words words words words words", "normal" )]
	[DataRow( "min-content", "words words\n", "pre" )]
	[DataRow( "min-content", "words words\n", "pre-wrap" )]
	[DataRow( "min-content", "bb   a", "break-spaces" )]
	[DataRow( "min-content", "   ", "break-spaces" )]
	public void MinContentGridRowsHaveCompatibleTextHeight( string columns, string text, string whitespace )
	{
		var root = CreateRoot();
		var grid = root.AddChild<Panel>();
		grid.Style.Set( $"display: grid; grid-template-columns: {columns}; grid-template-rows: min-content;" );
		var label = grid.AddChild<Label>();
		label.Text = text;
		label.Style.Set( $"font-size: 16px; white-space: {whitespace};" );
		root.Layout();
		var intrinsic = label._textBlock.MeasureMinContent();
		var expected = label._textBlock.Measure( label.Box.Rect.Width, float.NaN );
		Assert.IsTrue( label.Box.Rect.Width > 0 );
		Assert.AreEqual( expected.y, label.Box.Rect.Height, 1f );
		if ( columns == "min-content" )
		{
			Assert.AreEqual( intrinsic.x, label.Box.Rect.Width, 1f );
			Assert.AreEqual( intrinsic.y, label.Box.Rect.Height, 1f );
		}
		root.Layout();
		Assert.AreEqual( expected.y, label.Box.Rect.Height, 1f );

		var callback = label.LayoutTree.Node.MeasureFunc;
		var heightOnly = callback( label.LayoutTree.Node, 160, Sandbox.Layout.MeasureMode.Exactly,
			float.NaN, Sandbox.Layout.MeasureMode.MinContent );
		Assert.AreEqual( label._textBlock.Measure( 160, float.NaN ).y, heightOnly.Height, 1f );
	}

	[TestMethod]
	public void DefaultContentDoesNotOverrideLabelText()
	{
		var root = CreateRoot();
		var label = root.AddChild<Label>();
		label.Text = "Visible text";
		root.Layout();
		Assert.IsNull( label.ComputedStyle.Content );
		Assert.AreEqual( label.Text, label._textBlock.Text );

		label.Style.Content = "";
		root.Layout();
		Assert.AreEqual( "", label._textBlock.Text );
	}

	[TestMethod]
	public void FractionalGridPreservesUnbreakableMinimumAndNoWrap()
	{
		var root = CreateRoot();
		var grid = root.AddChild<Panel>();
		grid.Style.Set( "display: grid; width: 20px; grid-template-columns: 1fr;" );
		var label = grid.AddChild<Label>();
		label.Text = "extraordinary words";
		label.Style.Set( "font-size: 16px; min-width: auto;" );
		root.Layout();
		var minimum = label._textBlock.MeasureMinContent().x;
		Assert.AreEqual( minimum, label.Box.Rect.Width, 1f );
		Assert.IsTrue( minimum > 20 );

		label.Multiline = false;
		root.Layout();
		Assert.AreEqual( label._textBlock.Measure( float.NaN, float.NaN ).x, label.Box.Rect.Width, 1f );
		Assert.IsTrue( label.Box.Rect.Width > minimum );
		label.Multiline = true;
		root.Layout();
		Assert.AreEqual( minimum, label.Box.Rect.Width, 1f );
	}

	[TestMethod]
	public void FractionalGridWithZeroMinimumShrinksAndWraps()
	{
		var root = CreateRoot();
		var grid = root.AddChild<Panel>();
		grid.Style.Set( "display: grid; width: 20px; grid-template-columns: 1fr;" );
		var label = grid.AddChild<Label>();
		label.Text = "extraordinary words";
		label.Style.Set( "font-size: 16px; min-width: 0;" );
		root.Layout();
		Assert.AreEqual( 20f, label.Box.Rect.Width, 1f );
		Assert.IsTrue( label._textBlock.MeasureMinContent().x > label.Box.Rect.Width );
		var wrappedHeight = label.Box.Rect.Height;
		Assert.IsTrue( wrappedHeight > label._textBlock.Measure( float.NaN, float.NaN ).y );
		Assert.AreEqual( label._textBlock.Measure( 20, float.NaN ).y, wrappedHeight, 1f );

		label.Multiline = false;
		root.Layout();
		Assert.AreEqual( 20f, label.Box.Rect.Width, 1f );
		Assert.IsTrue( label.Box.Rect.Height < wrappedHeight );
		label.Multiline = true;
		root.Layout();
		Assert.AreEqual( wrappedHeight, label.Box.Rect.Height, 1f );
	}

	/// <summary>
	/// Before the first layout there is no text block, so the selection setters are no-ops and the
	/// selection state stays inert instead of throwing.
	/// </summary>
	[TestMethod]
	public void SelectionIsInertBeforeLayout()
	{
		var label = new Label { Text = "Hello" };

		label.SelectionStart = 3;
		label.SelectionEnd = 4;
		Assert.AreEqual( 0, label.SelectionStart );
		Assert.AreEqual( 0, label.SelectionEnd );

		label.ShouldDrawSelection = true;
		Assert.IsFalse( label.ShouldDrawSelection );

		Assert.IsFalse( label.HasSelection() );
		Assert.AreEqual( "", label.GetSelectedText() );
	}

	/// <summary>
	/// After layout the selection API works headless: selecting a range reports HasSelection,
	/// returns the selected substring and exposes it as the clipboard value.
	/// </summary>
	[TestMethod]
	public void SelectionWorksAfterLayout()
	{
		var root = CreateRoot();

		var label = root.AddChild<Label>();
		label.Text = "Hello World";
		label.Style.Set( "font-size: 16px; white-space: nowrap;" );

		root.Layout();

		Assert.IsTrue( label.Selectable );

		label.ShouldDrawSelection = true;
		Assert.IsTrue( label.ShouldDrawSelection );

		label.SetSelection( 6, 11 );
		Assert.AreEqual( 6, label.SelectionStart );
		Assert.AreEqual( 11, label.SelectionEnd );
		Assert.IsTrue( label.HasSelection() );
		Assert.AreEqual( "World", label.GetSelectedText() );
		Assert.AreEqual( "World", label.GetClipboardValue( false ) );
	}

	/// <summary>
	/// SetSelection clamps its arguments to the text length, so selecting past the end of the
	/// text selects up to the last character.
	/// </summary>
	[TestMethod]
	public void SelectionEndClampsToTextLength()
	{
		var root = CreateRoot();

		var label = root.AddChild<Label>();
		label.Text = "Hello World";
		label.Style.Set( "font-size: 16px; white-space: nowrap;" );

		root.Layout();

		label.ShouldDrawSelection = true;
		label.SetSelection( 6, 99 );

		Assert.AreEqual( 11, label.SelectionEnd );
		Assert.AreEqual( "World", label.GetSelectedText() );
	}

	/// <summary>
	/// Shrinking the text clamps the caret position back inside the new text bounds.
	/// </summary>
	[TestMethod]
	public void CaretClampsWhenTextShrinks()
	{
		var label = new Label { Text = "Hello World" };
		label.CaretPosition = 11;

		label.Text = "Hi";

		Assert.AreEqual( 2, label.CaretPosition );
	}

	/// <summary>
	/// The editing helpers InsertText and RemoveText rewrite Text by text-element positions
	/// without needing any layout.
	/// </summary>
	[TestMethod]
	public void InsertAndRemoveTextEditsContent()
	{
		var label = new Label { Text = "Hello" };

		label.InsertText( " World", 5 );
		Assert.AreEqual( "Hello World", label.Text );

		label.RemoveText( 0, 6 );
		Assert.AreEqual( "World", label.Text );
		Assert.AreEqual( 5, label.TextLength );
	}

	/// <summary>
	/// Text that is only a newline collapses to nothing under the default white-space, leaving the
	/// paragraph with no lines. Measuring used to index the last line for the trailing newline's
	/// height and throw. Label's measure callback swallows the exception, so measure the block directly.
	/// </summary>
	[TestMethod]
	public void NewlineOnlyTextMeasuresWithoutThrowing()
	{
		var root = CreateRoot();

		var label = root.AddChild<Label>();
		label.Text = "\n";
		label.Style.Set( "font-size: 16px;" );

		root.Layout();

		var size = label._textBlock.Measure( 1000f, float.NaN );

		Assert.AreEqual( 0f, size.y );
		Assert.AreEqual( Vector2.Zero, label._textBlock.MeasureMinContent() );
	}

	/// <summary>
	/// A trailing newline that white-space collapsing strips never reaches the paragraph, so it must
	/// not add a line to the measured height either.
	/// </summary>
	[TestMethod]
	public void CollapsedTrailingNewlineAddsNoHeight()
	{
		var root = CreateRoot();

		var plain = root.AddChild<Label>();
		plain.Text = "Hello";
		plain.Style.Set( "font-size: 16px;" );

		var trailing = root.AddChild<Label>();
		trailing.Text = "Hello\n";
		trailing.Style.Set( "font-size: 16px;" );

		root.Layout();

		Assert.IsTrue( plain.Box.Rect.Height > 0 );
		Assert.AreEqual( plain.Box.Rect.Height, trailing.Box.Rect.Height );
	}

	/// <summary>
	/// When white-space preserves the trailing newline, the empty line after it counts towards the
	/// measured height so the caret has somewhere to sit.
	/// </summary>
	[TestMethod]
	public void PreservedTrailingNewlineAddsALine()
	{
		var root = CreateRoot();

		var plain = root.AddChild<Label>();
		plain.Text = "Hello";
		plain.Style.Set( "font-size: 16px; white-space: pre;" );

		var trailing = root.AddChild<Label>();
		trailing.Text = "Hello\n";
		trailing.Style.Set( "font-size: 16px; white-space: pre;" );

		root.Layout();

		Assert.IsTrue( plain.Box.Rect.Height > 0 );
		Assert.IsTrue( trailing.Box.Rect.Height > plain.Box.Rect.Height );
	}
}
