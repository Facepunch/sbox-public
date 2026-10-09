using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.PanelGallery;

/// <summary>
/// Editing, IntelliSense, diagnostics and independent themes using the engine ScriptControl.
/// </summary>
public sealed class ScriptControlPage : GalleryPage
{
	readonly Sandbox.UI.Label _checks;

	public ScriptControlPage() : base( "Script Editor", "Sandbox.UI.ScriptControl. These examples analyze text; they never run scripts." )
	{
		var actions = Case( "Checks" );
		actions.AddChild( new Sandbox.UI.Button( "Run checks", "check", "primarybutton", RunChecks ) );
		_checks = actions.Add.Label( "", "output" );

		var live = Editor( "Editing and IntelliSense — Ctrl+Space for completion, Ctrl+Shift+Space for parameters, F8 for errors", Sample );
		live.Inputs = [new( "speed", typeof( float ), "Speed supplied by the host, in units per second." )];
		var tools = Case( "Live editor controls" );
		tools.AddChild( new Sandbox.UI.Button( "Undo", null, "flatbutton", live.Undo ) );
		tools.AddChild( new Sandbox.UI.Button( "Redo", null, "flatbutton", live.Redo ) );
		tools.AddChild( new Sandbox.UI.Button( "Toggle status", null, "flatbutton", () => live.ShowStatusBar = !live.ShowStatusBar ) );
		tools.AddChild( new Sandbox.UI.Button( "Switch theme", null, "flatbutton", () =>
			live.Theme = live.Theme == ScriptControl.ThemeDefinition.Default ? LightTheme : ScriptControl.ThemeDefinition.Default ) );
		tools.AddChild( new Sandbox.UI.Button( "Reset", null, "flatbutton", () => live.Source = Sample ) );

		tools.AddChild( new Sandbox.UI.Button( "Show completion", null, "flatbutton", () =>
		{
			live.Source = "MathF.";
			live.CaretOffset = live.Source.Length;
			live.FocusEditor();
			live.ShowCompletions();
		} ) );

		var light = Editor( "Independent light theme — changing the editor above must not affect this one", Sample );
		light.Theme = LightTheme;
		light.Inputs = live.Inputs;
		light.ShowStatusBar = false;

		Editor( "Delayed diagnostics — fix the missing value; errors should wait until you stop typing", "var value = ;\nreturn value;" );
		var readOnly = Editor( "Read only — selection and copying work; typing, paste and completion must not edit", Sample );
		readOnly.ReadOnly = true;
		readOnly.Inputs = live.Inputs;

		Editor( "Unicode, tabs and scrolling — try selection, dragging and IME composition", "// 😀 e\u0301 日本語\n\tvar value = 1;\n" +
			"// " + new string( 'x', 160 ) + "\n" + string.Join( "\n", Enumerable.Range( 1, 30 ).Select( i => $"// Line {i}" ) ) + "\nreturn value;" );
		RunChecks();
	}

	ScriptControl Editor( string title, string source )
	{
		var control = Case( title ).AddChild<ScriptControl>();
		control.Style.Set( "width: 100%; height: 250px; flex-shrink: 0;" );
		control.Source = source;
		return control;
	}

	void RunChecks()
	{
		var control = new ScriptControl();
		var passed = 0;
		void Check( bool condition, string message )
		{
			if ( !condition ) throw new InvalidOperationException( message );
			passed++;
		}

		try
		{
			Check( control.Analysis.Source == "", "An empty control must have analysis." );
			var changes = 0;
			control.OnSourceChanged = _ => changes++;
			control.Source = "return 1;\r\n";
			Check( control.Source == "return 1;\n" && changes == 1, "Normalize newlines and notify once." );
			Check( control.Analysis.Source == control.Source, "Analysis must be immediate." );
			control.CaretOffset = control.Source.Length;
			control.InsertText( "// 😀 e\u0301" );
			Check( control.CanUndo && control.Analysis.Source == control.Source, "Paste must analyze immediately and be undoable." );
			control.Undo();
			Check( control.Source == "return 1;\n", "Undo must restore the text." );
			control.Redo();
			Check( control.Source.EndsWith( "// 😀 e\u0301" ), "Redo must restore Unicode text." );
			var source = control.Source;
			var analysis = control.Analysis;
			control.Theme = LightTheme;
			Check( control.Source == source && control.CanUndo && ReferenceEquals( analysis, control.Analysis ), "Changing theme must preserve editing state." );
			control.Source = "return speed;";
			control.Inputs = [new( "speed", typeof( float ) )];
			Check( control.Analysis.GetSymbol( 7 )?.Type == typeof( float ), "Host input metadata must take effect immediately." );
			control.Source = "MathF.";
			control.CaretOffset = control.Source.Length;
			control.ShowCompletions();
			Check( control.CompletionVisible, "Member completion should open." );
			control.DismissIntelliSense();
			Check( !control.CompletionVisible, "Completion must close." );
			control.ReadOnly = true;
			control.InsertText( "changed" );
			Check( control.Source == "MathF.", "Read-only must reject edits." );
			control.Source = "var broken = ;";
			Check( control.Diagnostics.Any( d => d.Severity == Script.DiagnosticSeverity.Error ), "Invalid syntax must produce diagnostics." );
			_checks.Text = $"{passed} checks passed";
			Log.Info( $"ScriptControl gallery: {passed} checks passed" );
		}
		catch ( Exception exception )
		{
			_checks.Text = $"FAILED after {passed} checks: {exception.Message}";
			Log.Error( exception, "ScriptControl gallery checks failed" );
		}
		finally
		{
			control.Delete( true );
		}
	}

	static ScriptControl.ThemeDefinition LightTheme { get; } = new()
	{
		Background = Color.Parse( "#fafafa" ).Value,
		Foreground = Color.Parse( "#202020" ).Value,
		GutterBackground = Color.Parse( "#f0f0f0" ).Value,
		CurrentLine = Color.Parse( "#e8e8e8" ).Value,
		Selection = Color.Parse( "#add6ff" ).Value,
		Caret = Color.Black,
		LineNumber = Color.Gray,
		ActiveLineNumber = Color.Black,
		Keyword = Color.Blue,
		ControlFlow = Color.Parse( "#af00db" ).Value,
		Number = Color.Parse( "#098658" ).Value,
		String = Color.Parse( "#a31515" ).Value,
		Comment = Color.Parse( "#008000" ).Value,
		Identifier = Color.Parse( "#001080" ).Value,
		Type = Color.Parse( "#267f99" ).Value,
		Member = Color.Parse( "#795e26" ).Value,
		PopupBackground = Color.White,
		PopupTitle = Color.Parse( "#267f99" ).Value,
		PopupDescription = Color.Black,
		SignatureForeground = Color.Black,
		ActiveParameter = Color.Blue,
		CompletionHoverBackground = Color.Parse( "#eeeeee" ).Value
	};

	const string Sample = """
		// Hover MathF, Sin or speed for documentation.
		// Type MathF. to explore members.
		var angle = MathF.Sin( speed );
		if ( angle > 0 )
		{
		    return angle;
		}
		return 0;
		""";
}
