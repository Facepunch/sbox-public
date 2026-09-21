using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class LabelEditingRegressionTests
{
	bool previousRenderText;
	[TestInitialize]
	public void DisableTextTextures()
	{
		previousRenderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
	}

	[TestCleanup]
	public void RestoreTextTextures() => TextBlock.ui_rendertext = previousRenderText;

	static Label CreateLabel( string text )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		var label = root.AddChild<Label>();
		label.Text = text;
		label.Multiline = true;
		label.Style.Set( "font-size: 16px; width: 400px;" );
		root.Layout();
		label.ShouldDrawSelection = true;
		return label;
	}

	[TestMethod]
	[DataRow( "ab\ncd", 4, true, 3 )]
	[DataRow( "ab\r\ncd", 4, true, 3 )]
	[DataRow( "ab\r\ncd\r\nef", 7, true, 6 )]
	[DataRow( "😀a\nbc", 4, true, 3 )]
	[DataRow( "e\u0301a\nbc", 4, true, 3 )]
	[DataRow( "😀a\nbc", 0, false, 2 )]
	[DataRow( "e\u0301a\nbc", 0, false, 2 )]
	[DataRow( "ab\r\ncd\r\nef", 3, false, 5 )]
	public void LineBoundariesUseTextElements( string text, int position, bool home, int expected )
	{
		var label = CreateLabel( text );
		label.CaretPosition = position;
		if ( home ) label.MoveToLineStart(); else label.MoveToLineEnd();
		Assert.AreEqual( expected, label.CaretPosition );
	}

	[TestMethod]
	public void BackwardSelectionKeepsItsAnchor()
	{
		var label = CreateLabel( "abcdef" );
		label.SetSelection( 5, 2 );
		label.CaretPosition = 2;
		label.MoveCaretPos( -1, select: true );
		Assert.AreEqual( 5, label.SelectionStart );
		Assert.AreEqual( 1, label.SelectionEnd );
		Assert.AreEqual( "bcde", label.GetSelectedText() );
	}

	[TestMethod]
	[DataRow( "ab", "x", 1, "axb", 2 )]
	[DataRow( "ab", "\u0301", 1, "a\u0301b", 1 )]
	[DataRow( "👍b", "🏽", 1, "👍🏽b", 1 )]
	[DataRow( "👩👧b", "\u200d", 1, "👩\u200d👧b", 1 )]
	[DataRow( "ab", "😀", 1, "a😀b", 2 )]
	[DataRow( "", "😀", 0, "😀", 1 )]
	[DataRow( "abc", "", 2, "abc", 2 )]
	public void InsertionPlacesCaretAfterResultingGrapheme( string original, string inserted, int position, string expected, int caret )
	{
		var label = CreateLabel( original );
		label.InsertTextAndMoveCaret( inserted, position );
		Assert.AreEqual( expected, label.Text );
		Assert.AreEqual( caret, label.CaretPosition );
	}

	[TestMethod]
	[DataRow( "abX", "\u0301", 1, 2, "a\u0301X", 1 )]
	[DataRow( "abX", "😀", 1, 2, "a😀X", 2 )]
	[DataRow( "abX", "", 2, 1, "aX", 1 )]
	public void ReplacementPlacesCaretAfterResultingGrapheme( string original, string inserted, int start, int end, string expected, int caret )
	{
		var label = CreateLabel( original );
		label.SetSelection( start, end );
		label.ReplaceSelection( inserted );
		Assert.AreEqual( expected, label.Text );
		Assert.AreEqual( caret, label.CaretPosition );
		Assert.IsFalse( label.HasSelection() );
	}
}
