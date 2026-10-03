using Diagnostic = Microsoft.CodeAnalysis.Diagnostic;

namespace Editor;

internal class StartupFailedPopup : BaseWindow
{
	TaskCompletionSource<bool> _tcs;
	public Task<bool> ContinueLoadTask => _tcs.Task;

	Label MessageLabel;
	Label StatusLabel;

	Layout ContentLayout;
	Layout ButtonLayout;

	public StartupFailedPopup( Project project ) : base()
	{
		_tcs = new TaskCompletionSource<bool>();

		WindowTitle = $"Failed to load {project.Config.Title} - Compile Failed";
		SetWindowIcon( "dangerous" );
		DeleteOnClose = true;

		Size = new( 800, 400 );

		SetWindowFlag( WindowFlags.CloseButton, false );
		SetWindowFlag( WindowFlags.MinMaxButtons, false );

		Layout = Layout.Column();
		Layout.Margin = 16;

		MessageLabel = Layout.Add( new Label() );
		MessageLabel.WordWrap = true;
		MessageLabel.TextSelectable = false;

		ContentLayout = Layout.AddColumn( 1 );
		ContentLayout.Margin = new Sandbox.UI.Margin( 0, 16, 0, 0 );
		ContentLayout.Spacing = 4;

		Layout.AddSpacingCell( 16 );
		Layout.AddStretchCell();

		ButtonLayout = Layout.AddRow();
		ButtonLayout.Spacing = 8;

		Refresh();
	}

	internal static async Task<bool> Show( Project project )
	{
		Log.Warning( $"Errors when loading '{project}'" );

		var popup = new StartupFailedPopup( project );

		// The splash is an SDL window, so it can't be the parent of a Qt dialog.
		// Hide it while the application-modal error dialog owns startup.
		EditorSplashScreen.Singleton?.Hide();
		popup.SetModal( true, true );
		popup.CenterWindow();
		popup.Show();

		Native.QApp.alert( popup._widget, 5000 ); // flash for 5 sec

		g_pToolFramework2.SetStallMonitorMainThreadWindow( popup._widget );

		try
		{
			while ( !popup.ContinueLoadTask.IsCompleted )
			{
				// we're calling this from a blocking task on the startup flow, so manually keep UI alive and responsive
				Application.Spin();
				Native.QApp.processEvents();

				// monitor for code changes, so we can recompile and reevaluate the state of things
				FileWatch.Tick();
				Project.Tick();

				await Task.Yield();
			}

			return await popup.ContinueLoadTask;
		}
		finally
		{
			EditorSplashScreen.Singleton?.Show();
			EditorSplashScreen.RestoreStallMonitor();
		}
	}

	void Refresh()
	{
		ContentLayout.Clear( true );

		var errors = Project.GetCompileDiagnostics().Where( x => x.Severity is Microsoft.CodeAnalysis.DiagnosticSeverity.Error );
		MessageLabel.Text = $"Project compilation failed with {errors.Count()} error(s).\n\nResolve the issues to continue loading, or continue anyway to launch the project in a broken state. ";

		var scroller = ContentLayout.Add( new ScrollArea( this ), 1 );

		scroller.Canvas = new Widget( scroller );
		scroller.Canvas.Layout = Layout.Column();
		scroller.Canvas.Layout.Margin = new Sandbox.UI.Margin( 4, 4, 12, 4 );
		scroller.Canvas.Layout.Spacing = 2;
		scroller.SetStyles( $"border-radius: 4px; background-color: {Theme.ControlBackground.Hex};" );

		foreach ( var diag in errors )
		{
			var row = scroller.Canvas.Layout.AddRow();
			var widget = row.Add( new DiagnosticWidget( diag ) );
		}

		scroller.Canvas.Layout.AddStretchCell();

		ButtonLayout.Clear( true );
		ButtonLayout.Add( new Button.Danger( "Continue Anyway", "report" ) { Clicked = ContinueLoad } );

		ButtonLayout.AddStretchCell();
		StatusLabel = ButtonLayout.Add( new Label() );
		ButtonLayout.AddStretchCell();

		ButtonLayout.Add( new IconButton( "content_paste", () => CopyToClipboard( errors ) ) { ToolTip = "Copy All to Clipboard" } );
		ButtonLayout.Add( new Button( "Open Solution", "integration_instructions" ) { Clicked = CodeEditor.OpenSolution } );
		ButtonLayout.Add( new Button( "Close Project", "close" ) { Clicked = CancelLoad } );
	}

