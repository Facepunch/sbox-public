namespace Editor.AnimGraph;

internal sealed class BreadcrumbBar : Widget
{
	internal record Item( string Title, string Icon, Color? Tint, Action Navigate );

	private const float SeparatorWidth = 16;
	private const float OverflowWidth = 24;
	private readonly IconButton _overflow;
	private readonly List<(Item Item, BreadcrumbButton Button)> _items = new();
	private int _firstVisible;

	public BreadcrumbBar( Widget parent ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;
		MinimumWidth = 80;
		_overflow = new IconButton( "more_horiz", OpenOverflow, this )
		{
			Background = Color.Transparent,
			Foreground = Theme.TextControl,
			IconSize = 16,
			ToolTip = "Show hidden parent pages",
			Visible = false
		};
	}

	public void SetItems( IEnumerable<Item> items )
	{
		foreach ( var entry in _items )
			entry.Button.Destroy();

		_items.Clear();
		foreach ( var item in items )
		{
			var button = new BreadcrumbButton( item.Title, item.Icon, item.Tint, item.Navigate ) { Parent = this };
			_items.Add( (item, button) );
		}

		LayoutChildren();
	}

	protected override void OnResize()
	{
		base.OnResize();
		LayoutChildren();
	}

	private void LayoutChildren()
	{
		if ( _items.Count == 0 )
		{
			_overflow.Visible = false;
			return;
		}

		var available = Math.Max( 0, Width );
		var total = _items.Sum( entry => entry.Button.ContentWidth ) + SeparatorWidth * (_items.Count - 1);
		var currentWidth = Math.Min( _items[^1].Button.ContentWidth, available );
		_firstVisible = 0;

		if ( total > available && _items.Count > 1 )
		{
			_firstVisible = _items.Count - 1;
			available = Math.Max( 0, available - OverflowWidth - SeparatorWidth );
			currentWidth = Math.Min( currentWidth, available );
			var used = currentWidth;

			while ( _firstVisible > 0 )
			{
				var width = _items[_firstVisible - 1].Button.ContentWidth + SeparatorWidth;
				if ( used + width > available )
					break;

				used += width;
				_firstVisible--;
			}
		}

		_overflow.Visible = _firstVisible > 0;
		_overflow.Position = Vector2.Zero;
		_overflow.FixedSize = new Vector2( OverflowWidth, Height );
		var x = _firstVisible > 0 ? OverflowWidth + SeparatorWidth : 0;

		for ( var i = 0; i < _items.Count; i++ )
		{
			var button = _items[i].Button;
			button.Visible = i >= _firstVisible;
			if ( i < _firstVisible )
				continue;

			button.FixedWidth = i == _items.Count - 1 ? currentWidth : button.ContentWidth;
			button.Position = new Vector2( x, 0 );
			button.Size = new Vector2( button.FixedWidth, Height );
			x += button.Width + SeparatorWidth;
		}

		Update();
	}

	private void OpenOverflow()
	{
		var menu = new Menu( this );
		for ( var i = 0; i < _firstVisible; i++ )
		{
			var item = _items[i].Item;
			menu.AddOption( item.Title, item.Icon, item.Navigate );
		}

		menu.OpenAt( _overflow.ScreenRect.BottomLeft );
	}

	protected override void OnPaint()
	{
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );
		for ( var i = Math.Max( 1, _firstVisible ); i < _items.Count; i++ )
		{
			var x = _items[i].Button.Position.x - SeparatorWidth;
			Paint.DrawIcon( new Rect( x, 0, SeparatorWidth, Height ), "chevron_right", 12, TextFlag.Center );
		}
	}
}
