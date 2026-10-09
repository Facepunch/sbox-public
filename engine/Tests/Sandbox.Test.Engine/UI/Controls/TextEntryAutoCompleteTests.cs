using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class TextEntryAutoCompleteTests
{
	bool previousRenderText;
	RootPanel root;

	[TestInitialize]
	public void Initialize()
	{
		previousRenderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
	}

	[TestCleanup]
	public void Cleanup()
	{
		root?.Delete( true );
		TextBlock.ui_rendertext = previousRenderText;
	}

	class Entry : TextEntry
	{
		public Label Content => Label;
	}

	Entry Create( string text = "h" )
	{
		root = new RootPanel( new Sandbox.UISystem() ) { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		var entry = root.AddChild<Entry>();
		entry.Style.Set( "font-size: 16px; width: 300px;" );
		entry.Text = text;
		root.Layout();
		entry.CaretPosition = entry.TextLength;
		entry.Content.ShouldDrawSelection = true;
		return entry;
	}

	void Frame()
	{
		root.UISystem.TickFocus();
		root.UISystem.TickPanels();
		root.Layout();
	}

	void OpenCompletion( Entry entry )
	{
		entry.AutoComplete = _ => new object[] { "hello" };
		entry.Focus();
		Frame();
		Assert.AreSame( entry, root.UISystem.CurrentFocus );
		Assert.IsNotNull( entry.AutoCompletePanel );
	}

	void Key( string key )
	{
		root.UISystem.InputEventQueue.AddButtonTyped( key, 0, default );
		root.UISystem.InputEventQueue.TickFocused( root.UISystem.CurrentFocus );
		Frame();
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void AcceptingCompletionNotifiesOnceAndCanBeUndone( bool mouse )
	{
		var entry = Create( "hi" );
		entry.Content.SetSelection( 0, 1 );
		entry.CaretPosition = 1;
		var notifications = 0;
		string notified = null;
		entry.OnTextEdited = text => { notifications++; notified = text; };
		OpenCompletion( entry );

		if ( mouse )
		{
			var option = entry.AutoCompletePanel.GetChild( 0 );
			option.DispatchEventImmediate( new MousePanelEvent( "onclick", option, "mouseleft" ) );
			Frame();
		}
		else
		{
			Key( "down" );
			Assert.AreEqual( 0, notifications );
			Assert.IsFalse( entry.CanUndo );
			Key( "enter" );
		}

		Assert.AreEqual( "hello", entry.Text );
		Assert.AreEqual( "hello", notified );
		Assert.AreEqual( 1, notifications );
		Assert.IsNull( entry.AutoCompletePanel );
		entry.Undo();
		Assert.AreEqual( "hi", entry.Text );
		Assert.AreEqual( 1, entry.CaretPosition );
		Assert.AreEqual( "h", entry.Content.GetSelectedText() );
		Assert.IsFalse( entry.CanUndo );
		entry.Redo();
		Assert.AreEqual( "hello", entry.Text );
	}

	[TestMethod]
	public void EscapeRestoresTextCaretAndSelectionWithoutAnEdit()
	{
		var entry = Create( "hi" );
		entry.Content.SetSelection( 0, 1 );
		entry.CaretPosition = 1;
		var notifications = 0;
		entry.OnTextEdited = _ => notifications++;
		OpenCompletion( entry );
		Key( "down" );
		Assert.AreEqual( "hello", entry.Text );
		Key( "escape" );
		Assert.AreEqual( "hi", entry.Text );
		Assert.AreEqual( 1, entry.CaretPosition );
		Assert.AreEqual( "h", entry.Content.GetSelectedText() );
		Assert.AreEqual( 0, notifications );
		Assert.IsFalse( entry.CanUndo );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NonEditableEntriesDoNotOpenCompletion( bool disabled )
	{
		var entry = Create();
		entry.ReadOnly = !disabled;
		entry.Disabled = disabled;
		entry.AutoComplete = _ => new object[] { "hello" };
		entry.UpdateAutoComplete();
		Assert.IsNull( entry.AutoCompletePanel );
		entry.UpdateAutoComplete( new object[] { "hello" } );
		Assert.IsNull( entry.AutoCompletePanel );
		Assert.AreEqual( "h", entry.Text );
	}

	[TestMethod]
	public void ReadOnlyCannotCommitAnAlreadyOpenPreview()
	{
		var entry = Create();
		OpenCompletion( entry );
		Key( "down" );
		entry.ReadOnly = true;
		Key( "enter" );
		Assert.AreEqual( "h", entry.Text );
		Assert.IsFalse( entry.CanUndo );
		Assert.IsNull( entry.AutoCompletePanel );
	}

	[TestMethod]
	public void MultilineEnterAcceptsCompletionWithoutAddingANewline()
	{
		var entry = Create();
		entry.Multiline = true;
		OpenCompletion( entry );
		Key( "down" );
		Key( "enter" );
		Assert.AreEqual( "hello", entry.Text );
		Assert.IsTrue( entry.CanUndo );
	}
}
