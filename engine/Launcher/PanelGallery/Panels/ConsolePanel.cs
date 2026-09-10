using System.Text.RegularExpressions;

namespace Sandbox.PanelGallery;

/// <summary>
/// The editor's log output. Everything the Qt console does - filters with counts, repeat collapsing,
/// a clickable stack trace, and a command line with history and autocomplete - plus Unity's global
/// collapse.
/// <para>
/// The list is virtual: rows are absolutely positioned inside a spacer the height of the whole list,
/// and only the ones on screen exist as panels. Twenty thousand lines cost the same as twenty.
/// </para>
/// </summary>
public class ConsolePanel : Panel
{
	/// <summary>
	/// A line, and how many identical ones it stands for.
	/// </summary>
	public class Entry
	{
		public LogEvent Log;
		public int Repeats;
	}

	//
	// The log is collected whether or not the console is open, so hook it once and keep it static
	//
	static readonly List<LogEvent> buffer = new();
	static bool hooked;

	const int MaxLines = 20000;
	const float RowHeight = 20.0f;
	const int Overscan = 6;

	public static int Count => buffer.Count;

	/// <summary>
	/// How many of each kind we've seen, for the counters in the status bar.
	/// </summary>
	public static int InfoCount { get; private set; }

	/// <inheritdoc cref="InfoCount"/>
	public static int WarnCount { get; private set; }

	/// <inheritdoc cref="InfoCount"/>
	public static int ErrorCount { get; private set; }

	/// <summary>
	/// Start collecting log output. Called at editor startup rather than when the console is first
	/// looked at, so it has the same history the editor's console does.
	/// </summary>
	public static void Hook()
	{
		if ( hooked ) return;

		hooked = true;

		// A named method, not a lambda - hotload can't rebind a lambda that something else holds
		EditorUtility.AddLogger( OnLogMessage );
	}

	static void OnLogMessage( LogEvent entry )
	{
		buffer.Add( entry );

		if ( entry.Level >= LogLevel.Error ) ErrorCount++;
		else if ( entry.Level == LogLevel.Warn ) WarnCount++;
		else InfoCount++;

		if ( buffer.Count > MaxLines )
			buffer.RemoveRange( 0, buffer.Count - MaxLines );
	}

	static void ClearBuffer()
	{
		buffer.Clear();

		InfoCount = 0;
		WarnCount = 0;
		ErrorCount = 0;
	}

	Panel scroll;
	Panel spacer;
	Panel detailPanel;
	Panel suggestionList;
	TextInput commandBox;

	Sandbox.UI.Label infoCount;
	Sandbox.UI.Label warnCount;
	Sandbox.UI.Label errorCount;
	Panel collapseButton;

	readonly Dictionary<int, Panel> rows = new();
	readonly List<Entry> visible = new();
	readonly List<string> history = new();

	bool showInfo = true;
	bool showWarn = true;
	bool showError = true;
	bool collapse;
	string[] terms = Array.Empty<string>();

	Entry selected;
	int selectedRow = -1;
	int historyIndex = -1;
	int builtFrom = -1;
	int builtTo = -1;
	int builtCount = -1;
	int seenLines;

	public ConsolePanel()
	{
		Hook();

		AddClass( "console" );

		BuildToolbar();

		scroll = Add.Panel( "list" );
		spacer = scroll.Add.Panel( "spacer" );

		detailPanel = Add.Panel( "detail" );

		BuildCommandLine();

		Refilter();
		ScrollToBottom();
	}

	void BuildToolbar()
	{
		var bar = Add.Panel( "bar" );

		Button( bar, "Clear", "delete", () =>
		{
			ClearBuffer();
			selected = null;
			seenLines = 0;
			Refilter();
		} );

		collapseButton = Button( bar, "Collapse", null, () =>
		{
			collapse = !collapse;
			collapseButton.SetClass( "active", collapse );
			Refilter();
		} );

		bar.Add.Panel( "grow" );

		var searchBox = bar.AddChild( new TextInput( "Filter..", "search" ) );
		searchBox.OnChange = value =>
		{
			terms = (value ?? "").Split( ' ', StringSplitOptions.RemoveEmptyEntries );
			Refilter();
		};

		infoCount = Filter( bar, "comment", "info", () => showInfo, v => showInfo = v );
		warnCount = Filter( bar, "warning", "warn", () => showWarn, v => showWarn = v );
		errorCount = Filter( bar, "error", "error", () => showError, v => showError = v );
	}

