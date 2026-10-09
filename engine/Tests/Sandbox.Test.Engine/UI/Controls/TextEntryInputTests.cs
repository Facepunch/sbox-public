using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class TextEntryInputTests
{
	bool previousRenderText;
	[TestInitialize] public void Initialize() { previousRenderText = TextBlock.ui_rendertext; TextBlock.ui_rendertext = false; }
	[TestCleanup] public void Cleanup() { TextBlock.ui_rendertext = previousRenderText; }

	class Entry : TextEntry
	{
		public Label ContentLabel => Label;
		public void Raise( string name, object value = null ) => OnEvent( new PanelEvent( name ) { Value = value } );
	}

	static Entry Create( string text = "" )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		var entry = root.AddChild<Entry>();
		entry.Style.Set( "font-size: 16px; width: 300px;" );
		entry.Text = text;
		entry.CaretPosition = entry.TextLength;
		root.Layout();
		entry.ContentLabel.ShouldDrawSelection = true;
		return entry;
	}

	[TestMethod]
	public void ImeReplacementCanBeUndoneThroughQueuedInput()
	{
		var entry = Create( "hello" );
		entry.ContentLabel.SetSelection( 0, 5 );
		var composing = ImeComposition.Update( entry, false, "かな" );
		entry.RunPendingEvents();
		// TickPanels handles queued IME events before TickInput delivers keys.
		ImeComposition.Update( entry, composing, "" );
		var input = new InputEventQueue();
		input.AddKeyTyped( 'か' );
		input.AddKeyTyped( 'な' );
		entry.RunPendingEvents();
		input.TickFocused( entry );
		Assert.AreEqual( "かな", entry.Text );
		entry.Undo();
		Assert.AreEqual( "hello", entry.Text );
	}

	[TestMethod]
	public void CancelledImeSelectionDeletionCanBeUndoneThroughQueuedInput()
	{
		var entry = Create( "hello" );
		entry.ContentLabel.SetSelection( 0, 5 );
		var composing = ImeComposition.Update( entry, false, "か" );
		entry.RunPendingEvents();
		ImeComposition.Update( entry, composing, "" );
		entry.RunPendingEvents();
		// Cancel need not restore automatically, but Undo must recover the deletion.
		entry.Undo();
		Assert.AreEqual( "hello", entry.Text );
	}

	[TestMethod]
	public void NumericCanReplaceSelectedDecimalSeparator()
	{
		var entry = Create( "1.5" );
		entry.Numeric = true;
		entry.ContentLabel.SetSelection( 1, 2 );
		entry.OnKeyTyped( ',' );
		Assert.AreEqual( "1,5", entry.Text );
	}

	[TestMethod]
	public void NumericPasteAllowsOnlyOneDecimalSeparator()
	{
		var entry = Create();
		entry.Numeric = true;
		entry.OnPaste( "1.2.3" );
		Assert.AreEqual( "1.23", entry.Text );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void CombiningMarkKeepsCaretAfterCombinedCharacter( bool paste )
	{
		var entry = Create( "ab" );
		entry.CaretPosition = 1;
		if ( paste ) entry.OnPaste( "\u0301" ); else entry.OnKeyTyped( '\u0301' );
		Assert.AreEqual( "a\u0301b", entry.Text );
		var caretAfterAccent = entry.CaretPosition;
		entry.OnKeyTyped( 'x' );
		Assert.AreEqual( "a\u0301xb", entry.Text );
		Assert.AreEqual( 1, caretAfterAccent );
	}

	[TestMethod]
	public void EmojiCharacterRegexAllowsPastingAcceptedTypedCharacter()
	{
		var entry = Create();
		entry.CharacterRegex = "^(?:\U0001F44D)$";
		entry.OnPaste( "\U0001F44D" );
		Assert.AreEqual( "\U0001F44D", entry.Text );
	}

	[TestMethod]
	public void EmojiCharacterRegexDoesNotRejectAcceptedTypedCharacterDuringValidation()
	{
		var entry = Create();
		entry.CharacterRegex = "^(?:\U0001F44D)$";
		foreach ( var ch in "\U0001F44D" ) entry.OnKeyTyped( ch );
		Assert.AreEqual( "\U0001F44D", entry.Text );
		Assert.IsFalse( entry.HasValidationErrors );
	}
}
