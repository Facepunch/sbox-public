namespace Editor;

class FilterBreadcrumb : Widget
{
	record struct Segment( string Label, string Filter, Rect PaintRect );

	List<Segment> _segments = new();
	readonly Action<string> _navigate;
	int _hoveredIndex = -1;

	public FilterBreadcrumb( Action<string> navigate, Widget parent ) : base( parent )
	{
		_navigate = navigate;
		// No FixedHeight — height is controlled by the nav bar row.
		// No Visible toggling — always occupies layout space so Search stays right-aligned;
		// OnPaint returns early when there are no segments.
	}

	/// <summary>
	/// Build the breadcrumb from a selected AssetFilterNode, walking up through its
	/// ancestor chain to collect display labels and navigation filters.
	/// </summary>
	public void SetPath( AssetFilterNode node )
	{
		_segments.Clear();

		var chain = new List<(string label, string filter)>();
		var current = (TreeNode)node;

		while ( current != null )
		{
			if ( current is AssetFilterNode afn && !string.IsNullOrEmpty( afn.Label ) )
				chain.Add( (afn.Label, afn.Filter) );
			else if ( current is TreeNode.SmallHeader sh && !string.IsNullOrEmpty( sh.Title ) )
				chain.Add( (sh.Title, null) ); // facet group label, not clickable

			// TreeNode.Header nodes (MY COLLECTIONS, MY ORGANISATIONS) are skipped

			current = current.Parent;
		}

		chain.Reverse();

		foreach ( var (label, filter) in chain )
			_segments.Add( new( label, filter, default ) );

		_hoveredIndex = -1;
		Update();
	}

	public void Reset()
	{
		_segments.Clear();
		_hoveredIndex = -1;
		Update();
	}

	protected override void OnPaint()
	{
		if ( _segments.Count == 0 ) return;

		var x = LocalRect.Left + 8f;
		var rect = LocalRect;

		for ( int i = 0; i < _segments.Count; i++ )
		{
			var seg = _segments[i];
			bool isCurrent = i == _segments.Count - 1;
			bool isLink = seg.Filter != null && !isCurrent;

			Color color;
			if ( isCurrent )
				color = Theme.Text;
			else if ( isLink && i == _hoveredIndex )
				color = Theme.Blue;
			else
				color = Theme.TextLight;

			Paint.SetPen( color );
			Paint.SetDefaultFont();

			var measured = Paint.MeasureText( new Rect( x, rect.Top, 4096, rect.Height ), seg.Label, TextFlag.LeftCenter );
			var segRect = new Rect( x, rect.Top, measured.Width, rect.Height );
			Paint.DrawText( segRect, seg.Label, TextFlag.LeftCenter );
			_segments[i] = seg with { PaintRect = segRect };

			x += measured.Width;

			if ( i < _segments.Count - 1 )
			{
				x += 4;
				Paint.SetPen( Theme.TextLight.WithAlpha( 0.4f ) );
				Paint.SetDefaultFont();
				var sepRect = new Rect( x, rect.Top, 12, rect.Height );
				Paint.DrawText( sepRect, "›", TextFlag.LeftCenter );
				x += 14;
			}
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		int prev = _hoveredIndex;
		_hoveredIndex = -1;

		for ( int i = 0; i < _segments.Count - 1; i++ )
		{
			if ( _segments[i].Filter != null && _segments[i].PaintRect.IsInside( e.LocalPosition ) )
			{
				_hoveredIndex = i;
				break;
			}
		}

		Cursor = _hoveredIndex >= 0 ? CursorShape.Finger : CursorShape.Arrow;

		if ( prev != _hoveredIndex )
			Update();
	}

	protected override void OnMouseLeave()
	{
		if ( _hoveredIndex < 0 ) return;

		_hoveredIndex = -1;
		Cursor = CursorShape.Arrow;
		Update();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( !e.LeftMouseButton ) return;

		// Only non-current segments are navigable
		for ( int i = 0; i < _segments.Count - 1; i++ )
		{
			var seg = _segments[i];
			if ( seg.Filter == null ) continue;
			if ( !seg.PaintRect.IsInside( e.LocalPosition ) ) continue;

			// Trim the breadcrumb to this point — we're navigating up the hierarchy
			_segments.RemoveRange( i + 1, _segments.Count - i - 1 );
			_hoveredIndex = -1;
			Update();

			_navigate?.Invoke( seg.Filter );
			return;
		}
	}
}
