using Editor.Assets;

namespace Editor.Inspectors;

public class AssetPreviewWidget : Widget
{
	SceneRenderingWidget renderWidget;
	AssetPreview preview;

	bool initializing;

	public AssetPreviewWidget( AssetPreview p ) : base( null )
	{
		preview = p;

		VerticalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		HorizontalSizeMode = SizeMode.Flexible;

		Layout = Layout.Column();

		initializing = true;
		_ = InitAsync();
	}

	async Task InitAsync()
	{
		await preview.InitializeScene();
		await preview.InitializeAsset();

		for ( int i = 0; i < 4; i++ )
		{
			preview.TickScene( 0.5f );
		}

		using ( preview.Scene.Push() )
		{
			preview.UpdateScene( 0, RealTime.Delta );
		}

		//
		// optional toolbar docked above the content
		//

		if ( preview.CreateToolbar() is { } toolbar )
			Layout.Add( toolbar );

		if ( preview.CreateWidget( this ) is { } widget )
		{
			//
			// use a fully custom widget
			//

			Layout.Add( widget, 1 );
		}
		else if ( preview.Camera is not null )
		{
			//
			// set up rendering
			//

			renderWidget = new SceneRenderingWidget();
			renderWidget.Scene = preview.Scene;

			preview.Camera.BackgroundColor = preview.BackgroundColor;
			renderWidget.OnPreFrame += PreFrame;

			Layout.Add( renderWidget, 1 );
		}

		if ( preview.Camera is null )
		{
			await UpdatePixmap();
		}

		initializing = false;
		Update();
	}

	protected override Vector2 SizeHint() => 400;

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		preview?.Dispose();
		preview = null;
	}


	Pixmap pixmap;

	public async Task UpdatePixmap()
	{
		var w = new Pixmap( Size * DpiScale );
		await preview.RenderToPixmap( w );
		pixmap = w;

		Update();
	}

	protected override void OnPaint()
	{
		if ( initializing )
			return;

		if ( preview.Camera is not null )
			return;

		if ( pixmap is not null )
		{
			Paint.Draw( LocalRect, pixmap );
		}
	}

	void PreFrame()
	{
		if ( initializing || preview?.Camera is null )
			return;

		using ( preview.Scene.Push() )
		{
			preview.ScreenSize = (Vector2Int)renderWidget.Size;
			preview.UpdateScene( RealTime.Now * preview.PreviewWidgetCycleSpeed, RealTime.Delta );
		}
	}
}
