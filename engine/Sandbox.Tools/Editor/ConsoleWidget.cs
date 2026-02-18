using Facepunch.ActionGraphs;
using Microsoft.CodeAnalysis;
using Sandbox.ActionGraphs;
using System;
using System.Text;

namespace Editor;

[Dock( "Editor", "Console", "text_snippet" )]
internal class ConsoleWidget : Widget
{
	internal static ConsoleWidget Instance { get; private set; }

	//
	// Keep quite a lot but not really stupid amounts, we want them so we can filter
	// This isn't how many are displayed, ConsoleOutput handles it's own
	//
	const int MaxEvents = 10000;
	static List<LogEvent> Events = new( MaxEvents );

	public LineEdit Input { get; init; }

	ConsoleOutput Output { get; init; }
	LineEdit Filter { get; init; }
	StatusBarLog CurrentStatusBarLog { get; set; }

	MessageCategory Message;
	MessageCategory Warning;
	MessageCategory Error;

	// Copied from addons Theme.cs
	static readonly Color MessageButtonColor = Color.Parse( "#cccdcd" ) ?? default;
	static readonly Color WarningButtonColor = Color.Parse( "#E6DB74" ) ?? default;
	static readonly Color ErrorButtonColor = Color.Parse( "#FB5A5A" ) ?? default;
	static readonly Color FilterButtonColor = Color.Parse( "#cccdcd" ) ?? default;

	Widget Inspector;
	StackTraceProperty CurrentStackTrace;

	/// <summary>
	/// Shows a stack trace on right side of the console window
	/// </summary>
	/// <param name="ev"></param>
	internal void SetLogEvent( LogEvent ev )
	{
		ClearLogEvent();
		CurrentStackTrace = new StackTraceProperty( Inspector, ev );
		Inspector.Layout.Add( CurrentStackTrace, 1 );
	}

	void ClearLogEvent()
	{
		CurrentStackTrace?.Destroy();
	}

	// Static because the widget can be deleted on hide/show & hotload
	static List<Microsoft.CodeAnalysis.Diagnostic> Diagnostics = new();

	/// <summary>
	/// Finds console output logs that are diagnostics and removes them.
	/// </summary>
	void ClearDiagnosticLogs()
	{
		Output.SetEvents( Events.Where( x => !x.IsDiagnostic ) );
	}

	void PopulateDiagnostics()
	{
		// Only show warnings and errors from diagnostics reports
		foreach ( var diagnostic in Diagnostics.Where( x => x.Severity >= DiagnosticSeverity.Warning ).OrderBy( x => x.Severity ) )
		{
			AddConsoleMessage( diagnostic );
		}

		foreach ( var graph in EditorNodeLibrary.GetGraphs() )
		{
			if ( !graph.HasErrors() ) continue;

			foreach ( var message in graph.Messages.Where( x => x.IsError ) )
			{
				AddConsoleMessage( message );
			}
		}
	}

	/// <summary>
	/// Raises the console dock so it's visible and in focus.
	/// </summary>
	static void RaiseConsole()
	{
		EditorWindow.Blur();
		EditorWindow.Focus( true );
		EditorWindow.DockManager.RaiseDock( "Console" );
	}

	[Event( "compile.complete" )]
	internal void CaptureDiagnostics( CompileGroup compileGroup )
	{
		// clear any log entries that are from diagnostic logs
		ClearDiagnosticLogs();

		Diagnostics.Clear();
		Diagnostics.AddRange( compileGroup.Compilers.Where( x => x.Diagnostics != null ).SelectMany( x => x.Diagnostics ) );

		ClearStatusBarButtons();
		PopulateDiagnostics();
	}

	[Event( "actiongraph.saved" )]
	internal void CaptureDiagnostics( ActionGraph actionGraph )
	{
		// clear any log entries that are from diagnostic logs
		ClearDiagnosticLogs();
		ClearStatusBarButtons();
		PopulateDiagnostics();
	}

