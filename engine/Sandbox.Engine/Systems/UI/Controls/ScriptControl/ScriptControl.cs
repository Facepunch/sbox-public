using Microsoft.AspNetCore.Components;
using System;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

/// <summary>
/// A reusable script editor with completion, quick info, parameter help and diagnostics.
/// Analysis uses Game.Scripting and never executes the source. All public source offsets are UTF-16.
/// </summary>
[Library( "ScriptControl" ), StyleSheet.Inline( "scriptcontrol", Styles )]
public partial class ScriptControl : Panel
{
	int _lastCaret = -1;
	int _tabSize = 4;

	internal ScriptTextEntry Entry { get; }
	internal ScriptDocument Document { get; private set; } = new( "" );

	/// <summary>
	/// The current immutable metadata snapshot, updated immediately when the source or inputs change.
	/// </summary>
	public Script.Analysis Analysis { get; private set; }

	/// <summary>
	/// Source code. Replacing the document clears its undo history.
	/// </summary>
	[Parameter]
	public string Source
	{
		get => Entry.Text ?? "";
		set
		{
			value = NormalizeNewlines( value );
			if ( Source == value )
			{
				return;
			}
			Entry.Text = value;
			Entry.ClearUndoHistory();
			Entry.SelectElements( 0, 0 );
			Entry.ScrollOffset = Vector2.Zero;
			SourceChanged();
			DismissIntelliSense();
		}
	}

	/// <summary>
	/// Typed external inputs made available to script analysis.
	/// </summary>
	[Parameter]
	public IReadOnlyList<Script.Input> Inputs
	{
		get => _inputs;
		set
		{
			_inputs = value?.ToArray() ?? Array.Empty<Script.Input>();
			RefreshAnalysis();
		}
	}

	/// <summary>
	/// Called immediately when the source text changes, including IME previews.
	/// </summary>
	[Parameter]
	public Action<string> OnSourceChanged { get; set; }

	/// <summary>
	/// Called when analysis produces a new diagnostic snapshot, before the display delay.
	/// </summary>
	[Parameter]
	public Action<IReadOnlyList<Script.Diagnostic>> OnDiagnosticsChanged { get; set; }

	/// <summary>
	/// Allow selection and navigation while preventing edits.
	/// </summary>
	[Parameter]
	public bool ReadOnly
	{
		get => Entry.ReadOnly;
		set => Entry.ReadOnly = value;
	}

	/// <summary>
	/// Number of spaces inserted for a tab, clamped to one through eight.
	/// </summary>
	[Parameter]
	public int TabSize
	{
		get => _tabSize;
		set => _tabSize = Math.Clamp( value, 1, 8 );
	}

	/// <summary>
	/// Diagnostics from the current analysis, including those waiting for the display delay.
	/// </summary>
	public IReadOnlyList<Script.Diagnostic> Diagnostics => Analysis.Diagnostics;

	/// <summary>
	/// Caret position measured in UTF-16 source units.
	/// </summary>
	public int CaretOffset
	{
		get => Document.ToOffset( Entry.CaretPosition );
		set => Select( value, 0 );
	}

	internal int CaretLine => Document.LineAt( CaretOffset );

	/// <summary>
	/// One-based line number at the caret.
	/// </summary>
	public int Line => CaretLine + 1;

	/// <summary>
	/// One-based text-element column at the caret.
	/// </summary>
	public int Column => Document.ToElement( CaretOffset ) - Document.ToElement( Document.Lines[CaretLine] ) + 1;

	/// <summary>
	/// Whether an edit is available to undo.
	/// </summary>
	public bool CanUndo => Entry.CanUndo;

	/// <summary>
	/// Whether an undone edit is available to redo.
	/// </summary>
	public bool CanRedo => Entry.CanRedo;

	/// <summary>
	/// Whether the completion list is open.
	/// </summary>
	public bool CompletionVisible => _completions.Count > 0;

	public ScriptControl()
	{
		AddClass( "script-control" );
		var body = Add.Panel( "editor-body" );
		_ = new ScriptGutter( this ) { Parent = body };
		Entry = new ScriptTextEntry( this ) { Parent = body };
		CreateStatusBar();
		RefreshAnalysis();
		ApplyTheme();
	}

	/// <summary>
	/// Give keyboard focus to the text editor.
	/// </summary>
	public void FocusEditor() => Entry.Focus();

	/// <summary>
	/// Undo the last edit and close IntelliSense.
	/// </summary>
	public void Undo()
	{
		Entry.Undo();
		DismissIntelliSense();
	}

	/// <summary>
	/// Reapply the last undone edit and close IntelliSense.
	/// </summary>
	public void Redo()
	{
		Entry.Redo();
		DismissIntelliSense();
	}

	/// <summary>
	/// Select a range of source code, measured in UTF-16 units.
	/// </summary>
	public void Select( int start, int length )
	{
		DismissIntelliSense();
		start = Math.Clamp( start, 0, Source.Length );
		var end = Math.Clamp( start + Math.Max( 0, length ), start, Source.Length );
		Entry.SelectElements( Document.ToElement( start ), Document.ToElement( end ) );
	}

	/// <summary>
	/// Insert text at the selection as one undoable edit.
	/// </summary>
	public void InsertText( string text ) => Entry.OnPaste( text ?? "" );

	public override void Tick()
	{
		base.Tick();
		if ( _lastCaret != CaretOffset )
		{
			OnCaretMoved();
		}

		if ( !Entry.HasFocus && (CompletionVisible || SignatureVisible) )
		{
			DismissIntelliSense();
		}

		if ( ReadOnly && CompletionVisible )
		{
			CloseCompletion();
		}

		UpdateHover();
		PositionPopups();
		if ( _diagnosticsPending && _sinceEdit >= Math.Max( 0, DiagnosticDelay ) )
		{
			ShowDiagnostics();
		}
	}

	void OnCaretMoved()
	{
		_lastCaret = CaretOffset;
		CloseHover( keepTarget: true );
		if ( Entry.HasFocus )
		{
			UpdateSignature();
		}

		UpdateStatusPosition();
	}

	internal Rect SourceCaretRect( int offset ) => Entry.ElementRect( Document.ToElement( offset ) );

	internal static string NormalizeNewlines( string text ) => (text ?? "").Replace( "\r\n", "\n" ).Replace( '\r', '\n' );

	internal static string EscapeHtml( string text ) => (text ?? "").Replace( "&", "&amp;" ).Replace( "<", "&lt;" ).Replace( ">", "&gt;" ).Replace( "\"", "&quot;" );

	internal void Replace( int start, int length, string text, int? caret = null )
	{
		// TextEntry currently exposes undoable replacement through its paste operation.
		Select( start, length );
		Entry.OnPaste( text );
		if ( caret.HasValue )
		{
			Select( caret.Value, 0 );
		}
	}
}