	static Panel Button( Panel parent, string title, string icon, Action clicked )
	{
		var button = parent.Add.Panel( "button" );

		if ( icon is not null ) button.Icon( icon );
		if ( title is not null ) button.Add.Label( title );

		button.AddEventListener( "onclick", clicked );

		return button;
	}

	Sandbox.UI.Label Filter( Panel bar, string icon, string className, Func<bool> get, Action<bool> set )
	{
		var button = bar.Add.Panel( "filter" );
		button.AddClass( className );
		button.SetClass( "active", get() );
		button.Icon( icon );

		var label = button.Add.Label( "0", "count" );

		button.AddEventListener( "onclick", () =>
		{
			set( !get() );
			button.SetClass( "active", get() );
			Refilter();
		} );

		return label;
	}

	/// <summary>
	/// The command line. Same job as the real console's: run what's typed, remember it, and offer
	/// completions from the convar system.
	/// </summary>
	void BuildCommandLine()
	{
		suggestionList = Add.Panel( "suggestions" );
		suggestionList.Style.Display = DisplayMode.None;

		var row = Add.Panel( "commandline" );

		row.Add.Label( ">", "prompt" );

		commandBox = row.AddChild( new TextInput( "Enter Console Command..", null ) );
		commandBox.Style.FlexGrow = 1;
		commandBox.OnChange = _ => UpdateSuggestions();
		commandBox.OnSubmit = RunCommand;
		commandBox.OnButton = OnCommandKey;

		Button( row, null, "vertical_align_bottom", ScrollToBottom ).AddClass( "square" );
	}

	bool OnCommandKey( string button )
	{
		if ( button == "tab" )
		{
			var first = suggestionList.Children.FirstOrDefault();
			if ( first is not null ) Complete( first.GetAttribute( "command", null ) );
			return true;
		}

		if ( button == "up" || button == "down" )
		{
			if ( history.Count == 0 ) return true;

			if ( historyIndex < 0 ) historyIndex = history.Count;

			historyIndex = (historyIndex + (button == "up" ? -1 : 1)).Clamp( 0, history.Count );

			commandBox.SetValue( historyIndex >= history.Count ? "" : history[historyIndex] );
			UpdateSuggestions();
			return true;
		}

		return false;
	}

	void Complete( string command )
	{
		if ( string.IsNullOrEmpty( command ) ) return;

		commandBox.SetValue( command + " " );
		UpdateSuggestions();
	}

	void UpdateSuggestions()
	{
		suggestionList.DeleteChildren( true );

		var text = commandBox.Value;

		if ( string.IsNullOrWhiteSpace( text ) || text.Contains( ' ' ) )
		{
			suggestionList.Style.Display = DisplayMode.None;
			return;
		}

		var options = EditorUtility.AutoComplete( text, 8 );

		if ( options.Length == 0 )
		{
			suggestionList.Style.Display = DisplayMode.None;
			return;
		}

		suggestionList.Style.Display = DisplayMode.Flex;

		foreach ( var option in options )
		{
			var row = suggestionList.Add.Panel( "suggestion" );
			row.SetAttribute( "command", option.Command );
			row.Add.Label( option.Command, "command" );

			if ( !string.IsNullOrEmpty( option.Description ) )
				row.Add.Label( option.Description, "description" );

			row.AddEventListener( "onclick", () => Complete( option.Command ) );
		}
	}

	void RunCommand()
	{
		var command = commandBox.Value.Trim();
		if ( command.Length == 0 ) return;

		commandBox.SetValue( "" );

		history.Remove( command );
		history.Add( command );
		historyIndex = -1;

		UpdateSuggestions();

		if ( command == "clear" )
		{
			ClearBuffer();
			selected = null;
			seenLines = 0;
			Refilter();
			return;
		}

		buffer.Add( new LogEvent
		{
			Level = LogLevel.Info,
			Logger = "console",
			Message = $"> {command}",
			Time = DateTime.Now,
		} );

		ConsoleSystem.Run( command );

		seenLines = buffer.Count;
		Refilter();
		ScrollToBottom();
	}