	public ConsoleWidget( Widget parent ) : base( parent )
	{
		Instance = this;

		DeleteOnClose = true;

		WindowTitle = "Console";
		SetWindowIcon( "text_snippet" );
		Name = "Console";
		Size = new( 500, 500 );
		MinimumSize = new( 100, 100 );

		Input = new LineEdit( this );
		Input.MaxHistoryItems = 20;
		Input.HistoryCookie = "ConsoleInput";

		Filter = new LineEdit( this );
		Filter.PlaceholderText = "Filter..";
		Filter.TextEdited += _ => OnFilter();

		Output = new ConsoleOutput( this );
		Layout = Layout.Row();

		var splitter = new Splitter( this );
		splitter.IsHorizontal = true;
		Layout.Add( splitter );

		// Main content
		var mainWidget = new Widget( this );
		mainWidget.Layout = Layout.Column();
		mainWidget.Layout.Margin = 4;

		{
			mainWidget.Layout.Add( Output, 1 );

			var hLayout = mainWidget.Layout.AddRow();
			{
				var clear = new Button.Clear( "" )
				{
					Icon = "delete",
					Clicked = Clear,
					ToolTip = "Clear the console log"
				};

				var bottom = new Button.Clear( "" )
				{
					Icon = "vertical_align_bottom",
					Clicked = Output.ScrollToBottom,
					ToolTip = "Scroll to bottom of console log"
				};

				hLayout.Add( Input, 3 );
				hLayout.AddSpacingCell( 4 );
				hLayout.Add( Filter, 1 );
				hLayout.AddSpacingCell( 4 );
				hLayout.Add( clear );
				hLayout.Add( bottom );
			}

			splitter.AddWidget( mainWidget );
		}

		{
			Inspector = new Widget( this );
			Inspector.Layout = Layout.Column();
			splitter.AddWidget( Inspector );

			// Set stretch factors
			splitter.SetStretch( 0, 7 );
			splitter.SetStretch( 1, 3 );
		}

		Input.PlaceholderText = "Enter Console Command..";
		Input.ReturnPressed += CommandInput;

		Input.SetAutoComplete( BuildAutoCompleteOptions );

		EditorUtility.AddLogger( OnConsoleMessage );

		Message.Button = new Button( "0", "comment", this )
		{
			Clicked = () => { Message.Toggle(); OnFilter(); RaiseConsole(); },
			OnPaintOverride = () => DrawToggleButton( Message.Button, MessageButtonColor, "comment", Message.Disabled, TextFlag.Center ),
		};

		Warning.Button = new Button( "0", "warning", this )
		{
			Clicked = () => { Warning.Toggle(); OnFilter(); RaiseConsole(); },
			OnPaintOverride = () => DrawToggleButton( Warning.Button, WarningButtonColor, "warning", Warning.Disabled, TextFlag.Center ),
		};

		Error.Button = new Button( "0", "error", this )
		{
			Clicked = () => { Error.Toggle(); OnFilter(); RaiseConsole(); },
			OnPaintOverride = () => DrawToggleButton( Error.Button, ErrorButtonColor, "error", Error.Disabled, TextFlag.Center ),
		};

		EditorWindow.StatusBar.AddWidgetRight( Message.Button, 0 );
		EditorWindow.StatusBar.AddWidgetRight( Warning.Button, 0 );
		EditorWindow.StatusBar.AddWidgetRight( Error.Button, 0 );

		PopulateDiagnostics();
	}

	public override void OnDestroyed()
	{
		EditorUtility.RemoveLogger( OnConsoleMessage );
		ClearStatusBar();
	}

	void ClearStatusBar()
	{
		if ( EditorWindow is null )
			return;

		if ( CurrentStatusBarLog != null )
		{
			EditorWindow.StatusBar.RemoveWidget( CurrentStatusBarLog );
			CurrentStatusBarLog?.Destroy();
			CurrentStatusBarLog = null;
		}

		EditorWindow.StatusBar.RemoveWidget( Message.Button );
		EditorWindow.StatusBar.RemoveWidget( Warning.Button );
		EditorWindow.StatusBar.RemoveWidget( Error.Button );

		// Get rid of old buttons
		Message.Button?.Destroy();
		Message.Button = default;

		Warning.Button?.Destroy();
		Warning.Button = default;

		Error.Button?.Destroy();
		Error.Button = default;
	}

	void UpdateStatusBar( LogEvent e )
	{
		if ( CurrentStatusBarLog == null )
		{
			CurrentStatusBarLog = new StatusBarLog( this, e );
			EditorWindow.StatusBar.AddWidgetLeft( CurrentStatusBarLog, 2 );
		}

		// Try to set the current status bar log
		CurrentStatusBarLog.SetLog( e );
	}

