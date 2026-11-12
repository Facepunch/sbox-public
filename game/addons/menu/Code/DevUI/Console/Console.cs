using Sandbox.UI.Construct;

namespace Sandbox.UI.Dev;

[Library( "console" )]
public class Console : Panel
{
	// Same cap as the editor console. Kept for the whole session, so an error logged every frame grew this without
	// bound: about a million entries and 3 GB of messages and stack text in a long editor session.
	const int MaxEntries = 10000;

	internal List<LogEvent> Entries = new();
	internal VirtualList Output;
	internal TextEntry Input;
	internal TextEntry Filter;
	internal Button ScrollConsole;

	LogEventPanel logEventPanel;

	struct MessageCategory
	{
		public Button Button;
		public int Count;
		public bool Disabled;

		public void Toggle()
		{
			Disabled = !Disabled;
			Button.SetClass( "disabled", Disabled );
		}

		public void Clear()
		{
			Button.Text = "0";
			Count = 0;
		}
	}

	MessageCategory Message;
	MessageCategory Warning;
	MessageCategory Error;

	public Console()
	{
		// Its header - the levels shown (each a chip with its count, pressed to hide it), a filter, then its tools
		var header = Add.Panel( "header" );
		{
			var levels = header.Add.Panel( "levels" );

			Error.Button = levels.AddChild( new Button( "0", "error", "level err", null ) );
			Error.Button.Tooltip = "Errors";
			Error.Button.AddEventListener( "onclick", () => { Error.Toggle(); OnFilter(); } );

			Warning.Button = levels.AddChild( new Button( "0", "warning", "level wrn", null ) );
			Warning.Button.Tooltip = "Warnings";
			Warning.Button.AddEventListener( "onclick", () => { Warning.Toggle(); OnFilter(); } );

			Message.Button = levels.AddChild( new Button( "0", "info", "level msg", null ) );
			Message.Button.Tooltip = "Info";
			Message.Button.AddEventListener( "onclick", () => { Message.Toggle(); OnFilter(); } );

			var search = header.Add.Panel( "filter" );
			search.Add.Icon( "search" );
			Filter = search.AddChild<TextEntry>();
			Filter.AddEventListener( "onchange", OnFilter );
			Filter.Placeholder = "Filter";

			header.Add.Panel( "spacer" );

			var logs = header.AddChild( new Button( null, "folder_open", () => OpenLogsFolder() ) );
			logs.AddClass( "tool" );
			logs.Tooltip = "Open the logs folder";

			var clear = header.AddChild( new Button( null, "delete_sweep", () => OnClear() ) );
			clear.AddClass( "tool" );
			clear.Tooltip = "Clear";

			ScrollConsole = header.AddChild( new Button( null, "vertical_align_bottom", () => Output?.TryScrollToBottom() ) );
			ScrollConsole.AddClass( "tool" );
			ScrollConsole.Tooltip = "Jump to the latest";
		}

		// The log - and a line's details folded out over it, once one's clicked
		Body = Add.Panel( "body" );
		{
			var body = Body;
			Output = body.AddChild<VirtualList>();
			Output.AddClass( "console_output" );
			Output.ItemHeight = 22;
			Output.PreferScrollToBottom = true;
			Output.OnCreateCell = ( Panel cell, object data ) =>
			{
				var entry = new ConsoleRow();
				entry.Parent = cell;
				entry.SetLogEvent( (LogEvent)data );
				entry.OnEntryClicked = logEventPanel.Switch;
			};

			logEventPanel = body.AddChild<LogEventPanel>();
		}

		// What's typed to run - its suggestions open upward, over the log
		var command = Add.Panel( "command" );
		{
			command.Add.Label( "›", "prompt" );

			Input = command.AddChild<TextEntry>();
			Input.AddClass( "input" );
			Input.AddEventListener( "onsubmit", OnSubmit );
			Input.AutoComplete = FillAutoComplete;
			Input.HistoryCookie = "console-input-history";
			Input.Placeholder = "Run a command";
		}

		MenuUtility.AddLogger( OnConsoleMessage );

		Output.AcceptsFocus = true;
		Output.AllowChildSelection = true;

	}

