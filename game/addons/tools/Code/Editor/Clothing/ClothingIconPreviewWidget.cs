namespace Editor;

class ClothingIconPreviewWidget : Widget
{
	public ClothingScene Scene;

	public Clothing Clothing;

	SceneRenderingWidget CanvasWidget;

	public ClothingIconPreviewWidget() : base( null )
	{
		Scene = new ClothingScene();
		Scene.Update();

		Layout = Layout.Column();

		CanvasWidget = new SceneRenderingWidget( this );
		CanvasWidget.SetSizeMode( SizeMode.CanGrow, SizeMode.CanGrow );

		Layout.Add( CanvasWidget );

		FixedSize = 256;
		SetSizeMode( SizeMode.CanGrow, SizeMode.CanGrow );
	}

	public string IconPath { get; internal set; }

	[EditorEvent.Frame]
	public void OnFrame()
	{
		Scene.Update();
		CanvasWidget.Scene = Scene.Scene;
	}

	public static void RenderIcon( Asset asset, Clothing resource )
	{
		// force an icon path
		var iconInfo = resource.Icon;

		iconInfo.Path = resource.ResourcePath + ".png";
		resource.Icon = iconInfo;

		var clothingSetup = new ClothingScene();
		clothingSetup.Update();
		clothingSetup.InstallClothing( resource );

		clothingSetup.Scene.EditorTick( RealTime.Now - 2, 1 );
		clothingSetup.Scene.EditorTick( RealTime.Now - 1, 1 );

		clothingSetup.Update();

		int size = 512;
		int upscale = 4;

		using var bitmap = new Bitmap( size * upscale, size * upscale );
		clothingSetup.Scene.Camera.RenderToBitmap( bitmap );

		if ( asset.SaveToDisk( resource ) )
		{
			asset.Compile( false );
		}

		var root = asset.AbsolutePath[0..^(asset.RelativePath.Length)];
		var pngPath = root + iconInfo.Path;
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( pngPath ) );

		using var downsampledBitmap = bitmap.Resize( size, size );
		var outputData = downsampledBitmap.ToPng();
		System.IO.File.WriteAllBytes( pngPath, outputData );
	}
}