	void AddConsoleMessage( Diagnostic diagnostic )
	{
		var msg = diagnostic.GetMessage();
		var span = diagnostic.Location.GetLineSpan();
		var mappedSpan = diagnostic.Location.GetMappedLineSpan();

		AddConsoleMessage( new LogEvent()
		{
			IsDiagnostic = true,

			Message = msg,
			HtmlMessage = $"<a href=\"{diagnostic.Descriptor.HelpLinkUri}\">{diagnostic.Descriptor.Id}</a> {msg}",
			Logger = "Compiler",
			Time = DateTime.Now,
			Level = diagnostic.Severity switch
			{
				DiagnosticSeverity.Error => LogLevel.Error,
				DiagnosticSeverity.Warning => LogLevel.Warn,
				_ => LogLevel.Info
			},

			// Generate a stack since diagnostics don't provide them (and shouldn't)
			Stack = $"at {msg} in {(mappedSpan.HasMappedPath ? mappedSpan.Path : span.Path)}:line {(mappedSpan.Span.Start.Line + 1)}"
		} );
	}

	void AddConsoleMessage( ValidationMessage message )
	{
		var msg = message.Value;

		AddConsoleMessage( new LogEvent()
		{
			IsDiagnostic = true,

			Message = msg,
			HtmlMessage = msg,
			Logger = "ActionGraph",
			Time = DateTime.Now,
			Level = message.Level switch
			{
				MessageLevel.Error => LogLevel.Error,
				MessageLevel.Warning => LogLevel.Warn,
				_ => LogLevel.Info
			},

			// Generate a stack since diagnostics don't provide them (and shouldn't)
			Stack = $"at {message.Context.StackTraceIdentifier}"
		} );
	}

	string GetCount( int inputCount, int maxCount = 999 )
	{
		return $"{inputCount.Clamp( 0, maxCount ):n0}{(inputCount > maxCount ? "+" : "")}";
	}

	void AddConsoleMessage( LogEvent e )
	{
		Events.Add( e );

		if ( e.Level == LogLevel.Trace && Message.Button.IsValid() ) Message.Button.Text = GetCount( ++Message.Count );
		if ( e.Level == LogLevel.Info && Message.Button.IsValid() ) Message.Button.Text = GetCount( ++Message.Count );
		if ( e.Level == LogLevel.Warn && Warning.Button.IsValid() ) Warning.Button.Text = GetCount( ++Warning.Count );
		if ( e.Level == LogLevel.Error && Error.Button.IsValid() ) Error.Button.Text = GetCount( ++Error.Count );

		if ( ShouldShowEvent( e ) )
		{
			Output.AddEvent( e );
		}

		UpdateStatusBar( e );

		// matt: this might be expensive with 10k events? would a circular buffer be better
		if ( Events.Count > MaxEvents )
		{
			Events.RemoveAt( 0 );
		}
	}

	void OnConsoleMessage( LogEvent e )
	{
		ThreadSafe.AssertIsMainThread();

		// Don't process messages if the widget has been destroyed
		if ( !this.IsValid() )
			return;

		AddConsoleMessage( e );
	}

	List<string> GetFilterTerms()
	{
		return Filter.Text.Split( " " ).Select( x => x.Trim() ).Where( x => !string.IsNullOrEmpty( x ) ).ToList();
	}

	void OnFilter()
	{
		Output.FilterTerms = GetFilterTerms();
		Output.SetEvents( Events.Where( x => ShouldShowEvent( x ) ) );
	}

	void ClearStatusBarButtons()
	{
		Message.Clear();
		Warning.Clear();
		Error.Clear();
	}

	void Clear()
	{
		Message.Clear();
		Warning.Clear();
		Error.Clear();
		Events.Clear();
		Output.Clear();

		// Clear current stack trace entry.
		ClearLogEvent();

		// Always populate diagnostics.
		PopulateDiagnostics();
	}

	bool ShouldShowEvent( LogEvent e )
	{
		var filterTerms = GetFilterTerms();

		if ( filterTerms != null && filterTerms.Any() )
		{
			bool ContainsFilterTerm( string str ) => filterTerms.All( filter => str.Contains( filter, StringComparison.OrdinalIgnoreCase ) );

			if ( !ContainsFilterTerm( e.Message ) && !ContainsFilterTerm( e.Logger ) )
				return false;
		}

		if ( e.Level == LogLevel.Trace && Message.Disabled ) return false;
		if ( e.Level == LogLevel.Info && Message.Disabled ) return false;
		if ( e.Level == LogLevel.Warn && Warning.Disabled ) return false;
		if ( e.Level == LogLevel.Error && Error.Disabled ) return false;

		return true;
	}

