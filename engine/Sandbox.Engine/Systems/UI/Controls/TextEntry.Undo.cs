namespace Sandbox.UI;

public partial class TextEntry
{
	/// <summary>
	/// The text and where the caret and selection were - everything undo has to put back.
	/// </summary>
	readonly record struct TextState( string Text, int Caret, int SelectionStart, int SelectionEnd );

	/// <summary>
	/// What kind of edit is going on. A run of the same kind folds into one undo step, so a
	/// word typed in one go comes back out in one go rather than a letter at a time.
	/// </summary>
	enum EditKind
	{
		/// <summary>Doesn't join onto anything - pastes, cuts, drops, whitespace.</summary>
		Single,
		Typing,
		Deleting
	}

	/// <summary>
	/// How long a run of typing can pause before the next character starts a new undo step.
	/// </summary>
	const float EditRunTimeout = 1.0f;

	/// <summary>
	/// How many steps to keep. Old ones fall off the bottom.
	/// </summary>
	const int MaxUndoSteps = 200;

	readonly List<TextState> _undoStack = new();
	readonly List<TextState> _redoStack = new();

	EditKind _runKind = EditKind.Single;
	RealTimeSince _timeSinceEdit;
	bool _restoringState;

	/// <summary>
	/// Is there anything to undo?
	/// </summary>
	public bool CanUndo => _undoStack.Count > 0;

	/// <summary>
	/// Is there anything to redo?
	/// </summary>
	public bool CanRedo => _redoStack.Count > 0;

	TextState CurrentState() => new( Text ?? "", CaretPosition, Label.SelectionStart, Label.SelectionEnd );

	/// <summary>
	/// Remember the state before an edit, so undo can come back to it. Call this before
	/// changing anything.
	/// </summary>
	void RecordEdit( EditKind kind )
	{
		// Putting an old state back isn't an edit
		if ( _restoringState ) return;

		// Editing after undoing throws away the redos, the same as everywhere else
		_redoStack.Clear();

		// Carry on the run we're already in rather than starting a step per keystroke
		if ( _undoStack.Count > 0 && kind != EditKind.Single && kind == _runKind && _timeSinceEdit < EditRunTimeout )
		{
			_timeSinceEdit = 0;
			return;
		}

		_undoStack.Add( CurrentState() );

		if ( _undoStack.Count > MaxUndoSteps )
			_undoStack.RemoveAt( 0 );

		_runKind = kind;
		_timeSinceEdit = 0;
	}

	/// <summary>
	/// Anything that moves the caret or changes the selection ends the current run, so the
	/// next thing typed becomes its own undo step.
	/// </summary>
	void BreakEditRun()
	{
		_runKind = EditKind.Single;
	}

	/// <summary>
	/// Put back the text, caret and selection from before the last edit.
	/// </summary>
	public void Undo()
	{
		if ( !CanEdit ) return;
		if ( _undoStack.Count == 0 ) return;

		_redoStack.Add( CurrentState() );

		Restore( _undoStack[^1] );
		_undoStack.RemoveAt( _undoStack.Count - 1 );

		BreakEditRun();
	}

	/// <summary>
	/// Put back whatever the last undo took away.
	/// </summary>
	public void Redo()
	{
		if ( !CanEdit ) return;
		if ( _redoStack.Count == 0 ) return;

		_undoStack.Add( CurrentState() );

		Restore( _redoStack[^1] );
		_redoStack.RemoveAt( _redoStack.Count - 1 );

		BreakEditRun();
	}

	/// <summary>
	/// Throw the history away - for when the text is replaced by something the user didn't do.
	/// </summary>
	public void ClearUndoHistory()
	{
		_undoStack.Clear();
		_redoStack.Clear();

		BreakEditRun();
	}

	void Restore( TextState state )
	{
		_restoringState = true;

		try
		{
			Label.Text = state.Text;
			Label.SetSelection( state.SelectionStart, state.SelectionEnd );
			CaretPosition = state.Caret;
		}
		finally
		{
			_restoringState = false;
		}

		OnValueChanged();
	}
}
