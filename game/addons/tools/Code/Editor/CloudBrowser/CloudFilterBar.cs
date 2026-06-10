namespace Editor;

/// <summary>
/// The full-width bar that sits between the nav bar and the list header.
/// Left side: <see cref="CloudFilterPills"/> — one pill per available facet dimension.
/// Right side: OrderMode + ViewMode toolbar buttons, always visible.
/// </summary>
class CloudFilterBar : Widget
{
	/// <summary>Exposed so FetchPackages can set up the order-mode context menu.</summary>
	public ToolButton OrderMode { get; }

	/// <summary>Exposed so ViewModeType setter can update the icon.</summary>
	public ToolButton ViewMode { get; }

	readonly CloudFilterPills _pills;

	public CloudFilterBar( ActiveFilterSet filters, Action onChanged, Widget parent ) : base( parent )
	{
		FixedHeight = 36;
		Layout = Layout.Row();
		Layout.Spacing = 2;
		Layout.Margin = new Margin( 6, 2, 6, 0 );

		_pills = new CloudFilterPills( filters, onChanged, this );
		Layout.Add( _pills, 1 );

		Layout.AddSpacingCell( 6 );

		OrderMode = new ToolButton( "Order Mode", "emoji_events", this );
		OrderMode.FixedSize = Theme.RowHeight;
		Layout.Add( OrderMode );

		ViewMode = new ToolButton( "View Mode\n(ctrl + mouse wheel)", "grid_view", this );
		ViewMode.FixedSize = Theme.RowHeight;
		Layout.Add( ViewMode );
	}

	public void SetFacets( IEnumerable<Package.Facet> facets ) => _pills.SetFacets( facets );
	public void ClearFacets() => _pills.ClearFacets();

	/// <summary>Repaint the pills (e.g. after an ActiveFilter toggle).</summary>
	public void RefreshPills() => _pills.Update();

	protected override void OnPaint()
	{
		const float hm = 12f; // horizontal margin for separator lines

		// Top separator
		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.35f ) );
		Paint.DrawRect( new Rect( hm, 0, LocalRect.Width - hm * 2, 1 ) );
	}
}

/// <summary>
/// Inner widget that owns and paints the facet-pill row.
/// Separated from <see cref="CloudFilterBar"/> so the buttons can live alongside it
/// in a normal row layout without fighting the custom painting.
/// </summary>
class CloudFilterPills : Widget
{
	record struct Pill( Package.Facet Facet, Rect Bounds, Rect IconBounds, bool HasActive );

	readonly ActiveFilterSet _filters;
	readonly Action _onChanged;
	List<Package.Facet> _facets = new();
	List<Pill> _pills = new();
	int _hoveredPill = -1;
	bool _hoveredIcon = false;

	const float LeftPad = 10f; // padding before facet icon
	const float RightPad = 6f;
	const float IconW = 16f; // right icon (expand / close)
	const float PillGap = 6f;

	public CloudFilterPills( ActiveFilterSet filters, Action onChanged, Widget parent ) : base( parent )
	{
		_filters = filters;
		_onChanged = onChanged;
		MouseTracking = true;
	}

	public void SetFacets( IEnumerable<Package.Facet> facets )
	{
		_facets = facets
			?.Where( f => f.Entries.Any( e => e.Name is not "game" and not "library" ) )
			.DistinctBy( f => f.Title ) // dedupe by display name — API sometimes returns two "Size" facets
			.ToList() ?? new();
		Update();
	}

	public void ClearFacets()
	{
		_facets.Clear();
		_pills.Clear();
		Update();
	}

