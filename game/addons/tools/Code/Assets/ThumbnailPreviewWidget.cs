using System.Threading;

namespace Editor.Assets;

/// <summary>
/// Widget used for AssetPreviews that toggle between a thumbnail and a live preview.
/// </summary>
class ThumbnailPreviewWidget : Widget, AssetSystem.IEventListener
{
	readonly AssetPreview preview;
	readonly Func<Task> loadFullAssetFunc;
	readonly CancellationTokenSource destroyCts = new();

	Pixmap thumbnail;
	SceneRenderingWidget renderWidget;
	IconButton toggleButton;
	bool liveMode;
	bool assetLoaded;
	bool loading;

	public ThumbnailPreviewWidget( AssetPreview preview, Func<Task> loadFullAssetFunc ) : base( null )
	{
		this.preview = preview;
		this.loadFullAssetFunc = loadFullAssetFunc;

		VerticalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		HorizontalSizeMode = SizeMode.Flexible;
		Layout = Layout.Row();

		thumbnail = preview.Asset.GetAssetThumb();

		// Make sure there is a thumbnail at all if one doesn't exist
		if ( !preview.Asset.HasCachedThumbnail )
		{
			try
			{
				preview.Asset.RebuildThumbnail();
			}
			catch ( System.Exception exception )
			{
				Log.Warning( $"Failed to rebuild thumbnail for asset '{preview.Asset}': {exception}" );
			}
		}

		toggleButton = new IconButton( "3d_rotation", parent: this );
		toggleButton.ToolTip = "Load Live Preview";
		toggleButton.MinimumSize = 24;
		toggleButton.MouseLeftPress = () => _ = ToggleMode();
	}

	async Task ToggleMode()
	{
		if ( loading ) return;

		if ( liveMode )
		{
			SwitchToThumbnail();
		}
		else
		{
			await SwitchToLive();
		}
	}

	void SwitchToThumbnail()
	{
		// Reparent toggle button before destroying the render widget, or the button gets destroyed with it
		toggleButton.Parent = this;

		if ( renderWidget.IsValid() )
		{
			renderWidget.OnPreFrame -= PreFrame;
			renderWidget.Destroy();
			renderWidget = null;
		}

		liveMode = false;
		toggleButton.Icon = "3d_rotation";
		toggleButton.ToolTip = "Load Live Preview";
		toggleButton.Visible = true;

		Update();
	}

	async Task SwitchToLive()
	{
		loading = true;
		toggleButton.Enabled = false;
		toggleButton.Icon = "hourglass_empty";
		toggleButton.ToolTip = "Loading...";

		try
		{
			var token = destroyCts.Token;

			// Only load the asset once, subsequent toggles should just show/hide the render widget
			if ( !assetLoaded )
			{
				await loadFullAssetFunc();

				if ( token.IsCancellationRequested ) return;

				assetLoaded = true;

				for ( int i = 0; i < 4; i++ )
				{
					preview.TickScene( 0.5f );
				}

				var scene = preview.Scene;
				if ( scene is not null )
				{
					using ( scene.Push() )
					{
						preview.UpdateScene( 0, 0.1f );
					}
				}
			}

			if ( token.IsCancellationRequested ) return;

			renderWidget = Layout.Add( new SceneRenderingWidget() );
			renderWidget.Scene = preview.Scene;
			renderWidget.OnPreFrame += PreFrame;

			if ( preview.Camera is not null )
			{
				preview.Camera.BackgroundColor = Theme.ControlBackground;
			}

			// Reparent toggle button onto the render widget
			toggleButton.Parent = renderWidget;
			toggleButton.Icon = "image";
			toggleButton.ToolTip = "Show Thumbnail";

			liveMode = true;
		}
		finally
		{
			loading = false;
			toggleButton.Enabled = true;
		}
	}

	void PreFrame()
	{
		if ( preview?.Camera is null ) return;
		if ( !renderWidget.IsValid() ) return;

		using ( preview.Scene.Push() )
		{
			preview.ScreenSize = (Vector2Int)renderWidget.Size;
			preview.UpdateScene( RealTime.Now * preview.PreviewWidgetCycleSpeed, RealTime.Delta );
		}
	}

	protected override void DoLayout()
	{
		base.DoLayout();

		if ( renderWidget.IsValid() )
		{
			renderWidget.Position = 0;
			renderWidget.Size = Size;
		}

		if ( toggleButton.IsValid() )
		{
			toggleButton.AdjustSize();
			toggleButton.AlignToParent( TextFlag.LeftBottom, 8 );
		}
	}

	protected override void OnPaint()
	{
		if ( liveMode ) return;

		Paint.ClearPen();
		Paint.SetBrush( Theme.ControlBackground );
		Paint.DrawRect( LocalRect );

		if ( thumbnail is null ) return;

		var thumbAspect = (float)thumbnail.Width / thumbnail.Height;
		var rectAspect = LocalRect.Width / LocalRect.Height;

		Vector2 drawSize;
		if ( thumbAspect > rectAspect )
		{
			drawSize = new Vector2( LocalRect.Width, LocalRect.Width / thumbAspect );
		}
		else
		{
			drawSize = new Vector2( LocalRect.Height * thumbAspect, LocalRect.Height );
		}

		var drawPos = (LocalRect.Size - drawSize) * 0.5f;

		// Draw the 256x256 thumbnail with filtering so we don't have to render a higher quality thumbnail just for this.
		Paint.BilinearFiltering = true;
		Paint.Draw( new Rect( drawPos, drawSize ), thumbnail );
		Paint.BilinearFiltering = false;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		destroyCts.Cancel();
		destroyCts.Dispose();

		if ( renderWidget.IsValid() )
		{
			renderWidget.OnPreFrame -= PreFrame;
		}

		thumbnail = null;
	}

	// When the thumbnail is generated or RE-generated, then we update our drawn thumbnail
	void AssetSystem.IEventListener.OnAssetThumbGenerated( Asset asset )
	{
		if ( asset != preview?.Asset ) return;

		thumbnail = asset.GetAssetThumb( generateIfNotInCache: false );
		Update();
	}
}
