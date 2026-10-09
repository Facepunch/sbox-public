using System.Globalization;

namespace Sandbox.UI;

public partial class TextEntry
{
	SelectionDrag localSelectionDrag;
	bool suppressDragSelection;
	internal int DropCaretPosition { get; private set; } = -1;

	// Keep moves inside this entry on the normal mouse path. The game view receives OS
	// drops asynchronously, after the source drag has already finished.
	void UpdateTextDrag()
	{
		if ( !Box.Rect.IsInside( ScreenMousePosition ) || !CanEdit )
		{
			CancelTextDrag();
			DragSelectionOut();
			return;
		}

		localSelectionDrag ??= new SelectionDrag( this, publish: false );
		suppressDragSelection = true;
		DropCaretPosition = Label.GetLetterAtScreenPosition( ScreenMousePosition );
	}

	void CancelTextDrag()
	{
		localSelectionDrag?.Dispose();
		localSelectionDrag = null;
		DropCaretPosition = -1;
	}

	bool FinishTextDrag( bool copy = false )
	{
		if ( localSelectionDrag is null ) return false;
		try
		{
			var position = Label.GetLetterAtScreenPosition( ScreenMousePosition );
			if ( Box.Rect.IsInside( ScreenMousePosition ) && position >= 0 )
			{
				if ( copy ) localSelectionDrag.CopyWithin( position );
				else localSelectionDrag.MoveWithin( position );
			}
		}
		finally
		{
			_pressedOnSelection = false;
			CancelTextDrag();
		}
		return true;
	}

	protected override void OnDragLeave( PanelEvent e )
	{
		if ( e is DropEvent ) DropCaretPosition = -1;
		base.OnDragLeave( e );
	}

	void DragSelectionOut()
	{
		suppressDragSelection = true;
		_pressedOnSelection = false;
		if ( string.IsNullOrEmpty( Label.GetSelectedText() ) ) return;

		using var selection = new SelectionDrag( this );
		var drag = new Drag( this );
		drag.SetText( selection.Text );
		selection.Complete( drag.Start() );
	}

	protected override void OnDrop( PanelEvent e )
	{
		if ( e is not DropEvent drop || string.IsNullOrEmpty( drop.Text ) || !CanEdit ) return;

		var letter = Label.GetLetterAtScreenPosition( drop.Position );
		if ( letter < 0 ) return;

		var selection = SelectionDrag.Current;
		if ( selection?.Text != drop.Text ) selection = null;
		drop.Action = selection?.CanMove == true ? DropAction.Move : DropAction.Copy;
		drop.StopPropagation();
		DropCaretPosition = drop.IsDrop ? -1 : letter;
		if ( !drop.IsDrop ) return;

		Focus();
		if ( drop.Action == DropAction.Move && selection.Source == this )
		{
			selection.MoveWithin( letter );
			return;
		}

		// Keep external drops and filtered/truncated pastes as copies: the sender must not
		// lose text that the receiver didn't insert. A normal entry-to-entry drop is a move.
		var before = Text ?? "";
		var elements = StringInfo.ParseCombiningCharacters( before );
		var offset = letter < elements.Length ? elements[letter] : before.Length;
		Label.SetCaretPosition( letter );
		OnPaste( drop.Text );
		if ( Text != before.Insert( offset, drop.Text ) ) drop.Action = DropAction.Copy;
	}

	/// <summary>
	/// An OS drag pumps nested UI frames. Retain the original source and range, rather than
	/// deleting whichever selection happens to be active when the blocking drag returns.
	/// </summary>
	internal sealed class SelectionDrag : IDisposable
	{
		[ThreadStatic] internal static SelectionDrag Current;
		readonly SelectionDrag previous;
		readonly bool published;
		readonly string original;
		readonly int start;
		readonly int length;
		bool handled;

		internal TextEntry Source { get; }
		internal string Text { get; }
		internal bool CanMove => !handled && Source.IsValid() && Source.CanEdit && Source.Text == original;

		internal SelectionDrag( TextEntry source, bool publish = true )
		{
			Source = source;
			original = source.Text;
			start = Math.Min( source.Label.SelectionStart, source.Label.SelectionEnd );
			length = Math.Abs( source.Label.SelectionEnd - source.Label.SelectionStart );
			Text = source.Label.GetSelectedText();
			published = publish;
			previous = Current;
			if ( publish ) Current = this;
		}

		internal void CopyWithin( int destination )
		{
			if ( !CanMove ) return;
			handled = true;
			Source.PasteText( Text, destination, destination, select: true );
		}

		internal void MoveWithin( int destination )
		{
			if ( !CanMove ) return;
			handled = true;
			// Dropping on either boundary or inside the original selection changes nothing.
			if ( destination >= start && destination <= start + length ) return;

			Source.RecordEdit( EditKind.Single );
			Source.Label.RemoveText( start, length );
			if ( destination > start ) destination -= length;
			Source.Label.SetCaretPosition( destination );
			Source.Label.InsertText( Text, destination );
			Source.Label.SetCaretPosition( destination + length );
			Source.Label.SetSelection( destination, destination + length );
			Source.OnValueChanged();
		}

		internal void Complete( DropAction action )
		{
			if ( action != DropAction.Move || !CanMove ) return;
			handled = true;
			Source.RecordEdit( EditKind.Single );
			Source.Label.RemoveText( start, length );
			Source.Label.SetCaretPosition( start );
			Source.OnValueChanged();
		}

		public void Dispose()
		{
			if ( published ) Current = previous;
		}
	}
}
