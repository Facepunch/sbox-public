using Sandbox.Internal;

namespace Editor;

/// <summary>
/// The tab widget that sits at the top of the ControlSheet if you add [Features]
/// </summary>
public class FeatureTabWidget : Widget
{
	List<FeatureTabOption> Tabs = new List<FeatureTabOption>();

	Layout TopRowLayout;
	Layout TabLayout;
	Layout ContentLayout;

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

	public FeatureTabWidget( Widget parent ) : base( parent )
	{
		VerticalSizeMode = SizeMode.CanGrow;
		HorizontalSizeMode = SizeMode.Flexible;

		Layout = Layout.Column();
		Layout.Margin = new Sandbox.UI.Margin( 0, 4, 0, 0 );
		TopRowLayout = Layout.AddRow();

		TopRowLayout.AddSpacingCell( 8 );
		TabLayout = TopRowLayout.AddRow();

		{
			IconButton add = new IconButton( "add", OpenAddFeaturesMenu );
			add.Background = Color.Transparent;
			add.Foreground = Color.White.WithAlpha( 0.6f );
			add.ForegroundActive = Theme.Primary;
			add.IconSize = 15;
			TopRowLayout.Add( add );
		}

		TopRowLayout.AddStretchCell();

		ContentLayout = Layout.AddColumn();
		ContentLayout.Margin = new Sandbox.UI.Margin( 0 );
	}

	public void OpenAddFeaturesMenu()
	{
		var menu = new ContextMenu( this );

		int iAdded = 0;

		foreach ( var tab in Tabs )
		{
			if ( tab.FeatureEnabled is null ) continue;

			var state = tab.FeatureEnabled.As.Bool;
			if ( state ) continue;

			string icon = null;
			string title = tab.FeatureEnabled.DisplayName;

			if ( tab.FeatureEnabled.TryGetAttribute<FeatureEnabledAttribute>( out var attribute ) )
			{
				icon = attribute.Icon;
				title = attribute.Title;
			}

			menu.AddOption( new Option( title, icon ) { Triggered = tab.SetFeatureEnabled } );
			iAdded++;
		}

		if ( iAdded == 0 )
		{
			menu.AddOption( new Option( "No features available" ) { Enabled = false } );
		}

		menu.OpenAtCursor();
	}

	void AddTab( FeatureTabOption tab )
	{
		Tabs.Add( tab );

		TabLayout.Add( tab );

		if ( Tabs.Count == 1 )
		{
			Select( tab );
		}
	}

	public void Select( FeatureTabOption option )
	{
		foreach ( var tab in Tabs.Where( x => x.IsSelected ) )
		{
			tab.IsSelected = false;
			tab.HidePage();
		}

		option.IsSelected = true;

		option.ShowPage( ContentLayout );

		Save();
	}

	bool TrySelectIndex( int index )
	{
		if ( index < 0 ) return false;
		if ( index >= Tabs.Count() ) return false;

		var tab = Tabs[index];
		if ( !tab.Visible ) return false;

		Select( tab );
		return true;
	}

	public void OnVisibilityChanged( FeatureTabOption option )
	{
		if ( !option.IsSelected ) return;

		var idx = Tabs.IndexOf( option );
		if ( idx < 0 ) idx = 0;

		for ( int i = 0; i < Tabs.Count; i++ )
		{
			if ( TrySelectIndex( idx + i ) )
				return;

			if ( TrySelectIndex( idx - i ) )
				return;
		}
	}

	private void Save()
	{
		if ( string.IsNullOrEmpty( StateCookie ) ) return;

		var selected = Tabs.FirstOrDefault( x => x.IsSelected );
		if ( selected is null )
		{
			EditorCookie.Remove( $"featuretab.{StateCookie}" );
			return;
		}

		EditorCookie.Set( $"featuretab.{StateCookie}", selected.UniqueId );
	}

	private void Restore()
	{
		if ( string.IsNullOrEmpty( StateCookie ) ) return;

		var pageName = EditorCookie.Get<string>( $"featuretab.{StateCookie}", null );
		if ( string.IsNullOrWhiteSpace( pageName ) ) return;

		var target = Tabs.Where( x => x.UniqueId == pageName ).FirstOrDefault();
		if ( target is null ) return;

		Select( target );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.SetBrush( InspectorHeader.BackgroundColor );
		Paint.DrawRect( TopRowLayout.OuterRect.Grow( 100, 50, 100, 0 ) );
	}

