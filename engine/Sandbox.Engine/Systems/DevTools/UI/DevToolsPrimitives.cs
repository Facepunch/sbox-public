using Sandbox.UI;

namespace Sandbox.Diagnostics;

/// <summary>
/// A header band - a title, an optional summary on the right - then the content under it.
/// </summary>
internal sealed class DevToolsSection : Panel
{
	public Panel Head { get; }
	public Label TitleLabel { get; }
	public Label Aside { get; }
	public Panel Body { get; }

	public DevToolsSection( string title, string classes = null )
	{
		AddClass( "section" );
		if ( classes is not null ) AddClass( classes );

		Head = AddChild( new Panel( null, "section-head" ) );
		TitleLabel = Head.AddChild( new Label( title, "section-title" ) );
		Aside = Head.AddChild( new Label( "", "section-aside" ) );
		Body = AddChild( new Panel( null, "section-body" ) );
	}

	/// <summary>
	/// Clicking the band opens another tab.
	/// </summary>
	public DevToolsSection LinkTo( string tab )
	{
		AddClass( "link" );
		Head.AddChild( new Label( "chevron_right", "section-link" ) );
		Head.AddEventListener( "onclick", () => DevTools.ActiveTab = tab );
		return this;
	}

	/// <summary>
	/// Open and close from the band. Closed to start with; the content is made the first time it opens.
	/// </summary>
	public DevToolsSection Collapsible( Action<Panel> build )
	{
		AddClass( "collapsible" );
		AddClass( "collapsed" );

		var chevron = new Label( "chevron_right", "section-chevron" );
		Head.AddChild( chevron );
		Head.SetChildIndex( chevron, 0 );

		var built = false;
		Head.AddEventListener( "onclick", () =>
		{
			if ( !built )
			{
				built = true;
				build( Body );
			}

			SetClass( "collapsed", !HasClass( "collapsed" ) );
			chevron.Text = IsCollapsed ? "chevron_right" : "expand_more";
		} );

		return this;
	}

	public bool IsCollapsed => HasClass( "collapsed" );

	/// <summary>
	/// Pad the body, for content that isn't a table.
	/// </summary>
	public DevToolsSection Padded()
	{
		Body.AddClass( "padded" );
		return this;
	}
}

/// <summary>
/// Label and value pairs in a grid of cells - the facts a page is about, before the detail.
/// </summary>
internal sealed class DevToolsFacts : Panel
{
	readonly Dictionary<string, (Panel Cell, Label Value)> _facts = new();

	public DevToolsFacts()
	{
		AddClass( "facts" );
	}

	/// <summary>
	/// Set a fact, adding it the first time. Facts stay in the order they were first set.
	/// </summary>
	public void Set( string key, string value, DevTools.Severity? severity = null, string tooltip = null )
	{
		if ( !_facts.TryGetValue( key, out var fact ) )
		{
			var cell = AddChild( new Panel( null, "fact" ) );
			cell.AddChild( new Label( key, "fact-key" ) );
			fact = (cell, cell.AddChild( new Label( "", "fact-value" ) ));
			_facts[key] = fact;
		}

		fact.Cell.SetClass( "hidden", false );
		fact.Cell.Tooltip = tooltip;
		fact.Value.Text = value;
		fact.Value.SetClass( "warning", severity == DevTools.Severity.Warning );
		fact.Value.SetClass( "bad", severity == DevTools.Severity.Bad );
	}

	/// <summary>
	/// Hide a fact that doesn't apply right now.
	/// </summary>
	public void Hide( string key )
	{
		if ( _facts.TryGetValue( key, out var fact ) ) fact.Cell.SetClass( "hidden", true );
	}
}

