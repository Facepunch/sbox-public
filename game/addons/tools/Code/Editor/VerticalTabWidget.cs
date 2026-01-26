namespace Editor;

public class VerticalTabWidget : Widget
{
	Dictionary<string, Widget> pages = new();
	Dictionary<string, VerticalTab> tabs = new();

	Widget _currentPage;
	Widget tabContainer;
	Widget contentContainer;

	public Widget CurrentPage
	{
		get => _currentPage;
		set
		{
			if ( _currentPage == value )
				return;

			SetPage( value );

			if ( _currentPage.IsValid() )
				_currentPage.Visible = false;

			_currentPage = value;

			if ( _currentPage.IsValid() )
				_currentPage.Visible = true;

			Update();
		}
	}

	public string Selected { get; private set; }
	public Action<string> OnSelectedChanged { get; set; }

	public VerticalTabWidget( Widget parent ) : base( parent )
	{
		Layout = Layout.Row();

		tabContainer = Layout.Add( new Widget( this ) );
		tabContainer.FixedWidth = Theme.RowHeight * 2;
		tabContainer.Layout = Layout.Column();
		tabContainer.Layout.Alignment = TextFlag.Top;

		contentContainer = Layout.Add( new Widget( this ) );
		contentContainer.Layout = Layout.Column();
	}

	public void SetPage( Widget page )
	{
		var entry = pages.FirstOrDefault( x => x.Value == page );
		if ( !entry.Value.IsValid() ) return;
		if ( _currentPage == entry.Value ) return;

		foreach ( var p in pages )
		{
			p.Value.Visible = false;
		}

		entry.Value.Visible = true;
		_currentPage = entry.Value;
		Selected = entry.Key;

		foreach ( var tab in tabs )
		{
			tab.Value.IsActive = tab.Key == Selected;
			tab.Value.Update();
		}

		Update();
		OnSelectedChanged?.Invoke( Selected );
		Save();
	}

	public void AddPage( string name, string icon = null, Widget page = null, string tooltip = null )
	{
		page ??= new Widget( null );

		page.Visible = false;
		pages[name] = page;

		var tab = tabContainer.Layout.Add( new VerticalTab( icon )
		{
			ToolTip = tooltip ?? name,
			IsActive = false,
			Name = name
		} );

		tabs[name] = tab;

		tab.MouseClick = () =>
		{
			SetPage( page );
		};

		contentContainer.Layout.Add( page );

		if ( pages.Count == 1 )
		{
			CurrentPage = page;
		}
	}

	public void SetPageEnabled( string name, bool enabled )
	{
		if ( !tabs.TryGetValue( name, out var tab ) )
			return;

		tab.Enabled = enabled;
	}

	string _cookie;

	public string StateCookie
	{
		get => _cookie;

		set
		{
			if ( _cookie == value ) return;
			_cookie = value;
			Restore();
		}
	}

	private void Save()
	{
		if ( string.IsNullOrEmpty( StateCookie ) ) return;

		var pageName = "";
		var page = pages.FirstOrDefault( x => x.Value == CurrentPage );
		pageName = page.Key ?? "";

		EditorCookie.Set( $"verticaltabwidget.{StateCookie}", pageName );
	}

	private void Restore()
	{
		if ( string.IsNullOrEmpty( StateCookie ) ) return;

		var pageName = EditorCookie.Get<string>( $"verticaltabwidget.{StateCookie}", null );
		if ( string.IsNullOrWhiteSpace( pageName ) ) return;

		var page = pages.FirstOrDefault( x => x.Key == pageName );
		if ( page.Key == null ) return;

		SetPage( page.Value );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.ClearBrush();

		var tabRect = tabContainer.LocalRect;
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( tabRect );

		Paint.SetBrushAndPen( Theme.Border );
		var borderRect = tabRect;
		borderRect.Left = borderRect.Right - 1;
		Paint.DrawRect( borderRect );

		Paint.SetBrushAndPen( Theme.BorderLight );
		borderRect.Position += new Vector2( 1, 0 );
		Paint.DrawRect( borderRect );
	}
}

public class VerticalTab : Button
{
	public bool IsActive { get; set; }

	public VerticalTab( string icon ) : base( null )
	{
		Icon = icon;
		FixedHeight = Theme.RowHeight * 1.5f;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.ClearBrush();

		Paint.SetBrushAndPen( Theme.Border );
		var r = LocalRect;
		r.Top = r.Bottom - 1;
		Paint.DrawRect( r );

		Paint.SetBrushAndPen( Theme.BorderLight );
		r.Position += new Vector2( 0, 1 );
		Paint.DrawRect( r );

		if ( !Enabled )
		{
			Paint.ClearBrush();
			Paint.SetPen( Theme.TextDisabled );
			Paint.DrawIcon( LocalRect, Icon, 16.0f );

			return;
		}

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrush( Theme.SurfaceBackground );
			Paint.DrawRect( LocalRect );
		}

		if ( IsActive )
		{
			Paint.SetBrush( Theme.Primary );
			Paint.DrawRect( LocalRect );
		}

		Paint.ClearBrush();
		Paint.SetPen( Theme.TextControl );
		Paint.DrawIcon( LocalRect, Icon, 16.0f );
	}
}