	/// <summary>
	/// Work out which entries are on show. Only run when something actually changes - the list can
	/// be twenty thousand long.
	/// </summary>
	void Refilter()
	{
		visible.Clear();

		var groups = collapse ? new Dictionary<string, Entry>() : null;

		foreach ( var log in buffer )
		{
			if ( !Allowed( log ) ) continue;

			var key = collapse ? $"{log.Level}\n{log.Logger}\n{log.Message}" : null;

			if ( collapse && groups.TryGetValue( key, out var existing ) )
			{
				existing.Repeats++;
				continue;
			}

			// Even without collapse on, runs of the same line fold up - same as the real console
			var last = visible.Count > 0 ? visible[^1] : null;

			if ( !collapse && last is not null && last.Log.Message == log.Message && last.Log.Logger == log.Logger )
			{
				last.Repeats++;
				continue;
			}

			var entry = new Entry { Log = log };

			if ( collapse ) groups[key] = entry;
			visible.Add( entry );
		}

		infoCount.Text = CountText( InfoCount );
		warnCount.Text = CountText( WarnCount );
		errorCount.Text = CountText( ErrorCount );

		spacer.Style.Height = visible.Count * RowHeight;

		// Everything shifted, so the built range is meaningless now
		ClearRows();
		builtCount = visible.Count;

		// The selected line is an entry, not an index - find where it ended up
		selectedRow = selected is null ? -1 : visible.FindIndex( x => x.Log.Message == selected.Log.Message && x.Log.Time == selected.Log.Time );
	}

	static string CountText( int count ) => count > 999 ? "999+" : $"{count:n0}";

	bool Allowed( LogEvent log )
	{
		if ( log.Level >= LogLevel.Error && !showError ) return false;
		if ( log.Level == LogLevel.Warn && !showWarn ) return false;
		if ( log.Level < LogLevel.Warn && !showInfo ) return false;

		// Every term has to be in there somewhere, same rule as the real console
		foreach ( var term in terms )
		{
			var inMessage = log.Message is not null && log.Message.Contains( term, StringComparison.OrdinalIgnoreCase );
			var inLogger = log.Logger is not null && log.Logger.Contains( term, StringComparison.OrdinalIgnoreCase );

			if ( !inMessage && !inLogger ) return false;
		}

		return true;
	}

	void ClearRows()
	{
		foreach ( var row in rows.Values )
		{
			row.Delete( true );
		}

		rows.Clear();

		builtFrom = -1;
		builtTo = -1;
	}

	RealTimeSince timeSinceChecked;

	public override void Tick()
	{
		// New output arrives whether we're looking or not
		if ( timeSinceChecked > 0.1f )
		{
			timeSinceChecked = 0;

			if ( seenLines != buffer.Count )
			{
				var atBottom = IsScrolledToBottom();

				seenLines = buffer.Count;
				Refilter();

				if ( atBottom ) ScrollToBottom();
			}
		}

		UpdateVisibleRows();
	}

	float ViewHeight => scroll.IsValid() ? scroll.Box.Rect.Height * scroll.ScaleFromScreen : 0;

	bool IsScrolledToBottom()
	{
		if ( !scroll.IsValid() ) return true;

		return scroll.ScrollOffset.y >= (visible.Count * RowHeight) - ViewHeight - RowHeight;
	}

	void ScrollToBottom()
	{
		if ( !scroll.IsValid() ) return;

		scroll.ScrollOffset = new Vector2( 0, MathF.Max( 0, visible.Count * RowHeight - ViewHeight ) );
	}

	/// <summary>
	/// Build only the rows in view, plus a few either side so scrolling doesn't show gaps.
	/// </summary>
	void UpdateVisibleRows()
	{
		if ( !scroll.IsValid() ) return;

		var height = ViewHeight;
		if ( height < 1 ) return;

		// The list resizes when the detail pane opens and closes, which can leave the scroll
		// hanging past the end
		var limit = MathF.Max( 0, visible.Count * RowHeight - height );
		if ( scroll.ScrollOffset.y > limit ) scroll.ScrollOffset = new Vector2( 0, limit );

		var offset = scroll.ScrollOffset.y;

		var from = Math.Max( 0, (int)(offset / RowHeight) - Overscan );
		var to = Math.Min( visible.Count - 1, (int)((offset + height) / RowHeight) + Overscan );

		if ( from == builtFrom && to == builtTo && builtCount == visible.Count )
			return;

		builtFrom = from;
		builtTo = to;
		builtCount = visible.Count;

		// Drop anything that scrolled out
		foreach ( var index in rows.Keys.ToArray() )
		{
			if ( index >= from && index <= to ) continue;

			rows[index].Delete( true );
			rows.Remove( index );
		}

		for ( int i = from; i <= to; i++ )
		{
			if ( rows.ContainsKey( i ) ) continue;

			rows[i] = BuildRow( i );
		}
	}

