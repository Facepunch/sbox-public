using System;

namespace Sandbox.UI;

public partial class ScriptControl
{
	IReadOnlyList<Script.Input> _inputs = [];

	/// <summary>
	/// Immediately recreate analysis after input metadata or scripting permissions change.
	/// </summary>
	public void RefreshAnalysis()
	{
		Analysis = Game.Scripting.Analyze( Source, _inputs );
		Entry.RebuildColors();
		UpdateSignature();
		CloseHover();
		UpdateStatus();
		OnDiagnosticsChanged?.Invoke( Diagnostics );
	}

	internal void SourceChanged()
	{
		if ( Document.Text == Source )
		{
			return;
		}

		Document = new( Source );
		_sinceEdit = 0;
		_diagnosticsPending = true;
		// Keep syntax colors in sync with the text. Only diagnostics wait for the idle delay.
		RefreshAnalysis();
		OnSourceChanged?.Invoke( Source );
		CreateValueEvent( "source", Source );
	}

	internal bool InLiteral( int offset )
		=> Analysis.GetToken( offset )?.Kind is Script.TokenKind.Comment or Script.TokenKind.String;

	// A diagnostic can point to a missing token, so give empty spans a visible width.
	internal static int DiagnosticEnd( Script.SourceSpan span ) => span.Start + Math.Max( 1, span.Length );

	internal static bool ContainsDiagnostic( Script.SourceSpan span, int offset ) => offset >= span.Start && offset < DiagnosticEnd( span );
}