	private void OnConsoleMessage( LogEvent e )
	{
		if ( e.Message.Contains( '\n' ) || e.Message.Contains( '\r' ) )
		{
			var parts = e.Message.Split( new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );
			foreach ( var part in parts )
			{
				var ee = e;
				ee.Message = part;

				AddEvent( ee );
			}
		}
		else
		{
			AddEvent( e );
		}
	}

	void AddEvent( LogEvent e )
	{
		// Only the text is shown. Logged objects (e.g. a component as message context) would otherwise
		// keep their whole scene alive for as long as the entry stays in this list.
		e.Arguments = null;
		Entries.Add( e );

		// Trim in batches like the dev tools console, so a busy log doesn't rebuild the shown items every line
		if ( Entries.Count > MaxEntries * 2 )
		{
			Entries.RemoveRange( 0, Entries.Count - MaxEntries );
			OnFilter();
		}
		else if ( ShouldShowEvent( e ) )
		{
			Output.AddItem( e );
		}

		if ( e.Level == LogLevel.Info || e.Level == LogLevel.Trace )
		{
			Message.Count++;
			Message.Button.Text = $"{Message.Count:n0}";
		}

		if ( e.Level == LogLevel.Warn )
		{
			Warning.Count++;
			Warning.Button.Text = $"{Warning.Count:n0}";
		}

		if ( e.Level == LogLevel.Error )
		{
			Error.Count++;
			Error.Button.Text = $"{Error.Count:n0}";
		}
	}

	void OnFilter()
	{
		Output.SetItems( Entries.Where( x => ShouldShowEvent( x ) ).Select( x => x as object ) );
	}

	bool ShouldShowEvent( LogEvent e )
	{
		if ( e.Level == LogLevel.Error && Error.Disabled ) return false;
		if ( e.Level == LogLevel.Warn && Warning.Disabled ) return false;
		if ( e.Level == LogLevel.Info && Message.Disabled ) return false;
		if ( e.Level == LogLevel.Trace && Message.Disabled ) return false;

		if ( string.IsNullOrWhiteSpace( Filter.Text ) )
			return true;

		return e.Message.Contains( Filter.Text, StringComparison.OrdinalIgnoreCase );
	}

	Panel Body;
	public override void Tick()
	{
		base.Tick();
		ScrollConsole?.SetClass( "active", Output?.IsScrollAtBottom ?? false );
	}

	void OnClear()
	{
		Output.Clear();
		Entries.Clear();

		Message.Clear();
		Warning.Clear();
		Error.Clear();
	}

	void OpenLogsFolder()
	{
		MenuUtility.OpenFolder( Environment.CurrentDirectory + "/logs/" );
	}

	void OutputLine( string line )
	{
		var e = new LogEvent() { Message = $"> {line}", Level = LogLevel.Info, Logger = "in", Time = DateTime.Now };
		Entries.Add( e );
		Output.AddItem( e );
		MenuUtility.RunCommand( line );
	}

	void OnSubmit()
	{
		var t = Input.Text;
		if ( string.IsNullOrWhiteSpace( t ) )
		{
			Input.Text = "";
			return;
		}

		if ( t == "clear" )
		{
			OnClear();
		}
		else
		{
			if ( t.Contains( '\n' ) || t.Contains( '\r' ) )
			{
				var parts = t.Split( new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries );
				foreach ( var part in parts )
				{
					OutputLine( part );
				}
			}
			else
			{
				OutputLine( t );
			}
		}

		Output.TryScrollToBottom();

		Input.Text = "";
		Input.AddToHistory( t );
		Input.DestroyAutoComplete();
		Input.Focus();
	}

	private object[] FillAutoComplete( string arg )
	{
		if ( string.IsNullOrWhiteSpace( arg ) )
			return Array.Empty<string>();

