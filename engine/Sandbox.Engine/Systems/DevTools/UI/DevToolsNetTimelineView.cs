using Sandbox.UI;
using static Sandbox.Diagnostics.DevTools.NetworkLog;

namespace Sandbox.Diagnostics;

/// <summary>
/// Draws network events on object, component and track lanes.
/// </summary>
internal sealed class DevToolsNetTimelineView : Panel
{
	const float LabelWidth = 230f;
	const float AxisHeight = 20f;
	const float GroupHeight = 20f;
	const float TrackHeight = 18f;
	const float ScrollbarWidth = 10f;
	const float Right = ScrollbarWidth + 6f;
	const float FoldArrowWidth = 18f;
	const double DefaultSpan = 30;
	const double MinSpan = 0.25;
	const double MaxSpan = 600;
	const double ZoomStep = 1.25;
	static readonly Color AsleepIdleColor = Color.Parse( "#3A4A66" ).Value;
	static readonly Color AsleepHiddenColor = Color.Parse( "#2A3348" ).Value;

	readonly DevToolsNetTimeline _tab;
	readonly List<Row> _rows = new();
	readonly List<(double Time, string Text)> _axisLabels = new();
	readonly Panel _tooltip;
	readonly Label[] _tooltipLines = new Label[4];
	readonly Label _tooltipTime;
	Entry? _tooltipEvent;
	string _tooltipSuffix;
	double _tooltipSeconds = double.NaN;
	double _cursorSeconds = double.NaN;
	string _cursorText;
	float _height = 120f;
	float _contentHeight;
	double _dragStartEnd;

	double _span = DefaultSpan;
	double _end;
	Vector2 _cursor;
	bool _inside;
	(Entry Event, Vector2 At)? _hovered;

	readonly record struct Row( float Y, float Height, DevToolsNetTimeline.Group Group, DevToolsNetTimeline.Track Track, string Component = null )
	{
		public bool IsComponentHeader => Component is not null;
		public bool IsObjectHeader => Track is null && Component is null;
	}

	public DevToolsNetTimelineView( DevToolsNetTimeline tab )
	{
		_tab = tab;
		AddClass( "nettl-view" );

		CanDragScroll = false;

		_tooltip = AddChild( new Panel( null, "nettl-tooltip hidden" ) );
		for ( int i = 0; i < _tooltipLines.Length; i++ )
		{
			_tooltipLines[i] = _tooltip.AddChild( new Label( "" ) );
		}
		_tooltipTime = _tooltip.AddChild( new Label( "", "faint" ) );
	}

	/// <summary>
	/// Whether the view follows new events independently of capture and pause.
	/// </summary>
	internal bool Following { get; private set; } = true;

	/// <summary>
	/// The live clock, frozen while paused.
	/// </summary>
	internal double Now { get; private set; }

	public override bool WantsDrag => true;

	internal void Follow() => Following = true;

	internal void CenterOn( double time ) => Hold( time + _span / 2 );

	void Hold( double end )
	{
		Following = false;
		_end = end;
	}

	float Scroll => ScrollOffset.y / ScaleToScreen;

	public override void OnMouseWheel( Vector2 value )
	{
		if ( !_inside )
		{
			base.OnMouseWheel( value );
			return;
		}

		var delta = value.y != 0 ? value.y : -value.x;
		if ( delta == 0 ) return;

		var span = Math.Clamp( delta > 0 ? _span * ZoomStep : _span / ZoomStep, MinSpan, MaxSpan );
		var fromRight = Math.Clamp( 1 - (_cursor.x - LabelWidth) / PlotWidth, 0, 1 );

		if ( !Following || span < _span ) Hold( _end + (span - _span) * fromRight );
		_span = span;
	}

	protected override void OnDragStart( DragEvent e )
	{
		base.OnDragStart( e );

		_dragStartEnd = _end;
	}

	protected override void OnDrag( DragEvent e )
	{
		e.StopPropagation();
		var moved = (MousePosition - e.LocalGrabPosition) / ScaleToScreen;
		Hold( _dragStartEnd - moved.x / PlotWidth * _span );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );
		if ( e.Target != this || _cursor.y < AxisHeight ) return;

