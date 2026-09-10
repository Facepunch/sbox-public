namespace Editor;

internal static class DecalMenu
{
	private const string ColorSuffix = "_color";
	private const string NormalSuffix = "_normal";
	private const string RmaSuffix = "_rma";
	private const string EmissiveSuffix = "_emissive";
	private const string HeightSuffix = "_height";

	[Event( "asset.contextmenu", Priority = 50 )]
	public static void OnDecalFileAssetContext( AssetContextMenu e )
	{
		if ( !e.SelectedList.All( x => x.AssetType == AssetType.ImageFile ) )
			return;

		e.Menu.AddOption( $"Create Decal", "approval", action: () => CreateDecalUsingImageFiles( e.SelectedList ) );
	}

	private static async void CreateDecalUsingImageFiles( IEnumerable<AssetEntry> entries )
	{
		var entryList = entries.ToList();
		var asset = entryList.First().Asset;
		var assetName = asset.Name;

		foreach ( var suffix in new[] { ColorSuffix, NormalSuffix, RmaSuffix, EmissiveSuffix, HeightSuffix } )
		{
			if ( assetName.EndsWith( suffix, StringComparison.OrdinalIgnoreCase ) )
			{
				assetName = assetName.Substring( 0, assetName.Length - suffix.Length );
				break;
			}
		}

		var fd = new FileDialog( null );
		fd.Title = "Create Decal from Image Files..";
		fd.Directory = System.IO.Path.GetDirectoryName( asset.AbsolutePath );
		fd.DefaultSuffix = ".decal";
		fd.SelectFile( $"{assetName}.decal" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Decal File (*.decal)" );

		if ( !fd.Execute() )
			return;

		var resultAsset = AssetSystem.CreateResource( "decal", fd.SelectedFile );
		if ( resultAsset is null )
			return;

		await resultAsset.CompileIfNeededAsync();

		if ( resultAsset.TryLoadResource<DecalDefinition>( out var obj ) )
		{
			AssignTextures( entryList, obj );
			resultAsset.SaveToDisk( obj );
		}

		MainAssetBrowser.Instance?.Local.UpdateAssetList();
		MainAssetBrowser.Instance?.Local.FocusOnAsset( resultAsset );
		EditorUtility.InspectorObject = resultAsset;
	}

	private static void AssignTextures( List<AssetEntry> entries, DecalDefinition obj )
	{
		// if there's just one texture selected, assign it to color input
		if ( entries.Count == 1 )
		{
			obj.ColorTexture = Texture.Load( entries[0].Asset.RelativePath );
			return;
		}

		AssetEntry colorEntry = null;
		var unmatched = new List<AssetEntry>();

		foreach ( var entry in entries )
		{
			var name = entry.Asset.Name;

			if ( name.EndsWith( NormalSuffix, StringComparison.OrdinalIgnoreCase ) )
				obj.NormalTexture ??= Texture.Load( entry.Asset.RelativePath );
			else if ( name.EndsWith( RmaSuffix, StringComparison.OrdinalIgnoreCase ) )
				obj.RoughMetalOcclusionTexture ??= Texture.Load( entry.Asset.RelativePath );
			else if ( name.EndsWith( EmissiveSuffix, StringComparison.OrdinalIgnoreCase ) )
				obj.EmissiveTexture ??= Texture.Load( entry.Asset.RelativePath );
			else if ( name.EndsWith( HeightSuffix, StringComparison.OrdinalIgnoreCase ) )
				obj.HeightTexture ??= Texture.Load( entry.Asset.RelativePath );
			else if ( name.EndsWith( ColorSuffix, StringComparison.OrdinalIgnoreCase ) )
				colorEntry ??= entry;
			else
				unmatched.Add( entry );
		}

		// mismatched textures will be assigned to color input if no color texture was found,
		// if ALL textures are mismatched, then first one in the selection will be assigned to color
		colorEntry ??= unmatched.FirstOrDefault() ?? entries.FirstOrDefault();
		if ( colorEntry != null )
		{
			obj.ColorTexture = Texture.Load( colorEntry.Asset.RelativePath );
			unmatched.Remove( colorEntry );
		}

		if ( unmatched.Count >= 1 )
		{
			ShowSkippedTexturesPopup( unmatched.Select( x => x.Asset.Name ) );
		}
	}

	private static void ShowSkippedTexturesPopup( IEnumerable<string> names )
	{
		var popup = new PopupDialogWidget();

		var message = $"The following textures didn't match any supported texture inputs and were skipped: \n\n{string.Join( "\n", names )}";
		message += $"\n\nSupported input file suffixes are: {ColorSuffix}, {NormalSuffix}, {RmaSuffix}, {HeightSuffix}, {EmissiveSuffix}";

		popup.FixedWidth = 700;
		popup.WindowTitle = "Create Decal";
		popup.MessageLabel.Text = message;

		popup.ButtonLayout.AddStretchCell();
		popup.ButtonLayout.Add( new Button( "Ok" ) { Clicked = popup.Destroy } );

		popup.SetModal( true, true );
		popup.Show();
	}
}