	internal void AddFeature( IControlSheet.Feature feature )
	{
		string title = feature.Name;
		string icon = feature.Icon;
		string desc = feature.Description;
		Color tint = tint = Theme.GetTint( feature.Tint );

		if ( string.IsNullOrEmpty( title ) )
		{
			title = "General";
		}

		var tab = new FeatureTabOption( this, title, icon, desc, tint, this );
		tab.Feature = feature;

		if ( tab.FeatureEnabled is not null && !tab.FeatureEnabled.As.Bool )
		{
			tab.Visible = false;
		}

		AddTab( tab );

	}
}


public class FeatureTabOption : Widget
{
	public string Icon { get; set; }
	public string Description { get; set; }
	public bool ShowText { get; set; } = true;
	public int Index { get; set; }
	public bool IsSelected { get; set; }
	public Color Tint { get; set; }

	public IControlSheet.Feature Feature { get; set; }

	public string UniqueId => Name;

	/// <summary>
	/// If this feature has an option to enable and disable it, this is it.
	/// </summary>
	public SerializedProperty FeatureEnabled => Feature.EnabledProperty;

	Widget Page;

	FeatureTabWidget Owner;

	public FeatureTabOption( FeatureTabWidget owner, string name, string icon, string desc, Color tint, Widget parent ) : base( parent )
	{
		Owner = owner;
		Icon = icon;
		Name = name;
		Tint = tint;
		Description = desc;
		Cursor = CursorShape.Finger;
		ToolTip = GetTooltip();
		VerticalSizeMode = SizeMode.CanGrow;
	}

