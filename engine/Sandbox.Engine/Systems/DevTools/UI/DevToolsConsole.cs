using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// The console, first of the tabs.
/// </summary>
internal sealed class DevToolsConsoleTab : DevToolsTab
{
	public override string TabId => DevTools.ConsoleTabId;
	public override string Title => "Console";
	public override bool FillsPage => true;

	public DevToolsConsoleTab()
	{
		AddChild( new DevToolsConsole() );
	}

	public override void Refresh( DevTools.Summary summary ) { }
}

/// <summary>
/// The engine console, for the dev tools' own window: the log, a filter, and a command line with autocomplete. Commands
/// run with the local console's privileges. History lasts the session - there's no game to keep cookies for.
/// </summary>
internal sealed class DevToolsConsole : Panel
{
	readonly VirtualList _output;
	readonly TextEntry _input;
	readonly TextEntry _filter;
	readonly List<LogEvent> _lines = new();

	/// <summary>
	/// Selected log text: a light grey, like the rest of the selection here.
	/// </summary>
	static readonly Color SelectionColor = Color.White.WithAlpha( 0.2f );

	public DevToolsConsole()
	{
		AddClass( "console" );

		// Clicking the log focuses the console, so Ctrl+C copies what's selected in it
		AcceptsFocus = true;

		var bar = AddChild( new Panel( null, "console-bar" ) );

		_filter = bar.AddChild( new TextEntry() );
		_filter.AddClass( "console-filter" );
		_filter.Placeholder = "Filter";
		_filter.AddEventListener( "onchange", Refilter );

		var copy = bar.AddChild( new Label( "content_copy", "icon-button material" ) );
		copy.Tooltip = "Copy every line shown";
		copy.AddEventListener( "onclick", CopyAll );

		var clear = bar.AddChild( new Label( "delete_sweep", "icon-button material" ) );
		clear.Tooltip = "Clear";
		clear.AddEventListener( "onclick", Clear );

		var bottom = bar.AddChild( new Label( "vertical_align_bottom", "icon-button material" ) );
		bottom.Tooltip = "Scroll to the end";
		bottom.AddEventListener( "onclick", () => _output.TryScrollToBottom() );

		_output = AddChild( new VirtualList() );
		_output.AddClass( "console-output" );
		_output.ItemHeight = 17;
		_output.PreferScrollToBottom = true;

		// Dragging across the log selects its text instead of scrolling it
		_output.AllowChildSelection = true;
		_output.CanDragScroll = false;

		_output.OnCreateCell = ( cell, data ) =>
		{
			var line = (LogEvent)data;
			var label = cell.AddChild( new Label( Format( line ), $"console-line {LevelClass( line.Level )}" ) );
			label.SelectionColor = SelectionColor;
		};

		_input = AddChild( new TextEntry() );
		_input.AddClass( "console-input" );
		_input.Placeholder = "Run a console command";
		_input.AddEventListener( "onsubmit", Submit );
		_input.AutoComplete = Complete;

		foreach ( var line in DevTools.Log ) AddEvent( line );
		DevTools.OnLog += AddEvent;
	}

	public override void OnDeleted()
	{
		DevTools.OnLog -= AddEvent;
		base.OnDeleted();
	}

	/// <summary>
	/// The selected text in the log, a line per row. The rows are laid out by position rather than in order, so they're
	/// put back in order here.
	/// </summary>
	public override string GetClipboardValue( bool cut )
	{
		var selected = _output.Descendants.OfType<Label>()
			.Where( x => x.HasSelection() )
			.OrderBy( x => x.Box.Rect.Top )
			.Select( x => x.GetSelectedText() )
			.ToList();

		return selected.Count > 0 ? string.Join( "\n", selected ) : null;
	}

	void CopyAll()
	{
		var text = string.Join( "\n", _lines.Where( Matches ).Select( Format ) );
		NativeEngine.Sdl.SetClipboardText( text );
	}

