using System;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

public partial class ScriptControl
{
	const int CompletionLimit = 200;
	const int CompletionRowsVisible = 10;
	internal const int CompletionPageSize = 8;

	IntelliSensePopup _completionPopup;
	Panel _completionRows;
	IntelliSensePopup _completionDetail;
	Label _completionTitle;
	Label _completionDescription;
	List<Script.Completion> _completions = new();
	Script.SourceSpan _replacementSpan;
	int _selectedCompletion;

	void CreateCompletionPopup()
	{
		_completionPopup = CreatePopup( "completion-popup", ignoresInput: false );
		_completionRows = new CompletionRows( this ) { Parent = _completionPopup };
		_completionRows.AddClass( "completion-list" );
		_completionDetail = CreatePopup( "completion-detail", ignoresInput: true );
		_completionTitle = _completionDetail.Add.Label( "", "info-title" );
		_completionDescription = _completionDetail.Add.Label( "", "info-description" );
	}

	/// <summary>
	/// Open permitted completions for the current caret. Ctrl+Space invokes this.
	/// </summary>
	public void ShowCompletions( bool explicitRequest = true )
	{
		if ( ReadOnly )
		{
			return;
		}
		if ( InLiteral( Math.Max( 0, CaretOffset - 1 ) ) )
		{
			CloseCompletion();
			return;
		}

		var result = Analysis.GetCompletions( CaretOffset );
		_replacementSpan = result.ReplacementSpan;
		var length = Math.Clamp( CaretOffset - _replacementSpan.Start, 0, _replacementSpan.Length );
		var prefix = Source.Substring( _replacementSpan.Start, length );
		var followsDot = CaretOffset > 0 && Source[CaretOffset - 1] == '.';
		if ( !explicitRequest && prefix.Length == 0 && !followsDot )
		{
			CloseCompletion();
			return;
		}

		var previous = CompletionVisible ? _completions[_selectedCompletion].Label : null;
		_completions = result.Items
			.Where( item => item.Label.Contains( prefix, StringComparison.OrdinalIgnoreCase ) )
			.DistinctBy( item => (item.Label, item.InsertText, item.Kind) )
			.OrderBy( item => CompletionRank( item.Label, prefix ) )
			.ThenBy( item => item.Label, StringComparer.OrdinalIgnoreCase )
			.Take( CompletionLimit )
			.ToList();
		_selectedCompletion = 0;
		var previousIndex = _completions.FindIndex( item => item.Label == previous );
		if ( previousIndex >= 0 && CompletionRank( _completions[previousIndex].Label, prefix ) == CompletionRank( _completions[0].Label, prefix ) )
		{
			_selectedCompletion = previousIndex;
		}

		if ( _completions.Count == 0 )
		{
			CloseCompletion();
			return;
		}

		if ( _completionPopup is null ) CreateCompletionPopup();
		CloseHover( keepTarget: true );
		RenderCompletions();
	}

	static int CompletionRank( string label, string prefix )
	{
		if ( label.Equals( prefix, StringComparison.Ordinal ) )
		{
			return 0;
		}
		if ( label.Equals( prefix, StringComparison.OrdinalIgnoreCase ) )
		{
			return 1;
		}
		if ( label.StartsWith( prefix, StringComparison.Ordinal ) )
		{
			return 2;
		}
		if ( label.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) )
		{
			return 3;
		}
		return 4;
	}

	void RenderCompletions()
	{
		_completionRows.DeleteChildren( true );
		var first = Math.Clamp( _selectedCompletion - CompletionRowsVisible / 2, 0, Math.Max( 0, _completions.Count - CompletionRowsVisible ) );
		var count = Math.Min( CompletionRowsVisible, _completions.Count - first );

		for ( var index = first; index < first + count; index++ )
		{
			AddCompletionRow( index );
		}

		_completionRows.Add.Label( $"{_selectedCompletion + 1} / {_completions.Count}", "completion-footer" );
		var selected = _completions[_selectedCompletion];
		_completionTitle.Text = selected.Detail ?? selected.Label;
		_completionDescription.Text = ScriptDocumentation.PlainText( selected.Description );
		_completionDescription.Style.Display = string.IsNullOrWhiteSpace( _completionDescription.Text ) ? DisplayMode.None : DisplayMode.Flex;
	}

	void AddCompletionRow( int index )
	{
		var item = _completions[index];
		var row = _completionRows.Add.Panel( "completion-row" );
		row.SetClass( "selected", index == _selectedCompletion );

		var icon = item.Kind switch
		{
			Script.SymbolKind.Type => "◇",
			Script.SymbolKind.Member => "⬡",
			Script.SymbolKind.Keyword => "{}",
			_ => "▪"
		};
		row.Add.Label( icon, $"symbol-icon {item.Kind.ToString().ToLowerInvariant()}" );
		row.Add.Label( item.Label, "completion-name" );
		row.AddEventListener( "onmousedown", e =>
		{
			e.StopPropagation();
			_selectedCompletion = index;
			AcceptCompletion();
			FocusEditor();
		} );
	}

	internal void MoveCompletion( int direction )
	{
		if ( !CompletionVisible )
		{
			return;
		}

		_selectedCompletion = Math.Clamp( _selectedCompletion + direction, 0, _completions.Count - 1 );
		RenderCompletions();
	}

	/// <summary>
	/// Replace the current completion range with the selected suggestion as an undoable edit.
	/// </summary>
	public void AcceptCompletion()
	{
		if ( !CompletionVisible || ReadOnly )
		{
			return;
		}

		var item = _completions[_selectedCompletion];
		var span = _replacementSpan;
		CloseCompletion();
		Replace( span.Start, span.Length, item.InsertText );
		UpdateSignature();
	}

	internal void CloseCompletion()
	{
		_completions.Clear();
		CloseCompletionDetail();
		var popup = _completionPopup;
		_completionPopup = null;
		popup?.Delete( true );
	}

	void CloseCompletionDetail()
	{
		var popup = _completionDetail;
		_completionDetail = null;
		popup?.Delete( true );
	}

	sealed class CompletionRows : Panel
	{
		readonly ScriptControl _editor;

		public CompletionRows( ScriptControl editor ) => _editor = editor;

		public override void OnMouseWheel( Vector2 value )
		{
			_editor.MoveCompletion( Math.Sign( value.y ) );
		}
	}
}