/// <summary>
/// A table made of grid rows that share one column definition. Rows are keyed, so a refresh updates them in place:
/// call <see cref="Begin"/>, then <see cref="Row"/> and <see cref="Group"/> in order, then <see cref="End"/>.
/// </summary>
internal sealed class DevToolsTable : Panel
{
	/// <summary>
	/// One cell: its text and the classes it takes - "number", "note", a severity. A cell with a
	/// <see cref="Bar"/> draws a bar filled to that fraction instead of text; <see cref="Indent"/> pushes the text in,
	/// for a tree.
	/// </summary>
	internal readonly record struct Cell( string Text, string Class = null, Color? Swatch = null, float? Bar = null, float Indent = 0 )
	{
		public static implicit operator Cell( string text ) => new( text );
	}

	readonly string _columns;
	readonly Panel _header;
	readonly Dictionary<string, RowPanel> _rows = new();
	readonly List<RowPanel> _order = new();
	readonly HashSet<string> _seen = new();
	readonly Label _empty;

	sealed class CellPanel : Panel
	{
		public readonly Panel Swatch;
		public readonly Label Text;
		public readonly Panel Bar;
		public readonly Panel Fill;
		public string ClassKey;
		public float Indent;

		public CellPanel()
		{
			AddClass( "cell" );
			Swatch = AddChild( new Panel( null, "swatch hidden" ) );
			Text = AddChild( new Label( "", "cell-text" ) );
			Bar = AddChild( new Panel( null, "cell-bar hidden" ) );
			Fill = Bar.AddChild( new Panel( null, "cell-bar-fill" ) );
		}
	}

	sealed class RowPanel : Panel
	{
		public readonly List<CellPanel> Cells = new();
		public Action Click;
		public string ClassKey;

		public RowPanel()
		{
			AddEventListener( "onclick", () => Click?.Invoke() );
		}
	}

	/// <param name="columns">The grid-template-columns every row uses.</param>
	/// <param name="headers">Column titles; a title starting with '#' is a number column, right aligned.</param>
	public DevToolsTable( string columns, params string[] headers )
	{
		AddClass( "table" );
		_columns = columns;

		if ( headers.Length > 0 )
		{
			_header = AddChild( new Panel( null, "data-header" ) );
			_header.Style.Set( "grid-template-columns", _columns );
			for ( int i = 0; i < headers.Length; i++ )
			{
				var h = headers[i];
				var numeric = h.StartsWith( '#' );
				var cell = _header.AddChild( new Label( numeric ? h[1..] : h, numeric ? "cell number" : "cell" ) );
				if ( i == 0 ) cell.AddClass( "first" );
			}
		}

		_empty = AddChild( new Label( "", "quiet hidden" ) );
	}

	/// <summary>
	/// The header row is the table's title band: the first column names the table, the rest name their columns.
	/// </summary>
	public DevToolsTable Banded()
	{
		AddClass( "banded" );
		return this;
	}

	public void Begin()
	{
		_seen.Clear();
		_order.Clear();
	}

	/// <summary>
	/// A heading row that spans the table.
	/// </summary>
	public void Group( string key, string title ) => Row( $"group:{key}", [new Cell( title )], rowClass: "group" );

