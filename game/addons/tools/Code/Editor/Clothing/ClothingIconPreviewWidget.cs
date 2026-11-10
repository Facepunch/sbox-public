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

	/// <summary>
	/// Exports a Human clothing icon, preserving the existing icon when the item has no renderable Human model.
	/// </summary>
	public static void RenderIcon( Asset asset, Clothing resource )
	{
		var clothingSetup = new ClothingScene();
		try
		{
			clothingSetup.Update();
			clothingSetup.InstallClothing( resource );

			if ( !clothingSetup.HasRenderableClothing )
			{
				Log.Warning( $"Cannot render clothing icon for '{resource.ResourcePath}': no Human model is available. Existing icon kept." );
				return;
			}

			var iconInfo = resource.Icon;
			iconInfo.Path = resource.ResourcePath + ".png";
			resource.Icon = iconInfo;

			using var bitmap = ClothingIconRenderer.Render( clothingSetup );

			if ( asset.SaveToDisk( resource ) )
			{
				asset.Compile( false );
			}

			var root = asset.AbsolutePath[0..^(asset.RelativePath.Length)];
			var pngPath = root + iconInfo.Path;
			System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( pngPath ) );

			var outputData = bitmap.ToPng();
			System.IO.File.WriteAllBytes( pngPath, outputData );
		}
		finally
		{
			clothingSetup.Scene.Destroy();
		}
	}
}