	void CopyToClipboard( IEnumerable<Diagnostic> errors )
	{
		var sb = new System.Text.StringBuilder();
		foreach ( var diag in errors )
		{
			sb.AppendLine( diag.ToString() );
		}

		EditorUtility.Clipboard.Copy( sb.ToString() );
	}

	void ContinueLoad()
	{
		_tcs.TrySetResult( true );
		Close();
	}

	void CancelLoad()
	{
		_tcs.TrySetResult( false );
		Close();
	}

	[Event( "compile.started" )]
	public void OnCompileStarted( CompileGroup group )
	{
		if ( StatusLabel.IsValid() )
		{
			StatusLabel.Text = "(Recompiling...)";
		}

		Update();
	}

	[Event( "compile.complete" )]
	public void OnCompileComplete( CompileGroup group )
	{
		if ( group.BuildResult.Success )
		{
			// good to go! close and move on
			ContinueLoad();
			return;
		}

		Refresh();
		Update();
	}
}


file class DiagnosticWidget : Widget
{
	private Diagnostic Diagnostic;

	public string FilePath { get; private set; }
	public int LineNumber { get; private set; }
	public int CharNumber { get; private set; }

	public DiagnosticWidget( Diagnostic diag ) : base( null )
	{
		Diagnostic = diag;

		Cursor = CursorShape.Finger;

		var span = diag.Location.GetLineSpan();
		var mappedSpan = diag.Location.GetMappedLineSpan();

		// Path can be null if the spans are not valid (not related to a file)
		FilePath = mappedSpan.HasMappedPath ? mappedSpan.Path : span.Path;
		LineNumber = mappedSpan.Span.Start.Line + 1;
		CharNumber = mappedSpan.Span.Start.Character + 1;

		Layout = Layout.Row();
		Layout.Spacing = 4;
		Layout.Margin = new Sandbox.UI.Margin( 24, 4, 4, 4 );

		Layout.Add( new Label( $"<a href=\"{diag.Descriptor.HelpLinkUri}\">{diag.Descriptor.Id}</a> {diag.GetMessage()}" )
		{
			Alignment = TextFlag.LeftTop | TextFlag.WordWrap,
			WordWrap = true,
		}, 1 ).SetStyles( "background-color: transparent;" );

		Layout.Add( new Label( $"{System.IO.Path.GetFileName( FilePath )}:{LineNumber}" )
		{
			Alignment = TextFlag.RightTop,
			TranslucentBackground = true,
		} ).SetStyles( $"color: {Color.White.WithAlpha( 0.5f ).Hex}; background-color: transparent;" );
	}

	protected override void OnPaint()
	{
		base.OnPaint();

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		if ( IsUnderMouse )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.TextControl.WithAlpha( 0.1f ) );
			Paint.DrawRect( LocalRect, 4 );
		}

		Paint.SetPen( Theme.Red );
		Paint.DrawIcon( LocalRect.Shrink( 4 ), "dangerous", 16, TextFlag.LeftTop );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			OpenInEditor();
			e.Accepted = true;
			return;
		}

		base.OnMouseClick( e );
	}

	private void OpenInEditor()
	{
		// Can be null if our diagnostic spans are not valid
		if ( FilePath != null )
		{
			CodeEditor.OpenFile( FilePath, LineNumber, CharNumber );
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var menu = new ContextMenu( this );
		menu.AddSeparator();

		menu.AddOption( $"Open in {CodeEditor.Title}", "edit", OpenInEditor );
		if ( FilePath != null )
		{
			menu.AddOption( $"Show in Explorer", "drive_file_move", () => EditorUtility.OpenFileFolder( FilePath ) );
		}
		menu.AddSeparator();
		menu.AddOption( $"Copy", "content_paste", () =>
		{
			EditorUtility.Clipboard.Copy( Diagnostic.ToString() );
		} );

		menu.OpenAtCursor();
		e.Accepted = true;
	}
}