		return MenuUtility.AutoComplete( arg, 20 ).Select( x => (object)new TextEntry.AutocompleteEntry { Title = $"{x.Command} - {x.Description}".Trim( '-', ' ' ), Value = x.Command } ).ToArray();
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		// Not the right button - what's selected is kept for its menu to copy
		if ( e.MouseButton != MouseButtons.Left ) return;

		foreach ( var child in Children )
		{
			Unselect( child );
		}
	}

	/// <summary>
	/// What's selected in it - the log's lines top to bottom, a line each, then anything in a line's details. The list
	/// would give its lines as one (its cells aren't laid out as a column, so the engine puts them a space apart, in the
	/// order they were made).
	/// </summary>
	string SelectedText()
	{
		var parts = new List<string>();

		foreach ( var row in Output.Descendants.OfType<ConsoleRow>().OrderBy( x => x.Box.Rect.Top ) )
		{
			if ( RowSelection( row ) is { Length: > 0 } text )
				parts.Add( text );
		}

		foreach ( var block in logEventPanel.Descendants.Where( x => x.AllowChildSelection ) )
		{
			if ( block.GetClipboardValue( false ) is { Length: > 0 } text )
				parts.Add( text );
		}

		return string.Join( "\n", parts );
	}

	/// <summary>
	/// What's selected of a line - its time and message a space apart, read as the engine reads a selection across a row
	/// of children. Only for the read: a row selecting its own children would stop a selection running across rows.
	/// </summary>
	static string RowSelection( ConsoleRow row )
	{
		row.AllowChildSelection = true;
		try { return row.GetClipboardValue( false ); }
		finally { row.AllowChildSelection = false; }
	}

	/// <summary>
	/// Ctrl+C on a selection in it - its lines as lines (see <see cref="SelectedText"/>). Comes after the list's own
	/// answer, as the copy works its way out from what has focus, so it's the one kept.
	/// </summary>
	public override string GetClipboardValue( bool cut ) => SelectedText() is { Length: > 0 } text ? text : null;

	/// <summary>
	/// Its menu, anywhere in it - Cut what's selected in the command line; Copy what's selected, or the line pressed on;
	/// Copy with stack, for a line that has one; Copy all that's shown; Paste into the command line
	/// </summary>
	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		var popup = new Popup( this, Popup.PositionMode.UnderMouse, 0 );
		var line = e.Target?.AncestorsAndSelf.OfType<ConsoleRow>().FirstOrDefault()?.Event;

		var typed = Input.GetClipboardValue( false );
		if ( !string.IsNullOrEmpty( typed ) )
			popup.AddOption( "Cut", "content_cut", () => Clipboard.SetText( Input.GetClipboardValue( true ) ) );

		var copy = SelectedText() is { Length: > 0 } selected ? selected : !string.IsNullOrEmpty( typed ) ? typed : line?.Message;
		if ( !string.IsNullOrEmpty( copy ) )
			popup.AddOption( "Copy", "content_copy", () => Clipboard.SetText( copy ) );

		if ( line is { } withStack && !string.IsNullOrWhiteSpace( withStack.Stack ) )
			popup.AddOption( "Copy with stack", "subject", () => Clipboard.SetText( $"{withStack.Message}\n{withStack.Stack}" ) );

		popup.AddOption( "Copy all", "select_all", () =>
			Clipboard.SetText( string.Join( "\n", Entries.Where( ShouldShowEvent ).Select( x => $"{x.Time:HH:mm:ss} {x.Message}" ) ) ) );

		if ( MenuUtility.GetClipboardText() is { Length: > 0 } paste )
		{
			popup.AddOption( "Paste", "content_paste", () =>
			{
				Input.Focus();
				Input.OnPaste( paste );
			} );
		}
	}

	private void Unselect( Panel p )
	{
		if ( p is Label l )
		{
			l.ShouldDrawSelection = false;
			return;
		}

		foreach ( var child in p.Children )
		{
			Unselect( child );
		}
	}

}
