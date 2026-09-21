using NativeEngine;
using Sandbox.UI;
using System;
using System.Collections.Generic;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class TextEntryMouseTests
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

	class Entry : TextEntry
	{
		public Label Content => Label;
		readonly Queue<PanelEvent> events = new();

		public override void CreateEvent( PanelEvent e ) => events.Enqueue( e );
		public void FlushEvents()
		{
			while ( events.TryDequeue( out var e ) ) OnEvent( e );
		}
	}

	class Pointer : PanelInput
	{
		public Vector2 Position;
		public Vector2 Delta;
		internal override Vector2 CursorPosition => Position;
		internal override Vector2 CursorDelta => Delta;
		public override void SetCursor( string name ) { }
	}

	// Drive the same button, selection and movement queues as UISystem, including the
	// final selection event being queued after mouse-up. Direct handler calls miss this.
	class MouseSequence
	{
		public Entry Entry { get; }
		public Pointer Input { get; } = new();
		readonly InputEventQueue queue = new();
		readonly RootPanel root;
		readonly ButtonCode button;
		public KeyboardModifiers Modifiers;
		bool down;

		public MouseSequence( ButtonCode button = ButtonCode.MouseLeft, KeyboardModifiers modifiers = default )
		{
			this.button = button;
			Modifiers = modifiers;
			root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
			root.Style.Set( "flex-direction: row; align-items: flex-start;" );
			Entry = root.AddChild<Entry>();
			Entry.Style.Set( "font-size: 16px; width: 300px; pointer-events: all;" );
			Entry.Content.Style.PointerEvents = PointerEvents.None;
			Entry.Text = "alpha beta gamma";
			root.Layout();
			Entry.Content.ShouldDrawSelection = true;
		}

		public void Frame( int letter, bool pressed, int clickCount = 1, bool keepPointer = false )
		{
			var previous = Input.Position;
			if ( !keepPointer )
			{
				var caret = Entry.Content.GetCaretRect( letter );
				Input.Position = caret.Position + caret.Size * 0.5f;
				Input.Position.x = MathF.Max( Input.Position.x, Entry.Box.Rect.Left );
			}
			Input.Delta = Input.Position - previous;
			if ( pressed != down ) Input.AddMouseButton( button, pressed, Modifiers, clickCount );
			down = pressed;
			Assert.IsTrue( Input.UpdateMouse( root, new InputData
			{
				MousePos = Input.Position,
				Mouse0 = pressed && button == ButtonCode.MouseLeft,
				Mouse1 = pressed && button == ButtonCode.MouseMiddle,
				Mouse2 = pressed && button == ButtonCode.MouseRight
			} ) );
			Assert.AreSame( Entry, Input.Hovered );
			queue.MouseMoved( Input.Delta );
			queue.Tick( Input.Hovered, Input.Active );
			Entry.FlushEvents();
		}
	}

	[TestMethod]
	[DataRow( true, 7 )]
	[DataRow( false, 13 )]
	public void OtherMouseButtonsPreserveSelection( bool right, int letter )
	{
		var mouse = new MouseSequence( right ? ButtonCode.MouseRight : ButtonCode.MouseMiddle );
		mouse.Entry.Content.SetSelection( 6, 10 );
		mouse.Frame( letter, true );
		mouse.Frame( letter, false, keepPointer: true );
		Assert.AreEqual( "beta", mouse.Entry.Content.GetSelectedText() );
	}

	[TestMethod]
	public void RightDraggingDoesNotStartTextMove()
	{
		var mouse = new MouseSequence( ButtonCode.MouseRight );
		mouse.Entry.Content.SetSelection( 6, 10 );
		mouse.Frame( 7, true );
		mouse.Frame( 16, true );
		mouse.Frame( 16, false );
		Assert.AreEqual( -1, mouse.Entry.DropCaretPosition );
		Assert.AreEqual( "beta", mouse.Entry.Content.GetSelectedText() );
		Assert.AreEqual( "alpha beta gamma", mouse.Entry.Text );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ControlAtDropCopiesText( bool heldAtStart )
	{
		var mouse = new MouseSequence( modifiers: heldAtStart ? KeyboardModifiers.Ctrl : default );
		mouse.Entry.Content.SetSelection( 6, 10 );
		mouse.Frame( 7, true );
		mouse.Frame( 16, true );
		mouse.Modifiers = KeyboardModifiers.Ctrl;
		mouse.Frame( 16, false );
		Assert.AreEqual( "alpha beta gammabeta", mouse.Entry.Text );
		Assert.AreEqual( "beta", mouse.Entry.Content.GetSelectedText() );
		mouse.Entry.Undo();
		Assert.AreEqual( "alpha beta gamma", mouse.Entry.Text );
		Assert.AreEqual( "beta", mouse.Entry.Content.GetSelectedText() );
		Assert.AreEqual( 6, mouse.Entry.Content.SelectionStart );
		Assert.IsFalse( mouse.Entry.CanUndo );
	}

	[TestMethod]
	public void ReleasingControlBeforeDropMovesText()
	{
		var mouse = new MouseSequence( modifiers: KeyboardModifiers.Ctrl );
		mouse.Entry.Content.SetSelection( 6, 10 );
		mouse.Frame( 7, true );
		mouse.Frame( 16, true );
		mouse.Modifiers = default;
		mouse.Frame( 16, false );
		Assert.AreEqual( "alpha  gammabeta", mouse.Entry.Text );
	}

	[TestMethod]
	public void ControlDragHonorsMaximumLength()
	{
		var mouse = new MouseSequence( modifiers: KeyboardModifiers.Ctrl );
		mouse.Entry.MaxLength = 18;
		mouse.Entry.Content.SetSelection( 6, 10 );
		mouse.Frame( 7, true );
		mouse.Frame( 16, true );
		mouse.Frame( 16, false );
		Assert.AreEqual( "alpha beta gammabe", mouse.Entry.Text );
		Assert.AreEqual( "be", mouse.Entry.Content.GetSelectedText() );
	}

	[TestMethod]
	public void ControlDragHonorsCharacterFilter()
	{
		var mouse = new MouseSequence( modifiers: KeyboardModifiers.Ctrl );
		mouse.Entry.CharacterRegex = "[a]";
		mouse.Entry.Content.SetSelection( 6, 10 );
		mouse.Frame( 7, true );
		mouse.Frame( 16, true );
		mouse.Frame( 16, false );
		Assert.AreEqual( "alpha beta gammaa", mouse.Entry.Text );
		Assert.AreEqual( "a", mouse.Entry.Content.GetSelectedText() );
	}

	[TestMethod]
	public void ShiftDraggingKeepsOriginalAnchor()
	{
		var mouse = new MouseSequence( modifiers: KeyboardModifiers.Shift );
		mouse.Entry.Content.SetCaretPosition( 0 );
		mouse.Frame( 7, true );
		mouse.Frame( 14, true );
		mouse.Frame( 14, false );
		Assert.AreEqual( 0, mouse.Entry.Content.SelectionStart );
		Assert.AreEqual( 14, mouse.Entry.Content.SelectionEnd );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void WordDragKeepsWholeWordsOnRelease( bool stationary )
	{
		var mouse = new MouseSequence();
		mouse.Frame( 7, true, 2 );
		mouse.Frame( 13, true );
		Assert.AreEqual( "beta gamma", mouse.Entry.Content.GetSelectedText() );
		mouse.Frame( 14, false, keepPointer: stationary );
		if ( stationary ) Assert.AreEqual( Vector2.Zero, mouse.Input.Delta );
		Assert.AreEqual( "beta gamma", mouse.Entry.Content.GetSelectedText() );
	}
}