	public void Row( string key, ReadOnlySpan<Cell> cells, Action onClick = null, string tooltip = null, string rowClass = null )
	{
		if ( !_rows.TryGetValue( key, out var row ) )
		{
			row = AddChild( new RowPanel() );
			row.AddClass( "data-row" );
			row.Style.Set( "grid-template-columns", _columns );
			_rows[key] = row;
		}

		_seen.Add( key );
		_order.Add( row );

		while ( row.Cells.Count < cells.Length )
			row.Cells.Add( row.AddChild( new CellPanel() ) );

		for ( int i = 0; i < row.Cells.Count; i++ )
		{
			var panel = row.Cells[i];
			var shown = i < cells.Length;
			panel.SetClass( "hidden", !shown );
			if ( !shown ) continue;

			var cell = cells[i];
			var classes = $"{cell.Class}{(cell.Swatch is not null ? " swatched" : "")}";
			if ( panel.ClassKey != classes )
			{
				panel.ClassKey = classes;
				panel.Classes = $"cell {classes}";
			}

			panel.Text.Text = cell.Text ?? "";
			if ( panel.Indent != cell.Indent )
			{
				panel.Indent = cell.Indent;
				panel.Style.PaddingLeft = cell.Indent;
			}

			panel.Text.SetClass( "hidden", cell.Bar is not null );
			panel.Swatch.SetClass( "hidden", cell.Swatch is null );
			if ( cell.Swatch is { } c ) panel.Swatch.Style.BackgroundColor = c;

			panel.Bar.SetClass( "hidden", cell.Bar is null );
			if ( cell.Bar is { } fraction ) panel.Fill.Style.Width = Length.Percent( Math.Clamp( fraction, 0, 1 ) * 100f );
		}

		row.Click = onClick;
		row.SetClass( "link", onClick is not null );
		row.Tooltip = tooltip;

		if ( row.ClassKey != rowClass )
		{
			if ( row.ClassKey is not null ) row.RemoveClass( row.ClassKey );
			if ( rowClass is not null ) row.AddClass( rowClass );
			row.ClassKey = rowClass;
		}
	}

	/// <summary>
	/// Hide the rows that weren't set this time, and put the rest in the order they were set.
	/// </summary>
	public void End( string emptyText = null )
	{
		foreach ( var (key, row) in _rows )
			row.SetClass( "hidden", !_seen.Contains( key ) );

		for ( int i = 0; i < _order.Count; i++ )
			_order[i].SetClass( "last", i == _order.Count - 1 );

		// Keep the children in the order the rows were set, so a re-ranked list shows in its new order
		var index = new Dictionary<Panel, int>( _order.Count );
		for ( int i = 0; i < _order.Count; i++ ) index[_order[i]] = i;

		var inOrder = true;
		var last = -1;
		foreach ( var child in Children )
		{
			if ( !index.TryGetValue( child, out var at ) ) continue;
			if ( at < last ) { inOrder = false; break; }
			last = at;
		}

		if ( !inOrder )
		{
			SortChildren( ( a, b ) =>
			{
				var ia = a == _header ? -2 : a == _empty ? -1 : index.TryGetValue( a, out var x ) ? x : int.MaxValue;
				var ib = b == _header ? -2 : b == _empty ? -1 : index.TryGetValue( b, out var y ) ? y : int.MaxValue;
				return ia.CompareTo( ib );
			} );
		}

		_empty.Text = emptyText ?? "";
		_empty.SetClass( "hidden", _order.Count > 0 || emptyText is null );
	}

	/// <summary>
	/// A table of costs, biggest first: name, average, max, and a bar for its share of <c>total</c>.
	/// </summary>
	public static DevToolsTable ForCosts( string title, bool compact = false ) => compact
		? new DevToolsTable( "minmax(0, 1fr) 46px 40px", title, "#avg", "" ) { _compact = true }.Banded()
		: new DevToolsTable( "minmax(0, 1fr) 52px 52px 56px", title, "#avg", "#max", "" ).Banded();

	/// <summary>
	/// A cost table without the max column, for a narrow column.
	/// </summary>
	bool _compact;

	public void SetCosts( IReadOnlyList<DevTools.Cost> costs, int limit, float total, string tab, string empty, bool swatches = false )
	{
		Begin();
		for ( int i = 0; i < Math.Min( limit, costs.Count ); i++ )
		{
			var c = costs[i];
			Cell[] cells = [new( c.Name, null, swatches ? c.Color : (Color?)null ), Number( DevToolsFormat.Ms( c.AvgMs ) ), new( DevToolsFormat.Ms( c.MaxMs ), "number dim" ), BarOf( total > 0 ? c.AvgMs / total : 0 )];
			if ( _compact ) cells = [cells[0], cells[1], cells[3]];
			Row( c.Name, cells, tab is null ? null : () => DevTools.ActiveTab = tab, c.Tooltip ?? $"{c.Name}: {c.AvgMs:0.00} ms avg, {c.MaxMs:0.00} ms max" );
		}
		End( empty );
	}

