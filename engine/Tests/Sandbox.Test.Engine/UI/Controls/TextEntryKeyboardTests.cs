using Sandbox.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class TextEntryKeyboardTests
{
	bool previousRenderText;
	[TestInitialize] public void Initialize() { previousRenderText = TextBlock.ui_rendertext; TextBlock.ui_rendertext = false; }
	[TestCleanup] public void Cleanup() { TextBlock.ui_rendertext = previousRenderText; }

	class Entry : TextEntry
	{
		public Label Content => Label;
		public readonly InputEventQueue Input = new();
		public override void CreateEvent( PanelEvent e ) => OnEvent( e );
		public void Press( NativeEngine.ButtonCode button, KeyboardModifiers modifiers = default )
		{
			var name = InputEventQueue.NormalizeButtonName( button.ToString() );
			Input.AddButtonEvent( name, true, 0, modifiers );
			// Native InputSystem is not initialized in the headless runner. Enter at the
			// string-key boundary, and supply the CutEvent emitted by clipboard routing.
			if ( button == NativeEngine.ButtonCode.KEY_X && modifiers == KeyboardModifiers.Ctrl )
				Input.QueueInputEvent( new CutEvent() );
			else
				Input.AddButtonTyped( name, 0, modifiers );
			Input.AddButtonEvent( name, false, 0, modifiers );
			Input.TickFocused( this );
			Tick();
		}
		public void Type( char c )
		{
			Input.AddKeyTyped( c );
			Input.TickFocused( this );
			Tick();
		}
	}
	static ButtonEvent Key( string name, KeyboardModifiers modifiers = default ) => new( name, true, 0, modifiers );
	static Entry Create( string text = "", bool multiline = false )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		var entry = root.AddChild<Entry>();
		entry.Style.Set( "font-size: 16px; width: 400px; height: 200px;" );
		entry.Multiline = multiline;
		entry.Text = text;
		root.Layout();
		entry.Content.ShouldDrawSelection = true;
		return entry;
	}

	[TestMethod]
	public void SelectAllReplacementIsSeparateUndoStep()
	{
		var entry = Create();
		foreach ( var c in "abc" ) entry.Type( c );
		entry.Press( NativeEngine.ButtonCode.KEY_A, KeyboardModifiers.Ctrl );
		entry.Type( 'x' );
		Assert.AreEqual( "x", entry.Text );
		entry.Press( NativeEngine.ButtonCode.KEY_Z, KeyboardModifiers.Ctrl );
		Assert.AreEqual( "abc", entry.Text, "Undo should undo replacement before undoing the original typing." );
	}

	[TestMethod]
	public void RejectedTypingPreservesRedo()
	{
		var entry = Create( "ab" );
		entry.MaxLength = 2;
		entry.CaretPosition = 2;
		entry.Press( NativeEngine.ButtonCode.KEY_BACKSPACE );
		entry.Press( NativeEngine.ButtonCode.KEY_Z, KeyboardModifiers.Ctrl );
		Assert.IsTrue( entry.CanRedo );
		entry.Type( 'c' );
		Assert.AreEqual( "ab", entry.Text );
		Assert.IsTrue( entry.CanRedo, "Rejected input must not destroy redo history." );
	}

	[TestMethod]
	public void RejectedTypingDoesNotCreateUndoStep()
	{
		var entry = Create( "a" );
		entry.MaxLength = 1;
		entry.CaretPosition = 1;
		entry.OnKeyTyped( 'b' );
		Assert.IsFalse( entry.CanUndo, "Text has not changed." );
	}

	[TestMethod]
	[DataRow( "ab\ncd", 4, "home", 3 )]
	[DataRow( "ab\r\ncd", 4, "home", 3 )]
	[DataRow( "ab\r\ncd\r\nef", 7, "home", 6 )]
	[DataRow( "😀a\nbc", 4, "home", 3 )]
	[DataRow( "e\u0301a\nbc", 4, "home", 3 )]
	[DataRow( "😀a\nbc", 0, "end", 2 )]
	[DataRow( "e\u0301a\nbc", 0, "end", 2 )]
	[DataRow( "ab\r\ncd\r\nef", 3, "end", 5 )]
	public void HomeEndUseTextElementPositions( string text, int start, string key, int expected )
	{
		var entry = Create( text, multiline: true );
		Assert.IsTrue( entry.Multiline );
		Assert.IsTrue( entry.Content.Multiline );
		entry.CaretPosition = start;
		entry.Press( key == "home" ? NativeEngine.ButtonCode.KEY_HOME : NativeEngine.ButtonCode.KEY_END );
		Assert.AreEqual( expected, entry.CaretPosition );
	}

	[TestMethod]
	public void UndoPreservesBackwardSelectionAnchor()
	{
		var entry = Create( "abcdef" );
		entry.CaretPosition = 5;
		for ( int i = 0; i < 3; i++ ) entry.Press( NativeEngine.ButtonCode.KEY_LEFT, KeyboardModifiers.Shift );
		Assert.AreEqual( "cde", entry.Content.GetSelectedText() );
		entry.Type( 'x' );
		entry.Press( NativeEngine.ButtonCode.KEY_Z, KeyboardModifiers.Ctrl );
		Assert.AreEqual( "abcdef", entry.Text );
		entry.Press( NativeEngine.ButtonCode.KEY_LEFT, KeyboardModifiers.Shift );
		Assert.AreEqual( "bcde", entry.Content.GetSelectedText(), "Shift+Left should grow restored backward selection from its original anchor." );
	}

	[TestMethod]
	[DataRow( "left", 2 )]
	[DataRow( "right", 5 )]
	public void PlainArrowsCollapseSelection( string key, int expected )
	{
		var entry = Create( "abcdef" );
		entry.Content.SetCaretPosition( 5 );
		entry.Content.SetCaretPosition( 2, select: true );
		entry.OnButtonTyped( Key( key ) );
		Assert.AreEqual( expected, entry.CaretPosition );
		Assert.IsFalse( entry.Content.HasSelection() );
	}

	[TestMethod]
	[DataRow( "delete", KeyboardModifiers.None )]
	[DataRow( "backspace", KeyboardModifiers.None )]
	[DataRow( "delete", KeyboardModifiers.Ctrl )]
	[DataRow( "backspace", KeyboardModifiers.Ctrl )]
	public void ReadOnlyDeleteDoesNotChangeText( string key, KeyboardModifiers modifiers )
	{
		var entry = Create( "abc def" );
		entry.CaretPosition = 3;
		entry.ReadOnly = true;
		entry.OnButtonTyped( Key( key, modifiers ) );
		Assert.AreEqual( "abc def", entry.Text );
		Assert.IsFalse( entry.CanUndo );
	}

	[TestMethod]
	public void CutWithoutSelectionKeepsCaret()
	{
		var entry = Create( "abcdef" );
		entry.CaretPosition = 4;
		entry.Press( NativeEngine.ButtonCode.KEY_X, KeyboardModifiers.Ctrl );
		Assert.AreEqual( "abcdef", entry.Text );
		Assert.AreEqual( 4, entry.CaretPosition, "Cut with no selection should leave the caret alone." );
		Assert.IsFalse( entry.CanUndo );
	}

}
