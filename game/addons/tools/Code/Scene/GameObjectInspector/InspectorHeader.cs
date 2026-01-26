namespace Editor;

// The component list will be rebuilt on hotload, so we can safely skip
[SkipHotload]
public class InspectorHeader : Widget
{
	public static Color HeaderColor = "#8E9199";
	public static Color BackgroundColor => Theme.WidgetBackground;

	public string Title { get; set; }
	public string Icon { get; set; }
	public string IconOverlay { get; set; }
	public string HelpUrl { get; set; }

	public Color Color { get; set; }

	public bool IsCollapsable { get; set; } = true;
	public bool IsExpanded { get; set; }

	public Action<Menu> ContextMenu;

	Layout expanderRect;
	Layout iconRect;
	Layout textRect;

	protected Drag dragData;

	public InspectorHeader() : base( null )
	{
		MouseTracking = true;
		Color = HeaderColor;

		FixedHeight = Theme.RowHeight + 8;

		ContentMargins = 0;
		Layout = Layout.Row();
	}

	public void BuildUI()
	{
		if ( !IsCollapsable ) IsExpanded = true;

		Layout.Clear( true );

		Layout.AddSpacingCell( 4 );

		expanderRect = Layout.AddRow();
		expanderRect.AddSpacingCell( 16 );

		iconRect = Layout.AddRow();
		iconRect.AddSpacingCell( 22 );

		Layout.AddSpacingCell( 4 );

		var checkbox = BuildIcons();
		if ( checkbox.IsValid() )
		{
			checkbox.FixedSize = 24;
			Layout.Add( checkbox );
			Layout.AddSpacingCell( 8 );
		}
		else
		{
			Layout.AddSpacingCell( 24 + 8 );
		}

		// text 
		textRect = Layout.AddColumn( 1 );

		var rightRect = Layout.AddRow();
		rightRect.Spacing = 2;
		BuildRightIcons( rightRect );

		Layout.AddSpacingCell( 16 );
	}

	protected virtual Widget BuildIcons()
	{
		return null;
	}

	protected virtual void BuildRightIcons( Layout layout )
	{
		if ( !string.IsNullOrWhiteSpace( HelpUrl ) )
		{
			var button = new IconButton( "help" );
			button.Background = Color.Transparent;
			button.OnClick = () => EditorUtility.OpenFile( HelpUrl );
			button.FixedSize = 20;
			button.IconSize = 14;
			button.Foreground = Color;

			layout.Add( button );
		}

		// More menu
		{
			var button = new IconButton( "more_horiz" );
			button.Background = Color.Transparent;
			button.OnClick = () => ShowContextMenu( button.ScreenRect.BottomRight );
			button.FixedSize = 20;
			button.IconSize = 14;
			button.Foreground = Color;

			layout.Add( button );
		}
	}

	protected virtual bool IsTargetDisabled() => false;


	protected override void OnPaint()
	{
		if ( iconRect is null )
			return;

		float opacity = 1f;
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		if ( Paint.HasMouseOver && IsCollapsable )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue.WithAlpha( 0.1f ) );
			Paint.DrawRect( LocalRect, 0 );
			opacity = 1.0f;
		}

		if ( !IsExpanded )
			opacity = 0.8f;

		if ( IsTargetDisabled() )
			opacity = 0.7f;

		var bgColor = BackgroundColor;
		var isBeingDragged = dragData?.IsValid ?? false;

		if ( isBeingDragged )
		{
			bgColor = bgColor.Lighten( 0.5f );
		}

		// Top border
		{
			Paint.SetBrushAndPen( Theme.ControlBackground );
			var r = LocalRect;
			r.Bottom = r.Top + 1;
			Paint.DrawRect( r );

			Paint.SetBrushAndPen( Theme.BorderLight );
			r.Position += new Vector2( 0, 1 );
			Paint.DrawRect( r );
		}

		Paint.SetPen( Color.Saturate( 0.2f ).Lighten( 0.3f ).WithAlpha( (IsExpanded || !IsCollapsable ? 0.9f : 0.6f) * opacity ) );

		// Chevron
		if ( IsCollapsable )
		{
			if ( !IsExpanded )
			{
				Paint.SetPen( Color );
				Paint.DrawIcon( expanderRect.InnerRect.Shrink( 3, 0, 0, 0 ), "arrow_right", 18, TextFlag.Center );
			}
			else
			{
				Paint.SetPen( Color );
				Paint.DrawIcon( expanderRect.InnerRect, "arrow_drop_down", 18, TextFlag.Center );
			}
		}

		// icon
		Paint.SetPen( Color.WithAlpha( opacity ) );
		Paint.DrawIcon( iconRect.InnerRect, string.IsNullOrEmpty( Icon ) ? "category" : Icon, 16, TextFlag.Center );
		if ( !string.IsNullOrEmpty( IconOverlay ) )
		{
			var overlayIconRect = iconRect.InnerRect;
			overlayIconRect.Left += 12;
			overlayIconRect.Top += 12;
			overlayIconRect.Width = 11;
			overlayIconRect.Height = 11;
			Paint.SetBrush( bgColor );
			Paint.SetPen( bgColor );
			Paint.DrawRect( overlayIconRect, 12 );
			Paint.SetPen( Color.WithAlpha( opacity ) );
			overlayIconRect.Left += 1;
			Paint.DrawIcon( overlayIconRect, IconOverlay, 11, TextFlag.Center );
		}

		// title
		Paint.SetPen( Theme.Text.WithAlphaMultiplied( opacity ) );
		Paint.SetHeadingFont( 11, 440, sizeInPixels: true );
		Paint.DrawText( textRect.InnerRect, Title, TextFlag.LeftCenter );
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		if ( ShowContextMenu( e.ScreenPosition ) )
			e.Accepted = true;
		else
			base.OnContextMenu( e );
	}

	private bool ShowContextMenu( Vector2 screenPosition )
	{
		if ( ContextMenu is null )
			return false;

		var menu = new ContextMenu( this );
		ContextMenu.Invoke( menu );
		menu.OpenAt( screenPosition, false );
		return true;
	}

	protected override void OnMouseRightClick( MouseEvent e )
	{
		base.OnMouseRightClick( e );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		base.OnMouseClick( e );

		if ( e.LeftMouseButton )
		{
			if ( IsCollapsable )
			{
				IsExpanded = !IsExpanded;
				OnExpandChanged();
			}
		}
	}

	protected virtual void OnExpandChanged()
	{

	}
}
