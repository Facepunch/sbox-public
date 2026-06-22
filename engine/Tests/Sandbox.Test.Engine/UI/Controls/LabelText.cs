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
}