	void CommandInput()
	{
		var command = Input.Text.Trim();
		if ( command.Length == 0 )
			return;

		Input.Clear();
		Input.AddHistory( command );

		// For them typists
		if ( command == "clear" )
		{
			Clear();
			return;
		}

		var textcolor = "#3f3";
		var message = $"> {command}";
		var html = $"<div><span style=\"color: rgb(75, 122, 75); background-color: rgb(34, 41, 34);\">&nbsp;{DateTime.Now.ToString( "hh:mm:ss" )}&nbsp;</span> <span style=\"color: {textcolor}\">&nbsp;{message}</span></div>";
		Output.AddEvent( html, new LogEvent() );

		ConsoleSystem.Run( command );
		Output.ScrollToBottom();
	}

	void BuildAutoCompleteOptions( Menu menu, string partial )
	{
		var options = EditorUtility.AutoComplete( partial, 20 );

		foreach ( var option in options )
		{
			//if ( string.Equals( option, partial, StringComparison.OrdinalIgnoreCase ) )
			//	continue;

			menu.AddOption( option.Command );
		}
	}

	static bool DrawToggleButton( Button button, Color color, string icon, bool disabled, TextFlag align = TextFlag.Center )
	{
		Paint.Antialiasing = true;
		Paint.ClearPen();

		var rect = button.LocalRect;

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrush( Color.White.WithAlpha( 0.1f ) );
			Paint.DrawRect( rect );
		}

		rect = rect.Shrink( 4, 4 );

		// If disabled make everything transparent and shit
		Paint.SetPen( disabled ? color.WithAlpha( 0.5f ) : color );
		Paint.ClearBrush();

		var iconRect = rect;
		iconRect.Width = 16;
		Paint.DrawIcon( iconRect, icon, 16, align );

		Paint.SetDefaultFont();
		Paint.DrawText( rect.Shrink( iconRect.Right + 2, 0, 0, 0 ), button.Text, align );