	/// <summary>
	/// A number cell, coloured when it's out of line.
	/// </summary>
	public static Cell Number( string text, DevTools.Severity? severity = null ) => new( text, severity switch
	{
		DevTools.Severity.Warning => "number warning",
		DevTools.Severity.Bad => "number bad",
		_ => "number",
	} );

	/// <summary>
	/// A bar filled to <paramref name="fraction"/>, coloured when it's out of line.
	/// </summary>
	public static Cell BarOf( double fraction, DevTools.Severity? severity = null ) => new( null, severity switch
	{
		DevTools.Severity.Warning => "warning",
		DevTools.Severity.Bad => "bad",
		_ => null,
	}, null, (float)fraction );

	/// <summary>
	/// A dimmed cell that says what's behind the number next to it. Dropped when the dev tools is narrow.
	/// </summary>
	public static Cell Note( string text ) => new( text, "note hide-narrow" );
}

/// <summary>
/// What's out of line, one line each: what it is, the numbers, and where to look. Nothing when there's nothing.
/// </summary>
internal sealed class DevToolsFindings : Panel
{
	readonly List<Row> _rows = new();
	readonly Label _more;
	bool _showAll;

	/// <summary>
	/// How many to show before the rest fold away.
	/// </summary>
	public int Expanded { get; set; } = 4;

	sealed class Row : Panel
	{
		public readonly Label Title, Detail, Link;
		public string Tab;

		public Row()
		{
			AddClass( "finding" );
			AddChild( new Panel( null, "finding-mark" ) );
			Title = AddChild( new Label( "", "finding-title" ) );
			Detail = AddChild( new Label( "", "finding-detail" ) );
			Link = AddChild( new Label( "", "finding-link" ) );
			AddEventListener( "onclick", () =>
			{
				if ( Tab is not null && DevTools.ShowAdvanced ) DevTools.ActiveTab = Tab;
			} );
		}
	}

	public DevToolsFindings()
	{
		AddClass( "findings" );
		_more = AddChild( new Label( "", "findings-more" ) );
		_more.AddEventListener( "onclick", () => _showAll = !_showAll );
	}

	public void Set( IReadOnlyList<DevTools.Issue> issues )
	{
		while ( _rows.Count < issues.Count )
		{
			var row = new Row();
			AddChild( row );
			_rows.Add( row );
		}

		var shown = _showAll ? issues.Count : Math.Min( issues.Count, Expanded );

		for ( int i = 0; i < _rows.Count; i++ )
		{
			var row = _rows[i];
			row.SetClass( "hidden", i >= shown );
			if ( i >= issues.Count ) continue;

			var issue = issues[i];
			var linked = issue.Tab is not null && DevTools.ShowAdvanced && issue.Tab != DevTools.ActiveTab;
			row.Tab = issue.Tab;
			row.SetClass( "link", linked );
			row.Title.Text = issue.Title;
			row.Detail.Text = issue.Detail ?? "";
			row.Detail.Tooltip = issue.Detail;
			row.Link.Text = linked ? TabTitle( issue.Tab ) : "";

			foreach ( var s in Enum.GetValues<DevTools.Severity>() )
				row.SetClass( DevToolsStyles.ClassFor( s ), issue.Severity == s );
		}

		SetClass( "hidden", issues.Count == 0 );

		_more.SetClass( "hidden", issues.Count <= Expanded );
		_more.Text = _showAll ? "fewer" : $"+{issues.Count - Expanded} more";

		// The "more" line sits after the rows
		if ( GetChildIndex( _more ) != ChildrenCount - 1 )
			SortChildren( ( a, b ) => (a == _more ? 1 : 0).CompareTo( b == _more ? 1 : 0 ) );
	}

	static string TabTitle( string tab ) => tab switch
	{
		"cpu" => "CPU",
		"gpu" => "GPU",
		"ui" => "UI",
		_ => char.ToUpperInvariant( tab[0] ) + tab[1..],
	};
}