	protected override Vector2 SizeHint()
	{
		var height = 24;

		if ( !ShowText && !IsSelected )
		{
			return height;
		}

		Paint.SetDefaultFont( 8 );
		var textSize = Paint.MeasureText( Name );
		textSize.x += 16;
		textSize.y += 8;

		if ( !string.IsNullOrWhiteSpace( Icon ) )
		{
			textSize.x += 22;
		}

		textSize.y = height;

		return textSize;
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );
		Owner?.Select( this );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.LeftMouseButton )
		{
			Owner?.Select( this );
		}
	}

	public bool CanFeatureBeRemoved => FeatureEnabled is not null && FeatureEnabled.As.Bool;

	public void RemoveFeature()
	{
		if ( FeatureEnabled is not null )
		{
			FeatureEnabled.As.Bool = false;
		}

		UpdateVisibility();

		// Mark workspace as dirty when removing a feature
		if ( SceneEditorSession.Active is not null )
		{
			SceneEditorSession.Active.HasUnsavedChanges = true;
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		Owner?.Select( this );
		e.Accepted = true;

		var menu = new ContextMenu( this );

		{
			var o = new Option( $"Remove {Name} Feature", "close" );
			o.Enabled = CanFeatureBeRemoved;
			o.Triggered = RemoveFeature;
			menu.AddOption( o );
		}

		{
			menu.AddSeparator();
			menu.AddOption( $"Copy {Name} Properties", "content_copy", () =>
			{
				ClipboardTools.CopyProperties( Name, Feature.Properties );
			} );
			var pasteOption = menu.AddOption( $"Paste {Name} Properties", "content_paste", () =>
			{
				ClipboardTools.PasteProperties( Name, Feature.Properties );
			} );
			pasteOption.Enabled = ClipboardTools.CanPasteProperties( Name, Feature.Properties );
			menu.AddOption( "Reset all to Default", "restart_alt", () =>
			{
				foreach ( var prop in Feature.Properties )
				{
					prop.SetValue( prop.GetDefault() );
				}
			} );
		}

		if ( FeatureEnabled is not null && CodeEditor.CanOpenFile( FeatureEnabled.SourceFile ) )
		{
			menu.AddSeparator();

			var filename = System.IO.Path.GetFileName( FeatureEnabled.SourceFile );
			menu.AddOption( $"Jump to code", "code", action: () => CodeEditor.OpenFile( FeatureEnabled.SourceFile, FeatureEnabled.SourceLine ) );
		}

		menu.OpenAtCursor();
	}

	public void UpdateVisibility()
	{
		if ( FeatureEnabled is null ) return;

		bool enabled = FeatureEnabled.As.Bool;
		if ( Visible == enabled ) return;

		Visible = enabled;
		Owner.OnVisibilityChanged( this );
	}

	public void SetFeatureEnabled()
	{
		if ( FeatureEnabled is null ) return;
		if ( FeatureEnabled.As.Bool ) return;

		FeatureEnabled.As.Bool = true;
		UpdateVisibility();
		Owner.Select( this );

		// Mark workspace as dirty when adding a feature
		if ( SceneEditorSession.Active is not null )
		{
			SceneEditorSession.Active.HasUnsavedChanges = true;
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.ClearBrush();

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		var r = LocalRect;
		Paint.ClearPen();

		float alpha = 1;

		if ( IsSelected )
		{
			var rr = LocalRect.Shrink( 0, 0, 0, 0 );
			var underline = rr;
			underline.Bottom = underline.Top + 2;

			Paint.SetBrush( Theme.WidgetBackground.Lighten( 0.2f ) );
			Paint.DrawRect( rr.Grow( 0, 0, 0, 0 ), 4 );

			Paint.SetBrush( Theme.WidgetBackground );
			Paint.DrawRect( rr.Grow( 0, -1, 0, 8 ), 3 );
		}
		else
		{
			alpha = 0.3f;

			if ( Paint.HasMouseOver ) alpha += 0.4f;
		}

		if ( !ShowText && !IsSelected )
		{
			Paint.ClearBrush();
			Paint.SetPen( Tint.WithAlpha( alpha ) );
			Paint.DrawIcon( r, Icon, 12.0f );
			return;
		}

		Paint.SetDefaultFont( 8 );
		Paint.ClearBrush();
		Paint.SetPen( Tint.WithAlpha( alpha ) );

		if ( string.IsNullOrEmpty( Icon ) )
		{
			Paint.DrawText( r, Name, TextFlag.Center );
		}
		else
		{
			r.Left += 12.0f;

			var nameRect = Paint.DrawText( r.Shrink( 0, 2, 0, 2 ), Name, TextFlag.Center );
			r.Left -= nameRect.Width + 24.0f;
			//r.Bottom += 6;

			alpha = alpha + 0.2f;
			alpha = alpha.Clamp( 0, 1 );

			Paint.ClearBrush();
			Paint.SetPen( Tint.WithAlpha( alpha ) );
			Paint.DrawIcon( r, Icon, 13.0f, TextFlag.Center | TextFlag.DontClip );
		}
	}

	/// <summary>
	/// Hide the page - we've been tabbed away
	/// </summary>
	internal void HidePage()
	{
		if ( Page is null ) return;

		Page.Hidden = true;
	}

	/// <summary>
	/// Convert the Properties into a ControlSheet
	/// </summary>
	void CreatePage()
	{
		var page = new Widget( this );

		page.Hidden = true;
		page.VerticalSizeMode = SizeMode.CanGrow;
		page.HorizontalSizeMode = SizeMode.Flexible;

		var sheet = new ControlSheet();
		sheet.Margin = 0;

		IControlSheet.AddProperties( sheet, Feature.Properties.Where( x => x != FeatureEnabled ).ToList(), false );

		page.Layout = sheet;

		Page = page;
	}

	/// <summary>
	/// Show the page, create it and add it to the layout if it doesn't already exist
	/// </summary>
	internal void ShowPage( Layout contentLayout )
	{
		if ( !Page.IsValid() )
		{
			CreatePage();
		}

		contentLayout.Add( Page );
		Page.Hidden = false;
	}

	string GetTooltip()
	{
		var str = "<strong>";
		str += $"<span style=\"color: #9CDCFE;\">{Name}</span>";
		str += "</strong>";

		if ( !string.IsNullOrWhiteSpace( Description ) )
		{
			str += $"<br/><br/><font>{Description}</font>";
		}

		return str;
	}
}