		if ( _hovered is { } hit )
		{
			_tab.Pin( hit.Event.ObjectId, hit.Event.ObjectName, hit.Event.Name, hit.Event );
			return;
		}
		if ( _cursor.x >= LabelWidth ) return;

		foreach ( var row in _rows )
		{
			if ( _cursor.y < row.Y || _cursor.y >= row.Y + row.Height ) continue;

			if ( row.IsComponentHeader )
			{
				var key = (row.Group.Id, row.Component);
				if ( !_tab.CollapsedComponents.Remove( key ) ) _tab.CollapsedComponents.Add( key );
			}
			else if ( row.IsObjectHeader && _cursor.x < FoldArrowWidth )
			{
				if ( !_tab.Collapsed.Remove( row.Group.Id ) ) _tab.Collapsed.Add( row.Group.Id );
			}
			else
			{
				_tab.Pin( row.Group.Id, row.Group.Name, row.Track?.Name );
			}

			return;
		}
	}

	float PlotWidth => MathF.Max( 40, Box.Rect.Width / ScaleToScreen - LabelWidth - Right );

	float TimeToX( double time, double start, float plotWidth ) => LabelWidth + (float)((time - start) / _span) * plotWidth;

	double CursorTime( double start, float plotWidth ) => start + (_cursor.x - LabelWidth) / plotWidth * _span;

	public override void OnDraw( Painter painter )
	{
		var scale = ScaleToScreen;
		var area = Box.Rect.Size / scale;

		_height = area.y;
		_cursor = MousePosition / scale;
		_inside = UISystem?.Input.Hovered == this && _cursor.x >= 0 && _cursor.y >= 0 && _cursor.x < area.x && _cursor.y < area.y;

		if ( !DevTools.Paused ) Now = DevTools.NetworkLog.Now;
		if ( Following ) _end = Now;

		using var state = painter.Scope();
		painter.Scale( scale );
		painter.Stroke = Stroke.None;

		var plotW = PlotWidth;
		var start = _end - _span;

		_rows.Clear();
		_hovered = null;

		var y = AxisHeight + 4 - Scroll;

		if ( _tab.Groups.Count == 0 )
		{
			var empty = _tab.HasEvents ? "Nothing matches the filter and shown events"
				: LogsMessages ? "No sync vars or RPCs yet - they're recorded while the Network tab is open"
				: "Not capturing - press record in the toolbar";
			Text( painter, empty, new Rect( 10, y, area.x - 20, 20 ), TextFlag.LeftCenter, DevToolsStyles.Faint );
			y += 24;
		}

		painter.Fill = DevToolsStyles.Faint.WithAlpha( 0.5f );
		foreach ( var e in _tab.SessionEvents )
		{
			if ( e.Seconds < start || e.Seconds > _end ) continue;
			painter.Rect( new Rect( TimeToX( e.Seconds, start, plotW ), AxisHeight, 1, _height - AxisHeight ) );
		}

		y = DrawGroups( painter, y, start, plotW );

		painter.Fill = DevToolsStyles.Plot;
		painter.Rect( new Rect( 0, 0, area.x, AxisHeight ) );
		DrawAxis( painter, start, plotW );

		if ( _inside && _cursor.x >= LabelWidth && _cursor.x < LabelWidth + plotW )
		{
			painter.Fill = DevToolsStyles.Text.WithAlpha( 0.35f );
			painter.Rect( new Rect( _cursor.x, AxisHeight, 1, MathF.Min( y, _height ) - AxisHeight ) );
			var cursorSeconds = Math.Round( CursorTime( start, plotW ) - Now, 2 );
			if ( cursorSeconds != _cursorSeconds )
			{
				_cursorSeconds = cursorSeconds;
				_cursorText = $"{cursorSeconds:0.00}s";
			}
			Text( painter, _cursorText, new Rect( _cursor.x + 4, 2, 80, 14 ), TextFlag.LeftCenter, DevToolsStyles.Text );
		}

		var contentHeight = y + Scroll - AxisHeight + 6;
		if ( contentHeight != _contentHeight )
		{
			_contentHeight = contentHeight;
			SetNeedsFinalLayout();
		}

		UpdateTooltip( area );
	}

	protected override void FinalLayoutChildren( Vector2 offset )
	{
		_tooltip.FinalLayout( Box.Rect.Position );

		var rect = Box.Rect;
		rect.Position -= ScrollOffset;
		rect.Height = MathF.Max( rect.Height, (AxisHeight + _contentHeight) * ScaleToScreen );
		ConstrainScrolling( rect.Size );
	}

	bool Visible( float y, float height ) => y + height > AxisHeight && y < _height;
	bool HoveredRow( float y, float height ) => _inside && _cursor.y >= MathF.Max( y, AxisHeight ) && _cursor.y < y + height;

	float DrawGroups( Painter painter, float y, double start, float plotWidth )
	{
		foreach ( var group in _tab.Groups )
		{
			var collapsed = _tab.Collapsed.Contains( group.Id );
			if ( Visible( y, GroupHeight ) )
			{
				DrawRowLabel( painter, new Row( y, GroupHeight, group, null ), collapsed ? group.CollapsedTitle : group.ExpandedTitle, 6, true, 600 );
				if ( collapsed )
				{
					foreach ( var track in group.Tracks )
					{
						DrawEvents( painter, track, y, GroupHeight, start, plotWidth, summary: true );
					}
				}
			}
			y += collapsed ? GroupHeight + 2 : GroupHeight;
			if ( !collapsed ) y = DrawTracks( painter, group, y, start, plotWidth ) + 4;
		}
		return y;
	}

	float DrawTracks( Painter painter, DevToolsNetTimeline.Group group, float y, double start, float plotWidth )
	{
		string component = null;
		var componentCollapsed = false;
		for ( int i = 0; i < group.Tracks.Count; i++ )
		{
			var track = group.Tracks[i];
			var grouped = _tab.GroupByComponent && track.Component is not null;
			if ( grouped && track.Component != component )
			{
				component = track.Component;
				componentCollapsed = _tab.CollapsedComponents.Contains( (group.Id, component) );
				if ( Visible( y, TrackHeight ) )
				{
					DrawRowLabel( painter, new Row( y, TrackHeight, group, null, component ), componentCollapsed ? track.ComponentCollapsed : track.ComponentExpanded, 18, HoveredRow( y, TrackHeight ), 600 );
					if ( componentCollapsed )
					{
						for ( int j = i; j < group.Tracks.Count && group.Tracks[j].Component == component; j++ )
						{
							DrawEvents( painter, group.Tracks[j], y, TrackHeight, start, plotWidth, summary: true );
						}
					}
				}
				y += TrackHeight;
			}
			if ( grouped && componentCollapsed ) continue;
			if ( Visible( y, TrackHeight ) )
			{
				var hot = HoveredRow( y, TrackHeight );
				painter.Fill = hot ? DevToolsStyles.Raised : DevToolsStyles.Line.WithAlpha( 0.35f );
				painter.Rect( new Rect( LabelWidth, y + TrackHeight / 2, plotWidth, 1 ) );
				var indent = grouped ? 32 : 18;
				DrawRowLabel( painter, new Row( y, TrackHeight, group, track ), grouped ? track.GroupedLabel : track.Label, indent, hot );
				DrawEvents( painter, track, y, TrackHeight, start, plotWidth, summary: false );
			}
			y += TrackHeight;
		}
		return y;
	}

	void DrawRowLabel( Painter painter, Row row, string label, float indent, bool hot, int weight = 500 )
	{
		_rows.Add( row );
		Text( painter, label, new Rect( indent, row.Y, LabelWidth - indent - 4, row.Height ), TextFlag.LeftCenter, hot ? DevToolsStyles.Text : DevToolsStyles.Dim, weight );
	}

	void DrawAxis( Painter painter, double start, float plotW )
	{
		painter.Fill = DevToolsStyles.Line;
		painter.Rect( new Rect( LabelWidth, AxisHeight, plotW, 1 ) );

		var now = Now;
		var step = GraphAxis.TickStep( _span, plotW, 90 );
		var index = 0;
		for ( var t = Math.Ceiling( (start - now) / step ) * step; t <= _end - now + 1e-6; t += step )
		{
			var px = TimeToX( now + t, start, plotW );
			painter.Fill = DevToolsStyles.Line;
			painter.Rect( new Rect( px, AxisHeight - 4, 1, 4 ) );
			if ( index == _axisLabels.Count ) _axisLabels.Add( (double.NaN, null) );
			if ( _axisLabels[index].Time != t )
			{
				_axisLabels[index] = (t, Math.Abs( t ) < step / 2 ? "now" : $"{t:0.##}s");
			}
			Text( painter, _axisLabels[index++].Text, new Rect( px - 40, 2, 80, 14 ), TextFlag.CenterTop, DevToolsStyles.Faint );
		}
	}

	void DrawEvents( Painter painter, DevToolsNetTimeline.Track track, float y, float height, double start, float plotWidth, bool summary )
	{
		if ( track.Kind == Kind.Dormancy )
		{
			DrawDormancy( painter, track, y, height, start, plotWidth, summary );
			return;
		}

		var events = track.Events;
		int lo = 0;
		int hi = events.Count;
		while ( lo < hi )
		{
			var mid = (lo + hi) / 2;
			if ( events[mid].Seconds < start ) lo = mid + 1; else hi = mid;
		}

		var midY = y + height / 2;
		var thin = track.Kind is Kind.SyncVar or Kind.Transform;
		var (width, markHeight) = summary ? (2f, 8f) : thin ? (3f, 12f) : (7f, 7f);
		var hoverable = !summary && HoveredRow( y, height );

		Span<float> lastX = stackalloc float[4];
		lastX.Fill( float.NegativeInfinity );
		for ( int i = lo; i < events.Count; i++ )
		{
			var e = events[i];
			if ( e.Seconds > _end ) break;

			var px = TimeToX( e.Seconds, start, plotWidth );
			var filled = e.Outbound || summary || e.Kind == Kind.Session;
			var look = (filled ? 1 : 0) | (e.Resend ? 2 : 0);
			var mark = new Rect( px - width / 2, midY - markHeight / 2, width, markHeight );

			if ( px - lastX[look] >= 1 )
			{
				lastX[look] = px;
				var color = track.Color.WithAlpha( e.Resend ? 0.3f : summary ? 0.7f : 1f );
				painter.Fill = filled ? color : Color.Transparent;
				painter.Stroke = filled ? Stroke.None : Stroke.Solid( color, 1.5f );
				painter.Rect( mark );
				painter.Stroke = Stroke.None;
			}

			if ( _tab.Selected == e )
			{
				painter.Fill = Color.Transparent;
				painter.Stroke = Stroke.Solid( DevToolsStyles.Text, 1 );
				painter.Rect( mark.Grow( 2 ) );
				painter.Stroke = Stroke.None;
			}

			if ( hoverable && MathF.Abs( _cursor.x - px ) <= 4 )
			{
				_hovered = (e, new Vector2( px, midY ));
			}
		}
	}

	static Color DormancyColor( DevTools.NetDormancy state ) => state switch
	{
		DevTools.NetDormancy.AsleepIdle => AsleepIdleColor,
		DevTools.NetDormancy.AsleepHidden => AsleepHiddenColor,
		DevTools.NetDormancy.Grace => DevToolsStyles.Warning.WithAlpha( 0.5f ),
		_ => Color.Transparent,
	};

	void DrawDormancy( Painter painter, DevToolsNetTimeline.Track track, float y, float height, double start, float plotWidth, bool summary )
	{
		var events = track.Events;
		var top = y + 3;
		var bandH = height - 6;
		var cursorT = CursorTime( start, plotWidth );
		var hoveredRow = !summary && _cursor.x >= LabelWidth && HoveredRow( y, height );

		for ( int i = 0; i < events.Count; i++ )
		{
			var e = events[i];
			var until = i + 1 < events.Count ? events[i + 1].Seconds : Now;
			if ( until < start ) continue;
			if ( e.Seconds > _end ) break;

			var x0 = TimeToX( Math.Max( e.Seconds, start ), start, plotWidth );
			var x1 = TimeToX( Math.Min( until, _end ), start, plotWidth );
			var color = DormancyColor( e.Dormancy ).WithAlpha( summary ? 0.5f : 1f );

			if ( e.Dormancy != DevTools.NetDormancy.Awake )
			{
				var grace = e.Dormancy == DevTools.NetDormancy.Grace;
				painter.Fill = color;
				painter.Rect( new Rect( x0, grace ? y + height / 2 - 1 : top, MathF.Max( 1, x1 - x0 ), grace ? 2 : bandH ) );
			}

			if ( e.Seconds >= start && !summary )
			{
				painter.Fill = (_tab.Selected == e ? DevToolsStyles.Text : DevToolsStyles.Dim).WithAlpha( 0.9f );
				painter.Rect( new Rect( x0 - 0.5f, top - 1, 1, bandH + 2 ) );
			}

			if ( hoveredRow && cursorT >= e.Seconds && cursorT < until )
			{
				_hovered = (e, new Vector2( _cursor.x, y + height / 2 ));
			}
		}
	}

	void UpdateTooltip( Vector2 area )
	{
		_tooltip.SetClass( "hidden", _hovered is null );
		if ( _hovered is not { } hover ) return;

		var e = hover.Event;
		if ( _tooltipEvent != e ) SetTooltipLines( e );

		var seconds = e.Seconds - Now;
		if ( seconds != _tooltipSeconds )
		{
			_tooltipSeconds = seconds;
			_tooltipTime.Text = $"{seconds:0.000}s{_tooltipSuffix}";
		}

		var size = _tooltip.Box.Rect.Size / ScaleToScreen;
		var x = hover.At.x + 10 + size.x > area.x ? hover.At.x - size.x - 10 : hover.At.x + 10;
		x = Math.Clamp( x, 2, MathF.Max( 2, area.x - size.x - 2 ) );
		var y = Math.Clamp( hover.At.y - size.y / 2, 2, MathF.Max( 2, area.y - size.y - 2 ) );
		_tooltip.Style.Left = x;
		_tooltip.Style.Top = y;
		var maxWidth = MathF.Max( 40, MathF.Min( 520, area.x - 4 ) );
		_tooltip.Style.MaxWidth = maxWidth;
		_tooltip.Style.MinWidth = MathF.Min( 260, maxWidth );
	}

	void SetTooltipLines( Entry e )
	{
		_tooltipEvent = e;
		_tooltipSeconds = double.NaN;
		_tooltipSuffix = e.Kind switch
		{
			Kind.Snapshot or Kind.Object => "",
			Kind.Session => string.IsNullOrEmpty( e.Peer ) ? "" : $" - {e.Peer}",
			_ => $" - {e.ObjectName}",
		};

		var line = 0;
		if ( e.Kind == Kind.Dormancy )
		{
			SetLine( line++, "Dormancy", false );
		}
		else if ( e.Kind != Kind.Session )
		{
			SetLine( line++, DevTools.NetworkLog.TrackLabel( e.Name ), false );
			var kind = e.Kind switch
			{
				Kind.Snapshot => "scene snapshot",
				Kind.Object => "object message",
				Kind.SyncVar => e.Resend ? "sync, same value" : "sync change",
				Kind.Transform => "transform",
				_ => "RPC",
			};
			SetLine( line++, $"{(e.Outbound ? "Sent" : "Received")} - {kind}", true );
		}
		if ( e.Kind is not (Kind.Snapshot or Kind.Object) ) SetLine( line++, e.Value, false );
		if ( e.Kind is not (Kind.Dormancy or Kind.Session) )
		{
			SetLine( line++, $"{(e.Outbound ? "to" : "from")} {e.Peer} - {DevToolsFormat.Bytes( e.Size )}", true );
		}

		while ( line < _tooltipLines.Length ) SetLine( line++, null, false );
	}

	void SetLine( int index, string text, bool dim )
	{
		var label = _tooltipLines[index];
		label.Text = text;
		label.SetClass( "hidden", string.IsNullOrEmpty( text ) );
		label.SetClass( "dim", dim );
	}

	static void Text( Painter painter, string text, Rect rect, TextFlag flags, Color color, int weight = 500 )
	{
		var scope = new TextRendering.Scope( text, color, 11, "Roboto Mono", weight );
		DebugOverlay.DrawText( painter, scope, rect, flags );
	}
}