/// <summary>
/// How the frame times in the analysis window are spread: a bar per bucket, the target as a line, and the buckets
/// past the hitch threshold in red. A tight spike is even pacing; a long tail is stutter.
/// </summary>
internal sealed class DevToolsHistogram : Panel
{
	const int Buckets = 48;
	readonly int[] _counts = new int[Buckets];

	/// <summary>
	/// The axis labels under the bars. Off for a chart too small to read them.
	/// </summary>
	public bool ShowLabels { get; set; } = true;

	public DevToolsHistogram()
	{
		AddClass( "histogram" );
	}

	public override void OnDraw( Painter painter )
	{
		var frames = DevTools.ShownFrames;
		var summary = DevTools.Current;
		var scale = ScaleToScreen;
		var w = Box.Rect.Width / scale;
		var h = Box.Rect.Height / scale;
		if ( w < 8 || h < 8 || summary.Frames == 0 ) return;

		using var state = painter.Scope();
		painter.Scale( scale );
		painter.Stroke = Stroke.None;

		var (Left, Right, Top, Bottom) = ShowLabels ? (10f, 10f, 6f, 16f) : (0f, 0f, 1f, 1f);
		var plotW = w - Left - Right;
		var plotH = h - Top - Bottom;
		var target = summary.TargetMs > 0 ? summary.TargetMs : 16.67f;
		var range = MathF.Max( summary.FrameP99 * 1.3f, target * 1.4f );

		Array.Clear( _counts );
		var n = Math.Min( summary.Frames, frames.Count );
		for ( int i = 0; i < n; i++ )
			_counts[Math.Clamp( (int)(frames.FrameMs( i ) / range * Buckets), 0, Buckets - 1 )]++;

		var most = 1;
		foreach ( var c in _counts ) most = Math.Max( most, c );

		var barW = plotW / Buckets;
		for ( int b = 0; b < Buckets; b++ )
		{
			if ( _counts[b] == 0 ) continue;

			// Square root, so a handful of slow frames still shows next to hundreds of good ones
			var bh = MathF.Max( 1, MathF.Sqrt( _counts[b] / (float)most ) * plotH );
			var ms = (b + 0.5f) * range / Buckets;
			painter.Fill = ms >= summary.HitchMs ? DevToolsStyles.Bad : DevToolsStyles.Cpu.WithAlpha( 0.8f );
			painter.Rect( new Rect( Left + b * barW, Top + plotH - bh, MathF.Max( 1, barW - 1 ), bh ) );
		}

		painter.Fill = DevToolsStyles.Line;
		painter.Rect( new Rect( Left, Top + plotH, plotW, 1 ) );

		var tx = Left + target / range * plotW;
		painter.Fill = DevToolsStyles.Text.WithAlpha( 0.6f );
		for ( float y = Top; y < Top + plotH; y += 6 )
			painter.Rect( new Rect( tx, y, 1, 3 ) );

		if ( !ShowLabels ) return;

		Label( painter, "0", Left, TextFlag.LeftTop, DevToolsStyles.Faint, Top + plotH + 2 );
		Label( painter, $"{target:0.#}", tx, TextFlag.CenterTop, DevToolsStyles.Text, Top + plotH + 2 );
		Label( painter, $"{range:0.#}+ ms", Left + plotW, TextFlag.RightTop, DevToolsStyles.Faint, Top + plotH + 2 );
	}

	static void Label( Painter painter, string text, float x, TextFlag flags, Color color, float y )
	{
		var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", 500 );
		var rect = flags.HasFlag( TextFlag.Right ) ? new Rect( x - 80, y, 80, 14 ) : flags.HasFlag( TextFlag.CenterHorizontally ) ? new Rect( x - 40, y, 80, 14 ) : new Rect( x, y, 80, 14 );
		DebugOverlay.DrawText( painter, scope, rect, flags );
	}
}