	protected override void OnPaint()
	{
		_pills.Clear();

		if ( _facets.Count == 0 )
		{
			// Hint text when no context is selected yet
			Paint.SetPen( Theme.TextLight.WithAlpha( 0.3f ) );
			Paint.SetDefaultFont();
			Paint.DrawText( LocalRect.Shrink( 10, 0 ), "Select a category to see filters", TextFlag.LeftCenter );
			return;
		}

		float x = 4f;
		float h = Theme.RowHeight;

		foreach ( var facet in _facets )
		{
			var activeEntries = facet.Entries
				.Where( e => _filters.IsActive( facet.Name, e.Name ) )
				.ToList();
			int activeCount = activeEntries.Count;
			bool hasActive = activeCount > 0;

			string label = activeCount switch
			{
				0 => facet.Title,
				1 => $"{facet.Title}: {activeEntries[0].Title}",
				_ => $"{facet.Title}: ({activeCount} selected)"
			};

			Paint.SetDefaultFont();
			var measured = Paint.MeasureText( new Rect( 0, 0, 2048, h ), label, TextFlag.LeftCenter );

			float pillW = LeftPad + measured.Width + IconW + RightPad;
			var bounds = new Rect( x, 4, pillW, h );
			var iconRect = new Rect( bounds.Right - IconW - RightPad, bounds.Top, IconW, bounds.Height );

			int idx = _pills.Count;
			bool hovered = _hoveredPill == idx;
			bool iconHov = hovered && _hoveredIcon;

			if ( hasActive )
			{
				// Filled blue tint + blue border
				Paint.ClearPen();
				Paint.SetBrush( Theme.Blue.WithAlpha( hovered && !iconHov ? 0.5f : 1.0f ) );
				Paint.DrawRect( bounds, Theme.ControlRadius );
			}
			else
			{
				// Match standard editor button/control background
				Paint.ClearPen();
				Paint.SetBrush( hovered ? Theme.ControlBackground.Lighten( 0.08f ) : Theme.ControlBackground );
				Paint.DrawRect( bounds, Theme.ControlRadius );
			}

			var contentColor = hasActive ? Theme.Text : (hovered ? Theme.Text : Theme.TextLight);

			Paint.SetPen( contentColor );
			Paint.SetDefaultFont();
			var labelRect = new Rect( bounds.Left + LeftPad, bounds.Top, measured.Width + 4, bounds.Height );
			Paint.DrawText( labelRect, label, TextFlag.LeftCenter );

			if ( hasActive )
			{
				Paint.SetPen( iconHov ? Theme.TextLight : Theme.Text );
				Paint.DrawIcon( iconRect, "close", 11, TextFlag.Center );
			}
			else
			{
				Paint.SetPen( hovered ? Theme.Text.WithAlpha( 0.7f ) : Theme.TextLight.WithAlpha( 0.4f ) );
				Paint.DrawIcon( iconRect, "expand_more", 14, TextFlag.Center );
			}

			_pills.Add( new Pill( facet, bounds, iconRect, hasActive ) );
			x += pillW + PillGap;
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		int prevPill = _hoveredPill;
		bool prevIcon = _hoveredIcon;

		_hoveredPill = -1;
		_hoveredIcon = false;

		for ( int i = 0; i < _pills.Count; i++ )
		{
			if ( !_pills[i].Bounds.IsInside( e.LocalPosition ) ) continue;
			_hoveredPill = i;
			_hoveredIcon = _pills[i].HasActive && _pills[i].IconBounds.IsInside( e.LocalPosition );
			break;
		}

		Cursor = _hoveredPill >= 0 ? CursorShape.Finger : CursorShape.Arrow;

		if ( prevPill != _hoveredPill || prevIcon != _hoveredIcon )
			Update();
	}

	protected override void OnMouseLeave()
	{
		if ( _hoveredPill < 0 ) return;
		_hoveredPill = -1;
		_hoveredIcon = false;
		Cursor = CursorShape.Arrow;
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton ) return;

		for ( int i = 0; i < _pills.Count; i++ )
		{
			var pill = _pills[i];
			if ( !pill.Bounds.IsInside( e.LocalPosition ) ) continue;

			if ( pill.HasActive && pill.IconBounds.IsInside( e.LocalPosition ) )
			{
				_filters.ClearFacet( pill.Facet.Name );
				Update();
				_onChanged?.Invoke();
				return;
			}

			OpenFacetMenu( i );
			return;
		}
	}

	void OpenFacetMenu( int pillIndex )
	{
		var pill = _pills[pillIndex];
		var facet = pill.Facet;
		var menu = new ContextMenu( this );

		foreach ( var entry in facet.Entries )
		{
			if ( entry.Name is "game" or "library" ) continue;

			bool included = _filters.IsIncluded( facet.Name, entry.Name );

			string optLabel = entry.Count > 0
				? $"{entry.Title}   ({entry.Count:n0})"
				: entry.Title;

			var icon = string.IsNullOrEmpty( entry.Icon ) ? "label" : entry.Icon;

			var opt = menu.AddOption( optLabel, icon, () =>
			{
				_filters.Toggle( facet.Name, entry.Name, false );
				Update();
			} );

			opt.Checkable = true;
			opt.Checked = included;
		}

		var screenPt = ToScreen( new Vector2( pill.Bounds.Left, pill.Bounds.Bottom + 2 ) );
		menu.OpenAt( screenPt, false );
	}
}