	static string Format( LogEvent line ) => string.IsNullOrEmpty( line.Logger )
		? $"{line.Time:HH:mm:ss}  {line.Message}"
		: $"{line.Time:HH:mm:ss}  [{line.Logger}] {line.Message}";

	static string LevelClass( LogLevel level ) => level switch
	{
		LogLevel.Warn => "warn",
		LogLevel.Error => "error",
		LogLevel.Trace => "trace",
		_ => "info",
	};

	void AddEvent( LogEvent e )
	{
		// One row per line - the list's rows are a fixed height
		var message = e.Message ?? "";
		if ( message.IndexOfAny( ['\n', '\r'] ) < 0 )
		{
			AddLine( e );
			return;
		}

		foreach ( var part in message.Split( ['\n', '\r'], StringSplitOptions.RemoveEmptyEntries ) )
		{
			var line = e;
			line.Message = part;
			AddLine( line );
		}
	}

	void AddLine( LogEvent line )
	{
		_lines.Add( line );
		if ( Matches( line ) ) _output.AddItem( line );

		// Trim in batches, so a busy log doesn't rebuild the list every line
		if ( _lines.Count > DevTools.LogCapacity * 2 )
		{
			_lines.RemoveRange( 0, _lines.Count - DevTools.LogCapacity );
			Refilter();
		}
	}

	bool Matches( LogEvent line )
	{
		var filter = _filter.Text;
		return string.IsNullOrWhiteSpace( filter ) || (line.Message?.Contains( filter, StringComparison.OrdinalIgnoreCase ) ?? false);
	}

	void Refilter()
	{
		_output.SetItems( _lines.Where( Matches ).Select( x => (object)x ) );
		_output.TryScrollToBottom();
	}

	void Clear()
	{
		_lines.Clear();
		_output.Clear();
	}

	void Submit()
	{
		var text = _input.Text?.Trim();
		_input.Text = "";
		_input.DestroyAutoComplete();

		if ( string.IsNullOrEmpty( text ) )
			return;

		_input.AddToHistory( text );
		AddLine( new LogEvent { Message = $"> {text}", Level = LogLevel.Info, Time = DateTime.Now } );

		if ( text == "clear" ) Clear();
		else ConVarSystem.Run( text );

		_output.TryScrollToBottom();
		_input.Focus();
	}

	static object[] Complete( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return [];

		return ConVarSystem.GetAutoComplete( text, 20 )
			.Select( x => (object)new TextEntry.AutocompleteEntry { Title = $"{x.Command} - {x.Description}".Trim( '-', ' ' ), Value = x.Command } )
			.ToArray();
	}
}

/// <summary>
/// The GPU timeline, hosted in a panel. It's interactive - hover a pass for its details, freeze the frame - so it's
/// given this panel's own cursor and button, in its drawing units, rather than reading the game's mouse.
/// </summary>
internal sealed class DevToolsTimelineView : Panel
{
	float _height = 120f;
	bool _down;

	public DevToolsTimelineView()
	{
		AddClass( "overlay-view" );
		Style.Height = _height;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		if ( e.MouseButton == MouseButtons.Left ) _down = true;
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );
		if ( e.MouseButton == MouseButtons.Left ) _down = false;
	}

	public override void OnDraw( Painter painter )
	{
		var scale = ScaleToScreen;
		var area = Box.Rect.Size / scale;
		var cursor = MousePosition / scale;
		var inside = HasHovered && cursor.x >= 0 && cursor.y >= 0 && cursor.x < area.x && cursor.y < area.y;
		if ( !inside ) _down = false;

		DebugOverlay.GpuTimeline.Input = new( inside, cursor, _down, area );

		using var state = painter.Scope();
		painter.Scale( scale );

		var pos = new Vector2( 12, 12 );
		DebugOverlay.GpuTimeline.Draw( painter, ref pos );
		DebugOverlay.GpuTimeline.DrawTooltip( painter );

		var height = MathF.Max( pos.y, 40f );
		if ( MathF.Abs( height - _height ) > 0.5f )
		{
			_height = height;
			Style.Height = _height;
		}
	}
}
