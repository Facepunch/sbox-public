namespace Editor.AnimGraph;

internal sealed class ParameterUsagePopup : PopupWidget
{
	private const int ResultHeight = 40;
	private const int VisibleRows = 7;
	private readonly Window _window;
	private readonly SearchLineEdit _search;
	private readonly UsageList _results;
	private readonly Label _summary;
	private readonly List<Usage> _usages = [];

	private sealed record Usage( string NodeIdentifier, string Title, string Path, string Icon, Color Color )
	{
		public string ToolTip { get; } = $"{Title}\n{Path}";
	}

	public ParameterUsagePopup( Window window, IParameter parameter ) : base( window )
	{
		_window = window;
		var references = window.GetParameterUsages( parameter ).ToArray();
		var color = AnimParameterUI.Color( parameter.Type );
		FixedSize = new Vector2( 520, Math.Clamp( references.Length, 1, VisibleRows ) * ResultHeight + Theme.RowHeight + 80 );
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 6;

		var count = $"{references.Length} {(references.Length == 1 ? "use" : "uses")}";
		var header = Layout.Add( new Widget( this )
		{
			FixedHeight = 28,
			ToolTip = $"{parameter.Name}\n{AnimParameterUI.TypeName( parameter.Type )} {(parameter.IsVirtual ? "virtual" : "control")} parameter"
		} );
		header.OnPaintOverride = () =>
		{
			var rect = header.LocalRect;
			var icon = new Rect( 0, 2, 24, 24 );
			Paint.ClearPen();
			Paint.SetBrush( color.WithAlpha( 0.12f ) );
			Paint.DrawRect( icon, Theme.ControlRadius );
			Paint.SetPen( color );
			Paint.DrawIcon( icon, AnimParameterUI.Icon( parameter.Type ), 16, TextFlag.Center );
			Paint.SetDefaultFont( 8, 500 );
			var badge = rect with { Left = rect.Right - Paint.MeasureText( count ).x - 16 };
			Paint.ClearPen();
			Paint.SetBrush( Theme.TextControl.WithAlpha( 0.06f ) );
			Paint.DrawRect( badge.Shrink( 0, 4 ), Theme.ControlRadius );
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.7f ) );
			Paint.DrawText( badge, count, TextFlag.Center );
			rect.Left = icon.Right + 8;
			rect.Right = badge.Left - 8;
			Paint.SetDefaultFont( 9, 600 );
			Paint.SetPen( Theme.TextControl );
			Paint.DrawText( rect, Paint.GetElidedText( parameter.Name, rect.Width ), TextFlag.LeftCenter | TextFlag.SingleLine );
			return true;
		};

		_search = Layout.Add( new SearchLineEdit( this )
		{
			PlaceholderText = "Filter by state, graph or connected node...",
			ClearButtonEnabled = true,
			FixedHeight = Theme.RowHeight
		} );
		_search.TextEdited += _ => Refresh();
		_search.KeyPressed = HandleSearchKey;

		var consumers = window.Graph.AllNodes
			.SelectMany( node => node.Inputs )
			.Where( input => input.ConnectedOutput is not null )
			.ToLookup( input => input.ConnectedOutput.Node, input => input.Node );

		foreach ( var group in references.GroupBy( node => node.Graph )
			.OrderBy( group => window.GetNodePath( group.First() ), StringComparer.OrdinalIgnoreCase ) )
		{
			var path = window.GetNodePath( group.First() );
			foreach ( var node in group.OrderBy( node => node.Position.y ).ThenBy( node => node.Position.x ) )
			{
				var targets = consumers[node].OfType<Node>().Distinct().Select( target => target.DisplayInfo.Name ).ToArray();
				var title = targets.Length == 0
					? $"{node.DisplayInfo.Name} (not connected)"
					: $"{node.DisplayInfo.Name} -> {string.Join( ", ", targets )}";
				_usages.Add( new Usage( node.Identifier, title, path,
					targets.Length == 0 ? "link_off" : node.DisplayInfo.Icon ?? AnimParameterUI.Icon( parameter.Type ),
					targets.Length == 0 ? Theme.TextControl.WithAlpha( 0.5f ) : color ) );
			}
		}

		_results = Layout.Add( new UsageList( this ), 1 );
		_results.ItemActivated = item => Activate( item as Usage );

		var footer = Layout.AddRow();
		_summary = footer.Add( new Label( "", this )
		{
			FixedHeight = 18,
			Color = Theme.TextControl.WithAlpha( 0.6f )
		}, 1 );
		footer.Add( new Label( "Enter: jump   Esc: close", this )
		{
			FixedHeight = 18,
			Color = Theme.TextControl.WithAlpha( 0.45f )
		} );

		Refresh();
		_search.Focus();
	}

	private void Refresh()
	{
		var words = _search.Text.Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );
		var matches = _usages.Where( usage => words.All( word => usage.ToolTip.Contains( word, StringComparison.OrdinalIgnoreCase ) ) ).ToArray();
		var selected = _results.SelectedItems.FirstOrDefault() as Usage;
		_results.UnselectAll();
		_results.SetItems( matches );
		_results.EmptyText = _usages.Count == 0 ? "This parameter isn't used in this graph." : "No matching usages.\nTry a state, graph or node name.";
		_summary.Text = matches.Length == _usages.Count
			? "This graph, including nested graphs"
			: $"{matches.Length} of {_usages.Count} uses - This graph";

		if ( matches.Length > 0 )
		{
			selected = matches.Contains( selected ) ? selected : matches[0];
			_results.SelectItem( selected );
			_results.ScrollTo( selected );
		}
	}

	private void Activate( Usage usage )
	{
		if ( usage is null )
			return;

		Destroy();
		_window.NavigateToNode( usage.NodeIdentifier );
	}

	private void HandleSearchKey( KeyEvent e )
	{
		switch ( e.Key )
		{
			case KeyCode.Down:
				_results.SelectMoveRow( 1 );
				break;
			case KeyCode.Up:
				_results.SelectMoveRow( -1 );
				break;
			case KeyCode.PageDown:
				_results.SelectMoveRow( VisibleRows );
				break;
			case KeyCode.PageUp:
				_results.SelectMoveRow( -VisibleRows );
				break;
			case KeyCode.Enter:
			case KeyCode.Return:
				Activate( _results.SelectedItems.FirstOrDefault() as Usage );
				break;
			case KeyCode.Escape:
				Destroy();
				break;
			default:
				return;
		}

		e.Accepted = true;
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( e.Key == KeyCode.Escape )
		{
			e.Accepted = true;
			Destroy();
			return;
		}

		base.OnKeyPress( e );
	}

	private sealed class UsageList : ListView
	{
		public string EmptyText { get; set; }

		public UsageList( Widget parent ) : base( parent )
		{
			ItemSize = new Vector2( 0, ResultHeight );
			ItemSpacing = 0;
			Margin = 0;
			HorizontalScrollbarMode = ScrollbarMode.Off;
			VerticalScrollbarMode = ScrollbarMode.Auto;
			AcceptDrops = false;
		}

		protected override string GetTooltip( object item ) => (item as Usage)?.ToolTip;

		protected override void OnPaint()
		{
			base.OnPaint();
			if ( _items.Count > 0 )
				return;

			Paint.SetDefaultFont();
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
			Paint.DrawText( LocalRect.Shrink( 8 ), EmptyText, TextFlag.Center );
		}

		protected override void PaintItem( VirtualWidget item )
		{
			if ( item.Object is not Usage usage )
				return;

			if ( item.Selected || item.Hovered )
			{
				Paint.ClearPen();
				Paint.SetBrush( item.Selected ? Theme.SelectedBackground : Theme.SelectedBackground.WithAlpha( 0.25f ) );
				Paint.DrawRect( item.Rect.Shrink( 1 ), Theme.ControlRadius );
			}

			var icon = new Rect( item.Rect.Position + new Vector2( 6, 0 ), new Vector2( 22, item.Rect.Height ) );
			Paint.SetPen( usage.Color );
			Paint.DrawIcon( icon, usage.Icon, 16, TextFlag.Center );

			var title = item.Rect.Shrink( 36, 3, 28, 0 );
			title.Height = 19;
			Paint.SetDefaultFont( 8, item.Selected ? 600 : 500 );
			Paint.SetPen( Theme.TextControl );
			Paint.DrawText( title, Paint.GetElidedText( usage.Title, title.Width ), TextFlag.LeftCenter | TextFlag.SingleLine );

			var path = title;
			path.Top += 17;
			path.Height = 17;
			Paint.SetDefaultFont( 7 );
			Paint.SetPen( Theme.TextControl.WithAlpha( item.Selected || item.Hovered ? 0.8f : 0.65f ) );
			Paint.DrawText( path, Paint.GetElidedText( usage.Path, path.Width, ElideMode.Left ), TextFlag.LeftCenter | TextFlag.SingleLine );

			if ( item.Selected || item.Hovered )
			{
				Paint.SetPen( Theme.Primary );
				Paint.DrawIcon( item.Rect.Shrink( 0, 0, 6, 0 ), "arrow_forward", 14, TextFlag.RightCenter );
			}
		}

		protected override bool OnItemPressed( VirtualWidget item, MouseEvent e )
		{
			if ( !e.LeftMouseButton || item.Object is not Usage usage )
				return false;

			OnItemActivated( usage );
			return true;
		}
	}
}
