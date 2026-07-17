namespace Editor;

file class StreamerTitleButton : Widget
{
	Project Project { get; init; }

	private const int HorizontalPadding = 8;
	private const int LogoSize = 24;
	private const int LogoTextSpacing = 8;

	public StreamerTitleButton( Project project )
	{
		Project = project;
		FixedHeight = 32;

		Layout = Layout.Row();
		Layout.Margin = new( 8, 0 );
		Layout.Spacing = 0;
		Layout.Alignment = TextFlag.LeftCenter;
	}

	string ButtonText()
	{
		if ( Streamer.IsActive )
		{
			return $"{Streamer.ViewerCount} Viewer{(Streamer.ViewerCount != 1 ? "s" : "")}";
		}
		else
		{
			return "Disconnected";
		}
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();

		if ( Paint.HasMouseOver )
		{
			Paint.SetBrush( Theme.ButtonBackground.Lighten( 0.25f ) );
			Paint.DrawRect( LocalRect );
		}

		var contentRect = LocalRect.Shrink( HorizontalPadding, 0 );
		var package = Project.Package;

		// Adjust text area to not overlap with logo
		var textRect = contentRect;
		textRect.Left = LogoSize + LogoTextSpacing;

		if ( Streamer.IsActive )
		{
			Paint.SetPen( Theme.Green );
			Paint.DrawIcon( contentRect, "live_tv", 18, TextFlag.LeftCenter );

			Paint.ClearBrush();
			Paint.SetPen( Theme.Green );
			Paint.DrawText( textRect, ButtonText(), TextFlag.LeftCenter );
		}
		else
		{
			Paint.SetPen( Theme.TextDisabled );
			Paint.DrawIcon( contentRect, "live_tv", 18, TextFlag.LeftCenter );

			Paint.ClearBrush();
			Paint.SetPen( Theme.Text );
			Paint.DrawText( textRect, ButtonText(), TextFlag.LeftCenter );
		}
	}

	protected override Vector2 SizeHint() => new( 130, 32 );

	protected override void OnMouseClick( MouseEvent e )
	{
		var menu = new Menu( this );

		menu.AddOption( "Loading.." );

		_ = PopulateOptions( menu );

		menu.OpenNextTo( this, WidgetAnchor.BottomEnd );
	}

	async Task PopulateOptions( Menu menu )
	{

		if ( Streamer.IsActive )
		{
			menu.Clear();
			menu.AddOption( $"Disconnect Twitch", "portable_wifi_off", () => EditorUtility.Streaming.Disconnect( "twitch" ) );
			menu.OpenNextTo( this, WidgetAnchor.BottomEnd );
			return;
		}

		var services = await EditorUtility.Streaming.ListServices();

		if ( !menu.IsValid() ) return;
		if ( !this.IsValid() ) return;

		if ( services.Count == 0 )
		{
			menu.Clear();
			menu.AddOption( "Link Twitch Account..", "open_in_browser", async () =>
			{
				var url = await EditorUtility.Streaming.BeginServiceLink( "twitch" );
				EditorUtility.OpenFile( url );
			} );
			menu.OpenNextTo( this, WidgetAnchor.BottomEnd );
			return;
		}

		menu.Clear();

		foreach ( var service in services )
		{
			menu.AddOption( $"Connect {service.Service} ({service.Name})", "cell_tower", () => EditorUtility.Streaming.Connect( service.Service ) );
		}

		menu.OpenNextTo( this, WidgetAnchor.BottomEnd );
	}

	[Event( "editor.titlebar.buttons.build", Priority = -1000 )]
	public static void OnBuildTitleBarButtons( TitleBarButtons titleBarButtons )
	{
		// Don't show streamer button unless we're using the streamer features
		if ( !Project.Current.Config.GetMetaOrDefault( "UsesStreamerFeatures", false ) )
			return;

		titleBarButtons.Add( new StreamerTitleButton( Project.Current ) );
	}

	int ContentHash() => HashCode.Combine( Streamer.ViewerCount, Streamer.IsActive );

	[EditorEvent.Frame]
	public void FrameUpdate()
	{
		if ( SetContentHash( ContentHash, 0.5f ) )
		{
			Update();
			AdjustSize();
		}
	}

}