	Panel BuildRow( int index )
	{
		var entry = visible[index];
		var log = entry.Log;

		var row = spacer.Add.Panel( "row" );
		row.Style.Top = index * RowHeight;
		row.SetClass( "odd", index % 2 == 1 );
		row.SetClass( "selected", index == selectedRow );
		row.SetClass( "warn", log.Level == LogLevel.Warn );
		row.SetClass( "error", log.Level >= LogLevel.Error );

		row.Icon( IconFor( log.Level ), "level" );
		row.Add.Label( log.Time.ToString( "HH:mm:ss" ), "time" );
		row.Add.Label( log.Logger ?? "", "source" );

		AddHighlighted( row.Add.Panel( "message" ), FirstLine( log.Message ) );

		if ( entry.Repeats > 0 )
			row.Add.Label( $"{entry.Repeats + 1:n0}", "repeat" );

		row.AddEventListener( "onclick", () =>
		{
			if ( rows.TryGetValue( selectedRow, out var previous ) ) previous.SetClass( "selected", false );

			selected = entry;
			selectedRow = index;
			row.SetClass( "selected", true );

			BuildDetail();
		} );

		return row;
	}

	/// <summary>
	/// Split the text around whatever is being filtered for, so the matches can be marked - the real
	/// console does the same thing with a background colour.
	/// </summary>
	void AddHighlighted( Panel parent, string text )
	{
		if ( terms.Length == 0 || string.IsNullOrEmpty( text ) )
		{
			parent.Add.Label( text, "part" );
			return;
		}

		var index = 0;

		while ( index < text.Length )
		{
			var best = -1;
			var length = 0;

			foreach ( var term in terms )
			{
				var found = text.IndexOf( term, index, StringComparison.OrdinalIgnoreCase );
				if ( found < 0 ) continue;

				if ( best < 0 || found < best )
				{
					best = found;
					length = term.Length;
				}
			}

			if ( best < 0 )
			{
				parent.Add.Label( text[index..], "part" );
				return;
			}

			if ( best > index ) parent.Add.Label( text[index..best], "part" );

			parent.Add.Label( text.Substring( best, length ), "part match" );
			index = best + length;
		}
	}

	static string IconFor( LogLevel level ) => level switch
	{
		LogLevel.Error => "error",
		LogLevel.Warn => "warning",
		_ => "comment",
	};

	static string FirstLine( string message )
	{
		if ( string.IsNullOrEmpty( message ) ) return "";

		var end = message.AsSpan().IndexOfAny( '\r', '\n' );

		return end < 0 ? message.TrimEnd() : message[..end];
	}

	static readonly Regex StackLine = new( @"^\s*at (.+?)( in (.+):line (\d+))?$", RegexOptions.Compiled );

	/// <summary>
	/// The selected line in full, with its stack broken into rows that open the file when clicked.
	/// </summary>
	void BuildDetail()
	{
		detailPanel.DeleteChildren( true );
		detailPanel.SetClass( "empty", selected is null );

		if ( selected is null ) return;

		var log = selected.Log;

		var message = detailPanel.Add.Label( log.Message?.TrimEnd() ?? "", "message" );
		message.AddEventListener( "onclick", () => EditorUtility.Clipboard.Copy( $"{log.Message}\n{log.Stack}" ) );

		var stack = log.Stack;

		if ( string.IsNullOrWhiteSpace( stack ) && log.Exception is not null )
			stack = log.Exception.ToString();

		if ( string.IsNullOrWhiteSpace( stack ) ) return;

		foreach ( var line in stack.Split( '\n', '\r' ) )
		{
			if ( string.IsNullOrWhiteSpace( line ) ) continue;

			AddStackRow( line );
		}
	}

	void AddStackRow( string line )
	{
		var match = StackLine.Match( line );

		var row = detailPanel.Add.Panel( "stackrow" );

		if ( !match.Success )
		{
			row.Add.Label( line.Trim(), "function" );
			return;
		}

		var function = match.Groups[1].Value;
		if ( function.IndexOf( '(' ) > 0 ) function = function[..function.IndexOf( '(' )];

		row.Add.Label( function, "function" );

		if ( !match.Groups[3].Success )
		{
			row.AddClass( "nofile" );
			return;
		}

		var file = match.Groups[3].Value;
		var lineNumber = match.Groups[4].Value.ToInt();

		row.SetClass( "engine", file.Contains( "\\engine\\Sandbox." ) );
		row.Add.Label( $"{System.IO.Path.GetFileName( file )}:{lineNumber}", "file" );

		row.AddEventListener( "onclick", () => CodeEditor.OpenFile( file, lineNumber ) );
	}
}