		return true;
	}

	struct MessageCategory
	{
		public Button Button;

		public int Count;
		public bool Disabled;

		public void Toggle()
		{
			Disabled = !Disabled;
			Button?.Update();
		}

		public void Clear()
		{
			if ( Button != null )
			{
				Button.Text = "0";
			}

			Count = 0;
		}
	}

	class ConsoleOutput : TextEdit
	{
		const int MaxDisplayedLines = 1000;

		// Store these here again (it doesn't fucking matter), we do a 1 <-> 1 of the blocks to events
		static List<LogEvent> Events = new( MaxDisplayedLines );

		public List<string> FilterTerms { get; set; }

		ConsoleWidget Console;

		public ConsoleOutput( ConsoleWidget parent ) : base( parent )
		{
			Console = parent;
			Name = "Output";
			ReadOnly = true;
			MaximumBlockCount = MaxDisplayedLines;
			MouseTracking = true;
			Editable = false;
			LinksClickable = true;
			TextSelectable = true;
		}

		public override void Clear()
		{
			Events.Clear();
			LastCursor = null;
			base.Clear();
		}

		public void AddEvent( string html, LogEvent e )
		{
			ThreadSafe.AssertIsMainThread();

			Events.Add( e );

			AppendHtml( html );

			if ( Events.Count > MaxDisplayedLines )
			{
				Events.RemoveAt( 0 );
			}
		}

		public void AddEvent( LogEvent e )
		{
			ThreadSafe.AssertIsMainThread();

			var lastEvent = Events.LastOrDefault();
			if ( lastEvent.Message == e.Message && lastEvent.Logger == e.Logger && lastEvent.Stack == e.Stack )
			{
				e.Repeats = lastEvent.Repeats + 1;

				var cursor = GetCursorAtBlock( Events.Count() - 1 );
				cursor.SelectBlockUnderCursor();
				cursor.RemoveSelectedText();
				cursor.deleteChar();
				(cursor as IDisposable).Dispose();

				Events.RemoveAt( Events.Count() - 1 );
			}

			var textcolor = "#ccc";

			if ( e.Level == LogLevel.Warn ) textcolor = "#ff9770";
			if ( e.Level == LogLevel.Trace ) textcolor = "#aaaaaa";
			if ( e.Level == LogLevel.Error ) textcolor = "#ff686b";

			var message = e.HtmlMessage;
			message = HighlightFilterText( message );

			message = message.Replace( "\t", "&nbsp;&nbsp;&nbsp;" );
			message = message.Replace( "  ", "&nbsp;&nbsp;" );

			var logger = e.Logger;
			logger = HighlightFilterText( logger );

			var loggerName = $"&nbsp; <span style=\"color: rgb(75, 122, 75); width: 200px;\">{logger}</span>";
			var repeatText = "";
			if ( e.Repeats > 0 )
			{
				repeatText = $"&nbsp; <span style=\"color: rgb(75, 122, 75); background-color: rgb(34, 41, 34);\">[{e.Repeats:n0}]</span>";
			}

			var html = $"<div>" +
				$"<span style=\"color: rgb(75, 122, 75); background-color: rgb(34, 41, 34);\">&nbsp;{e.Time.ToString( "hh:mm:ss" )}&nbsp;</span>" +
				$"{loggerName}" +
				$"{repeatText}" +
				$"<span style=\"color: {textcolor}\">&nbsp;{message}</span>" +
				$"</div>";

			AddEvent( html, e );
		}

		private string HighlightFilterText( string str )
		{
			if ( FilterTerms == null || FilterTerms.Count == 0 )
				return str;

			//
			// We do this in two steps to make sure that we don't do a replace on a HTML
			// element we've inserted (for example, if the user filters out the word "span").
			//

			// Find all matches
			var matches = new List<(int index, int length)>();
			foreach ( var term in FilterTerms )
			{
				int index = 0;

				while ( (index = str.IndexOf( term, index, StringComparison.OrdinalIgnoreCase )) != -1 )
				{
					matches.Add( (index, term.Length) );
					index += term.Length;
				}
			}

			// Now sort by index
			matches.Sort( ( a, b ) => a.index.CompareTo( b.index ) );

			var sb = new StringBuilder();
			int currentPosition = 0;

			// Build the string with the HTML tags
			foreach ( var (index, length) in matches )
			{
				if ( length <= 0 || index <= 0 )
					continue;

				var prefix = str[currentPosition..index];
				var replacement = $"{prefix}<span style=\"background-color: #404040;\">{str.Substring( index, length )}</span>";

				sb.Append( replacement );
				currentPosition = index + length;
			}

			// Append remaining text
			sb.Append( str[currentPosition..] );

			return sb.ToString();
		}

		public void SetEvents( IEnumerable<LogEvent> allEvents )
		{
			using var v = SuspendUpdates.For( this );

			Clear();

			// Do an early sort of repeats, otherwise it's just laggy shit
			List<LogEvent> events = new();
			foreach ( var ev in allEvents )
			{
				LogEvent logEvent = ev;

				var lastEvent = events.LastOrDefault();
				if ( lastEvent.Message == ev.Message )
				{
					logEvent.Repeats = lastEvent.Repeats + 1;
					events.RemoveAt( events.Count() - 1 );
				}

				events.Add( logEvent );
			}

			// Only add as many as we can display
			foreach ( var ev in events.TakeLast( MaxDisplayedLines ) )
				AddEvent( ev );
		}

		TextCursor LastCursor;
		TextCursor LastHover;

		protected override void OnMouseClick( MouseEvent e )
		{
			if ( OpenAnchor( e.LocalPosition ) )
			{
				e.Accepted = true;
				return;
			}

			LastCursor = GetCursorAtPosition( e.LocalPosition );
			Update();

			if ( LastCursor.BlockNumber >= Events.Count )
				return;

			var ev = Events[LastCursor.BlockNumber];

			Console.SetLogEvent( ev );

			e.Accepted = true;
		}

		private bool OpenAnchor( Vector2 localPosition )
		{
			var anchor = GetAnchorAt( localPosition );
			if ( string.IsNullOrEmpty( anchor ) ) return false;

			using var cursor = GetCursorAtPosition( localPosition );
			if ( cursor.BlockNumber >= Events.Count ) return false;
			var ev = Events[cursor.BlockNumber];

			// if an arg link, try to inspect arg
			if ( anchor.StartsWith( "arg:" ) )
			{
				var i = anchor[4..].ToInt();
				if ( i >= ev.Arguments.Length ) return false;

				EditorUtility.InspectorObject = ev.Arguments[i];
			}
			else if ( Uri.TryCreate( anchor, UriKind.RelativeOrAbsolute, out var uri ) )
			{
				EditorUtility.OpenFile( anchor );
			}

			return true;
		}

		protected override void OnMouseMove( MouseEvent e )
		{
			var hasAnchor = !string.IsNullOrEmpty( GetAnchorAt( e.LocalPosition ) );
			Cursor = hasAnchor ? CursorShape.Finger : CursorShape.None;

#pragma warning disable CA2000 // Dispose objects before losing scope
			// Silence this warning, this gets assigned to a field
			var newHover = GetCursorAtPosition( e.LocalPosition );
#pragma warning restore CA2000 // Dispose objects before losing scope
			if ( LastHover != null && newHover.BlockNumber == LastHover.BlockNumber ) return;

			LastHover = newHover;
			Update();
		}

		protected override void OnMouseLeave()
		{
			LastHover = null;
			Update();
		}

		protected override void OnPaint()
		{
			if ( LastCursor != null )
			{
				Paint.ClearPen();
				Paint.SetBrush( new Color( 0.5f, 0.5f, 0.5f, 0.1f ) );

				var rect = GetCursorRect( LastCursor );

				Paint.DrawRect( new Rect( 0, rect.Top - 2, Size.x, rect.Height + 4 ) );
			}

			if ( LastHover != null )
			{
				Paint.ClearPen();
				Paint.SetBrush( new Color( 0.4f, 0.5f, 0.4f, 0.1f ) );

				var rect = GetCursorRect( LastHover );

				Paint.DrawRect( new Rect( 0, rect.Top, Size.x, rect.Height ) );
			}

			base.OnPaint();
		}

		protected override void OnResize()
		{
			base.OnResize();

			ScrollToBottom();
		}

		protected override void OnShortcutPressed( KeyEvent e )
		{
			e.Accepted = true;
		}
	}

	static Color ControlText = Color.Parse( "#ccc" ) ?? default;

	class StatusBarLog : Button
	{
		Color Color;
		new string Icon;

		bool isEmpty = true;
		RealTimeSince timeSinceLastLog;

		internal StatusBarLog( Widget parent, LogEvent e ) : base( parent )
		{
			SetLog( e );
		}

		internal void SetLog( LogEvent e )
		{
			var msg = e.Message;
			// Get rid of all newlines/tabs
			msg = msg.Replace( "\n", string.Empty );
			msg = msg.Replace( "\r", string.Empty );
			msg = msg.Replace( "\t", string.Empty );

			Text = msg;

			Color = e.Level switch
			{
				LogLevel.Warn => Color.Orange,
				LogLevel.Error => Color.Parse( "#D53535" ) ?? default,
				_ => ControlText
			};

			Icon = e.Level switch
			{
				LogLevel.Warn => "warning",
				LogLevel.Error => "error",
				_ => "info"
			};

			isEmpty = false;

			// Only update the panel every 10ms
			if ( timeSinceLastLog > 0.1f )
			{
				Update();
			}

			timeSinceLastLog = 0;
		}


		[EditorEvent.Frame]
		protected void OnFrame()
		{
			// Empties the log so it doesn't render
			if ( timeSinceLastLog > 10f && !isEmpty )
			{
				isEmpty = true;
				Update();
			}
		}

		protected override void OnPaint()
		{
			// Don't bother rendering anything if we're empty
			if ( isEmpty ) return;

			var hovered = IsUnderMouse;
			var color = Color.Lighten( hovered ? 0.2f : 0 );

			Paint.Antialiasing = true;

			Paint.SetPen( color );
			Paint.SetDefaultFont( 10, 800 );

			var rect = LocalRect.Grow( -8, 0 );

			if ( !string.IsNullOrEmpty( Icon ) )
			{
				Paint.DrawIcon( rect, Icon, Size.y, TextFlag.LeftCenter );
				rect = rect.Grow( -Size.y, 0 );
			}

			Paint.DrawText( rect.Grow( -8, 0 ), Text, TextFlag.LeftCenter );
		}

		protected override void OnMouseClick( MouseEvent e )
		{
			RaiseConsole();
		}
	}

	[Event( "scene.startplay" )]
	private static void OnStartPlay()
	{
		if ( EditorPreferences.ClearConsoleOnPlay )
			ConsoleWidget.Instance?.Clear();
	}
}
