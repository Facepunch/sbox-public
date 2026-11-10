using Microsoft.AspNetCore.Components;
using System;
using Sandbox.UI.Construct;

namespace Sandbox.UI;

public partial class ScriptControl
{
	Panel _status;
	Label _statusMessage;
	Label _statusPosition;
	RealTimeSince _sinceEdit;
	bool _diagnosticsPending;
	bool _showStatusBar = true;

	/// <summary>
	/// Show the optional status bar beneath the editor.
	/// </summary>
	[Parameter]
	public bool ShowStatusBar
	{
		get => _showStatusBar;
		set
		{
			_showStatusBar = value;
			_status.Style.Display = value ? DisplayMode.Flex : DisplayMode.None;
		}
	}

	/// <summary>
	/// Seconds of idle time before showing diagnostics. Highlighting stays immediate.
	/// </summary>
	[Parameter]
	public float DiagnosticDelay { get; set; } = 0.75f;
	internal IReadOnlyList<Script.Diagnostic> VisibleDiagnostics => _diagnosticsPending ? Array.Empty<Script.Diagnostic>() : Diagnostics;

	void CreateStatusBar()
	{
		_status = Add.Panel( "editor-status" );
		_statusMessage = _status.Add.Label( "Ready", "status-message" );
		_statusPosition = _status.Add.Label( "Ln 1, Col 1", "status-position" );
	}

	void UpdateStatus()
	{
		Script.Diagnostic? error = null;
		foreach ( var diagnostic in VisibleDiagnostics )
		{
			if ( diagnostic.Severity != Script.DiagnosticSeverity.Error )
			{
				continue;
			}

			error = diagnostic;
			break;
		}

		var hasError = error.HasValue;
		_status.SetClass( "has-error", hasError );
		_statusMessage.Text = hasError
			? error.Value.Message.Replace( '\r', ' ' ).Replace( '\n', ' ' )
			: _diagnosticsPending ? "Editing…" : "No errors";
		UpdateStatusPosition();
	}

	void UpdateStatusPosition()
	{
		_statusPosition.Text = $"Ln {Line}, Col {Column}";
	}

	void ShowDiagnostics()
	{
		_diagnosticsPending = false;
		// Recreate quick info so it includes diagnostics that were hidden during typing.
		CloseHover( keepTarget: true );
		UpdateStatus();
	}

	/// <summary>
	/// Move to the next diagnostic (F8), or the previous one (Shift+F8).
	/// </summary>
	public void GoToDiagnostic( bool previous = false )
	{
		if ( Diagnostics.Count == 0 )
		{
			return;
		}
		var ordered = Diagnostics.OrderBy( d => d.Span.Start ).ToArray();
		var diagnostic = previous
			? ordered.LastOrDefault( d => d.Span.Start < CaretOffset, ordered[^1] )
			: ordered.FirstOrDefault( d => d.Span.Start > CaretOffset, ordered[0] );
		DismissIntelliSense();
		Select( diagnostic.Span.Start, diagnostic.Span.Length );
		FocusEditor();
		ShowQuickInfo( diagnostic.Span.Start );
	}
}
